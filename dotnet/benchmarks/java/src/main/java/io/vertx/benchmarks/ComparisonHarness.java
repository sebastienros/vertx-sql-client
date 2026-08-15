/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

package io.vertx.benchmarks;

import io.vertx.core.Vertx;
import io.vertx.mysqlclient.MySQLConnectOptions;
import io.vertx.mysqlclient.MySQLConnection;
import io.vertx.pgclient.PgConnectOptions;
import io.vertx.pgclient.PgConnection;
import io.vertx.sqlclient.PreparedQuery;
import io.vertx.sqlclient.PreparedStatement;
import io.vertx.sqlclient.Row;
import io.vertx.sqlclient.RowSet;
import io.vertx.sqlclient.RowStream;
import java.lang.management.GarbageCollectorMXBean;
import java.lang.management.ManagementFactory;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.atomic.LongAdder;

public final class ComparisonHarness {

  public static void main(String[] args) throws Exception {
    String database = environment("APEX_BENCH_DATABASE", "postgres").toLowerCase(Locale.ROOT);
    switch (database) {
      case "postgres":
        runPostgres();
        break;
      case "mysql":
        runMysql();
        break;
      default:
        throw new IllegalArgumentException(
          "Unknown database '" + database + "'. Use 'postgres' or 'mysql'.");
    }
  }

  /**
   * Runs the PostgreSQL workload exactly as before: this method's behavior and environment
   * variables are unchanged from the pre-MySQL version of this harness.
   */
  private static void runPostgres() throws Exception {
    int concurrency = Integer.parseInt(environment("APEX_BENCH_CONCURRENCY", "16"));
    int pipelineDepth = Integer.parseInt(environment("APEX_BENCH_PIPELINE_DEPTH", "64"));
    boolean pipeline = environment("APEX_BENCH_WORKLOAD", "query").equals("pipeline");
    double warmupSeconds = Double.parseDouble(environment("APEX_BENCH_WARMUP_SECONDS", "2"));
    double durationSeconds = Double.parseDouble(environment("APEX_BENCH_DURATION_SECONDS", "10"));
    Vertx vertx = Vertx.vertx();
    List<PgConnection> connections = new ArrayList<>(concurrency);
    List<PreparedStatement> statements = new ArrayList<>(concurrency);
    List<PreparedQuery<RowSet<Row>>> queries = new ArrayList<>(concurrency);
    PgConnectOptions options = new PgConnectOptions()
      .setHost(environment("APEX_PG_HOST", "localhost"))
      .setPort(Integer.parseInt(environment("APEX_PG_PORT", "5432")))
      .setDatabase(environment("APEX_PG_DATABASE", "db"))
      .setUser(environment("APEX_PG_USERNAME", "user"))
      .setPassword(environment("APEX_PG_PASSWORD", "pass"));
    try {
      for (int i = 0; i < concurrency; i++) {
        connections.add(PgConnection.connect(vertx, options)
          .toCompletionStage()
          .toCompletableFuture()
          .join());
      }

      if (pipeline) {
        for (PgConnection connection : connections) {
          PreparedStatement statement = connection.prepare("SELECT 1::INT4")
            .toCompletionStage()
            .toCompletableFuture()
            .join();
          statements.add(statement);
          queries.add(statement.query());
        }
      }

      run(connections, queries, pipeline, pipelineDepth, warmupSeconds, false);
      System.gc();
      long[] collectionsBefore = collections();
      Result result = run(
        connections,
        queries,
        pipeline,
        pipelineDepth,
        durationSeconds,
        true);
      long[] collectionsAfter = collections();
      result.gen0Collections = collectionsAfter[0] - collectionsBefore[0];
      result.gen1Collections = collectionsAfter[1] - collectionsBefore[1];
      System.out.println(result.toJson());
    } finally {
      for (PreparedStatement statement : statements) {
        statement.close().toCompletionStage().toCompletableFuture().join();
      }
      for (PgConnection connection : connections) {
        connection.close().toCompletionStage().toCompletableFuture().join();
      }
      vertx.close().toCompletionStage().toCompletableFuture().join();
    }
  }

