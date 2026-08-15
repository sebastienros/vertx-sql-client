# Database compatibility

## SQL Server compatibility

### Server versions

The Testcontainers matrix targets SQL Server 2019, 2022, and 2025, matching the
`MSSQL-2019-latest`, `MSSQL-2022-latest`, and `MSSQL-2025-latest` profiles in
`vertx-mssql-client/pom.xml`.

```bash
for version in 2019 2022 2025; do
  MSSQL_IMAGE="mcr.microsoft.com/mssql/server:${version}-latest" \
    dotnet test dotnet/tests/Apex.MsSqlClient.IntegrationTests --no-restore
done
```

SQL authentication is supported. Integrated/Windows authentication, Entra ID,
and access-token authentication are not.

### Encryption

| Mode | Wire behavior |
|---|---|
| `Disable` | No TLS; credentials are not protected on the wire |
| `Optional` | Use full-session TLS when offered by the server |
| `Require` | Require TDS 7.x PRELOGIN-negotiated full-session TLS |
| `Strict` | Require TDS 8.0 TLS before PRELOGIN with `tds/8.0` ALPN |

Server certificate validation remains on unless
`TrustServerCertificate = true`. Strict/TDS 8 requires a server configuration
and certificate that support TDS 8.0.

### Type mappings

| SQL Server type | Apex read type | Parameter type |
|---|---|---|
| `bit` | `bool` | `bool` |
| `tinyint` | `byte` | `byte` through `SqlValue.From` |
| `smallint`, `int`, `bigint` | `short`, `int`, `long` | Same |
| `real`, `float` | `float`, `double` | Same |
| `decimal`, `numeric` | `decimal` | `decimal` (`numeric(38, scale)`) |
| `smallmoney`, `money` | `decimal` | `decimal` |
| `uniqueidentifier` | `Guid` | `Guid` |
| `date` | `DateOnly` | `DateOnly` |
| `time` | `TimeOnly` | `TimeOnly` |
| `datetime`, `smalldatetime`, `datetime2` | `DateTime`; legacy `datetime` is rounded to whole milliseconds | `DateTime` as `datetime2(7)` |
| `datetimeoffset` | `DateTimeOffset` | `DateTimeOffset` |
| `char`, `varchar`, `text` with CP437/850/874/932/936/949/950, CP1250–1258, or UTF-8 collations | `string` | `string` as `nvarchar` |
| `nchar`, `nvarchar`, `ntext`, `xml` | `string` | `string`; JSON parameters as `nvarchar(max)` |
| native `json` (SQL Server 2025+) | UTF-16 `string` | JSON parameters as `nvarchar(max)` |
| `binary`, `varbinary`, `image`, UDT payloads | `byte[]` | `byte[]` or `ReadOnlyMemory<byte>` |
| SQL `NULL` | `null` | `SqlValue.Null` |

Unsupported or malformed TDS type metadata fails explicitly. Table-valued
parameters, spatial/CLR UDT interpretation, `sql_variant`, and bulk copy are not
currently exposed.

### Protocol and lifetime limits

MARS and concurrent requests on one physical connection are not supported.
Commands are submitted in order and serialized; use `MsSqlPool` for parallel
work. Prepared-statement batch execution is therefore a serial batch-equivalent,
not simulated pipelining.

Explicit `PrepareAsync` statements use SQL Server handles: the first execution
uses `sp_prepexec`, later executions use `sp_execute`, and disposal drains
`sp_unprepare`. Parameterized one-shot queries continue to use
Automatic prepared-statement caching is not supported; the shared
`CachePreparedStatements` option is rejected for `Apex.MsSqlClient`. Parameterized one-shot queries continue to use
`sp_executesql`.

`StreamAsync` returns lifetime-safe rows whose values remain valid after the
enumerator advances. `ExecuteReaderAsync` is the borrowed alternative: the
current row must not be retained past the next `ReadAsync`. Cancellation sends
TDS `ATTENTION` and drains its acknowledgement and final `DONE` before reuse.
Both APIs parse tokens across packet boundaries and can return the first row
before the response END_OF_MESSAGE packet arrives. There is no whole-response
64 MB buffer limit. Multiple result sets are streamed in order, and each safe
row retains the columns for the result set that produced it.

### NativeAOT

The three direct transports, options parsers, and parameter codecs are
NativeAOT-compatible. SQL Server also registers `CodePagesEncodingProvider`.
`Apex.AotSmoke` roots representative PostgreSQL, MySQL, and SQL Server options
and values and can be published with `PublishAot=true`.

