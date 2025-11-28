# Vertx.PgClient for .NET

A high-performance PostgreSQL client for .NET, ported from the [Vert.x pg-client](https://github.com/eclipse-vertx/vertx-sql-client).

## Features

- Fully asynchronous API using `async/await`
- Simple and prepared query execution
- **Prepared statement caching** with LRU eviction
- Binary and text protocol support
- **Connection pooling** with configurable size and timeouts
- **Query pipelining/multiplexing** - multiple concurrent queries on shared connections
- **Transactions** with savepoints and isolation levels
- PostgreSQL notifications (LISTEN/NOTIFY)
- SSL/TLS support (Prefer, Require, VerifyCa, VerifyFull modes)
- Connection URI parsing
- **SCRAM-SHA-256 authentication** (recommended for PostgreSQL 10+)
- MD5 and cleartext password authentication

## Requirements

- .NET 10.0 or later

## Quick Start

```csharp
using Vertx.PgClient;

// Create connection options
var options = new PgConnectOptions
{
    Host = "localhost",
    Port = 5432,
    Database = "mydb",
    User = "myuser",
    Password = "mypassword"
};

// Or from a connection string
var options = PgConnectOptions.FromUri("postgresql://myuser:mypassword@localhost:5432/mydb");

// Connect
await using var connection = await PgConnection.ConnectAsync(options);

// Simple query
var result = await connection.QueryAsync("SELECT * FROM users");
foreach (var row in result)
{
    Console.WriteLine($"{row.GetString("name")}: {row.GetInteger("age")}");
}

// Prepared query with parameters
var users = await connection.PreparedQueryAsync(
    "SELECT * FROM users WHERE age > $1",
    Tuple.Create(21)
);
```

## Prepared Statement Caching

Enable prepared statement caching to reuse parsed statements across multiple executions of the same SQL query. This reduces server round-trips and improves performance for repeated queries.

```csharp
var options = new PgConnectOptions
{
    Host = "localhost",
    Database = "mydb",
    User = "myuser",
    Password = "mypassword",
    CachePreparedStatements = true,          // Enable caching (default: false)
    PreparedStatementCacheMaxSize = 256,     // Max cached statements (default: 256)
    PreparedStatementCacheSqlLimit = 2048    // Max SQL length to cache (default: 2048)
};

await using var connection = await PgConnection.ConnectAsync(options);

// First call: parses, caches, and executes
var result1 = await connection.PreparedQueryAsync(
    "SELECT * FROM users WHERE id = $1",
    Tuple.Create(1)
);

// Subsequent calls: reuses cached statement (no parse needed)
var result2 = await connection.PreparedQueryAsync(
    "SELECT * FROM users WHERE id = $1",
    Tuple.Create(2)
);
```

The cache uses LRU (Least Recently Used) eviction when the maximum size is reached. Evicted statements are automatically closed on the server.

## Connection Pooling

```csharp
using Vertx.PgClient;

// Create a connection pool
var poolOptions = new PgPoolOptions
{
    MaxSize = 10,              // Maximum connections in the pool
    ConnectionTimeout = 30000, // Timeout waiting for a connection (ms)
    Pipelined = true           // Enable multiplexing (default: true)
};

await using var pool = PgPool.Create(options, poolOptions);

// Execute queries - connections are automatically managed
var result = await pool.QueryAsync("SELECT * FROM users");

// Or use multiplexed execution for high concurrency
var tasks = Enumerable.Range(0, 100)
    .Select(i => pool.ScheduleAsync($"SELECT {i}"))
    .ToList();
var results = await Task.WhenAll(tasks);
```

## Pipelining

When `Pipelined = true` (the default), multiple concurrent callers can share the same physical connection. Queries are pipelined to the server and responses are routed back to the correct caller. This dramatically improves throughput for concurrent workloads.

```csharp
// Configure pipelining limit per connection
var options = new PgConnectOptions
{
    Host = "localhost",
    PipeliningLimit = 256  // Max concurrent queries per connection (default: 256)
};

var poolOptions = new PgPoolOptions
{
    MaxSize = 4,       // 4 connections
    Pipelined = true   // Enable multiplexing
};

await using var pool = PgPool.Create(options, poolOptions);

// 1000 concurrent queries across 4 connections
var tasks = Enumerable.Range(0, 1000)
    .Select(i => pool.ScheduleAsync($"SELECT {i} as id"))
    .ToList();
await Task.WhenAll(tasks);
```

## Transactions

```csharp
// Begin a transaction on a connection
await using var connection = await PgConnection.ConnectAsync(options);
await using var tx = await connection.BeginTransactionAsync();

await tx.QueryAsync("INSERT INTO users (name) VALUES ('Alice')");
await tx.QueryAsync("INSERT INTO users (name) VALUES ('Bob')");

await tx.CommitAsync();  // Or tx.RollbackAsync() to discard changes
```

### Transaction Options

```csharp
var txOptions = new TransactionOptions
{
    IsolationLevel = IsolationLevel.Serializable,  // Default: ReadCommitted
    AccessMode = TransactionAccessMode.ReadOnly,   // Default: ReadWrite
    Deferrable = true  // Only for serializable read-only transactions
};

await using var tx = await connection.BeginTransactionAsync(txOptions);
```

### Savepoints

```csharp
await using var tx = await connection.BeginTransactionAsync();

await tx.QueryAsync("INSERT INTO users (name) VALUES ('Alice')");
await tx.SavepointAsync("sp1");

await tx.QueryAsync("INSERT INTO users (name) VALUES ('Bob')");
await tx.RollbackToSavepointAsync("sp1");  // Undo Bob's insert

await tx.QueryAsync("INSERT INTO users (name) VALUES ('Charlie')");
await tx.CommitAsync();  // Only Alice and Charlie are committed
```

### Pool Transactions

```csharp
await using var pool = PgPool.Create(options);

// Option 1: Manual transaction management
await using var tx = await pool.BeginTransactionAsync();
await tx.QueryAsync("INSERT INTO users (name) VALUES ('Alice')");
await tx.CommitAsync();
// Connection automatically returns to pool when transaction is disposed

// Option 2: Automatic commit/rollback with WithTransactionAsync
var result = await pool.WithTransactionAsync(async tx =>
{
    await tx.QueryAsync("INSERT INTO users (name) VALUES ('Alice')");
    await tx.QueryAsync("INSERT INTO users (name) VALUES ('Bob')");
    return 2;  // Return value
});
// Commits on success, rolls back on exception
```

## Supported PostgreSQL Types

### Scalar Types
- `bool`, `int2`, `int4`, `int8`, `float4`, `float8`, `numeric`
- `text`, `varchar`, `bpchar`, `char`, `name`
- `date`, `time`, `timetz`, `timestamp`, `timestamptz`, `interval`
- `bytea`, `uuid`, `json`, `jsonb`, `xml`

### Geometric Types
- `point`, `line`, `lseg`, `box`, `path`, `polygon`, `circle`

### Network Types
- `inet`, `cidr`, `macaddr`

### Custom Types
- `money` - Currency values
- `interval` - Time intervals

## SSL/TLS Modes

```csharp
options.SslMode = SslMode.Prefer;     // Try SSL, fall back to unencrypted
options.SslMode = SslMode.Require;    // Require SSL, don't verify certificate
options.SslMode = SslMode.VerifyCa;   // Verify server certificate
options.SslMode = SslMode.VerifyFull; // Verify certificate and hostname
```

## Notifications

```csharp
connection.NotificationReceived += notification =>
{
    Console.WriteLine($"Channel: {notification.Channel}, Payload: {notification.Payload}");
};

await connection.QueryAsync("LISTEN my_channel");
```

## Authentication

The client supports multiple PostgreSQL authentication methods:

```csharp
var options = new PgConnectOptions
{
    Host = "localhost",
    Database = "mydb",
    User = "myuser",
    Password = "mypassword"  // Used for SCRAM, MD5, and cleartext authentication
};
```

### Supported Methods

| Method | Description |
|--------|-------------|
| **SCRAM-SHA-256** | Modern, secure authentication (PostgreSQL 10+, recommended) |
| **MD5** | Legacy password authentication |
| **Cleartext** | Plain password (not recommended, use with SSL) |
| **Trust** | No password required (for local development) |

The authentication method is negotiated automatically based on the server's configuration.

## Limitations

The following features from the original Vert.x pg-client are not yet implemented:

| Feature | Description |
|---------|-------------|
| **COPY protocol** | COPY IN/OUT for bulk data transfer |
| **Cursors** | Server-side cursors for paginating large result sets |
| **Row streaming** | Reactive stream of rows with pause/resume/backpressure |
| **Batch queries** | Execute same prepared statement with multiple parameter sets |
| **Cancel request** | Cancel a running query from another connection |
| **Row mapping** | Transform rows to custom objects via mapping function |
| **Layer 7 proxy support** | PgBouncer transaction mode support |
| **Unix domain sockets** | Connect via Unix socket instead of TCP |

## License

Apache License 2.0 - Same as the original Vert.x pg-client.

## Acknowledgments

This is a C# port of the excellent [Vert.x pg-client](https://github.com/eclipse-vertx/vertx-sql-client) originally created by Julien Viet and the Eclipse Vert.x team.
