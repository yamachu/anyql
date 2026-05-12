namespace AnyQL.Core.Models;

/// <summary>
/// Options that control the behavior of an Analyze call.
/// </summary>
public sealed class AnalyzeOptions
{
    /// <summary>
    /// How long cached schema information (nullability, user-defined types) is considered
    /// fresh before being re-fetched from the database.
    /// Default: 5 minutes. Set to <see cref="TimeSpan.Zero"/> to disable caching.
    /// </summary>
    public TimeSpan SchemaCacheTtl { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Default instance with all defaults.</summary>
    public static AnalyzeOptions Default { get; } = new();
}
