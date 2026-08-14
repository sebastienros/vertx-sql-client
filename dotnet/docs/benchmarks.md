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
| Npgsql stream 100 rows | 325.60 us | 1,496 B |
| Apex stream 100 rows | 359.60 us | 14,551 B |

The original Apex implementation used a server cursor and was substantially slower than Npgsql's reader. The profiling-guided client-streaming implementation described below replaces that path.

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

### Client-streaming improvement

`StreamAsync` now consumes one extended-query response through a bounded channel. The channel applies client-side socket backpressure, while `OpenCursorAsync` remains the explicit server-cursor API.

The same PostgreSQL 16 single-connection workload after the change produced:

| Driver | operations/s | p50 | p95 | p99 | allocated/op |
|---|---:|---:|---:|---:|---:|
| Apex client stream | 2,832 | 0.347 ms | 0.388 ms | 0.433 ms | 15,269 B |
| Npgsql | 3,130 | 0.313 ms | 0.357 ms | 0.395 ms | 1,170 B |

BenchmarkDotNet ShortRun measured Apex at 359.6 us and 14.21 KB versus Npgsql at 325.6 us and 1.46 KB. The change improved Apex's single-connection throughput by about 10.5 times and median latency by about 10.6 times. Apex is now within roughly 10% of Npgsql latency and throughput for this workload.

Allocation remains the primary gap. Apex materializes every value into `object?[]` and every row into a lifetime-safe `SqlRow`; Npgsql reads typed values directly from a reusable reader buffer. A lower-allocation row representation or page-owned value storage is the next priority.

### Allocation attribution

Allocation-event traces of matched 8-second streaming runs reported 417.4 MB for Apex and 38.8 MB for Npgsql. The leading Apex allocation types were:

| Allocation group | Approximate share |
|---|---:|
| `object?[]`, `SqlRow`, and boxed `Int32` row materialization | 56% |
| Bounded channel and channel wait operations | 13% |
| Wire payload buffers | 6% |
| Streaming/scheduler async state machines, delegates, cancellation, and remaining infrastructure | 25% |

Npgsql had no per-row allocation category. About 90% of its sampled bytes were fixed per-operation `NpgsqlCommand`, `NpgsqlBatchCommand`, `ExecuteReader`/`NextResult` state machines, and command behavior objects.

Scaling the same query validates the attribution:

| Driver | Rows | Allocated/op |
|---|---:|---:|
| Apex | 1 | 5,775 B |
| Apex | 10 | 6,849 B |
| Apex | 100 | 15,710 B |
| Npgsql | 1 | 1,184 B |
| Npgsql | 10 | 1,182 B |
| Npgsql | 100 | 1,186 B |

A linear fit gives Apex approximately **5.7 KB fixed per stream plus 100 B per row**. Npgsql is effectively flat because `NpgsqlDataReader` exposes an ephemeral typed view over a reusable read buffer. Apex's `SqlRow` contract is stronger: yielded rows own safe managed values and remain usable after enumeration advances or the connection is released. The allocation comparison therefore includes a semantic difference, not only implementation overhead.

The next allocation work should be split accordingly:

1. Add an explicit ephemeral streaming-reader API for callers who want Npgsql-like zero-per-row ownership semantics.
2. Back lifetime-safe `SqlRow` instances with shared immutable page buffers and offsets, decoding typed values lazily instead of allocating `object?[]` and boxing scalars.
3. Replace the per-stream bounded channel with a custom pull enumerator integrated with the scheduler to reduce the roughly 5.7 KB fixed cost.

## Pipelining

The common pipelining harness uses one physical connection and reports individual queries/second. Apex and Vert.x submit independent prepared-query operations concurrently in order. Npgsql does not permit concurrent commands on one connection, so its closest supported equivalent is one reusable `NpgsqlBatch` containing the same number of prepared `SELECT 1::int4` commands.

PostgreSQL 16, one connection, 3-second warmup, 10-second measurement:

| Driver | Depth | Queries/s | Batch p50 | Allocated/query |
|---|---:|---:|---:|---:|
| Apex | 1 | 3,106 | 0.301 ms | 3,630 B |
| NpgsqlBatch | 1 | 3,189 | 0.300 ms | 729 B |
| Vert.x | 1 | 3,054 | 0.317 ms | Not available |
| Apex | 16 | 16,244 | 0.911 ms | 2,344 B |
| NpgsqlBatch | 16 | 49,506 | 0.316 ms | 46 B |
| Vert.x | 16 | 18,704 | 0.672 ms | Not available |
| Apex | 64 | 26,450 | 2.364 ms | 2,239 B |
| NpgsqlBatch | 64 | 170,835 | 0.364 ms | 12 B |
| Vert.x | 64 | 38,835 | 1.478 ms | Not available |
| Apex | 256 | 46,677 | 5.379 ms | 2,178 B |
| NpgsqlBatch | 256 | 384,347 | 0.644 ms | 3 B |
| Vert.x | 256 | 55,552 | 4.537 ms | Not available |

At depth 1 the three drivers are effectively equal. At depth 256, Apex improves by 15 times over its depth-1 throughput; Vert.x improves by 18 times and remains about 19% faster than Apex. NpgsqlBatch is 8.2 times faster than Apex because it uses a single batch operation and amortizes one approximately 1 KB batch allocation across all commands. Apex and Vert.x retain one future/result lifecycle and protocol command sequence per submitted query.

BenchmarkDotNet ShortRun confirms the .NET batch shape:

| Driver | Depth | Batch mean | Allocated/batch |
|---|---:|---:|---:|
| Apex | 1 | 306.4 us | 3.47 KB |
| NpgsqlBatch | 1 | 304.7 us | 1.01 KB |
| Apex | 16 | 1,105.9 us | 36.48 KB |
| NpgsqlBatch | 16 | 325.2 us | 1.01 KB |
| Apex | 64 | 2,296.0 us | 139.21 KB |
| NpgsqlBatch | 64 | 379.1 us | 1.01 KB |
| Apex | 256 | 5,451.2 us | 542.45 KB |
| NpgsqlBatch | 256 | 663.7 us | 1.01 KB |

Vert.x JMH measured 3,093, 1,385, 614, and 216 batches/second at depths 1, 16, 64, and 256 respectively, equivalent to approximately 3.1k, 22.2k, 39.3k, and 55.4k queries/second.

The next Apex pipelining optimization is a first-class reusable batch API that emits one protocol batch and returns compact batch results, instead of constructing one task, row set, and command lifecycle per query.

### Vert.x JMH

JMH 1.37 used 3 warmups, 5 measurements, 2-second measurement iterations, and 2 forks:

| Workload | Throughput |
|---|---:|
| Vert.x simple query | 3,069.72 ops/s |
| Vert.x prepared query | 1,564.33 ops/s |

Native BenchmarkDotNet and JMH values are not compared directly. Full JSON, CSV, Markdown, HTML, and logs are retained outside the repository in the session benchmark artifacts under `final-9b272f37-20260814`.
