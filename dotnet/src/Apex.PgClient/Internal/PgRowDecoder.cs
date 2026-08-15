/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Buffers.Binary;
using System.Buffers.Text;
using System.Runtime.CompilerServices;
using Apex.SqlClient;
using Apex.SqlClient.Internal;

namespace Apex.PgClient.Internal;

internal sealed class PgRowDecoder : ISqlRowDecoder
{
  private readonly Utf8StringCache _strings;

  internal PgRowDecoder(
    int stringCacheCapacity,
    int stringCacheMaximumByteLength)
  {
    _strings = new Utf8StringCache(
      stringCacheCapacity,
      stringCacheMaximumByteLength);
  }

  public int GetFieldCount(ReadOnlySpan<byte> row)
  {
    Ensure(row, 0, sizeof(short));
    return BinaryPrimitives.ReadUInt16BigEndian(row);
  }

  public bool IsNull(ReadOnlySpan<byte> row, int ordinal) =>
    GetField(row, ordinal).IsNull;

  public object? Decode(
    ReadOnlySpan<byte> row,
    int ordinal,
    SqlColumn column)
  {
    Field field = GetField(row, ordinal);
    if (field.IsNull)
    {
      return null;
    }

    if (CanDecodeAsString(column))
    {
      return _strings.GetString(field.Value);
    }

    return column.TypeId switch
    {
      16 => BoxedScalarCache.Box(Decode<bool>(row, ordinal, column)),
      21 => BoxedScalarCache.Box(Decode<short>(row, ordinal, column)),
      23 => BoxedScalarCache.Box(Decode<int>(row, ordinal, column)),
      20 => BoxedScalarCache.Box(Decode<long>(row, ordinal, column)),
      _ => column.Format == SqlDataFormat.Binary
        ? PgBinaryCodec.Decode(column.TypeId, field.Value)
        : PgTextCodec.Decode(column.TypeId, field.Value),
    };
  }

  public T Decode<T>(
    ReadOnlySpan<byte> row,
    int ordinal,
    SqlColumn column)
  {
    Field field = GetField(row, ordinal);
    if (field.IsNull)
    {
      if (default(T) is null)
      {
        return default!;
      }

      throw new InvalidCastException($"Column {ordinal} contains NULL.");
    }

    if (typeof(T) == typeof(bool))
    {
      EnsureType(column, expectedTypeId: 16, typeof(T));
      bool value = column.Format == SqlDataFormat.Binary
        ? field.Value.Length == 1 && field.Value[0] != 0
        : field.Value.SequenceEqual("t"u8);
      return Unsafe.As<bool, T>(ref value);
    }

    if (typeof(T) == typeof(short))
    {
      EnsureType(column, expectedTypeId: 21, typeof(T));
      short value = column.Format == SqlDataFormat.Binary
        ? ReadInt16(field.Value)
        : ParseInt16(field.Value);
      return Unsafe.As<short, T>(ref value);
    }

    if (typeof(T) == typeof(int))
    {
      EnsureType(column, expectedTypeId: 23, typeof(T));
      int value = column.Format == SqlDataFormat.Binary
        ? ReadInt32(field.Value)
        : ParseInt32(field.Value);
      return Unsafe.As<int, T>(ref value);
    }

    if (typeof(T) == typeof(long))
    {
      EnsureType(column, expectedTypeId: 20, typeof(T));
      long value = column.Format == SqlDataFormat.Binary
        ? ReadInt64(field.Value)
        : ParseInt64(field.Value);
      return Unsafe.As<long, T>(ref value);
    }

    if (typeof(T) == typeof(float))
    {
      EnsureType(column, expectedTypeId: 700, typeof(T));
      float value = column.Format == SqlDataFormat.Binary
        ? BitConverter.Int32BitsToSingle(ReadInt32(field.Value))
        : ParseSingle(field.Value);
      return Unsafe.As<float, T>(ref value);
    }

    if (typeof(T) == typeof(double))
    {
      EnsureType(column, expectedTypeId: 701, typeof(T));
      double value = column.Format == SqlDataFormat.Binary
        ? BitConverter.Int64BitsToDouble(ReadInt64(field.Value))
        : ParseDouble(field.Value);
      return Unsafe.As<double, T>(ref value);
    }

    if (typeof(T) == typeof(string))
    {
      if (!CanDecodeAsString(column))
      {
        throw new InvalidCastException(
          $"PostgreSQL type OID {column.TypeId} is not a string type.");
      }

      string value = _strings.GetString(field.Value);
      return Unsafe.As<string, T>(ref value);
    }

    object? decoded = column.Format == SqlDataFormat.Binary
      ? PgBinaryCodec.Decode(column.TypeId, field.Value)
      : PgTextCodec.Decode(column.TypeId, field.Value);
    if (decoded is T typed)
    {
      return typed;
    }

    throw new InvalidCastException(
      $"Column {ordinal} contains {decoded?.GetType().FullName ?? "NULL"}, " +
      $"not {typeof(T).FullName}.");
  }

