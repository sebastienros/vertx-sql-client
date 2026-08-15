/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using Apex.SqlClient;

namespace Apex.MsSqlClient.IntegrationTests;

[TestClass]
public sealed class MsSqlConnectionIntegrationTests
{
  [TestMethod]
  public async Task ConnectsQueriesMetadataAndExecutesParameterizedRpc()
  {
    await using MsSqlConnection connection =
      await MsSqlClient.ConnectAsync(MsSqlTestEnvironment.Options);

    SqlRowSet scalar = await connection.QueryAsync(
      "SELECT 1 AS id, CAST(N'hello' AS nvarchar(20)) AS message, DB_NAME() AS database_name");

    Assert.IsTrue(connection.IsSecure);
    Assert.AreEqual("Microsoft SQL Server", connection.DatabaseMetadata.ProductName);
    Assert.IsGreaterThanOrEqualTo(15, connection.DatabaseMetadata.MajorVersion);
    Assert.AreEqual(1, scalar.Count);
    Assert.AreEqual(1, scalar[0].GetInt32("id"));
    Assert.AreEqual("hello", scalar[0].GetString("message"));
    Assert.AreEqual("master", scalar[0].GetString("database_name"));

    SqlRowSet parameterized = await connection.QueryAsync(
      "SELECT @P1 AS id, @P2 AS message",
      SqlParameters.Create(42, "forty-two"));
    Assert.AreEqual(42, parameterized[0].Get<int>("id"));
    Assert.AreEqual("forty-two", parameterized[0].GetString("message"));
  }

  [TestMethod]
  public async Task RollsBackTransactionOnDispose()
  {
    await using MsSqlConnection connection =
      await MsSqlClient.ConnectAsync(MsSqlTestEnvironment.Options);
    await connection.ExecuteAsync("CREATE TABLE #rollback_values (value int NOT NULL)");

    await using (ISqlTransaction transaction = await connection.BeginTransactionAsync())
    {
      await connection.ExecuteAsync(
        "INSERT INTO #rollback_values VALUES (@P1)",
        SqlParameters.Create(1));
    }

    SqlRowSet count = await connection.QueryAsync(
      "SELECT COUNT(*) AS value_count FROM #rollback_values");
    Assert.AreEqual(0, count[0].GetInt32("value_count"));
  }

  [TestMethod]
  public async Task SurfacesStructuredErrorsAndInfoMessages()
  {
    await using MsSqlConnection connection =
      await MsSqlClient.ConnectAsync(MsSqlTestEnvironment.Options);
    List<MsSqlInfo> messages = [];
    connection.InfoMessage += messages.Add;

    await connection.ExecuteAsync("RAISERROR(N'apex-info', 10, 1)");

    Assert.HasCount(1, messages);
    Assert.AreEqual(50000, messages[0].Number);
    Assert.AreEqual(0, messages[0].Severity);
    StringAssert.Contains(messages[0].Message, "apex-info");

    MsSqlException exception = await Assert.ThrowsExactlyAsync<MsSqlException>(
      () => connection.QueryAsync("SELECT missing_column").AsTask());
    Assert.AreEqual(207, exception.Number);
    Assert.AreEqual(16, exception.Severity);
    Assert.AreEqual(1, exception.State);
    Assert.HasCount(1, exception.Errors);
    StringAssert.Contains(exception.Message, "missing_column");
    Assert.IsGreaterThan(0, exception.LineNumber);
  }

