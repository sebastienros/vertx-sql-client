# Apex MySQL client

`Apex.MySqlClient` is a direct, NativeAOT-compatible MySQL protocol driver. It does not use
ADO.NET or wrap MySqlConnector.

## Connect

```csharp
MySqlConnectOptions options = MySqlConnectOptions.Parse(
  "mysql://app:secret@localhost:3306/app?sslMode=preferred");

await using MySqlConnection connection = await MySqlClient.ConnectAsync(options);
SqlRowSet rows = await connection.QueryAsync(
  "SELECT id, message FROM messages WHERE id = ?",
  SqlParameters.Create(42));
```

Keyword connection strings are also accepted:

```text
Server=localhost;Port=3306;User ID=app;Password=secret;Database=app
```

Unknown URI or keyword options become connection attributes. Set session variables explicitly
through `MySqlConnectOptions.SessionVariables`.

## Authentication and TLS

The driver supports `mysql_native_password`, `caching_sha2_password`, `sha256_password`, and
opt-in `mysql_clear_password`.

`MySqlSslMode.Preferred` is the default. It negotiates TLS when the server advertises it and
otherwise uses the unencrypted transport. `Required`, `VerifyCa`, and `VerifyIdentity` enforce
TLS with progressively stronger certificate checks. A certificate callback and client
certificates can be supplied for private PKI and mutual TLS.

SHA-2 full authentication sends a clear password only over TLS. On an unencrypted transport,
configure a trusted PEM public key or explicitly enable `AllowPublicKeyRetrieval`; the latter
trusts a key obtained from the server and should only be used on a trusted network.

## Queries, prepared statements, and results

Queries without parameters use `COM_QUERY`. Parameterized queries and explicit prepared
statements use `COM_STMT_PREPARE` and `COM_STMT_EXECUTE` with binary parameters and binary rows.
`SqlRowSet.Next` exposes additional result sets when multi-statements are enabled.

`SqlRowSet` owns immutable page-backed packet data and decodes fields lazily. `StreamAsync`
returns equally safe page-backed rows. `ExecuteReaderAsync` is the lower-allocation borrowed API:
its current values remain valid only until the next `ReadAsync` call.

`SqlCommandResult` reports affected rows, last insert identifier, server status flags, and warning
count, including for pooled execution. `MySqlConnection.LastCommandInfo` additionally exposes the
server information string. The default uses matched-row semantics, like Vert.x; set
`UseAffectedRows` to report only rows whose values changed.

Prepared batches preserve submission and result order. Increasing `PipeliningLimit` allows
independent commands to overlap on the wire; the conservative default is one.

## Transactions, cursors, cancellation, and pooling

Disposing an incomplete transaction rolls it back. A pooled lease remains pinned while a
transaction, prepared statement, cursor, stream, or borrowed reader is alive.

MySQL sends complete result sets rather than PostgreSQL-style fetch portals. `OpenCursorAsync`
therefore pages a backpressured wire reader and keeps the connection exclusively pinned until
the cursor is exhausted or disposed.

Cancellation defaults to a second authenticated connection issuing `KILL QUERY`. The command
connection drains the interrupted response and remains reusable. If cancellation cannot be
delivered, the driver closes that physical connection instead of risking protocol
desynchronization.

## LOCAL INFILE

`LOAD DATA LOCAL INFILE` is disabled by default. Enabling `AllowLoadLocalInfile` allows the server
to request and upload the named local file. Only enable it for a trusted server because the
server chooses the requested path.

## Active server matrix

| Server | Image |
|---|---|
| MySQL 8.4 | `mysql:8.4` |
| MySQL 9.6 | `mysql:9.6` |
| MariaDB 11.8 | `mariadb:11.8` |

Set `MYSQL_IMAGE` when running `Apex.MySqlClient.IntegrationTests` to select one matrix entry.
