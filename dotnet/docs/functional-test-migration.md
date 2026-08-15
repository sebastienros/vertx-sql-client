# Vert.x Functional Test Migration

This inventory tracks behavioral coverage migrated from the Vert.x SQL client test suites to Apex. A migrated test may cover several Java tests when the same behavior is data-driven in .NET.

## Shared SQL Client

Sources: `ConnectionTestBase`, `TransactionTestBase`, `PreparedQueryTestBase`, `PreparedBatchTestBase`, `CollectorTestBase`, and `ConnectionAutoRetryTestBase`.

Migrated:

- Connection, scalar query, parameters, prepared execution, and affected rows
- Explicit commit, explicit rollback, and rollback on disposal
- Prepared batch execution and empty batches
- Cursor paging, streaming, cancellation, and connection reuse
- Pool concurrency and reader lease pinning
- Mapping and collection, including callback failure propagation
- Prepare syntax errors, missing parameters, and cursor disposal idempotency
- Invalid database, username, and password failures
- Reconnect-attempt exhaustion and configured retry intervals
- Null-first, extra, and changing prepared parameter types
- Prepared stream cancellation and connection reuse
- Cross-connection transaction visibility and pool release after transactional work
- Activities and metrics for direct, prepared, pooled, and failed queries
- Prepared stream failure propagation and successful/failing in-flight disposal
- Prepared-cache concurrency, eviction, size, and SQL-length limits where supported
- Queued query draining and immediate post-disposal rejection
- Direct and pooled reconnect success after transient handshake failures
- Driver-specific incompatible parameter coercion and mid-batch failure recovery

## PostgreSQL

Sources: `PgClientTestBase`, `PgConnectionTestBase`, `PgPoolTest`, `PreparedStatementTestBase`, `PreparedStatementCachedTest`, `CloseConnectionTest`, `NoticeTest`, `PubSubTest`, `TLSTest`, and the `data` and `tck` packages.

Migrated:

- Query, transaction, pool, pipeline, prepared statement, batch, cursor, and stream behavior
- SQL errors, cancellation, metadata, authentication, TLS, Unix sockets, and PgBouncer
- Prepared-cache invalidation and reprepare
- Notifications and subscription reconnect
- Notice fields, direct cancellation, quoted subscribe/unsubscribe, and channel validation
- Single-row `INSERT ... RETURNING`
- Text and binary scalar, array, enum, network, temporal, numeric, JSON, money, interval, and geometric decoding
- Deferred-constraint commit failure and aborted-transaction recovery
- Prepared-cache concurrency, bounded eviction, and server-side size limits
- Prepared-cache SQL-length bypass
- Null parameter encoding across supported PostgreSQL type families
- BCL alternative scalar and array mappings for numeric, temporal, text, network, MAC, and bit types
- Geometric scalar parameter encoding
- Geometric array, interval, time-zone, network, CIDR, and money parameter encoding
- Subscriber reconnect-policy exhaustion, queued pool replacement after server loss, and partial-page prepared stream errors
- Disable, Allow, Prefer, Require, VerifyCa, and VerifyFull TLS modes with CA trust, hostname validation, and direct ALPN

## MySQL

Sources: `MySQLConnectionTest`, `MySQLPoolTest`, `MySQLPreparedQueryTest`, `MySQLPreparedBatchTest`, `MySQLBatchInsertExceptionTest`, `MySQLUtilityCommandTest`, `MySQLCollationTest`, `MySQLAuthenticationTest`, `MySQLUnixDomainSocketTest`, `MySQLLocalInfileTest`, and datatype tests.

Migrated:

- Query, prepared statement, transaction, batch, result-chain, stream, cancellation, and pool behavior
- TLS, native and caching-SHA2 authentication, local infile, metadata, and server image matrix
- Numeric, text, binary, bit, temporal, JSON, enum, set, null, and zero-date decoding
- Batch failure metadata, ping/reset, affected-row modes, extended negative `TIME`, and all zero-date modes
- Prepared parameter type changes plus cache concurrency and bounded eviction
- Prepared-cache SQL-length bypass
- Fractional `TIME` and `DATETIME` column precision
- Empty, chunked, and greater-than-16-MiB local infile uploads
- Connection/table/column collations, emoji, and prepared binding failure recovery
- Unix-domain-socket parsing, queries, prepared batches, and TLS rejection
- Clear-password over explicit TLS and SHA-256 over TLS or retrieved RSA public keys
- ProxySQL prepared queries and serialized prepared batches on a dedicated container network
- BCL alternative mappings for numeric, text, network, MAC, and bit values

## Microsoft SQL Server

Sources: `MSSQLConnectionTest`, `MSSQLQueriesTest`, `MSSQLPreparedQueryTest`, `MSSQLTransactionTest`, `MSSQLEncryptionTest`, `MSSQLForcedEncryptionTest`, `MSSQLStrictEncryptionTest`, and datatype tests.

Migrated:

- Query, RPC parameters, metadata, transactions, errors, info events, prepared statements, batches, readers, and streams
- Cancellation with ATTENTION, pooling, result sets, row lifetimes, packet fragmentation, PLP, collations, JSON, and core scalar codecs
- Failed lazy preparation and connection recovery
- Legacy date/time, money, text/image LOBs, nullable getters, and large-decimal boundaries
- `OUTPUT` clauses and stored-procedure result sets
- Full nullable scalar getter matrix and XML text decoding
- Nullable scalar parameter encoding and fixed/max parameter length boundaries
- Abrupt server close during an in-flight command
- BCL alternative mappings for numeric, temporal, text, network, MAC, and bit values
- Optional/Require TLS, certificate trust callbacks and hostname errors, Strict TDS 8.0 ALPN, and safe pool replacement after server loss

## Not Applicable or Unavailable

- Vert.x driver SPI/service loading, event-loop/context affinity, verticle undeploy, and row recycling have no Apex public equivalent.
- Vert.x pool callback metrics differ from Apex `ActivitySource` and `Meter` diagnostics.
- PostgreSQL multi-host load balancing, Java enum conversion, `tsquery`, `tsvector`, and structured composite mapping have no Apex API.
- MySQL structured geometry is exposed by Apex as raw WKB; change-user, statistics, and set-option utility commands have no Apex API.
- Java `JsonObject`, `JsonArray`, enum coercion, and `Row.toJson` tests are not .NET API contracts.
- Prepared `SELECT` batch row chains are not represented by Apex's `ExecuteBatchAsync`, which returns command results.
- Apex currently has no DB2 or Oracle driver, so those Vert.x suites cannot be migrated.

## Contract Divergences

- MySQL follows server coercion semantics for incompatible integer strings and returns zero with a warning; PostgreSQL and SQL Server reject the same value. All three paths preserve connection reuse.
- MySQL exposes `MySqlBatchException.FailedIndex` and the completed result prefix. PostgreSQL and SQL Server expose their native server exception; tests verify that nonfailing submissions complete and the protocol remains synchronized.
- Apex retries transient failures while establishing a new physical connection. It does not transparently replay commands after an established connection is lost because their server-side outcome may be ambiguous.
- After established PostgreSQL, MySQL, or SQL Server connections fail, Apex pools discard the dead physical connection and satisfy queued acquisitions with a replacement. This is the safe Apex equivalent of transparent reconnect.
- ProxySQL prepared statements are executed with `PipeliningLimit=1` because the layer-7 proxy does not preserve prepared-statement scope for multiple in-flight commands on one frontend connection.