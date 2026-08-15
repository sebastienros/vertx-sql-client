/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Apex.PgClient;
using Apex.SqlClient;
using Npgsql;

string driver = args.ElementAtOrDefault(0) ?? "apex";
string workload =
  Environment.GetEnvironmentVariable("APEX_BENCH_WORKLOAD") ?? "query";
int fetchSize = int.Parse(
  Environment.GetEnvironmentVariable("APEX_BENCH_FETCH_SIZE") ?? "16");
int rowCount = int.Parse(
  Environment.GetEnvironmentVariable("APEX_BENCH_ROW_COUNT") ?? "100");
int pipelineDepth = int.Parse(
  Environment.GetEnvironmentVariable("APEX_BENCH_PIPELINE_DEPTH") ?? "64");
int concurrency = int.Parse(
  Environment.GetEnvironmentVariable("APEX_BENCH_CONCURRENCY") ?? "16");
TimeSpan warmup = TimeSpan.FromSeconds(double.Parse(
  Environment.GetEnvironmentVariable("APEX_BENCH_WARMUP_SECONDS") ?? "2"));
TimeSpan duration = TimeSpan.FromSeconds(double.Parse(
  Environment.GetEnvironmentVariable("APEX_BENCH_DURATION_SECONDS") ?? "10"));
string connectionString =
  Environment.GetEnvironmentVariable("APEX_PG_CONNECTION_STRING") ??
  throw new InvalidOperationException("Set APEX_PG_CONNECTION_STRING.");

IQueryRunner[] runners = await Task.WhenAll(
  Enumerable.Range(0, concurrency)
    .Select(_ => CreateRunnerAsync(
      driver,
      workload,
      fetchSize,
      rowCount,
      pipelineDepth,
      connectionString).AsTask()));
try
{
  await RunPhaseAsync(driver, runners, warmup, record: false);
  GC.Collect();
  GC.WaitForPendingFinalizers();
  GC.Collect();
  long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
  int[] collectionsBefore = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
  HarnessResult result = await RunPhaseAsync(
    driver,
    runners,
    duration,
    record: true);
  result = result with
  {
    AllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore,
    Gen0Collections = GC.CollectionCount(0) - collectionsBefore[0],
    Gen1Collections = GC.CollectionCount(1) - collectionsBefore[1],
    Gen2Collections = GC.CollectionCount(2) - collectionsBefore[2],
  };
  Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
}
finally
{
  foreach (IQueryRunner runner in runners)
  {
    await runner.DisposeAsync();
  }
}

static async Task<HarnessResult> RunPhaseAsync(
  string driver,
  IQueryRunner[] runners,
  TimeSpan duration,
  bool record)
{
  ConcurrentBag<long> latencies = [];
  long operations = 0;
  using CancellationTokenSource stop = new(duration);
  Task[] workers = runners
    .Select(runner => RunWorkerAsync(
      runner,
      stop.Token,
      record ? latencies : null,
      count => Interlocked.Add(ref operations, count)))
    .ToArray();
  Stopwatch elapsed = Stopwatch.StartNew();
  await Task.WhenAll(workers);
  elapsed.Stop();
  long[] ordered = latencies.Order().ToArray();
  return new HarnessResult(
    driver,
    runners.Length,
    operations,
    elapsed.Elapsed.TotalSeconds,
    operations / elapsed.Elapsed.TotalSeconds,
    Percentile(ordered, 0.50),
    Percentile(ordered, 0.95),
    Percentile(ordered, 0.99),
    0,
    0,
    0,
    0,
    Environment.Version.ToString(),
    System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString());
}

static async Task RunWorkerAsync(
  IQueryRunner runner,
  CancellationToken cancellationToken,
  ConcurrentBag<long>? latencies,
  Action<int> completed)
{
  while (!cancellationToken.IsCancellationRequested)
  {
    long started = Stopwatch.GetTimestamp();
    try
    {
      await runner.QueryAsync(cancellationToken);
    }

    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      break;
    }

    latencies?.Add(Stopwatch.GetTimestamp() - started);
    completed(runner.OperationsPerInvocation);
  }
}

static ValueTask<IQueryRunner> CreateRunnerAsync(
  string driver,
  string workload,
  int fetchSize,
  int rowCount,
  int pipelineDepth,
  string connectionString) =>
  driver.ToLowerInvariant() switch
  {
    "apex" => WrapAsync(ApexQueryRunner.CreateAsync(
      workload,
      fetchSize,
      rowCount,
      pipelineDepth,
      connectionString)),
    "npgsql" => WrapAsync(NpgsqlQueryRunner.CreateAsync(
      workload,
      rowCount,
      pipelineDepth,
      connectionString)),
    _ => throw new ArgumentException($"Unknown driver '{driver}'."),
  };

