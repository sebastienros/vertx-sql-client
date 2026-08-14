/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using Apex.PgClient;
using Apex.SqlClient;
using BenchmarkDotNet.Attributes;
using Npgsql;

namespace Apex.DriverBenchmarks;

[MemoryDiagnoser]
public class PipeliningBenchmarks
{
  private PgConnection _apex = null!;
  private ISqlPreparedStatement _apexStatement = null!;
  private Task<SqlRowSet>[] _apexPending = null!;
  private NpgsqlConnection _npgsql = null!;
  private NpgsqlBatch _npgsqlBatch = null!;

  [Params(1, 16, 64, 256)]
  public int Depth { get; set; }

  [GlobalSetup]
  public async Task SetupAsync()
  {
    string connectionString =
      Environment.GetEnvironmentVariable("APEX_PG_CONNECTION_STRING") ??
      throw new InvalidOperationException(
        "Set APEX_PG_CONNECTION_STRING before running database benchmarks.");
    NpgsqlConnectionStringBuilder builder = new(connectionString);
    string username = builder.Username ??
      throw new InvalidOperationException("The benchmark connection string requires Username.");
    _apex = await Apex.PgClient.PgClient.ConnectAsync(new PgConnectOptions
    {
      Host = builder.Host ??
        throw new InvalidOperationException("The benchmark connection string requires Host."),
      Port = builder.Port,
      Database = builder.Database ?? username,
      Username = username,
      Password = builder.Password ?? string.Empty,
      PipeliningLimit = 256,
    });
    _apexStatement = await _apex.PrepareAsync("SELECT 1::int4");
    _apexPending = new Task<SqlRowSet>[Depth];

    _npgsql = new NpgsqlConnection(builder.ConnectionString);
    await _npgsql.OpenAsync();
    _npgsqlBatch = new NpgsqlBatch(_npgsql);
    for (int i = 0; i < Depth; i++)
    {
      _npgsqlBatch.BatchCommands.Add(
        new NpgsqlBatchCommand("SELECT 1::int4"));
    }

    await _npgsqlBatch.PrepareAsync();
  }

  [GlobalCleanup]
  public async Task CleanupAsync()
  {
    await _apexStatement.DisposeAsync();
    await _apex.DisposeAsync();
    await _npgsqlBatch.DisposeAsync();
    await _npgsql.DisposeAsync();
  }

  [Benchmark]
  public async Task<int> ApexPipelineAsync()
  {
    for (int i = 0; i < _apexPending.Length; i++)
    {
      _apexPending[i] = _apexStatement.QueryAsync().AsTask();
    }

    SqlRowSet[] results = await Task.WhenAll(_apexPending);
    int sum = 0;
    foreach (SqlRowSet rows in results)
    {
      sum += rows[0].Get<int>(0);
    }

    return sum;
  }

  [Benchmark]
  public async Task<int> NpgsqlBatchAsync()
  {
    await using NpgsqlDataReader reader =
      await _npgsqlBatch.ExecuteReaderAsync();
    int sum = 0;
    do
    {
      if (!await reader.ReadAsync())
      {
        throw new InvalidOperationException("Npgsql batch result is empty.");
      }

      sum += reader.GetInt32(0);
    }
    while (await reader.NextResultAsync());

    return sum;
  }
}
