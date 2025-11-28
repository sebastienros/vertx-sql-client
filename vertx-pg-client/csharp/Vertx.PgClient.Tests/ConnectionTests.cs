// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using Xunit;

namespace Vertx.PgClient.Tests;

[Collection("PostgreSQL")]
public class ConnectionTests
{
    private readonly PostgresFixture _fixture;

    public ConnectionTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanConnect()
    {
        var options = _fixture.CreateConnectOptions();

        await using var connection = await PgConnection.ConnectAsync(options);

        Assert.True(connection.IsOpen);
        Assert.True(connection.ProcessId > 0);
    }

    [Fact]
    public async Task CanQueryServerVersion()
    {
        var options = _fixture.CreateConnectOptions();

        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT version()");

        Assert.Equal(1, result.Count);
        var version = result[0].GetValue(0).GetString();
        Assert.Contains("PostgreSQL", version);
    }

    [Fact]
    public async Task CanGetDatabaseMetadata()
    {
        var options = _fixture.CreateConnectOptions();

        await using var connection = await PgConnection.ConnectAsync(options);

        var metadata = connection.DatabaseMetadata;

        Assert.Equal("PostgreSQL", metadata.ProductName);
        Assert.True(metadata.MajorVersion >= 16);
        Assert.NotNull(metadata.ServerVersion);
    }

    [Fact]
    public async Task CanCloseConnection()
    {
        var options = _fixture.CreateConnectOptions();

        var connection = await PgConnection.ConnectAsync(options);
        Assert.True(connection.IsOpen);

        await connection.CloseAsync();
        Assert.False(connection.IsOpen);
    }
}
