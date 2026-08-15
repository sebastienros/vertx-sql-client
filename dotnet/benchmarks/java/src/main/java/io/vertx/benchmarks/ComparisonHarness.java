/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

package io.vertx.benchmarks;

import io.vertx.core.Future;
import io.vertx.core.Vertx;
import io.vertx.core.net.ClientSSLOptions;
import io.vertx.mssqlclient.EncryptionMode;
import io.vertx.mssqlclient.MSSQLConnectOptions;
import io.vertx.mssqlclient.MSSQLConnection;
import io.vertx.mysqlclient.MySQLConnectOptions;
import io.vertx.mysqlclient.MySQLConnection;
import io.vertx.pgclient.PgConnectOptions;
import io.vertx.pgclient.PgConnection;
import io.vertx.sqlclient.PreparedQuery;
import io.vertx.sqlclient.PreparedStatement;
import io.vertx.sqlclient.Row;
import io.vertx.sqlclient.RowSet;
import io.vertx.sqlclient.RowStream;
import io.vertx.sqlclient.SqlConnection;
import io.vertx.sqlclient.Tuple;
import java.lang.management.GarbageCollectorMXBean;
import java.lang.management.ManagementFactory;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.LongAdder;

public final class ComparisonHarness {

  public static void main(String[] args) throws Exception {
    String requestedDatabase = System.getenv("APEX_BENCH_DATABASE");
    String driver = args.length == 0
      ? switch (requestedDatabase == null ? "postgres" : requestedDatabase.toLowerCase(Locale.ROOT)) {
        case "mysql" -> "vertx-mysql";
        case "mssql", "sqlserver" -> "vertx-mssql";
        default -> "vertx";
      }
      : args[0].toLowerCase(Locale.ROOT);
    boolean msSql = driver.equals("vertx-mssql");
    boolean mySql = driver.equals("vertx-mysql");
    if (!msSql && !mySql && !driver.equals("vertx")) {
      throw new IllegalArgumentException("Unknown Java driver '" + driver + "'.");
    }
    String database = requestedDatabase == null || requestedDatabase.isBlank()
      ? (msSql ? "mssql" : mySql ? "mysql" : "postgres")
      : requestedDatabase.toLowerCase(Locale.ROOT);
    boolean databaseMatches = (database.equals("postgres") && driver.equals("vertx")) ||
      (database.equals("mysql") && mySql) ||
      ((database.equals("mssql") || database.equals("sqlserver")) && msSql);
    if (!databaseMatches) {
      throw new IllegalArgumentException(
        "Driver '" + driver + "' does not support database '" + database + "'.");
    }
    String workload = environment("APEX_BENCH_WORKLOAD", "query");
    if (!List.of("query", "stream100", "borrowed100", "pipeline", "batch", "string100")
        .contains(workload)) {
      throw new IllegalArgumentException("Unknown workload '" + workload + "'.");
    }
    int concurrency = Integer.parseInt(environment("APEX_BENCH_CONCURRENCY", "16"));
    int fetchSize = Integer.parseInt(environment("APEX_BENCH_FETCH_SIZE", "16"));
    int rowCount = Integer.parseInt(environment("APEX_BENCH_ROW_COUNT", "100"));
    int pipelineDepth = Integer.parseInt(environment("APEX_BENCH_PIPELINE_DEPTH", "64"));
    double warmupSeconds = Double.parseDouble(environment("APEX_BENCH_WARMUP_SECONDS", "2"));
    double durationSeconds = Double.parseDouble(environment("APEX_BENCH_DURATION_SECONDS", "10"));
    Vertx vertx = Vertx.vertx();
    List<Runner> runners = new ArrayList<>(concurrency);
    try {
      for (int i = 0; i < concurrency; i++) {
        runners.add(msSql
          ? Runner.msSql(vertx, workload, fetchSize, rowCount, pipelineDepth)
          : mySql
            ? Runner.mySql(vertx, workload, fetchSize, rowCount, pipelineDepth)
            : Runner.postgreSql(vertx, workload, fetchSize, rowCount, pipelineDepth));
      }

      run(driver, runners, warmupSeconds, false);
      System.gc();
      long[] collectionsBefore = collections();
      Result result = run(driver, runners, durationSeconds, true);
      long[] collectionsAfter = collections();
      result.gen0Collections = collectionsAfter[0] - collectionsBefore[0];
      result.gen1Collections = collectionsAfter[1] - collectionsBefore[1];
      System.out.println(result.toJson());
    } finally {
      for (Runner runner : runners) {
        runner.close();
      }
      vertx.close().toCompletionStage().toCompletableFuture().join();
    }
  }

