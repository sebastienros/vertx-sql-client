/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

namespace Apex.MsSqlClient;

public static class MsSqlClient
{
  public static ValueTask<MsSqlConnection> ConnectAsync(
    MsSqlConnectOptions options,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(options);
    return MsSqlConnection.ConnectAsync(options, cancellationToken);
  }

  public static ValueTask<MsSqlConnection> ConnectAsync(
    string connectionString,
    CancellationToken cancellationToken = default) =>
    ConnectAsync(MsSqlConnectOptions.Parse(connectionString), cancellationToken);
}
