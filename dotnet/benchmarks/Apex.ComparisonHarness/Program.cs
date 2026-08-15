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
using MySqlConnector;
using Npgsql;
using ApexMySql = Apex.MySqlClient;

// The database selector preserves backward compatibility: it defaults to "postgres", so existing
// invocations that only set APEX_PG_CONNECTION_STRING and pass "apex"/"npgsql" behave exactly as
// before. Set APEX_BENCH_DATABASE=mysql and APEX_MYSQL_CONNECTION_STRING, with driver "apex-mysql"
// or "mysqlconnector", to benchmark MySQL/MariaDB instead.
string database = (Environment.GetEnvironmentVariable("APEX_BENCH_DATABASE") ?? "postgres")
  .ToLowerInvariant();
string driver = args.ElementAtOrDefault(0) ?? (database == "mysql" ? "apex-mysql" : "apex");
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
string connectionString = database switch
{
  "postgres" => Environment.GetEnvironmentVariable("APEX_PG_CONNECTION_STRING") ??
    throw new InvalidOperationException("Set APEX_PG_CONNECTION_STRING."),
  "mysql" => Environment.GetEnvironmentVariable("APEX_MYSQL_CONNECTION_STRING") ??
    throw new InvalidOperationException("Set APEX_MYSQL_CONNECTION_STRING."),
  _ => throw new ArgumentException(
    $"Unknown database '{database}'. Use 'postgres' or 'mysql'."),
};

