namespace AnyQL.MySql.Protocol;

/// <summary>Represents an error returned by MySQL.</summary>
public sealed class MySqlException(string message) : Exception(message);