static async ValueTask<IQueryRunner> WrapAsync<T>(ValueTask<T> runner)
  where T : IQueryRunner =>
  await runner;

static double Percentile(long[] ordered, double percentile)
{
  if (ordered.Length == 0)
  {
    return 0;
  }

  int index = Math.Clamp(
    (int)Math.Ceiling(percentile * ordered.Length) - 1,
    0,
    ordered.Length - 1);
  return ordered[index] * 1000d / Stopwatch.Frequency;
}

internal interface IQueryRunner : IAsyncDisposable
{
  int OperationsPerInvocation { get; }

  ValueTask QueryAsync(CancellationToken cancellationToken);
}

internal sealed class ApexQueryRunner(
  PgConnection connection,
  string workload,
  int fetchSize,
  string streamSql,
  int rowCount,
  int expectedSum,
  int pipelineDepth,
  ISqlPreparedStatement? pipelineStatement) : IQueryRunner
{
  public static async ValueTask<ApexQueryRunner> CreateAsync(
    string workload,
    int fetchSize,
    int rowCount,
    int pipelineDepth,
    string connectionString)
  {
    NpgsqlConnectionStringBuilder builder = new(connectionString);
    string username = builder.Username ??
      throw new InvalidOperationException("Connection string requires Username.");
    int stringCacheCapacity = int.Parse(
      Environment.GetEnvironmentVariable("APEX_BENCH_STRING_CACHE_CAPACITY") ??
      "1024");
    PgConnection connection = await PgClient.ConnectAsync(new PgConnectOptions
    {
      Host = builder.Host ??
        throw new InvalidOperationException("Connection string requires Host."),
      Port = builder.Port,
      Database = builder.Database ?? username,
      Username = username,
      Password = builder.Password ?? string.Empty,
      PipeliningLimit = 256,
      StringCacheCapacity = stringCacheCapacity,
    });
    ISqlPreparedStatement? pipelineStatement = workload == "pipeline"
      ? await connection.PrepareAsync("SELECT 1::int4")
      : null;
    return new ApexQueryRunner(
      connection,
      workload,
      fetchSize,
      workload == "string100"
        ? $"SELECT 'repeated-value'::text FROM generate_series(1, {rowCount})"
        : $"SELECT generate_series(1, {rowCount})::int4",
      rowCount,
      checked(rowCount * (rowCount + 1) / 2),
      pipelineDepth,
      pipelineStatement);
  }

  public int OperationsPerInvocation =>
    workload == "pipeline" ? pipelineDepth : 1;

  public async ValueTask QueryAsync(CancellationToken cancellationToken)
  {
    if (workload == "pipeline")
    {
      Task<SqlRowSet>[] pending = new Task<SqlRowSet>[pipelineDepth];
      for (int i = 0; i < pending.Length; i++)
      {
        pending[i] = pipelineStatement!.QueryAsync(
          cancellationToken: CancellationToken.None).AsTask();
      }

      SqlRowSet[] results = await Task.WhenAll(pending);
      if (results.Any(static rows => rows[0].Get<int>(0) != 1))
      {
        throw new InvalidOperationException("Unexpected Apex pipeline result.");
      }
    }
    else if (workload == "borrowed100")
    {
      int sum = 0;
      await using ISqlRowReader reader =
        await connection.ExecuteReaderAsync(streamSql, cancellationToken: cancellationToken);
      while (await reader.ReadAsync(cancellationToken))
      {
        sum += reader.GetInt32(0);
      }

      if (sum != expectedSum)
      {
        throw new InvalidOperationException($"Unexpected borrowed-reader sum {sum}.");
      }
    }
    else if (workload == "stream100")
    {
      int sum = 0;
      await foreach (SqlRow row in connection.StreamAsync(
                       streamSql,
                       fetchSize: fetchSize,
                       cancellationToken: cancellationToken))
      {
        sum += row.Get<int>(0);
      }

      if (sum != expectedSum)
      {
        throw new InvalidOperationException($"Unexpected stream sum {sum}.");
      }
    }
    else if (workload == "string100")
    {
      int count = 0;
      await foreach (SqlRow row in connection.StreamAsync(
                       streamSql,
                       fetchSize: fetchSize,
                       cancellationToken: cancellationToken))
      {
        if (row.GetString(0) != "repeated-value")
        {
          throw new InvalidOperationException("Unexpected string value.");
        }

        count++;
      }

      if (count != rowCount)
      {
        throw new InvalidOperationException($"Unexpected row count {count}.");
      }
    }
    else
    {
      _ = await connection.QueryAsync("SELECT 1", cancellationToken);
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (pipelineStatement is not null)
    {
      await pipelineStatement.DisposeAsync();
    }

    await connection.DisposeAsync();
  }
}

internal sealed class NpgsqlQueryRunner(
  NpgsqlConnection connection,
  string workload,
  string streamSql,
  int rowCount,
  int expectedSum,
  int pipelineDepth,
  NpgsqlBatch? pipelineBatch) : IQueryRunner
{
  public static async ValueTask<NpgsqlQueryRunner> CreateAsync(
    string workload,
    int rowCount,
    int pipelineDepth,
    string connectionString)
  {
    NpgsqlConnection connection = new(connectionString);
    await connection.OpenAsync();
    NpgsqlBatch? pipelineBatch = null;
    if (workload == "pipeline")
    {
      pipelineBatch = new NpgsqlBatch(connection);
      for (int i = 0; i < pipelineDepth; i++)
      {
        pipelineBatch.BatchCommands.Add(
          new NpgsqlBatchCommand("SELECT 1::int4"));
      }

      await pipelineBatch.PrepareAsync();
    }

    return new NpgsqlQueryRunner(
      connection,
      workload,
      workload == "string100"
        ? $"SELECT 'repeated-value'::text FROM generate_series(1, {rowCount})"
        : $"SELECT generate_series(1, {rowCount})::int4",
      rowCount,
      checked(rowCount * (rowCount + 1) / 2),
      pipelineDepth,
      pipelineBatch);
  }

  public int OperationsPerInvocation =>
    workload == "pipeline" ? pipelineDepth : 1;

  public async ValueTask QueryAsync(CancellationToken cancellationToken)
  {
    if (workload == "pipeline")
    {
      await using NpgsqlDataReader reader =
        await pipelineBatch!.ExecuteReaderAsync(CancellationToken.None);
      int count = 0;
      do
      {
        if (!await reader.ReadAsync(CancellationToken.None) ||
            reader.GetInt32(0) != 1)
        {
          throw new InvalidOperationException("Unexpected Npgsql pipeline result.");
        }

        count++;
      }
      while (await reader.NextResultAsync(CancellationToken.None));

      if (count != pipelineDepth)
      {
        throw new InvalidOperationException(
          $"Expected {pipelineDepth} Npgsql results but received {count}.");
      }
    }
    else if (workload is "stream100" or "borrowed100")
    {
      await using NpgsqlCommand command =
        new(streamSql, connection);
      await using NpgsqlDataReader reader =
        await command.ExecuteReaderAsync(cancellationToken);
      int sum = 0;
      while (await reader.ReadAsync(cancellationToken))
      {
        sum += reader.GetInt32(0);
      }

      if (sum != expectedSum)
      {
        throw new InvalidOperationException($"Unexpected stream sum {sum}.");
      }
    }
    else if (workload == "string100")
    {
      await using NpgsqlCommand command =
        new(streamSql, connection);
      await using NpgsqlDataReader reader =
        await command.ExecuteReaderAsync(cancellationToken);
      int count = 0;
      while (await reader.ReadAsync(cancellationToken))
      {
        if (reader.GetString(0) != "repeated-value")
        {
          throw new InvalidOperationException("Unexpected string value.");
        }

        count++;
      }

      if (count != rowCount)
      {
        throw new InvalidOperationException($"Unexpected row count {count}.");
      }
    }
    else
    {
      await using NpgsqlCommand command = new("SELECT 1", connection);
      _ = await command.ExecuteScalarAsync(cancellationToken);
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (pipelineBatch is not null)
    {
      await pipelineBatch.DisposeAsync();
    }

    await connection.DisposeAsync();
  }
}

internal sealed record HarnessResult(
  string Driver,
  int Concurrency,
  long Operations,
  double DurationSeconds,
  double OperationsPerSecond,
  double P50Milliseconds,
  double P95Milliseconds,
  double P99Milliseconds,
  long AllocatedBytes,
  int Gen0Collections,
  int Gen1Collections,
  int Gen2Collections,
  string Runtime,
  string OperatingSystem,
  string Architecture);
