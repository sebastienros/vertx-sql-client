# Vertx.PgClient for .NET

A high-performance PostgreSQL client for .NET, ported from the [Vert.x pg-client](https://github.com/eclipse-vertx/vertx-sql-client).

## Features

- Fully asynchronous API using `async/await`
- Simple and prepared query execution
- Binary and text protocol support
- PostgreSQL notifications (LISTEN/NOTIFY)
- SSL/TLS support (Prefer, Require, VerifyCa, VerifyFull modes)
- Connection URI parsing
- MD5 password authentication

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

## Limitations

The following features from the original Vert.x pg-client are not yet implemented:

- **SCRAM authentication** - Only MD5 and cleartext password authentication are supported. For now, configure PostgreSQL to use `md5` or `trust` authentication.
- **Connection pooling** - Each connection is independent. Use external pooling if needed.
- **Pipelining** - Queries are executed sequentially.
- **COPY protocol** - COPY IN/OUT for bulk data transfer is not implemented.
- **Custom type handlers** - Extended type registration is not available.

## License

Apache License 2.0 - Same as the original Vert.x pg-client.

## Acknowledgments

This is a C# port of the excellent [Vert.x pg-client](https://github.com/eclipse-vertx/vertx-sql-client) originally created by Julien Viet and the Eclipse Vert.x team.
