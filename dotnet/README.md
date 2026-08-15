# Apex SQL Client

`Apex.SqlClient` is a .NET 10 direct SQL client API inspired by the common capabilities of the Vert.x SQL clients. It does not implement ADO.NET interfaces.

`Apex.PgClient` provides the first PostgreSQL implementation. Operations are asynchronous and cancellable, connections and transactions are async-disposable, buffered rows have safe managed lifetimes, and streaming uses `IAsyncEnumerable<SqlRow>`.

```csharp
await using PgPool pool = PgPool.Create(
    new PgConnectOptions
    {
        Host = "localhost",
        Database = "app",
        Username = "app",
        Password = "secret"
    });

SqlRowSet rows = await pool.QueryAsync(
    "SELECT id, message FROM messages WHERE id = $1",
    SqlParameters.Create(1));

Console.WriteLine(rows[0].Get<string>("message"));
```

Like Npgsql 10, Apex exposes both specific typed getters and generic field
access. Common CLR values have getters such as `GetInt32`, `GetDecimal`,
and `GetGuid`. `Get<T>` uses the same typed decoder for provider-specific
PostgreSQL values, JSON, arrays, nullable values, and
`ReadOnlyMemory<byte>`; it does not route through the object indexer. The
object indexer remains available when runtime typing is required and may
box value types.

Binary `ReadOnlyMemory<byte>` from a buffered `SqlRow` can borrow its
row-owned page without copying. `ISqlRowReader.Get<ReadOnlyMemory<byte>>`
returns owned memory because reader payloads use pooled buffers that are
released on the next read.

## Build and test

Use the repository-approved package feed as an ephemeral restore source; do not commit a NuGet source configuration.

```bash
dotnet restore dotnet/Apex.SqlClient.slnx --source "$NUGET_SOURCE"
dotnet test dotnet/Apex.SqlClient.slnx --no-restore
```

PostgreSQL integration tests use Testcontainers and require Docker. The unit suite includes an in-process PostgreSQL protocol server for deterministic framing, connection, query, and pool-lifetime tests.

## Benchmarks

Set `APEX_PG_CONNECTION_STRING` to a PostgreSQL connection string, then run:

```bash
dotnet run -c Release --project dotnet/benchmarks/Apex.DriverBenchmarks
```

The initial BenchmarkDotNet suite compares simple-query throughput and allocations with Npgsql. Additional prepared, batch, streaming, pipelining, pool-contention, and cross-runtime Vert.x workloads will use the same schema and database instance.