  private static Result run(
      String driver,
      List<Runner> runners,
      double durationSeconds,
      boolean record) throws Exception {
    long startedAt = System.nanoTime();
    long deadline = startedAt + (long) (durationSeconds * 1_000_000_000L);
    LongAdder operations = new LongAdder();
    List<Long> latencies = Collections.synchronizedList(new ArrayList<>());
    ExecutorService workers = Executors.newFixedThreadPool(runners.size());
    try {
      List<java.util.concurrent.Future<?>> pending = new ArrayList<>(runners.size());
      for (Runner runner : runners) {
        pending.add(workers.submit(() -> {
          while (System.nanoTime() < deadline) {
            long started = System.nanoTime();
            runner.invoke();
            if (record) {
              latencies.add(System.nanoTime() - started);
            }
            operations.add(runner.operationsPerInvocation());
          }
        }));
      }
      for (java.util.concurrent.Future<?> worker : pending) {
        worker.get();
      }
    } finally {
      workers.shutdownNow();
    }

    double elapsedSeconds = (System.nanoTime() - startedAt) / 1_000_000_000d;
    Collections.sort(latencies);
    long count = operations.sum();
    return new Result(
      driver,
      runners.size(),
      count,
      elapsedSeconds,
      count / elapsedSeconds,
      percentile(latencies, 0.50),
      percentile(latencies, 0.95),
      percentile(latencies, 0.99));
  }

  private static final class Runner {
    private final SqlConnection connection;
    private final String workload;
    private final boolean msSql;
    private final boolean mySql;
    private final int rowCount;
    private final long expectedSum;
    private final int pipelineDepth;
    private final PreparedStatement streamStatement;
    private final PreparedStatement pipelineStatement;
    private final PreparedQuery<RowSet<Row>> pipelineQuery;
    private final List<Tuple> batch;
    private final int fetchSize;

    private Runner(
        SqlConnection connection,
        String workload,
        boolean msSql,
        boolean mySql,
        int fetchSize,
        int rowCount,
        int pipelineDepth) {
      this.connection = connection;
      this.workload = workload;
      this.msSql = msSql;
      this.mySql = mySql;
      this.fetchSize = fetchSize;
      this.rowCount = rowCount;
      this.expectedSum = (long) rowCount * (rowCount + 1) / 2;
      this.pipelineDepth = pipelineDepth;
      String streamSql = rowsSql(msSql, mySql, rowCount, workload.equals("string100"));
      this.streamStatement = workload.equals("stream100") ||
        workload.equals("borrowed100") ||
        workload.equals("string100")
        ? await(connection.prepare(streamSql))
        : null;
      if (msSql && isBatch(workload)) {
        await(connection.query(
          "CREATE TABLE #vertx_batch (value int NOT NULL); " +
            "INSERT INTO #vertx_batch VALUES (0)").execute());
      }
      this.pipelineStatement = isBatch(workload)
        ? await(connection.prepare(
          msSql
            ? "UPDATE #vertx_batch SET value = @p1"
            : mySql ? "SELECT CAST(1 AS SIGNED)" : "SELECT 1::INT4"))
        : null;
      this.pipelineQuery = pipelineStatement == null ? null : pipelineStatement.query();
      if (msSql && isBatch(workload)) {
        this.batch = new ArrayList<>(pipelineDepth);
        for (int value = 1; value <= pipelineDepth; value++) {
          batch.add(Tuple.of(value));
        }
      } else {
        this.batch = null;
      }
    }

    private static Runner msSql(
        Vertx vertx,
        String workload,
        int fetchSize,
        int rowCount,
        int pipelineDepth) {
      MSSQLConnectOptions options = new MSSQLConnectOptions()
        .setHost(environment("APEX_MSSQL_HOST", "localhost"))
        .setPort(Integer.parseInt(environment("APEX_MSSQL_PORT", "1433")))
        .setDatabase(requiredEnvironment("APEX_MSSQL_DATABASE"))
        .setUser(requiredEnvironment("APEX_MSSQL_USERNAME"))
        .setPassword(requiredEnvironment("APEX_MSSQL_PASSWORD"))
        .setEncryptionMode(EncryptionMode.ON)
        .setSslOptions(new ClientSSLOptions().setTrustAll(true));
      return new Runner(
        await(MSSQLConnection.connect(vertx, options)),
        workload,
        true,
        false,
        fetchSize,
        rowCount,
        pipelineDepth);
    }

