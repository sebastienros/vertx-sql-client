/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using Apex.SqlClient;
using Apex.SqlClient.SpecificationTests;
using Testcontainers.MySql;

namespace Apex.MySqlClient.IntegrationTests;

[TestClass]
public sealed class MySqlSqlClientSpecificationTests : SqlClientSpecificationTests
{
  private static MySqlContainer _container = null!;

  [ClassInitialize]
  public static async Task StartMySqlAsync(TestContext testContext) =>
    _container = await MySqlContainerFixture.StartAsync();

  [ClassCleanup]
  public static async Task StopMySqlAsync() => await _container.DisposeAsync();

  private static MySqlConnectOptions Options => MySqlContainerFixture.CreateOptions(_container);

  protected override string ParameterizedScalarSql => "SELECT CAST(? AS SIGNED)";

  protected override string CreateTemporaryTableSql =>
    "CREATE TEMPORARY TABLE specification_values (value INT)";

  protected override string InsertTemporaryValueSql =>
    "INSERT INTO specification_values VALUES (?)";

  protected override string CountTemporaryValuesSql =>
    "SELECT COUNT(*) FROM specification_values";

  protected override string SequenceSql =>
    "WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10) " +
    "SELECT n FROM seq ORDER BY n";

  protected override string LongRunningSql => "SELECT SLEEP(10)";

  protected override async ValueTask<ISqlConnection> OpenConnectionAsync(
    CancellationToken cancellationToken = default) =>
    await MySqlClient.ConnectAsync(Options, cancellationToken);

  protected override ISqlPool CreatePool() =>
    MySqlPool.Create(Options, new SqlPoolOptions { MaximumSize = 4 });
}
