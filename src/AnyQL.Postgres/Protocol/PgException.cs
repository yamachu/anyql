namespace AnyQL.Postgres.Protocol;

/// <summary>Represents an error returned by PostgreSQL.</summary>
public sealed class PgException(string message) : Exception(message);
