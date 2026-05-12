using System.Buffers.Binary;
using System.Text;
using AnyQL.Core.Models;
using AnyQL.Core.Transport;
using AnyQL.Core.TypeMapping;

namespace AnyQL.Postgres.Protocol;

/// <summary>
/// Manages a single PostgreSQL connection lifecycle:
///   Connect → Startup → Auth → (Parse + Describe + Sync) → Terminate
/// </summary>
internal sealed class PgConnection(ISocketTransport transport) : IAsyncDisposable
{
    private readonly ISocketTransport _transport = transport;
    private PgStream? _stream;
    private bool _ready;

    // ── Connect / Startup ────────────────────────────────────────────────────

    public async Task OpenAsync(ConnectionInfo conn, CancellationToken ct)
    {
        await _transport.ConnectAsync(conn.Host, conn.Port, ct).ConfigureAwait(false);
        _stream = new PgStream(_transport);

        await SendStartupAsync(conn.User, conn.Database, ct).ConfigureAwait(false);
        await AuthenticateAsync(conn.User, conn.Password, ct).ConfigureAwait(false);
        // Drain ParameterStatus + BackendKeyData until ReadyForQuery
        await DrainUntilReadyAsync(ct).ConfigureAwait(false);
        _ready = true;
    }

    // ── Parse + Describe ─────────────────────────────────────────────────────

    /// <summary>
    /// Sends Parse (unnamed) + Describe (statement) + Sync, then reads
    /// ParameterDescription and RowDescription messages.
    /// </summary>
    public async Task<(uint[] paramOids, PgRowDescriptionField[] columns)> DescribeAsync(
        string sql, CancellationToken ct)
    {
        EnsureReady();
        var stream = _stream!;

        // Parse message: 'P' + len + stmt_name(cstr) + query(cstr) + numParams(int16) + [paramOids]
        byte[] stmtName = [0]; // unnamed statement
        byte[] queryCStr = PgStream.BuildCString(sql);
        byte[] parsePayload = [.. stmtName, .. queryCStr, 0, 0]; // numParams = 0 (let server infer)
        await stream.WriteMessageAsync(PgMessageType.Parse, parsePayload, ct).ConfigureAwait(false);

        // Describe message: 'D' + len + 'S'(statement) + stmt_name(cstr)
        byte[] describePayload = [(byte)'S', .. stmtName];
        await stream.WriteMessageAsync(PgMessageType.Describe, describePayload, ct).ConfigureAwait(false);

        // Sync: no payload
        await stream.WriteMessageAsync(PgMessageType.Sync, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false);

        // Read responses
        uint[]? paramOids = null;
        PgRowDescriptionField[]? rowDesc = null;

        while (true)
        {
            var (type, body) = await stream.ReadMessageAsync(ct).ConfigureAwait(false);
            switch (type)
            {
                case PgMessageType.ParseComplete:
                    break;

                case PgMessageType.ParameterDescription:
                    paramOids = ParseParameterDescription(body);
                    break;

                case PgMessageType.RowDescription:
                    rowDesc = ParseRowDescription(body);
                    break;

                case PgMessageType.NoData:
                    rowDesc ??= [];
                    break;

                case PgMessageType.ReadyForQuery:
                    goto done;

                case PgMessageType.ErrorResponse:
                    throw new PgException(ParseErrorResponse(body));

                case PgMessageType.NoticeResponse:
                    break; // ignore notices

                default:
                    break;
            }
        }
    done:
        return (paramOids ?? [], rowDesc ?? []);
    }

    /// <summary>
    /// Queries pg_class to resolve unqualified table names to their OIDs.
    /// Returns a dictionary of relname → oid (case-insensitive match).
    /// </summary>
    public async Task<Dictionary<string, uint>> QueryTableOidsAsync(
        IEnumerable<string> tableNames, CancellationToken ct)
    {
        var names = tableNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0) return [];