    private static Runner mySql(
        Vertx vertx,
        String workload,
        int fetchSize,
        int rowCount,
        int pipelineDepth) {
      MySQLConnectOptions options = new MySQLConnectOptions()
        .setHost(environment("APEX_MYSQL_HOST", "localhost"))
        .setPort(Integer.parseInt(environment("APEX_MYSQL_PORT", "3306")))
        .setDatabase(environment("APEX_MYSQL_DATABASE", "db"))
        .setUser(environment("APEX_MYSQL_USERNAME", "user"))
        .setPassword(environment("APEX_MYSQL_PASSWORD", "pass"))
        .setPipeliningLimit(Math.max(256, pipelineDepth));
      return new Runner(
        await(MySQLConnection.connect(vertx, options)),
        workload,
        false,
        true,
        fetchSize,
        rowCount,
        pipelineDepth);
    }

    private static Runner postgreSql(
        Vertx vertx,
        String workload,
        int fetchSize,
        int rowCount,
        int pipelineDepth) {
      PgConnectOptions options = new PgConnectOptions()
        .setHost(environment("APEX_PG_HOST", "localhost"))
        .setPort(Integer.parseInt(environment("APEX_PG_PORT", "5432")))
        .setDatabase(environment("APEX_PG_DATABASE", "db"))
        .setUser(environment("APEX_PG_USERNAME", "user"))
        .setPassword(environment("APEX_PG_PASSWORD", "pass"))
        .setPipeliningLimit(Math.max(256, pipelineDepth));
      return new Runner(
        await(PgConnection.connect(vertx, options)),
        workload,
        false,
        false,
        fetchSize,
        rowCount,
        pipelineDepth);
    }

    private int operationsPerInvocation() {
      return isBatch(workload) ? pipelineDepth : 1;
    }

    private void invoke() {
      if (isBatch(workload)) {
        if (msSql) {
          int affected = 0;
          for (Tuple parameters : batch) {
            affected += await(pipelineQuery.execute(parameters)).rowCount();
          }
          if (affected != pipelineDepth) {
            throw new IllegalStateException("Unexpected Vert.x SQL Server batch affected rows");
          }
        } else {
          List<Future<RowSet<Row>>> pending = new ArrayList<>(pipelineDepth);
          for (int i = 0; i < pipelineDepth; i++) {
            pending.add(pipelineQuery.execute());
          }
          await(Future.all(pending));
          for (Future<RowSet<Row>> result : pending) {
            Number value = (Number) result.result().iterator().next().getValue(0);
            if (value.longValue() != 1L) {
              throw new IllegalStateException("Unexpected Vert.x pipeline result");
            }
          }
        }
      } else if (workload.equals("borrowed100")) {
        long sum = 0;
        for (Row row : await(streamStatement.query().execute())) {
          sum += ((Number) row.getValue(0)).longValue();
        }
        if (sum != expectedSum) {
          throw new IllegalStateException("Unexpected Vert.x borrowed-reader sum " + sum);
        }
      } else if (workload.equals("stream100")) {
        long sum = consumeStream(false);
        if (sum != expectedSum) {
          throw new IllegalStateException("Unexpected Vert.x stream sum " + sum);
        }
      } else if (workload.equals("string100")) {
        long count = consumeStream(true);
        if (count != rowCount) {
          throw new IllegalStateException("Unexpected Vert.x string row count " + count);
        }
      } else if (((Number) await(connection.query(
          mySql ? "SELECT CAST(1 AS SIGNED)" : "SELECT 1").execute())
          .iterator().next().getValue(0)).longValue() != 1L) {
        throw new IllegalStateException("Unexpected Vert.x query result");
      }
    }

    private long consumeStream(boolean strings) {
      CompletableFuture<Long> completion = new CompletableFuture<>();
      long[] value = new long[1];
      RowStream<Row> stream = streamStatement.createStream(fetchSize);
      stream.exceptionHandler(completion::completeExceptionally);
      stream.handler(row -> {
        if (strings) {
          if (!"repeated-value".equals(row.getString(0))) {
            completion.completeExceptionally(
              new IllegalStateException("Unexpected Vert.x string value"));
          }
          value[0]++;
        } else {
          value[0] += ((Number) row.getValue(0)).longValue();
        }
      });
      stream.endHandler(ignored -> completion.complete(value[0]));
      return completion.join();
    }