  /**
   * Runs the MySQL/MariaDB workload set: {@code query}, {@code stream100}, {@code borrowed100},
   * {@code pipeline}, and {@code string100}. The 100-row workloads use a recursive common table
   * expression because MySQL/MariaDB have no built-in row-generating function equivalent to
   * PostgreSQL's {@code generate_series}; this requires MySQL 8.0+ or MariaDB 10.2+.
   */
  private static void runMysql() throws Exception {
    int concurrency = Integer.parseInt(environment("APEX_BENCH_CONCURRENCY", "16"));
    int pipelineDepth = Integer.parseInt(environment("APEX_BENCH_PIPELINE_DEPTH", "64"));
    int rowCount = Integer.parseInt(environment("APEX_BENCH_ROW_COUNT", "100"));
    int fetchSize = Integer.parseInt(environment("APEX_BENCH_FETCH_SIZE", "16"));
    String workload = environment("APEX_BENCH_WORKLOAD", "query");
    double warmupSeconds = Double.parseDouble(environment("APEX_BENCH_WARMUP_SECONDS", "2"));
    double durationSeconds = Double.parseDouble(environment("APEX_BENCH_DURATION_SECONDS", "10"));
    boolean pipeline = workload.equals("pipeline");
    boolean sequence = workload.equals("stream100") ||
      workload.equals("borrowed100") ||
      workload.equals("string100");
    long expectedSum = (long) rowCount * (rowCount + 1) / 2;
    Vertx vertx = Vertx.vertx();
    List<MySQLConnection> connections = new ArrayList<>(concurrency);
    List<PreparedStatement> statements = new ArrayList<>(concurrency);
    MySQLConnectOptions options = new MySQLConnectOptions()
      .setHost(environment("APEX_MYSQL_HOST", "localhost"))
      .setPort(Integer.parseInt(environment("APEX_MYSQL_PORT", "3306")))
      .setDatabase(environment("APEX_MYSQL_DATABASE", "db"))
      .setUser(environment("APEX_MYSQL_USERNAME", "user"))
      .setPassword(environment("APEX_MYSQL_PASSWORD", "pass"))
      .setPipeliningLimit(256);
    try {
      for (int i = 0; i < concurrency; i++) {
        connections.add(MySQLConnection.connect(vertx, options)
          .toCompletionStage()
          .toCompletableFuture()
          .join());
      }

      if (pipeline) {
        for (MySQLConnection connection : connections) {
          statements.add(connection.prepare("SELECT CAST(1 AS SIGNED)")
            .toCompletionStage()
            .toCompletableFuture()
            .join());
        }
      } else if (sequence) {
        String sequenceSql = buildSequenceSql(rowCount, workload.equals("string100"));
        for (MySQLConnection connection : connections) {
          statements.add(connection.prepare(sequenceSql)
            .toCompletionStage()
            .toCompletableFuture()
            .join());
        }
      }

      runMysqlPhase(
        connections, statements, workload, pipelineDepth, fetchSize, expectedSum, rowCount,
        warmupSeconds, false);
      System.gc();
      long[] collectionsBefore = collections();
      Result result = runMysqlPhase(
        connections, statements, workload, pipelineDepth, fetchSize, expectedSum, rowCount,
        durationSeconds, true);
      long[] collectionsAfter = collections();
      result.gen0Collections = collectionsAfter[0] - collectionsBefore[0];
      result.gen1Collections = collectionsAfter[1] - collectionsBefore[1];
      System.out.println(result.toJson());
    } finally {
      for (PreparedStatement statement : statements) {
        statement.close().toCompletionStage().toCompletableFuture().join();
      }
      for (MySQLConnection connection : connections) {
        connection.close().toCompletionStage().toCompletableFuture().join();
      }
      vertx.close().toCompletionStage().toCompletableFuture().join();
    }
  }

  private static Result runMysqlPhase(
      List<MySQLConnection> connections,
      List<PreparedStatement> statements,
      String workload,
      int pipelineDepth,
      int fetchSize,
      long expectedSum,
      int rowCount,
      double durationSeconds,
      boolean record) throws Exception {
    long deadline = System.nanoTime() + (long) (durationSeconds * 1_000_000_000L);
    LongAdder operations = new LongAdder();
    List<Long> latencies = Collections.synchronizedList(new ArrayList<>());
    ExecutorService workers = Executors.newFixedThreadPool(connections.size());
    try {
      List<Future<?>> pending = new ArrayList<>(connections.size());
      for (int workerIndex = 0; workerIndex < connections.size(); workerIndex++) {
        MySQLConnection connection = connections.get(workerIndex);
        PreparedStatement statement = statements.isEmpty() ? null : statements.get(workerIndex);
        pending.add(workers.submit(() -> {
          while (System.nanoTime() < deadline) {
            long started = System.nanoTime();
            executeMysqlOperation(
              connection, statement, workload, pipelineDepth, fetchSize, expectedSum, rowCount);
            if (record) {
              latencies.add(System.nanoTime() - started);
            }
            operations.add(workload.equals("pipeline") ? pipelineDepth : 1);
          }
        }));
      }
      for (Future<?> worker : pending) {
        worker.get();
      }
    } finally {
      workers.shutdownNow();
    }

    Collections.sort(latencies);
    long count = operations.sum();
    return new Result(
      "vertx-mysql",
      connections.size(),
      count,
      durationSeconds,
      count / durationSeconds,
      percentile(latencies, 0.50),
      percentile(latencies, 0.95),
      percentile(latencies, 0.99));
  }

