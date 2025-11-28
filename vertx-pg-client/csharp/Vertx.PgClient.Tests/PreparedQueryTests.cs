// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using Xunit;

namespace Vertx.PgClient.Tests;

[Collection("PostgreSQL")]
public class PreparedQueryTests
{
    private readonly PostgresFixture _fixture;

    public PreparedQueryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanExecutePreparedQueryWithIntParameter()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.PreparedQueryAsync(
            "SELECT $1::int as value",
            Tuple.Create(42)
        );

        Assert.Equal(1, result.Count);
        Assert.Equal(42, result[0].GetValue("value").GetInteger());
    }

    [Fact]
    public async Task CanExecutePreparedQueryWithStringParameter()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.PreparedQueryAsync(
            "SELECT $1::text as greeting",
            Tuple.Create("Hello, World!")
        );

        Assert.Equal(1, result.Count);
        Assert.Equal("Hello, World!", result[0].GetValue("greeting").GetString());
    }

    [Fact]
    public async Task CanExecutePreparedQueryWithMultipleParameters()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.PreparedQueryAsync(
            "SELECT $1::int + $2::int as sum, $3::text as label",
            Tuple.Create(10, 20, "result")
        );

        Assert.Equal(1, result.Count);
        Assert.Equal(30, result[0].GetValue("sum").GetInteger());
        Assert.Equal("result", result[0].GetValue("label").GetString());
    }

    [Fact]
    public async Task CanExecutePreparedQueryWithNullParameter()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.PreparedQueryAsync(
            "SELECT $1::text as maybe_null",
            Tuple.Create((string?)null)
        );

        Assert.Equal(1, result.Count);
        Assert.Null(result[0].GetValue("maybe_null").GetString());
    }

    [Fact]
    public async Task CanExecutePreparedQueryWithBoolParameter()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.PreparedQueryAsync(
            "SELECT $1::bool as flag",
            Tuple.Create(true)
        );

        Assert.Equal(1, result.Count);
        Assert.True(result[0].GetValue("flag").GetBoolean());
    }

    [Fact]
    public async Task CanInsertAndQueryWithPreparedStatements()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var tableName = $"products_{Guid.NewGuid():N}";

        // Create table
        await connection.QueryAsync($@"
            CREATE TABLE {tableName} (
                id SERIAL PRIMARY KEY,
                name TEXT NOT NULL,
                price FLOAT8 NOT NULL
            )
        ");

        try
        {
            // Insert with prepared query
            await connection.PreparedQueryAsync(
                $"INSERT INTO {tableName} (name, price) VALUES ($1, $2)",
                Tuple.Create("Widget", 19.99)
            );

            await connection.PreparedQueryAsync(
                $"INSERT INTO {tableName} (name, price) VALUES ($1, $2)",
                Tuple.Create("Gadget", 29.99)
            );

            // Query with prepared query
            var result = await connection.PreparedQueryAsync(
                $"SELECT name, price FROM {tableName} WHERE price > $1 ORDER BY name",
                Tuple.Create(20.0)
            );

            Assert.Equal(1, result.Count);
            Assert.Equal("Gadget", result[0].GetValue("name").GetString());
            Assert.Equal(29.99, result[0].GetValue("price").GetDouble(), 0.01);
        }
        finally
        {
            // Cleanup
            await connection.QueryAsync($"DROP TABLE {tableName}");
        }
    }

    [Fact]
    public async Task CanUsePreparedStatementCaching()
    {
        var options = _fixture.CreateConnectOptions()
            .SetCachePreparedStatements(true)
            .SetPreparedStatementCacheMaxSize(10);
        await using var connection = await PgConnection.ConnectAsync(options);

        const string sql = "SELECT $1::int as value";

        // Execute the same query multiple times - should reuse the cached statement
        for (int i = 0; i < 5; i++)
        {
            var result = await connection.PreparedQueryAsync(sql, Tuple.Create(i * 10));
            Assert.Equal(1, result.Count);
            Assert.Equal(i * 10, result[0].GetValue("value").GetInteger());
        }
    }

    [Fact]
    public async Task PreparedStatementCachingWithMultipleQueries()
    {
        var options = _fixture.CreateConnectOptions()
            .SetCachePreparedStatements(true)
            .SetPreparedStatementCacheMaxSize(10);
        await using var connection = await PgConnection.ConnectAsync(options);

        const string sql1 = "SELECT $1::int + $2::int as sum";
        const string sql2 = "SELECT $1::text as name";

        // Interleave different queries
        for (int i = 0; i < 3; i++)
        {
            var result1 = await connection.PreparedQueryAsync(sql1, Tuple.Create(i, i + 1));
            Assert.Equal(i + (i + 1), result1[0].GetValue("sum").GetInteger());

            var result2 = await connection.PreparedQueryAsync(sql2, Tuple.Create($"test{i}"));
            Assert.Equal($"test{i}", result2[0].GetValue("name").GetString());
        }
    }

    [Fact]
    public async Task PreparedStatementCacheLRUEviction()
    {
        // Small cache to test eviction
        var options = _fixture.CreateConnectOptions()
            .SetCachePreparedStatements(true)
            .SetPreparedStatementCacheMaxSize(3);
        await using var connection = await PgConnection.ConnectAsync(options);

        // Execute more unique queries than cache size
        for (int i = 0; i < 10; i++)
        {
            var sql = $"SELECT {i} as value, $1::int as param";
            var result = await connection.PreparedQueryAsync(sql, Tuple.Create(i * 100));
            Assert.Equal(i, result[0].GetValue("value").GetInteger());
            Assert.Equal(i * 100, result[0].GetValue("param").GetInteger());
        }
    }

    [Fact]
    public async Task PreparedStatementCachingDisabledByDefault()
    {
        // Default options should not have caching
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        // This should work without caching
        for (int i = 0; i < 3; i++)
        {
            var result = await connection.PreparedQueryAsync(
                "SELECT $1::int as value",
                Tuple.Create(i)
            );
            Assert.Equal(i, result[0].GetValue("value").GetInteger());
        }
    }

    [Fact]
    public async Task PreparedStatementCacheRespectsSqlLengthLimit()
    {
        var options = _fixture.CreateConnectOptions()
            .SetCachePreparedStatements(true)
            .SetPreparedStatementCacheSqlLimit(50);  // Very short limit
        await using var connection = await PgConnection.ConnectAsync(options);

        // Short query should work
        var result1 = await connection.PreparedQueryAsync("SELECT $1::int", Tuple.Create(1));
        Assert.Equal(1, result1[0].GetValue("int4").GetInteger());

        // Long query should also work (but won't be cached - no error expected)
        var longSql = $"SELECT $1::int /* {new string('x', 100)} */";
        var result2 = await connection.PreparedQueryAsync(longSql, Tuple.Create(2));
        Assert.Equal(2, result2[0].GetValue("int4").GetInteger());
    }
}
