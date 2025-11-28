// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using Testcontainers.PostgreSql;
using Xunit;

namespace Vertx.PgClient.Tests;

/// <summary>
/// Tests for various PostgreSQL authentication methods.
/// Focused on MD5, Trust, and Clear Text (Password) authentication.
/// SCRAM-SHA-256 tests are in ScramAuthenticationTests.cs.
/// </summary>
public class AuthenticationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _md5Container;
    private PostgreSqlContainer? _trustContainer;
    private PostgreSqlContainer? _clearTextContainer;

    public async ValueTask InitializeAsync()
    {
        // Start containers in parallel for faster test execution
        var md5Task = CreateMd5ContainerAsync();
        var trustTask = CreateTrustContainerAsync();
        var clearTextTask = CreateClearTextContainerAsync();

        await Task.WhenAll(md5Task, trustTask, clearTextTask);

        _md5Container = md5Task.Result;
        _trustContainer = trustTask.Result;
        _clearTextContainer = clearTextTask.Result;
    }

    public async ValueTask DisposeAsync()
    {
        var tasks = new List<Task>();
        if (_md5Container != null) tasks.Add(_md5Container.DisposeAsync().AsTask());
        if (_trustContainer != null) tasks.Add(_trustContainer.DisposeAsync().AsTask());
        if (_clearTextContainer != null) tasks.Add(_clearTextContainer.DisposeAsync().AsTask());
        await Task.WhenAll(tasks);
    }

    private static async Task<PostgreSqlContainer> CreateMd5ContainerAsync()
    {
        var container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .WithEnvironment("POSTGRES_HOST_AUTH_METHOD", "md5")
            .WithEnvironment("POSTGRES_INITDB_ARGS", "--auth-host=md5")
            .Build();
        await container.StartAsync();
        return container;
    }

    private static async Task<PostgreSqlContainer> CreateTrustContainerAsync()
    {
        var container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .WithEnvironment("POSTGRES_HOST_AUTH_METHOD", "trust")
            .Build();
        await container.StartAsync();
        return container;
    }

    private static async Task<PostgreSqlContainer> CreateClearTextContainerAsync()
    {
        var container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .WithEnvironment("POSTGRES_HOST_AUTH_METHOD", "password")
            .WithEnvironment("POSTGRES_INITDB_ARGS", "--auth-host=password")
            .Build();
        await container.StartAsync();
        return container;
    }

    private static PgConnectOptions CreateOptions(PostgreSqlContainer container, string password = "testpass")
    {
        return new PgConnectOptions
        {
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(5432),
            Database = "testdb",
            User = "testuser",
            Password = password,
            SslMode = SslMode.Disable
        };
    }

    #region MD5 Authentication Tests

    [Fact]
    public async Task Md5_CanConnect()
    {
        var options = CreateOptions(_md5Container!);

        await using var connection = await PgConnection.ConnectAsync(options);

        Assert.True(connection.IsOpen);
    }

    [Fact]
    public async Task Md5_CanExecuteQuery()
    {
        var options = CreateOptions(_md5Container!);

        await using var connection = await PgConnection.ConnectAsync(options);
        var result = await connection.QueryAsync("SELECT 1 as value");

        Assert.Equal(1, result.Count);
        Assert.Equal(1, result[0].GetValue(0).GetInteger());
    }

    [Fact]
    public async Task Md5_WrongPassword_Fails()
    {
        var options = CreateOptions(_md5Container!, password: "wrongpassword");

        var ex = await Assert.ThrowsAsync<PgException>(
            () => PgConnection.ConnectAsync(options).AsTask());

        Assert.Contains("authentication failed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Md5_CanUsePreparedQueries()
    {
        var options = CreateOptions(_md5Container!);

        await using var connection = await PgConnection.ConnectAsync(options);
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::int + $2::int as sum",
            Tuple.Create(10, 20));

        Assert.Equal(1, result.Count);
        Assert.Equal(30, result[0].GetValue(0).GetInteger());
    }

    #endregion

    #region Trust Authentication Tests

    [Fact]
    public async Task Trust_CanConnect()
    {
        var options = CreateOptions(_trustContainer!);

        await using var connection = await PgConnection.ConnectAsync(options);

        Assert.True(connection.IsOpen);
    }

    [Fact]
    public async Task Trust_CanConnectWithoutPassword()
    {
        // Trust authentication doesn't require a password (empty string is fine)
        var options = CreateOptions(_trustContainer!, password: "");

        await using var connection = await PgConnection.ConnectAsync(options);

        Assert.True(connection.IsOpen);
    }

    [Fact]
    public async Task Trust_CanExecuteQuery()
    {
        var options = CreateOptions(_trustContainer!);

        await using var connection = await PgConnection.ConnectAsync(options);
        var result = await connection.QueryAsync("SELECT current_user");

        Assert.Equal(1, result.Count);
        Assert.Equal("testuser", result[0].GetValue(0).GetString());
    }

    [Fact]
    public async Task Trust_CanUsePreparedQueries()
    {
        var options = CreateOptions(_trustContainer!);

        await using var connection = await PgConnection.ConnectAsync(options);
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::text || ' ' || $2::text as greeting",
            Tuple.Create("Hello", "World"));

        Assert.Equal(1, result.Count);
        Assert.Equal("Hello World", result[0].GetValue(0).GetString());
    }

    #endregion

    #region Clear Text (Password) Authentication Tests

    [Fact]
    public async Task ClearText_CanConnect()
    {
        var options = CreateOptions(_clearTextContainer!);

        await using var connection = await PgConnection.ConnectAsync(options);

        Assert.True(connection.IsOpen);
    }

    [Fact]
    public async Task ClearText_CanExecuteQuery()
    {
        var options = CreateOptions(_clearTextContainer!);

        await using var connection = await PgConnection.ConnectAsync(options);
        var result = await connection.QueryAsync("SELECT 1 as value");

        Assert.Equal(1, result.Count);
        Assert.Equal(1, result[0].GetValue(0).GetInteger());
    }

    [Fact]
    public async Task ClearText_WrongPassword_Fails()
    {
        var options = CreateOptions(_clearTextContainer!, password: "wrongpassword");

        var ex = await Assert.ThrowsAsync<PgException>(
            () => PgConnection.ConnectAsync(options).AsTask());

        Assert.Contains("authentication failed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClearText_CanUsePreparedQueries()
    {
        var options = CreateOptions(_clearTextContainer!);

        await using var connection = await PgConnection.ConnectAsync(options);
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::int * $2::int as product",
            Tuple.Create(6, 7));

        Assert.Equal(1, result.Count);
        Assert.Equal(42, result[0].GetValue(0).GetInteger());
    }

    #endregion
}
