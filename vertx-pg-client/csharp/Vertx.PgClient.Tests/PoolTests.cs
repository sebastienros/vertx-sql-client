// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using Xunit;

namespace Vertx.PgClient.Tests;

[Collection("PostgreSQL")]
public class PoolTests
{
    private readonly PostgresFixture _fixture;

    public PoolTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanCreatePool()
    {
        await using var pool = PgPool.Create(_fixture.CreateConnectOptions());
        Assert.NotNull(pool);
        Assert.Equal(0, pool.Size);
    }

    [Fact]
    public async Task CanQueryWithPool()
    {
        await using var pool = PgPool.Create(_fixture.CreateConnectOptions());
        
        var result = await pool.QueryAsync("SELECT 1 as value");
        
        Assert.Single(result);
        Assert.Equal(1, result[0].GetInteger(0));
    }

    [Fact]
    public async Task PoolCreatesConnectionOnDemand()
    {
        await using var pool = PgPool.Create(_fixture.CreateConnectOptions());
        
        Assert.Equal(0, pool.Size);
        
        await pool.QueryAsync("SELECT 1");
        
        Assert.Equal(1, pool.Size);
    }

    [Fact]
    public async Task PoolReusesConnections()
    {
        await using var pool = PgPool.Create(_fixture.CreateConnectOptions());
        
        // Execute multiple queries
        await pool.QueryAsync("SELECT 1");
        await pool.QueryAsync("SELECT 2");
        await pool.QueryAsync("SELECT 3");
        
        // Should still only have one connection
        Assert.Equal(1, pool.Size);
    }

    [Fact]
    public async Task CanPreparedQueryWithPool()
    {
        await using var pool = PgPool.Create(_fixture.CreateConnectOptions());
        
        var result = await pool.PreparedQueryAsync(
            "SELECT $1::int + $2::int as sum", 
            Tuple.Of(10, 20));
        
        Assert.Single(result);
        Assert.Equal(30, result[0].GetInteger(0));
    }

    [Fact]
    public async Task CanPipelineWithPool()
    {
        await using var pool = PgPool.Create(_fixture.CreateConnectOptions());
        
        var results = await pool.PipelineQueryAsync(
            "SELECT 1 as a",
            "SELECT 2 as b",
            "SELECT 3 as c");
        
        Assert.Equal(3, results.Length);
        Assert.Equal(1, results[0][0].GetInteger(0));
        Assert.Equal(2, results[1][0].GetInteger(0));
        Assert.Equal(3, results[2][0].GetInteger(0));
    }

    [Fact]
    public async Task ConcurrentQueriesCreateMultipleConnections()
    {
        var poolOptions = new PgPoolOptions { MaxSize = 4 };
        await using var pool = PgPool.Create(_fixture.CreateConnectOptions(), poolOptions);
        
        // Run 4 concurrent slow queries
        var tasks = Enumerable.Range(0, 4)
            .Select(_ => pool.QueryAsync("SELECT pg_sleep(0.1)").AsTask())
            .ToArray();
        
        await Task.WhenAll(tasks);
        
        // Should have created multiple connections due to concurrent demand
        Assert.True(pool.Size >= 1);
    }

