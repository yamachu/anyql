using AnyQL.Core.Models;
using AnyQL.Core.Transport;
using AnyQL.MySql;
using AnyQL.Postgres;
using DotNet.Testcontainers.Builders;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace AnyQL.Tests;

/// <summary>
/// Integration tests for PostgresAnalyzer using a real PostgreSQL instance
/// spun up via Testcontainers.
/// </summary>
public sealed class PostgresAnalyzerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("testdb")
        .WithUsername("testuser")
        .WithPassword("testpass")
        .Build();

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        // Seed schema
        await _pg.ExecScriptAsync(@"
            CREATE TABLE users (
                id   uuid        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
                name varchar(100) NOT NULL,
                age  int,
                bio  text
            );
            CREATE TABLE posts (
                id         serial      PRIMARY KEY,
                user_id    uuid        NOT NULL REFERENCES users(id),
                title      text        NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );
        ");
    }

    public async Task DisposeAsync() => await _pg.DisposeAsync();

    private ConnectionInfo GetConn() => new()
    {
        Dialect = DbDialect.PostgreSql,
        Host = _pg.Hostname,
        Port = _pg.GetMappedPublicPort(5432),
        User = "testuser",
        Password = "testpass",
        Database = "testdb",
    };

    // ── Column metadata ──────────────────────────────────────────────────────

    [Fact]
    public async Task SimpleSelect_ReturnsCorrectColumns()
    {
        var result = await PostgresAnalyzer.AnalyzeAsync(
            "SELECT id, name, age FROM users",
            GetConn(),
            new TcpSocketTransport());

        Assert.Equal(3, result.Columns.Count);

        var id = result.Columns[0];
        var name = result.Columns[1];
        var age = result.Columns[2];

        Assert.Equal("id", id.Name);
        Assert.Equal("uuid", id.DbTypeName);
        Assert.Equal("Guid", id.DotNetType);
        Assert.Equal("string", id.TsType);
        Assert.False(id.IsNullable);

        Assert.Equal("name", name.Name);
        Assert.Equal("varchar", name.DbTypeName);
        Assert.Equal("string", name.DotNetType);
        Assert.False(name.IsNullable);

        Assert.Equal("age", age.Name);
        Assert.Equal("int4", age.DbTypeName);
        Assert.Equal("int", age.DotNetType);
        Assert.Equal("number", age.TsType);
        Assert.True(age.IsNullable);  // age is nullable
    }

    [Fact]
    public async Task SelectWithAlias_ReturnsAliasName()
    {
        var result = await PostgresAnalyzer.AnalyzeAsync(
            "SELECT id AS user_id, name AS display_name FROM users",
            GetConn(),
            new TcpSocketTransport());

        Assert.Equal("user_id", result.Columns[0].Name);
        Assert.Equal("display_name", result.Columns[1].Name);
    }

    [Fact]
    public async Task SelectWithJoin_ReturnsAllColumns()
    {
        var result = await PostgresAnalyzer.AnalyzeAsync(
            "SELECT u.id, u.name, p.title, p.created_at FROM users u JOIN posts p ON p.user_id = u.id",
            GetConn(),
            new TcpSocketTransport());

        Assert.Equal(4, result.Columns.Count);
        Assert.Equal("id", result.Columns[0].Name);
        Assert.Equal("name", result.Columns[1].Name);
        Assert.Equal("title", result.Columns[2].Name);
        Assert.Equal("created_at", result.Columns[3].Name);
        Assert.Equal("DateTimeOffset", result.Columns[3].DotNetType);
        Assert.Equal("Date", result.Columns[3].TsType);
        // INNER JOIN: nullability from pg_attribute only (posts.title NOT NULL → false)
        Assert.Equal(false, result.Columns[2].IsNullable);
    }

    [Fact]
    public async Task LeftJoin_RightSideColumns_AreNullable()
    {
        // posts.title is NOT NULL in the schema, but LEFT JOIN means it can be NULL
        // when there is no matching post for a user.
        var result = await PostgresAnalyzer.AnalyzeAsync(
            "SELECT u.id, u.name, p.title, p.created_at FROM users u LEFT JOIN posts p ON p.user_id = u.id",
            GetConn(),
            new TcpSocketTransport());

        Assert.Equal(4, result.Columns.Count);
        // users side (left/driving side): nullability from pg_attribute
        Assert.Equal(false, result.Columns[0].IsNullable); // users.id (PK, NOT NULL)
        Assert.Equal(false, result.Columns[1].IsNullable); // users.name (NOT NULL)
        // posts side (right/nullable side): forced nullable even though schema says NOT NULL
        Assert.Equal(true, result.Columns[2].IsNullable);  // posts.title NOT NULL in schema, but LEFT JOIN → nullable
        Assert.Equal(true, result.Columns[3].IsNullable);  // posts.created_at NOT NULL in schema, but LEFT JOIN → nullable
    }

    [Fact]
    public async Task LeftJoin_WildcardExpand_RightSideNullable()
    {
        // p.* expands to all posts columns; they should all be nullable
        var result = await PostgresAnalyzer.AnalyzeAsync(
            "SELECT u.id, u.name, p.* FROM users u LEFT JOIN posts p ON p.user_id = u.id",
            GetConn(),
            new TcpSocketTransport());

        Assert.True(result.Columns.Count >= 5); // users: id, name  posts: id, user_id, title, created_at
        // posts columns (index 2 onward) should all be nullable from LEFT JOIN
        foreach (var col in result.Columns.Skip(2))
            Assert.True(col.IsNullable, $"Column '{col.Name}' from LEFT JOIN side should be nullable");
    }

    [Fact]
    public async Task RightJoin_AllJoinedColumns_NullabilityUnknown()
    {
        // RIGHT JOIN: the left-side table (posts) may have no matching rows.
        // SqlJoinAnalyzer sets AllNullable = true → IsNullable = null for all
        // source-table columns (we can't statically determine which side is null).
        var result = await PostgresAnalyzer.AnalyzeAsync(
            "SELECT p.title, u.id, u.name FROM posts p RIGHT JOIN users u ON p.user_id = u.id",
            GetConn(),
            new TcpSocketTransport());

        Assert.Equal(3, result.Columns.Count);
        // All columns from source tables become null (unknown) due to RIGHT JOIN
        Assert.Null(result.Columns[0].IsNullable); // posts.title
        Assert.Null(result.Columns[1].IsNullable); // users.id
        Assert.Null(result.Columns[2].IsNullable); // users.name
    }

    [Fact]
    public async Task FullJoin_AllJoinedColumns_NullabilityUnknown()
    {
        // FULL JOIN: either side may have no match → all source-table columns unknown.
        var result = await PostgresAnalyzer.AnalyzeAsync(
            "SELECT u.id, u.name, p.title FROM users u FULL JOIN posts p ON p.user_id = u.id",
            GetConn(),
            new TcpSocketTransport());

        Assert.Equal(3, result.Columns.Count);
        Assert.Null(result.Columns[0].IsNullable); // users.id
        Assert.Null(result.Columns[1].IsNullable); // users.name
        Assert.Null(result.Columns[2].IsNullable); // posts.title
    }

    [Fact]
    public async Task SelectAggregate_ExpressionColumnsNullableUnknown()
    {
        var result = await PostgresAnalyzer.AnalyzeAsync(
            "SELECT COUNT(*) AS cnt, MAX(age) AS max_age FROM users",
            GetConn(),
            new TcpSocketTransport());

        Assert.Equal(2, result.Columns.Count);
        // Aggregate expressions have no source table → nullability unknown (null)
        Assert.Null(result.Columns[0].IsNullable);
        Assert.Null(result.Columns[1].IsNullable);
    }

    // ── Parameter type inference & RequiresCast ──────────────────────────────

    [Fact]
    public async Task Parameter_UuidColumn_RequiresCast()
    {
        // $1 is inferred as uuid (the column type) — a plain string needs ::uuid
        var result = await PostgresAnalyzer.AnalyzeAsync(
            "SELECT id, name FROM users WHERE id = $1",
            GetConn(),
            new TcpSocketTransport());

        Assert.Single(result.Parameters);
        var p = result.Parameters[0];
        Assert.Equal(1, p.Index);
        Assert.Equal("uuid", p.DbTypeName);
        Assert.True(p.RequiresCast);
    }

    [Fact]
    public async Task Parameter_WithExplicitCast_DoesNotRequireCast()
    {
        // $1::uuid — explicit cast present → RequiresCast = false
        var result = await PostgresAnalyzer.AnalyzeAsync(
            "SELECT id, name FROM users WHERE id = $1::uuid",
            GetConn(),
            new TcpSocketTransport());

        Assert.Single(result.Parameters);
        Assert.False(result.Parameters[0].RequiresCast);
    }

    [Fact]
    public async Task Parameter_TextColumn_DoesNotRequireCast()
    {
        // $1 matched against text/varchar → string is fine, no cast needed
        var result = await PostgresAnalyzer.AnalyzeAsync(
            "SELECT id FROM users WHERE name = $1",
            GetConn(),
            new TcpSocketTransport());

        Assert.Single(result.Parameters);
        Assert.False(result.Parameters[0].RequiresCast);
    }

    [Fact]
    public async Task Parameter_TimestampColumn_RequiresCast()
    {
        var result = await PostgresAnalyzer.AnalyzeAsync(
            "SELECT id FROM posts WHERE created_at > $1",
            GetConn(),
            new TcpSocketTransport());

        Assert.Single(result.Parameters);
        Assert.Equal("timestamptz", result.Parameters[0].DbTypeName);
        Assert.True(result.Parameters[0].RequiresCast);
    }

    [Fact]
    public async Task MultipleParameters_IndexedCorrectly()
    {
        var result = await PostgresAnalyzer.AnalyzeAsync(
            "SELECT id FROM users WHERE name = $1 AND age > $2",
            GetConn(),
            new TcpSocketTransport());

        Assert.Equal(2, result.Parameters.Count);
        Assert.Equal(1, result.Parameters[0].Index);
        Assert.Equal(2, result.Parameters[1].Index);
        Assert.False(result.Parameters[0].RequiresCast); // varchar — string is fine
        Assert.True(result.Parameters[1].RequiresCast);  // int4 — TS string needs ::int4 cast
    }

    // ── DML with RETURNING ────────────────────────────────────────────────────

    [Fact]
    public async Task InsertReturning_ReturnsColumns()
    {
        var result = await PostgresAnalyzer.AnalyzeAsync(
            "INSERT INTO users (id, name) VALUES ($1::uuid, $2) RETURNING id, name",
            GetConn(),
            new TcpSocketTransport());

        Assert.Equal(2, result.Columns.Count);
        Assert.Equal("id", result.Columns[0].Name);
        Assert.Equal("name", result.Columns[1].Name);
        Assert.Equal(2, result.Parameters.Count);
    }
}

