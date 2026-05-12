namespace AnyQL.Postgres.Protocol;

/// <summary>Type information fetched dynamically from pg_type for unknown OIDs.</summary>
public sealed class DynamicTypeInfo
{
    /// <summary>pg_type.typname (e.g. "status", "_status", "address")</summary>
    public required string TypeName { get; init; }

    /// <summary>
    /// pg_type.typtype:
    ///   'b' = base, 'c' = composite, 'd' = domain, 'e' = enum,
    ///   'p' = pseudo, 'r' = range
    /// </summary>
    public required char TypeType { get; init; }

    /// <summary>
    /// For array types (typname starts with '_'): OID of the element type.
    /// 0 for non-array types.
    /// </summary>
    public uint ElemOid { get; init; }

    /// <summary>Schema name (e.g. "public", "pg_catalog").</summary>
    public required string Namespace { get; init; }
}