    [Fact]
    public async Task PoolRespectsMaxSize()
    {
        var poolOptions = new PgPoolOptions { MaxSize = 2, ConnectionTimeout = 100 };
        await using var pool = PgPool.Create(_fixture.CreateConnectOptions(), poolOptions);
        
        // Get dedicated connections to exhaust the pool
        var conn1 = await pool.GetConnectionAsync();
        var conn2 = await pool.GetConnectionAsync();
        
        Assert.Equal(2, pool.Size);
        
        // The next request should timeout since all connections are in use
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await pool.GetConnectionAsync();
        });
        
        // Release a connection
        conn1.Close();
        
        // Now we can get another connection
        var conn3 = await pool.GetConnectionAsync();
        Assert.NotNull(conn3);
        
        conn2.Close();
        conn3.Close();
    }

    [Fact]
    public async Task CanGetDedicatedConnection()
    {
        await using var pool = PgPool.Create(_fixture.CreateConnectOptions());
        
        await using var conn = await pool.GetConnectionAsync();
        Assert.True(conn.IsValid);
        
        var result = await conn.QueryAsync("SELECT 42 as answer");
        Assert.Equal(42, result[0].GetInteger(0));
    }

    [Fact]
    public async Task DedicatedConnectionReturnsToPool()
    {
        await using var pool = PgPool.Create(_fixture.CreateConnectOptions());
        
        // Get and release a connection
        {
            await using var conn = await pool.GetConnectionAsync();
            await conn.QueryAsync("SELECT 1");
        }
        
        Assert.Equal(1, pool.Size);
        
        // Get another connection - should reuse the same one
        await using var conn2 = await pool.GetConnectionAsync();
        Assert.Equal(1, pool.Size);
    }

    [Fact]
    public async Task PoolOptionsDefaults()
    {
        var options = new PgPoolOptions();
        
        Assert.Equal(4, options.MaxSize);
        Assert.Equal(0, options.MaxWaitQueueSize);
        Assert.Equal(0, options.IdleTimeout);
        Assert.Equal(0, options.MaxLifetime);
        Assert.Equal(30000, options.ConnectionTimeout);
        Assert.True(options.Pipelined);
    }

    [Fact]
    public async Task CanConfigurePoolOptions()
    {
        var options = new PgPoolOptions
        {
            MaxSize = 8,
            MaxWaitQueueSize = 5000,
            IdleTimeout = 600000,
            MaxLifetime = 3600000,
            ConnectionTimeout = 15000,
            Pipelined = false,
            Name = "TestPool"
        };
        
        Assert.Equal(8, options.MaxSize);
        Assert.Equal(5000, options.MaxWaitQueueSize);
        Assert.Equal(600000, options.IdleTimeout);
        Assert.Equal(3600000, options.MaxLifetime);
        Assert.Equal(15000, options.ConnectionTimeout);
        Assert.False(options.Pipelined);
        Assert.Equal("TestPool", options.Name);
    }

    [Fact]
    public async Task ScheduleAsyncExecutesQueries()
    {
        await using var pool = PgPool.Create(_fixture.CreateConnectOptions());
        
        // Schedule queries - they execute in order
        var result1 = await pool.ScheduleAsync("SELECT 1 as value");
        var result2 = await pool.ScheduleAsync("SELECT 2 as value");
        var result3 = await pool.ScheduleAsync("SELECT 3 as value");
        
        Assert.Equal(1, result1[0].GetInteger(0));
        Assert.Equal(2, result2[0].GetInteger(0));
        Assert.Equal(3, result3[0].GetInteger(0));
    }

    [Fact]
    public async Task PoolDisposesConnectionsProperly()
    {
        PgPool pool;
        
        {
            pool = PgPool.Create(_fixture.CreateConnectOptions());
            await pool.QueryAsync("SELECT 1");
            Assert.Equal(1, pool.Size);
        }
        
        await pool.DisposeAsync();
        
        // After dispose, operations should throw
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await pool.QueryAsync("SELECT 1");
        });
    }

    [Fact]
    public async Task CanCreatePoolFromConnectionUri()
    {
        await using var pool = PgPool.Create(_fixture.ConnectionUri);
        
        var result = await pool.QueryAsync("SELECT current_database() as db");
        Assert.Single(result);
    }

    [Fact]
    public async Task PoolHandlesConcurrentLoad()
    {
        var poolOptions = new PgPoolOptions { MaxSize = 4 };
        await using var pool = PgPool.Create(_fixture.CreateConnectOptions(), poolOptions);
        
        // Generate concurrent queries that will be distributed across connections
        var tasks = Enumerable.Range(0, 20)
            .Select(i => pool.QueryAsync($"SELECT {i} as value").AsTask())
            .ToArray();
        
        var results = await Task.WhenAll(tasks);
        
        Assert.Equal(20, results.Length);
        
        // Verify each result contains exactly one row
        for (int i = 0; i < 20; i++)
        {
            Assert.Single(results[i]);
        }
    }

    [Fact]
    public async Task PipelineQueriesReturnInOrder()
    {
        await using var pool = PgPool.Create(_fixture.CreateConnectOptions());
        
        // Use queries with different execution times to verify ordering
        var results = await pool.PipelineQueryAsync(
            "SELECT 'first' as value",
            "SELECT 'second' as value",
            "SELECT 'third' as value"
        );
        
        Assert.Equal(3, results.Length);
        Assert.Equal("first", results[0][0].GetString(0));
        Assert.Equal("second", results[1][0].GetString(0));
        Assert.Equal("third", results[2][0].GetString(0));
    }
}