  private static void executeMysqlOperation(
      MySQLConnection connection,
      PreparedStatement statement,
      String workload,
      int pipelineDepth,
      int fetchSize,
      long expectedSum,
      int rowCount) {
    switch (workload) {
      case "pipeline": {
        PreparedQuery<RowSet<Row>> query = statement.query();
        List<io.vertx.core.Future<RowSet<Row>>> batch = new ArrayList<>(pipelineDepth);
        for (int i = 0; i < pipelineDepth; i++) {
          batch.add(query.execute());
        }
        io.vertx.core.Future.all(batch)
          .toCompletionStage()
          .toCompletableFuture()
          .join();
        for (io.vertx.core.Future<RowSet<Row>> result : batch) {
          if (result.result().iterator().next().getLong(0) != 1L) {
            throw new IllegalStateException("Unexpected Vert.x MySQL pipeline result");
          }
        }
        break;
      }
      case "stream100": {
        long sum = 0;
        RowStream<Row> stream = statement.createStream(fetchSize);
        CompletableFuture<Long> completion =
          new CompletableFuture<>();
        long[] accumulator = new long[1];
        stream.handler(row -> accumulator[0] += row.getLong(0));
        stream.endHandler(ignored -> completion.complete(accumulator[0]));
        stream.exceptionHandler(completion::completeExceptionally);
        try {
          sum = completion.get();
        } catch (Exception exception) {
          throw new IllegalStateException("Vert.x MySQL stream failed.", exception);
        }

        if (sum != expectedSum) {
          throw new IllegalStateException("Unexpected Vert.x MySQL stream sum " + sum);
        }
        break;
      }
      case "borrowed100": {
        long sum = 0;
        RowSet<Row> rows = statement.query()
          .execute()
          .toCompletionStage()
          .toCompletableFuture()
          .join();
        for (Row row : rows) {
          sum += row.getLong(0);
        }
        if (sum != expectedSum) {
          throw new IllegalStateException("Unexpected Vert.x MySQL borrowed-reader sum " + sum);
        }
        break;
      }
      case "string100": {
        int count = 0;
        RowSet<Row> rows = statement.query()
          .execute()
          .toCompletionStage()
          .toCompletableFuture()
          .join();
        for (Row row : rows) {
          if (!"repeated-value".equals(row.getString(0))) {
            throw new IllegalStateException("Unexpected string value.");
          }

          count++;
        }
        if (count != rowCount) {
          throw new IllegalStateException("Unexpected row count " + count);
        }
        break;
      }
      default:
        connection.query("SELECT CAST(1 AS SIGNED)")
          .execute()
          .toCompletionStage()
          .toCompletableFuture()
          .join();
        break;
    }
  }

  private static String buildSequenceSql(int rowCount, boolean asString) {
    String cte = "WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < " +
      rowCount + ") ";
    return asString ? cte + "SELECT 'repeated-value' FROM seq" : cte + "SELECT CAST(n AS SIGNED) FROM seq";
  }

  private static Result run(
      List<PgConnection> connections,
      List<PreparedQuery<RowSet<Row>>> queries,
      boolean pipeline,
      int pipelineDepth,
      double durationSeconds,
      boolean record) throws Exception {
    long deadline = System.nanoTime() + (long) (durationSeconds * 1_000_000_000L);
    LongAdder operations = new LongAdder();
    List<Long> latencies = Collections.synchronizedList(new ArrayList<>());
    ExecutorService workers = Executors.newFixedThreadPool(connections.size());
    try {
      List<Future<?>> pending = new ArrayList<>(connections.size());
      for (int workerIndex = 0; workerIndex < connections.size(); workerIndex++) {
        PgConnection connection = connections.get(workerIndex);
        PreparedQuery<RowSet<Row>> preparedQuery =
          pipeline ? queries.get(workerIndex) : null;
        pending.add(workers.submit(() -> {
          while (System.nanoTime() < deadline) {
            long started = System.nanoTime();
            if (pipeline) {
              List<io.vertx.core.Future<RowSet<Row>>> batch = new ArrayList<>(pipelineDepth);
              for (int i = 0; i < pipelineDepth; i++) {
                batch.add(preparedQuery.execute());
              }
              io.vertx.core.Future.all(batch)
                .toCompletionStage()
                .toCompletableFuture()
                .join();
              for (io.vertx.core.Future<RowSet<Row>> result : batch) {
                if (result.result().iterator().next().getInteger(0) != 1) {
                  throw new IllegalStateException("Unexpected Vert.x pipeline result");
                }
              }
            } else {
              connection.query("SELECT 1")
                .execute()
                .toCompletionStage()
                .toCompletableFuture()
                .join();
            }
            if (record) {
              latencies.add(System.nanoTime() - started);
            }
            operations.add(pipeline ? pipelineDepth : 1);
          }
        }));
      }
      for (Future<?> worker : pending) {
        worker.get();
      }
    } finally {
      workers.shutdownNow();
    }

    Collections.sort(latencies);
    long count = operations.sum();
    return new Result(
      "vertx",
      connections.size(),
      count,
      durationSeconds,
      count / durationSeconds,
      percentile(latencies, 0.50),
      percentile(latencies, 0.95),
      percentile(latencies, 0.99));
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
