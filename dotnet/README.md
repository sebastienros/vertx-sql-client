# Apex SQL Client

`Apex.SqlClient` is a .NET 10 direct SQL client API inspired by the common capabilities of the Vert.x SQL clients. It does not implement ADO.NET interfaces.

`Apex.PgClient`, `Apex.MySqlClient`, and `Apex.MsSqlClient` implement the
PostgreSQL, MySQL/MariaDB, and SQL Server wire protocols directly. They do not
wrap Npgsql, MySqlConnector, Microsoft.Data.SqlClient, ODBC, or another runtime
database driver. Operations are asynchronous and cancellable, connections and
transactions are async-disposable, buffered rows have safe lazy lifetimes, and
streaming offers both `IAsyncEnumerable<SqlRow>` and the borrowed
`ISqlRowReader`.

### PostgreSQL

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

### SQL Server

```csharp
await using MsSqlPool pool = MsSqlPool.Create(
    MsSqlConnectOptions.Parse(
        Environment.GetEnvironmentVariable("APEX_MSSQL_CONNECTION_STRING")
        ?? throw new InvalidOperationException("Connection string is required.")));

SqlRowSet rows = await pool.QueryAsync(
    "SELECT id, message FROM messages WHERE id = @P1",
    SqlParameters.Create(1));

Console.WriteLine(rows[0].Get<string>("message"));
```

`MsSqlConnectOptions.Parse` accepts standard SQL Server key/value connection
strings and `sqlserver://` URIs. SQL authentication is currently supported;
integrated/Windows, Entra ID, and access-token authentication are not. See
[the API contract](docs/api-contract.md) and
[compatibility matrix](docs/compatibility.md) for encryption, concurrency,
types, and row-lifetime details.

SQL Server streaming parses packet payloads incrementally, including tokens and
PLP values split across packets. The per-connection repeated-string cache is
bounded by `StringCacheCapacity` and `StringCacheMaximumByteLength`.

### MySQL and MariaDB

MySQL uses `?` placeholders:

```csharp
await using MySqlPool pool = MySqlPool.Create(
    new MySqlConnectOptions
    {
        Host = "localhost",
        Database = "app",
        Username = "app",
        Password = "secret"
    });

SqlRowSet rows = await pool.QueryAsync(
    "SELECT id, message FROM messages WHERE id = ?",
    SqlParameters.Create(1));
```

`Apex.MySqlClient` supports the active Vert.x matrix: MySQL 8.4 and 9.6, and
MariaDB 11.8. See [MySQL usage and compatibility](docs/mysql.md).

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
dotnet test --solution dotnet/Apex.SqlClient.slnx --no-restore
```

PostgreSQL, MySQL/MariaDB, and SQL Server integration tests use Testcontainers
and require Docker. Unit suites also use deterministic in-process protocol
servers for framing, connection, query, and pool-lifetime coverage. The SQL
Server image can be selected
to reproduce the Vert.x MSSQL Maven profile matrix:

```bash
MSSQL_IMAGE=mcr.microsoft.com/mssql/server:2019-latest \
  dotnet test dotnet/tests/Apex.MsSqlClient.IntegrationTests --no-restore
MSSQL_IMAGE=mcr.microsoft.com/mssql/server:2022-latest \
  dotnet test dotnet/tests/Apex.MsSqlClient.IntegrationTests --no-restore
MSSQL_IMAGE=mcr.microsoft.com/mssql/server:2025-latest \
  dotnet test dotnet/tests/Apex.MsSqlClient.IntegrationTests --no-restore
```

The direct drivers and options/parameter paths are NativeAOT-compatible. The
smoke application retains PostgreSQL, MySQL, and SQL Server roots:

```bash
dotnet publish dotnet/tests/Apex.AotSmoke -c Release -r linux-x64 --no-restore
```

## Benchmarks

Set `APEX_PG_CONNECTION_STRING` or `APEX_MSSQL_CONNECTION_STRING` to the
matching standard connection string, or set `APEX_MYSQL_CONNECTION_STRING` for
MySQL/MariaDB, then select a suite:

```bash
dotnet run -c Release --project dotnet/benchmarks/Apex.DriverBenchmarks -- \
  --filter '*MsSqlBenchmarks*'
```

The BenchmarkDotNet and common-process suites compare Apex with Npgsql,
MySqlConnector, Microsoft.Data.SqlClient, and equivalent Vert.x JMH workloads.
Comparator packages are referenced only by benchmark projects; the direct
runtime drivers do not depend on them. See
[benchmark methodology](docs/benchmarks.md) for all suites and the cross-runtime
process harness. No SQL Server performance result is claimed by this
documentation.
