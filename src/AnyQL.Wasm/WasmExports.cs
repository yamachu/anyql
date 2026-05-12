using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnyQL.Core.Models;
using AnyQL.MySql;
using AnyQL.Postgres;
using AnyQL.Postgres.Protocol;

namespace AnyQL.Wasm;

/// <summary>
/// JSExport entry points exposed to the JavaScript host.
/// All methods accept/return JSON strings for simplicity across the WASM boundary.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class WasmExports
{
    // Process-wide schema cache shared across all Analyze calls within the WASM instance.
    private static readonly PgSchemaCache _pgCache = PgSchemaCache.Default;
    private static bool _socketModuleInitialized;

    /// <summary>
    /// Registers the socket JS module with the .NET runtime.
    /// Must be called once from the JS host before the first <see cref="Analyze"/> call.
    /// </summary>
    /// <param name="socketModuleUrl">
    /// Absolute URL (e.g. file:///…/nodeSocket.js) or importable specifier
    /// of the JS module that implements the "anyql-socket" interop contract.
    /// </param>
    [JSExport]
    public static async Task InitializeSocket(string socketModuleUrl)
    {
        if (_socketModuleInitialized) return;
        await JSHost.ImportAsync("anyql-socket", socketModuleUrl).ConfigureAwait(false);
        _socketModuleInitialized = true;
    }
    /// <summary>
    /// Analyzes a SQL query and returns column + parameter metadata as JSON.
    ///
    /// JS usage:
    ///   const result = await AnyQL.analyze(sqlJson);
    ///   // result is a JSON string: { columns: [...], parameters: [...], error?: string }
    /// </summary>
    [JSExport]
    public static Task<string> Analyze(string requestJson)
    {
        return AnalyzeInternalAsync(requestJson);
    }

    private static async Task<string> AnalyzeInternalAsync(string requestJson)
    {
        try
        {
            var request = JsonSerializer.Deserialize(requestJson, WasmJsonContext.Default.AnalyzeRequest)
                          ?? throw new ArgumentException("Invalid request JSON.");

            var conn = new ConnectionInfo
            {
                Dialect = request.Dialect == "postgresql" ? DbDialect.PostgreSql : DbDialect.MySql,
                Host = request.Host,
                Port = request.Port,
                User = request.User,
                Password = request.Password,
                Database = request.Database,
            };

            await using var transport = new JsSocketTransport();

            AnalyzeResult result = conn.Dialect switch
            {
                DbDialect.PostgreSql => await PostgresAnalyzer.AnalyzeAsync(
                    request.Sql, conn, transport, cache: _pgCache),
                DbDialect.MySql => await MySqlAnalyzer.AnalyzeAsync(request.Sql, conn, transport),
                _ => throw new NotSupportedException($"Unsupported dialect: {conn.Dialect}")
            };

            var response = new AnalyzeResponse
            {
                Columns = result.Columns.Select(c => new ColumnInfoDto
                {
                    Name = c.Name,
                    DbTypeName = c.DbTypeName,
                    TypeCode = c.TypeCode,
                    IsNullable = c.IsNullable,
                    DotNetType = c.DotNetType,
                    TsType = c.TsType,
                    SourceTableOid = c.SourceTableOid,
                    SourceColumnAttributeNumber = c.SourceColumnAttributeNumber,
                }).ToArray(),
                Parameters = result.Parameters.Select(p => new ParameterInfoDto
                {
                    Index = p.Index,
                    DbTypeName = p.DbTypeName,
                    TypeCode = p.TypeCode,
                    TsType = p.TsType,
                    DotNetType = p.DotNetType,
                    RequiresCast = p.RequiresCast,
                }).ToArray(),
            };

            return JsonSerializer.Serialize(response, WasmJsonContext.Default.AnalyzeResponse);
        }
        catch (Exception ex)
        {
            var error = new ErrorResponse { Error = ex.Message };
            return JsonSerializer.Serialize(error, WasmJsonContext.Default.ErrorResponse);
        }
    }
}

// ── DTO types ────────────────────────────────────────────────────────────────

public sealed class AnalyzeRequest
{
    [JsonPropertyName("sql")] public required string Sql { get; init; }
    [JsonPropertyName("dialect")] public required string Dialect { get; init; } // "postgresql" | "mysql"
    [JsonPropertyName("host")] public required string Host { get; init; }
    [JsonPropertyName("port")] public required int Port { get; init; }
    [JsonPropertyName("user")] public required string User { get; init; }
    [JsonPropertyName("password")] public required string Password { get; init; }
    [JsonPropertyName("database")] public required string Database { get; init; }
}

public sealed class AnalyzeResponse
{
    [JsonPropertyName("columns")] public required ColumnInfoDto[] Columns { get; init; }
    [JsonPropertyName("parameters")] public required ParameterInfoDto[] Parameters { get; init; }
}

public sealed class ColumnInfoDto
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("dbTypeName")] public required string DbTypeName { get; init; }
    [JsonPropertyName("typeCode")] public required uint TypeCode { get; init; }
    [JsonPropertyName("isNullable")] public bool? IsNullable { get; init; }
    [JsonPropertyName("dotNetType")] public required string DotNetType { get; init; }
    [JsonPropertyName("tsType")] public required string TsType { get; init; }
    [JsonPropertyName("sourceTableOid")] public uint SourceTableOid { get; init; }
    [JsonPropertyName("sourceColumnAttributeNumber")] public short SourceColumnAttributeNumber { get; init; }
}

public sealed class ParameterInfoDto
{
    [JsonPropertyName("index")] public required int Index { get; init; }
    [JsonPropertyName("dbTypeName")] public required string DbTypeName { get; init; }
    [JsonPropertyName("typeCode")] public required uint TypeCode { get; init; }
    [JsonPropertyName("tsType")] public required string TsType { get; init; }
    [JsonPropertyName("dotNetType")] public required string DotNetType { get; init; }
    [JsonPropertyName("requiresCast")] public required bool RequiresCast { get; init; }
}

public sealed class ErrorResponse
{
    [JsonPropertyName("error")] public required string Error { get; init; }
}

// Source-generated JSON context for WASM (avoids reflection)
[JsonSerializable(typeof(AnalyzeRequest))]
[JsonSerializable(typeof(AnalyzeResponse))]
[JsonSerializable(typeof(ErrorResponse))]
internal sealed partial class WasmJsonContext : JsonSerializerContext;
