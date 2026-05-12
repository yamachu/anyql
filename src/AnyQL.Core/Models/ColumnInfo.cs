namespace AnyQL.Core.Models;

/// <summary>
/// Metadata for a single column in a SELECT result.
/// </summary>
public sealed class ColumnInfo
{
    /// <summary>Column name as it appears in the result set (alias applied).</summary>
    public required string Name { get; init; }

    /// <summary>Database-native type name (e.g. "int4", "varchar", "uuid" for PG; "INT", "VARCHAR" for MySQL).</summary>
    public required string DbTypeName { get; init; }

    /// <summary>
    /// Database-specific type identifier.
    /// PostgreSQL: OID (uint). MySQL: column_type enum value (byte).
    /// </summary>
    public required uint TypeCode { get; init; }

    /// <summary>Whether the column can be NULL. Null means unknown (e.g. expression columns).</summary>
    public bool? IsNullable { get; init; }

    /// <summary>Suggested .NET type name (e.g. "int", "string", "Guid", "DateTime").</summary>
    public required string DotNetType { get; init; }

    /// <summary>Suggested TypeScript type name (e.g. "number", "string", "Date").</summary>
    public required string TsType { get; init; }

    /// <summary>OID of the source table (PostgreSQL only). 0 if not available.</summary>
    public uint SourceTableOid { get; init; }

    /// <summary>Attribute number of the source column (PostgreSQL only). 0 if not available.</summary>
    public short SourceColumnAttributeNumber { get; init; }
}
