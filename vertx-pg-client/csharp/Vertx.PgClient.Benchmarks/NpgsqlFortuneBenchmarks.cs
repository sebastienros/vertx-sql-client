// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using BenchmarkDotNet.Attributes;
using Npgsql;

namespace Vertx.PgClient.Benchmarks;

/// <summary>
/// Benchmarks for Fortune queries using Npgsql for comparison.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class NpgsqlFortuneBenchmarks
{
    private PostgresFixture _fixture = null!;
    private NpgsqlConnection _connection = null!;
    private NpgsqlDataSource _dataSource = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _fixture = new PostgresFixture();
        await _fixture.InitializeAsync();

        _connection = new NpgsqlConnection(_fixture.ConnectionString);
        await _connection.OpenAsync();

        _dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _connection.DisposeAsync();
        await _dataSource.DisposeAsync();
        await _fixture.DisposeAsync();
    }

    /// <summary>
    /// Simple query to fetch all fortunes.
    /// </summary>
    [Benchmark(Description = "Npgsql: SELECT all fortunes (simple query)")]
    public async Task<int> SelectAllFortunes_SimpleQuery()
    {
        await using var cmd = new NpgsqlCommand("SELECT id, message FROM fortune", _connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        int count = 0;
        while (await reader.ReadAsync())
        {
            _ = reader.GetInt32(0);
            _ = reader.GetString(1);
            count++;
        }
        return count;
    }

    /// <summary>
    /// Prepared query to fetch all fortunes.
    /// </summary>
    [Benchmark(Description = "Npgsql: SELECT all fortunes (prepared query)")]
    public async Task<int> SelectAllFortunes_PreparedQuery()
    {
        await using var cmd = new NpgsqlCommand("SELECT id, message FROM fortune", _connection);
        await cmd.PrepareAsync();
        await using var reader = await cmd.ExecuteReaderAsync();

        int count = 0;
        while (await reader.ReadAsync())
        {
            _ = reader.GetInt32(0);
            _ = reader.GetString(1);
            count++;
        }
        return count;
    }

    /// <summary>
    /// Fetch a single fortune by ID using prepared query.
    /// </summary>
    [Benchmark(Description = "Npgsql: SELECT fortune by ID (prepared query)")]
    public async Task<string?> SelectFortuneById_PreparedQuery()
    {
        await using var cmd = new NpgsqlCommand("SELECT id, message FROM fortune WHERE id = $1", _connection);
        cmd.Parameters.AddWithValue(1);
        await cmd.PrepareAsync();
        await using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return reader.GetString(1);
        }
        return null;
    }

    /// <summary>
    /// DataSource-based query for all fortunes.
    /// </summary>
    [Benchmark(Description = "Npgsql: SELECT all fortunes (DataSource)")]
    public async Task<int> SelectAllFortunes_DataSource()
    {
        await using var cmd = _dataSource.CreateCommand("SELECT id, message FROM fortune");
        await using var reader = await cmd.ExecuteReaderAsync();

        int count = 0;
        while (await reader.ReadAsync())
        {
            _ = reader.GetInt32(0);
            _ = reader.GetString(1);
            count++;
        }
        return count;
    }

    /// <summary>
    /// Concurrent queries using DataSource.
    /// </summary>
    [Benchmark(Description = "Npgsql: 10 concurrent fortune queries (DataSource)")]
    public async Task<int> ConcurrentFortuneQueries_DataSource()
    {
        var tasks = new Task<int>[10];
        for (int i = 0; i < 10; i++)
        {
            tasks[i] = ExecuteFortuneQueryAsync();
        }

        await Task.WhenAll(tasks);

        int total = 0;
        foreach (var task in tasks)
        {
            total += task.Result;
        }
        return total;

        async Task<int> ExecuteFortuneQueryAsync()
        {
            await using var cmd = _dataSource.CreateCommand("SELECT id, message FROM fortune");
            await using var reader = await cmd.ExecuteReaderAsync();

            int count = 0;
            while (await reader.ReadAsync())
            {
                _ = reader.GetInt32(0);
                _ = reader.GetString(1);
                count++;
            }
            return count;
        }
    }
}
