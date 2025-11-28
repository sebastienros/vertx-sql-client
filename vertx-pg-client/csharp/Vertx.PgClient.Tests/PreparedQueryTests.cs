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

        // Create table
        await connection.QueryAsync(@"
            CREATE TABLE IF NOT EXISTS test_products (
                id SERIAL PRIMARY KEY,
                name TEXT NOT NULL,
                price FLOAT8 NOT NULL
            )
        ");

        await connection.QueryAsync("DELETE FROM test_products");

        // Insert with prepared query
        await connection.PreparedQueryAsync(
            "INSERT INTO test_products (name, price) VALUES ($1, $2)",
            Tuple.Create("Widget", 19.99)
        );

        await connection.PreparedQueryAsync(
            "INSERT INTO test_products (name, price) VALUES ($1, $2)",
            Tuple.Create("Gadget", 29.99)
        );

        // Query with prepared query
        var result = await connection.PreparedQueryAsync(
            "SELECT name, price FROM test_products WHERE price > $1 ORDER BY name",
            Tuple.Create(20.0)
        );

        Assert.Equal(1, result.Count);
        Assert.Equal("Gadget", result[0].GetValue("name").GetString());
        Assert.Equal(29.99, result[0].GetValue("price").GetDouble(), 0.01);

        // Cleanup
        await connection.QueryAsync("DROP TABLE test_products");
    }
}
