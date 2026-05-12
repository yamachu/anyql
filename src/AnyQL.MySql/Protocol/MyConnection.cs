using System.Buffers.Binary;
using System.Text;
using AnyQL.Core.Models;
using AnyQL.Core.Transport;

namespace AnyQL.MySql.Protocol;

/// <summary>
/// Manages a single MySQL connection lifecycle:
///   Connect → Handshake → Auth → COM_STMT_PREPARE → COM_STMT_CLOSE → COM_QUIT
/// </summary>
internal sealed class MyConnection(ISocketTransport transport) : IAsyncDisposable
{
    private readonly ISocketTransport _transport = transport;
    private MyStream? _stream;
    private bool _ready;

    // ── Open ─────────────────────────────────────────────────────────────────

    public async Task OpenAsync(ConnectionInfo conn, CancellationToken ct)
    {
        await _transport.ConnectAsync(conn.Host, conn.Port, ct).ConfigureAwait(false);
        _stream = new MyStream(_transport);

        await HandshakeAsync(conn.User, conn.Password, conn.Database, ct).ConfigureAwait(false);
        _ready = true;
    }

    // ── COM_STMT_PREPARE ─────────────────────────────────────────────────────

    /// <summary>
    /// Sends COM_STMT_PREPARE and reads back the statement metadata:
    /// column definitions and parameter definitions.
    /// </summary>
    public async Task<(MyColumnDef[] paramDefs, MyColumnDef[] colDefs)> PrepareAsync(
        string sql, CancellationToken ct)
    {
        EnsureReady();
        var stream = _stream!;
        stream.ResetSequence();

        byte[] sqlBytes = Encoding.UTF8.GetBytes(sql);
        byte[] payload = new byte[1 + sqlBytes.Length];
        payload[0] = 0x16; // COM_STMT_PREPARE
        sqlBytes.CopyTo(payload, 1);
        await stream.WritePacketAsync(payload, ct).ConfigureAwait(false);

        // Read OK packet
        byte[] okPacket = await stream.ReadPacketAsync(ct).ConfigureAwait(false);
        if (okPacket[0] == 0xFF)
            throw new MySqlException(ParseErrorPacket(okPacket));
        if (okPacket[0] != 0x00)
            throw new MySqlException($"COM_STMT_PREPARE: unexpected status byte 0x{okPacket[0]:X2}.");

        int offset = 1;
        uint stmtId = MyStream.ReadUInt32(okPacket, ref offset);
        ushort numCols = MyStream.ReadUInt16(okPacket, ref offset);
        ushort numParams = MyStream.ReadUInt16(okPacket, ref offset);
        // skip reserved byte + warning count
        offset += 3;

        var paramDefs = await ReadColumnDefsAsync(numParams, stream, ct).ConfigureAwait(false);
        var colDefs = await ReadColumnDefsAsync(numCols, stream, ct).ConfigureAwait(false);

        // Close the prepared statement to free server resources
        await SendStmtCloseAsync(stmtId, ct).ConfigureAwait(false);

        return (paramDefs, colDefs);
    }

    // ── Quit ─────────────────────────────────────────────────────────────────

    public async Task CloseAsync(CancellationToken ct)
    {
        if (_stream is not null && _ready)
        {
            try
            {
                _stream.ResetSequence();
                await _stream.WritePacketAsync(new byte[] { 0x01 }, ct).ConfigureAwait(false); // COM_QUIT
            }
            catch { /* best-effort */ }
        }
        await _transport.CloseAsync(ct).ConfigureAwait(false);
        _ready = false;
    }

    public async ValueTask DisposeAsync() => await CloseAsync(CancellationToken.None).ConfigureAwait(false);

    // ── Private: Handshake ────────────────────────────────────────────────────

