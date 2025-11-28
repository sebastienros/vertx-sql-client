// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using System.Net;
using Xunit;

namespace Vertx.PgClient.Tests;

[Collection("PostgreSQL")]
public class DataTypeTests
{
    private readonly PostgresFixture _fixture;

    public DataTypeTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    #region Boolean

    [Fact]
    public async Task CanSelectBoolTrue()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT true::bool as b");

        Assert.Equal(1, result.Count);
        Assert.True(result[0].GetValue("b").GetBoolean());
    }

    [Fact]
    public async Task CanSelectBoolFalse()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT false::bool as b");

        Assert.Equal(1, result.Count);
        Assert.False(result[0].GetValue("b").GetBoolean());
    }

    #endregion

    #region Integer Types

    [Fact]
    public async Task CanSelectInt2()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT 12345::int2 as n");

        Assert.Equal(1, result.Count);
        Assert.Equal((short)12345, result[0].GetValue("n").Get<short>());
    }

    [Fact]
    public async Task CanSelectInt4()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT 123456789::int4 as n");

        Assert.Equal(1, result.Count);
        Assert.Equal(123456789, result[0].GetValue("n").GetInteger());
    }

    [Fact]
    public async Task CanSelectInt8()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT 9223372036854775807::int8 as n");

        Assert.Equal(1, result.Count);
        Assert.Equal(9223372036854775807L, result[0].GetValue("n").GetLong());
    }

    #endregion

    #region Floating Point Types

    [Fact]
    public async Task CanSelectFloat4()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT 3.14::float4 as n");

        Assert.Equal(1, result.Count);
        Assert.Equal(3.14f, result[0].GetValue("n").Get<float>(), 0.01f);
    }

    [Fact]
    public async Task CanSelectFloat8()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT 3.141592653589793::float8 as n");

        Assert.Equal(1, result.Count);
        Assert.Equal(3.141592653589793, result[0].GetValue("n").GetDouble(), 0.0000000001);
    }

    [Fact]
    public async Task CanSelectNumeric()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT 123.456::numeric as n");

        Assert.Equal(1, result.Count);
        Assert.Equal(123.456m, result[0].GetValue("n").Get<decimal>());
    }

    #endregion

    #region String Types

    [Fact]
    public async Task CanSelectText()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT 'Hello, World!'::text as t");

        Assert.Equal(1, result.Count);
        Assert.Equal("Hello, World!", result[0].GetValue("t").GetString());
    }

    [Fact]
    public async Task CanSelectVarchar()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT 'PostgreSQL'::varchar(50) as v");

        Assert.Equal(1, result.Count);
        Assert.Equal("PostgreSQL", result[0].GetValue("v").GetString());
    }

    [Fact]
    public async Task CanSelectChar()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT 'A'::char as c");

        Assert.Equal(1, result.Count);
        Assert.Equal("A", result[0].GetValue("c").GetString());
    }

    #endregion

    #region Date/Time Types

    [Fact]
    public async Task CanSelectDate()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '2024-06-15'::date as d");

        Assert.Equal(1, result.Count);
        var date = result[0].GetValue("d").Get<DateOnly>();
        Assert.Equal(new DateOnly(2024, 6, 15), date);
    }

    [Fact]
    public async Task CanSelectTime()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '14:30:45'::time as t");

        Assert.Equal(1, result.Count);
        var time = result[0].GetValue("t").Get<TimeOnly>();
        Assert.Equal(new TimeOnly(14, 30, 45), time);
    }

    [Fact]
    public async Task CanSelectTimestamp()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '2024-06-15 14:30:00'::timestamp as ts");

        Assert.Equal(1, result.Count);
        var ts = result[0].GetValue("ts").Get<DateTime>();
        Assert.Equal(new DateTime(2024, 6, 15, 14, 30, 0), ts);
    }

    [Fact]
    public async Task CanSelectTimestamptz()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '2024-06-15 14:30:00+00'::timestamptz as ts");

        Assert.Equal(1, result.Count);
        var ts = result[0].GetValue("ts").Get<DateTimeOffset>();
        Assert.Equal(2024, ts.Year);
        Assert.Equal(6, ts.Month);
        Assert.Equal(15, ts.Day);
    }

    [Fact]
    public async Task CanSelectInterval()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '1 year 2 months 3 days 4 hours 5 minutes 6 seconds'::interval as i");

        Assert.Equal(1, result.Count);
        var interval = result[0].GetValue("i").Get<Data.Interval>();
        Assert.NotNull(interval);
        Assert.Equal(1, interval.Years);
        Assert.Equal(2, interval.Months);
        Assert.Equal(3, interval.Days);
        Assert.Equal(4, interval.Hours);
        Assert.Equal(5, interval.Minutes);
        Assert.Equal(6, interval.Seconds);
    }

    #endregion

    #region Binary Types

    [Fact]
    public async Task CanSelectBytea()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '\\x48656c6c6f'::bytea as data");

        Assert.Equal(1, result.Count);
        var bytes = result[0].GetValue("data").Get<byte[]>();
        Assert.NotNull(bytes);
        Assert.Equal("Hello", System.Text.Encoding.ASCII.GetString(bytes));
    }

    #endregion

    #region UUID

    [Fact]
    public async Task CanSelectUuid()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var uuid = Guid.NewGuid();
        var result = await connection.QueryAsync($"SELECT '{uuid}'::uuid as id");

        Assert.Equal(1, result.Count);
        Assert.Equal(uuid, result[0].GetValue("id").Get<Guid>());
    }

    #endregion

    #region JSON Types

    [Fact]
    public async Task CanSelectJson()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '{\"name\": \"test\", \"value\": 42}'::json as j");

        Assert.Equal(1, result.Count);
        var json = result[0].GetValue("j").GetString();
        Assert.Contains("\"name\"", json);
        Assert.Contains("\"test\"", json);
    }

    [Fact]
    public async Task CanSelectJsonb()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '{\"key\": \"value\"}'::jsonb as jb");

        Assert.Equal(1, result.Count);
        var jsonb = result[0].GetValue("jb").GetString();
        Assert.Contains("key", jsonb);
        Assert.Contains("value", jsonb);
    }

    #endregion

    #region Geometric Types

    [Fact]
    public async Task CanSelectPoint()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '(1.5, 2.5)'::point as p");

        Assert.Equal(1, result.Count);
        var point = result[0].GetValue("p").Get<Data.Point>();
        Assert.Equal(1.5, point.X, 0.01);
        Assert.Equal(2.5, point.Y, 0.01);
    }

    [Fact]
    public async Task CanSelectLine()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '{1,2,3}'::line as l");

        Assert.Equal(1, result.Count);
        var line = result[0].GetValue("l").Get<Data.Line>();
        Assert.NotNull(line);
        Assert.Equal(1, line.A, 0.01);
        Assert.Equal(2, line.B, 0.01);
        Assert.Equal(3, line.C, 0.01);
    }

    [Fact]
    public async Task CanSelectLineSegment()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '[(0,0),(1,1)]'::lseg as ls");

        Assert.Equal(1, result.Count);
        var lseg = result[0].GetValue("ls").Get<Data.LineSegment>();
        Assert.NotNull(lseg);
        Assert.Equal(0, lseg.P1.X, 0.01);
        Assert.Equal(0, lseg.P1.Y, 0.01);
        Assert.Equal(1, lseg.P2.X, 0.01);
        Assert.Equal(1, lseg.P2.Y, 0.01);
    }

    [Fact]
    public async Task CanSelectBox()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '((0,0),(1,1))'::box as b");

        Assert.Equal(1, result.Count);
        var box = result[0].GetValue("b").Get<Data.Box>();
        Assert.NotNull(box);
    }

    [Fact]
    public async Task CanSelectCircle()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '<(1,2),3>'::circle as c");

        Assert.Equal(1, result.Count);
        var circle = result[0].GetValue("c").Get<Data.Circle>();
        Assert.NotNull(circle);
        Assert.Equal(1, circle.CenterPoint.X, 0.01);
        Assert.Equal(2, circle.CenterPoint.Y, 0.01);
        Assert.Equal(3, circle.Radius, 0.01);
    }

    [Fact]
    public async Task CanSelectPolygon()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '((0,0),(1,0),(1,1),(0,1))'::polygon as p");

        Assert.Equal(1, result.Count);
        var polygon = result[0].GetValue("p").Get<Data.Polygon>();
        Assert.NotNull(polygon);
        Assert.Equal(4, polygon.Points.Count);
    }

    [Fact]
    public async Task CanSelectPath()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '[(0,0),(1,1),(2,0)]'::path as p");

        Assert.Equal(1, result.Count);
        var path = result[0].GetValue("p").Get<Data.Path>();
        Assert.NotNull(path);
        Assert.Equal(3, path.Points.Count);
        Assert.True(path.IsOpen);
    }

    #endregion

    #region Network Types

    [Fact]
    public async Task CanSelectInet()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '192.168.1.1'::inet as ip");

        Assert.Equal(1, result.Count);
        var inet = result[0].GetValue("ip").Get<Data.Inet>();
        Assert.NotNull(inet);
        Assert.NotNull(inet.Address);
        Assert.Equal(IPAddress.Parse("192.168.1.1"), inet.Address);
    }

    [Fact]
    public async Task CanSelectCidr()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT '192.168.1.0/24'::cidr as net");

        Assert.Equal(1, result.Count);
        var cidr = result[0].GetValue("net").Get<Data.Cidr>();
        Assert.NotNull(cidr);
        Assert.NotNull(cidr.Address);
        Assert.Equal(IPAddress.Parse("192.168.1.0"), cidr.Address);
        Assert.Equal(24, cidr.Netmask);
    }

    #endregion

    #region Array Types

    [Fact]
    public async Task CanSelectBoolArray()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT ARRAY[true, false, true]::bool[] as arr");

        Assert.Equal(1, result.Count);
        var arr = result[0].GetValue("arr").Get<bool[]>();
        Assert.NotNull(arr);
        Assert.Equal(new[] { true, false, true }, arr);
    }

    [Fact]
    public async Task CanSelectIntegerArray()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT ARRAY[1, 2, 3, 4, 5]::int4[] as arr");

        Assert.Equal(1, result.Count);
        var arr = result[0].GetValue("arr").Get<int[]>();
        Assert.NotNull(arr);
        Assert.Equal(5, arr.Length);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, arr);
    }

    [Fact]
    public async Task CanSelectTextArray()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT ARRAY['a', 'b', 'c']::text[] as arr");

        Assert.Equal(1, result.Count);
        var arr = result[0].GetValue("arr").Get<string[]>();
        Assert.NotNull(arr);
        Assert.Equal(3, arr.Length);
        Assert.Equal(new[] { "a", "b", "c" }, arr);
    }

    [Fact]
    public async Task CanSelectFloat8Array()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT ARRAY[1.1, 2.2, 3.3]::float8[] as arr");

        Assert.Equal(1, result.Count);
        var arr = result[0].GetValue("arr").Get<double[]>();
        Assert.NotNull(arr);
        Assert.Equal(3, arr.Length);
    }

    #endregion

    #region NULL Handling

    [Fact]
    public async Task CanSelectNull()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT NULL::text as n");

        Assert.Equal(1, result.Count);
        Assert.Null(result[0].GetValue("n").GetString());
    }

    [Fact]
    public async Task CanSelectNullInteger()
    {
        var options = _fixture.CreateConnectOptions();
        await using var connection = await PgConnection.ConnectAsync(options);

        var result = await connection.QueryAsync("SELECT NULL::int4 as n");

        Assert.Equal(1, result.Count);
        Assert.True(result[0].GetValue("n").IsNull);
    }

    #endregion
}
