// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using BenchmarkDotNet.Attributes;

namespace Vertx.PgClient.Benchmarks;

/// <summary>
/// Benchmarks for World queries matching TechEmpower benchmark patterns.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class WorldBenchmarks
{
    private PostgresFixture _fixture = null!;
    private IPgConnection _connection = null!;
    private PgPool _pool = null!;
    private Random _random = null!;

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

        _random = new Random(42); // Fixed seed for reproducibility
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _connection.DisposeAsync();
        await _pool.DisposeAsync();
        await _fixture.DisposeAsync();
    }

    /// <summary>
    /// Single world query - matches TechEmpower "db" test.
    /// </summary>
    [Benchmark(Description = "Single world query (prepared)")]
    public async Task<int> SingleWorldQuery()
    {
        int id = _random.Next(1, 10001);
        var result = await _connection.PreparedQueryAsync(
            "SELECT id, randomnumber FROM world WHERE id = $1",
            Tuple.Create(id));
        return result.Count > 0 ? result[0].GetValue(1).GetInteger() : 0;
    }

    /// <summary>
    /// Multiple world queries - matches TechEmpower "queries" test with 20 queries.
    /// </summary>
    [Benchmark(Description = "20 world queries (sequential)")]
    public async Task<int> MultipleWorldQueries_Sequential()
    {
        int total = 0;
        for (int i = 0; i < 20; i++)
        {
            int id = _random.Next(1, 10001);
            var result = await _connection.PreparedQueryAsync(
                "SELECT id, randomnumber FROM world WHERE id = $1",
                Tuple.Create(id));
            if (result.Count > 0)
            {
                total += result[0].GetValue(1).GetInteger();
            }
        }
        return total;
    }

    /// <summary>
    /// Multiple world queries using pool - concurrent execution.
    /// </summary>
    [Benchmark(Description = "20 world queries (pool, concurrent)")]
    public async Task<int> MultipleWorldQueries_PoolConcurrent()
    {
        var tasks = new Task<RowSet>[20];
        for (int i = 0; i < 20; i++)
        {
            int id = _random.Next(1, 10001);
            tasks[i] = _pool.PreparedQueryAsync(
                "SELECT id, randomnumber FROM world WHERE id = $1",
                Tuple.Create(id));
        }

        await Task.WhenAll(tasks);

        int total = 0;
        foreach (var task in tasks)
        {
            if (task.Result.Count > 0)
            {
                total += task.Result[0].GetValue(1).GetInteger();
            }
        }
        return total;
    }

    /// <summary>
    /// Multiple world queries using pipelining for maximum throughput.
    /// </summary>
    [Benchmark(Description = "20 world queries (pipelined)")]
    public async Task<int> MultipleWorldQueries_Pipelined()
    {
        var queries = new string[20];
        for (int i = 0; i < 20; i++)
        {
            int id = _random.Next(1, 10001);
            queries[i] = $"SELECT id, randomnumber FROM world WHERE id = {id}";
        }

        var results = await _connection.PipelineQueryAsync(queries);

        int total = 0;
        foreach (var result in results)
        {
            if (result.Count > 0)
            {
                total += result[0].GetValue(1).GetInteger();
            }
        }
        return total;
    }

    /// <summary>
    /// Update world query - matches TechEmpower "updates" test pattern.
    /// </summary>
    [Benchmark(Description = "Single world update (prepared)")]
    public async Task<int> SingleWorldUpdate()
    {
        int id = _random.Next(1, 10001);
        int newRandomNumber = _random.Next(1, 10001);

        // Read
        var result = await _connection.PreparedQueryAsync(
            "SELECT id, randomnumber FROM world WHERE id = $1",
            Tuple.Create(id));

        // Update
        await _connection.PreparedQueryAsync(
            "UPDATE world SET randomnumber = $1 WHERE id = $2",
            Tuple.Create(newRandomNumber, id));

        return result.Count;
    }
}
