/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

package io.vertx.benchmarks;

import io.vertx.core.Future;
import io.vertx.core.Vertx;
import io.vertx.pgclient.PgConnectOptions;
import io.vertx.pgclient.PgConnection;
import io.vertx.sqlclient.PreparedQuery;
import io.vertx.sqlclient.PreparedStatement;
import io.vertx.sqlclient.Row;
import io.vertx.sqlclient.RowSet;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.TimeUnit;
import org.openjdk.jmh.annotations.Benchmark;
import org.openjdk.jmh.annotations.BenchmarkMode;
import org.openjdk.jmh.annotations.Fork;
import org.openjdk.jmh.annotations.Level;
import org.openjdk.jmh.annotations.Measurement;
import org.openjdk.jmh.annotations.Mode;
import org.openjdk.jmh.annotations.OutputTimeUnit;
import org.openjdk.jmh.annotations.Param;
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
public class PipeliningBenchmarks {

  @Param({"1", "16", "64", "256"})
  public int depth;

  private Vertx vertx;
  private PgConnection connection;
  private PreparedStatement statement;
  private PreparedQuery<RowSet<Row>> query;

  @Setup(Level.Trial)
  public void setup() {
    vertx = Vertx.vertx();
    PgConnectOptions options = new PgConnectOptions()
      .setHost(environment("APEX_PG_HOST", "localhost"))
      .setPort(Integer.parseInt(environment("APEX_PG_PORT", "5432")))
      .setDatabase(environment("APEX_PG_DATABASE", "db"))
      .setUser(environment("APEX_PG_USERNAME", "user"))
      .setPassword(environment("APEX_PG_PASSWORD", "pass"))
      .setPipeliningLimit(256);
    connection = PgConnection.connect(vertx, options)
      .toCompletionStage()
      .toCompletableFuture()
      .join();
    statement = connection.prepare("SELECT 1::INT4")
      .toCompletionStage()
      .toCompletableFuture()
      .join();
    query = statement.query();
  }

  @TearDown(Level.Trial)
  public void teardown() {
    statement.close().toCompletionStage().toCompletableFuture().join();
    connection.close().toCompletionStage().toCompletableFuture().join();
    vertx.close().toCompletionStage().toCompletableFuture().join();
  }

  @Benchmark
  public int pipeline() {
    List<Future<RowSet<Row>>> pending = new ArrayList<>(depth);
    for (int i = 0; i < depth; i++) {
      pending.add(query.execute());
    }
    Future.all(pending)
      .toCompletionStage()
      .toCompletableFuture()
      .join();

    int sum = 0;
    for (Future<RowSet<Row>> result : pending) {
      sum += result.result().iterator().next().getInteger(0);
    }
    return sum;
  }

  private static String environment(String name, String fallback) {
    String value = System.getenv(name);
    return value == null || value.isBlank() ? fallback : value;
  }
}
