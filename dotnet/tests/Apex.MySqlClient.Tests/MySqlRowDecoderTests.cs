/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Apex.MySqlClient.Internal;
using Apex.SqlClient;

namespace Apex.MySqlClient.Tests;

[TestClass]
public sealed class MySqlRowDecoderTests
{
  [TestMethod]
  public void TextRowDecodesSignedIntegerBoundaries()
  {
    MySqlColumnMetadata[] columns =
    [
      Column("tiny", MySqlType.Tiny),
      Column("short", MySqlType.Short),
      Column("long", MySqlType.Long),
      Column("longlong", MySqlType.LongLong),
    ];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: false);
    byte[] row = BuildTextRow(
      sbyte.MinValue.ToString(),
      short.MinValue.ToString(),
      int.MinValue.ToString(),
      long.MinValue.ToString());

    Assert.AreEqual(sbyte.MinValue, decoder.Decode<sbyte>(row, 0));
    Assert.AreEqual(short.MinValue, decoder.Decode<short>(row, 1));
    Assert.AreEqual(int.MinValue, decoder.Decode<int>(row, 2));
    Assert.AreEqual(long.MinValue, decoder.Decode<long>(row, 3));
  }

  [TestMethod]
  public void TextRowDecodesUnsignedIntegerBoundaries()
  {
    MySqlColumnMetadata[] columns =
    [
      Column("tiny", MySqlType.Tiny, unsigned: true),
      Column("short", MySqlType.Short, unsigned: true),
      Column("long", MySqlType.Long, unsigned: true),
      Column("longlong", MySqlType.LongLong, unsigned: true),
    ];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: false);
    byte[] row = BuildTextRow(
      byte.MaxValue.ToString(),
      ushort.MaxValue.ToString(),
      uint.MaxValue.ToString(),
      ulong.MaxValue.ToString());

    Assert.AreEqual(byte.MaxValue, decoder.Decode<byte>(row, 0));
    Assert.AreEqual(ushort.MaxValue, decoder.Decode<ushort>(row, 1));
    Assert.AreEqual(uint.MaxValue, decoder.Decode<uint>(row, 2));
    Assert.AreEqual(ulong.MaxValue, decoder.Decode<ulong>(row, 3));
  }

  [TestMethod]
  public void BinaryRowDecodesSignedIntegerBoundaries()
  {
    MySqlColumnMetadata[] columns =
    [
      Column("tiny", MySqlType.Tiny),
      Column("short", MySqlType.Short),
      Column("long", MySqlType.Long),
      Column("longlong", MySqlType.LongLong),
    ];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: true);
    BinaryRowBuilder builder = new(columns.Length);
    builder.WriteInt8(0, sbyte.MinValue);
    builder.WriteInt16(1, short.MinValue);
    builder.WriteInt32(2, int.MinValue);
    builder.WriteInt64(3, long.MinValue);
    byte[] row = builder.Build();

    Assert.AreEqual(sbyte.MinValue, decoder.Decode<sbyte>(row, 0));
    Assert.AreEqual(short.MinValue, decoder.Decode<short>(row, 1));
    Assert.AreEqual(int.MinValue, decoder.Decode<int>(row, 2));
    Assert.AreEqual(long.MinValue, decoder.Decode<long>(row, 3));
  }

  [TestMethod]
  public void BinaryRowDecodesUnsignedIntegerBoundaries()
  {
    MySqlColumnMetadata[] columns =
    [
      Column("tiny", MySqlType.Tiny, unsigned: true),
      Column("short", MySqlType.Short, unsigned: true),
      Column("long", MySqlType.Long, unsigned: true),
      Column("longlong", MySqlType.LongLong, unsigned: true),
    ];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: true);
    BinaryRowBuilder builder = new(columns.Length);
    builder.WriteInt8(0, unchecked((sbyte)byte.MaxValue));
    builder.WriteInt16(1, unchecked((short)ushort.MaxValue));
    builder.WriteInt32(2, unchecked((int)uint.MaxValue));
    builder.WriteInt64(3, unchecked((long)ulong.MaxValue));
    byte[] row = builder.Build();

    Assert.AreEqual(byte.MaxValue, decoder.Decode<byte>(row, 0));
    Assert.AreEqual(ushort.MaxValue, decoder.Decode<ushort>(row, 1));
    Assert.AreEqual(uint.MaxValue, decoder.Decode<uint>(row, 2));
    Assert.AreEqual(ulong.MaxValue, decoder.Decode<ulong>(row, 3));
  }

  [TestMethod]
  public void BinaryRowDecodesFloatAndDouble()
  {
    MySqlColumnMetadata[] columns = [Column("f", MySqlType.Float), Column("d", MySqlType.Double)];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: true);
    BinaryRowBuilder builder = new(columns.Length);
    builder.WriteFloat(0, 1.5f);
    builder.WriteDouble(1, 2.25d);
    byte[] row = builder.Build();

    Assert.AreEqual(1.5f, decoder.Decode<float>(row, 0));
    Assert.AreEqual(2.25d, decoder.Decode<double>(row, 1));
  }

  [TestMethod]
  public void TextRowDecodesFloatDoubleAndDecimal()
  {
    MySqlColumnMetadata[] columns =
    [
      Column("f", MySqlType.Float),
      Column("d", MySqlType.Double),
      Column("m", MySqlType.NewDecimal),
    ];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: false);
    byte[] row = BuildTextRow("1.5", "2.25", "12345.6789");

    Assert.AreEqual(1.5f, decoder.Decode<float>(row, 0));
    Assert.AreEqual(2.25d, decoder.Decode<double>(row, 1));
    Assert.AreEqual(12345.6789m, decoder.Decode<decimal>(row, 2));
  }

  [TestMethod]
  public void TextRowPreservesArbitraryPrecisionDecimal()
  {
    const string text =
      "12345678901234567890123456789012345.123456789012345678901234567890";
    MySqlColumnMetadata[] columns = [Column("value", MySqlType.NewDecimal)];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: false);
    byte[] row = BuildTextRow(text);

    MySqlDecimal value = decoder.Decode<MySqlDecimal>(row, 0);

    Assert.AreEqual(text, value.ToString());
    Assert.AreEqual(value, decoder.DecodeObject(row, 0));
    Assert.ThrowsExactly<FormatException>(() => decoder.Decode<decimal>(row, 0));
  }

  [TestMethod]
  public void DecodesBitColumnAsUnsignedIntegerInBothProtocols()
  {
    MySqlColumnMetadata[] columns = [Column("bits", MySqlType.Bit)];
    MySqlRowDecoder textDecoder = CreateDecoder(columns, binary: false);
    byte[] textRow = BuildTextRowRaw([[0x01, 0x02]]);
    Assert.AreEqual(0x0102ul, textDecoder.Decode<ulong>(textRow, 0));

    MySqlRowDecoder binaryDecoder = CreateDecoder(columns, binary: true);
    BinaryRowBuilder builder = new(columns.Length);
    builder.WriteLengthEncodedBytes(0, [0x01, 0x02]);
    byte[] binaryRow = builder.Build();
    Assert.AreEqual(0x0102ul, binaryDecoder.Decode<ulong>(binaryRow, 0));
  }

  [TestMethod]
  public void DecodesTextDateAndDateTime()
  {
    MySqlColumnMetadata[] columns =
    [
      Column("d", MySqlType.Date),
      Column("dt", MySqlType.DateTime),
      Column("t", MySqlType.Time),
    ];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: false);
    byte[] row = BuildTextRow("2024-03-15", "2024-03-15 13:45:30", "13:45:30");

    Assert.AreEqual(new DateOnly(2024, 3, 15), decoder.Decode<DateOnly>(row, 0));
    Assert.AreEqual(new DateTime(2024, 3, 15, 13, 45, 30), decoder.Decode<DateTime>(row, 1));
    Assert.AreEqual(new TimeSpan(13, 45, 30), decoder.Decode<TimeSpan>(row, 2));
    Assert.AreEqual(new TimeOnly(13, 45, 30), decoder.Decode<TimeOnly>(row, 2));
  }

  [TestMethod]
  public void DecodesBinaryDateAndDateTime()
  {
    MySqlColumnMetadata[] columns = [Column("d", MySqlType.Date), Column("dt", MySqlType.DateTime)];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: true);
    BinaryRowBuilder builder = new(columns.Length);
    builder.WriteDate(0, 2024, 3, 15);
    builder.WriteDateTime(1, 2024, 3, 15, 13, 45, 30, 0);
    byte[] row = builder.Build();

    Assert.AreEqual(new DateOnly(2024, 3, 15), decoder.Decode<DateOnly>(row, 0));
    Assert.AreEqual(new DateTime(2024, 3, 15, 13, 45, 30), decoder.Decode<DateTime>(row, 1));
  }

  [TestMethod]
  public void ZeroDateBehaviorErrorThrowsFormatException()
  {
    MySqlColumnMetadata[] columns = [Column("d", MySqlType.Date)];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: false, MySqlZeroDateBehavior.Error);
    byte[] row = BuildTextRow("0000-00-00");

    Assert.ThrowsExactly<FormatException>(() => decoder.Decode<DateOnly>(row, 0));
    Assert.ThrowsExactly<FormatException>(() => decoder.DecodeObject(row, 0));
  }

  [TestMethod]
  public void ZeroDateBehaviorNullReturnsNull()
  {
    MySqlColumnMetadata[] columns = [Column("d", MySqlType.Date)];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: false, MySqlZeroDateBehavior.Null);
    byte[] row = BuildTextRow("0000-00-00");

    Assert.IsNull(decoder.DecodeObject(row, 0));
    Assert.ThrowsExactly<InvalidCastException>(() => decoder.Decode<DateOnly>(row, 0));
  }

  [TestMethod]
  public void ZeroDateBehaviorMinValueReturnsMinValue()
  {
    MySqlColumnMetadata[] columns = [Column("d", MySqlType.Date), Column("dt", MySqlType.DateTime)];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: false, MySqlZeroDateBehavior.MinValue);
    byte[] row = BuildTextRow("0000-00-00", "0000-00-00 00:00:00");

    Assert.AreEqual(DateOnly.MinValue, decoder.Decode<DateOnly>(row, 0));
    Assert.AreEqual(DateOnly.MinValue, (DateOnly)decoder.DecodeObject(row, 0)!);
    Assert.AreEqual(DateTime.MinValue, decoder.Decode<DateTime>(row, 1));
  }

  [TestMethod]
  public void DecodesNullColumnsInBothProtocols()
  {
    MySqlColumnMetadata[] columns = [Column("value", MySqlType.VarString)];

    MySqlRowDecoder textDecoder = CreateDecoder(columns, binary: false);
    byte[] textRow = BuildTextRowRaw([null]);
    Assert.IsTrue(textDecoder.IsNull(textRow, 0));
    Assert.IsNull(textDecoder.DecodeObject(textRow, 0));
    Assert.IsNull(textDecoder.Decode<string>(textRow, 0));
    Assert.ThrowsExactly<InvalidCastException>(() => textDecoder.Decode<int>(textRow, 0));

    MySqlRowDecoder binaryDecoder = CreateDecoder(columns, binary: true);
    BinaryRowBuilder builder = new(columns.Length);
    builder.SetNull(0);
    byte[] binaryRow = builder.Build();
    Assert.IsTrue(binaryDecoder.IsNull(binaryRow, 0));
    Assert.IsNull(binaryDecoder.DecodeObject(binaryRow, 0));
  }

  [TestMethod]
  public void DecodeThrowsForOutOfRangeOrdinal()
  {
    MySqlColumnMetadata[] columns = [Column("value", MySqlType.Long)];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: false);
    byte[] row = BuildTextRow("1");

    Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => decoder.Decode<int>(row, 1));
    Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => decoder.Decode<int>(row, -1));
  }

  [TestMethod]
  public void UnsignedOverflowThrowsWhenReadAsSignedType()
  {
    MySqlColumnMetadata[] columns = [Column("value", MySqlType.Short, unsigned: true)];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: true);
    BinaryRowBuilder builder = new(columns.Length);
    builder.WriteInt16(0, unchecked((short)50_000));
    byte[] row = builder.Build();

    Assert.ThrowsExactly<OverflowException>(() => decoder.Decode<short>(row, 0));
    Assert.AreEqual((ushort)50_000, decoder.Decode<ushort>(row, 0));
  }

  [TestMethod]
  public void UnsignedLongLongAboveInt64MaxThrowsWhenReadAsSignedLong()
  {
    MySqlColumnMetadata[] columns = [Column("value", MySqlType.LongLong, unsigned: true)];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: true);
    BinaryRowBuilder builder = new(columns.Length);
    builder.WriteInt64(0, unchecked((long)ulong.MaxValue));
    byte[] row = builder.Build();

    Assert.ThrowsExactly<OverflowException>(() => decoder.Decode<long>(row, 0));
    Assert.AreEqual(ulong.MaxValue, decoder.Decode<ulong>(row, 0));
  }

  [TestMethod]
  public void BinaryStringColumnCannotBeReadAsString()
  {
    MySqlColumnMetadata[] columns =
    [
      new MySqlColumnMetadata(
        "value",
        "value",
        string.Empty,
        string.Empty,
        string.Empty,
        MySqlType.VarString,
        MySqlColumnFlags.Binary,
        MySqlProtocol.BinaryCollation,
        0,
        0),
    ];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: false);
    byte[] row = BuildTextRowRaw([[1, 2, 3]]);

    Assert.ThrowsExactly<InvalidCastException>(() => decoder.Decode<string>(row, 0));
    CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, decoder.Decode<byte[]>(row, 0));
  }

  [TestMethod]
  public void DecodesGuidFromBinaryFixedLengthAndTextRepresentation()
  {
    Guid value = Guid.Parse("12345678-1234-1234-1234-123456789abc");
    MySqlColumnMetadata[] textColumns = [Column("id", MySqlType.VarChar)];
    MySqlRowDecoder textDecoder = CreateDecoder(textColumns, binary: false);
    byte[] textRow = BuildTextRow(value.ToString());
    Assert.AreEqual(value, textDecoder.Decode<Guid>(textRow, 0));

    MySqlColumnMetadata[] binaryColumns =
    [
      new MySqlColumnMetadata(
        "id",
        "id",
        string.Empty,
        string.Empty,
        string.Empty,
        MySqlType.String,
        MySqlColumnFlags.Binary,
        MySqlProtocol.BinaryCollation,
        16,
        0),
    ];
    MySqlRowDecoder binaryDecoder = CreateDecoder(binaryColumns, binary: true);
    BinaryRowBuilder builder = new(binaryColumns.Length);
    builder.WriteLengthEncodedBytes(0, value.ToByteArray(bigEndian: true));
    byte[] binaryRow = builder.Build();
    Assert.AreEqual(value, binaryDecoder.Decode<Guid>(binaryRow, 0));
  }

  [TestMethod]
  public void DecodesJsonAndEnumColumnsAsStrings()
  {
    MySqlColumnMetadata[] columns = [Column("j", MySqlType.Json), Column("e", MySqlType.Enum)];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: false);
    byte[] row = BuildTextRow("""{"a":1}""", "small");

    Assert.AreEqual("""{"a":1}""", decoder.Decode<string>(row, 0));
    Assert.AreEqual("small", decoder.Decode<string>(row, 1));
  }

  [TestMethod]
  public void DecodesJsonAsJsonElementAndTypedScalar()
  {
    MySqlColumnMetadata[] columns =
    [
      Column("object", MySqlType.Json),
      Column("boolean", MySqlType.Json),
      Column("number", MySqlType.Json),
    ];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: false);
    byte[] row = BuildTextRow("""{"a":1}""", "true", "42");

    JsonElement json = decoder.Decode<JsonElement>(row, 0);

    Assert.AreEqual(1, json.GetProperty("a").GetInt32());
    Assert.IsInstanceOfType<JsonElement>(decoder.DecodeObject(row, 0));
    Assert.IsTrue(decoder.Decode<bool>(row, 1));
    Assert.AreEqual(42, decoder.Decode<int>(row, 2));
  }

  [TestMethod]
  public void DecodesGeometryAndVectorAsRawBytes()
  {
    MySqlColumnMetadata[] columns = [Column("g", MySqlType.Geometry), Column("v", MySqlType.Vector)];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: false);
    byte[] row = BuildTextRowRaw([[1, 2, 3, 4], [5, 6]]);

    CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, decoder.Decode<byte[]>(row, 0));
    CollectionAssert.AreEqual(new byte[] { 5, 6 }, decoder.Decode<byte[]>(row, 1));
  }

  [TestMethod]
  public void GetFieldCountReturnsColumnCount()
  {
    MySqlColumnMetadata[] columns = [Column("a", MySqlType.Long), Column("b", MySqlType.Long)];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: false);

    Assert.AreEqual(2, decoder.GetFieldCount(ReadOnlySpan<byte>.Empty));
  }

  [TestMethod]
  public void ObjectDecodeUsesBoxedScalarCacheForSmallValues()
  {
    MySqlColumnMetadata[] columns = [Column("a", MySqlType.Long)];
    MySqlRowDecoder decoder = CreateDecoder(columns, binary: false);
    byte[] first = BuildTextRow("5");
    byte[] second = BuildTextRow("5");

    object? firstValue = decoder.DecodeObject(first, 0);
    object? secondValue = decoder.DecodeObject(second, 0);

    Assert.AreSame(firstValue, secondValue);
  }

  private static MySqlRowDecoder CreateDecoder(
    MySqlColumnMetadata[] columns,
    bool binary,
    MySqlZeroDateBehavior zeroDates = MySqlZeroDateBehavior.Error)
  {
    Utf8StringCache strings = new(capacity: 16, maximumByteLength: 64);
    MySqlRowDecoder decoder = new(strings, zeroDates);
    decoder.SetColumns(columns, binary);
    return decoder;
  }

  private static MySqlColumnMetadata Column(string name, MySqlType type, bool unsigned = false) =>
    new(
      name,
      name,
      string.Empty,
      string.Empty,
      string.Empty,
      type,
      unsigned ? MySqlColumnFlags.Unsigned : MySqlColumnFlags.None,
      MySqlProtocol.Utf8Mb4Collation,
      0,
      0);

  private static byte[] BuildTextRow(params string[] values) =>
    BuildTextRowRaw(values.Select(static value => (byte[]?)Encoding.UTF8.GetBytes(value)).ToArray());

  private static byte[] BuildTextRowRaw(byte[]?[] values)
  {
    MySqlPayloadWriter writer = new();
    try
    {
      foreach (byte[]? value in values)
      {
        if (value is null)
        {
          writer.WriteByte(0xFB);
        }
        else
        {
          writer.WriteLengthEncodedBytes(value);
        }
      }

      return writer.WrittenSpan.ToArray();
    }
    finally
    {
      writer.Release();
    }
  }

  /// <summary>Builds a COM_STMT_EXECUTE style binary result row for decoder tests.</summary>
  private sealed class BinaryRowBuilder
  {
    private readonly int _count;
    private readonly byte[] _nullBitmap;
    private readonly MySqlPayloadWriter _values = new();

    internal BinaryRowBuilder(int count)
    {
      _count = count;
      _nullBitmap = new byte[(count + 9) / 8];
    }

    internal void SetNull(int ordinal)
    {
      int bit = ordinal + 2;
      _nullBitmap[bit >> 3] |= (byte)(1 << (bit & 7));
    }

    internal void WriteInt8(int ordinal, sbyte value) => _values.WriteByte(unchecked((byte)value));

    internal void WriteInt16(int ordinal, short value) => _values.WriteUInt16(unchecked((ushort)value));

    internal void WriteInt32(int ordinal, int value) => _values.WriteInt32(value);

    internal void WriteInt64(int ordinal, long value) => _values.WriteInt64(value);

    internal void WriteFloat(int ordinal, float value) => _values.WriteSingle(value);

    internal void WriteDouble(int ordinal, double value) => _values.WriteDouble(value);

    internal void WriteLengthEncodedBytes(int ordinal, ReadOnlySpan<byte> value) =>
      _values.WriteLengthEncodedBytes(value);

    internal void WriteDate(int ordinal, int year, int month, int day)
    {
      _values.WriteByte(4);
      _values.WriteUInt16((ushort)year);
      _values.WriteByte((byte)month);
      _values.WriteByte((byte)day);
    }

    internal void WriteDateTime(
      int ordinal,
      int year,
      int month,
      int day,
      int hour,
      int minute,
      int second,
      int microseconds)
    {
      _values.WriteByte(microseconds != 0 ? (byte)11 : (byte)7);
      _values.WriteUInt16((ushort)year);
      _values.WriteByte((byte)month);
      _values.WriteByte((byte)day);
      _values.WriteByte((byte)hour);
      _values.WriteByte((byte)minute);
      _values.WriteByte((byte)second);
      if (microseconds != 0)
      {
        _values.WriteUInt32((uint)microseconds);
      }
    }

    internal byte[] Build()
    {
      byte[] row = new byte[1 + _nullBitmap.Length + _values.Length];
      row[0] = 0;
      _nullBitmap.CopyTo(row.AsSpan(1));
      _values.WrittenSpan.CopyTo(row.AsSpan(1 + _nullBitmap.Length));
      _values.Release();
      return row;
    }
  }
}
