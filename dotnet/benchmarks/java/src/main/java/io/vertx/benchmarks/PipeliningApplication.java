/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

package io.vertx.benchmarks;

import io.vertx.core.Future;
import io.vertx.core.Promise;
import io.vertx.core.Vertx;
import io.vertx.pgclient.PgConnectOptions;
import io.vertx.pgclient.PgConnection;
import io.vertx.sqlclient.PreparedQuery;
import io.vertx.sqlclient.PreparedStatement;
import io.vertx.sqlclient.Row;
import io.vertx.sqlclient.RowSet;
import java.lang.management.ManagementFactory;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.LongAdder;

public final class PipeliningApplication {

  public static void main(String[] args) {
    int concurrency = positiveEnvironment("APEX_BENCH_CONCURRENCY", 64);
    int queryCount = positiveEnvironment("APEX_BENCH_QUERY_COUNT", 100_000);
    int warmupQueryCount = nonNegativeEnvironment("APEX_BENCH_WARMUP_QUERY_COUNT", 10_000);
    Vertx vertx = Vertx.vertx();
    PgConnectOptions options = new PgConnectOptions()
      .setHost(environment("APEX_PG_HOST", "localhost"))
      .setPort(positiveEnvironment("APEX_PG_PORT", 5432))
      .setDatabase(environment("APEX_PG_DATABASE", "db"))
      .setUser(environment("APEX_PG_USERNAME", "user"))
      .setPassword(environment("APEX_PG_PASSWORD", "pass"))
      .setPipeliningLimit(concurrency);
    PgConnection connection = await(PgConnection.connect(vertx, options));
    PreparedStatement statement = await(connection.prepare("SELECT 1::int4"));
    try {
      if (warmupQueryCount > 0) {
        run(statement.query(), concurrency, warmupQueryCount);
      }

      System.gc();
      Map<Long, Long> allocatedBefore = allocatedBytes();
      long started = System.nanoTime();
      long sum = run(statement.query(), concurrency, queryCount);
      double elapsedSeconds = (System.nanoTime() - started) / 1_000_000_000d;
      long allocatedBytes = allocatedBytesSince(allocatedBefore);
      System.out.printf(
        """
        {
          "Driver": "Vert.x",
          "Concurrency": %d,
          "QueryCount": %d,
          "WarmupQueryCount": %d,
          "ElapsedSeconds": %.6f,
          "QueriesPerSecond": %.6f,
          "AllocatedBytes": %d,
          "ResultSum": %d,
          "Runtime": "%s",
          "OperatingSystem": "%s",
          "Architecture": "%s"
        }
        """,
        concurrency,
        queryCount,
        warmupQueryCount,
        elapsedSeconds,
        queryCount / elapsedSeconds,
        allocatedBytes,
        sum,
        System.getProperty("java.runtime.version"),
        System.getProperty("os.name") + " " + System.getProperty("os.version"),
        System.getProperty("os.arch"));
    } finally {
      await(statement.close());
      await(connection.close());
      await(vertx.close());
    }
  }

  private static long run(
      PreparedQuery<RowSet<Row>> query,
      int concurrency,
      int queryCount) {
    AtomicInteger next = new AtomicInteger();
    LongAdder sum = new LongAdder();
    List<Future<Void>> workers = new ArrayList<>(Math.min(concurrency, queryCount));
    for (int i = 0; i < Math.min(concurrency, queryCount); i++) {
      workers.add(runWorker(query, queryCount, next, sum));
    }

    await(Future.all(workers));
    return sum.sum();
  }

  private static Future<Void> runWorker(
      PreparedQuery<RowSet<Row>> query,
      int queryCount,
      AtomicInteger next,
      LongAdder sum) {
    Promise<Void> completion = Promise.promise();
    runNext(query, queryCount, next, sum, completion);
    return completion.future();
  }

  private static void runNext(
      PreparedQuery<RowSet<Row>> query,
      int queryCount,
      AtomicInteger next,
      LongAdder sum,
      Promise<Void> completion) {
    if (next.getAndIncrement() >= queryCount) {
      completion.complete();
      return;
    }

    query.execute().onComplete(result -> {
      if (result.failed()) {
        completion.fail(result.cause());
        return;
      }

      try {
        int value = result.result().iterator().next().getInteger(0);
        if (value != 1) {
          completion.fail("Unexpected PostgreSQL result " + value);
          return;
        }

        sum.add(value);
        runNext(query, queryCount, next, sum, completion);
      } catch (Throwable failure) {
        completion.fail(failure);
      }
    });
  }

  private static <T> T await(Future<T> future) {
    return future.toCompletionStage().toCompletableFuture().join();
  }

  private static Map<Long, Long> allocatedBytes() {
    if (!(ManagementFactory.getThreadMXBean() instanceof com.sun.management.ThreadMXBean bean) ||
        !bean.isThreadAllocatedMemorySupported()) {
      return Map.of();
    }

    if (!bean.isThreadAllocatedMemoryEnabled()) {
      bean.setThreadAllocatedMemoryEnabled(true);
    }

    long[] threadIds = bean.getAllThreadIds();
    long[] allocated = bean.getThreadAllocatedBytes(threadIds);
    Map<Long, Long> result = new HashMap<>(threadIds.length);
    for (int i = 0; i < threadIds.length; i++) {
      if (allocated[i] >= 0) {
        result.put(threadIds[i], allocated[i]);
      }
    }
    return result;
  }

  private static long allocatedBytesSince(Map<Long, Long> before) {
    if (before.isEmpty()) {
      return -1;
    }

    Map<Long, Long> after = allocatedBytes();
    long allocated = 0;
    for (Map.Entry<Long, Long> entry : after.entrySet()) {
      allocated += Math.max(0, entry.getValue() - before.getOrDefault(entry.getKey(), 0L));
    }
    return allocated;
  }

  private static int positiveEnvironment(String name, int fallback) {
    int value = Integer.parseInt(environment(name, Integer.toString(fallback)));
    if (value <= 0) {
      throw new IllegalArgumentException(name + " must be a positive integer");
    }
    return value;
  }

  private static int nonNegativeEnvironment(String name, int fallback) {
    int value = Integer.parseInt(environment(name, Integer.toString(fallback)));
    if (value < 0) {
      throw new IllegalArgumentException(name + " must be a nonnegative integer");
    }
    return value;
  }

  private static String environment(String name, String fallback) {
    String value = System.getenv(name);
    return value == null || value.isBlank() ? fallback : value;
  }
}