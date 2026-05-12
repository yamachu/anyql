namespace AnyQL.Core.TypeMapping;

/// <summary>
/// Maps PostgreSQL built-in OIDs to .NET and TypeScript type names.
/// Source: pg_type system catalog (select oid, typname from pg_type order by oid).
/// Only the most commonly encountered OIDs are listed; unknowns fall back to (string, string).
/// </summary>
public static class PostgresTypeMap
{
    // OIDs that are safely representable as a plain string on the TypeScript side
    // without an explicit cast in SQL.
    private static readonly HashSet<uint> StringCompatibleOids = new()
    {
        25,   // text
        705,  // unknown
        1042, // bpchar
        1043, // varchar
        19,   // name
        18,   // char (single byte)
    };

    private static readonly Dictionary<uint, TypeEntry> Map = new()
    {
        // ── Boolean ──────────────────────────────────────────────────────────
        { 16,   new("bool",        "bool",             "boolean") },

        // ── Integer ──────────────────────────────────────────────────────────
        { 21,   new("int2",        "short",            "number") },
        { 23,   new("int4",        "int",              "number") },
        { 20,   new("int8",        "long",             "string") },  // bigint → string to avoid JS precision loss
        { 26,   new("oid",         "uint",             "number") },

        // ── Floating point ───────────────────────────────────────────────────
        { 700,  new("float4",      "float",            "number") },
        { 701,  new("float8",      "double",           "number") },

        // ── Arbitrary precision ──────────────────────────────────────────────
        { 1700, new("numeric",     "decimal",          "string") },

        // ── Text ─────────────────────────────────────────────────────────────
        { 25,   new("text",        "string",           "string") },
        { 1042, new("bpchar",      "string",           "string") },
        { 1043, new("varchar",     "string",           "string") },
        { 19,   new("name",        "string",           "string") },
        { 18,   new("char",        "string",           "string") },
        { 705,  new("unknown",     "string",           "string") },

        // ── UUID ─────────────────────────────────────────────────────────────
        { 2950, new("uuid",        "Guid",             "string") },

        // ── Date / Time ──────────────────────────────────────────────────────
        { 1082, new("date",        "DateOnly",         "string") },
        { 1083, new("time",        "TimeOnly",         "string") },
        { 1114, new("timestamp",   "DateTime",         "Date") },
        { 1184, new("timestamptz", "DateTimeOffset",   "Date") },
        { 1186, new("interval",    "TimeSpan",         "string") },
        { 1266, new("timetz",      "DateTimeOffset",   "string") },

        // ── Binary ───────────────────────────────────────────────────────────
        { 17,   new("bytea",       "byte[]",           "Uint8Array") },

        // ── JSON ─────────────────────────────────────────────────────────────
        { 114,  new("json",        "string",           "unknown") },
        { 3802, new("jsonb",       "string",           "unknown") },

        // ── Network ──────────────────────────────────────────────────────────
        { 650,  new("cidr",        "string",           "string") },
        { 869,  new("inet",        "string",           "string") },
        { 829,  new("macaddr",     "string",           "string") },
        { 774,  new("macaddr8",    "string",           "string") },

        // ── Arrays (common) ──────────────────────────────────────────────────
        { 1000, new("_bool",       "bool[]",           "boolean[]") },
        { 1005, new("_int2",       "short[]",          "number[]") },
        { 1007, new("_int4",       "int[]",            "number[]") },
        { 1016, new("_int8",       "long[]",           "string[]") },
        { 1009, new("_text",       "string[]",         "string[]") },
        { 1015, new("_varchar",    "string[]",         "string[]") },
        { 2951, new("_uuid",       "Guid[]",           "string[]") },
        { 1231, new("_numeric",    "decimal[]",        "string[]") },
        { 1021, new("_float4",     "float[]",          "number[]") },
        { 1022, new("_float8",     "double[]",         "number[]") },

        // ── Misc ─────────────────────────────────────────────────────────────
        { 2278, new("void",        "void",             "void") },
        { 2249, new("record",      "object",           "Record<string, unknown>") },
        { 3614, new("tsvector",    "string",           "string") },
        { 3615, new("tsquery",     "string",           "string") },
        { 142,  new("xml",         "string",           "string") },
        { 3769, new("regconfig",   "uint",             "number") },
    };

    /// <summary>
    /// Look up .NET and TypeScript type names for a given PostgreSQL OID.
    /// Returns ("string", "string") for unknown OIDs.
    /// </summary>
    public static (string dotNetType, string tsType) Resolve(uint oid)
    {
        if (Map.TryGetValue(oid, out var entry))
            return (entry.DotNetType, entry.TsType);

        return ("string", "string");
    }

    /// <summary>
    /// Returns the canonical PG type name for an OID, or "unknown" if not in the map.
    /// </summary>
    public static string GetTypeName(uint oid)
    {
        if (Map.TryGetValue(oid, out var entry))
            return entry.PgTypeName;
        return "unknown";
    }

    /// <summary>
    /// Returns true when a plain TypeScript string value can be passed
    /// for this OID without needing an explicit SQL cast.
    /// </summary>
    public static bool IsStringCompatible(uint oid) => StringCompatibleOids.Contains(oid);

    private readonly record struct TypeEntry(string PgTypeName, string DotNetType, string TsType);
}
