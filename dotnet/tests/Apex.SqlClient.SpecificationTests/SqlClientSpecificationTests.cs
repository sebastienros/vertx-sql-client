/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

namespace Apex.SqlClient.SpecificationTests;

[TestClass]
public abstract class SqlClientSpecificationTests
{
    protected abstract ValueTask<ISqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default);

    protected abstract ISqlPool CreatePool();

    protected abstract string ParameterizedScalarSql { get; }

    protected abstract string CreateTemporaryTableSql { get; }

    protected abstract string InsertTemporaryValueSql { get; }

    protected abstract string CountTemporaryValuesSql { get; }

    protected abstract string SequenceSql { get; }

    protected abstract string LongRunningSql { get; }

    [TestMethod]
    public async Task ConnectsAndQueriesScalar()
    {
        await using var connection = await OpenConnectionAsync();
        var rows = await connection.QueryAsync("SELECT 1");

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(1, rows[0].Get<int>(0));
    }

    [TestMethod]
    public async Task ExecutesParameterizedQuery()
    {
        await using var connection = await OpenConnectionAsync();
        var rows = await connection.QueryAsync(
          ParameterizedScalarSql,
          SqlParameters.Create(42));

        Assert.AreEqual(42, rows[0].Get<int>(0));
    }

    [TestMethod]
    public async Task RollsBackTransactionOnDispose()
    {
        await using var connection = await OpenConnectionAsync();
        await connection.ExecuteAsync(CreateTemporaryTableSql);
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await connection.ExecuteAsync(InsertTemporaryValueSql, SqlParameters.Create(1));
        }

        var rows = await connection.QueryAsync(CountTemporaryValuesSql);
        Assert.AreEqual(0L, rows[0].Get<long>(0));
    }

    [TestMethod]
    public async Task ExecutesPreparedBatch()
    {
        await using var connection = await OpenConnectionAsync();
        await connection.ExecuteAsync(CreateTemporaryTableSql);
        await using var statement =
          await connection.PrepareAsync(InsertTemporaryValueSql);
        var batch = Enumerable.Range(0, 16)
          .Select(static value => SqlParameters.Create(value))
          .ToArray();

        var results = await statement.ExecuteBatchAsync(batch);

        Assert.AreEqual(16, results.Count);
        Assert.IsTrue(results.All(static result => result.AffectedRows == 1));
    }

    [TestMethod]
    public async Task StreamsWithFetchSize()
    {
        await using var connection = await OpenConnectionAsync();
        List<int> values = [];
        await foreach (var row in connection.StreamAsync(SequenceSql, fetchSize: 3))
        {
            values.Add(row.Get<int>(0));
        }

        CollectionAssert.AreEqual(Enumerable.Range(1, 10).ToArray(), values);
    }

    [TestMethod]
    public async Task CancellationLeavesConnectionReusable()
    {
        await using var connection = await OpenConnectionAsync();
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAsync<OperationCanceledException>(
          () => connection.QueryAsync(LongRunningSql, cancellation.Token).AsTask());

        var rows = await connection.QueryAsync("SELECT 1");
        Assert.AreEqual(1, rows[0].Get<int>(0));
    }

    [TestMethod]
    public async Task PoolServesConcurrentQueries()
    {
        await using var pool = CreatePool();
        var queries = Enumerable.Range(0, 32)
          .Select(_ => pool.QueryAsync("SELECT 1").AsTask())
          .ToArray();

        var results = await Task.WhenAll(queries);

        Assert.IsTrue(results.All(static rows => rows[0].Get<int>(0) == 1));
        Assert.IsLessThanOrEqualTo(4, pool.Size);
    }

    [TestMethod]
    public async Task MapsAndCollectsRows()
    {
        await using var connection = await OpenConnectionAsync();
        var mapped = await connection.QueryMappedAsync(
          SequenceSql,
          static row => row.Get<int>(0));
        var sum = await connection.QueryCollectedAsync(
          SequenceSql,
          static rows => rows.Sum(static row => row.Get<int>(0)));

        CollectionAssert.AreEqual(Enumerable.Range(1, 10).ToArray(), mapped.ToArray());
        Assert.AreEqual(55, sum);
    }
}
