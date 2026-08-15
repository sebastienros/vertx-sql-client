/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using Apex.SqlClient;
using Apex.SqlClient.Internal;

namespace Apex.MsSqlClient.Internal;

internal sealed class MsSqlRowDecoder : ISqlRowDecoder
{
  private readonly MsSqlStringCache _strings;

  internal MsSqlRowDecoder(
    int stringCacheCapacity = 1024,
    int stringCacheMaximumByteLength = 128)
  {
    _strings = new MsSqlStringCache(
      stringCacheCapacity,
      stringCacheMaximumByteLength);
  }

  public int GetFieldCount(ReadOnlySpan<byte> row)
  {
    Ensure(row, 0, sizeof(ushort));
    return BinaryPrimitives.ReadUInt16LittleEndian(row);
  }

  public bool IsNull(ReadOnlySpan<byte> row, int ordinal) =>
    GetField(row, ordinal).IsNull;

  public object? Decode(
    ReadOnlySpan<byte> row,
    int ordinal,
    SqlColumn column)
  {
    Field field = GetField(row, ordinal);
    return field.IsNull ? null : DecodeValue(field.Value, column);
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

    byte type = checked((byte)column.TypeId);
    ReadOnlySpan<byte> value = field.Value;
    if (typeof(T) == typeof(bool))
    {
      EnsureType(type is TdsDataType.Bit or TdsDataType.BitN, type, typeof(T));
      EnsureExact(value, 1);
      bool decoded = value[0] != 0;
      return Unsafe.As<bool, T>(ref decoded);
    }

    if (typeof(T) == typeof(byte))
    {
      EnsureType(
        type == TdsDataType.Int1 ||
        type == TdsDataType.IntN && value.Length == 1,
        type,
        typeof(T));
      EnsureExact(value, 1);
      byte decoded = value[0];
      return Unsafe.As<byte, T>(ref decoded);
    }

    if (typeof(T) == typeof(short))
    {
      EnsureType(
        type == TdsDataType.Int2 ||
        type == TdsDataType.IntN && value.Length == 2,
        type,
        typeof(T));
      short decoded = ReadInt16(value);
      return Unsafe.As<short, T>(ref decoded);
    }

    if (typeof(T) == typeof(int))
    {
      EnsureType(
        type == TdsDataType.Int4 ||
        type == TdsDataType.IntN && value.Length == 4,
        type,
        typeof(T));
      int decoded = ReadInt32(value);
      return Unsafe.As<int, T>(ref decoded);
    }

    if (typeof(T) == typeof(long))
    {
      EnsureType(
        type == TdsDataType.Int8 ||
        type == TdsDataType.IntN && value.Length == 8,
        type,
        typeof(T));
      long decoded = ReadInt64(value);
      return Unsafe.As<long, T>(ref decoded);
    }

    if (typeof(T) == typeof(float))
    {
      EnsureType(
        type == TdsDataType.Float4 ||
        type == TdsDataType.FloatN && value.Length == 4,
        type,
        typeof(T));
      float decoded = ReadSingle(value);
      return Unsafe.As<float, T>(ref decoded);
    }

    if (typeof(T) == typeof(double))
    {
      EnsureType(
        type == TdsDataType.Float8 ||
        type == TdsDataType.FloatN && value.Length == 8,
        type,
        typeof(T));
      double decoded = ReadDouble(value);
      return Unsafe.As<double, T>(ref decoded);
    }

    if (typeof(T) == typeof(decimal))
    {
      decimal decoded = DecodeDecimalValue(value, type, (byte)column.TypeModifier);
      return Unsafe.As<decimal, T>(ref decoded);
    }

    if (typeof(T) == typeof(Guid))
    {
      EnsureType(type == TdsDataType.Guid, type, typeof(T));
      Guid decoded = new(value);
      return Unsafe.As<Guid, T>(ref decoded);
    }

    if (typeof(T) == typeof(DateOnly))
    {
      EnsureType(type == TdsDataType.Date, type, typeof(T));
      DateOnly decoded = DecodeDate(value);
      return Unsafe.As<DateOnly, T>(ref decoded);
    }

    if (typeof(T) == typeof(TimeOnly))
    {
      EnsureType(type == TdsDataType.Time, type, typeof(T));
      TimeOnly decoded = DecodeTime(value, (byte)column.TypeModifier);
      return Unsafe.As<TimeOnly, T>(ref decoded);
    }

    if (typeof(T) == typeof(DateTime))
    {
      DateTime decoded = DecodeDateTimeValue(
        value,
        type,
        (byte)column.TypeModifier);
      return Unsafe.As<DateTime, T>(ref decoded);
    }

    if (typeof(T) == typeof(DateTimeOffset))
    {
      EnsureType(type == TdsDataType.DateTimeOffset, type, typeof(T));
      DateTimeOffset decoded = DecodeDateTimeOffset(
        value,
        (byte)column.TypeModifier);
      return Unsafe.As<DateTimeOffset, T>(ref decoded);
    }

    if (typeof(T) == typeof(string))
    {
      EnsureType(IsStringType(type), type, typeof(T));
      string decoded = DecodeString(value, column);
      return Unsafe.As<string, T>(ref decoded);
    }

    if (typeof(T) == typeof(byte[]))
    {
      EnsureType(IsBinaryType(type), type, typeof(T));
      byte[] decoded = value.ToArray();
      return Unsafe.As<byte[], T>(ref decoded);
    }

    object decodedValue = DecodeValue(value, column);
    if (decodedValue is T typed)
    {
      return typed;
    }

    throw new InvalidCastException(
      $"Column {ordinal} contains {decodedValue.GetType().FullName}, " +
      $"not {typeof(T).FullName}.");
  }

