/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

package io.vertx.benchmarks;

import io.vertx.core.Vertx;
import io.vertx.mysqlclient.MySQLConnectOptions;
import io.vertx.mysqlclient.MySQLConnection;
import io.vertx.sqlclient.PreparedStatement;
import io.vertx.sqlclient.Row;
import io.vertx.sqlclient.RowSet;
import io.vertx.sqlclient.RowStream;
import io.vertx.sqlclient.Tuple;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;
import java.util.stream.Collectors;
import org.openjdk.jmh.annotations.Benchmark;
import org.openjdk.jmh.annotations.BenchmarkMode;
import org.openjdk.jmh.annotations.Fork;
import org.openjdk.jmh.annotations.Level;
import org.openjdk.jmh.annotations.Measurement;
import org.openjdk.jmh.annotations.Mode;
import org.openjdk.jmh.annotations.OutputTimeUnit;
import org.openjdk.jmh.annotations.Scope;
import org.openjdk.jmh.annotations.Setup;
import org.openjdk.jmh.annotations.State;
import org.openjdk.jmh.annotations.TearDown;
import org.openjdk.jmh.annotations.Warmup;

/**
 * Vert.x MySQL client workloads equivalent to the .NET MySqlBenchmarks: simple query, prepared
 * query, a safe 100-row {@link RowStream}, a collector-based 100-row reduction (the closest
 * Vert.x equivalent to a borrowed-reader single round trip), and 100 repeated small strings.
 *
 * <p>The 100-row workloads use a recursive common table expression because MySQL/MariaDB have no
 * built-in row-generating function equivalent to PostgreSQL's {@code generate_series}; this
 * requires MySQL 8.0+ or MariaDB 10.2+.
 */
@State(Scope.Benchmark)
@BenchmarkMode(Mode.Throughput)
@OutputTimeUnit(TimeUnit.SECONDS)
@Warmup(iterations = 5, time = 1)
@Measurement(iterations = 10, time = 2)
@Fork(2)
public class MySqlBenchmarks {

  private static final String SEQUENCE_SQL =
    "WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 100) " +
    "SELECT CAST(n AS SIGNED) FROM seq";
  private static final String STRING_SEQUENCE_SQL =
    "WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 100) " +
    "SELECT 'repeated-value' FROM seq";
  private static final long EXPECTED_SUM = 100L * 101L / 2L;

  private Vertx vertx;
  private MySQLConnection connection;
  private PreparedStatement preparedStatement;
  private PreparedStatement sequenceStatement;

  @Setup(Level.Trial)
  public void setup() {
    vertx = Vertx.vertx();
    MySQLConnectOptions options = new MySQLConnectOptions()
      .setHost(environment("APEX_MYSQL_HOST", "localhost"))
      .setPort(Integer.parseInt(environment("APEX_MYSQL_PORT", "3306")))
      .setDatabase(environment("APEX_MYSQL_DATABASE", "db"))
      .setUser(environment("APEX_MYSQL_USERNAME", "user"))
      .setPassword(environment("APEX_MYSQL_PASSWORD", "pass"));
    connection = MySQLConnection.connect(vertx, options)
      .toCompletionStage()
      .toCompletableFuture()
      .join();
    preparedStatement = connection.prepare("SELECT CAST(? AS SIGNED)")
      .toCompletionStage()
      .toCompletableFuture()
      .join();
    sequenceStatement = connection.prepare(SEQUENCE_SQL)
      .toCompletionStage()
      .toCompletableFuture()
      .join();
  }

  @TearDown(Level.Trial)
  public void teardown() {
    preparedStatement.close().toCompletionStage().toCompletableFuture().join();
    sequenceStatement.close().toCompletionStage().toCompletableFuture().join();
    connection.close().toCompletionStage().toCompletableFuture().join();
    vertx.close().toCompletionStage().toCompletableFuture().join();
  }

  @Benchmark
  public long simpleQuery() {
    long value = connection.query("SELECT CAST(1 AS SIGNED)")
      .execute()
      .toCompletionStage()
      .toCompletableFuture()
      .join()
      .iterator()
      .next()
      .getLong(0);
    if (value != 1L) {
      throw new IllegalStateException("Unexpected simple query result.");
    }

    return value;
  }

  @Benchmark
  public long preparedQuery() {
    long value = preparedStatement.query()
      .execute(Tuple.of(42))
      .toCompletionStage()
      .toCompletableFuture()
      .join()
      .iterator()
      .next()
      .getLong(0);
    if (value != 42L) {
      throw new IllegalStateException("Unexpected prepared query result.");
    }

    return value;
  }

  @Benchmark
  public long stream100() throws Exception {
    CompletableFuture<Long> result = new CompletableFuture<>();
    long[] sum = new long[1];
    RowStream<Row> stream = sequenceStatement.createStream(16);
    stream.handler(row -> sum[0] += row.getLong(0));
    stream.endHandler(ignored -> result.complete(sum[0]));
    stream.exceptionHandler(result::completeExceptionally);
    long value = result.get();
    if (value != EXPECTED_SUM) {
      throw new IllegalStateException("Unexpected stream sum " + value + ".");
    }

    return value;
  }

  @Benchmark
  public long collect100() {
    long value = connection.query(SEQUENCE_SQL)
      .collecting(Collectors.summingLong(row -> row.getLong(0)))
      .execute()
      .toCompletionStage()
      .toCompletableFuture()
      .join()
      .value();
    if (value != EXPECTED_SUM) {
      throw new IllegalStateException("Unexpected collector sum " + value + ".");
    }

    return value;
  }

  @Benchmark
  public int string100() {
    RowSet<Row> rows = connection.query(STRING_SEQUENCE_SQL)
      .execute()
      .toCompletionStage()
      .toCompletableFuture()
      .join();
    int count = 0;
    for (Row row : rows) {
      if (!"repeated-value".equals(row.getString(0))) {
        throw new IllegalStateException("Unexpected string value.");
      }

      count++;
    }

    if (count != 100) {
      throw new IllegalStateException("Unexpected row count " + count + ".");
    }

    return count;
  }

  private static String environment(String name, String fallback) {
    String value = System.getenv(name);
    return value == null || value.isBlank() ? fallback : value;
  }
}