## PostgreSQL compatibility

## PostgreSQL server versions

The integration matrix targets PostgreSQL 14, 16, and 18. PostgreSQL 17+ direct TLS is covered by protocol-level TLS tests and remains part of the server matrix.

## PostgreSQL type mappings

| PostgreSQL type | Apex type | Text | Binary | Array |
|---|---|---:|---:|---:|
| `bool` | `bool` | Yes | Yes | Yes |
| `int2`, `int4`, `int8` | `short`, `int`, `long` | Yes | Yes | Yes |
| `float4`, `float8` | `float`, `double` | Yes | Yes | Yes |
| `numeric` | `PgNumeric` | Yes | Yes | Yes |
| character, name, text-search | `string` | Yes | Yes for registered built-in OIDs | Yes for built-in arrays |
| custom enum/unknown | `string` | Yes | No; unknown binary OIDs fail explicitly | No |
| `uuid` | `Guid` | Yes | Yes | Yes |
| `date` | `DateOnly` | Yes | Yes | Yes |
| `time` | `TimeOnly` | Yes | Yes | Yes |
| `timetz` | `PgTimeWithTimeZone` | Yes | Yes | Yes |
| `timestamp` | `DateTime` with `Unspecified` kind | Yes | Yes | Yes |
| `timestamptz` | `DateTimeOffset` | Yes | Yes | Yes |
| `interval` | `PgInterval` | Yes | Yes | Yes |
| `bytea` | `byte[]` | Hex text | Yes | Yes |
| `json`, `jsonb` | `JsonElement` | Yes | Yes | Yes |
| geometric types | `PgPoint`, `PgLine`, `PgLineSegment`, `PgBox`, `PgPath`, `PgPolygon`, `PgCircle` | Yes | Yes | Yes |
| `inet`, `cidr` | `PgInet`, `PgCidr` | Yes | Yes | Yes |
| `money` | `PgMoney` | Yes | Yes | Yes |

Date and timestamp infinity values map to the corresponding .NET minimum and maximum values. One-dimensional arrays preserve SQL `NULL` elements as `null` in `object?[]`.

## PostgreSQL intentionally unsupported

For parity with the Vert.x PostgreSQL client, `bit`, `varbit`, `macaddr`, `macaddr8`, `xml`, `oid`, and `void` throw `PgUnsupportedTypeException`. `hstore` uses extension-assigned OIDs and requires a future type-registry lookup before it can be rejected by name.

Multidimensional arrays are not yet supported and currently throw `NotSupportedException`.

## MySQL and MariaDB server versions

The active Vert.x 5.x matrix defines the .NET direct-driver matrix:

| Product | Versions | Coverage |
|---|---|---|
| MySQL | 8.4, 9.6 | text/binary query protocols, caching SHA-2 auth, TLS, cancellation |
| MariaDB | 11.8 | text/binary query protocols, native auth, TLS where advertised |

The older 4.x and 5.x-stable workflows retain MySQL 5.6/5.7/8.0 and MariaDB 10.4
jobs. Those branches are useful compatibility evidence but are not part of this driver's active
release matrix.

## MySQL type mappings

| MySQL type | Apex type | Text | Binary |
|---|---|---:|---:|
| signed integer family | `sbyte`, `short`, `int`, `long` | Yes | Yes |
| unsigned integer family | `byte`, `ushort`, `uint`, `ulong` | Yes | Yes |
| `YEAR` | `int` | Yes | Yes |
| `BIT` | `ulong` | Yes | Yes |
| `FLOAT`, `DOUBLE` | `float`, `double` | Yes | Yes |
| `DECIMAL` | `MySqlDecimal` (`decimal` typed getter) | Yes | Yes |
| character, `ENUM`, `SET` | `string` | Yes | Yes |
| binary and blob | `byte[]` | Yes | Yes |
| `DATE` | `DateOnly` | Yes | Yes |
| `TIME` | `TimeSpan` (`TimeOnly` when within one day) | Yes | Yes |
| `DATETIME`, `TIMESTAMP` | `DateTime` with `Unspecified` kind | Yes | Yes |
| `JSON` | `JsonElement` (`string` getter also available) | Yes | Yes |
| geometry and vector | protocol `byte[]` | Yes | Yes |

Zero dates fail by default and can be mapped to `null` or the corresponding minimum value.
Values outside the range of their requested .NET type fail explicitly.
