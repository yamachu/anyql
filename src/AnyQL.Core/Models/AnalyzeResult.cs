namespace AnyQL.Core.Models;

/// <summary>
/// Result of analyzing a SQL statement against the live database schema.
/// </summary>
public sealed class AnalyzeResult
{
    /// <summary>Ordered list of columns returned by the SELECT (empty for non-SELECT statements).</summary>
    public required IReadOnlyList<ColumnInfo> Columns { get; init; }

    /// <summary>Ordered list of query parameters ($1…$N for PG, ? for MySQL).</summary>
    public required IReadOnlyList<ParameterInfo> Parameters { get; init; }
}
