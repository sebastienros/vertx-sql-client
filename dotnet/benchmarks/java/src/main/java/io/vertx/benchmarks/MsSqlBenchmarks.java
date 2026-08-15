/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

package io.vertx.benchmarks;

import io.vertx.core.Vertx;
import io.vertx.core.net.ClientSSLOptions;
import io.vertx.mssqlclient.EncryptionMode;
import io.vertx.mssqlclient.MSSQLConnectOptions;
import io.vertx.mssqlclient.MSSQLConnection;
import io.vertx.sqlclient.PreparedQuery;
import io.vertx.sqlclient.PreparedStatement;
import io.vertx.sqlclient.Row;
import io.vertx.sqlclient.RowSet;
import io.vertx.sqlclient.RowStream;
import io.vertx.sqlclient.Tuple;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;
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

@State(Scope.Benchmark)
@BenchmarkMode(Mode.Throughput)
@OutputTimeUnit(TimeUnit.SECONDS)
@Warmup(iterations = 5, time = 1)
@Measurement(iterations = 10, time = 2)
@Fork(2)
public class MsSqlBenchmarks {

  private static final int BATCH_DEPTH = 16;
  private static final String ROWS_SQL = rowsSql(false);
  private static final String STRINGS_SQL = rowsSql(true);

  private Vertx vertx;
  private MSSQLConnection connection;
  private PreparedQuery<RowSet<Row>> prepared;
  private PreparedQuery<RowSet<Row>> batch;
  private PreparedStatement rows;
  private PreparedStatement strings;
  private List<Tuple> batchParameters;

  @Setup(Level.Trial)
  public void setup() {
    vertx = Vertx.vertx();
    MSSQLConnectOptions options = new MSSQLConnectOptions()
      .setHost(environment("APEX_MSSQL_HOST", "localhost"))
      .setPort(Integer.parseInt(environment("APEX_MSSQL_PORT", "1433")))
      .setDatabase(requiredEnvironment("APEX_MSSQL_DATABASE"))
      .setUser(requiredEnvironment("APEX_MSSQL_USERNAME"))
      .setPassword(requiredEnvironment("APEX_MSSQL_PASSWORD"))
      .setEncryptionMode(EncryptionMode.ON)
      .setSslOptions(new ClientSSLOptions().setTrustAll(true));
    connection = MSSQLConnection.connect(vertx, options)
      .toCompletionStage()
      .toCompletableFuture()
      .join();
    prepared = connection.preparedQuery("SELECT CAST(@p1 AS int)");
    connection.query(
      "CREATE TABLE #vertx_batch (value int NOT NULL); " +
        "INSERT INTO #vertx_batch VALUES (0)")
      .execute()
      .toCompletionStage()
      .toCompletableFuture()
      .join();
    batch = connection.preparedQuery("UPDATE #vertx_batch SET value = @p1");
    rows = connection.prepare(ROWS_SQL).toCompletionStage().toCompletableFuture().join();
    strings = connection.prepare(STRINGS_SQL).toCompletionStage().toCompletableFuture().join();
    batchParameters = new ArrayList<>(BATCH_DEPTH);
    for (int value = 1; value <= BATCH_DEPTH; value++) {
      batchParameters.add(Tuple.of(value));
    }
  }

  @TearDown(Level.Trial)
  public void teardown() {
    rows.close().toCompletionStage().toCompletableFuture().join();
    strings.close().toCompletionStage().toCompletableFuture().join();
    connection.close().toCompletionStage().toCompletableFuture().join();
    vertx.close().toCompletionStage().toCompletableFuture().join();
  }

  @Benchmark
  public int simpleQuery() {
    return connection.query("SELECT 1")
      .execute()
      .toCompletionStage()
      .toCompletableFuture()
      .join()
      .iterator()
      .next()
      .getInteger(0);
  }

  @Benchmark
  public int preparedQuery() {
    return prepared.execute(Tuple.of(42))
      .toCompletionStage()
      .toCompletableFuture()
      .join()
      .iterator()
      .next()
      .getInteger(0);
  }

  @Benchmark
  public int stream100Rows() {
    return consume(rows, false);
  }

  @Benchmark
  public int batchEquivalent() {
    int affected = 0;
    for (Tuple parameters : batchParameters) {
      affected += batch.execute(parameters)
        .toCompletionStage()
        .toCompletableFuture()
        .join()
        .rowCount();
    }
    return affected;
  }

  @Benchmark
  public int repeatedStrings100Rows() {
    return consume(strings, true);
  }

  private static int consume(PreparedStatement statement, boolean strings) {
    CompletableFuture<Integer> completion = new CompletableFuture<>();
    int[] value = new int[1];
    RowStream<Row> stream = statement.createStream(16);
    stream.exceptionHandler(completion::completeExceptionally);
    stream.handler(row -> {
      if (strings) {
        if (!"repeated-value".equals(row.getString(0))) {
          completion.completeExceptionally(
            new IllegalStateException("Unexpected SQL Server string value"));
        }
        value[0]++;
      } else {
        value[0] += row.getInteger(0);
      }
    });
    stream.endHandler(ignored -> completion.complete(value[0]));
    return completion.join();
  }

  private static String rowsSql(boolean strings) {
    return """
      WITH numbers AS (
        SELECT 1 AS value
        UNION ALL
        SELECT value + 1 FROM numbers WHERE value < 100
      )
      SELECT %s FROM numbers OPTION (MAXRECURSION 100)
      """.formatted(strings
      ? "CAST(N'repeated-value' AS nvarchar(32))"
      : "value");
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
}
