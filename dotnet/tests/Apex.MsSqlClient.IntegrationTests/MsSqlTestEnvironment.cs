/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using Testcontainers.MsSql;

namespace Apex.MsSqlClient.IntegrationTests;

internal static class MsSqlTestEnvironment
{
  private const string Password = "Apex_Testcontainers!2026#Sql";
  private static MsSqlContainer? _container;

  internal static MsSqlConnectOptions Options
  {
    get
    {
      MsSqlContainer container = _container ??
        throw new InvalidOperationException("The SQL Server container is not running.");
      return new MsSqlConnectOptions
      {
        Host = container.Hostname,
        Port = container.GetMappedPublicPort(1433),
        Database = "master",
        Username = "sa",
        Password = Password,
        EncryptionMode = MsSqlEncryptionMode.Require,
        TrustServerCertificate = true,
      };
    }
  }

  internal static async Task StartAsync()
  {
    string image = Environment.GetEnvironmentVariable("MSSQL_IMAGE") ??
      "mcr.microsoft.com/mssql/server:2025-latest";
    _container = new MsSqlBuilder(image)
      .WithPassword(Password)
      .Build();
    await _container.StartAsync();
  }

  internal static async Task StopAsync()
  {
    if (_container is not null)
    {
      await _container.DisposeAsync();
      _container = null;
    }
  }
}