  [TestMethod]
  public async Task ExecutesPreparedStatementAndBatch()
  {
    await using MsSqlConnection connection =
      await MsSqlClient.ConnectAsync(MsSqlTestEnvironment.Options);
    await connection.ExecuteAsync("CREATE TABLE #batch_values (value int NOT NULL)");
    await using ISqlPreparedStatement statement =
      await connection.PrepareAsync("INSERT INTO #batch_values VALUES (@P1)");

    SqlCommandResult first = await statement.ExecuteAsync(SqlParameters.Create(-1));
    SqlParameters[] batch = Enumerable.Range(0, 16)
      .Select(static value => SqlParameters.Create(value))
      .ToArray();
    IReadOnlyList<SqlCommandResult> results = await statement.ExecuteBatchAsync(batch);

    Assert.AreEqual(1L, first.AffectedRows);
    Assert.HasCount(16, results);
    Assert.IsTrue(results.All(static result => result.AffectedRows == 1));
    SqlRowSet rows = await connection.QueryAsync(
      "SELECT value FROM #batch_values ORDER BY value");
    CollectionAssert.AreEqual(
      Enumerable.Range(-1, 17).ToArray(),
      rows.Select(static row => row.GetInt32(0)).ToArray());

    await using ISqlPreparedStatement query =
      await connection.PrepareAsync("SELECT @P1 AS value");
    await using (ISqlRowReader reader =
                 await query.ExecuteReaderAsync(SqlParameters.Create(100)))
    {
      Assert.IsTrue(await reader.ReadAsync());
      Assert.AreEqual(100, reader.GetInt32("value"));
      Assert.IsFalse(await reader.ReadAsync());
    }

    Assert.AreEqual(
      101,
      (await query.QueryAsync(SqlParameters.Create(101)))[0].GetInt32(0));
    List<int> streamed = [];
    await foreach (SqlRow row in query.StreamAsync(
                     SqlParameters.Create(102),
                     fetchSize: 1))
    {
      streamed.Add(row.GetInt32(0));
    }

    CollectionAssert.AreEqual(new[] { 102 }, streamed);

    await using ISqlPreparedStatement streamFirst =
      await connection.PrepareAsync("SELECT @P1 AS value");
    List<int> firstStream = [];
    await foreach (SqlRow row in streamFirst.StreamAsync(
                     SqlParameters.Create(200),
                     fetchSize: 1))
    {
      firstStream.Add(row.GetInt32(0));
    }

    CollectionAssert.AreEqual(new[] { 200 }, firstStream);
    Assert.AreEqual(
      201,
      (await streamFirst.QueryAsync(SqlParameters.Create(201)))[0].GetInt32(0));
  }