    private async Task HandshakeAsync(string user, string password, string database, CancellationToken ct)
    {
        var stream = _stream!;

        // Read server handshake
        byte[] handshake = await stream.ReadPacketAsync(ct).ConfigureAwait(false);
        if (handshake[0] == 0xFF)
            throw new MySqlException("Server sent error during handshake.");

        int off = 1; // skip protocol version
        string serverVersion = MyStream.ReadNullTerminatedString(handshake, ref off);
        uint connId = MyStream.ReadUInt32(handshake, ref off);

        // Scramble part 1 (8 bytes) + filler
        byte[] scramble = new byte[20];
        Array.Copy(handshake, off, scramble, 0, 8);
        off += 8 + 1; // 8 bytes + filler

        uint capLow = MyStream.ReadUInt16(handshake, ref off);
        byte charset = MyStream.ReadUInt8(handshake, ref off);
        ushort statusFlags = MyStream.ReadUInt16(handshake, ref off);
        uint capHigh = MyStream.ReadUInt16(handshake, ref off);
        byte authDataLen = MyStream.ReadUInt8(handshake, ref off);
        off += 10; // reserved

        int scramble2Len = Math.Max(13, authDataLen - 8);
        Array.Copy(handshake, off, scramble, 8, scramble2Len - 1); // last byte is \0
        off += scramble2Len;

        string authPlugin = MyStream.ReadNullTerminatedString(handshake, ref off);

        // Build HandshakeResponse41
        uint capabilities =
            MySqlCapabilities.Protocol41 |
            MySqlCapabilities.SecureConnection |
            MySqlCapabilities.PluginAuth |
            MySqlCapabilities.LongPassword |
            MySqlCapabilities.LongFlag |
            MySqlCapabilities.Transactions |
            MySqlCapabilities.ConnectWithDb;

        byte[] authResponse = authPlugin.Contains("caching_sha2")
            ? MySqlCachingSha2Auth.ComputeFastAuthResponse(password, scramble)
            : ComputeMysqlNativePassword(password, scramble);

        var resp = BuildHandshakeResponse(capabilities, user, authResponse, database,
            authPlugin.Contains("caching_sha2") ? "caching_sha2_password" : "mysql_native_password");

        // _seq is already 1 (set by ReadPacketAsync after reading server greeting seq=0);
        // do NOT reset here — MySQL 8.4+ strictly validates sequence numbers.
        await stream.WritePacketAsync(resp, ct).ConfigureAwait(false);

        // Read auth result
        byte[] authResult = await stream.ReadPacketAsync(ct).ConfigureAwait(false);
        switch (authResult[0])
        {
            case 0x00:
                return; // OK — direct success (mysql_native_password etc.)

            case 0x01 when authResult.Length > 1 && authResult[1] == 0x03:
                // caching_sha2_password: fast auth OK (cached credentials verified)
                // Server sends one more OK packet
                byte[] fastOk = await stream.ReadPacketAsync(ct).ConfigureAwait(false);
                if (fastOk[0] != 0x00)
                    throw new MySqlException(ParseErrorPacket(fastOk));
                return;

            case 0x01 when authResult.Length > 1 && authResult[1] == 0x04:
                // Full auth required: send cleartext password (sequence continues from handshake)
                byte[] cleartext = MySqlCachingSha2Auth.CleartextPassword(password);
                await stream.WritePacketAsync(cleartext, ct).ConfigureAwait(false);
                byte[] finalResult = await stream.ReadPacketAsync(ct).ConfigureAwait(false);
                if (finalResult[0] != 0x00)
                    throw new MySqlException(ParseErrorPacket(finalResult));
                return;

            case 0xFE:
                throw new MySqlException("Auth switch request not supported in MVP.");

            case 0xFF:
                throw new MySqlException(ParseErrorPacket(authResult));

            default:
                throw new MySqlException($"Unexpected auth result byte: 0x{authResult[0]:X2}.");
        }
    }

    // ── Private: helpers ──────────────────────────────────────────────────────

    private static async Task<MyColumnDef[]> ReadColumnDefsAsync(int count, MyStream stream, CancellationToken ct)
    {
        if (count == 0) return [];
        var defs = new MyColumnDef[count];
        for (int i = 0; i < count; i++)
        {
            byte[] pkt = await stream.ReadPacketAsync(ct).ConfigureAwait(false);
            defs[i] = ParseColumnDef(pkt);
        }
        // EOF / OK packet after column defs
        await stream.ReadPacketAsync(ct).ConfigureAwait(false);
        return defs;
    }

