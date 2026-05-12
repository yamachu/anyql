namespace AnyQL.Core.Models;

/// <summary>Database dialect.</summary>
public enum DbDialect
{
    PostgreSql,
    MySql
}

/// <summary>Connection parameters for the target database.</summary>
public sealed class ConnectionInfo
{
    public required DbDialect Dialect { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string User { get; init; }
    public required string Password { get; init; }
    public required string Database { get; init; }
}