  [TestMethod]
  public async Task DecodesAndEncodesTypeMatrixIncludingPlpValues()
  {
    Guid guid = Guid.Parse("12345678-1234-5678-9012-123456789abc");
    DateOnly date = new(2026, 8, 14);
    TimeOnly time = new(12, 34, 56, 123, 456);
    DateTime dateTime = new(
      2026,
      8,
      14,
      12,
      34,
      56,
      123,
      456,
      DateTimeKind.Unspecified);
    DateTimeOffset dateTimeOffset = new(
      2026,
      8,
      14,
      12,
      34,
      56,
      123,
      456,
      TimeSpan.FromHours(2.5));

    await using MsSqlConnection connection =
      await MsSqlClient.ConnectAsync(MsSqlTestEnvironment.Options);
    SqlRowSet decodedRows = await connection.QueryAsync(
      """
      SELECT
        CAST(1 AS bit) AS boolean_value,
        CAST(2 AS tinyint) AS byte_value,
        CAST(-3 AS smallint) AS int16_value,
        CAST(4 AS int) AS int32_value,
        CAST(5 AS bigint) AS int64_value,
        CAST(1.5 AS real) AS single_value,
        CAST(2.5 AS float) AS double_value,
        CAST(123456789012345.6789 AS decimal(19,4)) AS decimal_value,
        CAST('12345678-1234-5678-9012-123456789abc' AS uniqueidentifier) AS guid_value,
        CAST('2026-08-14' AS date) AS date_value,
        CAST('12:34:56.1234560' AS time(7)) AS time_value,
        CAST('2026-08-14T12:34:56.1234560' AS datetime2(7)) AS datetime2_value,
        CAST('2026-08-14T12:34:56.1234560+02:30' AS datetimeoffset(7)) AS datetimeoffset_value,
        CAST(N'apex-text' AS nvarchar(20)) AS text_value,
        CAST(0x0001FEFF AS varbinary(4)) AS binary_value,
        CAST(NULL AS int) AS null_value
      """);
    SqlRow decoded = decodedRows[0];

    Assert.IsTrue(decoded.Get<bool>("boolean_value"));
    Assert.AreEqual((byte)2, decoded.Get<byte>("byte_value"));
    Assert.AreEqual((short)-3, decoded.GetInt16("int16_value"));
    Assert.AreEqual(4, decoded.GetInt32("int32_value"));
    Assert.AreEqual(5L, decoded.GetInt64("int64_value"));
    Assert.AreEqual(1.5f, decoded.GetFloat("single_value"));
    Assert.AreEqual(2.5d, decoded.GetDouble("double_value"));
    Assert.AreEqual(123456789012345.6789m, decoded.Get<decimal>("decimal_value"));
    Assert.AreEqual(guid, decoded.GetGuid("guid_value"));
    Assert.AreEqual(date, decoded.GetDateOnly("date_value"));
    Assert.AreEqual(time, decoded.GetTimeOnly("time_value"));
    Assert.AreEqual(dateTime, decoded.GetDateTime("datetime2_value"));
    Assert.AreEqual(dateTimeOffset, decoded.GetDateTimeOffset("datetimeoffset_value"));
    Assert.AreEqual("apex-text", decoded.GetString("text_value"));
    CollectionAssert.AreEqual(
      new byte[] { 0, 1, 254, 255 },
      decoded.GetBytes("binary_value"));
    Assert.IsTrue(decoded.IsNull(decoded.GetOrdinal("null_value")));

    string longText = new('x', 9001);
    byte[] longBinary = Enumerable.Range(0, 9001)
      .Select(static value => (byte)(value % 251))
      .ToArray();
    SqlRow encoded = (await connection.QueryAsync(
      """
      SELECT
        @P1 AS boolean_value,
        @P2 AS int16_value,
        @P3 AS int32_value,
        @P4 AS int64_value,
        @P5 AS single_value,
        @P6 AS double_value,
        @P7 AS decimal_value,
        @P8 AS guid_value,
        @P9 AS date_value,
        @P10 AS time_value,
        @P11 AS datetime2_value,
        @P12 AS datetimeoffset_value,
        @P13 AS text_value,
        @P14 AS binary_value,
        CAST(@P15 AS int) AS null_value,
        CAST(
          SQL_VARIANT_PROPERTY(CAST(@P3 AS sql_variant), 'BaseType')
          AS nvarchar(128)
        ) AS int_base_type
      """,
      SqlParameters.Create(
        true,
        (short)-2,
        3,
        4L,
        1.25f,
        2.5d,
        123456789012345.6789m,
        guid,
        date,
        time,
        dateTime,
        dateTimeOffset,
        longText,
        longBinary,
        SqlValue.Null)))[0];

    Assert.IsTrue(encoded.Get<bool>("boolean_value"));
    Assert.AreEqual((short)-2, encoded.GetInt16("int16_value"));
    Assert.AreEqual(3, encoded.GetInt32("int32_value"));
    Assert.AreEqual(4L, encoded.GetInt64("int64_value"));
    Assert.AreEqual(1.25f, encoded.GetFloat("single_value"));
    Assert.AreEqual(2.5d, encoded.GetDouble("double_value"));
    Assert.AreEqual(123456789012345.6789m, encoded.Get<decimal>("decimal_value"));
    Assert.AreEqual(guid, encoded.GetGuid("guid_value"));
    Assert.AreEqual(date, encoded.GetDateOnly("date_value"));
    Assert.AreEqual(time, encoded.GetTimeOnly("time_value"));
    Assert.AreEqual(dateTime, encoded.GetDateTime("datetime2_value"));
    Assert.AreEqual(dateTimeOffset, encoded.GetDateTimeOffset("datetimeoffset_value"));
    Assert.AreEqual(longText, encoded.GetString("text_value"));
    CollectionAssert.AreEqual(longBinary, encoded.GetBytes("binary_value"));
    Assert.IsTrue(encoded.IsNull(encoded.GetOrdinal("null_value")));
    Assert.AreEqual("int", encoded.GetString("int_base_type"));
  }

