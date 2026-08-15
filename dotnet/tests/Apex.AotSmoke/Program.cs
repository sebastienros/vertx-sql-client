/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Text.Json;
using Apex.MsSqlClient;
using Apex.PgClient;
using Apex.SqlClient;

PgConnectOptions pgOptions = PgConnectOptions.Parse(
  "host=localhost port=5432 user=user dbname=db sslmode=disable");
MsSqlConnectOptions msSqlOptions = MsSqlConnectOptions.Parse(
  "Server=tcp:localhost,1433;Database=db;User ID=user;Encrypt=Strict;" +
  "TrustServerCertificate=false;Application Name=aot-smoke");
Func<MsSqlConnectOptions, CancellationToken, ValueTask<MsSqlConnection>> msSqlConnect =
  MsSqlClient.ConnectAsync;
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
if (System.Text.Encoding.GetEncoding(1252).GetString([0x80]) != "€")
{
  throw new InvalidOperationException("NativeAOT code-page decoding failed.");
}
using JsonDocument json = JsonDocument.Parse("""{"aot":true}""");
SqlParameters parameters = SqlParameters.Create(
  SqlValue.Null,
  true,
  (short)7,
  42,
  84L,
  1.5f,
  2.5d,
  12.34m,
  "value",
  new byte[] { 1, 2, 3 },
  Guid.Empty,
  new DateOnly(2026, 8, 14),
  new TimeOnly(18, 11, 14),
  new DateTime(2026, 8, 14, 18, 11, 14, DateTimeKind.Unspecified),
  new DateTimeOffset(2026, 8, 14, 18, 11, 14, TimeSpan.Zero),
  json.RootElement.Clone());
Console.WriteLine(
  $"pg={pgOptions.Host}:{pgOptions.Port}/{pgOptions.Database} " +
  $"mssql={msSqlOptions.Host}:{msSqlOptions.Port}/{msSqlOptions.Database} " +
  $"encryption={msSqlOptions.EncryptionMode} parameters={parameters.Count}");
GC.KeepAlive(msSqlConnect);
