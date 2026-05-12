using AnyQL.Client;
using AnyQL.Core.Models;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace AnyQL.Tests;

/// <summary>
/// Unit tests for <see cref="ConnectionStringParser"/> — no Docker required.
/// </summary>
public sealed class ConnectionStringParserTests
{
    [Theory]
    [InlineData("Host=pg-host;Port=5432;Username=alice;Password=secret;Database=mydb")]
    [InlineData("Server=pg-host;Port=5432;User Id=alice;Password=secret;Database=mydb")]
    [InlineData("host=pg-host;port=5432;user=alice;password=secret;db=mydb")]
    public void ForPostgres_ParsesCorrectly(string cs)
    {
        var info = ConnectionStringParser.ForPostgres(cs);

        Assert.Equal(DbDialect.PostgreSql, info.Dialect);
        Assert.Equal("pg-host", info.Host);
        Assert.Equal(5432, info.Port);
        Assert.Equal("alice", info.User);
        Assert.Equal("secret", info.Password);
        Assert.Equal("mydb", info.Database);
    }

    [Fact]
    public void ForPostgres_DefaultPort()
    {
        var info = ConnectionStringParser.ForPostgres(
            "Host=pg-host;Username=alice;Password=secret;Database=mydb");

        Assert.Equal(5432, info.Port);
    }

    [Theory]
    [InlineData("Server=my-host;Port=3306;User Id=bob;Password=secret;Database=mydb")]
    [InlineData("Host=my-host;Port=3306;Username=bob;Password=secret;Database=mydb")]
    public void ForMySql_ParsesCorrectly(string cs)
    {
        var info = ConnectionStringParser.ForMySql(cs);

        Assert.Equal(DbDialect.MySql, info.Dialect);
        Assert.Equal("my-host", info.Host);
        Assert.Equal(3306, info.Port);
        Assert.Equal("bob", info.User);
        Assert.Equal("secret", info.Password);
        Assert.Equal("mydb", info.Database);
    }

    [Fact]
    public void ForMySql_DefaultPort()
    {
        var info = ConnectionStringParser.ForMySql(
            "Server=my-host;User Id=bob;Password=secret;Database=mydb");

        Assert.Equal(3306, info.Port);
    }

    [Fact]
    public void ForPostgres_MissingKey_Throws()
    {
        Assert.Throws<FormatException>(
            () => ConnectionStringParser.ForPostgres("Host=pg-host;Password=secret;Database=mydb"));
    }
}

// ── Shared Docker fixture ────────────────────────────────────────────────────

/// <summary>
/// Starts a PostgreSQL and MySQL container once for the entire <see cref="AnyQLClientTests"/>
/// class, reusing them across all tests.
/// </summary>
public sealed class AnyQLClientFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("testdb")
        .WithUsername("testuser")
        .WithPassword("testpass")
        .Build();

    private readonly MySqlContainer _my = new MySqlBuilder("mysql:8.4")
        .WithDatabase("testdb")
        .WithUsername("testuser")
        .WithPassword("testpass")
        .Build();

    public ConnectionInfo PgConn { get; private set; } = null!;
    public ConnectionInfo MyConn { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_pg.StartAsync(), _my.StartAsync());

        await _pg.ExecScriptAsync(@"
            CREATE TABLE items (
                id   serial PRIMARY KEY,
                name text   NOT NULL,
                note text
            );
        ");

        await _my.ExecScriptAsync(@"
            CREATE TABLE items (
                id   INT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
                name VARCHAR(100) NOT NULL,
                note TEXT
            );
        ");

        PgConn = new ConnectionInfo
        {
            Dialect = DbDialect.PostgreSql,
            Host = _pg.Hostname,
            Port = _pg.GetMappedPublicPort(5432),
            User = "testuser",
            Password = "testpass",
            Database = "testdb",
        };

        MyConn = new ConnectionInfo
        {
            Dialect = DbDialect.MySql,
            Host = _my.Hostname,
            Port = _my.GetMappedPublicPort(3306),
            User = "testuser",
            Password = "testpass",
            Database = "testdb",
        };
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(_pg.DisposeAsync().AsTask(), _my.DisposeAsync().AsTask());
    }
}

/// <summary>
/// Integration tests for <see cref="AnyQLAnalyzer"/> — requires Docker.
/// Containers are shared across all tests via <see cref="AnyQLClientFixture"/>.
/// </summary>
public sealed class AnyQLClientTests : IClassFixture<AnyQLClientFixture>
{
    private readonly AnyQLClientFixture _fixture;

    public AnyQLClientTests(AnyQLClientFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AnalyzeAsync_PostgreSQL_ReturnsColumns()
    {
        var result = await AnyQLAnalyzer.AnalyzeAsync(
            "SELECT id, name, note FROM items", _fixture.PgConn);

        Assert.Equal(3, result.Columns.Count);
        Assert.Equal("id", result.Columns[0].Name);
        Assert.Equal(false, result.Columns[0].IsNullable);
        Assert.Equal(false, result.Columns[1].IsNullable); // name NOT NULL
        Assert.Equal(true, result.Columns[2].IsNullable);  // note nullable
    }

    [Fact]
    public async Task AnalyzeAsync_MySQL_ReturnsColumns()
    {
        var result = await AnyQLAnalyzer.AnalyzeAsync(
            "SELECT id, name, note FROM items", _fixture.MyConn);

        Assert.Equal(3, result.Columns.Count);
        Assert.Equal("id", result.Columns[0].Name);
    }

    [Fact]
    public async Task AnalyzeAsync_InvalidDialect_Throws()
    {
        var conn = new ConnectionInfo
        {
            Dialect = (DbDialect)99,
            Host = "localhost",
            Port = 5432,
            User = "u",
            Password = "p",
            Database = "d",
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => AnyQLAnalyzer.AnalyzeAsync("SELECT 1", conn));
    }

    [Fact]
    public async Task ConnectionStringParser_RoundTrip_WorksWithAnalyzer()
    {
        var cs = $"Host={_fixture.PgConn.Host};Port={_fixture.PgConn.Port};" +
                 $"Username=testuser;Password=testpass;Database=testdb";
        var conn = ConnectionStringParser.ForPostgres(cs);

        var result = await AnyQLAnalyzer.AnalyzeAsync(
            "SELECT id, name FROM items", conn);

        Assert.Equal(2, result.Columns.Count);
        Assert.Equal("id", result.Columns[0].Name);
    }
}