    private static MyColumnDef ParseColumnDef(byte[] pkt)
    {
        int off = 0;
        string catalog = MyStream.ReadLengthEncodedString(pkt, ref off);
        string schema = MyStream.ReadLengthEncodedString(pkt, ref off);
        string table = MyStream.ReadLengthEncodedString(pkt, ref off);
        string orgTable = MyStream.ReadLengthEncodedString(pkt, ref off);
        string name = MyStream.ReadLengthEncodedString(pkt, ref off);
        string orgName = MyStream.ReadLengthEncodedString(pkt, ref off);
        off++; // 0x0C fixed length
        ushort charSet = MyStream.ReadUInt16(pkt, ref off);
        uint columnLength = MyStream.ReadUInt32(pkt, ref off);
        byte columnType = MyStream.ReadUInt8(pkt, ref off);
        ushort flags = MyStream.ReadUInt16(pkt, ref off);
        byte decimals = MyStream.ReadUInt8(pkt, ref off);
        return new MyColumnDef(name, orgName, table, orgTable, columnType, flags, charSet, columnLength, decimals);
    }

    private async Task SendStmtCloseAsync(uint stmtId, CancellationToken ct)
    {
        _stream!.ResetSequence();
        byte[] payload = new byte[5];
        payload[0] = 0x19; // COM_STMT_CLOSE
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(1), stmtId);
        await _stream.WritePacketAsync(payload, ct).ConfigureAwait(false);
        // COM_STMT_CLOSE has no response
    }

    private static byte[] BuildHandshakeResponse(uint capabilities, string user, byte[] authResponse,
        string database, string authPlugin)
    {
        var buf = new List<byte>();
        void AddUInt32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); buf.AddRange(b); }
        void AddCStr(string s) { buf.AddRange(Encoding.UTF8.GetBytes(s)); buf.Add(0); }

        AddUInt32(capabilities);
        AddUInt32(0x1000000); // max packet size
        buf.Add(MySqlCapabilities.Utf8Mb4Charset);
        buf.AddRange(new byte[23]); // reserved
        AddCStr(user);
        buf.Add((byte)authResponse.Length);
        buf.AddRange(authResponse);
        AddCStr(database);
        AddCStr(authPlugin);
        return buf.ToArray();
    }

    private static byte[] ComputeMysqlNativePassword(string password, byte[] scramble)
    {
        if (string.IsNullOrEmpty(password)) return [];
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        byte[] pwBytes = Encoding.UTF8.GetBytes(password);
        byte[] hash1 = sha1.ComputeHash(pwBytes);
        byte[] hash2 = sha1.ComputeHash(hash1);
        byte[] combined = [.. scramble, .. hash2];
        byte[] hash3 = sha1.ComputeHash(combined);
        byte[] result = new byte[hash1.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = (byte)(hash1[i] ^ hash3[i]);
        return result;
    }

    private static string ParseErrorPacket(byte[] pkt)
    {
        if (pkt.Length < 3) return "Unknown MySQL error.";
        ushort code = BinaryPrimitives.ReadUInt16LittleEndian(pkt.AsSpan(1));
        string msg = pkt.Length > 3 ? Encoding.UTF8.GetString(pkt, 3, pkt.Length - 3) : string.Empty;
        // Skip SQL state marker '#' + 5-char state if present
        if (msg.StartsWith('#')) msg = msg.Length > 6 ? msg[6..] : msg;
        return $"MySQL error {code}: {msg.Trim()}";
    }

    private void EnsureReady()
    {
        if (!_ready)
            throw new InvalidOperationException("MySQL connection is not open.");
    }
}

internal readonly record struct MyColumnDef(
    string Name,
    string OrgName,
    string Table,
    string OrgTable,
    byte ColumnType,
    ushort Flags,
    ushort CharSet,
    uint ColumnLength,
    byte Decimals);
