// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using Testcontainers.PostgreSql;
using Xunit;

namespace Vertx.PgClient.Tests;

/// <summary>
/// Tests for SCRAM-SHA-256 authentication.
/// Uses a separate PostgreSQL container with SCRAM authentication enabled.
/// </summary>
public class ScramAuthenticationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public async ValueTask InitializeAsync()
    {
        // Create a PostgreSQL container with SCRAM authentication (the default)
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("testdb")
            .WithUsername("scramuser")
            .WithPassword("scrampassword")
            // Don't set POSTGRES_HOST_AUTH_METHOD, so it defaults to SCRAM-SHA-256
            .Build();

        await _container.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [Fact]
    public async Task CanConnectWithScramAuthentication()
    {
        var options = new PgConnectOptions
        {
            Host = _container!.Hostname,
            Port = _container.GetMappedPublicPort(5432),
            Database = "testdb",
            User = "scramuser",
            Password = "scrampassword",
            SslMode = SslMode.Disable
        };

        await using var connection = await PgConnection.ConnectAsync(options);

        Assert.True(connection.IsOpen);
        Assert.True(connection.ProcessId > 0);

        // Execute a query to verify the connection works
        var result = await connection.QueryAsync("SELECT 1 as test");
        Assert.Equal(1, result.Count);
        Assert.Equal(1, result[0].GetValue(0).GetInteger());
    }

    [Fact]
    public async Task ScramAuthenticationFailsWithWrongPassword()
    {
        var options = new PgConnectOptions
        {
            Host = _container!.Hostname,
            Port = _container.GetMappedPublicPort(5432),
            Database = "testdb",
            User = "scramuser",
            Password = "wrongpassword",
            SslMode = SslMode.Disable
        };

        var exception = await Assert.ThrowsAsync<PgException>(async () =>
        {
            await using var connection = await PgConnection.ConnectAsync(options);
        });

        // The error should be authentication related
        Assert.Contains("password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CanExecuteQueriesAfterScramAuthentication()
    {
        var options = new PgConnectOptions
        {
            Host = _container!.Hostname,
            Port = _container.GetMappedPublicPort(5432),
            Database = "testdb",
            User = "scramuser",
            Password = "scrampassword",
            SslMode = SslMode.Disable
        };

        await using var connection = await PgConnection.ConnectAsync(options);

        var tableName = $"scram_test_{Guid.NewGuid():N}";

        // Create a table
        await connection.QueryAsync($"CREATE TABLE {tableName} (id SERIAL PRIMARY KEY, name TEXT)");

        try
        {
            // Insert data
            await connection.QueryAsync($"INSERT INTO {tableName} (name) VALUES ('test1'), ('test2')");

            // Query data
            var result = await connection.QueryAsync($"SELECT * FROM {tableName} ORDER BY id");
            Assert.Equal(2, result.Count);
            Assert.Equal("test1", result[0].GetValue("name").GetString());
            Assert.Equal("test2", result[1].GetValue("name").GetString());
        }
        finally
        {
            // Cleanup
            await connection.QueryAsync($"DROP TABLE {tableName}");
        }
    }

    [Fact]
    public async Task CanUsePreparedQueriesWithScramAuthentication()
    {
        var options = new PgConnectOptions
        {
            Host = _container!.Hostname,
            Port = _container.GetMappedPublicPort(5432),
            Database = "testdb",
            User = "scramuser",
            Password = "scrampassword",
            SslMode = SslMode.Disable
        };

        await using var connection = await PgConnection.ConnectAsync(options);

        var tableName = $"scram_prepared_{Guid.NewGuid():N}";

        // Create a table
        await connection.QueryAsync($"CREATE TABLE {tableName} (id INT, value TEXT)");

        try
        {
            // Use prepared query with parameters
            await connection.PreparedQueryAsync(
                $"INSERT INTO {tableName} (id, value) VALUES ($1, $2)",
                Tuple.Create(1, "hello")
            );

            await connection.PreparedQueryAsync(
                $"INSERT INTO {tableName} (id, value) VALUES ($1, $2)",
                Tuple.Create(2, "world")
            );

            // Query with prepared statement
            var result = await connection.PreparedQueryAsync(
                $"SELECT * FROM {tableName} WHERE id = $1",
                Tuple.Create(1)
            );

            Assert.Equal(1, result.Count);
            Assert.Equal("hello", result[0].GetValue("value").GetString());
        }
        finally
        {
            await connection.QueryAsync($"DROP TABLE {tableName}");
        }
    }
}
