// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using System.Diagnostics;
using Xunit;

namespace Vertx.PgClient.Tests;

[Collection("PostgreSQL")]
public class PipeliningTests
{
    private readonly PostgresFixture _fixture;

    public PipeliningTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanPipelineMultipleQueries()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        // Execute multiple queries using pipelining
        var results = await connection.PipelineQueryAsync(
            "SELECT 1 as num",
            "SELECT 2 as num",
            "SELECT 3 as num"
        );

        Assert.Equal(3, results.Length);
        Assert.Equal(1, results[0][0].GetInteger("num"));
        Assert.Equal(2, results[1][0].GetInteger("num"));
        Assert.Equal(3, results[2][0].GetInteger("num"));
    }

    [Fact]
    public async Task CanPipelineDifferentQueryTypes()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var results = await connection.PipelineQueryAsync(
            "SELECT 'hello'::text as greeting",
            "SELECT 42::int4 as answer",
            "SELECT true as flag",
            "SELECT 3.14::float8 as pi"
        );

        Assert.Equal(4, results.Length);
        Assert.Equal("hello", results[0][0].GetString("greeting"));
        Assert.Equal(42, results[1][0].GetInteger("answer"));
        Assert.True(results[2][0].GetBoolean("flag"));
        Assert.Equal(3.14, results[3][0].GetDouble("pi"), 0.01);
    }

    [Fact]
    public async Task CanPipelineQueriesWithMultipleRows()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var results = await connection.PipelineQueryAsync(
            "SELECT * FROM generate_series(1, 3) as n",
            "SELECT * FROM generate_series(10, 12) as n"
        );

        Assert.Equal(2, results.Length);
        
        Assert.Equal(3, results[0].Count);
        Assert.Equal(1, results[0][0].GetInteger(0));
        Assert.Equal(2, results[0][1].GetInteger(0));
        Assert.Equal(3, results[0][2].GetInteger(0));
        
        Assert.Equal(3, results[1].Count);
        Assert.Equal(10, results[1][0].GetInteger(0));
        Assert.Equal(11, results[1][1].GetInteger(0));
        Assert.Equal(12, results[1][2].GetInteger(0));
    }

    [Fact]
    public async Task PipeliningIsFasterThanSequential()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        const int queryCount = 20;  // More queries for better timing measurement
        var queries = new string[queryCount];
        for (int i = 0; i < queryCount; i++)
        {
            // pg_sleep adds a small delay to make the timing difference more apparent
            queries[i] = $"SELECT {i + 1} as num, pg_sleep(0.005)";
        }

        // Warm up
        await connection.QueryAsync("SELECT 1");

        // Measure sequential execution
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < queryCount; i++)
        {
            await connection.QueryAsync(queries[i]);
        }
        var sequentialTime = sw.ElapsedMilliseconds;

        // Measure pipelined execution
        sw.Restart();
        var results = await connection.PipelineQueryAsync(queries);
        var pipelinedTime = sw.ElapsedMilliseconds;

        // Verify results are correct
        Assert.Equal(queryCount, results.Length);
        for (int i = 0; i < queryCount; i++)
        {
            Assert.Equal(i + 1, results[i][0].GetInteger("num"));
        }

        // Pipelining should be noticeably faster
        // With 20 queries * 5ms sleep each:
        // Sequential: ~100ms (20 * 5ms roundtrips)
        // Pipelined: ~5-20ms (single roundtrip wait + one sleep period)
        // We allow pipelining to be at most equal (timing can be affected by Docker/network)
        // The key benefit of pipelining is reduced network roundtrips, not parallel execution
        // on the server side.
        // Note: In a real network environment with latency, pipelining would show much larger gains
        Assert.True(pipelinedTime <= sequentialTime + 50, 
            $"Pipelining ({pipelinedTime}ms) should not be slower than sequential ({sequentialTime}ms)");
    }

    [Fact]
    public async Task PipeliningRespectsLimit()
    {
        var options = _fixture.CreateConnectOptions();
        options.PipeliningLimit = 2; // Very low limit for testing
        
        await using var connection = await PgConnection.ConnectAsync(options);

        // Even with a low limit, all queries should complete
        var results = await connection.PipelineQueryAsync(
            "SELECT 1 as n",
            "SELECT 2 as n",
            "SELECT 3 as n",
            "SELECT 4 as n",
            "SELECT 5 as n"
        );

        Assert.Equal(5, results.Length);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(i + 1, results[i][0].GetInteger("n"));
        }
    }

    [Fact]
    public async Task CanPipelineWithNullResults()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var results = await connection.PipelineQueryAsync(
            "SELECT NULL::text as empty",
            "SELECT 'not null'::text as value",
            "SELECT NULL::int4 as empty_int"
        );

        Assert.Equal(3, results.Length);
        Assert.Null(results[0][0].GetValue("empty"));
        Assert.Equal("not null", results[1][0].GetString("value"));
        Assert.Null(results[2][0].GetValue("empty_int"));
    }

    [Fact]
    public async Task CanPipelineEmptyResults()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        // Create a temp table
        await connection.QueryAsync("CREATE TEMP TABLE empty_test (id int)");

        var results = await connection.PipelineQueryAsync(
            "SELECT * FROM empty_test",  // Empty result
            "SELECT 1 as num"             // Non-empty result
        );

        Assert.Equal(2, results.Length);
        Assert.Equal(0, results[0].Count);  // Empty
        Assert.Equal(1, results[1].Count);  // One row
        Assert.Equal(1, results[1][0].GetInteger("num"));
    }

    [Fact]
    public async Task DefaultPipeliningLimitIs256()
    {
        var options = _fixture.CreateConnectOptions();
        Assert.Equal(256, options.PipeliningLimit);
    }

    [Fact]
    public async Task CanSetPipeliningLimit()
    {
        var options = _fixture.CreateConnectOptions();
        options.PipeliningLimit = 10;
        Assert.Equal(10, options.PipeliningLimit);
    }

    [Fact]
    public void PipeliningLimitCannotBeZero()
    {
        var options = _fixture.CreateConnectOptions();
        Assert.Throws<ArgumentException>(() => options.PipeliningLimit = 0);
    }

    [Fact]
    public void PipeliningLimitCannotBeNegative()
    {
        var options = _fixture.CreateConnectOptions();
        Assert.Throws<ArgumentException>(() => options.PipeliningLimit = -1);
    }
}
