# SELECT Statement Allocation Analysis

This document analyzes the memory allocations that occur when executing a SELECT statement returning an integer and string value using the Vertx.PgClient C# library.

## Example Query

```csharp
// Simple Query (text protocol)
var result = await connection.QueryAsync("SELECT 42 as id, 'hello world' as message");

// Or Prepared Query (binary protocol)
var result = await connection.PreparedQueryAsync(
    "SELECT id, message FROM users WHERE id = $1",
    Tuple.Create(1));
```

## Execution Flow Overview

```
User Code
    └── PgConnection.QueryAsync() / PreparedQueryAsync()
        └── PgSocketConnection.QueryAsync() / PreparedQueryAsync()
            ├── PgEncoder (Write query to buffer)
            │   └── SendAsync (Write to network stream)
            ├── ReceiveAsync (Read from network stream)
            │   └── PgDecoder.TryParse (Parse response messages)
            └── ReceiveQueryResultAsync / ReceiveExtendedQueryResultAsync
                └── DecodeRow (Decode data row values)
                    └── PgValue.DecodeBinary/DecodeText
                        └── DataTypeCodec (Decode individual values)
```

---

## Detailed Call Stack Analysis

### 1. Query Execution Entry Point

#### For Simple Query (`QueryAsync`)

```
PgConnection.QueryAsync(sql)                          [No allocation]
  └── PgSocketConnection.QueryAsync(sql)              [No allocation]
      ├── _encoder.Reset()                            [No allocation]
      ├── _encoder.WriteQuery(sql)                    [No allocation - uses pre-allocated buffer]
      │   └── WriteCStringUtf8(sql)                   [Allocates: UTF-8 encoding if > buffer capacity]
      ├── SendAsync()                                 [No allocation - uses existing buffer]
      └── ReceiveQueryResultAsync()                   [See Section 4]
```

#### For Prepared Query (`PreparedQueryAsync`)

```
PgConnection.PreparedQueryAsync(sql, parameters)      [No allocation]
  └── PgSocketConnection.PreparedQueryAsync(...)      [No allocation]
      ├── _encoder.GenerateStatementName()            [Allocates: string + byte[] for statement name]
      ├── _encoder.WriteParse(sql, name)              [No allocation - uses pre-allocated buffer]
      ├── _encoder.WriteDescribe('S', name)           [No allocation]
      ├── _encoder.WriteSync()                        [No allocation]
      ├── SendAsync()                                 [No allocation]
      ├── [Loop: Parse Description Responses]         [See Section 2]
      ├── _encoder.WriteBind(...)                     [See Section 3]
      ├── _encoder.WriteExecute()                     [No allocation]
      ├── _encoder.WriteClose()                       [No allocation (if not caching)]
      ├── _encoder.WriteSync()                        [No allocation]
      ├── SendAsync()                                 [No allocation]
      └── ReceiveExtendedQueryResultAsync()           [See Section 4]
```

---

### 2. Response Parsing (PgDecoder)

```
ReceiveAsync()                                        [No allocation]
  └── _decoder.TryParse(buffer)                       [No allocation]
      └── ParseMessage(messageType, payload)          [See allocations per message type]
```

#### Message-specific Allocations:

| Message Type | Method | Allocations |
|-------------|--------|-------------|
| `ParseCompleteResponse` | `new ParseCompleteResponse()` | 1x object (record struct) |
| `ParameterDescriptionResponse` | `ParseParameterDescription()` | 1x `int[]` for type OIDs |
| `RowDescriptionResponse` | `ParseRowDescription()` | 1x `PgColumnDesc[]` + N×`PgColumnDesc` objects + N×strings (column names) |
| `DataRowResponse` | `ParseDataRow()` | 1x `byte[][]` + N×`byte[]` per column value |
| `BindCompleteResponse` | `new BindCompleteResponse()` | 1x object (record struct) |
| `CommandCompleteResponse` | `ParseCommandComplete()` | 1x string (command tag) |
| `ReadyForQueryResponse` | `new ReadyForQueryResponse(status)` | 1x object (record struct) |

---

### 3. Parameter Encoding (WriteBind)

