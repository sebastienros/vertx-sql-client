# Pipelining and Multiplexing in Vertx.PgClient

This document explains how pipelining and multiplexing are implemented in the C# PostgreSQL client.

## Overview

PostgreSQL supports **pipelining** - the ability to send multiple queries before waiting for responses. This reduces network round-trip latency and improves throughput for workloads with many small queries.

This client provides two levels of pipelining support:

1. **Connection-level pipelining**: Batch multiple queries on a single connection
2. **Multiplexed connections**: Multiple concurrent callers share the same socket

## Connection-Level Pipelining

### PipelineQueryAsync

The simplest form of pipelining batches multiple queries together:

```csharp
await using var connection = new PgConnection(options);
await connection.ConnectAsync();

// Send 3 queries in a single network round-trip
var results = await connection.PipelineQueryAsync(
    "SELECT 1",
    "SELECT 2", 
    "SELECT 3"
);

// results[0], results[1], results[2] contain the respective results
```

**How it works:**
1. All queries are encoded into a single buffer
2. The buffer is sent to PostgreSQL in one write
3. Responses are read sequentially and matched to queries in order

### Pipelining Limit

PostgreSQL has a limit on how many queries can be "in flight" simultaneously. This is exposed as `PipeliningLimit` (default: 256). The client respects this limit when scheduling commands.

## Multiplexed Connections

### The Problem

With traditional connection pooling, each caller gets exclusive access to a connection:

```
Caller A: [acquire conn] [send query] [wait for response] [release conn]
Caller B:                                                   [acquire conn] ...
```

If the pool has 4 connections and 10 callers arrive simultaneously, 6 must wait.

### The Solution: Multiplexing

Multiplexed connections allow multiple callers to share a single socket:

```
Caller A: [send query A] -----> [receive response A]
Caller B:    [send query B] --> [receive response B]
Caller C:      [send query C] > [receive response C]
          |__________________|  |___________________|
             All pipelined        Responses routed
             on one socket        to correct caller
```

### Using Multiplexing

Use `ScheduleAsync` to leverage multiplexed connections:

```csharp
var pool = PgPool.Create(options, new PgPoolOptions { Pipelined = true });

// These can all be called concurrently from different tasks
var task1 = pool.ScheduleAsync("SELECT 1");
var task2 = pool.ScheduleAsync("SELECT 2");
var task3 = pool.ScheduleAsync("SELECT 3");

// All queries may share the same physical connection
var results = await Task.WhenAll(task1, task2, task3);
```

### When to Use Each Method

| Method | Use Case |
|--------|----------|
| `QueryAsync` | Simple one-off queries |
| `PreparedQueryAsync` | Parameterized queries (SQL injection safe) |
| `PipelineQueryAsync` | Batch of known queries from single caller |
| `ScheduleAsync` | High-concurrency scenarios with many callers |
| `GetConnectionAsync` | Need transaction control or session state |

## Architecture

### MultiplexedConnection

The `MultiplexedConnection` class manages multiplexing with two background tasks:

```
┌─────────────────────────────────────────────────────────────┐
│                   MultiplexedConnection                      │
│                                                              │
│  ┌──────────┐    ┌─────────────┐    ┌───────────────────┐  │
│  │ Callers  │───>│   Channel   │───>│ CommandSenderTask │  │
│  └──────────┘    └─────────────┘    └─────────┬─────────┘  │
│                                               │             │
│                                               ▼             │
│  ┌──────────┐    ┌─────────────┐    ┌───────────────────┐  │
│  │ Callers  │<───│   Inflight  │<───│    Socket I/O     │  │
│  │ (await)  │    │    Queue    │    └─────────┬─────────┘  │
│  └──────────┘    └─────────────┘              │             │
│                        ▲                      │             │
│                        │                      ▼             │
│               ┌────────┴────────────────────────────────┐  │
│               │       ResponseDispatcherTask            │  │
│               └─────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

### Components

1. **Channel** (`Channel<PgCommand>`)
   - Bounded channel provides backpressure
   - Multiple writers (callers), single reader (sender task)
   - Capacity: `PipeliningLimit * 2`

2. **CommandSenderTask**
   - Reads commands from the channel
   - Adds them to the inflight queue
   - Sends them to PostgreSQL via the socket

3. **Inflight Queue** (`Queue<PgCommand>`)
   - FIFO queue of commands awaiting responses
   - PostgreSQL processes commands in order, so responses arrive in order

4. **ResponseDispatcherTask**
   - Reads responses from the socket
   - Matches responses to the front of the inflight queue
   - Completes the command's `TaskCompletionSource` to signal the caller

### Command Flow

1. Caller calls `QueryAsync("SELECT 1")`
2. A `SimpleQueryCommand` is created with a `TaskCompletionSource<RowSet>`
3. Command is written to the channel (may wait if channel is full = backpressure)
4. CommandSenderTask picks it up, adds to inflight queue, sends to PostgreSQL
5. ResponseDispatcherTask reads responses (RowDescription, DataRow, CommandComplete, ReadyForQuery)
6. When ReadyForQuery arrives, the command is complete
7. Command is dequeued, its TaskCompletionSource is completed
8. Original caller's `await` resumes with the result

### Thread Safety

- **Channel**: Thread-safe by design (multiple writers, single reader)
- **Inflight Queue**: Protected by `lock (_inflight)`
- **Socket Writes**: Serialized by `SemaphoreSlim _sendLock`
- **Socket Reads**: Single reader (ResponseDispatcherTask)

## Pool Integration

The `PgPool` maintains two types of connections:

```csharp
private readonly List<PooledConnection> _connections = new();           // Regular
private readonly List<MultiplexedConnection> _multiplexedConnections = new();  // Multiplexed
```

### Connection Selection

When `ScheduleAsync` is called:

1. If `Pipelined = false`, falls back to regular `QueryAsync`
2. Otherwise, calls `AcquireMultiplexedAsync`:
   - Finds the multiplexed connection with most available slots
   - If none have capacity, creates a new one (up to `MaxSize`)
   - Returns the best available connection

### Capacity Tracking

Each multiplexed connection tracks:
- `InflightCount`: Current number of pending commands
- `AvailableSlots`: `PipeliningLimit - InflightCount`

The pool prefers connections with more available slots to balance load.

## Performance Considerations

### When Pipelining Helps

- High network latency (reduces round-trips)
- Many small queries (amortizes connection overhead)
- High concurrency (more efficient than one-connection-per-request)

### When Pipelining Doesn't Help

- Single large queries (dominated by execution time)
- Low latency local connections (round-trip cost is minimal)
- Queries that depend on previous results (can't pipeline dependent queries)

### PostgreSQL Behavior

Important: PostgreSQL executes pipelined queries **sequentially**, not in parallel. Pipelining saves network time, not database execution time.

```
Without pipelining:
  Query 1: [send]──RTT──[execute]──RTT──[receive]
  Query 2:                               [send]──RTT──[execute]──RTT──[receive]
  Total: 2 executions + 4 RTTs

With pipelining:
  Query 1: [send]──────────────────[execute]──────[receive]
  Query 2:    [send]──────────────────────[execute]──[receive]
  Total: 2 executions + 2 RTTs (queries share the RTT)
```

## Configuration

```csharp
var poolOptions = new PgPoolOptions
{
    MaxSize = 4,              // Maximum connections (regular + multiplexed)
    Pipelined = true,         // Enable multiplexing for ScheduleAsync
    ConnectionTimeout = 30000 // Timeout for acquiring connections
};

var pool = PgPool.Create(connectionOptions, poolOptions);
```

## Error Handling

If an error occurs during multiplexed execution:

1. **Send error**: Command is removed from inflight, completed with exception
2. **Receive error**: Current command is completed with exception, dispatcher continues
3. **Connection closed**: All inflight commands are completed with `ObjectDisposedException`

Each caller receives their specific error - errors don't affect other callers on the same connection (unless the connection itself fails).
