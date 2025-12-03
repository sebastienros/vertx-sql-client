// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Npgsql;

namespace Vertx.PgClient.Benchmarks;

/// <summary>
/// Throughput benchmark measuring queries per second using Npgsql for comparison.
/// </summary>
[SimpleJob(RunStrategy.Monitoring, warmupCount: 1, iterationCount: 1)]
public class NpgsqlThroughputBenchmarks
{
    private PostgresFixture _fixture = null!;
    private NpgsqlDataSource _dataSource = null!;

    // Number of connections in the pool
    private int Connections;
    private const int DurationSeconds = 10;
    private int ConcurrencyLevel;

    [GlobalSetup]
    public async Task Setup()
    {
        var processorCount = Environment.ProcessorCount;
        Connections =  processorCount;
        ConcurrencyLevel = processorCount * 16;

        _fixture = new PostgresFixture();
        await _fixture.InitializeAsync();

        // Configure connection string with pool size
        var connectionString = _fixture.ConnectionString + $";Maximum Pool Size={Connections}";
        _dataSource = NpgsqlDataSource.Create(connectionString);

        // Warmup: run a few queries to establish connections
        for (int i = 0; i < 100; i++)
        {
            await using var cmd = _dataSource.CreateCommand("SELECT id, message FROM fortune");
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) { }
        }
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _dataSource.DisposeAsync();
        await _fixture.DisposeAsync();
    }

    /// <summary>
    /// Measures queries per second for fortune queries over 5 seconds using Npgsql.
    /// Maintains a fixed concurrency level by immediately starting a new query when one completes.
    /// </summary>
    [Benchmark(Description = "Npgsql: fortunes throughput")]
    public async Task<double> NpgsqlFortunesThroughput()
    {
        long completedQueries = 0;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DurationSeconds));
        var token = cts.Token;

        // Use a semaphore to maintain fixed concurrency
        var tasks = new List<Task>();
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < ConcurrencyLevel; i++)
        {
            var task = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        await using var cmd = _dataSource.CreateCommand("SELECT id, message FROM fortune");
                        await using var reader = await cmd.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            _ = reader.GetInt32(0);
                            _ = reader.GetString(1);
                        }
                        Interlocked.Increment(ref completedQueries);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when duration expires
                }
            }, CancellationToken.None);

            tasks.Add(task);
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
