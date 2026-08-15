/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Text.Json;
using Apex.SqlClient;
using Testcontainers.MySql;

namespace Apex.MySqlClient.IntegrationTests;

/// <summary>
/// Exercises <see cref="MySqlConnection"/> and <see cref="MySqlPool"/> against a real MySQL or
/// MariaDB server. One container is shared for every test in this class (started once in
/// <see cref="StartMySqlAsync"/>) so the suite does not pay container startup cost per test; each
/// test opens its own connection or pool so tests remain independent and parallel-safe.
/// </summary>
[TestClass]
public sealed class MySqlConnectionIntegrationTests
{
  private static MySqlContainer _container = null!;

  [ClassInitialize]
  public static async Task StartMySqlAsync(TestContext testContext) =>
    _container = await MySqlContainerFixture.StartAsync();

  [ClassCleanup]
  public static async Task StopMySqlAsync() => await _container.DisposeAsync();

  private static MySqlConnectOptions Options => MySqlContainerFixture.CreateOptions(_container);

  [TestMethod]
  public async Task ConnectsAndExecutesSimpleTextQuery()
  {
    await using MySqlConnection connection = await MySqlClient.ConnectAsync(Options);

    SqlRowSet rows = await connection.QueryAsync("SELECT 1 AS id, 'hello' AS message");

    Assert.AreEqual(1, rows.Count);
    Assert.AreEqual(1, rows[0].Get<int>("id"));
    Assert.AreEqual("hello", rows[0].Get<string>("message"));
  }

  [TestMethod]
  public async Task UsesSupportedAuthenticationAndTlsModes()
  {
    bool isMariaDb = MySqlContainerFixture.ResolveImage()
      .Contains("mariadb", StringComparison.OrdinalIgnoreCase);
    MySqlConnectOptions options = isMariaDb
      ? Options with
      {
        AuthenticationPlugin = MySqlAuthenticationPlugin.NativePassword,
      }
      : Options with
      {
        SslMode = MySqlSslMode.Required,
        AllowPublicKeyRetrieval = false,
      };

    await using MySqlConnection connection = await MySqlClient.ConnectAsync(options);
    SqlRowSet rows = await connection.QueryAsync("SELECT CURRENT_USER()");

    StringAssert.StartsWith(rows[0].GetString(0), MySqlContainerFixture.Username + "@");
    if (!isMariaDb)
    {
      Assert.IsTrue(connection.IsSecure);
    }
  }

