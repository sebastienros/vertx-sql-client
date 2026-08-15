/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using Apex.MySqlClient;
using Apex.PgClient;
using Apex.SqlClient;

PgConnectOptions pgOptions = PgConnectOptions.Parse(
  "host=localhost port=5432 user=user password=pass dbname=db sslmode=disable");
MySqlConnectOptions mySqlOptions = new()
{
  Host = "localhost",
  Port = 3306,
  Database = "db",
  Username = "user",
};
SqlParameters parameters = SqlParameters.Create(42, "value", DateOnly.FromDateTime(DateTime.UtcNow));
Console.WriteLine(
  $"pg={pgOptions.Host}:{pgOptions.Port}/{pgOptions.Database} " +
  $"mysql={mySqlOptions.Host}:{mySqlOptions.Port}/{mySqlOptions.Database} " +
  $"parameters={parameters.Count}");

if (Environment.GetEnvironmentVariable("APEX_AOT_MYSQL_CONNECTION_STRING") is { Length: > 0 } connectionString)
{
  await using MySqlConnection connection = await MySqlClient.ConnectAsync(connectionString);
  await connection.PingAsync();
  SqlRowSet rows = await connection.QueryAsync("SELECT 1 AS value, 'aot' AS label");
  if (rows[0].GetInt32("value") != 1 || rows[0].GetString("label") != "aot")
  {
    throw new InvalidOperationException("The NativeAOT MySQL text query returned unexpected values.");
  }

  await using ISqlPreparedStatement statement =
    await connection.PrepareAsync("SELECT ? + 1 AS value");
  rows = await statement.QueryAsync(SqlParameters.Create(41));
  if (rows[0].GetInt64("value") != 42)
  {
    throw new InvalidOperationException(
      "The NativeAOT MySQL prepared query returned an unexpected value.");
  }

  await connection.ExecuteAsync(
    "CREATE TEMPORARY TABLE apex_aot_values (" +
    "id BIGINT AUTO_INCREMENT PRIMARY KEY, value INT NOT NULL)");
  SqlCommandResult inserted = await connection.ExecuteAsync(
    "INSERT INTO apex_aot_values(value) VALUES (?)",
    SqlParameters.Create(7));
  if (inserted.AffectedRows != 1 || inserted.LastInsertId != 1)
  {
    throw new InvalidOperationException("The NativeAOT MySQL command metadata is invalid.");
  }

  await using (ISqlTransaction transaction = await connection.BeginTransactionAsync())
  {
    await connection.ExecuteAsync("INSERT INTO apex_aot_values(value) VALUES (8)");
  }

  rows = await connection.QueryAsync("SELECT COUNT(*) AS count FROM apex_aot_values");
  if (rows[0].GetInt64("count") != 1)
  {
    throw new InvalidOperationException("The NativeAOT MySQL transaction was not rolled back.");
  }

  int readerSum = 0;
  await using (ISqlRowReader reader =
               await connection.ExecuteReaderAsync("SELECT 1 UNION ALL SELECT 2"))
  {
    while (await reader.ReadAsync())
    {
      readerSum += reader.GetInt32(0);
    }
  }

  int streamSum = 0;
  await foreach (SqlRow row in connection.StreamAsync(
                   "SELECT 1 UNION ALL SELECT 2",
                   fetchSize: 1))
  {
    streamSum += row.GetInt32(0);
  }

  if (readerSum != 3 || streamSum != 3)
  {
    throw new InvalidOperationException("The NativeAOT MySQL streaming results are invalid.");
  }

  using (CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(200)))
  {
    try
    {
      await connection.QueryAsync("SELECT SLEEP(10)", cancellation.Token);
      throw new InvalidOperationException("The NativeAOT MySQL cancellation did not interrupt the query.");
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
    }
  }

  rows = await connection.QueryAsync("SELECT 9");
  if (rows[0].GetInt32(0) != 9)
  {
    throw new InvalidOperationException(
      "The NativeAOT MySQL connection was not reusable after cancellation.");
  }

  await using MySqlPool pool = MySqlPool.Create(
    connectionString,
    new SqlPoolOptions { MaximumSize = 2 });
  rows = await pool.QueryAsync("SELECT 10");
  if (rows[0].GetInt32(0) != 10)
  {
    throw new InvalidOperationException("The NativeAOT MySQL pool returned an unexpected value.");
  }
}
