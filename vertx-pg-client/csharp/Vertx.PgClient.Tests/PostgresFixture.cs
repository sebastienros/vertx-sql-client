// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using Testcontainers.PostgreSql;
using Xunit;

namespace Vertx.PgClient.Tests;

/// <summary>
/// Shared PostgreSQL container fixture for all tests.
/// Uses trust authentication to avoid SCRAM which isn't implemented.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;

    public PostgresFixture()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            // Use trust authentication instead of SCRAM (host_auth_method=trust means no password needed)
            .WithEnvironment("POSTGRES_HOST_AUTH_METHOD", "trust")
            .Build();
    }

    public string Host => _container.Hostname;
    public int Port => _container.GetMappedPublicPort(5432);
    public string Database => "testdb";
    public string Username => "testuser";
    public string Password => "testpass";

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>
    /// Gets the connection URI in PostgreSQL format (postgresql://user@host:port/database).
    /// </summary>
    public string ConnectionUri => $"postgresql://{Username}:{Password}@{Host}:{Port}/{Database}";

    public PgConnectOptions CreateConnectOptions()
    {
        return new PgConnectOptions
        {
            Host = Host,
            Port = Port,
            Database = Database,
            User = Username,
            Password = Password,
            SslMode = SslMode.Disable
        };
    }

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

/// <summary>
/// Collection definition for sharing the PostgreSQL container across tests.
/// </summary>
[CollectionDefinition("PostgreSQL")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
}