  [TestMethod]
  [DoNotParallelize]
  public async Task CachingSha2FullAuthenticationRequiresAnExplicitSecurePath()
  {
    if (MySqlContainerFixture.ResolveImage()
          .Contains("mariadb", StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    await using (MySqlConnection secure = await MySqlClient.ConnectAsync(
                   Options with { SslMode = MySqlSslMode.Required }))
    {
      await secure.ExecuteAsync(
        $"ALTER USER CURRENT_USER() IDENTIFIED BY '{MySqlContainerFixture.Password}'");
    }

    await Assert.ThrowsExactlyAsync<System.Security.Authentication.AuthenticationException>(
      () => MySqlClient.ConnectAsync(
          Options with
          {
            SslMode = MySqlSslMode.Disabled,
            AllowPublicKeyRetrieval = false,
          })
        .AsTask());

    await using MySqlConnection rsa = await MySqlClient.ConnectAsync(
      Options with
      {
        SslMode = MySqlSslMode.Disabled,
        AllowPublicKeyRetrieval = true,
      });
    SqlRowSet rows = await rsa.QueryAsync("SELECT 1");

    Assert.IsFalse(rsa.IsSecure);
    Assert.AreEqual(1, rows[0].GetInt32(0));
  }

  [TestMethod]
  public async Task UploadsLocalInfileWhenExplicitlyEnabled()
  {
    string fileName = Path.GetTempFileName();
    try
    {
      await File.WriteAllTextAsync(fileName, "1,alpha\n2,beta\n");
      await using MySqlConnection connection = await MySqlClient.ConnectAsync(
        Options with { AllowLoadLocalInfile = true });
      await connection.ExecuteAsync(
        "CREATE TEMPORARY TABLE local_infile_probe (id INT, value VARCHAR(16))");
      string escaped = fileName
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "\\'", StringComparison.Ordinal);

      SqlCommandResult loaded = await connection.ExecuteAsync(
        $"LOAD DATA LOCAL INFILE '{escaped}' INTO TABLE local_infile_probe " +
        "FIELDS TERMINATED BY ',' LINES TERMINATED BY '\\n'");
      SqlRowSet rows = await connection.QueryAsync(
        "SELECT id, value FROM local_infile_probe ORDER BY id");

      Assert.AreEqual(2L, loaded.AffectedRows);
      Assert.AreEqual(2, rows.Count);
      Assert.AreEqual("alpha", rows[0].GetString("value"));
      Assert.AreEqual("beta", rows[1].GetString("value"));
    }
    finally
    {
      File.Delete(fileName);
    }
  }

  [TestMethod]
  public async Task PreparedLocalInfileBatchRemainsProtocolSynchronized()
  {
    if (!MySqlContainerFixture.ResolveImage()
          .Contains("mariadb", StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    string fileName = Path.GetTempFileName();
    try
    {
      await File.WriteAllTextAsync(fileName, "1,alpha\n2,beta\n");
      await using MySqlConnection connection = await MySqlClient.ConnectAsync(
        Options with
        {
          AllowLoadLocalInfile = true,
          PipeliningLimit = 8,
        });
      await connection.ExecuteAsync(
        "CREATE TEMPORARY TABLE prepared_local_infile_probe (id INT, value VARCHAR(16))");
      string escaped = fileName
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "\\'", StringComparison.Ordinal);
      await using ISqlPreparedStatement statement = await connection.PrepareAsync(
        $"LOAD DATA LOCAL INFILE '{escaped}' INTO TABLE prepared_local_infile_probe " +
        "FIELDS TERMINATED BY ',' LINES TERMINATED BY '\\n'");

      IReadOnlyList<SqlCommandResult> loaded = await statement.ExecuteBatchAsync(
        Enumerable.Repeat(SqlParameters.Empty, 8).ToArray());
      SqlRowSet rows = await connection.QueryAsync(
        "SELECT COUNT(*) AS count FROM prepared_local_infile_probe");

      Assert.AreEqual(8, loaded.Count);
      Assert.IsTrue(loaded.All(static result => result.AffectedRows == 2));
      Assert.AreEqual(16L, rows[0].GetInt64("count"));
    }
    finally
    {
      File.Delete(fileName);
    }
  }

  [TestMethod]
  public async Task ReportsServerMetadataAndMariaDbCompatibilityPrefix()
  {
    await using MySqlConnection connection = await MySqlClient.ConnectAsync(Options);

    bool imageIsMariaDb = MySqlContainerFixture.ResolveImage()
      .Contains("mariadb", StringComparison.OrdinalIgnoreCase);

    Assert.AreEqual(imageIsMariaDb, connection.ServerVersion.IsMariaDb);
    Assert.AreEqual(imageIsMariaDb ? "MariaDB" : "MySQL", connection.DatabaseMetadata.ProductName);
    Assert.IsGreaterThanOrEqualTo(5, connection.DatabaseMetadata.MajorVersion);
    Assert.IsTrue(connection.ConnectionId > 0);
  }

  [TestMethod]
  public async Task PreparesExecutesRepeatedlyAndClosesStatement()
  {
    await using MySqlConnection connection = await MySqlClient.ConnectAsync(Options);
    ISqlPreparedStatement statement = await connection.PrepareAsync("SELECT ? * 2 AS doubled");

    SqlRowSet first = await statement.QueryAsync(SqlParameters.Create(21));
    SqlRowSet second = await statement.QueryAsync(SqlParameters.Create(100));

    Assert.AreEqual(42, first[0].Get<int>("doubled"));
    Assert.AreEqual(200, second[0].Get<int>("doubled"));

    await statement.DisposeAsync();

    // The statement is closed; the connection itself must remain usable afterward.
    SqlRowSet stillWorks = await connection.QueryAsync("SELECT 1");
    Assert.AreEqual(1, stillWorks[0].Get<int>(0));
  }

  [TestMethod]
  public async Task ReportsAffectedRowsLastInsertIdAndStatus()
  {
    await using MySqlConnection connection = await MySqlClient.ConnectAsync(Options);
    await connection.ExecuteAsync(
      "CREATE TEMPORARY TABLE affected_rows_probe (" +
      "id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY, value INT)");
    await using ISqlPreparedStatement insert =
      await connection.PrepareAsync("INSERT INTO affected_rows_probe (value) VALUES (?)");

    SqlCommandResult first = await insert.ExecuteAsync(SqlParameters.Create(10));
    SqlCommandResult second = await insert.ExecuteAsync(SqlParameters.Create(20));

    Assert.AreEqual(1L, first.AffectedRows);
    Assert.AreEqual(1L, second.AffectedRows);
    Assert.IsTrue(first.LastInsertId is > 0);
    Assert.AreEqual(first.LastInsertId!.Value + 1, second.LastInsertId!.Value);
    Assert.AreEqual(1, connection.LastCommandInfo.AffectedRows);
    Assert.AreEqual(second.LastInsertId!.Value, connection.LastCommandInfo.LastInsertId);
    Assert.IsTrue((connection.ServerStatus & MySqlServerStatus.AutoCommit) != 0);

    SqlCommandResult update = await connection.ExecuteAsync(
      "UPDATE affected_rows_probe SET value = 99 WHERE value = 10");
    Assert.AreEqual(1L, update.AffectedRows);
  }

  [TestMethod]
  public async Task CommitsAndRollsBackTransactions()
  {
    await using MySqlConnection connection = await MySqlClient.ConnectAsync(Options);
    await connection.ExecuteAsync("CREATE TEMPORARY TABLE transaction_probe (value INT)");

    await using (ISqlTransaction rolledBack = await connection.BeginTransactionAsync())
    {
      await connection.ExecuteAsync(
        "INSERT INTO transaction_probe VALUES (?)",
        SqlParameters.Create(1));
      Assert.IsTrue(connection.InTransaction);
    }

    SqlRowSet afterRollback = await connection.QueryAsync("SELECT COUNT(*) FROM transaction_probe");
    Assert.AreEqual(0L, afterRollback[0].Get<long>(0));
    Assert.IsFalse(connection.InTransaction);

    await using (ISqlTransaction committed = await connection.BeginTransactionAsync())
    {
      await connection.ExecuteAsync(
        "INSERT INTO transaction_probe VALUES (?)",
        SqlParameters.Create(2));
      await committed.CommitAsync();
    }

    SqlRowSet afterCommit = await connection.QueryAsync("SELECT COUNT(*) FROM transaction_probe");
    Assert.AreEqual(1L, afterCommit[0].Get<long>(0));
  }

  [TestMethod]
  public async Task ExecutesPreparedBatchInSubmissionOrder()
  {
    await using MySqlConnection connection = await MySqlClient.ConnectAsync(Options);
    await connection.ExecuteAsync("CREATE TEMPORARY TABLE batch_probe (value INT)");
    await using ISqlPreparedStatement statement =
      await connection.PrepareAsync("INSERT INTO batch_probe VALUES (?)");
    SqlParameters[] batch = Enumerable.Range(0, 20)
      .Select(static value => SqlParameters.Create(value))
      .ToArray();

    IReadOnlyList<SqlCommandResult> results = await statement.ExecuteBatchAsync(batch);

    Assert.AreEqual(20, results.Count);
    Assert.IsTrue(results.All(static result => result.AffectedRows == 1));
    SqlRowSet rows = await connection.QueryAsync("SELECT value FROM batch_probe ORDER BY value");
    CollectionAssert.AreEqual(
      Enumerable.Range(0, 20).ToArray(),
      rows.Select(static row => row.Get<int>("value")).ToArray());
  }

  [TestMethod]
  public async Task ReadsMultipleResultSetsFromAStoredProcedure()
  {
    await using MySqlConnection connection = await MySqlClient.ConnectAsync(Options);
    string procedureName = "multi_result_" + Guid.NewGuid().ToString("N");
    await connection.ExecuteAsync(
      $"CREATE PROCEDURE {procedureName}() " +
      "BEGIN SELECT 1 AS a; SELECT 'two' AS b; END");
    try
    {
      SqlRowSet first = await connection.QueryAsync($"CALL {procedureName}()");

      Assert.AreEqual(1, first[0].Get<int>("a"));
      Assert.IsNotNull(first.Next);
      Assert.AreEqual("two", first.Next![0].Get<string>("b"));
    }
    finally
    {
      await connection.ExecuteAsync($"DROP PROCEDURE IF EXISTS {procedureName}");
    }
  }

  [TestMethod]
  public async Task DecodesSignedAndUnsignedIntegerBoundariesThroughTextAndBinaryProtocols()
  {
    await using MySqlConnection connection = await MySqlClient.ConnectAsync(Options);
    await connection.ExecuteAsync(
      "CREATE TEMPORARY TABLE integer_matrix (" +
      "c_tinyint TINYINT, c_tinyint_u TINYINT UNSIGNED, " +
      "c_smallint SMALLINT, c_smallint_u SMALLINT UNSIGNED, " +
      "c_int INT, c_int_u INT UNSIGNED, " +
      "c_bigint BIGINT, c_bigint_u BIGINT UNSIGNED)");
    await using (ISqlPreparedStatement insert = await connection.PrepareAsync(
      "INSERT INTO integer_matrix VALUES (?, ?, ?, ?, ?, ?, ?, ?)"))
    {
      await insert.ExecuteAsync(SqlParameters.Create(
        SqlValue.From((sbyte)sbyte.MinValue),
        SqlValue.From((byte)byte.MaxValue),
        (short)short.MinValue,
        SqlValue.From((ushort)ushort.MaxValue),
        int.MinValue,
        SqlValue.From((uint)uint.MaxValue),
        long.MinValue,
        SqlValue.From(ulong.MaxValue)));
    }

    await AssertIntegerMatrixAsync(await connection.QueryAsync("SELECT * FROM integer_matrix"));
    await using ISqlPreparedStatement select =
      await connection.PrepareAsync("SELECT * FROM integer_matrix");
    await AssertIntegerMatrixAsync(await select.QueryAsync());

    static Task AssertIntegerMatrixAsync(SqlRowSet rows)
    {
      SqlRow row = rows[0];
      Assert.AreEqual(sbyte.MinValue, row.Get<sbyte>("c_tinyint"));
      Assert.AreEqual(byte.MaxValue, row.Get<byte>("c_tinyint_u"));
      Assert.AreEqual(short.MinValue, row.Get<short>("c_smallint"));
      Assert.AreEqual(ushort.MaxValue, row.Get<ushort>("c_smallint_u"));
      Assert.AreEqual(int.MinValue, row.Get<int>("c_int"));
      Assert.AreEqual(uint.MaxValue, row.Get<uint>("c_int_u"));
      Assert.AreEqual(long.MinValue, row.Get<long>("c_bigint"));
      Assert.AreEqual(ulong.MaxValue, row.Get<ulong>("c_bigint_u"));
      return Task.CompletedTask;
    }
  }

  [TestMethod]
  public async Task DecodesDecimalFloatingPointStringBinaryAndBitColumns()
  {
    await using MySqlConnection connection = await MySqlClient.ConnectAsync(Options);
    await connection.ExecuteAsync(
      "CREATE TEMPORARY TABLE scalar_matrix (" +
      "c_decimal DECIMAL(20,4), c_float FLOAT, c_double DOUBLE, " +
      "c_varchar VARCHAR(64), c_varbinary VARBINARY(64), c_blob BLOB, c_bit BIT(16))");
    await using (ISqlPreparedStatement insert = await connection.PrepareAsync(
      "INSERT INTO scalar_matrix VALUES (?, ?, ?, ?, ?, ?, ?)"))
    {
      await insert.ExecuteAsync(SqlParameters.Create(
        12345.6789m,
        1.5f,
        2.25d,
        "héllo",
        new byte[] { 1, 2, 3 },
        new byte[] { 9, 8, 7, 6 },
        (short)0x0102));
    }

    await AssertScalarMatrixAsync(await connection.QueryAsync("SELECT * FROM scalar_matrix"));
    await using ISqlPreparedStatement select =
      await connection.PrepareAsync("SELECT * FROM scalar_matrix");
    await AssertScalarMatrixAsync(await select.QueryAsync());

    static Task AssertScalarMatrixAsync(SqlRowSet rows)
    {
      SqlRow row = rows[0];
      Assert.AreEqual(12345.6789m, row.Get<decimal>("c_decimal"));
      Assert.AreEqual(1.5f, row.Get<float>("c_float"));
      Assert.AreEqual(2.25d, row.Get<double>("c_double"));
      Assert.AreEqual("héllo", row.Get<string>("c_varchar"));
      CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, row.Get<byte[]>("c_varbinary"));
      CollectionAssert.AreEqual(new byte[] { 9, 8, 7, 6 }, row.Get<byte[]>("c_blob"));
      Assert.AreEqual(0x0102ul, row.Get<ulong>("c_bit"));
      return Task.CompletedTask;
    }
  }

  [TestMethod]
  public async Task RoundTripsArbitraryPrecisionDecimal()
  {
    const string text =
      "12345678901234567890123456789012345.123456789012345678901234567890";
    MySqlDecimal value = MySqlDecimal.Parse(text);
    await using MySqlConnection connection = await MySqlClient.ConnectAsync(Options);
    await connection.ExecuteAsync(
      "CREATE TEMPORARY TABLE large_decimal_probe (value DECIMAL(65,30))");
    await connection.ExecuteAsync(
      "INSERT INTO large_decimal_probe VALUES (?)",
      SqlParameters.Create(value));

    SqlRowSet textRows = await connection.QueryAsync(
      "SELECT value FROM large_decimal_probe");
    await using ISqlPreparedStatement select = await connection.PrepareAsync(
      "SELECT value FROM large_decimal_probe");
    SqlRowSet binaryRows = await select.QueryAsync();

    Assert.AreEqual(value, textRows[0].Get<MySqlDecimal>(0));
    Assert.AreEqual(value, binaryRows[0].Get<MySqlDecimal>(0));
    Assert.AreEqual(text, textRows[0].Get<MySqlDecimal>(0).ToString());
  }

  [TestMethod]
  public async Task DecodesTemporalYearAndJsonColumns()
  {
    await using MySqlConnection connection = await MySqlClient.ConnectAsync(Options);
    await connection.ExecuteAsync(
      "CREATE TEMPORARY TABLE temporal_matrix (" +
      "c_year YEAR, c_date DATE, c_time TIME, c_datetime DATETIME(6), " +
      "c_timestamp TIMESTAMP(6) NULL, c_json JSON)");
    DateOnly date = new(2024, 3, 15);
    TimeSpan time = new(13, 45, 30);
    DateTime dateTime = new DateTime(2024, 3, 15, 13, 45, 30, DateTimeKind.Unspecified).AddTicks(1234560);
    await using (ISqlPreparedStatement insert = await connection.PrepareAsync(
      "INSERT INTO temporal_matrix VALUES (?, ?, ?, ?, ?, ?)"))
    {
      await insert.ExecuteAsync(SqlParameters.Create(
        2024,
        date,
        SqlValue.From(time),
        dateTime,
        dateTime,
        """{"ok":true}"""));
    }

    await AssertTemporalMatrixAsync(await connection.QueryAsync("SELECT * FROM temporal_matrix"));
    await using ISqlPreparedStatement select =
      await connection.PrepareAsync("SELECT * FROM temporal_matrix");
    await AssertTemporalMatrixAsync(await select.QueryAsync());

    Task AssertTemporalMatrixAsync(SqlRowSet rows)
    {
      SqlRow row = rows[0];
      Assert.AreEqual(2024, row.Get<int>("c_year"));
      Assert.AreEqual(date, row.Get<DateOnly>("c_date"));
      Assert.AreEqual(time, row.Get<TimeSpan>("c_time"));
      Assert.AreEqual(dateTime, row.Get<DateTime>("c_datetime"));
      Assert.AreEqual(dateTime, row.Get<DateTime>("c_timestamp"));
      // MySQL canonicalizes stored JSON text (adds a space after ':'), so parse it instead of
      // comparing the raw string.
      using JsonDocument json = JsonDocument.Parse(row.Get<string>("c_json"));
      Assert.IsTrue(json.RootElement.GetProperty("ok").GetBoolean());
      return Task.CompletedTask;
    }
  }

  [TestMethod]
  public async Task DecodesEnumAndSetColumnsAsStrings()
  {
    await using MySqlConnection connection = await MySqlClient.ConnectAsync(Options);
    await connection.ExecuteAsync(
      "CREATE TEMPORARY TABLE enum_matrix (" +
      "c_enum ENUM('small','medium','large'), c_set SET('a','b','c'))");
    await connection.ExecuteAsync(
      "INSERT INTO enum_matrix VALUES ('medium', 'a,c')");

    SqlRowSet textRows = await connection.QueryAsync("SELECT * FROM enum_matrix");
    Assert.AreEqual("medium", textRows[0].Get<string>("c_enum"));
    Assert.AreEqual("a,c", textRows[0].Get<string>("c_set"));

    await using ISqlPreparedStatement select =
      await connection.PrepareAsync("SELECT * FROM enum_matrix");
    SqlRowSet binaryRows = await select.QueryAsync();
    Assert.AreEqual("medium", binaryRows[0].Get<string>("c_enum"));
    Assert.AreEqual("a,c", binaryRows[0].Get<string>("c_set"));
  }

  [TestMethod]
  public async Task DecodesGeometryColumnAsRawBytes()
  {
    await using MySqlConnection connection = await MySqlClient.ConnectAsync(Options);
    await connection.ExecuteAsync("CREATE TEMPORARY TABLE geometry_matrix (c_point POINT)");
    await connection.ExecuteAsync(
      "INSERT INTO geometry_matrix VALUES (ST_PointFromText('POINT(1 1)'))");

    SqlRowSet rows = await connection.QueryAsync(
      "SELECT ST_AsBinary(c_point) AS wkb FROM geometry_matrix");

    byte[] wellKnownBinary = rows[0].Get<byte[]>("wkb");
    Assert.IsGreaterThan(0, wellKnownBinary.Length);
  }

  [TestMethod]
  public async Task NullValuesRoundTripAsNullInBothProtocols()
  {
    await using MySqlConnection connection = await MySqlClient.ConnectAsync(Options);
    await connection.ExecuteAsync("CREATE TEMPORARY TABLE nullable_probe (value INT)");
    await connection.ExecuteAsync(
      "INSERT INTO nullable_probe VALUES (?)",
      SqlParameters.Create(SqlValue.Null));

    SqlRowSet textRows = await connection.QueryAsync("SELECT value FROM nullable_probe");
    Assert.IsTrue(textRows[0].IsNull(0));
    Assert.IsNull(textRows[0].Get<int?>(0));

    await using ISqlPreparedStatement select =
      await connection.PrepareAsync("SELECT value FROM nullable_probe");
    SqlRowSet binaryRows = await select.QueryAsync();
    Assert.IsTrue(binaryRows[0].IsNull(0));
  }

  [TestMethod]
  public async Task RowsRemainValidAfterConnectionDisposal()
  {
    MySqlConnection connection = await MySqlClient.ConnectAsync(Options);
    SqlRowSet rows = await connection.QueryAsync("SELECT 1 AS id, 'safe' AS message");
    await connection.DisposeAsync();

    Assert.AreEqual(1, rows[0].Get<int>("id"));
    Assert.AreEqual("safe", rows[0].Get<string>("message"));
  }

  [TestMethod]
  public async Task StreamsAndReusesBorrowedReaderRepeatedly()
  {
    await using MySqlConnection connection = await MySqlClient.ConnectAsync(Options);

    for (int iteration = 0; iteration < 5; iteration++)
    {
      List<int> streamed = [];
      await foreach (SqlRow row in connection.StreamAsync(
                       "SELECT 1 AS v UNION ALL SELECT 2 UNION ALL SELECT 3",
                       fetchSize: 2))
      {
        streamed.Add(row.Get<int>("v"));
      }

      CollectionAssert.AreEqual(new[] { 1, 2, 3 }, streamed);

      await using ISqlRowReader reader = await connection.ExecuteReaderAsync(
        "SELECT 1 AS v UNION ALL SELECT 2 UNION ALL SELECT 3");
      List<int> borrowed = [];
      while (await reader.ReadAsync())
      {
        borrowed.Add(reader.GetInt32(0));
      }

      CollectionAssert.AreEqual(new[] { 1, 2, 3 }, borrowed);
    }
  }

  [TestMethod]
  public async Task DisposingAStreamEarlyStillLeavesTheConnectionReusable()
  {
    await using MySqlConnection connection = await MySqlClient.ConnectAsync(Options);

    await foreach (SqlRow row in connection.StreamAsync(
                     "SELECT 1 AS v UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4",
                     fetchSize: 1))
    {
      Assert.AreEqual(1, row.Get<int>("v"));
      break;
    }

    SqlRowSet rows = await connection.QueryAsync("SELECT 42 AS answer");
    Assert.AreEqual(42, rows[0].Get<int>("answer"));
  }

  [TestMethod]
  public async Task PoolLeasePinsTheUnderlyingConnectionUntilTheReaderIsDisposed()
  {
    await using MySqlPool pool = MySqlPool.Create(
      Options,
      new SqlPoolOptions { MaximumSize = 1, MaximumWaitQueueSize = 0 });

    ISqlConnection leased = await pool.GetConnectionAsync();
    await using (ISqlRowReader reader =
                   await leased.ExecuteReaderAsync("SELECT 1 AS v UNION ALL SELECT 2"))
    {
      Assert.IsTrue(await reader.ReadAsync());
      Assert.AreEqual(1, reader.GetInt32(0));

      // The pool has no free slot and no wait queue, so a second lease must fail immediately
      // instead of ever handing out a second physical connection while this one stays open.
      await Assert.ThrowsExactlyAsync<SqlClientException>(
        () => pool.GetConnectionAsync().AsTask());
    }

    await leased.DisposeAsync();

    ISqlConnection released = await pool.GetConnectionAsync();
    await released.DisposeAsync();
  }

  [TestMethod]
  public async Task PoolServesConcurrentQueriesUnderLoad()
  {
    await using MySqlPool pool = MySqlPool.Create(Options, new SqlPoolOptions { MaximumSize = 8 });

    Task<SqlRowSet>[] queries = Enumerable.Range(0, 64)
      .Select(index => pool.QueryAsync($"SELECT {index} AS v").AsTask())
      .ToArray();
    SqlRowSet[] results = await Task.WhenAll(queries);

    for (int i = 0; i < results.Length; i++)
    {
      Assert.AreEqual(i, results[i][0].Get<int>("v"));
    }

    Assert.IsLessThanOrEqualTo(8, pool.Size);
  }

  [TestMethod]
  public async Task CancellationInterruptsAQueryAndTheConnectionStaysReusable()
  {
    await using MySqlConnection connection = await MySqlClient.ConnectAsync(
      Options with { QueryCancellation = MySqlQueryCancellation.KillQuery });
    using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(300));

    await Assert.ThrowsExactlyAsync<OperationCanceledException>(
      () => connection.QueryAsync("SELECT SLEEP(10)", cancellation.Token).AsTask());

    SqlRowSet rows = await connection.QueryAsync("SELECT 1 AS v");
    Assert.AreEqual(1, rows[0].Get<int>("v"));
  }

