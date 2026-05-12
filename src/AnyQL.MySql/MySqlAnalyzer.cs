using AnyQL.Core.Models;
using AnyQL.Core.Transport;
using AnyQL.Core.TypeMapping;
using AnyQL.MySql.Protocol;

namespace AnyQL.MySql;

/// <summary>
/// Analyzes a SQL query against a live MySQL database and returns
/// column metadata and parameter type information.
/// </summary>
public static class MySqlAnalyzer
{
    /// <summary>
    /// Connects to MySQL, issues COM_STMT_PREPARE, and returns an <see cref="AnalyzeResult"/>.
    /// </summary>
    /// <param name="sql">The SQL statement to analyze.</param>
    /// <param name="conn">Connection information. Must have Dialect == MySql.</param>
    /// <param name="transport">Socket transport to use. Caller is responsible for disposal.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<AnalyzeResult> AnalyzeAsync(
        string sql,
        ConnectionInfo conn,
        ISocketTransport transport,
        CancellationToken ct = default)
    {
        if (conn.Dialect != DbDialect.MySql)
            throw new ArgumentException("ConnectionInfo.Dialect must be MySql.", nameof(conn));

        await using var my = new MyConnection(transport);
        await my.OpenAsync(conn, ct).ConfigureAwait(false);

        var (paramDefs, colDefs) = await my.PrepareAsync(sql, ct).ConfigureAwait(false);

        // ── Build ColumnInfo list ────────────────────────────────────────────
        var columns = colDefs.Select(c =>
        {
            var (dotNet, ts) = MySqlTypeMap.Resolve(c.ColumnType, c.Flags);
            bool isNullable = (c.Flags & MySqlTypeMap.NotNullFlag) == 0;

            return new ColumnInfo
            {
                Name = c.Name,
                DbTypeName = MySqlTypeMap.GetTypeName(c.ColumnType),
                TypeCode = c.ColumnType,
                IsNullable = isNullable,
                DotNetType = dotNet,
                TsType = ts,
                // MySQL COM_STMT_PREPARE does not return source table OIDs
                SourceTableOid = 0,
                SourceColumnAttributeNumber = 0,
            };
        }).ToList();

        // ── Build ParameterInfo list ─────────────────────────────────────────
        // MySQL COM_STMT_PREPARE returns parameter Column Definitions but
        // their column_type is almost always MYSQL_TYPE_VAR_STRING (0xFD).
        // RequiresCast is always false because MySQL does implicit coercion.
        var parameters = paramDefs.Select((p, idx) =>
        {
            var (dotNet, ts) = MySqlTypeMap.Resolve(p.ColumnType, p.Flags);
            return new ParameterInfo
            {
                Index = idx + 1,
                DbTypeName = MySqlTypeMap.GetTypeName(p.ColumnType),
                TypeCode = p.ColumnType,
                TsType = ts,
                DotNetType = dotNet,
                RequiresCast = false, // MySQL does not benefit from strict cast checking
            };
        }).ToList();

        return new AnalyzeResult
        {
            Columns = columns,
            Parameters = parameters,
        };
    }
}
