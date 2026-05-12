namespace AnyQL.Core.Models;

/// <summary>
/// Type information for a single query parameter ($1, $2, ... for PG; ? for MySQL).
/// </summary>
public sealed class ParameterInfo
{
    /// <summary>1-based parameter index.</summary>
    public required int Index { get; init; }

    /// <summary>Database-native type name inferred by the server.</summary>
    public required string DbTypeName { get; init; }

    /// <summary>Database-specific type code (OID for PG; column_type for MySQL).</summary>
    public required uint TypeCode { get; init; }

    /// <summary>Suggested TypeScript type for this parameter.</summary>
    public required string TsType { get; init; }

    /// <summary>Suggested .NET type for this parameter.</summary>
    public required string DotNetType { get; init; }

    /// <summary>
    /// When true the caller should add an explicit cast in SQL
    /// (e.g. $1::uuid) because the inferred DB type does not accept
    /// a plain string without narrowing.
    /// Only meaningful for PostgreSQL; always false for MySQL.
    /// </summary>
    public bool RequiresCast { get; init; }
}