  [TestMethod]
  public async Task CancellationDoesNotExposeAnUndeliveredBorrowedRow()
  {
    await using MySqlConnection connection = await MySqlClient.ConnectAsync(Options);
    using CancellationTokenSource cancellation = new();
    await using (ISqlRowReader reader =
                 await connection.ExecuteReaderAsync("SELECT 42 AS v", cancellationToken: cancellation.Token))
    {
      await Task.Delay(100);
      cancellation.Cancel();

      await Assert.ThrowsExactlyAsync<OperationCanceledException>(
        () => reader.ReadAsync().AsTask());
    }

    SqlRowSet rows = await connection.QueryAsync("SELECT 43 AS v");
    Assert.AreEqual(43, rows[0].GetInt32("v"));
  }

  [TestMethod]
  public async Task SurfacesMySqlErrorNumberAndSqlState()
  {
    await using MySqlConnection connection = await MySqlClient.ConnectAsync(Options);

    MySqlException exception = await Assert.ThrowsExactlyAsync<MySqlException>(
      () => connection.QueryAsync("SELECT * FROM no_such_table_xyz").AsTask());

    Assert.AreEqual(1146, exception.ErrorNumber);
    Assert.IsFalse(string.IsNullOrEmpty(exception.SqlState));
  }
}
