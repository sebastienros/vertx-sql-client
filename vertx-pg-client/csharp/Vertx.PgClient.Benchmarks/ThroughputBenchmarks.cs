// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace Vertx.PgClient.Benchmarks;

/// <summary>
/// Throughput benchmark measuring queries per second for pipelined fortune queries.
/// </summary>
[SimpleJob(RunStrategy.Monitoring, warmupCount: 1, iterationCount: 1)]
public class ThroughputBenchmarks
{
    private PostgresFixture _fixture = null!;
    private PgPool _pool = null!;

    private const int DurationSeconds = 10;
    private const int ConcurrencyLevel = 64;

    [GlobalSetup]
    public async Task Setup()
    {
        _fixture = new PostgresFixture();
        await _fixture.InitializeAsync();

        var options = _fixture.CreateConnectOptions()
            .SetCachePreparedStatements(true);

        _pool = PgPool.Create(options, new PgPoolOptions
        {
            MaxSize = 16,
            Pipelined = true
        });

        // Warmup: run a few queries to establish connections
        for (int i = 0; i < 100; i++)
        {
            await _pool.ScheduleAsync("SELECT id, message FROM fortune");
        }
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _pool.DisposeAsync();
        await _fixture.DisposeAsync();
    }

    /// <summary>
    /// Measures queries per second for pipelined fortune queries over 5 seconds.
    /// Maintains a fixed concurrency level by immediately starting a new query when one completes.
    /// </summary>
    [Benchmark(Description = "Pipelined fortunes throughput (5s)")]
    public async Task<double> PipelinedFortunesThroughput()
    {
        long completedQueries = 0;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DurationSeconds));
        var token = cts.Token;

        // Use a semaphore to maintain fixed concurrency
        var semaphore = new SemaphoreSlim(ConcurrencyLevel, ConcurrencyLevel);
        var tasks = new List<Task>();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            while (!token.IsCancellationRequested)
            {
                await semaphore.WaitAsync(token);

                var task = Task.Run(async () =>
                {
                    try
                    {
                        await _pool.ScheduleAsync("SELECT id, message FROM fortune");
                        Interlocked.Increment(ref completedQueries);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, CancellationToken.None);

                tasks.Add(task);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when duration expires
        }

        // Wait for all in-flight queries to complete
        await Task.WhenAll(tasks);
        stopwatch.Stop();

        double queriesPerSecond = completedQueries / stopwatch.Elapsed.TotalSeconds;

        Console.WriteLine();
        Console.WriteLine($"  Duration:          {stopwatch.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine($"  Completed queries: {completedQueries:N0}");
        Console.WriteLine($"  Queries/second:    {queriesPerSecond:N0}");
        Console.WriteLine();

        return queriesPerSecond;
    }
}