  internal void DisableCache() => _strings.Disable();

  private object DecodeValue(ReadOnlySpan<byte> value, SqlColumn column)
  {
    byte type = checked((byte)column.TypeId);
    byte scale = (byte)column.TypeModifier;
    return type switch
    {
      TdsDataType.Int1 => value[0],
      TdsDataType.Bit or TdsDataType.BitN => value[0] != 0,
      TdsDataType.Int2 => ReadInt16(value),
      TdsDataType.Int4 => ReadInt32(value),
      TdsDataType.Int8 => ReadInt64(value),
      TdsDataType.IntN => DecodeIntN(value),
      TdsDataType.Float4 => ReadSingle(value),
      TdsDataType.Float8 => ReadDouble(value),
      TdsDataType.FloatN => DecodeFloatN(value),
      TdsDataType.Decimal or
      TdsDataType.Numeric or
      TdsDataType.DecimalN or
      TdsDataType.NumericN => DecodeDecimal(value, scale),
      TdsDataType.Money or TdsDataType.MoneyN when value.Length == 8 =>
        DecodeMoney(value),
      TdsDataType.Money4 or TdsDataType.MoneyN =>
        ReadInt32(value) / 10000m,
      TdsDataType.Guid => new Guid(value),
      TdsDataType.Date => DecodeDate(value),
      TdsDataType.Time => DecodeTime(value, scale),
      TdsDataType.DateTime2 => DecodeDateTime2(value, scale),
      TdsDataType.DateTimeOffset => DecodeDateTimeOffset(value, scale),
      TdsDataType.DateTime => DecodeLegacyDateTime(value),
      TdsDataType.DateTime4 => DecodeSmallDateTime(value),
      TdsDataType.DateTimeN when value.Length == 8 => DecodeLegacyDateTime(value),
      TdsDataType.DateTimeN => DecodeSmallDateTime(value),
      TdsDataType.NVarChar or
      TdsDataType.NChar or
      TdsDataType.NText or
      TdsDataType.Xml or
      TdsDataType.Json or
      TdsDataType.Char or
      TdsDataType.VarChar or
      TdsDataType.BigChar or
      TdsDataType.BigVarChar or
      TdsDataType.Text => DecodeString(value, column),
      TdsDataType.Binary or
      TdsDataType.VarBinary or
      TdsDataType.BigBinary or
      TdsDataType.BigVarBinary or
      TdsDataType.Image or
      TdsDataType.Udt => value.ToArray(),
      _ => throw new NotSupportedException(
        $"Cannot decode SQL Server TDS data type 0x{type:X2}."),
    };
  }

