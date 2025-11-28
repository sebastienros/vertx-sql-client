// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using Xunit;

namespace Vertx.PgClient.Tests;

/// <summary>
/// Tests for transaction support.
/// </summary>
public class TransactionTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public TransactionTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BeginAndCommit_PersistsData()
    {
        var tableName = $"tx_test_{Guid.NewGuid():N}";

        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        await connection.QueryAsync($"CREATE TABLE {tableName} (id INT PRIMARY KEY, name TEXT)");

        try
        {
            // Begin transaction
            await using var tx = await connection.BeginTransactionAsync();
            Assert.True(tx.IsActive);
            Assert.Equal('T', connection.TransactionStatus);

            // Insert data
            await tx.QueryAsync($"INSERT INTO {tableName} (id, name) VALUES (1, 'Alice')");
            await tx.QueryAsync($"INSERT INTO {tableName} (id, name) VALUES (2, 'Bob')");

            // Commit
            await tx.CommitAsync();
            Assert.False(tx.IsActive);
            Assert.Equal('I', connection.TransactionStatus);

            // Verify data persisted
            var result = await connection.QueryAsync($"SELECT COUNT(*) FROM {tableName}");
            Assert.Equal(2L, result[0].GetValue(0).GetLong());
        }
        finally
        {
            await connection.QueryAsync($"DROP TABLE {tableName}");
        }
    }

    [Fact]
    public async Task BeginAndRollback_DiscardsData()
    {
        var tableName = $"tx_test_{Guid.NewGuid():N}";

        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        await connection.QueryAsync($"CREATE TABLE {tableName} (id INT PRIMARY KEY, name TEXT)");

        try
        {
            // Begin transaction
            await using var tx = await connection.BeginTransactionAsync();

            // Insert data
            await tx.QueryAsync($"INSERT INTO {tableName} (id, name) VALUES (1, 'Alice')");
            await tx.QueryAsync($"INSERT INTO {tableName} (id, name) VALUES (2, 'Bob')");

            // Rollback
            await tx.RollbackAsync();
            Assert.False(tx.IsActive);
            Assert.Equal('I', connection.TransactionStatus);

            // Verify data was not persisted
            var result = await connection.QueryAsync($"SELECT COUNT(*) FROM {tableName}");
            Assert.Equal(0L, result[0].GetValue(0).GetLong());
        }
        finally
        {
            await connection.QueryAsync($"DROP TABLE {tableName}");
        }
    }

    [Fact]
    public async Task Dispose_WithoutCommit_RollsBack()
    {
        var tableName = $"tx_test_{Guid.NewGuid():N}";

        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        await connection.QueryAsync($"CREATE TABLE {tableName} (id INT PRIMARY KEY, name TEXT)");

        try
        {
            // Begin transaction and insert data, then dispose without committing
            {
                await using var tx = await connection.BeginTransactionAsync();
                await tx.QueryAsync($"INSERT INTO {tableName} (id, name) VALUES (1, 'Alice')");
                // No commit - dispose will rollback
            }

            // Connection should be back to idle
            Assert.Equal('I', connection.TransactionStatus);

            // Verify data was rolled back
            var result = await connection.QueryAsync($"SELECT COUNT(*) FROM {tableName}");
            Assert.Equal(0L, result[0].GetValue(0).GetLong());
        }
        finally
        {
            await connection.QueryAsync($"DROP TABLE {tableName}");
        }
    }

    [Fact]
    public async Task Savepoint_AllowsPartialRollback()
    {
        var tableName = $"tx_test_{Guid.NewGuid():N}";

        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        await connection.QueryAsync($"CREATE TABLE {tableName} (id INT PRIMARY KEY, name TEXT)");

        try
        {
            await using var tx = await connection.BeginTransactionAsync();

            // Insert first row
            await tx.QueryAsync($"INSERT INTO {tableName} (id, name) VALUES (1, 'Alice')");

            // Create savepoint
            await tx.SavepointAsync("sp1");

            // Insert second row
            await tx.QueryAsync($"INSERT INTO {tableName} (id, name) VALUES (2, 'Bob')");

            // Rollback to savepoint (discards Bob)
            await tx.RollbackToSavepointAsync("sp1");

            // Insert third row
            await tx.QueryAsync($"INSERT INTO {tableName} (id, name) VALUES (3, 'Charlie')");

            // Commit
            await tx.CommitAsync();

            // Verify: Alice and Charlie, but not Bob
            var result = await connection.QueryAsync($"SELECT name FROM {tableName} ORDER BY id");
            Assert.Equal(2, result.Count);
            Assert.Equal("Alice", result[0].GetValue(0).GetString());
            Assert.Equal("Charlie", result[1].GetValue(0).GetString());
        }
        finally
        {
            await connection.QueryAsync($"DROP TABLE {tableName}");
        }
    }

    [Fact]
    public async Task ReleaseSavepoint_RemovesSavepoint()
    {
        var tableName = $"tx_test_{Guid.NewGuid():N}";

        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        await connection.QueryAsync($"CREATE TABLE {tableName} (id INT PRIMARY KEY, name TEXT)");

        try
        {
            await using var tx = await connection.BeginTransactionAsync();

            await tx.QueryAsync($"INSERT INTO {tableName} (id, name) VALUES (1, 'Alice')");
            await tx.SavepointAsync("sp1");
            await tx.QueryAsync($"INSERT INTO {tableName} (id, name) VALUES (2, 'Bob')");

            // Release the savepoint (can't rollback to it anymore)
            await tx.ReleaseSavepointAsync("sp1");

            await tx.CommitAsync();

            // Verify both rows persisted
            var result = await connection.QueryAsync($"SELECT COUNT(*) FROM {tableName}");
            Assert.Equal(2L, result[0].GetValue(0).GetLong());
        }
        finally
        {
            await connection.QueryAsync($"DROP TABLE {tableName}");
        }
    }

    [Fact]
    public async Task IsolationLevel_Serializable()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());

        var options = new TransactionOptions
        {
            IsolationLevel = IsolationLevel.Serializable
        };

        await using var tx = await connection.BeginTransactionAsync(options);
        Assert.True(tx.IsActive);

        // Verify isolation level
        var result = await tx.QueryAsync("SHOW transaction_isolation");
        Assert.Equal("serializable", result[0].GetValue(0).GetString());

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task IsolationLevel_RepeatableRead()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());

        var options = new TransactionOptions
        {
            IsolationLevel = IsolationLevel.RepeatableRead
        };

        await using var tx = await connection.BeginTransactionAsync(options);

        var result = await tx.QueryAsync("SHOW transaction_isolation");
        Assert.Equal("repeatable read", result[0].GetValue(0).GetString());

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task AccessMode_ReadOnly()
    {
        var tableName = $"tx_test_{Guid.NewGuid():N}";

        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        await connection.QueryAsync($"CREATE TABLE {tableName} (id INT PRIMARY KEY)");

        try
        {
            var options = new TransactionOptions
            {
                AccessMode = TransactionAccessMode.ReadOnly
            };

            await using var tx = await connection.BeginTransactionAsync(options);

            // Reading should work
            await tx.QueryAsync($"SELECT * FROM {tableName}");

            // Writing should fail
            var ex = await Assert.ThrowsAsync<PgException>(
                () => tx.QueryAsync($"INSERT INTO {tableName} (id) VALUES (1)").AsTask());

            Assert.Contains("read-only", ex.Message.ToLower());

            await tx.RollbackAsync();
        }
        finally
        {
            await connection.QueryAsync($"DROP TABLE {tableName}");
        }
    }

    [Fact]
    public async Task PreparedQuery_WithinTransaction()
    {
        var tableName = $"tx_test_{Guid.NewGuid():N}";

        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        await connection.QueryAsync($"CREATE TABLE {tableName} (id INT PRIMARY KEY, value INT)");

        try
        {
            await using var tx = await connection.BeginTransactionAsync();

            // Use prepared query within transaction
            for (int i = 0; i < 10; i++)
            {
                await tx.PreparedQueryAsync(
                    $"INSERT INTO {tableName} (id, value) VALUES ($1, $2)",
                    Tuple.Create(i, i * 10));
            }

            await tx.CommitAsync();

            // Verify
            var result = await connection.QueryAsync($"SELECT SUM(value) FROM {tableName}");
            Assert.Equal(450L, result[0].GetValue(0).GetLong()); // 0+10+20+...+90 = 450
        }
        finally
        {
            await connection.QueryAsync($"DROP TABLE {tableName}");
        }
    }

    [Fact]
    public async Task Transaction_CannotBeUsedAfterCommit()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        await using var tx = await connection.BeginTransactionAsync();

        await tx.CommitAsync();

        // Transaction is no longer active
        Assert.False(tx.IsActive);

        // Any operation should throw
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tx.QueryAsync("SELECT 1").AsTask());
    }

    [Fact]
    public async Task Transaction_CannotBeUsedAfterRollback()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        await using var tx = await connection.BeginTransactionAsync();

        await tx.RollbackAsync();

        Assert.False(tx.IsActive);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tx.QueryAsync("SELECT 1").AsTask());
    }

    [Fact]
    public async Task NestedTransaction_NotSupported()
    {
        await using var connection = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        await using var tx = await connection.BeginTransactionAsync();

        // Trying to begin another transaction on the same connection should fail
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => connection.BeginTransactionAsync().AsTask());

        await tx.RollbackAsync();
    }

    // Pool transaction tests

    [Fact]
    public async Task Pool_BeginTransaction_AcquiresConnection()
    {
        await using var pool = PgPool.Create(_fixture.CreateConnectOptions(), new PgPoolOptions { MaxSize = 2 });
        var tableName = $"tx_test_{Guid.NewGuid():N}";

        await pool.QueryAsync($"CREATE TABLE {tableName} (id INT PRIMARY KEY, name TEXT)");

        try
        {
            await using var tx = await pool.BeginTransactionAsync();
            Assert.True(tx.IsActive);

            await tx.QueryAsync($"INSERT INTO {tableName} (id, name) VALUES (1, 'Alice')");
            await tx.CommitAsync();

            var result = await pool.QueryAsync($"SELECT COUNT(*) FROM {tableName}");
            Assert.Equal(1L, result[0].GetValue(0).GetLong());
        }
        finally
        {
            await pool.QueryAsync($"DROP TABLE {tableName}");
        }
    }

    [Fact]
    public async Task Pool_TransactionDispose_ReturnsConnectionToPool()
    {
        await using var pool = PgPool.Create(_fixture.CreateConnectOptions(), new PgPoolOptions { MaxSize = 1 });
        var tableName = $"tx_test_{Guid.NewGuid():N}";

        await pool.QueryAsync($"CREATE TABLE {tableName} (id INT PRIMARY KEY)");

        try
        {
            // Acquire transaction (holds the only connection)
            {
                await using var tx = await pool.BeginTransactionAsync();
                await tx.QueryAsync($"INSERT INTO {tableName} (id) VALUES (1)");
                await tx.CommitAsync();
                // Transaction disposes, connection returns to pool
            }

            // Should be able to get another connection immediately
            var result = await pool.QueryAsync($"SELECT COUNT(*) FROM {tableName}");
            Assert.Equal(1L, result[0].GetValue(0).GetLong());
        }
        finally
        {
            await pool.QueryAsync($"DROP TABLE {tableName}");
        }
    }

    [Fact]
    public async Task Pool_WithTransactionAsync_CommitsOnSuccess()
    {
        await using var pool = PgPool.Create(_fixture.CreateConnectOptions(), new PgPoolOptions { MaxSize = 2 });
        var tableName = $"tx_test_{Guid.NewGuid():N}";

        await pool.QueryAsync($"CREATE TABLE {tableName} (id INT PRIMARY KEY, name TEXT)");

        try
        {
            var count = await pool.WithTransactionAsync(async tx =>
            {
                await tx.QueryAsync($"INSERT INTO {tableName} (id, name) VALUES (1, 'Alice')");
                await tx.QueryAsync($"INSERT INTO {tableName} (id, name) VALUES (2, 'Bob')");
                return 2;
            });

            Assert.Equal(2, count);

            var result = await pool.QueryAsync($"SELECT COUNT(*) FROM {tableName}");
            Assert.Equal(2L, result[0].GetValue(0).GetLong());
        }
        finally
        {
            await pool.QueryAsync($"DROP TABLE {tableName}");
        }
    }

    [Fact]
    public async Task Pool_WithTransactionAsync_RollsBackOnException()
    {
        await using var pool = PgPool.Create(_fixture.CreateConnectOptions(), new PgPoolOptions { MaxSize = 2 });
        var tableName = $"tx_test_{Guid.NewGuid():N}";

        await pool.QueryAsync($"CREATE TABLE {tableName} (id INT PRIMARY KEY, name TEXT)");

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await pool.WithTransactionAsync(async tx =>
                {
                    await tx.QueryAsync($"INSERT INTO {tableName} (id, name) VALUES (1, 'Alice')");
                    throw new InvalidOperationException("Simulated error");
                });
            });

            // Data should have been rolled back
            var result = await pool.QueryAsync($"SELECT COUNT(*) FROM {tableName}");
            Assert.Equal(0L, result[0].GetValue(0).GetLong());
        }
        finally
        {
            await pool.QueryAsync($"DROP TABLE {tableName}");
        }
    }

    [Fact]
    public async Task Pool_WithTransactionAsync_WithOptions()
    {
        await using var pool = PgPool.Create(_fixture.CreateConnectOptions(), new PgPoolOptions { MaxSize = 2 });

        var options = new TransactionOptions
        {
            IsolationLevel = IsolationLevel.Serializable
        };

        var isolation = await pool.WithTransactionAsync(async tx =>
        {
            var result = await tx.QueryAsync("SHOW transaction_isolation");
            return result[0].GetValue(0).GetString();
        }, options);

        Assert.Equal("serializable", isolation);
    }

    [Fact]
    public async Task Pool_Transaction_SavepointSupport()
    {
        await using var pool = PgPool.Create(_fixture.CreateConnectOptions(), new PgPoolOptions { MaxSize = 2 });
        var tableName = $"tx_test_{Guid.NewGuid():N}";

        await pool.QueryAsync($"CREATE TABLE {tableName} (id INT PRIMARY KEY, name TEXT)");

        try
        {
            await using var tx = await pool.BeginTransactionAsync();

            await tx.QueryAsync($"INSERT INTO {tableName} (id, name) VALUES (1, 'Alice')");
            await tx.SavepointAsync("sp1");
            await tx.QueryAsync($"INSERT INTO {tableName} (id, name) VALUES (2, 'Bob')");
            await tx.RollbackToSavepointAsync("sp1");
            await tx.QueryAsync($"INSERT INTO {tableName} (id, name) VALUES (3, 'Charlie')");
            await tx.CommitAsync();

            var result = await pool.QueryAsync($"SELECT name FROM {tableName} ORDER BY id");
            Assert.Equal(2, result.Count);
            Assert.Equal("Alice", result[0].GetValue(0).GetString());
            Assert.Equal("Charlie", result[1].GetValue(0).GetString());
        }
        finally
        {
            await pool.QueryAsync($"DROP TABLE IF EXISTS {tableName}");
        }
    }
}