For a query with parameters (e.g., `SELECT ... WHERE id = $1` with int parameter):

```
_encoder.WriteBind(statementName, portal, parameters, paramTypes)
  └── [For each parameter]
      └── value.EncodeBinary(buffer, dataType)        [No allocation for int/primitives]
          └── DataTypeCodec.EncodeInt32Binary(...)    [No allocation - writes to buffer]
```

#### Per-Type Encoding Allocations:

| Type | EncodeBinary Method | Allocations |
|------|-------------------|-------------|
| `bool` | `EncodeBoolBinary` | None |
| `short` | `EncodeInt16Binary` | None |
| `int` | `EncodeInt32Binary` | None |
| `long` | `EncodeInt64Binary` | None |
| `float` | `EncodeFloatBinary` | None |
| `double` | `EncodeDoubleBinary` | None |
| `string` | `EncodeStringBinary` | None (writes directly to buffer) |
| `DateTime` | `EncodeTimestampBinary` | None |
| `Guid` | `EncodeGuidBinary` | None |
| `byte[]` | `EncodeByteArrayBinary` | None |
| `decimal` | `EncodeNumericBinary` | 1x string (via ToString) |
| `int[]`, `string[]`, etc. | `Encode*ArrayBinary` | Intermediate `Cast<T?>().ToArray()` |

---

### 4. Result Set Construction

#### ReceiveQueryResultAsync (Simple Query - Text Protocol)

```
ReceiveQueryResultAsync()
  ├── new List<Row>()                                 [Allocates: 1x List<Row>]
  ├── [Loop: Process each message]
  │   ├── RowDescriptionResponse:
  │   │   └── columnDesc = rd.Columns                 [Already allocated in ParseRowDescription]
  │   │
  │   ├── DataRowResponse:
  │   │   └── DecodeRow(dataRow.Values, columnDesc)   [See Section 5]
  │   │       └── rows.Add(row)                       [List may resize]
  │   │
  │   └── CommandCompleteResponse:
  │       └── ParseRowsAffected(cmd.Tag)              [No allocation - uses Span]
  │
  └── new RowSet(rows, columnNames, rowsAffected)
      ├── columnDesc.Select(c => c.Name).ToArray()    [Allocates: 1x string[]]
      └── new PgRowDescriptor(columns)                [Allocates: 1x PgRowDescriptor]
```

#### ReceiveExtendedQueryResultAsync (Prepared Query - Binary Protocol)

Similar to above but uses pre-existing `rowDesc` from Describe phase.

---

### 5. Row Decoding (Critical Allocation Path)

```
DecodeRow(byte[][] values, PgColumnDesc[] columnDesc)
  ├── new PgValue[values.Length]                      [Allocates: 1x PgValue[]]
  │
  ├── [For each column value]
  │   ├── If NULL:
  │   │   └── PgValue.CreateNull(dataType)            [No allocation - struct]
  │   │
  │   └── If Binary format:
  │       └── PgValue.DecodeBinary(dataType, buffer)  [See Section 6]
  │
  ├── columnDesc.Select(c => c.Name).ToArray()        [Allocates: 1x string[] per row!]
  │
  └── new Row(decodedValues, columnNames)             [Allocates: 1x Row object]
```

**🔴 IMPROVEMENT OPPORTUNITY #1**: The `columnDesc.Select(c => c.Name).ToArray()` in `DecodeRow` creates a new string array for **every row**. This should be cached/shared across rows.

---

### 6. Value Decoding (PgValue.DecodeBinary)

```
PgValue.DecodeBinary(DataType dataType, ReadOnlySpan<byte> buffer)
  └── [Switch on dataType.Id]
```

#### Per-Type Decoding Allocations:

| Type | Decode Method | Allocations |
|------|--------------|-------------|
| `Bool` | `DecodeBoolBinary` | None (stored in primitive) |
| `Int2` | `DecodeInt16Binary` | None (stored in primitive) |
| `Int4` | `DecodeInt32Binary` | None (stored in primitive) |
| `Int8` | `DecodeInt64Binary` | None (stored in primitive) |
| `Float4` | `DecodeFloatBinary` | None (stored in primitive) |
| `Float8` | `DecodeDoubleBinary` | None (stored in primitive) |
| `Text/Varchar` | `DecodeStringBinary` | **1x string** |
| `Date` | `DecodeDateBinary` | None (stored in primitive) |
| `Time` | `DecodeTimeBinary` | None (stored in primitive) |
| `Timestamp` | `DecodeTimestampBinary` | None (stored in primitive) |
| `Timestamptz` | `DecodeTimestampTzBinary` | **1x DateTimeOffset (boxed)** |
| `Bytea` | `DecodeByteArrayBinary` | **1x byte[] (ToArray())** |
| `Uuid` | `DecodeGuidBinary` | **1x Guid (boxed)** |
| `Json/Jsonb` | `DecodeJsonBinary` | **1x string** |
| `Numeric` | `DecodeNumericBinary` | **1x decimal (boxed)** |
| `Point` | `DecodePointBinary` | **1x Point object** |
| `Interval` | `DecodeIntervalBinary` | **1x Interval object** |
| `Inet` | `DecodeInetBinary` | **1x Inet + 1x byte[] + 1x IPAddress** |
| Array types | `Decode*ArrayBinary` | **1x T[]** |

---

## Allocation Summary for `SELECT id (int), message (text)` with 1 Row

### Simple Query Path (Text Protocol)

| Stage | Object Type | Count | Size (Est.) |
|-------|-------------|-------|-------------|
| Encoder buffer | byte[] | 0 (reused) | - |
| Receive buffer | byte[] | 0 (rented from pool) | - |
| ParseRowDescription | PgColumnDesc[] | 1 | 24 bytes + refs |
| ParseRowDescription | PgColumnDesc | 2 | 2×72 bytes |
| ParseRowDescription | string (col names) | 2 | ~40 bytes each |
| ParseDataRow | byte[][] | 1 | 24 bytes + refs |
| ParseDataRow | byte[] | 2 | 4 + N bytes each |
| DecodeRow | PgValue[] | 1 | 24 bytes + 2×24 bytes |
| DecodeRow | string[] (col names) | 0 ✅ | Cached once per result set |
| PgValue (int) | - | 0 | Stored in struct |
| PgValue (string) | string | 1 | N + ~26 bytes |
| Row | Row object | 1 | 32 bytes |
| RowSet | List<Row> | 1 | 32 bytes |
| RowSet | RowSet | 1 | 40 bytes |
| RowSet | string[] | 1 | 24 bytes + refs (shared with rows) |
| RowSet | PgRowDescriptor | 1 | 24 bytes |
| CommandComplete | string | 1 | ~40 bytes |

**Approximate Total for 1 row: ~14-18 allocations, ~450-750 bytes**

### For N Rows

Per additional row:
- 1× byte[][] + N×byte[] (in ParseDataRow)
- 1× PgValue[]
- 0× string[] (column names) ✅ **Now shared across all rows**
- 1× string (message value)
- 1× Row object

---

## Identified Improvement Opportunities

### ✅ Implemented

#### 1. Column Name Array Per Row (DecodeRow) - FIXED
**Location**: `PgSocketConnection.cs` - `ReceiveQueryResultAsync` and `ReceiveExtendedQueryResultAsync`
**Original Issue**: Created a new `string[]` for every row decoded via `columnDesc.Select(c => c.Name).ToArray()`.
**Solution**: Column names array is now created once when the `RowDescriptionResponse` is received and shared across all rows in the result set.

### 🔴 High Priority (Remaining)

#### 2. DataRow byte[] Per Column (ParseDataRow) - FIXED
**Location**: `PgDecoder.cs`, `PgSocketConnection.cs`, `Response.cs`
**Original Issue**: Created a new `byte[]` for every column in every row via `payload.Slice(pos, length).ToArray()`.
**Solution**: Added direct decoding path that skips intermediate byte[] allocations:
- New `DecodeDataRowDirect()` method decodes values directly from the payload span to PgValue[]
- New `DecodedDataRowResponse` type holds pre-decoded PgValue array
- `TryParse()` now accepts optional column descriptors and uses direct decoding when available
- `ReceiveQueryResultAsync` and `ReceiveExtendedQueryResultAsync` pass column descriptors to enable direct decoding

