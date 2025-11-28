// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using Xunit;

namespace Vertx.PgClient.Tests;

[Collection("PostgreSQL")]
public class ExecuteReaderTests
{
    private readonly PostgresFixture _fixture;

    public ExecuteReaderTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanReadSingleRow()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        await using var reader = await connection.ExecuteReaderAsync("SELECT 1 as num, 'hello' as greeting");

        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetValue(0).GetInteger());
        Assert.Equal("hello", reader.GetValue(1).GetString());
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task CanReadMultipleRows()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        await using var reader = await connection.ExecuteReaderAsync("SELECT * FROM generate_series(1, 5) as n");

        for (int i = 1; i <= 5; i++)
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(i, reader.GetValue(0).GetInteger());
        }
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task CanReadByColumnName()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        await using var reader = await connection.ExecuteReaderAsync("SELECT 42 as answer, 'world' as target");

        Assert.True(await reader.ReadAsync());
        Assert.Equal(42, reader.GetValue("answer").GetInteger());
        Assert.Equal("world", reader.GetValue("target").GetString());
    }

    [Fact]
    public async Task CanReadNullValues()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        await using var reader = await connection.ExecuteReaderAsync("SELECT NULL::text as empty");

        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetValue(0).IsNull);
        Assert.True(reader.IsDBNull(0));
        Assert.Null(reader.GetString(0));
    }

    [Fact]
    public async Task CanReadVariousTypes()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        await using var reader = await connection.ExecuteReaderAsync(@"
            SELECT 
                true as bool_val,
                42::int2 as int2_val,
                123456::int4 as int4_val,
                9876543210::int8 as int8_val,
                3.14::float4 as float4_val,
                2.718281828::float8 as float8_val,
                'hello world'::text as text_val
        ");

        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.Equal(42, reader.GetInt16(1));
        Assert.Equal(123456, reader.GetInt32(2));
        Assert.Equal(9876543210L, reader.GetInt64(3));
        Assert.Equal(3.14f, reader.GetFloat(4), 0.01f);
        Assert.Equal(2.718281828, reader.GetDouble(5), 0.0001);
        Assert.Equal("hello world", reader.GetString(6));
    }

    [Fact]
    public async Task FieldCountIsCorrect()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        await using var reader = await connection.ExecuteReaderAsync("SELECT 1, 2, 3, 4, 5");

        Assert.True(await reader.ReadAsync());
        Assert.Equal(5, reader.FieldCount);
    }

    [Fact]
    public async Task ColumnNamesAreAvailable()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        await using var reader = await connection.ExecuteReaderAsync("SELECT 1 as first, 2 as second, 3 as third");

        Assert.True(await reader.ReadAsync());
        Assert.Equal(3, reader.ColumnNames.Count);
        Assert.Equal("first", reader.ColumnNames[0]);
        Assert.Equal("second", reader.ColumnNames[1]);
        Assert.Equal("third", reader.ColumnNames[2]);
    }

    [Fact]
    public async Task GetOrdinalReturnsCorrectIndex()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        await using var reader = await connection.ExecuteReaderAsync("SELECT 1 as alpha, 2 as beta, 3 as gamma");

        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetOrdinal("alpha"));
        Assert.Equal(1, reader.GetOrdinal("beta"));
        Assert.Equal(2, reader.GetOrdinal("gamma"));
    }

    [Fact]
    public async Task GetNameReturnsCorrectColumnName()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        await using var reader = await connection.ExecuteReaderAsync("SELECT 1 as alpha, 2 as beta");

        Assert.True(await reader.ReadAsync());
        Assert.Equal("alpha", reader.GetName(0));
        Assert.Equal("beta", reader.GetName(1));
    }

    [Fact]
    public async Task HasRowsIsTrueWhenResultsExist()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        await using var reader = await connection.ExecuteReaderAsync("SELECT 1");

        Assert.True(await reader.ReadAsync());
        Assert.True(reader.HasRows);
    }

    [Fact]
    public async Task IsCompletedIsTrueAfterAllRowsRead()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        await using var reader = await connection.ExecuteReaderAsync("SELECT 1");

        Assert.False(reader.IsCompleted);
        Assert.True(await reader.ReadAsync());
        Assert.False(reader.IsCompleted);
        Assert.False(await reader.ReadAsync());
        Assert.True(reader.IsCompleted);
    }

    [Fact]
    public async Task CanReadWithParameters()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        await using var reader = await connection.ExecuteReaderAsync(
            "SELECT $1::int as value, $2::text as name",
            Tuple.Create(42, "test")
        );

        Assert.True(await reader.ReadAsync());
        Assert.Equal(42, reader.GetInt32(0));
        Assert.Equal("test", reader.GetString(1));
    }

    [Fact]
    public async Task CanReadWithNullParameter()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        await using var reader = await connection.ExecuteReaderAsync(
            "SELECT $1::text as maybe_null",
            Tuple.Create((string?)null)
        );

        Assert.True(await reader.ReadAsync());
        Assert.True(reader.IsDBNull(0));
    }

    [Fact]
    public async Task MustReadAllRowsBeforeDispose()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        // Read all rows before dispose
        await using (var reader = await connection.ExecuteReaderAsync("SELECT * FROM generate_series(1, 5) as n"))
        {
            int count = 0;
            while (await reader.ReadAsync())
            {
                count++;
            }
            Assert.Equal(5, count);
        }

        // Connection should still be usable
        await using var reader2 = await connection.ExecuteReaderAsync("SELECT 42 as answer");
        Assert.True(await reader2.ReadAsync());
        Assert.Equal(42, reader2.GetInt32(0));
    }

    [Fact]
    public async Task CanReadDateTimeTypes()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        await using var reader = await connection.ExecuteReaderAsync(@"
            SELECT 
                '2024-01-15'::date as date_val,
                '2024-01-15 10:30:00'::timestamp as ts_val,
                '2024-01-15 10:30:00+00'::timestamptz as tstz_val
        ");

        Assert.True(await reader.ReadAsync());
        
        var dateVal = reader.GetDateTime(0);
        Assert.Equal(2024, dateVal.Year);
        Assert.Equal(1, dateVal.Month);
        Assert.Equal(15, dateVal.Day);

        var tsVal = reader.GetDateTime(1);
        Assert.Equal(2024, tsVal.Year);
        Assert.Equal(10, tsVal.Hour);
        Assert.Equal(30, tsVal.Minute);
    }

    [Fact]
    public async Task CanReadGuid()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var expectedGuid = Guid.Parse("12345678-1234-1234-1234-123456789012");
        await using var reader = await connection.ExecuteReaderAsync(
            "SELECT $1::uuid as guid_val",
            Tuple.Create(expectedGuid)
        );

        Assert.True(await reader.ReadAsync());
        Assert.Equal(expectedGuid, reader.GetGuid(0));
    }

    [Fact]
    public async Task CanReadBytea()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var expectedBytes = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        await using var reader = await connection.ExecuteReaderAsync(
            "SELECT $1::bytea as bytes_val",
            Tuple.Create(expectedBytes)
        );

        Assert.True(await reader.ReadAsync());
        Assert.Equal(expectedBytes, reader.GetBytes(0));
    }

    [Fact]
    public async Task CanReadDecimal()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        await using var reader = await connection.ExecuteReaderAsync("SELECT 123.456::numeric as decimal_val");

        Assert.True(await reader.ReadAsync());
        Assert.Equal(123.456m, reader.GetDecimal(0));
    }

    [Fact]
    public async Task GetFieldValueWorksForDifferentTypes()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        await using var reader = await connection.ExecuteReaderAsync("SELECT 42::int as int_val, 'hello'::text as text_val");

        Assert.True(await reader.ReadAsync());
        Assert.Equal(42, reader.GetFieldValue<int>(0));
        Assert.Equal("hello", reader.GetFieldValue<string>(1));
    }

    [Fact]
    public async Task ThrowsWhenAccessingRowBeforeRead()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        await using var reader = await connection.ExecuteReaderAsync("SELECT 1");

        Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
    }

    [Fact]
    public async Task ThrowsWhenColumnIndexOutOfRange()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        await using var reader = await connection.ExecuteReaderAsync("SELECT 1, 2");

        Assert.True(await reader.ReadAsync());
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetValue(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetValue(-1));
    }

    [Fact]
    public async Task ThrowsWhenColumnNameNotFound()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        await using var reader = await connection.ExecuteReaderAsync("SELECT 1 as existing");

        Assert.True(await reader.ReadAsync());
        Assert.Throws<ArgumentException>(() => reader.GetValue("nonexistent"));
    }

    [Fact]
    public async Task ThrowsWhenAccessingDisposedReader()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var reader = await connection.ExecuteReaderAsync("SELECT 1");
        await reader.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await reader.ReadAsync());
    }

    [Fact]
    public async Task CanExecuteReaderMultipleTimes()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        // Execute the same query multiple times
        for (int i = 0; i < 5; i++)
        {
            await using var reader = await connection.ExecuteReaderAsync("SELECT 1 as value");
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
        }
    }

    [Fact]
    public async Task CanReadFromTable()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var tableName = $"reader_table_{Guid.NewGuid():N}";

        // Create and populate table
        await connection.QueryAsync($@"
            CREATE TABLE {tableName} (
                id SERIAL PRIMARY KEY,
                name TEXT NOT NULL,
                value INT
            )
        ");
        await connection.QueryAsync($"INSERT INTO {tableName} (name, value) VALUES ('a', 1), ('b', 2), ('c', 3)");

        try
        {
            await using var reader = await connection.ExecuteReaderAsync(
                $"SELECT name, value FROM {tableName} ORDER BY id"
            );

            var results = new List<(string Name, int Value)>();
            while (await reader.ReadAsync())
            {
                results.Add((reader.GetString(0)!, reader.GetInt32(1)));
            }

            Assert.Equal(3, results.Count);
            Assert.Equal(("a", 1), results[0]);
            Assert.Equal(("b", 2), results[1]);
            Assert.Equal(("c", 3), results[2]);
        }
        finally
        {
            await connection.QueryAsync($"DROP TABLE {tableName}");
        }
    }

    [Fact]
    public async Task CanReadWithParameterizedTableQuery()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var tableName = $"reader_param_{Guid.NewGuid():N}";

        // Create and populate table
        await connection.QueryAsync($@"
            CREATE TABLE {tableName} (
                id SERIAL PRIMARY KEY,
                category TEXT NOT NULL,
                amount INT
            )
        ");
        await connection.QueryAsync($"INSERT INTO {tableName} (category, amount) VALUES ('A', 10), ('B', 20), ('A', 30), ('B', 40)");

        try
        {
            await using var reader = await connection.ExecuteReaderAsync(
                $"SELECT category, amount FROM {tableName} WHERE category = $1 ORDER BY amount",
                Tuple.Create("A")
            );

            var results = new List<int>();
            while (await reader.ReadAsync())
            {
                Assert.Equal("A", reader.GetString(0));
                results.Add(reader.GetInt32(1));
            }

            Assert.Equal(2, results.Count);
            Assert.Equal(10, results[0]);
            Assert.Equal(30, results[1]);
        }
        finally
        {
            await connection.QueryAsync($"DROP TABLE {tableName}");
        }
    }
}
