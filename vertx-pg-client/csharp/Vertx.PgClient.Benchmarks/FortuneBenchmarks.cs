// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using BenchmarkDotNet.Attributes;

namespace Vertx.PgClient.Benchmarks;

/// <summary>
/// Benchmarks for Fortune queries matching TechEmpower benchmark patterns.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class FortuneBenchmarks
{
    private PostgresFixture _fixture = null!;
    private IPgConnection _connection = null!;
    private PgPool _pool = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _fixture = new PostgresFixture();
        await _fixture.InitializeAsync();

        var options = _fixture.CreateConnectOptions()
            .SetCachePreparedStatements(true);

        _connection = await PgConnection.ConnectAsync(options);

        _pool = PgPool.Create(options, new PgPoolOptions
        {
            MaxSize = 16,
            Pipelined = true
        });
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _connection.DisposeAsync();
        await _pool.DisposeAsync();
        await _fixture.DisposeAsync();
    }

    /// <summary>
    /// Simple query to fetch all fortunes - matches TechEmpower "fortunes" test.
    /// </summary>
    [Benchmark(Description = "SELECT all fortunes (simple query)")]
    public async Task<int> SelectAllFortunes_SimpleQuery()
    {
        var result = await _connection.QueryAsync("SELECT id, message FROM fortune");
        return result.Count;
    }

    /// <summary>
    /// Prepared query to fetch all fortunes.
    /// </summary>
    [Benchmark(Description = "SELECT all fortunes (prepared query)")]
    public async Task<int> SelectAllFortunes_PreparedQuery()
    {
        var result = await _connection.PreparedQueryAsync("SELECT id, message FROM fortune");
        return result.Count;
    }

    /// <summary>
    /// Fetch a single fortune by ID using prepared query - common pattern.
    /// </summary>
    [Benchmark(Description = "SELECT fortune by ID (prepared query)")]
    public async Task<string?> SelectFortuneById_PreparedQuery()
    {
        var result = await _connection.PreparedQueryAsync(
            "SELECT id, message FROM fortune WHERE id = $1",
            Tuple.Create(1));
        return result.Count > 0 ? result[0].GetValue(1).GetString() : null;
    }

    /// <summary>
    /// Pool-based query for all fortunes.
    /// </summary>
    [Benchmark(Description = "SELECT all fortunes (pool)")]
    public async Task<int> SelectAllFortunes_Pool()
    {
        var result = await _pool.QueryAsync("SELECT id, message FROM fortune");
        return result.Count;
    }

    /// <summary>
    /// Pool-based prepared query for all fortunes.
    /// </summary>
    [Benchmark(Description = "SELECT all fortunes (pool, prepared)")]
    public async Task<int> SelectAllFortunes_PoolPrepared()
    {
        var result = await _pool.PreparedQueryAsync("SELECT id, message FROM fortune");
        return result.Count;
    }

    /// <summary>
    /// Concurrent queries using pool multiplexing.
    /// </summary>
    [Benchmark(Description = "10 concurrent fortune queries (pool)")]
    public async Task<int> ConcurrentFortuneQueries_Pool()
    {
        var tasks = new Task<RowSet>[10];
        for (int i = 0; i < 10; i++)
        {
            tasks[i] = _pool.QueryAsync("SELECT id, message FROM fortune").AsTask();
        }

        await Task.WhenAll(tasks);

        int total = 0;
        foreach (var task in tasks)
        {
            total += task.Result.Count;
        }
        return total;
    }

    /// <summary>
    /// Pipelined queries using ScheduleAsync for maximum throughput.
    /// </summary>
    [Benchmark(Description = "10 pipelined fortune queries")]
    public async Task<int> PipelinedFortuneQueries()
    {
        var tasks = new Task<RowSet>[10];
        for (int i = 0; i < 10; i++)
        {
            tasks[i] = _pool.ScheduleAsync("SELECT id, message FROM fortune");
        }

        await Task.WhenAll(tasks);

        int total = 0;
        foreach (var task in tasks)
        {
            total += task.Result.Count;
        }
        return total;
    }
}
