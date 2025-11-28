// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using Xunit;

namespace Vertx.PgClient.Tests;

/// <summary>
/// Tests for PostgreSQL array type support.
/// </summary>
public class ArrayTypeTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public ArrayTypeTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanSelectIntegerArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        var result = await connection.QueryAsync("SELECT ARRAY[1, 2, 3, 4, 5]::int4[]");
        var array = result[0].GetValue(0).GetIntegerArray();
        
        Assert.NotNull(array);
        Assert.Equal([1, 2, 3, 4, 5], array);
    }

    [Fact]
    public async Task CanSelectBigIntArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        var result = await connection.QueryAsync("SELECT ARRAY[1000000000000, 2000000000000]::int8[]");
        var array = result[0].GetValue(0).GetLongArray();
        
        Assert.NotNull(array);
        Assert.Equal([1000000000000L, 2000000000000L], array);
    }

    [Fact]
    public async Task CanSelectSmallIntArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        var result = await connection.QueryAsync("SELECT ARRAY[1, 2, 3]::int2[]");
        var array = result[0].GetValue(0).GetShortArray();
        
        Assert.NotNull(array);
        Assert.Equal([(short)1, (short)2, (short)3], array);
    }

    [Fact]
    public async Task CanSelectFloatArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        var result = await connection.QueryAsync("SELECT ARRAY[1.5, 2.5, 3.5]::float4[]");
        var array = result[0].GetValue(0).GetFloatArray();
        
        Assert.NotNull(array);
        Assert.Equal([1.5f, 2.5f, 3.5f], array);
    }

    [Fact]
    public async Task CanSelectDoubleArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        var result = await connection.QueryAsync("SELECT ARRAY[1.5, 2.5, 3.5]::float8[]");
        var array = result[0].GetValue(0).GetDoubleArray();
        
        Assert.NotNull(array);
        Assert.Equal([1.5, 2.5, 3.5], array);
    }

    [Fact]
    public async Task CanSelectBooleanArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        var result = await connection.QueryAsync("SELECT ARRAY[true, false, true]::boolean[]");
        var array = result[0].GetValue(0).GetBooleanArray();
        
        Assert.NotNull(array);
        Assert.Equal([true, false, true], array);
    }

    [Fact]
    public async Task CanSelectTextArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        var result = await connection.QueryAsync("SELECT ARRAY['hello', 'world', 'test']::text[]");
        var array = result[0].GetValue(0).GetStringArray();
        
        Assert.NotNull(array);
        Assert.Equal(3, array.Length);
        Assert.Equal("hello", array[0]);
        Assert.Equal("world", array[1]);
        Assert.Equal("test", array[2]);
    }

    [Fact]
    public async Task CanSelectVarcharArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        var result = await connection.QueryAsync("SELECT ARRAY['a', 'b', 'c']::varchar[]");
        var array = result[0].GetValue(0).GetStringArray();
        
        Assert.NotNull(array);
        Assert.Equal(3, array.Length);
        Assert.Equal("a", array[0]);
        Assert.Equal("b", array[1]);
        Assert.Equal("c", array[2]);
    }

    [Fact]
    public async Task CanSelectUuidArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        var uuid1 = Guid.Parse("a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11");
        var uuid2 = Guid.Parse("b0eebc99-9c0b-4ef8-bb6d-6bb9bd380a22");
        
        var result = await connection.QueryAsync(
            $"SELECT ARRAY['{uuid1}'::uuid, '{uuid2}'::uuid]");
        var array = result[0].GetValue(0).GetGuidArray();
        
        Assert.NotNull(array);
        Assert.Equal([uuid1, uuid2], array);
    }

    [Fact]
    public async Task CanSelectDateArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        var result = await connection.QueryAsync("SELECT ARRAY['2023-01-15'::date, '2023-06-20'::date]");
        var array = result[0].GetValue(0).GetDateArray();
        
        Assert.NotNull(array);
        Assert.Equal([new DateOnly(2023, 1, 15), new DateOnly(2023, 6, 20)], array);
    }

    [Fact]
    public async Task CanSelectTimestampArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        var result = await connection.QueryAsync(
            "SELECT ARRAY['2023-01-15 10:30:00'::timestamp, '2023-06-20 15:45:00'::timestamp]");
        var array = result[0].GetValue(0).GetDateTimeArray();
        
        Assert.NotNull(array);
        Assert.Equal(2, array.Length);
        Assert.Equal(new DateTime(2023, 1, 15, 10, 30, 0), array[0]);
        Assert.Equal(new DateTime(2023, 6, 20, 15, 45, 0), array[1]);
    }

    [Fact]
    public async Task CanSelectTimestampTzArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        var result = await connection.QueryAsync(
            "SELECT ARRAY['2023-01-15 10:30:00+00'::timestamptz, '2023-06-20 15:45:00+00'::timestamptz]");
        var array = result[0].GetValue(0).GetDateTimeOffsetArray();
        
        Assert.NotNull(array);
        Assert.Equal(2, array.Length);
    }

    [Fact]
    public async Task CanSelectEmptyArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        var result = await connection.QueryAsync("SELECT ARRAY[]::int4[]");
        var array = result[0].GetValue(0).GetIntegerArray();
        
        Assert.NotNull(array);
        Assert.Empty(array);
    }

    [Fact]
    public async Task CanInsertAndSelectIntegerArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        // Create temp table
        await connection.QueryAsync("CREATE TEMP TABLE test_array (id serial, values int4[])");
        
        // Insert using prepared query
        var inputArray = new[] { 10, 20, 30, 40, 50 };
        await connection.PreparedQueryAsync(
            "INSERT INTO test_array (values) VALUES ($1)",
            Tuple.Of(inputArray));
        
        // Select back
        var result = await connection.QueryAsync("SELECT values FROM test_array");
        var outputArray = result[0].GetValue(0).GetIntegerArray();
        
        Assert.NotNull(outputArray);
        Assert.Equal(inputArray, outputArray);
    }

    [Fact]
    public async Task CanInsertAndSelectStringArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        // Create temp table
        await connection.QueryAsync("CREATE TEMP TABLE test_text_array (id serial, values text[])");
        
        // Insert using prepared query - cast to object to prevent params expansion
        var inputArray = new[] { "hello", "world", "test" };
        await connection.PreparedQueryAsync(
            "INSERT INTO test_text_array (values) VALUES ($1)",
            Tuple.Create((object)inputArray));
        
        // Select back
        var result = await connection.QueryAsync("SELECT values FROM test_text_array");
        var outputArray = result[0].GetValue(0).GetStringArray();
        
        Assert.NotNull(outputArray);
        Assert.Equal(inputArray, outputArray);
    }

    [Fact]
    public async Task CanInsertAndSelectBooleanArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        // Create temp table
        await connection.QueryAsync("CREATE TEMP TABLE test_bool_array (id serial, values boolean[])");
        
        // Insert using prepared query
        var inputArray = new[] { true, false, true, false };
        await connection.PreparedQueryAsync(
            "INSERT INTO test_bool_array (values) VALUES ($1)",
            Tuple.Of(inputArray));
        
        // Select back
        var result = await connection.QueryAsync("SELECT values FROM test_bool_array");
        var outputArray = result[0].GetValue(0).GetBooleanArray();
        
        Assert.NotNull(outputArray);
        Assert.Equal(inputArray, outputArray);
    }

    [Fact]
    public async Task CanInsertAndSelectDoubleArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        // Create temp table
        await connection.QueryAsync("CREATE TEMP TABLE test_double_array (id serial, values float8[])");
        
        // Insert using prepared query
        var inputArray = new[] { 1.1, 2.2, 3.3, 4.4 };
        await connection.PreparedQueryAsync(
            "INSERT INTO test_double_array (values) VALUES ($1)",
            Tuple.Of(inputArray));
        
        // Select back
        var result = await connection.QueryAsync("SELECT values FROM test_double_array");
        var outputArray = result[0].GetValue(0).GetDoubleArray();
        
        Assert.NotNull(outputArray);
        Assert.Equal(inputArray, outputArray);
    }

    [Fact]
    public async Task CanInsertAndSelectUuidArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        // Create temp table
        await connection.QueryAsync("CREATE TEMP TABLE test_uuid_array (id serial, values uuid[])");
        
        // Insert using prepared query
        var inputArray = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        await connection.PreparedQueryAsync(
            "INSERT INTO test_uuid_array (values) VALUES ($1)",
            Tuple.Of(inputArray));
        
        // Select back
        var result = await connection.QueryAsync("SELECT values FROM test_uuid_array");
        var outputArray = result[0].GetValue(0).GetGuidArray();
        
        Assert.NotNull(outputArray);
        Assert.Equal(inputArray, outputArray);
    }

    [Fact]
    public async Task CanInsertAndSelectDateArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        // Create temp table
        await connection.QueryAsync("CREATE TEMP TABLE test_date_array (id serial, values date[])");
        
        // Insert using prepared query
        var inputArray = new[] { new DateOnly(2023, 1, 1), new DateOnly(2023, 6, 15), new DateOnly(2023, 12, 31) };
        await connection.PreparedQueryAsync(
            "INSERT INTO test_date_array (values) VALUES ($1)",
            Tuple.Of(inputArray));
        
        // Select back
        var result = await connection.QueryAsync("SELECT values FROM test_date_array");
        var outputArray = result[0].GetValue(0).GetDateArray();
        
        Assert.NotNull(outputArray);
        Assert.Equal(inputArray, outputArray);
    }

    [Fact]
    public async Task CanSelectArrayByColumnName()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        var result = await connection.QueryAsync("SELECT ARRAY[1, 2, 3] AS numbers");
        var array = result[0].GetValue("numbers").GetIntegerArray();
        
        Assert.NotNull(array);
        Assert.Equal([1, 2, 3], array);
    }

    [Fact]
    public async Task CanUseArrayInWhereClause()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        // Create temp table with data
        await connection.QueryAsync(@"
            CREATE TEMP TABLE test_any (id serial, value int);
            INSERT INTO test_any (value) VALUES (1), (2), (3), (4), (5);
        ");
        
        // Query using ANY with array parameter
        var searchValues = new[] { 2, 4 };
        var result = await connection.PreparedQueryAsync(
            "SELECT value FROM test_any WHERE value = ANY($1) ORDER BY value",
            Tuple.Of(searchValues));
        
        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].GetValue(0).GetInteger());
        Assert.Equal(4, result[1].GetValue(0).GetInteger());
    }

    [Fact]
    public async Task CanUseArrayWithUnnest()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        var inputArray = new[] { 10, 20, 30 };
        var result = await connection.PreparedQueryAsync(
            "SELECT unnest($1::int[]) AS value",
            Tuple.Of(inputArray));
        
        Assert.Equal(3, result.Count);
        Assert.Equal(10, result[0].GetValue(0).GetInteger());
        Assert.Equal(20, result[1].GetValue(0).GetInteger());
        Assert.Equal(30, result[2].GetValue(0).GetInteger());
    }

    [Fact]
    public async Task CanSelectArrayWithSpecialCharacters()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        var result = await connection.QueryAsync(@"SELECT ARRAY['hello, world', 'test""quote', 'back\slash']::text[]");
        var array = result[0].GetValue(0).GetStringArray();
        
        Assert.NotNull(array);
        Assert.Equal(3, array.Length);
        Assert.Equal("hello, world", array[0]);
        Assert.Equal("test\"quote", array[1]);
        Assert.Equal("back\\slash", array[2]);
    }

    [Fact]
    public async Task CanSelectSingleElementArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        var result = await connection.QueryAsync("SELECT ARRAY[42]::int4[]");
        var array = result[0].GetValue(0).GetIntegerArray();
        
        Assert.NotNull(array);
        Assert.Single(array);
        Assert.Equal(42, array[0]);
    }

    [Fact]
    public async Task CanSelectLargeArray()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        // Generate array with 1000 elements
        var result = await connection.QueryAsync("SELECT array_agg(i) FROM generate_series(1, 1000) i");
        var array = result[0].GetValue(0).GetIntegerArray();
        
        Assert.NotNull(array);
        Assert.Equal(1000, array.Length);
        Assert.Equal(1, array[0]);
        Assert.Equal(1000, array[999]);
    }
}