    private void close() {
      if (streamStatement != null) {
        await(streamStatement.close());
      }
      if (pipelineStatement != null) {
        await(pipelineStatement.close());
      }
      await(connection.close());
    }
  }

  private static String rowsSql(boolean msSql, boolean mySql, int count, boolean strings) {
    if (msSql) {
      return """
        WITH numbers AS (
          SELECT 1 AS value
          UNION ALL
          SELECT value + 1 FROM numbers WHERE value < %d
        )
        SELECT %s FROM numbers OPTION (MAXRECURSION 0)
        """.formatted(
          count,
          strings ? "CAST(N'repeated-value' AS nvarchar(32))" : "value");
    }

    if (mySql) {
      String cte =
        "WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < " +
          count + ") ";
      return strings
        ? cte + "SELECT 'repeated-value' FROM seq"
        : cte + "SELECT CAST(n AS SIGNED) FROM seq";
    }

    return strings
      ? "SELECT 'repeated-value'::text FROM generate_series(1, " + count + ")"
      : "SELECT generate_series(1, " + count + ")::int4";
  }

  private static boolean isBatch(String workload) {
    return workload.equals("pipeline") || workload.equals("batch");
  }

  private static <T> T await(Future<T> future) {
    return future.toCompletionStage().toCompletableFuture().join();
  }

  private static double percentile(List<Long> ordered, double percentile) {
    if (ordered.isEmpty()) {
      return 0;
    }
    int index = Math.max(
      0,
      Math.min(ordered.size() - 1, (int) Math.ceil(percentile * ordered.size()) - 1));
    return ordered.get(index) / 1_000_000d;
  }

  private static long[] collections() {
    long young = 0;
    long old = 0;
    for (GarbageCollectorMXBean bean : ManagementFactory.getGarbageCollectorMXBeans()) {
      long count = Math.max(0, bean.getCollectionCount());
      if (bean.getName().toLowerCase(Locale.ROOT).contains("young")) {
        young += count;
      } else {
        old += count;
      }
    }
    return new long[] { young, old };
  }

  private static String environment(String name, String fallback) {
    String value = System.getenv(name);
    return value == null || value.isBlank() ? fallback : value;
  }

  private static String requiredEnvironment(String name) {
    String value = System.getenv(name);
    if (value == null || value.isBlank()) {
      throw new IllegalStateException("Set " + name + " before running SQL Server benchmarks.");
    }
    return value;
  }

  private static final class Result {
    private final String driver;
    private final int concurrency;
    private final long operations;
    private final double durationSeconds;
    private final double operationsPerSecond;
    private final double p50Milliseconds;
    private final double p95Milliseconds;
    private final double p99Milliseconds;
    private long gen0Collections;
    private long gen1Collections;

    private Result(
        String driver,
        int concurrency,
        long operations,
        double durationSeconds,
        double operationsPerSecond,
        double p50Milliseconds,
        double p95Milliseconds,
        double p99Milliseconds) {
      this.driver = driver;
      this.concurrency = concurrency;
      this.operations = operations;
      this.durationSeconds = durationSeconds;
      this.operationsPerSecond = operationsPerSecond;
      this.p50Milliseconds = p50Milliseconds;
      this.p95Milliseconds = p95Milliseconds;
      this.p99Milliseconds = p99Milliseconds;
    }

    private String toJson() {
      return String.format(
        Locale.ROOT,
        """
        {
          "Driver": "%s",
          "Concurrency": %d,
          "Operations": %d,
          "DurationSeconds": %.6f,
          "OperationsPerSecond": %.6f,
          "P50Milliseconds": %.6f,
          "P95Milliseconds": %.6f,
          "P99Milliseconds": %.6f,
          "AllocatedBytes": -1,
          "Gen0Collections": %d,
          "Gen1Collections": %d,
          "Runtime": "%s",
          "OperatingSystem": "%s",
          "Architecture": "%s"
        }
        """,
        driver,
        concurrency,
        operations,
        durationSeconds,
        operationsPerSecond,
        p50Milliseconds,
        p95Milliseconds,
        p99Milliseconds,
        gen0Collections,
        gen1Collections,
        System.getProperty("java.version"),
        System.getProperty("os.name") + " " + System.getProperty("os.version"),
        System.getProperty("os.arch"));
    }
  }
}
