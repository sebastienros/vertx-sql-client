// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using Xunit;

namespace Vertx.PgClient.Tests;

[Collection("PostgreSQL")]
public class DataTypeTests
{
    private readonly PostgresFixture _fixture;

    public DataTypeTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanSelectDate()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '2024-06-15'::date as d");

        Assert.Equal(1, result.Count);
        var date = result[0].Get<DateOnly>("d");
        Assert.Equal(new DateOnly(2024, 6, 15), date);
    }

    [Fact]
    public async Task CanSelectTimestamp()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '2024-06-15 14:30:00'::timestamp as ts");

        Assert.Equal(1, result.Count);
        var ts = result[0].Get<DateTime>("ts");
        Assert.Equal(new DateTime(2024, 6, 15, 14, 30, 0), ts);
    }

    [Fact]
    public async Task CanSelectUuid()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var uuid = Guid.NewGuid();
        var result = await connection.QueryAsync($"SELECT '{uuid}'::uuid as id");

        Assert.Equal(1, result.Count);
        Assert.Equal(uuid, result[0].Get<Guid>("id"));
    }

    [Fact]
    public async Task CanSelectBytea()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '\\x48656c6c6f'::bytea as data");

        Assert.Equal(1, result.Count);
        var bytes = result[0].Get<byte[]>("data");
        Assert.NotNull(bytes);
        Assert.Equal("Hello", System.Text.Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public async Task CanSelectJson()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '{\"name\": \"test\", \"value\": 42}'::json as j");

        Assert.Equal(1, result.Count);
        var json = result[0].GetString("j");
        Assert.Contains("\"name\"", json);
        Assert.Contains("\"test\"", json);
    }

    [Fact]
    public async Task CanSelectJsonb()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '{\"key\": \"value\"}'::jsonb as jb");

        Assert.Equal(1, result.Count);
        var jsonb = result[0].GetString("jb");
        Assert.Contains("key", jsonb);
        Assert.Contains("value", jsonb);
    }

    [Fact]
    public async Task CanSelectPoint()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '(1.5, 2.5)'::point as p");

        Assert.Equal(1, result.Count);
        var point = result[0].Get<Data.Point>("p");
        Assert.Equal(1.5, point.X, 0.01);
        Assert.Equal(2.5, point.Y, 0.01);
    }

    [Fact]
    public async Task CanSelectInterval()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '1 year 2 months 3 days 4 hours 5 minutes 6 seconds'::interval as i");

        Assert.Equal(1, result.Count);
        var interval = result[0].Get<Data.Interval>("i");
        Assert.Equal(14, interval.Months); // 1 year + 2 months = 14 months
        Assert.Equal(3, interval.Days);
    }

    [Fact]
    public async Task CanSelectNumeric()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT 123.456::numeric as n");

        Assert.Equal(1, result.Count);
        // Numeric is returned as string in text mode
        var value = result[0].GetValue("n");
        Assert.NotNull(value);
    }

    [Fact]
    public async Task CanSelectIntegerArray()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT ARRAY[1, 2, 3, 4, 5]::int4[] as arr");

        Assert.Equal(1, result.Count);
        var arr = result[0].Get<int[]>("arr");
        Assert.NotNull(arr);
        Assert.Equal(5, arr.Length);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, arr);
    }

    [Fact]
    public async Task CanSelectTextArray()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT ARRAY['a', 'b', 'c']::text[] as arr");

        Assert.Equal(1, result.Count);
        var arr = result[0].Get<string[]>("arr");
        Assert.NotNull(arr);
        Assert.Equal(3, arr.Length);
        Assert.Equal(new[] { "a", "b", "c" }, arr);
    }
}
