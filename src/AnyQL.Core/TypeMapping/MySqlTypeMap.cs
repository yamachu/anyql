namespace AnyQL.Core.TypeMapping;

/// <summary>
/// Maps MySQL column_type byte values (from COM_STMT_PREPARE response) to .NET and TypeScript types.
/// Values from MySQL protocol: https://dev.mysql.com/doc/dev/mysql-server/latest/field__types_8h.html
/// </summary>
public static class MySqlTypeMap
{
    private static readonly Dictionary<byte, TypeEntry> Map = new()
    {
        // ── Integer ──────────────────────────────────────────────────────────
        { 0x01, new("TINYINT",    "sbyte",    "number") },
        { 0x02, new("SMALLINT",   "short",    "number") },
        { 0x03, new("INT",        "int",      "number") },
        { 0x08, new("BIGINT",     "long",     "string") },  // bigint → string
        { 0x09, new("MEDIUMINT",  "int",      "number") },

        // ── Floating point ───────────────────────────────────────────────────
        { 0x04, new("FLOAT",      "float",    "number") },
        { 0x05, new("DOUBLE",     "double",   "number") },

        // ── Fixed-point ──────────────────────────────────────────────────────
        { 0x00, new("DECIMAL",    "decimal",  "string") },
        { 0xF6, new("NEWDECIMAL", "decimal",  "string") },

        // ── Date / Time ──────────────────────────────────────────────────────
        { 0x0A, new("DATE",       "DateOnly",         "string") },
        { 0x0B, new("TIME",       "TimeOnly",         "string") },
        { 0x0C, new("DATETIME",   "DateTime",         "Date") },
        { 0x07, new("TIMESTAMP",  "DateTimeOffset",   "Date") },
        { 0x0D, new("YEAR",       "int",              "number") },

        // ── String ───────────────────────────────────────────────────────────
        { 0x0F, new("VARCHAR",    "string",   "string") },
        { 0xFD, new("VARCHAR",    "string",   "string") },  // VAR_STRING in protocol = VARCHAR
        { 0xFE, new("STRING",     "string",   "string") },

        // ── Text / Blob ──────────────────────────────────────────────────────
        { 0xFC, new("BLOB",       "byte[]",   "Uint8Array") },
        { 0x10, new("BIT",        "ulong",    "string") },

        // ── JSON ─────────────────────────────────────────────────────────────
        { 0xF5, new("JSON",       "string",   "unknown") },

        // ── Geometry ─────────────────────────────────────────────────────────
        { 0xFF, new("GEOMETRY",   "byte[]",   "Uint8Array") },

        // ── NULL ─────────────────────────────────────────────────────────────
        { 0x06, new("NULL",       "object",   "null") },
    };

    // Column flags (subset used for nullability)
    public const ushort NotNullFlag = 0x0001;
    public const ushort UnsignedFlag = 0x0020;

    /// <summary>Returns .NET and TypeScript type names for a MySQL column_type byte.</summary>
    public static (string dotNetType, string tsType) Resolve(byte columnType, ushort flags)
    {
        if (Map.TryGetValue(columnType, out var entry))
        {
            // Unsigned integers: promote to uint / ulong where applicable
            if ((flags & UnsignedFlag) != 0)
            {
                var (dotNet, ts) = entry.ColumnType switch
                {
                    "TINYINT" => ("byte", "number"),
                    "SMALLINT" => ("ushort", "number"),
                    "INT" => ("uint", "number"),
                    "MEDIUMINT" => ("uint", "number"),
                    "BIGINT" => ("ulong", "string"),
                    _ => (entry.DotNetType, entry.TsType)
                };
                return (dotNet, ts);
            }
            return (entry.DotNetType, entry.TsType);
        }
        return ("string", "string");
    }

    /// <summary>Returns the canonical MySQL type name for a column_type byte.</summary>
    public static string GetTypeName(byte columnType)
    {
        if (Map.TryGetValue(columnType, out var entry))
            return entry.ColumnType;
        return "UNKNOWN";
    }

    private readonly record struct TypeEntry(string ColumnType, string DotNetType, string TsType);
}
