/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Net;
using System.Text;
using System.Text.Json;
using Apex.PgClient.Internal;

namespace Apex.PgClient.Tests;

[TestClass]
public sealed class PgTextCodecTests
{
    [TestMethod]
    public void DecodesNumericWithoutDecimalRangeLoss()
    {
        PgNumeric numeric = (PgNumeric)Decode(1700, "123456789012345678901234567890.1234");

        Assert.AreEqual("123456789012345678901234567890.1234", numeric.ToString());
        Assert.AreEqual("1200", PgNumeric.Parse("1.2e3").ToString());
        Assert.AreEqual(PgNumericSpecialValue.NaN, PgNumeric.Parse("NaN").SpecialValue);
    }

    [TestMethod]
    public void DecodesByteaJsonAndInfinity()
    {
        CollectionAssert.AreEqual(new byte[] { 0, 1, 254, 255 }, (byte[])Decode(17, "\\x0001feff"));
        JsonElement json = (JsonElement)Decode(3802, """{"ok":true}""");

        Assert.IsTrue(json.GetProperty("ok").GetBoolean());
        Assert.AreEqual(DateOnly.MaxValue, Decode(1082, "infinity"));
        Assert.AreEqual(DateTimeOffset.MinValue, Decode(1184, "-infinity"));
    }

    [TestMethod]
    public void DecodesIntervalGeometryAndNetworkTypes()
    {
        PgInterval interval = (PgInterval)Decode(1186, "P1Y2M3DT4H5M6.123456S");
        PgPoint point = (PgPoint)Decode(600, "(1.5,-2.25)");
        PgCidr cidr = (PgCidr)Decode(650, "2001:db8::/64");

        Assert.AreEqual(
          new PgInterval(1, 2, 3, 4, 5, 6, 123456),
          interval);
        Assert.AreEqual(new PgPoint(1.5, -2.25), point);
        Assert.AreEqual(IPAddress.Parse("2001:db8::"), cidr.Address);
        Assert.AreEqual(64, cidr.PrefixLength);
    }

    [TestMethod]
    public void DecodesQuotedAndNullArrayElements()
    {
        var payload = Encoding.UTF8.GetBytes(
            """{"one,two",NULL,"quoted\"value"}""");
        var values = PgTextCodec.DecodeArray<string?>(1009, payload);
        var objectValue = PgTextCodec.Decode(1009, payload);

        CollectionAssert.AreEqual(
            new string?[] { "one,two", null, "quoted\"value" },
            values);
        Assert.IsInstanceOfType<string?[]>(objectValue);

        var nullableInts = PgTextCodec.DecodeArray<int?>(
            1007,
            Encoding.UTF8.GetBytes("{1,NULL,3}"));
        CollectionAssert.AreEqual(new int?[] { 1, null, 3 }, nullableInts);
        Assert.ThrowsExactly<InvalidCastException>(() =>
            PgTextCodec.DecodeArray<int>(1007, Encoding.UTF8.GetBytes("{1,NULL,3}")));
    }

    [TestMethod]
    public void RejectsMultidimensionalArrays()
    {
        Assert.ThrowsExactly<NotSupportedException>(() => Decode(1007, "{{1,2},{3,4}}"));
    }

    [TestMethod]
    public void RejectsDocumentedUnsupportedTypes()
    {
        var exception =
          Assert.ThrowsExactly<PgUnsupportedTypeException>(() => Decode(26, "42"));

        Assert.AreEqual(26U, exception.TypeId);
    }

    private static object Decode(uint typeId, string value) =>
      PgTextCodec.Decode(typeId, Encoding.UTF8.GetBytes(value));
}
