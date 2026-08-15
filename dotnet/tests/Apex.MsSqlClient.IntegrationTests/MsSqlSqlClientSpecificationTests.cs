/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using Apex.SqlClient;
using Apex.SqlClient.SpecificationTests;

namespace Apex.MsSqlClient.IntegrationTests;

[TestClass]
public sealed class MsSqlSqlClientSpecificationTests : SqlClientSpecificationTests
{
    protected override string ParameterizedScalarSql =>
      "SELECT CAST(@P1 AS int)";

    protected override string CreateTemporaryTableSql =>
      "CREATE TABLE #specification_values (value int NOT NULL)";

    protected override string InsertTemporaryValueSql =>
      "INSERT INTO #specification_values VALUES (@P1)";

    protected override string CountTemporaryValuesSql =>
      "SELECT COUNT(*) FROM #specification_values";

    protected override string SequenceSql =>
      """
    WITH sequence(value) AS
    (
      SELECT CAST(1 AS int)
      UNION ALL
      SELECT value + 1 FROM sequence WHERE value < 10
    )
    SELECT value FROM sequence OPTION (MAXRECURSION 10)
    """;

    protected override string LongRunningSql =>
      "WAITFOR DELAY '00:00:10'; SELECT CAST(1 AS int)";

    protected override async ValueTask<ISqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default) =>
      await MsSqlClient.ConnectAsync(MsSqlTestEnvironment.Options, cancellationToken);

    protected override ISqlPool CreatePool() =>
      MsSqlPool.Create(
        MsSqlTestEnvironment.Options,
        new SqlPoolOptions { MaximumSize = 4 });
}
