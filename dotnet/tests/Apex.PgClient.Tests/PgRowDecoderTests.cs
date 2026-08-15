/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Buffers.Binary;
using System.Text;
using Apex.PgClient.Internal;
using Apex.SqlClient;

namespace Apex.PgClient.Tests;

[TestClass]
public sealed class PgRowDecoderTests
{
  [TestMethod]
  public void EveryTypedGetterValidatesPostgreSqlType()
  {
    PgRowDecoder decoder = new(16, 64);
    byte[] row = CreateRow([0]);
    SqlColumn mismatch = Column(
      TypeId: 99999,
      SqlDataFormat.Binary);

    AssertInvalid(() => decoder.DecodeBoolean(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodeInt16(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodeInt32(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodeInt64(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodeFloat(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodeDouble(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodeDecimal(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodeString(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodeBytes(row, 0, mismatch));
    AssertInvalid(() =>
      decoder.DecodeReadOnlyMemory(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodeGuid(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodeDateOnly(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodeTimeOnly(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodeDateTime(row, 0, mismatch));
    AssertInvalid(() =>
      decoder.DecodeDateTimeOffset(row, 0, mismatch));
    AssertInvalid(() =>
      decoder.DecodeJsonElement(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodeArray(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodePgNumeric(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodePgMoney(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodePgInterval(row, 0, mismatch));
    AssertInvalid(() =>
      decoder.DecodePgTimeWithTimeZone(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodePgPoint(row, 0, mismatch));
    AssertInvalid(() =>
      decoder.DecodePgLineSegment(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodePgPath(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodePgBox(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodePgPolygon(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodePgLine(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodePgCidr(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodePgCircle(row, 0, mismatch));
    AssertInvalid(() => decoder.DecodePgInet(row, 0, mismatch));
  }

  [TestMethod]
  public void DecodesBinaryAndTextTypedValues()
  {
    PgRowDecoder decoder = new(16, 64);
    Guid expected =
      Guid.Parse("12345678-1234-5678-9012-123456789abc");

    Assert.AreEqual(
      42,
      decoder.DecodeInt32(
        CreateRow(Int32(42)),
        0,
        Column(23, SqlDataFormat.Binary)));
    Assert.AreEqual(
      42,
      decoder.DecodeInt32(
        CreateRow("42"u8),
        0,
        Column(23, SqlDataFormat.Text)));
    Assert.AreEqual(
      expected,
      decoder.DecodeGuid(
        CreateRow(expected.ToByteArray(bigEndian: true)),
        0,
        Column(2950, SqlDataFormat.Binary)));
    Assert.AreEqual(
      expected,
      decoder.DecodeGuid(
        CreateRow(Encoding.UTF8.GetBytes(expected.ToString("D"))),
        0,
        Column(2950, SqlDataFormat.Text)));
    Assert.AreEqual(
      12.34m,
      decoder.DecodeDecimal(
        CreateRow("12.34"u8),
        0,
        Column(1700, SqlDataFormat.Text)));
  }

  [TestMethod]
  public void GenericDispatchUsesExactTypedPaths()
  {
    PgRowDecoder decoder = new(16, 64);
    Guid expected =
      Guid.Parse("12345678-1234-5678-9012-123456789abc");
    SqlColumn intBinary = Column(23, SqlDataFormat.Binary);
    SqlColumn intText = Column(23, SqlDataFormat.Text);
    SqlColumn guidBinary = Column(2950, SqlDataFormat.Binary);
    SqlColumn guidText = Column(2950, SqlDataFormat.Text);
    SqlColumn pointBinary = Column(600, SqlDataFormat.Binary);

    Assert.AreEqual(
      42,
      decoder.Decode<int>(
        CreateRow(Int32(42)),
        0,
        intBinary,
        copyReadOnlyMemory: false));
    Assert.AreEqual(
      42,
      decoder.Decode<int>(
        CreateRow("42"u8),
        0,
        intText,
        copyReadOnlyMemory: false));
    Assert.AreEqual(
      expected,
      decoder.Decode<Guid>(
        CreateRow(expected.ToByteArray(bigEndian: true)),
        0,
        guidBinary,
        copyReadOnlyMemory: false));
    Assert.AreEqual(
      expected,
      decoder.Decode<Guid>(
        CreateRow(Encoding.UTF8.GetBytes(expected.ToString("D"))),
        0,
        guidText,
        copyReadOnlyMemory: false));
    Assert.AreEqual(
      new PgPoint(1.5, -2.25),
      decoder.Decode<PgPoint>(
        CreateRow(Point(1.5, -2.25)),
        0,
        pointBinary,
        copyReadOnlyMemory: false));
    Assert.AreEqual(
      42,
      decoder.Decode<object>(
        CreateRow(Int32(42)),
        0,
        intBinary,
        copyReadOnlyMemory: false));
    Assert.IsNull(
      decoder.Decode<int?>(
        CreateNullRow(),
        0,
        intBinary,
        copyReadOnlyMemory: false));
    AssertInvalid(() =>
      decoder.Decode<int>(
        CreateRow(Point(1.5, -2.25)),
        0,
        pointBinary,
        copyReadOnlyMemory: false));
    AssertInvalid(() =>
      decoder.Decode<TimeSpan>(
        CreateRow([0]),
        0,
        Column(1186, SqlDataFormat.Binary),
        copyReadOnlyMemory: false));
    AssertInvalid(() =>
      decoder.Decode<int>(
        CreateRow(Int32(42)),
        0,
        Column(23, (SqlDataFormat)2),
        copyReadOnlyMemory: false));
  }

  [TestMethod]
  public void GenericProviderValuesAreTypedAndNullable()
  {
    PgRowDecoder decoder = new(16, 64);
    SqlColumn column = Column(600, SqlDataFormat.Binary);
    byte[] row = CreateRow(Point(1.5, -2.25));

    Assert.AreEqual(
      new PgPoint(1.5, -2.25),
      decoder.Decode<PgPoint>(
        row,
        0,
        column,
        copyReadOnlyMemory: false));
    Assert.AreEqual(
      new PgPoint(1.5, -2.25),
      decoder.Decode<PgPoint?>(
        row,
        0,
        column,
        copyReadOnlyMemory: false));
    Assert.IsNull(
      decoder.Decode<PgPoint?>(
        CreateNullRow(),
        0,
        column,
        copyReadOnlyMemory: false));
  }

  [TestMethod]
  public void NullReferenceValuesRemainNullButStillValidateType()
  {
    PgRowDecoder decoder = new(16, 64);
    byte[] row = CreateNullRow();

    Assert.IsNull(
      decoder.DecodeString(
        row,
        0,
        Column(25, SqlDataFormat.Text)));
    AssertInvalid(() =>
      decoder.DecodeString(
        row,
        0,
        Column(23, SqlDataFormat.Text)));
  }

  [TestMethod]
  public void GenericReadOnlyMemoryCanBorrowOrCopy()
  {
    PgRowDecoder decoder = new(16, 64);
    byte[] row = CreateRow([1, 2, 3]);
    SqlColumn column = Column(17, SqlDataFormat.Binary);
    ReadOnlyMemory<byte> borrowed =
      decoder.Decode<ReadOnlyMemory<byte>>(
        row,
        0,
        column,
        copyReadOnlyMemory: false);
    ReadOnlyMemory<byte> copied =
      decoder.Decode<ReadOnlyMemory<byte>>(
        row,
        0,
        column,
        copyReadOnlyMemory: true);
    ReadOnlyMemory<byte> direct = decoder.DecodeReadOnlyMemory(
      row,
      0,
      column);

    row[sizeof(short) + sizeof(int)] = 9;

    CollectionAssert.AreEqual(
      new byte[] { 9, 2, 3 },
      borrowed.ToArray());
    CollectionAssert.AreEqual(
      new byte[] { 9, 2, 3 },
      direct.ToArray());
    CollectionAssert.AreEqual(
      new byte[] { 1, 2, 3 },
      copied.ToArray());
  }

  [TestMethod]
  public void ObjectDecodeUsesScalarCache()
  {
    PgRowDecoder decoder = new(16, 64);
    byte[] row = CreateRow(Int32(42));
    SqlColumn column = Column(23, SqlDataFormat.Binary);

    object? first = decoder.DecodeObject(row, 0, column);
    object? second = decoder.DecodeObject(row, 0, column);

    Assert.AreSame(first, second);
    Assert.AreEqual(42, first);
  }

  [TestMethod]
  public void GenericTypedDecodingDoesNotAllocateAfterWarmup()
  {
    PgRowDecoder decoder = new(16, 64);
    byte[] intRow = CreateRow(Int32(42));
    byte[] decimalRow = CreateRow("12.34"u8);
    byte[] pointRow = CreateRow(Point(1.5, -2.25));
    Guid expected =
      Guid.Parse("12345678-1234-5678-9012-123456789abc");
    byte[] guidRow =
      CreateRow(expected.ToByteArray(bigEndian: true));
    SqlColumn intColumn = Column(23, SqlDataFormat.Binary);
    SqlColumn decimalColumn = Column(1700, SqlDataFormat.Text);
    SqlColumn pointColumn = Column(600, SqlDataFormat.Binary);
    SqlColumn guidColumn = Column(2950, SqlDataFormat.Binary);
    for (int i = 0; i < 1000; i++)
    {
      _ = decoder.Decode<int>(
        intRow,
        0,
        intColumn,
        copyReadOnlyMemory: false);
      _ = decoder.Decode<decimal>(
        decimalRow,
        0,
        decimalColumn,
        copyReadOnlyMemory: false);
      _ = decoder.Decode<Guid>(
        guidRow,
        0,
        guidColumn,
        copyReadOnlyMemory: false);
      _ = decoder.Decode<PgPoint>(
        pointRow,
        0,
        pointColumn,
        copyReadOnlyMemory: false);
    }

    long before = GC.GetAllocatedBytesForCurrentThread();
    int intSum = 0;
    decimal decimalSum = 0;
    double pointSum = 0;
    Guid lastGuid = default;
    for (int i = 0; i < 10_000; i++)
    {
      intSum += decoder.Decode<int>(
        intRow,
        0,
        intColumn,
        copyReadOnlyMemory: false);
      decimalSum += decoder.Decode<decimal>(
        decimalRow,
        0,
        decimalColumn,
        copyReadOnlyMemory: false);
      lastGuid = decoder.Decode<Guid>(
        guidRow,
        0,
        guidColumn,
        copyReadOnlyMemory: false);
      pointSum += decoder.Decode<PgPoint>(
        pointRow,
        0,
        pointColumn,
        copyReadOnlyMemory: false).X;
    }
    long allocated =
      GC.GetAllocatedBytesForCurrentThread() - before;

    Assert.AreEqual(420_000, intSum);
    Assert.AreEqual(123_400m, decimalSum);
    Assert.AreEqual(15_000d, pointSum);
    Assert.AreEqual(expected, lastGuid);
    Assert.AreEqual(0, allocated);
  }

  [TestMethod]
  public void UnknownBinaryTypeIsNotDecodedAsUtf8()
  {
    PgRowDecoder decoder = new(16, 64);
    byte[] row = CreateRow([0xff]);
    SqlColumn column = Column(99999, SqlDataFormat.Binary);

    Assert.ThrowsExactly<PgUnsupportedTypeException>(
      () => decoder.DecodeObject(row, 0, column));
  }

  private static void AssertInvalid(Action action) =>
    Assert.ThrowsExactly<InvalidCastException>(action);

  private static SqlColumn Column(
    uint TypeId,
    SqlDataFormat format) =>
    new(
      "value",
      TypeId,
      TypeSize: -1,
      TypeModifier: -1,
      format);

  private static byte[] CreateNullRow()
  {
    byte[] row = new byte[sizeof(short) + sizeof(int)];
    BinaryPrimitives.WriteInt16BigEndian(row, 1);
    BinaryPrimitives.WriteInt32BigEndian(
      row.AsSpan(sizeof(short)),
      -1);
    return row;
  }

  private static byte[] CreateRow(ReadOnlySpan<byte> value)
  {
    byte[] row =
      new byte[sizeof(short) + sizeof(int) + value.Length];
    BinaryPrimitives.WriteInt16BigEndian(row, 1);
    BinaryPrimitives.WriteInt32BigEndian(
      row.AsSpan(sizeof(short)),
      value.Length);
    value.CopyTo(
      row.AsSpan(sizeof(short) + sizeof(int)));
    return row;
  }

  private static byte[] Int32(int value)
  {
    byte[] bytes = new byte[sizeof(int)];
    BinaryPrimitives.WriteInt32BigEndian(bytes, value);
    return bytes;
  }

  private static byte[] Point(double x, double y)
  {
    byte[] bytes = new byte[sizeof(double) * 2];
    BinaryPrimitives.WriteInt64BigEndian(
      bytes,
      BitConverter.DoubleToInt64Bits(x));
    BinaryPrimitives.WriteInt64BigEndian(
      bytes.AsSpan(sizeof(double)),
      BitConverter.DoubleToInt64Bits(y));
    return bytes;
  }
}
