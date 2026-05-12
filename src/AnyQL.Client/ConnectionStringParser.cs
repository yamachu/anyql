using System.Text.RegularExpressions;
using AnyQL.Core.Models;

namespace AnyQL.Client;

/// <summary>
/// Helpers for building <see cref="ConnectionInfo"/> from ADO.NET-style connection strings.
/// </summary>
/// <remarks>
/// The parser handles the most common key names used by Npgsql and MySqlConnector.
/// SSL/TLS options and other advanced parameters are silently ignored — only the
/// host, port, user, password, and database keys are extracted.
/// </remarks>
public static class ConnectionStringParser
{
    // ── PostgreSQL (Npgsql) ──────────────────────────────────────────────────

    /// <summary>
    /// Parses a Npgsql-compatible connection string and returns a
    /// <see cref="ConnectionInfo"/> with <see cref="DbDialect.PostgreSql"/>.
    /// </summary>
    /// <param name="connectionString">
    /// E.g. <c>Host=localhost;Port=5432;Username=app;Password=secret;Database=mydb</c>
    /// or <c>Server=localhost;User Id=app;Password=secret;Database=mydb</c>
    /// </param>
    public static ConnectionInfo ForPostgres(string connectionString)
    {
        var kv = Parse(connectionString);

        string host = GetRequired(kv, connectionString, "host", "server");
        int port = GetInt(kv, 5432, "port");
        string user = GetRequired(kv, connectionString, "username", "user id", "user", "uid");
        string password = GetOptional(kv, "", "password", "pwd");
        string database = GetRequired(kv, connectionString, "database", "db");

        return new ConnectionInfo
        {
            Dialect = DbDialect.PostgreSql,
            Host = host,
            Port = port,
            User = user,
            Password = password,
            Database = database,
        };
    }

    // ── MySQL (MySqlConnector / MySql.Data) ──────────────────────────────────

    /// <summary>
    /// Parses a MySqlConnector-compatible connection string and returns a
    /// <see cref="ConnectionInfo"/> with <see cref="DbDialect.MySql"/>.
    /// </summary>
    /// <param name="connectionString">
    /// E.g. <c>Server=localhost;Port=3306;User Id=app;Password=secret;Database=mydb</c>
    /// or <c>Host=localhost;Username=app;Password=secret;Database=mydb</c>
    /// </param>
    public static ConnectionInfo ForMySql(string connectionString)
    {
        var kv = Parse(connectionString);

        string host = GetRequired(kv, connectionString, "server", "host", "data source", "datasource");
        int port = GetInt(kv, 3306, "port");
        string user = GetRequired(kv, connectionString, "user id", "uid", "username", "user");
        string password = GetOptional(kv, "", "password", "pwd");
        string database = GetRequired(kv, connectionString, "database", "initial catalog");

        return new ConnectionInfo
        {
            Dialect = DbDialect.MySql,
            Host = host,
            Port = port,
            User = user,
            Password = password,
            Database = database,
        };
    }

    // ── Internal parser ──────────────────────────────────────────────────────

    /// <summary>
    /// Splits a semicolon-delimited key=value connection string into a
    /// case-insensitive dictionary.
    /// </summary>
    private static Dictionary<string, string> Parse(string connectionString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = segment.IndexOf('=');
            if (eq <= 0) continue;

            string key = segment[..eq].Trim();
            string value = segment[(eq + 1)..].Trim();
            result[key] = value;
        }

        return result;
    }

    private static string GetRequired(
        Dictionary<string, string> kv,
        string original,
        params string[] candidates)
    {
        foreach (var key in candidates)
        {
            if (kv.TryGetValue(key, out var val) && val.Length > 0)
                return val;
        }

        throw new FormatException(
            $"Connection string is missing a required key " +
            $"(expected one of: {string.Join(", ", candidates)}). " +
            $"Input: {original}");
    }

    private static string GetOptional(
        Dictionary<string, string> kv,
        string defaultValue,
        params string[] candidates)
    {
        foreach (var key in candidates)
        {
            if (kv.TryGetValue(key, out var val))
                return val;
        }
        return defaultValue;
    }

    private static int GetInt(
        Dictionary<string, string> kv,
        int defaultValue,
        params string[] candidates)
    {
        foreach (var key in candidates)
        {
            if (kv.TryGetValue(key, out var val) && int.TryParse(val, out int result))
                return result;
        }
        return defaultValue;
    }
}
