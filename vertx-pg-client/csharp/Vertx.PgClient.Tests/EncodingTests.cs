// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using System.Net;
using Xunit;

namespace Vertx.PgClient.Tests;

/// <summary>
/// Tests for encoding (sending parameters) of all PostgreSQL data types via prepared queries.
/// </summary>
[Collection("PostgreSQL")]
public class EncodingTests
{
    private readonly PostgresFixture _fixture;

    public EncodingTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    #region Boolean

    [Fact]
    public async Task CanEncodeBoolTrue()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.PreparedQueryAsync(
            "SELECT $1::bool as b",
            Tuple.Create(true)
        );

        Assert.Equal(1, result.Count);
        Assert.True(result[0].GetBoolean("b"));
    }

    [Fact]
    public async Task CanEncodeBoolFalse()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.PreparedQueryAsync(
            "SELECT $1::bool as b",
            Tuple.Create(false)
        );

        Assert.Equal(1, result.Count);
        Assert.False(result[0].GetBoolean("b"));
    }

    #endregion

    #region Integer Types

    [Fact]
    public async Task CanEncodeInt2()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.PreparedQueryAsync(
            "SELECT $1::int2 as n",
            Tuple.Create((short)12345)
        );

        Assert.Equal(1, result.Count);
        Assert.Equal((short)12345, result[0].Get<short>("n"));
    }

    [Fact]
    public async Task CanEncodeInt4()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.PreparedQueryAsync(
            "SELECT $1::int4 as n",
            Tuple.Create(123456789)
        );

        Assert.Equal(1, result.Count);
        Assert.Equal(123456789, result[0].GetInteger("n"));
    }

    [Fact]
    public async Task CanEncodeInt8()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.PreparedQueryAsync(
            "SELECT $1::int8 as n",
            Tuple.Create(9223372036854775807L)
        );

        Assert.Equal(1, result.Count);
        Assert.Equal(9223372036854775807L, result[0].GetLong("n"));
    }

    #endregion

    #region Floating Point Types

    [Fact]
    public async Task CanEncodeFloat4()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.PreparedQueryAsync(
            "SELECT $1::float4 as n",
            Tuple.Create(3.14f)
        );

        Assert.Equal(1, result.Count);
        Assert.Equal(3.14f, result[0].Get<float>("n"), 0.01f);
    }

    [Fact]
    public async Task CanEncodeFloat8()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.PreparedQueryAsync(
            "SELECT $1::float8 as n",
            Tuple.Create(3.141592653589793)
        );

        Assert.Equal(1, result.Count);
        Assert.Equal(3.141592653589793, result[0].GetDouble("n"), 0.0000000001);
    }

    #endregion

    #region String Types

    [Fact]
    public async Task CanEncodeText()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.PreparedQueryAsync(
            "SELECT $1::text as t",
            Tuple.Create("Hello, World!")
        );

        Assert.Equal(1, result.Count);
        Assert.Equal("Hello, World!", result[0].GetString("t"));
    }

    [Fact]
    public async Task CanEncodeVarchar()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.PreparedQueryAsync(
            "SELECT $1::varchar(50) as v",
            Tuple.Create("PostgreSQL")
        );

        Assert.Equal(1, result.Count);
        Assert.Equal("PostgreSQL", result[0].GetString("v"));
    }

    #endregion

    #region Date/Time Types

    [Fact]
    public async Task CanEncodeDate()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var date = new DateOnly(2024, 6, 15);
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::date as d",
            Tuple.Create(date)
        );

        Assert.Equal(1, result.Count);
        Assert.Equal(date, result[0].Get<DateOnly>("d"));
    }

    [Fact]
    public async Task CanEncodeTime()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var time = new TimeOnly(14, 30, 45);
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::time as t",
            Tuple.Create(time)
        );

        Assert.Equal(1, result.Count);
        Assert.Equal(time, result[0].Get<TimeOnly>("t"));
    }

    [Fact]
    public async Task CanEncodeTimestamp()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var timestamp = new DateTime(2024, 6, 15, 14, 30, 0, DateTimeKind.Unspecified);
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::timestamp as ts",
            Tuple.Create(timestamp)
        );

        Assert.Equal(1, result.Count);
        var returnedTs = result[0].Get<DateTime>("ts");
        Assert.Equal(timestamp, returnedTs);
    }

    [Fact]
    public async Task CanEncodeTimestamptz()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var timestamp = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::timestamptz as ts",
            Tuple.Create(timestamp)
        );

        Assert.Equal(1, result.Count);
        var returnedTs = result[0].Get<DateTimeOffset>("ts");
        Assert.Equal(timestamp.UtcDateTime, returnedTs.UtcDateTime);
    }

    [Fact]
    public async Task CanEncodeInterval()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var interval = new Data.Interval(1, 2, 3, 4, 5, 6, 0);
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::interval as i",
            Tuple.Create(interval)
        );

        Assert.Equal(1, result.Count);
        var returnedInterval = result[0].Get<Data.Interval>("i");
        Assert.NotNull(returnedInterval);
        Assert.Equal(1, returnedInterval.Years);
        Assert.Equal(2, returnedInterval.Months);
        Assert.Equal(3, returnedInterval.Days);
        Assert.Equal(4, returnedInterval.Hours);
        Assert.Equal(5, returnedInterval.Minutes);
        Assert.Equal(6, returnedInterval.Seconds);
    }

    #endregion

    #region Binary Types

    [Fact]
    public async Task CanEncodeBytea()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var bytes = new byte[] { 0x48, 0x65, 0x6c, 0x6c, 0x6f }; // "Hello"
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::bytea as data",
            Tuple.Create(bytes)
        );

        Assert.Equal(1, result.Count);
        var returnedBytes = result[0].Get<byte[]>("data");
        Assert.NotNull(returnedBytes);
        Assert.Equal(bytes, returnedBytes);
    }

    #endregion

    #region UUID

    [Fact]
    public async Task CanEncodeUuid()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var uuid = Guid.NewGuid();
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::uuid as id",
            Tuple.Create(uuid)
        );

        Assert.Equal(1, result.Count);
        Assert.Equal(uuid, result[0].Get<Guid>("id"));
    }

    #endregion

    #region JSON Types

    [Fact]
    public async Task CanEncodeJson()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var json = "{\"name\": \"test\", \"value\": 42}";
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::json as j",
            Tuple.Create(json)
        );

        Assert.Equal(1, result.Count);
        var returnedJson = result[0].GetString("j");
        Assert.Contains("\"name\"", returnedJson);
        Assert.Contains("\"test\"", returnedJson);
    }

    [Fact]
    public async Task CanEncodeJsonb()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var json = "{\"key\": \"value\"}";
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::jsonb as jb",
            Tuple.Create(json)
        );

        Assert.Equal(1, result.Count);
        var returnedJsonb = result[0].GetString("jb");
        Assert.Contains("key", returnedJsonb);
        Assert.Contains("value", returnedJsonb);
    }

    #endregion

    #region Geometric Types

    [Fact]
    public async Task CanEncodePoint()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var point = new Data.Point(1.5, 2.5);
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::point as p",
            Tuple.Create(point)
        );

        Assert.Equal(1, result.Count);
        var returnedPoint = result[0].Get<Data.Point>("p");
        Assert.Equal(1.5, returnedPoint.X, 0.01);
        Assert.Equal(2.5, returnedPoint.Y, 0.01);
    }

    [Fact]
    public async Task CanEncodeLine()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var line = new Data.Line(1, 2, 3);
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::line as l",
            Tuple.Create(line)
        );

        Assert.Equal(1, result.Count);
        var returnedLine = result[0].Get<Data.Line>("l");
        Assert.NotNull(returnedLine);
        Assert.Equal(1, returnedLine.A, 0.01);
        Assert.Equal(2, returnedLine.B, 0.01);
        Assert.Equal(3, returnedLine.C, 0.01);
    }

    [Fact]
    public async Task CanEncodeLineSegment()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var lseg = new Data.LineSegment(new Data.Point(0, 0), new Data.Point(1, 1));
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::lseg as ls",
            Tuple.Create(lseg)
        );

        Assert.Equal(1, result.Count);
        var returnedLseg = result[0].Get<Data.LineSegment>("ls");
        Assert.NotNull(returnedLseg);
        Assert.Equal(0, returnedLseg.P1.X, 0.01);
        Assert.Equal(0, returnedLseg.P1.Y, 0.01);
        Assert.Equal(1, returnedLseg.P2.X, 0.01);
        Assert.Equal(1, returnedLseg.P2.Y, 0.01);
    }

    [Fact]
    public async Task CanEncodeBox()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var box = new Data.Box(new Data.Point(1, 1), new Data.Point(0, 0));
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::box as b",
            Tuple.Create(box)
        );

        Assert.Equal(1, result.Count);
        var returnedBox = result[0].Get<Data.Box>("b");
        Assert.NotNull(returnedBox);
    }

    [Fact]
    public async Task CanEncodeCircle()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var circle = new Data.Circle(new Data.Point(1, 2), 3);
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::circle as c",
            Tuple.Create(circle)
        );

        Assert.Equal(1, result.Count);
        var returnedCircle = result[0].Get<Data.Circle>("c");
        Assert.NotNull(returnedCircle);
        Assert.Equal(1, returnedCircle.CenterPoint.X, 0.01);
        Assert.Equal(2, returnedCircle.CenterPoint.Y, 0.01);
        Assert.Equal(3, returnedCircle.Radius, 0.01);
    }

    [Fact]
    public async Task CanEncodePolygon()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var polygon = new Data.Polygon(new List<Data.Point>
        {
            new(0, 0),
            new(1, 0),
            new(1, 1),
            new(0, 1)
        });
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::polygon as p",
            Tuple.Create(polygon)
        );

        Assert.Equal(1, result.Count);
        var returnedPolygon = result[0].Get<Data.Polygon>("p");
        Assert.NotNull(returnedPolygon);
        Assert.Equal(4, returnedPolygon.Points.Count);
    }

    [Fact]
    public async Task CanEncodePath()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var path = new Data.Path(true, new List<Data.Point>
        {
            new(0, 0),
            new(1, 1),
            new(2, 0)
        });
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::path as p",
            Tuple.Create(path)
        );

        Assert.Equal(1, result.Count);
        var returnedPath = result[0].Get<Data.Path>("p");
        Assert.NotNull(returnedPath);
        Assert.Equal(3, returnedPath.Points.Count);
        Assert.True(returnedPath.IsOpen);
    }

    #endregion

    #region Network Types

    [Fact]
    public async Task CanEncodeInet()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var inet = new Data.Inet().SetAddress(IPAddress.Parse("192.168.1.1")).SetNetmask(32);
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::inet as ip",
            Tuple.Create(inet)
        );

        Assert.Equal(1, result.Count);
        var returnedInet = result[0].Get<Data.Inet>("ip");
        Assert.NotNull(returnedInet);
        Assert.Equal(IPAddress.Parse("192.168.1.1"), returnedInet.Address);
    }

    [Fact]
    public async Task CanEncodeCidr()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var cidr = new Data.Cidr().SetAddress(IPAddress.Parse("192.168.1.0")).SetNetmask(24);
        var result = await connection.PreparedQueryAsync(
            "SELECT $1::cidr as net",
            Tuple.Create(cidr)
        );

        Assert.Equal(1, result.Count);
        var returnedCidr = result[0].Get<Data.Cidr>("net");
        Assert.NotNull(returnedCidr);
        Assert.Equal(IPAddress.Parse("192.168.1.0"), returnedCidr.Address);
        Assert.Equal(24, returnedCidr.Netmask);
    }

    #endregion

    #region NULL Handling

    [Fact]
    public async Task CanEncodeNullString()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.PreparedQueryAsync(
            "SELECT $1::text as n",
            Tuple.Create((string?)null)
        );

        Assert.Equal(1, result.Count);
        Assert.Null(result[0].GetString("n"));
    }

    [Fact]
    public async Task CanEncodeNullInteger()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.PreparedQueryAsync(
            "SELECT $1::int4 as n",
            Tuple.Create((int?)null)
        );

        Assert.Equal(1, result.Count);
        Assert.Null(result[0].GetValue("n"));
    }

    #endregion

    #region Roundtrip Tests

    [Fact]
    public async Task CanRoundtripMultipleTypes()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.PreparedQueryAsync(
            "SELECT $1::int4 as i, $2::text as t, $3::bool as b, $4::float8 as f",
            Tuple.Create(42, "hello", true, 3.14)
        );

        Assert.Equal(1, result.Count);
        Assert.Equal(42, result[0].GetInteger("i"));
        Assert.Equal("hello", result[0].GetString("t"));
        Assert.True(result[0].GetBoolean("b"));
        Assert.Equal(3.14, result[0].GetDouble("f"), 0.01);
    }

    [Fact]
    public async Task CanRoundtripDateTimeTypes()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var date = new DateOnly(2024, 6, 15);
        var time = new TimeOnly(14, 30, 45);
        var timestamp = new DateTime(2024, 6, 15, 14, 30, 0);

        var result = await connection.PreparedQueryAsync(
            "SELECT $1::date as d, $2::time as t, $3::timestamp as ts",
            Tuple.Create(date, time, timestamp)
        );

        Assert.Equal(1, result.Count);
        Assert.Equal(date, result[0].Get<DateOnly>("d"));
        Assert.Equal(time, result[0].Get<TimeOnly>("t"));
        Assert.Equal(timestamp, result[0].Get<DateTime>("ts"));
    }

    #endregion
}