  internal void DisableCache() => _strings.Disable();

  private static Field GetField(ReadOnlySpan<byte> row, int ordinal)
  {
    Ensure(row, 0, sizeof(short));
    int count = BinaryPrimitives.ReadUInt16BigEndian(row);
    if ((uint)ordinal >= (uint)count)
    {
      throw new ArgumentOutOfRangeException(nameof(ordinal));
    }

    int position = sizeof(short);
    for (int i = 0; i < count; i++)
    {
      Ensure(row, position, sizeof(int));
      int length = BinaryPrimitives.ReadInt32BigEndian(row[position..]);
      position += sizeof(int);
      if (length < 0)
      {
        if (i == ordinal)
        {
          return new Field(default, isNull: true);
        }

        continue;
      }

      Ensure(row, position, length);
      if (i == ordinal)
      {
        return new Field(row.Slice(position, length), isNull: false);
      }

      position += length;
    }

    throw new InvalidDataException(
      "PostgreSQL row ended before the requested field.");
  }

  private static short ReadInt16(ReadOnlySpan<byte> value)
  {
    Ensure(value, 0, sizeof(short));
    return BinaryPrimitives.ReadInt16BigEndian(value);
  }

  private static int ReadInt32(ReadOnlySpan<byte> value)
  {
    Ensure(value, 0, sizeof(int));
    return BinaryPrimitives.ReadInt32BigEndian(value);
  }

  private static long ReadInt64(ReadOnlySpan<byte> value)
  {
    Ensure(value, 0, sizeof(long));
    return BinaryPrimitives.ReadInt64BigEndian(value);
  }

  private static short ParseInt16(ReadOnlySpan<byte> value) =>
    Utf8Parser.TryParse(value, out short parsed, out int consumed) &&
    consumed == value.Length
      ? parsed
      : throw new FormatException("Invalid PostgreSQL INT2 value.");

  private static int ParseInt32(ReadOnlySpan<byte> value) =>
    Utf8Parser.TryParse(value, out int parsed, out int consumed) &&
    consumed == value.Length
      ? parsed
      : throw new FormatException("Invalid PostgreSQL INT4 value.");

  private static long ParseInt64(ReadOnlySpan<byte> value) =>
    Utf8Parser.TryParse(value, out long parsed, out int consumed) &&
    consumed == value.Length
      ? parsed
      : throw new FormatException("Invalid PostgreSQL INT8 value.");

  private static float ParseSingle(ReadOnlySpan<byte> value) =>
    Utf8Parser.TryParse(value, out float parsed, out int consumed) &&
    consumed == value.Length
      ? parsed
      : throw new FormatException("Invalid PostgreSQL FLOAT4 value.");

  private static double ParseDouble(ReadOnlySpan<byte> value) =>
    Utf8Parser.TryParse(value, out double parsed, out int consumed) &&
    consumed == value.Length
      ? parsed
      : throw new FormatException("Invalid PostgreSQL FLOAT8 value.");

  private static bool IsStringType(uint typeId) =>
    typeId is 18 or 19 or 25 or 705 or 1042 or 1043 or 3614 or 3615 ||
    typeId is not (
      16 or 17 or 20 or 21 or 23 or 26 or 114 or 142 or 600 or 601 or 602 or
      603 or 604 or 628 or 650 or 700 or 701 or 718 or 774 or 790 or 829 or
      869 or 1082 or 1083 or 1114 or 1184 or 1186 or 1266 or 1560 or 1562 or
      1700 or 2278 or 2950 or 3802 or
      1000 or 1001 or 1002 or 1003 or 1005 or 1007 or 1009 or 1015 or 1016 or
      1017 or 1018 or 1019 or 1020 or 1021 or 1022 or 1027 or 1041 or 1115 or
      1182 or 1183 or 1185 or 1187 or 1231 or 1270 or 199 or 629 or 651 or
      719 or 791 or 2951 or 3807);

  private static bool CanDecodeAsString(SqlColumn column) =>
    column.Format == SqlDataFormat.Text && IsStringType(column.TypeId) ||
    column.TypeId is 18 or 19 or 25 or 1042 or 1043;

  private static void EnsureType(
    SqlColumn column,
    uint expectedTypeId,
    Type requestedType)
  {
    if (column.TypeId != expectedTypeId)
    {
      throw new InvalidCastException(
        $"PostgreSQL type OID {column.TypeId} cannot be read as " +
        $"{requestedType.FullName}.");
    }
  }

  private static void Ensure(
    ReadOnlySpan<byte> value,
    int position,
    int length)
  {
    if (length < 0 || position < 0 || position > value.Length - length)
    {
      throw new InvalidDataException("PostgreSQL row is truncated.");
    }
  }

  private readonly ref struct Field(
    ReadOnlySpan<byte> value,
    bool isNull)
  {
    public ReadOnlySpan<byte> Value { get; } = value;

    public bool IsNull { get; } = isNull;
  }
}