### 🟡 Medium Priority

#### 3. Large Value Type Boxing - FIXED (Guid, DateTimeOffset)
**Location**: `PgValue.cs`
**Original Issue**: `decimal`, `Guid`, `DateTimeOffset` were boxed because they exceed 8 bytes.
**Solution**: Added a second primitive field `_primitiveValue2` to store 16-byte value types without boxing:
- `Guid` (16 bytes): stored as two 8-byte longs using `Unsafe.ReadUnaligned`/`WriteUnaligned`
- `DateTimeOffset` (12 bytes): stored as UTC ticks in `_primitiveValue` and offset minutes in `_primitiveValue2`
- `decimal` (16 bytes): still boxed due to complexity of its internal representation

#### 4. Array Encoding Intermediate Allocations - FIXED
**Location**: `DataTypeCodec.cs:719-785`
**Original Issue**: Used `array.Cast<T?>().ToArray()` which created intermediate nullable arrays.
**Solution**: Added `EncodeValueTypeArrayBinary<T>` method that encodes value type arrays directly without creating intermediate allocations. Since value types cannot be null, we skip the nullable conversion entirely.

### 🟢 Low Priority

#### 5. Statement Name Generation - FIXED
**Location**: `PgEncoder.cs:34-75`
**Original Issue**: Allocated string via interpolation and then byte[] for each new statement.
**Solution**: Now uses a pre-allocated buffer for hex conversion, eliminating the intermediate string allocation. Only the final byte[] result is allocated.

#### 6. List Resizing in ReceiveQueryResultAsync - FIXED
**Location**: `PgSocketConnection.cs`
**Original Issue**: List was created with default capacity (0), causing multiple reallocations for typical queries.
**Solution**: List is now created with initial capacity of 16, reducing reallocations for most query result sets.

---

## Potential Buffer Pooling Strategies

### 1. Receive Buffer (Already Implemented ✅)
```csharp
_receiveBuffer = ArrayPool<byte>.Shared.Rent(65536);
```

### 2. DataRow Value Buffers (Not Implemented)
Instead of allocating `byte[]` per column:
- Decode values directly from the receive buffer span
- Or use `ArrayPool<byte>.Shared` for value buffers

### 3. Row Object Pooling (Not Implemented)
For high-throughput scenarios:
- Pool `Row` objects with `PgValue[]` arrays
- Pool `RowSet` objects with `List<Row>`

---

## Benchmark Validation

The existing benchmarks in `Vertx.PgClient.Benchmarks` can validate allocations:

```bash
cd vertx-pg-client/csharp
dotnet run -c Release --project Vertx.PgClient.Benchmarks -- --filter "*Fortune*" --memory
```

The `[MemoryDiagnoser]` attribute on benchmark classes reports:
- Total allocated bytes
- Allocation count
- Gen0/Gen1/Gen2 collections

### Benchmark Results

Benchmark: `SELECT all fortunes (prepared query)` - Returns 12 rows, each with `int` and `string` columns.

| Version | Allocated Memory | Time |
|---------|-----------------|------|
| **Before** (baseline) | **7.9 KB** | 226.9 μs |
| **After** (optimized) | **5.06 KB** | 224.6 μs |
| **Improvement** | **36% less memory** | ~1% faster |

---

## Summary

For a simple `SELECT id, message FROM table` returning 1 row with an int and string:

| Category | Allocations | Notes |
|----------|-------------|-------|
| Protocol structures | ~6-8 | Records, arrays for messages |
| Per-row overhead | 2-3 | PgValue[], Row (byte[][] and string[] eliminated) |
| Value storage | 1-2 | String value only (Guid/DateTimeOffset no longer boxed) |
| Result set | 3-4 | RowSet, List, descriptor |

**All Optimizations Completed**:
1. ✅ Column names cached once per result set (not per row)
2. ✅ Direct DataRow decoding skips intermediate byte[][] allocations
3. ✅ Guid and DateTimeOffset stored without boxing
4. ✅ Array encoding without intermediate nullable arrays
5. ✅ Statement name generation without string interpolation
6. ✅ List pre-allocation reduces resize operations