  [TestMethod]
  public async Task BufferedRowsRemainValidAfterConnectionDisposal()
  {
    SqlRow row;
    await using (MsSqlConnection connection =
                 await MsSqlClient.ConnectAsync(MsSqlTestEnvironment.Options))
    {
      row = (await connection.QueryAsync(
        "SELECT 42 AS value, CAST(N'safe' AS nvarchar(20)) AS label"))[0];
    }

    Assert.AreEqual(42, row.GetInt32("value"));
    Assert.AreEqual("safe", row.GetString("label"));
  }

  [TestMethod]
  public async Task ReadsBorrowedRowsWithTypedGettersAndRepeatedStress()
  {
    await using MsSqlConnection connection =
      await MsSqlClient.ConnectAsync(MsSqlTestEnvironment.Options);
    await using (ISqlRowReader reader = await connection.ExecuteReaderAsync(
                   """
                   SELECT
                     CAST(42 AS int) AS value,
                     CAST(N'borrowed' AS nvarchar(20)) AS label,
                     CAST('2026-08-14' AS date) AS date_value,
                     CAST(0x0102FEFF AS varbinary(4)) AS bytes_value
                   """))
    {
      Assert.IsTrue(await reader.ReadAsync());
      Assert.AreEqual(4, reader.FieldCount);
      Assert.AreEqual(0, reader.GetOrdinal("value"));
      Assert.AreEqual(42, reader.GetInt32(0));
      Assert.AreEqual("borrowed", reader.GetString(1));
      Assert.AreEqual(new DateOnly(2026, 8, 14), reader.GetDateOnly(2));
      CollectionAssert.AreEqual(
        new byte[] { 1, 2, 254, 255 },
        reader.GetBytes(3));
      Assert.IsFalse(await reader.ReadAsync());
    }

    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
    for (int iteration = 0; iteration < 250; iteration++)
    {
      await using ISqlRowReader reader = await connection.ExecuteReaderAsync(
        """
        WITH sequence(value) AS
        (
          SELECT CAST(1 AS int)
          UNION ALL
          SELECT value + 1 FROM sequence WHERE value < 25
        )
        SELECT value FROM sequence OPTION (MAXRECURSION 25)
        """,
        cancellationToken: timeout.Token);
      int sum = 0;
      while (await reader.ReadAsync(timeout.Token))
      {
        sum += reader.GetInt32(0);
      }

      Assert.AreEqual(325, sum);
    }
  }

  [TestMethod]
  public async Task DecodesDefaultAndUtf8VarcharCollations()
  {
    await using MsSqlConnection connection =
      await MsSqlClient.ConnectAsync(MsSqlTestEnvironment.Options);

    SqlRow row = (await connection.QueryAsync(
      """
      SELECT
        CAST('€“smart”' AS varchar(40)) AS default_code_page,
        CAST(
          N'€漢字' COLLATE Latin1_General_100_CI_AS_SC_UTF8
          AS varchar(40)
        ) AS utf8_code_page
      """))[0];

    Assert.AreEqual("€“smart”", row.GetString("default_code_page"));
    Assert.AreEqual("€漢字", row.GetString("utf8_code_page"));
  }

  [TestMethod]
  public async Task RoundsLegacyDateTimeToMilliseconds()
  {
    await using MsSqlConnection connection =
      await MsSqlClient.ConnectAsync(MsSqlTestEnvironment.Options);

    DateTime value = (await connection.QueryAsync(
      "SELECT CAST('2026-01-02T03:04:05.997' AS datetime) AS value"))[0]
      .GetDateTime(0);

    Assert.AreEqual(new DateTime(2026, 1, 2, 3, 4, 5, 997), value);
    Assert.AreEqual(0, value.Ticks % TimeSpan.TicksPerMillisecond);
  }

