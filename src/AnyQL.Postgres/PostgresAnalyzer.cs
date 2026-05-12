using AnyQL.Core.Models;
using AnyQL.Core.Transport;
using AnyQL.Core.TypeMapping;
using AnyQL.Postgres.Protocol;

namespace AnyQL.Postgres;

/// <summary>
/// Analyzes a SQL query against a live PostgreSQL database and returns
/// column metadata and parameter type information.
/// </summary>
public static class PostgresAnalyzer
{
    /// <summary>
    /// Connects to PostgreSQL, issues Parse + Describe, and returns an <see cref="AnalyzeResult"/>.
    /// </summary>
    /// <param name="sql">The SQL statement to analyze (SELECT or DML with RETURNING).</param>
    /// <param name="conn">Connection information. Must have Dialect == PostgreSql.</param>
    /// <param name="transport">Socket transport to use. Caller is responsible for disposal.</param>
    /// <param name="options">Options controlling caching behaviour. Defaults to <see cref="AnalyzeOptions.Default"/>.</param>
    /// <param name="cache">Schema cache instance. Defaults to <see cref="PgSchemaCache.Default"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<AnalyzeResult> AnalyzeAsync(
        string sql,
        ConnectionInfo conn,
        ISocketTransport transport,
        AnalyzeOptions? options = null,
        PgSchemaCache? cache = null,
        CancellationToken ct = default)
    {
        if (conn.Dialect != DbDialect.PostgreSql)
            throw new ArgumentException("ConnectionInfo.Dialect must be PostgreSql.", nameof(conn));

        options ??= AnalyzeOptions.Default;
        cache ??= PgSchemaCache.Default;

        await using var pg = new PgConnection(transport);
        await pg.OpenAsync(conn, ct).ConfigureAwait(false);

        var (paramOids, rowFields) = await pg.DescribeAsync(sql, ct).ConfigureAwait(false);

        // ── Collect all OIDs we need to resolve ─────────────────────────────
        var allOids = rowFields.Select(f => f.TypeOid)
            .Concat(paramOids)
            .Where(oid => oid != 0)
            .Distinct()
            .ToHashSet();

        var unknownOids = allOids.Where(oid => PostgresTypeMap.GetTypeName(oid) == "unknown").ToList();

        // ── Resolve dynamic types (with cache) ──────────────────────────────
        var dynamicTypeMap = await ResolveDynamicTypesAsync(
            pg, conn, unknownOids, options, cache, ct).ConfigureAwait(false);

        // ── Resolve nullability (with cache) ────────────────────────────────
        var attrsToQuery = rowFields
            .Where(f => f.TableOid != 0)
            .Select(f => (f.TableOid, f.AttributeNumber));

        var notNullMap = await ResolveNotNullAsync(
            pg, conn, attrsToQuery, options, cache, ct).ConfigureAwait(false);

        // ── Resolve JOIN-side nullability ────────────────────────────────────
        // Columns from the nullable side of a LEFT/RIGHT/FULL JOIN may be NULL
        // even if pg_attribute.attnotnull = true. Resolve which OIDs are "nullable-side".
        var joinInfo = SqlJoinAnalyzer.Analyze(sql);
        HashSet<uint>? leftJoinNullableOids = null;
        if (joinInfo.HasOuterJoins && !joinInfo.AllNullable && joinInfo.NullableTableNames!.Count > 0)
        {
            var tableOidMap = await pg.QueryTableOidsAsync(joinInfo.NullableTableNames, ct)
                                      .ConfigureAwait(false);
            leftJoinNullableOids = new HashSet<uint>(tableOidMap.Values);
        }

        // ── Build ColumnInfo list ────────────────────────────────────────────
        var columns = rowFields.Select(f =>
        {
            var (dotNet, ts, typeName) = ResolveTypeInfo(f.TypeOid, dynamicTypeMap);
            bool? isNullable = notNullMap.TryGetValue((f.TableOid, f.AttributeNumber), out bool notNull)
                ? !notNull
                : null;

            // Override: RIGHT/FULL JOIN → nullability is indeterminate
            if (joinInfo.AllNullable && f.TableOid != 0)
                isNullable = null;
            // Override: LEFT JOIN right-side → force nullable = true
            else if (leftJoinNullableOids != null && leftJoinNullableOids.Contains(f.TableOid))
                isNullable = true;

            return new ColumnInfo
            {
                Name = f.Name,
                DbTypeName = typeName,
                TypeCode = f.TypeOid,
                IsNullable = isNullable,
                DotNetType = dotNet,
                TsType = ts,
                SourceTableOid = f.TableOid,
                SourceColumnAttributeNumber = f.AttributeNumber,
            };
        }).ToList();

        // ── Build ParameterInfo list ─────────────────────────────────────────
        var explicitCasts = SqlCastDetector.FindExplicitlyCastParameters(sql);

        var parameters = paramOids.Select((oid, idx) =>
        {
            int oneBasedIndex = idx + 1;
            var (dotNet, ts, typeName) = ResolveTypeInfo(oid, dynamicTypeMap);
            bool hasCast = explicitCasts.Contains(oneBasedIndex);
            bool requiresCast = !PostgresTypeMap.IsStringCompatible(oid)
                                && !IsStringCompatibleDynamic(oid, dynamicTypeMap)
                                && !hasCast;

            return new ParameterInfo
            {
                Index = oneBasedIndex,
                DbTypeName = typeName,
                TypeCode = oid,
                TsType = ts,
                DotNetType = dotNet,
                RequiresCast = requiresCast,
            };
        }).ToList();

        return new AnalyzeResult
        {
            Columns = columns,
            Parameters = parameters,
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (string dotNet, string ts, string typeName) ResolveTypeInfo(
        uint oid, IReadOnlyDictionary<uint, DynamicTypeInfo> dynamicMap)
    {
        if (PostgresTypeMap.GetTypeName(oid) != "unknown")
        {
            var (dotNet, ts) = PostgresTypeMap.Resolve(oid);
            return (dotNet, ts, PostgresTypeMap.GetTypeName(oid));
        }

        if (dynamicMap.TryGetValue(oid, out var dyn))
        {
            DynamicTypeInfo? elem = dyn.ElemOid != 0 && dynamicMap.TryGetValue(dyn.ElemOid, out var e) ? e : null;
            var (dotNet, ts) = DynamicTypeResolver.Resolve(dyn, elem);
            return (dotNet, ts, dyn.TypeName);
        }

        return ("string", "string", "unknown");
    }

    /// <summary>
    /// Enum types can accept a plain string literal in PG without an explicit cast.
    /// </summary>
    private static bool IsStringCompatibleDynamic(
        uint oid, IReadOnlyDictionary<uint, DynamicTypeInfo> dynamicMap)
    {
        return dynamicMap.TryGetValue(oid, out var dyn) && dyn.TypeType == 'e';
    }

    private static async Task<IReadOnlyDictionary<uint, DynamicTypeInfo>> ResolveDynamicTypesAsync(
        PgConnection pg,
        ConnectionInfo conn,
        IReadOnlyList<uint> unknownOids,
        AnalyzeOptions options,
        PgSchemaCache cache,
        CancellationToken ct)
    {
        if (unknownOids.Count == 0)
            return new Dictionary<uint, DynamicTypeInfo>();

        var cached = cache.GetDynamicTypeMap(conn, options);
        var stillUnknown = cached is null
            ? unknownOids.ToList()
            : unknownOids.Where(oid => !cached.ContainsKey(oid)).ToList();

        if (stillUnknown.Count > 0)
        {
            var fetched = await pg.QueryUnknownOidsAsync(stillUnknown, ct).ConfigureAwait(false);
            if (fetched.Count > 0)
            {
                cache.MergeDynamicTypes(conn, fetched);
                cached = cache.GetDynamicTypeMap(conn, options);
            }
        }

        return cached ?? new Dictionary<uint, DynamicTypeInfo>();
    }

    private static async Task<IReadOnlyDictionary<(uint tableOid, short attrNum), bool>> ResolveNotNullAsync(
        PgConnection pg,
        ConnectionInfo conn,
        IEnumerable<(uint tableOid, short attrNum)> attrs,
        AnalyzeOptions options,
        PgSchemaCache cache,
        CancellationToken ct)
    {
        var pairs = attrs.Where(a => a.tableOid != 0).Distinct().ToList();
        if (pairs.Count == 0) return new Dictionary<(uint, short), bool>();

        var cachedMap = cache.GetNotNullMap(conn, options);
        var missing = cachedMap is null
            ? pairs
            : pairs.Where(p => !cachedMap.ContainsKey(p)).ToList();

        if (missing.Count > 0)
        {
            var fetched = await pg.QueryNotNullAsync(missing, ct).ConfigureAwait(false);
            if (fetched.Count > 0)
            {
                cache.MergeNotNullMap(conn, fetched);
                cachedMap = cache.GetNotNullMap(conn, options);
            }
        }

        return cachedMap ?? new Dictionary<(uint, short), bool>();
    }
}