/// <summary>
/// Tests for schema caching and user-defined type resolution.
/// Shares the same PostgreSQL container as <see cref="PostgresAnalyzerTests"/>.
/// </summary>
public sealed class PgSchemaCacheAndUdtTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("testdb")
        .WithUsername("testuser")
        .WithPassword("testpass")
        .Build();

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        await _pg.ExecScriptAsync(@"
            CREATE TYPE order_status AS ENUM ('pending', 'shipped', 'delivered');
            CREATE TABLE orders (
                id     serial       PRIMARY KEY,
                status order_status NOT NULL DEFAULT 'pending',
                notes  text
            );
        ");
    }

    public async Task DisposeAsync() => await _pg.DisposeAsync();

    private ConnectionInfo GetConn() => new()
    {
        Dialect = DbDialect.PostgreSql,
        Host = _pg.Hostname,
        Port = _pg.GetMappedPublicPort(5432),
        User = "testuser",
        Password = "testpass",
        Database = "testdb",
    };

    // ── User-defined type (enum) resolution ───────────────────────────────

    [Fact]
    public async Task EnumColumn_ReturnsEnumTypeName()
    {
        var result = await PostgresAnalyzer.AnalyzeAsync(
            "SELECT id, status, notes FROM orders",
            GetConn(),
            new TcpSocketTransport());

        Assert.Equal(3, result.Columns.Count);
        var statusCol = result.Columns[1];
        Assert.Equal("status", statusCol.Name);
        // UDT enum should be resolved to the type name, not "unknown"
        Assert.Equal("order_status", statusCol.DbTypeName);
        Assert.Equal("string", statusCol.TsType);
        Assert.Equal("string", statusCol.DotNetType);
    }

    [Fact]
    public async Task EnumParameter_DoesNotRequireCast()
    {
        // Enum types accept plain string literals in PG without ::cast
        var result = await PostgresAnalyzer.AnalyzeAsync(
            "SELECT id FROM orders WHERE status = $1",
            GetConn(),
            new TcpSocketTransport());

        Assert.Single(result.Parameters);
        var p = result.Parameters[0];
        Assert.Equal("order_status", p.DbTypeName);
        // Enums are string-compatible → RequiresCast should be false
        Assert.False(p.RequiresCast);
    }

    // ── Schema cache ──────────────────────────────────────────────────────

    [Fact]
    public async Task Cache_HitsOnSecondCall()
    {
        var cache = new PgSchemaCache();
        var opts = new AnalyzeOptions { SchemaCacheTtl = TimeSpan.FromMinutes(5) };
        var conn = GetConn();

        // First call — populates cache
        var r1 = await PostgresAnalyzer.AnalyzeAsync(
            "SELECT id, status FROM orders",
            conn, new TcpSocketTransport(), opts, cache);

        // The dynamic type map should now be populated
        var dynamicMap = cache.GetDynamicTypeMap(conn, opts);
        Assert.NotNull(dynamicMap);
        Assert.Contains(dynamicMap, kv => kv.Value.TypeName == "order_status");

        // Second call with same cache — should not need to re-fetch dynamic types
        // (We can't directly observe DB queries, but the result must be identical.)
        var r2 = await PostgresAnalyzer.AnalyzeAsync(
            "SELECT id, status FROM orders",
            conn, new TcpSocketTransport(), opts, cache);

        Assert.Equal(r1.Columns.Count, r2.Columns.Count);
        Assert.Equal(r1.Columns[1].DbTypeName, r2.Columns[1].DbTypeName);
    }

    [Fact]
    public async Task Cache_Invalidate_FetchesAgain()
    {
        var cache = new PgSchemaCache();
        var opts = new AnalyzeOptions { SchemaCacheTtl = TimeSpan.FromMinutes(5) };
        var conn = GetConn();

        await PostgresAnalyzer.AnalyzeAsync(
            "SELECT status FROM orders", conn, new TcpSocketTransport(), opts, cache);

        Assert.NotNull(cache.GetDynamicTypeMap(conn, opts));

        cache.Invalidate(conn);

        // After invalidation the cache should return null
        Assert.Null(cache.GetDynamicTypeMap(conn, opts));
    }

    [Fact]
    public async Task Cache_Disabled_WithZeroTtl()
    {
        var noCache = new AnalyzeOptions { SchemaCacheTtl = TimeSpan.Zero };
        var cache = new PgSchemaCache();
        var conn = GetConn();

        await PostgresAnalyzer.AnalyzeAsync(
            "SELECT status FROM orders", conn, new TcpSocketTransport(), noCache, cache);

        // TTL=0 means every entry is immediately expired
        Assert.Null(cache.GetDynamicTypeMap(conn, noCache));
    }
}