  [TestMethod]
  public async Task StreamsMultipleResultSetsWithTheirOwnMetadata()
  {
    await using MsSqlConnection connection =
      await MsSqlClient.ConnectAsync(MsSqlTestEnvironment.Options);
    List<SqlRow> rows = [];

    await foreach (SqlRow row in connection.StreamAsync(
      "SELECT CAST(1 AS int) AS a; SELECT CAST(N'x' AS nvarchar(10)) AS b",
      fetchSize: 50))
    {
      rows.Add(row);
    }

    Assert.HasCount(2, rows);
    Assert.AreEqual(0, rows[0].GetOrdinal("a"));
    Assert.AreEqual(1, rows[0].GetInt32(0));
    Assert.AreEqual(0, rows[1].GetOrdinal("b"));
    Assert.AreEqual("x", rows[1].GetString(0));
  }

  [TestMethod]
  public async Task DecodesSqlServer2025NativeJson()
  {
    await using MsSqlConnection connection =
      await MsSqlClient.ConnectAsync(MsSqlTestEnvironment.Options);
    if (connection.DatabaseMetadata.MajorVersion < 17)
    {
      Assert.Inconclusive("The native json type requires SQL Server 2025 or later.");
    }

    const string json = """{"name":"apex","values":[1,2]}""";
    SqlRow row = (await connection.QueryAsync(
      $$"""SELECT CAST(N'{{json}}' AS json) AS payload"""))[0];

    Assert.AreEqual(json, row.GetString("payload"));
  }

  [TestMethod]
  public async Task StopsClientStreamEarlyAndReusesConnection()
  {
    await using MsSqlConnection connection =
      await MsSqlClient.ConnectAsync(MsSqlTestEnvironment.Options);
    List<int> values = [];
    await foreach (SqlRow row in connection.StreamAsync(
                     """
                     WITH sequence(value) AS
                     (
                       SELECT CAST(1 AS int)
                       UNION ALL
                       SELECT value + 1 FROM sequence WHERE value < 10000
                     )
                     SELECT value FROM sequence OPTION (MAXRECURSION 0)
                     """,
                     fetchSize: 2))
    {
      values.Add(row.GetInt32(0));
      if (values.Count == 3)
      {
        break;
      }
    }

    CollectionAssert.AreEqual(new[] { 1, 2, 3 }, values);
    Assert.AreEqual(
      42,
      (await connection.QueryAsync("SELECT CAST(42 AS int)"))[0].GetInt32(0));
  }

  [TestMethod]
  public async Task CancelsWaitForWithAttentionAndReusesConnection()
  {
    await using MsSqlConnection connection =
      await MsSqlClient.ConnectAsync(MsSqlTestEnvironment.Options);
    using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(250));

    OperationCanceledException exception =
      await Assert.ThrowsAsync<OperationCanceledException>(
        () => connection.QueryAsync(
          "WAITFOR DELAY '00:00:10'; SELECT CAST(1 AS int)",
          cancellation.Token).AsTask());

    Assert.AreEqual(cancellation.Token, exception.CancellationToken);
    Assert.AreEqual(
      42,
      (await connection.QueryAsync("SELECT CAST(42 AS int)"))[0].GetInt32(0));
  }

  [TestMethod]
  public async Task PoolPinsLeaseUntilBorrowedReaderIsDisposed()
  {
    await using MsSqlPool pool = MsSqlPool.Create(
      MsSqlTestEnvironment.Options,
      new SqlPoolOptions
      {
        MaximumSize = 1,
        AcquisitionTimeout = TimeSpan.FromSeconds(5),
      });
    ISqlConnection first = await pool.GetConnectionAsync();
    ISqlRowReader reader = await first.ExecuteReaderAsync(
      """
      SELECT CAST(1 AS int) AS value
      UNION ALL
      SELECT CAST(2 AS int)
      """);
    await first.DisposeAsync();

    Task<ISqlConnection> pending = pool.GetConnectionAsync().AsTask();
    await Task.Delay(100);
    Assert.IsFalse(pending.IsCompleted);

    await reader.DisposeAsync();
    await using ISqlConnection second = await pending;
    Assert.AreEqual(
      1,
      (await second.QueryAsync("SELECT CAST(1 AS int)"))[0].GetInt32(0));
    Assert.AreEqual(1, pool.Size);
  }
}
