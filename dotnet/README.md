# Apex SQL Client

`Apex.SqlClient` is a .NET 10 direct SQL client API inspired by the common capabilities of the Vert.x SQL clients. It does not implement ADO.NET interfaces.

`Apex.PgClient` and `Apex.MySqlClient` implement the PostgreSQL and MySQL/MariaDB
wire protocols directly. Operations are asynchronous and cancellable, connections and
transactions are async-disposable, buffered rows have safe lazy lifetimes, and streaming offers
both `IAsyncEnumerable<SqlRow>` and the borrowed `ISqlRowReader`.

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

## Build and test

Use the repository-approved package feed as an ephemeral restore source; do not commit a NuGet source configuration.

```bash
dotnet restore dotnet/Apex.SqlClient.slnx --source "$NUGET_SOURCE"
dotnet test --solution dotnet/Apex.SqlClient.slnx --no-restore
```

PostgreSQL and MySQL/MariaDB integration tests use Testcontainers and require Docker. Unit suites
also use deterministic in-process protocol servers for framing, connection, query, and
pool-lifetime coverage.

## Benchmarks

Set `APEX_PG_CONNECTION_STRING` or `APEX_MYSQL_CONNECTION_STRING`, then run:

```bash
dotnet run -c Release --project dotnet/benchmarks/Apex.DriverBenchmarks
```

The BenchmarkDotNet and common-process suites compare Apex with Npgsql, MySqlConnector, and
equivalent Vert.x JMH workloads. See [benchmark methodology](docs/benchmarks.md).