IQueryRunner[] runners = await Task.WhenAll(
  Enumerable.Range(0, concurrency)
    .Select(_ => CreateRunnerAsync(
      database,
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
  string database,
  string driver,
  string workload,
  int fetchSize,
  int rowCount,
  int pipelineDepth,
  string connectionString) =>
  (database, driver.ToLowerInvariant()) switch
  {
    ("postgres", "apex") => WrapAsync(ApexQueryRunner.CreateAsync(
      workload,
      fetchSize,
      rowCount,
      pipelineDepth,
      connectionString)),
    ("postgres", "npgsql") => WrapAsync(NpgsqlQueryRunner.CreateAsync(
      workload,
      rowCount,
      pipelineDepth,
      connectionString)),
    ("mysql", "apex-mysql") => WrapAsync(ApexMySqlQueryRunner.CreateAsync(
      workload,
      fetchSize,
      rowCount,
      pipelineDepth,
      connectionString)),
    ("mysql", "mysqlconnector") => WrapAsync(MySqlConnectorQueryRunner.CreateAsync(
      workload,
      rowCount,
      pipelineDepth,
      connectionString)),
    _ => throw new ArgumentException($"Unknown driver '{driver}' for database '{database}'."),
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

/// <summary>
/// Runs the shared workload set against Apex.MySqlClient. The 100-row workloads use a recursive
/// common table expression because MySQL/MariaDB have no built-in row-generating function
/// equivalent to PostgreSQL's <c>generate_series</c>; this requires MySQL 8.0+ or MariaDB 10.2+.
/// </summary>
internal sealed class ApexMySqlQueryRunner(
  ApexMySql.MySqlConnection connection,
  string workload,
  int fetchSize,
  string streamSql,
  int rowCount,
  long expectedSum,
  int pipelineDepth,
  ISqlPreparedStatement? pipelineStatement) : IQueryRunner
{
  public static async ValueTask<ApexMySqlQueryRunner> CreateAsync(
    string workload,
    int fetchSize,
    int rowCount,
    int pipelineDepth,
    string connectionString)
  {
    MySqlConnectionStringBuilder builder = new(connectionString);
    string username = string.IsNullOrEmpty(builder.UserID)
      ? throw new InvalidOperationException("Connection string requires UserID.")
      : builder.UserID;
    int stringCacheCapacity = int.Parse(
      Environment.GetEnvironmentVariable("APEX_BENCH_STRING_CACHE_CAPACITY") ??
      "1024");
    ApexMySql.MySqlConnection connection = await ApexMySql.MySqlClient.ConnectAsync(
      new ApexMySql.MySqlConnectOptions
      {
        Host = string.IsNullOrEmpty(builder.Server)
          ? throw new InvalidOperationException("Connection string requires Server.")
          : builder.Server,
        Port = (int)builder.Port,
        Database = builder.Database,
        Username = username,
        Password = builder.Password,
        PipeliningLimit = 256,
        StringCacheCapacity = stringCacheCapacity,
      });
    ISqlPreparedStatement? pipelineStatement = workload == "pipeline"
      ? await connection.PrepareAsync("SELECT CAST(1 AS SIGNED)")
      : null;
    return new ApexMySqlQueryRunner(
      connection,
      workload,
      fetchSize,
      BuildSequenceSql(rowCount, asString: workload == "string100"),
      rowCount,
      checked((long)rowCount * (rowCount + 1) / 2),
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
      if (results.Any(static rows => rows[0].Get<long>(0) != 1L))
      {
        throw new InvalidOperationException("Unexpected Apex pipeline result.");
      }
    }
    else if (workload == "borrowed100")
    {
      long sum = 0;
      await using ISqlRowReader reader =
        await connection.ExecuteReaderAsync(streamSql, cancellationToken: cancellationToken);
      while (await reader.ReadAsync(cancellationToken))
      {
        sum += reader.GetInt64(0);
      }

      if (sum != expectedSum)
      {
        throw new InvalidOperationException($"Unexpected borrowed-reader sum {sum}.");
      }
    }
    else if (workload == "stream100")
    {
      long sum = 0;
      await foreach (SqlRow row in connection.StreamAsync(
                       streamSql,
                       fetchSize: fetchSize,
                       cancellationToken: cancellationToken))
      {
        sum += row.GetInt64(0);
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
      _ = await connection.QueryAsync("SELECT CAST(1 AS SIGNED)", cancellationToken);
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

  private static string BuildSequenceSql(int rowCount, bool asString) =>
    asString
      ? $"WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < {rowCount}) " +
        "SELECT 'repeated-value' FROM seq"
      : $"WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < {rowCount}) " +
        "SELECT CAST(n AS SIGNED) FROM seq";
}

/// <summary>
/// Runs the shared workload set against MySqlConnector. MySqlConnector does not permit
/// concurrent commands on one connection, so its closest supported equivalent to pipelining is
/// one reusable <see cref="MySqlBatch"/> containing the same number of prepared commands,
/// matching the PostgreSQL/Npgsql comparison.
/// </summary>
internal sealed class MySqlConnectorQueryRunner(
  MySqlConnection connection,
  string workload,
  string streamSql,
  int rowCount,
  long expectedSum,
  int pipelineDepth,
  MySqlBatch? pipelineBatch) : IQueryRunner
{
  public static async ValueTask<MySqlConnectorQueryRunner> CreateAsync(
    string workload,
    int rowCount,
    int pipelineDepth,
    string connectionString)
  {
    MySqlConnection connection = new(connectionString);
    await connection.OpenAsync();
    MySqlBatch? pipelineBatch = null;
    if (workload == "pipeline")
    {
      pipelineBatch = new MySqlBatch(connection);
      for (int i = 0; i < pipelineDepth; i++)
      {
        pipelineBatch.BatchCommands.Add(
          new MySqlBatchCommand("SELECT CAST(1 AS SIGNED)"));
      }

      await pipelineBatch.PrepareAsync();
    }

    return new MySqlConnectorQueryRunner(
      connection,
      workload,
      BuildSequenceSql(rowCount, asString: workload == "string100"),
      rowCount,
      checked((long)rowCount * (rowCount + 1) / 2),
      pipelineDepth,
      pipelineBatch);
  }

  public int OperationsPerInvocation =>
    workload == "pipeline" ? pipelineDepth : 1;

  public async ValueTask QueryAsync(CancellationToken cancellationToken)
  {
    if (workload == "pipeline")
    {
      await using MySqlDataReader reader =
        await pipelineBatch!.ExecuteReaderAsync(CancellationToken.None);
      int count = 0;
      do
      {
        if (!await reader.ReadAsync(CancellationToken.None) ||
            reader.GetInt64(0) != 1L)
        {
          throw new InvalidOperationException("Unexpected MySqlConnector pipeline result.");
        }

        count++;
      }
      while (await reader.NextResultAsync(CancellationToken.None));

      if (count != pipelineDepth)
      {
        throw new InvalidOperationException(
          $"Expected {pipelineDepth} MySqlConnector results but received {count}.");
      }
    }
    else if (workload is "stream100" or "borrowed100")
    {
      await using MySqlCommand command =
        new(streamSql, connection);
      await using MySqlDataReader reader =
        await command.ExecuteReaderAsync(cancellationToken);
      long sum = 0;
      while (await reader.ReadAsync(cancellationToken))
      {
        sum += reader.GetInt64(0);
      }

      if (sum != expectedSum)
      {
        throw new InvalidOperationException($"Unexpected stream sum {sum}.");
      }
    }
    else if (workload == "string100")
    {
      await using MySqlCommand command =
        new(streamSql, connection);
      await using MySqlDataReader reader =
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
      await using MySqlCommand command = new("SELECT CAST(1 AS SIGNED)", connection);
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

  private static string BuildSequenceSql(int rowCount, bool asString) =>
    asString
      ? $"WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < {rowCount}) " +
        "SELECT 'repeated-value' FROM seq"
      : $"WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < {rowCount}) " +
        "SELECT CAST(n AS SIGNED) FROM seq";
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
