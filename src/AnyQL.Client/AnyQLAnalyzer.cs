using AnyQL.Core.Models;
using AnyQL.Core.Transport;
using AnyQL.MySql;
using AnyQL.Postgres;
using AnyQL.Postgres.Protocol;

namespace AnyQL.Client;

/// <summary>
/// Unified entry point for SQL analysis against PostgreSQL or MySQL.
/// <para>
/// Automatically selects the right backend based on <see cref="ConnectionInfo.Dialect"/>
/// and manages the <see cref="TcpSocketTransport"/> internally, so callers need not
/// reference <c>AnyQL.Postgres</c> or <c>AnyQL.MySql</c> directly.
/// </para>
/// </summary>
public static class AnyQLAnalyzer
{
    // Process-wide PostgreSQL schema cache shared across all Analyze calls.
    private static readonly PgSchemaCache _pgCache = PgSchemaCache.Default;

    /// <summary>
    /// Analyzes <paramref name="sql"/> against the live database described by
    /// <paramref name="conn"/> and returns column metadata and parameter type
    /// information.
    /// </summary>
    /// <param name="sql">
    /// The SQL statement to analyze. Must be a <c>SELECT</c> or a DML with
    /// <c>RETURNING</c>. Parameterized queries are supported ($1/$2 for PostgreSQL,
    /// ? for MySQL).
    /// </param>
    /// <param name="conn">Target database connection details.</param>
    /// <param name="options">
    /// Analysis options such as schema cache TTL. Defaults to
    /// <see cref="AnalyzeOptions.Default"/> (5-minute cache).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="AnalyzeResult"/> containing per-column metadata and per-parameter
    /// type information.
    /// </returns>
    public static Task<AnalyzeResult> AnalyzeAsync(
        string sql,
        ConnectionInfo conn,
        AnalyzeOptions? options = null,
        CancellationToken ct = default)
    {
        return conn.Dialect switch
        {
            DbDialect.PostgreSql => PostgresAnalyzer.AnalyzeAsync(
                sql, conn, new TcpSocketTransport(), options, _pgCache, ct),

            DbDialect.MySql => MySqlAnalyzer.AnalyzeAsync(
                sql, conn, new TcpSocketTransport(), ct),

            _ => throw new ArgumentException(
                $"Unsupported dialect: {conn.Dialect}", nameof(conn))
        };
    }
}
