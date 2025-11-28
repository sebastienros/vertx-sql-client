// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using BenchmarkDotNet.Attributes;
using Npgsql;

namespace Vertx.PgClient.Benchmarks;

/// <summary>
/// Benchmarks for World queries using Npgsql for comparison.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class NpgsqlWorldBenchmarks
{
    private PostgresFixture _fixture = null!;
    private NpgsqlConnection _connection = null!;
    private NpgsqlDataSource _dataSource = null!;
    private Random _random = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _fixture = new PostgresFixture();
        await _fixture.InitializeAsync();

        _connection = new NpgsqlConnection(_fixture.ConnectionString);
        await _connection.OpenAsync();

        _dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);

        _random = new Random(42); // Fixed seed for reproducibility
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _connection.DisposeAsync();
        await _dataSource.DisposeAsync();
        await _fixture.DisposeAsync();
    }

    /// <summary>
    /// Single world query - matches TechEmpower "db" test.
    /// </summary>
    [Benchmark(Description = "Npgsql: Single world query (prepared)")]
    public async Task<int> SingleWorldQuery()
    {
        int id = _random.Next(1, 10001);

        await using var cmd = new NpgsqlCommand("SELECT id, randomnumber FROM world WHERE id = $1", _connection);
        cmd.Parameters.AddWithValue(id);
        await cmd.PrepareAsync();
        await using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return reader.GetInt32(1);
        }
        return 0;
    }

    /// <summary>
    /// Multiple world queries - matches TechEmpower "queries" test with 20 queries.
    /// </summary>
    [Benchmark(Description = "Npgsql: 20 world queries (sequential)")]
    public async Task<int> MultipleWorldQueries_Sequential()
    {
        int total = 0;
        for (int i = 0; i < 20; i++)
        {
            int id = _random.Next(1, 10001);

            await using var cmd = new NpgsqlCommand("SELECT id, randomnumber FROM world WHERE id = $1", _connection);
            cmd.Parameters.AddWithValue(id);
            await using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                total += reader.GetInt32(1);
            }
        }
        return total;
    }

    /// <summary>
    /// Multiple world queries using DataSource - concurrent execution.
    /// </summary>
    [Benchmark(Description = "Npgsql: 20 world queries (DataSource, concurrent)")]
    public async Task<int> MultipleWorldQueries_DataSourceConcurrent()
    {
        var tasks = new Task<int>[20];
        for (int i = 0; i < 20; i++)
        {
            int id = _random.Next(1, 10001);
            tasks[i] = ExecuteWorldQueryAsync(id);
        }

        await Task.WhenAll(tasks);

        int total = 0;
        foreach (var task in tasks)
        {
            total += task.Result;
        }
        return total;

        async Task<int> ExecuteWorldQueryAsync(int id)
        {
            await using var cmd = _dataSource.CreateCommand("SELECT id, randomnumber FROM world WHERE id = $1");
            cmd.Parameters.AddWithValue(id);
            await using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return reader.GetInt32(1);
            }
            return 0;
        }
    }

    /// <summary>
    /// Update world query - matches TechEmpower "updates" test pattern.
    /// </summary>
    [Benchmark(Description = "Npgsql: Single world update (prepared)")]
    public async Task<int> SingleWorldUpdate()
    {
        int id = _random.Next(1, 10001);
        int newRandomNumber = _random.Next(1, 10001);

        // Read
        await using var readCmd = new NpgsqlCommand("SELECT id, randomnumber FROM world WHERE id = $1", _connection);
        readCmd.Parameters.AddWithValue(id);
        await using var reader = await readCmd.ExecuteReaderAsync();

        int count = 0;
        if (await reader.ReadAsync())
        {
            count = 1;
        }
        await reader.CloseAsync();

        // Update
        await using var updateCmd = new NpgsqlCommand("UPDATE world SET randomnumber = $1 WHERE id = $2", _connection);
        updateCmd.Parameters.AddWithValue(newRandomNumber);
        updateCmd.Parameters.AddWithValue(id);
        await updateCmd.ExecuteNonQueryAsync();

        return count;
    }
}