        // Table names come from regex (\w+) so they contain only word chars;
        // we still escape single quotes to be safe.
        var inClause = string.Join(",", names.Select(n => $"'{n.Replace("'", "''")}'"));
        string sql = $"SELECT relname, oid::bigint FROM pg_class " +
                     $"WHERE relname IN ({inClause}) AND relkind = 'r'";

        var rows = await SimpleQueryAsync(sql, ct).ConfigureAwait(false);
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            string relname = row[0]!;
            uint oid = (uint)long.Parse(row[1]!);
            result[relname] = oid;
        }
        return result;
    }

    /// <summary>
    /// Queries pg_attribute to determine attnotnull for a batch of (tableOid, attrNum) pairs.
    /// Returns a dictionary keyed by (tableOid, attrNum) → notNull.
    /// </summary>
    public async Task<Dictionary<(uint tableOid, short attrNum), bool>> QueryNotNullAsync(
        IEnumerable<(uint tableOid, short attrNum)> attrs, CancellationToken ct)
    {
        var pairs = attrs.Where(a => a.tableOid != 0).Distinct().ToList();
        if (pairs.Count == 0) return [];

        // Build: SELECT attrelid, attnum, attnotnull FROM pg_attribute
        //        WHERE (attrelid, attnum) IN ((oid1,n1),(oid2,n2),...)
        var inClause = string.Join(",", pairs.Select(p => $"({p.tableOid},{p.attrNum})"));
        string sql = $"SELECT attrelid::bigint, attnum, attnotnull FROM pg_attribute " +
                     $"WHERE (attrelid, attnum) IN ({inClause})";

        var rows = await SimpleQueryAsync(sql, ct).ConfigureAwait(false);
        var result = new Dictionary<(uint, short), bool>();
        foreach (var row in rows)
        {
            uint tableOid = (uint)long.Parse(row[0]!);
            short attrNum = short.Parse(row[1]!);
            bool notNull = row[2] == "t";
            result[(tableOid, attrNum)] = notNull;
        }
        return result;
    }

    /// <summary>
    /// Queries pg_type for a set of OIDs not present in the static type map.
    /// Returns a dictionary keyed by OID.
    /// </summary>
    public async Task<Dictionary<uint, DynamicTypeInfo>> QueryUnknownOidsAsync(
        IEnumerable<uint> oids, CancellationToken ct)
    {
        var list = oids.Distinct().ToList();
        if (list.Count == 0) return [];

        string inClause = string.Join(",", list);
        // typtype: 'e'=enum, 'c'=composite, 'd'=domain, 'r'=range, 'b'=base
        // typelem: for array types, OID of the element type
        string sql = $"""
            SELECT t.oid::bigint, t.typname, t.typtype, t.typelem::bigint, n.nspname
            FROM pg_type t
            JOIN pg_namespace n ON n.oid = t.typnamespace
            WHERE t.oid IN ({inClause})
            """;

        var rows = await SimpleQueryAsync(sql, ct).ConfigureAwait(false);
        var result = new Dictionary<uint, DynamicTypeInfo>();
        foreach (var row in rows)
        {
            uint oid = (uint)long.Parse(row[0]!);
            string name = row[1]!;
            char typType = row[2]![0];
            uint elemOid = (uint)long.Parse(row[3]!);
            string ns = row[4]!;
            result[oid] = new DynamicTypeInfo
            {
                TypeName = name,
                TypeType = typType,
                ElemOid = elemOid,
                Namespace = ns,
            };
        }
        return result;
    }

    // ── Terminate ────────────────────────────────────────────────────────────

    public async Task CloseAsync(CancellationToken ct)
    {
        if (_stream is not null && _ready)
        {
            try
            {
                await _stream.WriteMessageAsync(PgMessageType.Terminate, ReadOnlyMemory<byte>.Empty, ct)
                    .ConfigureAwait(false);
            }
            catch { /* best-effort */ }
        }
        await _transport.CloseAsync(ct).ConfigureAwait(false);
        _ready = false;
    }