/// <summary>
/// Integration tests for MySqlAnalyzer using a real MySQL instance.
/// </summary>
public sealed class MySqlAnalyzerTests : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("testdb")
        .WithUsername("testuser")
        .WithPassword("testpass")
        .Build();

    public async Task InitializeAsync()
    {
        await _mysql.StartAsync();
        await _mysql.ExecScriptAsync(@"
            CREATE TABLE users (
                id   CHAR(36)     NOT NULL PRIMARY KEY,
                name VARCHAR(100) NOT NULL,
                age  INT,
                bio  TEXT
            );
            CREATE TABLE posts (
                id         INT         NOT NULL AUTO_INCREMENT PRIMARY KEY,
                user_id    CHAR(36)    NOT NULL,
                title      TEXT        NOT NULL,
                created_at DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
        ");
    }

    public async Task DisposeAsync() => await _mysql.DisposeAsync();

    private ConnectionInfo GetConn() => new()
    {
        Dialect = DbDialect.MySql,
        Host = _mysql.Hostname,
        Port = _mysql.GetMappedPublicPort(3306),
        User = "testuser",
        Password = "testpass",
        Database = "testdb",
    };

    [Fact]
    public async Task SimpleSelect_ReturnsCorrectColumns()
    {
        var result = await MySqlAnalyzer.AnalyzeAsync(
            "SELECT id, name, age FROM users",
            GetConn(),
            new TcpSocketTransport());

        Assert.Equal(3, result.Columns.Count);

        Assert.Equal("id", result.Columns[0].Name);
        Assert.Equal("STRING", result.Columns[0].DbTypeName);

        Assert.Equal("name", result.Columns[1].Name);
        Assert.Equal("VARCHAR", result.Columns[1].DbTypeName);

        Assert.Equal("age", result.Columns[2].Name);
        Assert.Equal("INT", result.Columns[2].DbTypeName);
        Assert.True(result.Columns[2].IsNullable);
    }

    [Fact]
    public async Task Parameters_AreReturnedWithBestEffortType()
    {
        var result = await MySqlAnalyzer.AnalyzeAsync(
            "SELECT id FROM users WHERE name = ? AND age > ?",
            GetConn(),
            new TcpSocketTransport());

        Assert.Equal(2, result.Parameters.Count);
        Assert.Equal(1, result.Parameters[0].Index);
        Assert.Equal(2, result.Parameters[1].Index);
        // MySQL returns mostly VARSTRING for params — RequiresCast always false
        Assert.False(result.Parameters[0].RequiresCast);
        Assert.False(result.Parameters[1].RequiresCast);
    }
}
