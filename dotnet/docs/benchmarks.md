# Benchmark methodology

## Native microbenchmarks

- `Apex.DriverBenchmarks` uses BenchmarkDotNet for .NET codec allocations and Apex/Npgsql query, prepared-query, and streaming workloads.
- `dotnet/benchmarks/java` uses JMH for equivalent Vert.x PostgreSQL query workloads.
- Native BenchmarkDotNet and JMH scores are reported separately because their harnesses, runtimes, warmup models, and profilers differ.

## Common process harness

`Apex.ComparisonHarness` and `io.vertx.benchmarks.ComparisonHarness` execute the same `SELECT 1` workload with the same database, concurrency, warmup, and measurement duration. Both emit JSON containing operations/second, p50/p95/p99 latency, runtime, OS, architecture, and GC counts. The .NET harness also reports process allocation bytes.

Environment variables:

| Variable | Purpose | Default |
|---|---|---|
| `APEX_PG_CONNECTION_STRING` | .NET PostgreSQL connection string | Required |
| `APEX_PG_HOST`, `APEX_PG_PORT`, `APEX_PG_DATABASE`, `APEX_PG_USERNAME`, `APEX_PG_PASSWORD` | Vert.x connection fields | Local Vert.x test defaults |
| `APEX_BENCH_CONCURRENCY` | Concurrent workers/connections | `16` |
| `APEX_BENCH_WARMUP_SECONDS` | Warmup duration | `2` |
| `APEX_BENCH_DURATION_SECONDS` | Measurement duration | `10` |

Results must record the exact driver commit/package, SDK/JDK, CPU, OS, database image/version, container limits, and harness settings. Short local runs are diagnostic only and are not release claims.

## Final PostgreSQL-first baseline

The final branch at commit `9b272f37` was measured on macOS 15.7.8 Arm64 against one PostgreSQL 16 Alpine container. The common harness used 16 workers, a 5-second warmup, and a 20-second measurement.

| Driver | operations/s | p50 | p95 | p99 | allocated/op |
|---|---:|---:|---:|---:|---:|
| Apex | 19,943 | 0.748 ms | 1.305 ms | 1.684 ms | 2,785 B |
| Npgsql | 20,240 | 0.728 ms | 1.307 ms | 1.704 ms | 1,346 B |
| Vert.x | 21,654 | 0.665 ms | 1.294 ms | 1.623 ms | Not available |

Apex was within 1.5% of Npgsql throughput and 8% of Vert.x throughput in this run. Its latency distribution was close to Npgsql, but it allocated about 2.1 times as much per operation.

### BenchmarkDotNet ShortRun

| Workload | Mean | Allocated |
|---|---:|---:|
| Decode numeric text | 160.98 ns | 296 B |
| Decode numeric binary | 79.96 ns | 200 B |
| Decode text array | 107.95 ns | 808 B |
| Npgsql simple query | 308.34 us | 1,672 B |
| Apex simple query | 301.96 us | 2,743 B |
| Npgsql prepared query | 303.97 us | 1,208 B |
| Apex prepared query | 295.56 us | 3,574 B |
| Npgsql stream 100 rows | 327.39 us | 1,496 B |
| Apex stream 100 rows | 3,682.49 us | 37,618 B |

The Apex cursor-based stream fetches 16 rows per round trip in this benchmark and is substantially slower than Npgsql's reader. Streaming round trips and per-page allocations are the primary performance follow-up.

### Streaming profile

A warmed macOS `sample` capture and managed heap dump used the same 100-row workload with one connection. The common harness ran for 30 seconds after a 5-second warmup:

| Driver/configuration | operations/s | p50 | allocated/op |
|---|---:|---:|---:|
| Apex, fetch 16 | 270 | 3.670 ms | 37,944 B |
| Apex, fetch 100 | 463 | 2.139 ms | 27,620 B |
| Npgsql | 3,106 | 0.315 ms | 1,184 B |

Increasing Apex's fetch size from 16 to 100 reduced the number of portal fetches, improving throughput by 71%, reducing median latency by 42%, and reducing allocation per operation by 27%. It remained 6.7 times slower than Npgsql and allocated 23 times more.

The reason is architectural:

- Apex direct streaming creates a prepared statement, starts a transaction, opens a named portal, performs one fetch per page, closes the portal, commits, and closes the statement.
- With fetch size 16, 100 rows require seven portal executions in addition to setup and cleanup round trips.
- Npgsql sends one query and streams all rows client-side from its read buffer.
- Apex materializes every value into `object?[]` and every row into `SqlRow`, then creates a `SqlRowSet` for each fetched page. Npgsql's reader exposes values without equivalent row/page materialization.

The macOS samples include blocked threads, so their counts are wall-clock residency rather than pure CPU time. They nevertheless show Apex repeatedly resident in scheduler, `WriteExecutePortalAsync`, `ReadPortalAsync`, and `ReadPortalPageAsync` state machines, whereas Npgsql is concentrated in `ExecuteReader`, `ReadMessageLong`, and `NpgsqlReadBuffer.Ensure`. Heap snapshots were small for both processes (approximately 0.4 MB Apex and 1.1 MB Npgsql), indicating the Apex difference is transient allocation rather than retained leakage.

The recommended change is to make `StreamAsync` use one extended-query response and client-side socket backpressure, while retaining `OpenCursorAsync` for callers who explicitly require server cursors. A lower-allocation row representation or page-owned value storage is the second priority.

### Vert.x JMH

JMH 1.37 used 3 warmups, 5 measurements, 2-second measurement iterations, and 2 forks:

| Workload | Throughput |
|---|---:|
| Vert.x simple query | 3,069.72 ops/s |
| Vert.x prepared query | 1,564.33 ops/s |

Native BenchmarkDotNet and JMH values are not compared directly. Full JSON, CSV, Markdown, HTML, and logs are retained outside the repository in the session benchmark artifacts under `final-9b272f37-20260814`.
