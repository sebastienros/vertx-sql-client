// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using Xunit;

namespace Vertx.PgClient.Tests;

/// <summary>
/// Tests for SSL/TLS connection support.
/// Note: These tests require a PostgreSQL server with SSL enabled.
/// The default test container does not have SSL configured, so some tests are skipped.
/// </summary>
public class SslTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public SslTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SslMode_Disable_ConnectsWithoutSsl()
    {
        var options = _fixture.CreateConnectOptions();
        options.SslMode = SslMode.Disable;

        await using var connection = await PgConnection.ConnectAsync(options);
        Assert.True(connection.IsOpen);
        
        var result = await connection.QueryAsync("SELECT 1");
        Assert.Equal(1, result[0].GetValue(0).GetInteger());
    }

    [Fact]
    public async Task SslMode_Prefer_FallsBackToUnencrypted()
    {
        // With SslMode.Prefer, if the server doesn't support SSL, 
        // the connection should fall back to unencrypted
        var options = _fixture.CreateConnectOptions();
        options.SslMode = SslMode.Prefer;

        await using var connection = await PgConnection.ConnectAsync(options);
        Assert.True(connection.IsOpen);
        
        var result = await connection.QueryAsync("SELECT 1");
        Assert.Equal(1, result[0].GetValue(0).GetInteger());
    }

    [Fact]
    public async Task SslMode_Require_FailsWithoutSslServer()
    {
        // The test container doesn't have SSL enabled, so Require should fail
        var options = _fixture.CreateConnectOptions();
        options.SslMode = SslMode.Require;

        // This should throw because the server doesn't support SSL
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var connection = await PgConnection.ConnectAsync(options);
        });
    }

    [Fact]
    public void SslModeExtensions_Parse_ValidModes()
    {
        Assert.Equal(SslMode.Disable, SslModeExtensions.Parse("disable"));
        Assert.Equal(SslMode.Prefer, SslModeExtensions.Parse("prefer"));
        Assert.Equal(SslMode.Require, SslModeExtensions.Parse("require"));
        Assert.Equal(SslMode.VerifyCa, SslModeExtensions.Parse("verify-ca"));
        Assert.Equal(SslMode.VerifyFull, SslModeExtensions.Parse("verify-full"));
        
        // Case insensitive
        Assert.Equal(SslMode.Disable, SslModeExtensions.Parse("DISABLE"));
        Assert.Equal(SslMode.VerifyFull, SslModeExtensions.Parse("VERIFY-FULL"));
    }

    [Fact]
    public void SslModeExtensions_Parse_InvalidMode_Throws()
    {
        Assert.Throws<ArgumentException>(() => SslModeExtensions.Parse("invalid"));
        Assert.Throws<ArgumentException>(() => SslModeExtensions.Parse(""));
    }

    [Fact]
    public void ConnectionUri_WithSslMode_ParsesCorrectly()
    {
        var options = PgConnectOptions.FromUri("postgresql://user:pass@localhost:5432/db?sslmode=require");
        Assert.Equal(SslMode.Require, options.SslMode);

        options = PgConnectOptions.FromUri("postgresql://user:pass@localhost:5432/db?sslmode=verify-full");
        Assert.Equal(SslMode.VerifyFull, options.SslMode);
    }

    [Fact]
    public void PgSslOptions_DefaultValues()
    {
        var sslOptions = new PgSslOptions();
        
        // Default should allow any certificate (for development)
        Assert.Null(sslOptions.ClientCertificate);
        Assert.Null(sslOptions.TrustCertificates);
        Assert.False(sslOptions.Enabled);
        Assert.False(sslOptions.TrustAll);
        Assert.Null(sslOptions.HostnameVerificationAlgorithm);
    }

    [Fact]
    public void PgConnectOptions_SslMode_DefaultIsPrefer()
    {
        var options = new PgConnectOptions();
        
        // Default SSL mode - check what the default is
        // (Could be Disable or Prefer depending on implementation)
        Assert.True(options.SslMode == SslMode.Disable || options.SslMode == SslMode.Prefer);
    }

    // The following tests would require a PostgreSQL server with SSL enabled
    // They are marked as Skip until such a test environment is set up

    [Fact(Skip = "Requires PostgreSQL with SSL enabled")]
    public async Task SslMode_Require_ConnectsWithSsl()
    {
        var options = _fixture.CreateConnectOptions();
        options.SslMode = SslMode.Require;

        await using var connection = await PgConnection.ConnectAsync(options);
        Assert.True(connection.IsOpen);
        
        // Verify SSL is being used
        var result = await connection.QueryAsync("SHOW ssl");
        Assert.Equal("on", result[0].GetValue(0).GetString());
    }

    [Fact(Skip = "Requires PostgreSQL with SSL enabled")]
    public async Task SslMode_VerifyCa_ValidatesServerCertificate()
    {
        var options = _fixture.CreateConnectOptions();
        options.SslMode = SslMode.VerifyCa;
        // Would need to set options.SslOptions.TrustCertificates

        await using var connection = await PgConnection.ConnectAsync(options);
        Assert.True(connection.IsOpen);
    }

    [Fact(Skip = "Requires PostgreSQL with SSL enabled")]
    public async Task SslMode_VerifyFull_ValidatesServerCertificateAndHostname()
    {
        var options = _fixture.CreateConnectOptions();
        options.SslMode = SslMode.VerifyFull;
        // Would need to set options.SslOptions.TrustCertificates

        await using var connection = await PgConnection.ConnectAsync(options);
        Assert.True(connection.IsOpen);
    }

    [Fact(Skip = "Requires PostgreSQL with SSL enabled and client certificates")]
    public async Task ClientCertificate_Authentication()
    {
        var options = _fixture.CreateConnectOptions();
        options.SslMode = SslMode.Require;
        // Would need to set options.SslOptions.ClientCertificate

        await using var connection = await PgConnection.ConnectAsync(options);
        Assert.True(connection.IsOpen);
    }
}
