/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

namespace Apex.MySqlClient;

/// <summary>Entry points for connecting to MySQL and MariaDB.</summary>
public static class MySqlClient
{
    /// <summary>Opens a connection with the supplied settings.</summary>
    /// <param name="options">How to reach and authenticate against the server.</param>
    /// <param name="cancellationToken">Cancels the connection attempt.</param>
    /// <returns>An authenticated connection.</returns>
    public static ValueTask<MySqlConnection> ConnectAsync(
        MySqlConnectOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return MySqlConnection.ConnectAsync(options, cancellationToken);
    }

    /// <summary>Opens a connection described by a connection string.</summary>
    /// <param name="connectionString">
    /// A <c>mysql://</c> or <c>mariadb://</c> URI, or a semicolon separated keyword string.
    /// </param>
    /// <param name="cancellationToken">Cancels the connection attempt.</param>
    /// <returns>An authenticated connection.</returns>
    public static ValueTask<MySqlConnection> ConnectAsync(
        string connectionString,
        CancellationToken cancellationToken = default) =>
      ConnectAsync(MySqlConnectOptions.Parse(connectionString), cancellationToken);
}