  private static decimal DecodeDecimalValue(
    ReadOnlySpan<byte> value,
    byte type,
    byte scale) =>
    type switch
    {
      TdsDataType.Decimal or
      TdsDataType.Numeric or
      TdsDataType.DecimalN or
      TdsDataType.NumericN => DecodeDecimal(value, scale),
      TdsDataType.Money or TdsDataType.MoneyN when value.Length == 8 =>
        DecodeMoney(value),
      TdsDataType.Money4 or TdsDataType.MoneyN =>
        ReadInt32(value) / 10000m,
      _ => throw CreateInvalidCast(type, typeof(decimal)),
    };

  private static DateTime DecodeDateTimeValue(
    ReadOnlySpan<byte> value,
    byte type,
    byte scale) =>
    type switch
    {
      TdsDataType.DateTime2 => DecodeDateTime2(value, scale),
      TdsDataType.DateTime => DecodeLegacyDateTime(value),
      TdsDataType.DateTime4 => DecodeSmallDateTime(value),
      TdsDataType.DateTimeN when value.Length == 8 => DecodeLegacyDateTime(value),
      TdsDataType.DateTimeN when value.Length == 4 => DecodeSmallDateTime(value),
      _ => throw CreateInvalidCast(type, typeof(DateTime)),
    };

  private static object DecodeIntN(ReadOnlySpan<byte> value) =>
    value.Length switch
    {
      1 => (object)value[0],
      2 => ReadInt16(value),
      4 => ReadInt32(value),
      8 => ReadInt64(value),
      _ => throw new InvalidDataException($"Invalid SQL Server INTN length {value.Length}."),
    };

  private static object DecodeFloatN(ReadOnlySpan<byte> value) =>
    value.Length switch
    {
      4 => (object)ReadSingle(value),
      8 => ReadDouble(value),
      _ => throw new InvalidDataException($"Invalid SQL Server FLTN length {value.Length}."),
    };

  private static decimal DecodeDecimal(ReadOnlySpan<byte> value, byte scale)
  {
    if (value.Length is < 1 or > 17 || scale > 28)
    {
      throw new OverflowException("SQL Server decimal value cannot be represented by System.Decimal.");
    }

    ReadOnlySpan<byte> magnitude = value[1..];
    if (magnitude.Length > 12 && !IsZero(magnitude[12..]))
    {
      throw new OverflowException("SQL Server decimal value cannot be represented by System.Decimal.");
    }

    Span<byte> bits = stackalloc byte[12];
    magnitude[..Math.Min(magnitude.Length, bits.Length)].CopyTo(bits);
    int low = BinaryPrimitives.ReadInt32LittleEndian(bits);
    int middle = BinaryPrimitives.ReadInt32LittleEndian(bits[4..]);
    int high = BinaryPrimitives.ReadInt32LittleEndian(bits[8..]);
    return new decimal(low, middle, high, value[0] == 0, scale);
  }

  private static decimal DecodeMoney(ReadOnlySpan<byte> value)
  {
    EnsureExact(value, sizeof(long));
    long high = BinaryPrimitives.ReadInt32LittleEndian(value);
    long low = BinaryPrimitives.ReadUInt32LittleEndian(value[4..]);
    return ((high << 32) | low) / 10000m;
  }

  private static DateOnly DecodeDate(ReadOnlySpan<byte> value)
  {
    if (value.Length != 3)
    {
      throw new InvalidDataException($"Invalid SQL Server DATE length {value.Length}.");
    }

    int days = value[0] | value[1] << 8 | value[2] << 16;
    return DateOnly.FromDayNumber(days);
  }

