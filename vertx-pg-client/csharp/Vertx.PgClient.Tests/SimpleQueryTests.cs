// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using Xunit;

namespace Vertx.PgClient.Tests;

[Collection("PostgreSQL")]
public class SimpleQueryTests
{
    private readonly PostgresFixture _fixture;

    public SimpleQueryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanSelectLiteral()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT 1 as num, 'hello' as greeting");

        Assert.Equal(1, result.Count);
        Assert.Equal(1, result[0].GetValue(0).GetInteger());
        Assert.Equal("hello", result[0].GetValue(1).GetString());
    }

    [Fact]
    public async Task CanSelectByColumnName()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT 42 as answer, 'world' as target");

        Assert.Equal(1, result.Count);
        Assert.Equal(42, result[0].GetValue("answer").GetInteger());
        Assert.Equal("world", result[0].GetValue("target").GetString());
    }

    [Fact]
    public async Task CanSelectMultipleRows()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT * FROM generate_series(1, 5) as n");

        Assert.Equal(5, result.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(i + 1, result[i].GetValue(0).GetInteger());
        }
    }

    [Fact]
    public async Task CanSelectNull()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT NULL::text as empty");

        Assert.Equal(1, result.Count);
        Assert.True(result[0].GetValue(0).IsNull);
        Assert.Null(result[0].GetValue("empty").GetString());
    }

    [Fact]
    public async Task CanSelectVariousTypes()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync(@"
            SELECT 
                true as bool_val,
                42::int2 as int2_val,
                123456::int4 as int4_val,
                9876543210::int8 as int8_val,
                3.14::float4 as float4_val,
                2.718281828::float8 as float8_val,
                'hello world'::text as text_val
        ");

        Assert.Equal(1, result.Count);
        var row = result[0];

        Assert.True(row.GetValue("bool_val").GetBoolean());
        Assert.Equal(42, row.GetValue("int2_val").GetShort());
        Assert.Equal(123456, row.GetValue("int4_val").GetInteger());
        Assert.Equal(9876543210L, row.GetValue("int8_val").GetLong());
        Assert.Equal(3.14f, row.GetValue("float4_val").GetFloat(), 0.01f);
        Assert.Equal(2.718281828, row.GetValue("float8_val").GetDouble(), 0.0001);
        Assert.Equal("hello world", row.GetValue("text_val").GetString());
    }

    [Fact]
    public async Task CanCreateAndQueryTable()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var tableName = $"users_{Guid.NewGuid():N}";

        // Create table
        await connection.QueryAsync($@"
            CREATE TABLE {tableName} (
                id SERIAL PRIMARY KEY,
                name TEXT NOT NULL,
                age INT
            )
        ");

        try
        {
            // Insert data
            await connection.QueryAsync($"INSERT INTO {tableName} (name, age) VALUES ('Alice', 30)");
            await connection.QueryAsync($"INSERT INTO {tableName} (name, age) VALUES ('Bob', 25)");
            await connection.QueryAsync($"INSERT INTO {tableName} (name, age) VALUES ('Charlie', 35)");

            // Query
            var result = await connection.QueryAsync($"SELECT name, age FROM {tableName} ORDER BY name");

            Assert.Equal(3, result.Count);
            Assert.Equal("Alice", result[0].GetValue("name").GetString());
            Assert.Equal(30, result[0].GetValue("age").GetInteger());
            Assert.Equal("Bob", result[1].GetValue("name").GetString());
            Assert.Equal(25, result[1].GetValue("age").GetInteger());
            Assert.Equal("Charlie", result[2].GetValue("name").GetString());
            Assert.Equal(35, result[2].GetValue("age").GetInteger());
        }
        finally
        {
            // Cleanup
            await connection.QueryAsync($"DROP TABLE {tableName}");
        }
    }
}
