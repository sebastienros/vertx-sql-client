// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using Testcontainers.PostgreSql;

namespace Vertx.PgClient.Benchmarks;

/// <summary>
/// Manages a PostgreSQL test container with TechEmpower Fortune data.
/// </summary>
public sealed class PostgresFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainer _container;
    private bool _initialized;

    public PostgresFixture()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:17")
            .WithDatabase("benchmarkdb")
            .WithUsername("benchmarkdbuser")
            .WithPassword("benchmarkdbpass")
            .Build();
    }

    public string ConnectionString => _container.GetConnectionString();

    public PgConnectOptions CreateConnectOptions()
    {
        return new PgConnectOptions
        {
            Host = _container.Hostname,
            Port = _container.GetMappedPublicPort(5432),
            Database = "benchmarkdb",
            User = "benchmarkdbuser",
            Password = "benchmarkdbpass"
        };
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        await _container.StartAsync();

        // Create the Fortune table and insert TechEmpower benchmark data
        await using var connection = await PgConnection.ConnectAsync(CreateConnectOptions());

        await connection.QueryAsync("""
            CREATE TABLE fortune (
                id integer NOT NULL PRIMARY KEY,
                message varchar(2048) NOT NULL
            )
            """);

        await connection.QueryAsync("""
            INSERT INTO fortune (id, message) VALUES
            (1, 'fortune: No such file or directory'),
            (2, 'A computer scientist is someone who fixes things that aren''t broken.'),
            (3, 'After enough decimal places, nobody gives a damn.'),
            (4, 'A bad random number generator: 1, 1, 1, 1, 1, 4.33e+67, 1, 1, 1'),
            (5, 'A computer program does what you tell it to do, not what you want it to do.'),
            (6, 'Emacs is a nice operating system, but I prefer UNIX. — Tom Christaensen'),
            (7, 'Any program that runs right is obsolete.'),
            (8, 'A list is only as strong as its weakest link. — Donald Knuth'),
            (9, 'Feature: A bug with seniority.'),
            (10, 'Computers make very fast, very accurate mistakes.'),
            (11, '<script>alert("This should not be displayed in a browser alert box.");</script>'),
            (12, 'フレームワークのベンチマーク')
            """);

        // Create World table for additional benchmarks
        await connection.QueryAsync("""
            CREATE TABLE world (
                id integer NOT NULL PRIMARY KEY,
                randomnumber integer NOT NULL DEFAULT 0
            )
            """);

        await connection.QueryAsync("""
            INSERT INTO world (id, randomnumber)
            SELECT x.id, floor(random() * 10000 + 1)::integer
            FROM generate_series(1, 10000) AS x(id)
            """);

        _initialized = true;
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
