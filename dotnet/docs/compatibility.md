# Database compatibility

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