  private static TimeOnly DecodeTime(ReadOnlySpan<byte> value, byte scale)
  {
    long units = ReadUnsignedLittleEndian(value);
    long ticks = checked(units * PowerOfTen(7 - scale));
    return new TimeOnly(ticks);
  }

  private static DateTime DecodeDateTime2(ReadOnlySpan<byte> value, byte scale)
  {
    int timeLength = value.Length - 3;
    if (timeLength is < 3 or > 5)
    {
      throw new InvalidDataException($"Invalid SQL Server DATETIME2 length {value.Length}.");
    }

    TimeOnly time = DecodeTime(value[..timeLength], scale);
    DateOnly date = DecodeDate(value[timeLength..]);
    return date.ToDateTime(time, DateTimeKind.Unspecified);
  }

  private static DateTimeOffset DecodeDateTimeOffset(ReadOnlySpan<byte> value, byte scale)
  {
    int timeLength = value.Length - 5;
    if (timeLength is < 3 or > 5)
    {
      throw new InvalidDataException(
        $"Invalid SQL Server DATETIMEOFFSET length {value.Length}.");
    }

    TimeOnly time = DecodeTime(value[..timeLength], scale);
    DateOnly date = DecodeDate(value.Slice(timeLength, 3));
    TimeSpan offset = TimeSpan.FromMinutes(
      BinaryPrimitives.ReadInt16LittleEndian(value[(timeLength + 3)..]));
    DateTime utc = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Utc);
    return new DateTimeOffset(utc).ToOffset(offset);
  }

  private static DateTime DecodeLegacyDateTime(ReadOnlySpan<byte> value)
  {
    if (value.Length != 8)
    {
      throw new InvalidDataException($"Invalid SQL Server DATETIME length {value.Length}.");
    }

    int days = BinaryPrimitives.ReadInt32LittleEndian(value);
    uint threeHundredths = BinaryPrimitives.ReadUInt32LittleEndian(value[4..]);
    long milliseconds = checked((long)Math.Round(
      threeHundredths * 1000d / 300,
      MidpointRounding.AwayFromZero));
    return new DateTime(1900, 1, 1).AddDays(days).AddMilliseconds(milliseconds);
  }

  private static DateTime DecodeSmallDateTime(ReadOnlySpan<byte> value)
  {
    if (value.Length != 4)
    {
      throw new InvalidDataException(
        $"Invalid SQL Server SMALLDATETIME length {value.Length}.");
    }

    int days = BinaryPrimitives.ReadUInt16LittleEndian(value);
    int minutes = BinaryPrimitives.ReadUInt16LittleEndian(value[2..]);
    return new DateTime(1900, 1, 1).AddDays(days).AddMinutes(minutes);
  }

  private string DecodeString(ReadOnlySpan<byte> value, SqlColumn column)
  {
    byte type = checked((byte)column.TypeId);
    int codePage = type is
      TdsDataType.NVarChar or
      TdsDataType.NChar or
      TdsDataType.NText or
      TdsDataType.Xml or
      TdsDataType.Json
        ? 1200
        : (int)(unchecked((uint)column.TypeModifier) >> 16);
    if (codePage == 0)
    {
      throw new InvalidDataException(
        $"SQL Server character type 0x{type:X2} did not include a resolvable collation.");
    }

    return _strings.GetString(value, codePage);
  }

  private static bool IsStringType(byte type) =>
    type is
      TdsDataType.NVarChar or
      TdsDataType.NChar or
      TdsDataType.NText or
      TdsDataType.Xml or
      TdsDataType.Json or
      TdsDataType.Char or
      TdsDataType.VarChar or
      TdsDataType.BigChar or
      TdsDataType.BigVarChar or
      TdsDataType.Text;

  private static bool IsBinaryType(byte type) =>
    type is
      TdsDataType.Binary or
      TdsDataType.VarBinary or
      TdsDataType.BigBinary or
      TdsDataType.BigVarBinary or
      TdsDataType.Image or
      TdsDataType.Udt;

  private static void EnsureType(bool matches, byte type, Type requestedType)
  {
    if (!matches)
    {
      throw CreateInvalidCast(type, requestedType);
    }
  }

  private static InvalidCastException CreateInvalidCast(byte type, Type requestedType) =>
    new(
      $"SQL Server TDS type 0x{type:X2} cannot be decoded as " +
      $"{requestedType.FullName}.");

  private static short ReadInt16(ReadOnlySpan<byte> value)
  {
    EnsureExact(value, sizeof(short));
    return BinaryPrimitives.ReadInt16LittleEndian(value);
  }

  private static int ReadInt32(ReadOnlySpan<byte> value)
  {
    EnsureExact(value, sizeof(int));
    return BinaryPrimitives.ReadInt32LittleEndian(value);
  }

  private static long ReadInt64(ReadOnlySpan<byte> value)
  {
    EnsureExact(value, sizeof(long));
    return BinaryPrimitives.ReadInt64LittleEndian(value);
  }

  private static float ReadSingle(ReadOnlySpan<byte> value) =>
    BitConverter.Int32BitsToSingle(ReadInt32(value));

  private static double ReadDouble(ReadOnlySpan<byte> value) =>
    BitConverter.Int64BitsToDouble(ReadInt64(value));

  private static long ReadUnsignedLittleEndian(ReadOnlySpan<byte> value)
  {
    if (value.Length is < 1 or > 8)
    {
      throw new InvalidDataException("Invalid little-endian integer length.");
    }

    long result = 0;
    for (int i = 0; i < value.Length; i++)
    {
      result |= (long)value[i] << (8 * i);
    }

    return result;
  }

  private static long PowerOfTen(int exponent)
  {
    if ((uint)exponent > 7)
    {
      throw new InvalidDataException("Invalid SQL Server temporal scale.");
    }

    long result = 1;
    for (int i = 0; i < exponent; i++)
    {
      result *= 10;
    }

    return result;
  }

  private static Field GetField(ReadOnlySpan<byte> row, int ordinal)
  {
    Ensure(row, 0, sizeof(ushort));
    int count = BinaryPrimitives.ReadUInt16LittleEndian(row);
    if ((uint)ordinal >= (uint)count)
    {
      throw new ArgumentOutOfRangeException(nameof(ordinal));
    }

    int position = sizeof(ushort);
    for (int i = 0; i < count; i++)
    {
      Ensure(row, position, sizeof(int));
      int length = BinaryPrimitives.ReadInt32LittleEndian(row[position..]);
      position += sizeof(int);
      if (length < 0)
      {
        if (i == ordinal)
        {
          return new Field(default, IsNull: true);
        }

        continue;
      }

      Ensure(row, position, length);
      if (i == ordinal)
      {
        return new Field(row.Slice(position, length), IsNull: false);
      }

      position += length;
    }

    throw new InvalidDataException("SQL Server row ended before the requested field.");
  }

  private static bool IsZero(ReadOnlySpan<byte> value)
  {
    foreach (byte item in value)
    {
      if (item != 0)
      {
        return false;
      }
    }

    return true;
  }

  private static void EnsureExact(ReadOnlySpan<byte> value, int length)
  {
    if (value.Length != length)
    {
      throw new InvalidDataException(
        $"SQL Server value has length {value.Length}; expected {length}.");
    }
  }

  private static void Ensure(ReadOnlySpan<byte> value, int position, int length)
  {
    if (position < 0 || length < 0 || position > value.Length - length)
    {
      throw new InvalidDataException("SQL Server row is truncated.");
    }
  }

  private readonly ref struct Field(ReadOnlySpan<byte> value, bool IsNull)
  {
    internal ReadOnlySpan<byte> Value { get; } = value;

    internal bool IsNull { get; } = IsNull;
  }
}