    public async ValueTask DisposeAsync() => await CloseAsync(CancellationToken.None).ConfigureAwait(false);

    // ── Private: Startup ─────────────────────────────────────────────────────

    private async Task SendStartupAsync(string user, string database, CancellationToken ct)
    {
        // Protocol version 3.0 = 196608 (0x00030000)
        const int protocolVersion = 196608;
        var builder = new List<byte>();
        void AppendInt32(int v)
        {
            builder.AddRange(BitConverter.IsLittleEndian
            ? BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(v))
            : BitConverter.GetBytes(v));
        }
        void AppendCStr(string s) { builder.AddRange(Encoding.UTF8.GetBytes(s)); builder.Add(0); }

        AppendInt32(protocolVersion);
        AppendCStr("user"); AppendCStr(user);
        AppendCStr("database"); AppendCStr(database);
        AppendCStr("application_name"); AppendCStr("anyql");
        builder.Add(0); // trailing null

        await _stream!.WriteStartupAsync(builder.ToArray(), ct).ConfigureAwait(false);
    }

    // ── Private: Auth ────────────────────────────────────────────────────────

    private async Task AuthenticateAsync(string user, string password, CancellationToken ct)
    {
        var stream = _stream!;
        PgScramSha256Auth? scram = null;
        byte[]? serverFirstBytes = null;

        while (true)
        {
            var (type, body) = await stream.ReadMessageAsync(ct).ConfigureAwait(false);
            if (type != PgMessageType.Authentication)
                throw new PgException($"Expected Authentication message, got '{(char)type}'.");

            int offset = 0;
            int authType = PgStream.ReadInt32(body, ref offset);
            switch (authType)
            {
                case PgMessageType.AuthOk:
                    return;

                case PgMessageType.AuthMd5Password:
                    {
                        byte[] salt = body[offset..(offset + 4)];
                        string hash = PgMd5Auth.Compute(password, user, salt);
                        byte[] payload = PgStream.BuildCString(hash);
                        await stream.WriteMessageAsync(PgMessageType.PasswordMessage, payload, ct)
                            .ConfigureAwait(false);
                        break;
                    }

                case PgMessageType.AuthSASL:
                    {
                        // Server lists mechanisms. We only support SCRAM-SHA-256.
                        scram = new PgScramSha256Auth(user, password);
                        var (mechanism, initial) = scram.BuildClientFirstMessage();

                        // SASLInitialResponse: mechanism(cstr) + int32(len) + data
                        var mechBytes = PgStream.BuildCString(mechanism);
                        byte[] lenBytes = new byte[4];
                        BinaryPrimitives.WriteInt32BigEndian(lenBytes, initial.Length);
                        byte[] saslPayload = [.. mechBytes, .. lenBytes, .. initial];
                        await stream.WriteMessageAsync(PgMessageType.PasswordMessage, saslPayload, ct)
                            .ConfigureAwait(false);
                        break;
                    }

                case PgMessageType.AuthSASLContinue:
                    {
                        if (scram is null) throw new PgException("Unexpected SASL continue without prior SASL init.");
                        serverFirstBytes = body[offset..];
                        byte[] clientFinal = scram.BuildClientFinalMessage(serverFirstBytes);
                        await stream.WriteMessageAsync(PgMessageType.PasswordMessage, clientFinal, ct)
                            .ConfigureAwait(false);
                        break;
                    }

                case PgMessageType.AuthSASLFinal:
                    {
                        if (scram is null || serverFirstBytes is null)
                            throw new PgException("Unexpected SASL final.");
                        scram.ValidateServerFinal(body[offset..], serverFirstBytes);
                        break;
                    }

                default:
                    throw new PgException($"Unsupported authentication type: {authType}.");
            }
        }
    }

    // ── Private: Message parsers ─────────────────────────────────────────────

    private static uint[] ParseParameterDescription(byte[] body)
    {
        int offset = 0;
        short count = PgStream.ReadInt16(body, ref offset);
        var oids = new uint[count];
        for (int i = 0; i < count; i++)
            oids[i] = PgStream.ReadUInt32(body, ref offset);
        return oids;
    }

    private static PgRowDescriptionField[] ParseRowDescription(byte[] body)
    {
        int offset = 0;
        short count = PgStream.ReadInt16(body, ref offset);
        var fields = new PgRowDescriptionField[count];
        for (int i = 0; i < count; i++)
        {
            string name = PgStream.ReadCString(body, ref offset);
            uint tableOid = PgStream.ReadUInt32(body, ref offset);
            short attrNum = PgStream.ReadInt16(body, ref offset);
            uint typeOid = PgStream.ReadUInt32(body, ref offset);
            short typeSize = PgStream.ReadInt16(body, ref offset);
            int typeMod = PgStream.ReadInt32(body, ref offset);
            short format = PgStream.ReadInt16(body, ref offset); // 0=text,1=binary
            fields[i] = new PgRowDescriptionField(name, tableOid, attrNum, typeOid, typeSize, typeMod, format);
        }
        return fields;
    }

    private static string ParseErrorResponse(byte[] body)
    {
        int offset = 0;
        var parts = new List<string>();
        while (offset < body.Length)
        {
            byte code = body[offset++];
            if (code == 0) break;
            string value = PgStream.ReadCString(body, ref offset);
            if (code == (byte)'M') parts.Insert(0, value); // Message field first
            else parts.Add($"{(char)code}={value}");
        }
        return string.Join("; ", parts);
    }

    private async Task DrainUntilReadyAsync(CancellationToken ct)
    {
        var stream = _stream!;
        while (true)
        {
            var (type, _) = await stream.ReadMessageAsync(ct).ConfigureAwait(false);
            if (type == PgMessageType.ReadyForQuery) return;
            if (type == PgMessageType.ErrorResponse) throw new PgException("Error during startup.");
        }
    }

    /// <summary>Simple Query protocol — for internal pg_attribute queries only.</summary>
    private async Task<List<string?[]>> SimpleQueryAsync(string sql, CancellationToken ct)
    {
        var stream = _stream!;
        // Send 'Q' message
        byte[] payload = PgStream.BuildCString(sql);
        await stream.WriteMessageAsync((byte)'Q', payload, ct).ConfigureAwait(false);

        var rows = new List<string?[]>();
        int? colCount = null;

        while (true)
        {
            var (type, body) = await stream.ReadMessageAsync(ct).ConfigureAwait(false);
            switch (type)
            {
                case PgMessageType.RowDescription:
                    {
                        int offset = 0;
                        colCount = PgStream.ReadInt16(body, ref offset);
                        break;
                    }
                case PgMessageType.DataRow:
                    {
                        int offset = 0;
                        short cols = PgStream.ReadInt16(body, ref offset);
                        var row = new string?[cols];
                        for (int i = 0; i < cols; i++)
                        {
                            int len = PgStream.ReadInt32(body, ref offset);
                            if (len == -1) { row[i] = null; continue; }
                            row[i] = Encoding.UTF8.GetString(body, offset, len);
                            offset += len;
                        }
                        rows.Add(row);
                        break;
                    }
                case PgMessageType.CommandComplete:
                case PgMessageType.EmptyQueryResponse:
                    break;
                case PgMessageType.ReadyForQuery:
                    return rows;
                case PgMessageType.ErrorResponse:
                    throw new PgException(ParseErrorResponse(body));
            }
        }
    }

    private void EnsureReady()
    {
        if (!_ready)
            throw new InvalidOperationException("PostgreSQL connection is not open.");
    }
}

internal readonly record struct PgRowDescriptionField(
    string Name,
    uint TableOid,
    short AttributeNumber,
    uint TypeOid,
    short TypeSize,
    int TypeModifier,
    short Format);
