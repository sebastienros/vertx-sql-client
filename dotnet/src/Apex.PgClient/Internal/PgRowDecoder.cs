/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text.Json;
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

  public int GetFieldCount(ReadOnlyMemory<byte> row) =>
    GetFieldCount(row.Span);

  internal int GetFieldCount(ReadOnlySpan<byte> row)
  {
    Ensure(row, 0, sizeof(short));
    return BinaryPrimitives.ReadUInt16BigEndian(row);
  }

  public bool IsNull(ReadOnlyMemory<byte> row, int ordinal) =>
    GetField(row, ordinal).IsNull;

  public object? DecodeObject(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureFormat(column, typeof(object));
    Field field = GetField(row, ordinal);
    if (field.IsNull)
    {
      return null;
    }

    if (CanDecodeAsString(column))
    {
      return _strings.GetString(field.Value.Span);
    }

    return column.TypeId switch
    {
      16 => BoxedScalarCache.Box(
        DecodeBooleanValue(column.Format, field.Value.Span)),
      21 => BoxedScalarCache.Box(
        DecodeInt16Value(column.Format, field.Value.Span)),
      23 => BoxedScalarCache.Box(
        DecodeInt32Value(column.Format, field.Value.Span)),
      20 => BoxedScalarCache.Box(
        DecodeInt64Value(column.Format, field.Value.Span)),
      _ => column.Format == SqlDataFormat.Binary
        ? PgBinaryCodec.Decode(column.TypeId, field.Value)
        : PgTextCodec.Decode(column.TypeId, field.Value),
    };
  }

  public bool DecodeBoolean(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 16, typeof(bool));
    Field field = GetRequiredField(row, ordinal);
    return DecodeBooleanValue(column.Format, field.Value.Span);
  }

  public bool? DecodeNullableBoolean(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 16, typeof(bool?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodeBooleanValue(column.Format, field.Value.Span);
  }

  public short DecodeInt16(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 21, typeof(short));
    Field field = GetRequiredField(row, ordinal);
    return DecodeInt16Value(column.Format, field.Value.Span);
  }

  public short? DecodeNullableInt16(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 21, typeof(short?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodeInt16Value(column.Format, field.Value.Span);
  }

  public int DecodeInt32(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 23, typeof(int));
    Field field = GetRequiredField(row, ordinal);
    return DecodeInt32Value(column.Format, field.Value.Span);
  }

  public int? DecodeNullableInt32(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 23, typeof(int?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodeInt32Value(column.Format, field.Value.Span);
  }

  public long DecodeInt64(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 20, typeof(long));
    Field field = GetRequiredField(row, ordinal);
    return DecodeInt64Value(column.Format, field.Value.Span);
  }

  public long? DecodeNullableInt64(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 20, typeof(long?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodeInt64Value(column.Format, field.Value.Span);
  }

  public float DecodeFloat(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 700, typeof(float));
    Field field = GetRequiredField(row, ordinal);
    return DecodeFloatValue(column.Format, field.Value.Span);
  }

  public float? DecodeNullableFloat(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 700, typeof(float?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodeFloatValue(column.Format, field.Value.Span);
  }

  public double DecodeDouble(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 701, typeof(double));
    Field field = GetRequiredField(row, ordinal);
    return DecodeDoubleValue(column.Format, field.Value.Span);
  }

  public double? DecodeNullableDouble(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 701, typeof(double?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodeDoubleValue(column.Format, field.Value.Span);
  }

  public decimal DecodeDecimal(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 1700, typeof(decimal));
    Field field = GetRequiredField(row, ordinal);
    return DecodeDecimalValue(column.Format, field.Value.Span);
  }

  public decimal? DecodeNullableDecimal(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 1700, typeof(decimal?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodeDecimalValue(column.Format, field.Value.Span);
  }

  public string? DecodeString(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureStringType(column, typeof(string));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : _strings.GetString(field.Value.Span);
  }

  public byte[]? DecodeBytes(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 17, typeof(byte[]));
    Field field = GetField(row, ordinal);
    if (field.IsNull)
    {
      return null;
    }

    return column.Format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeBytes(field.Value.Span)
      : PgTextCodec.DecodeBytes(field.Value.Span);
  }

  public ReadOnlyMemory<byte> DecodeReadOnlyMemory(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 17, typeof(ReadOnlyMemory<byte>));
    Field field = GetRequiredField(row, ordinal);
    return column.Format == SqlDataFormat.Binary
      ? field.Value
      : PgTextCodec.DecodeBytes(field.Value.Span);
  }

  public ReadOnlyMemory<byte>? DecodeNullableReadOnlyMemory(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 17, typeof(ReadOnlyMemory<byte>?));
    Field field = GetField(row, ordinal);
    if (field.IsNull)
    {
      return null;
    }

    return column.Format == SqlDataFormat.Binary
      ? field.Value
      : PgTextCodec.DecodeBytes(field.Value.Span);
  }

  public Guid DecodeGuid(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 2950, typeof(Guid));
    Field field = GetRequiredField(row, ordinal);
    return DecodeGuidValue(column.Format, field.Value.Span);
  }

  public Guid? DecodeNullableGuid(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 2950, typeof(Guid?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodeGuidValue(column.Format, field.Value.Span);
  }

  public DateOnly DecodeDateOnly(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 1082, typeof(DateOnly));
    Field field = GetRequiredField(row, ordinal);
    return DecodeDateOnlyValue(column.Format, field.Value.Span);
  }

  public DateOnly? DecodeNullableDateOnly(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 1082, typeof(DateOnly?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodeDateOnlyValue(column.Format, field.Value.Span);
  }

  public TimeOnly DecodeTimeOnly(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 1083, typeof(TimeOnly));
    Field field = GetRequiredField(row, ordinal);
    return DecodeTimeOnlyValue(column.Format, field.Value.Span);
  }

  public TimeOnly? DecodeNullableTimeOnly(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 1083, typeof(TimeOnly?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodeTimeOnlyValue(column.Format, field.Value.Span);
  }

  public DateTime DecodeDateTime(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 1114, typeof(DateTime));
    Field field = GetRequiredField(row, ordinal);
    return DecodeDateTimeValue(column.Format, field.Value.Span);
  }

  public DateTime? DecodeNullableDateTime(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 1114, typeof(DateTime?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodeDateTimeValue(column.Format, field.Value.Span);
  }

  public DateTimeOffset DecodeDateTimeOffset(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 1184, typeof(DateTimeOffset));
    Field field = GetRequiredField(row, ordinal);
    return DecodeDateTimeOffsetValue(
      column.Format,
      field.Value.Span);
  }

  public DateTimeOffset? DecodeNullableDateTimeOffset(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 1184, typeof(DateTimeOffset?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodeDateTimeOffsetValue(
        column.Format,
        field.Value.Span);
  }

  public JsonElement DecodeJsonElement(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureJsonType(column, typeof(JsonElement));
    Field field = GetRequiredField(row, ordinal);
    return DecodeJsonValue(column, field.Value);
  }

  public JsonElement? DecodeNullableJsonElement(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureJsonType(column, typeof(JsonElement?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodeJsonValue(column, field.Value);
  }

  public object?[]? DecodeArray(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureArrayType(column, typeof(object?[]));
    Field field = GetField(row, ordinal);
    if (field.IsNull)
    {
      return null;
    }

    return column.Format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeArray(field.Value)
      : PgTextCodec.DecodeArray(column.TypeId, field.Value);
  }

  public T DecodeProviderSpecific<T>(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    if (typeof(T) == typeof(PgNumeric))
    {
      PgNumeric value = DecodePgNumeric(row, ordinal, column);
      return Unsafe.As<PgNumeric, T>(ref value);
    }

    if (typeof(T) == typeof(PgMoney))
    {
      PgMoney value = DecodePgMoney(row, ordinal, column);
      return Unsafe.As<PgMoney, T>(ref value);
    }

    if (typeof(T) == typeof(PgInterval))
    {
      PgInterval value = DecodePgInterval(row, ordinal, column);
      return Unsafe.As<PgInterval, T>(ref value);
    }

    if (typeof(T) == typeof(PgTimeWithTimeZone))
    {
      PgTimeWithTimeZone value =
        DecodePgTimeWithTimeZone(row, ordinal, column);
      return Unsafe.As<PgTimeWithTimeZone, T>(ref value);
    }

    if (typeof(T) == typeof(PgPoint))
    {
      PgPoint value = DecodePgPoint(row, ordinal, column);
      return Unsafe.As<PgPoint, T>(ref value);
    }

    if (typeof(T) == typeof(PgLineSegment))
    {
      PgLineSegment value =
        DecodePgLineSegment(row, ordinal, column);
      return Unsafe.As<PgLineSegment, T>(ref value);
    }

    if (typeof(T) == typeof(PgPath))
    {
      PgPath? value = DecodePgPath(row, ordinal, column);
      return Unsafe.As<PgPath?, T>(ref value);
    }

    if (typeof(T) == typeof(PgBox))
    {
      PgBox value = DecodePgBox(row, ordinal, column);
      return Unsafe.As<PgBox, T>(ref value);
    }

    if (typeof(T) == typeof(PgPolygon))
    {
      PgPolygon? value = DecodePgPolygon(row, ordinal, column);
      return Unsafe.As<PgPolygon?, T>(ref value);
    }

    if (typeof(T) == typeof(PgLine))
    {
      PgLine value = DecodePgLine(row, ordinal, column);
      return Unsafe.As<PgLine, T>(ref value);
    }

    if (typeof(T) == typeof(PgCidr))
    {
      PgCidr value = DecodePgCidr(row, ordinal, column);
      return Unsafe.As<PgCidr, T>(ref value);
    }

    if (typeof(T) == typeof(PgCircle))
    {
      PgCircle value = DecodePgCircle(row, ordinal, column);
      return Unsafe.As<PgCircle, T>(ref value);
    }

    if (typeof(T) == typeof(PgInet))
    {
      PgInet value = DecodePgInet(row, ordinal, column);
      return Unsafe.As<PgInet, T>(ref value);
    }

    if (typeof(T) == typeof(PgNumeric?))
    {
      PgNumeric? value =
        DecodeNullablePgNumeric(row, ordinal, column);
      return Unsafe.As<PgNumeric?, T>(ref value);
    }

    if (typeof(T) == typeof(PgMoney?))
    {
      PgMoney? value =
        DecodeNullablePgMoney(row, ordinal, column);
      return Unsafe.As<PgMoney?, T>(ref value);
    }

    if (typeof(T) == typeof(PgInterval?))
    {
      PgInterval? value =
        DecodeNullablePgInterval(row, ordinal, column);
      return Unsafe.As<PgInterval?, T>(ref value);
    }

    if (typeof(T) == typeof(PgTimeWithTimeZone?))
    {
      PgTimeWithTimeZone? value =
        DecodeNullablePgTimeWithTimeZone(
          row,
          ordinal,
          column);
      return Unsafe.As<PgTimeWithTimeZone?, T>(ref value);
    }

    if (typeof(T) == typeof(PgPoint?))
    {
      PgPoint? value =
        DecodeNullablePgPoint(row, ordinal, column);
      return Unsafe.As<PgPoint?, T>(ref value);
    }

    if (typeof(T) == typeof(PgLineSegment?))
    {
      PgLineSegment? value =
        DecodeNullablePgLineSegment(row, ordinal, column);
      return Unsafe.As<PgLineSegment?, T>(ref value);
    }

    if (typeof(T) == typeof(PgBox?))
    {
      PgBox? value = DecodeNullablePgBox(row, ordinal, column);
      return Unsafe.As<PgBox?, T>(ref value);
    }

    if (typeof(T) == typeof(PgLine?))
    {
      PgLine? value =
        DecodeNullablePgLine(row, ordinal, column);
      return Unsafe.As<PgLine?, T>(ref value);
    }

    if (typeof(T) == typeof(PgCidr?))
    {
      PgCidr? value =
        DecodeNullablePgCidr(row, ordinal, column);
      return Unsafe.As<PgCidr?, T>(ref value);
    }

    if (typeof(T) == typeof(PgCircle?))
    {
      PgCircle? value =
        DecodeNullablePgCircle(row, ordinal, column);
      return Unsafe.As<PgCircle?, T>(ref value);
    }

    if (typeof(T) == typeof(PgInet?))
    {
      PgInet? value =
        DecodeNullablePgInet(row, ordinal, column);
      return Unsafe.As<PgInet?, T>(ref value);
    }

    throw CannotRead(column, typeof(T));
  }

  internal void DisableCache() => _strings.Disable();

  internal PgNumeric DecodePgNumeric(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 1700, typeof(PgNumeric));
    Field field = GetRequiredField(row, ordinal);
    return DecodePgNumericValue(column.Format, field.Value.Span);
  }

  internal PgMoney DecodePgMoney(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 790, typeof(PgMoney));
    Field field = GetRequiredField(row, ordinal);
    return DecodePgMoneyValue(column.Format, field.Value.Span);
  }

  internal PgInterval DecodePgInterval(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 1186, typeof(PgInterval));
    Field field = GetRequiredField(row, ordinal);
    return DecodePgIntervalValue(column.Format, field.Value.Span);
  }

  internal PgTimeWithTimeZone DecodePgTimeWithTimeZone(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 1266, typeof(PgTimeWithTimeZone));
    Field field = GetRequiredField(row, ordinal);
    return DecodePgTimeWithTimeZoneValue(
      column.Format,
      field.Value.Span);
  }

  internal PgPoint DecodePgPoint(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 600, typeof(PgPoint));
    Field field = GetRequiredField(row, ordinal);
    return DecodePgPointValue(column.Format, field.Value.Span);
  }

  internal PgLineSegment DecodePgLineSegment(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 601, typeof(PgLineSegment));
    Field field = GetRequiredField(row, ordinal);
    return DecodePgLineSegmentValue(
      column.Format,
      field.Value.Span);
  }

  internal PgPath? DecodePgPath(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 602, typeof(PgPath));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodePgPathValue(column.Format, field.Value.Span);
  }

  internal PgBox DecodePgBox(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 603, typeof(PgBox));
    Field field = GetRequiredField(row, ordinal);
    return DecodePgBoxValue(column.Format, field.Value.Span);
  }

  internal PgPolygon? DecodePgPolygon(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 604, typeof(PgPolygon));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodePgPolygonValue(column.Format, field.Value.Span);
  }

  internal PgLine DecodePgLine(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 628, typeof(PgLine));
    Field field = GetRequiredField(row, ordinal);
    return DecodePgLineValue(column.Format, field.Value.Span);
  }

  internal PgCidr DecodePgCidr(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 650, typeof(PgCidr));
    Field field = GetRequiredField(row, ordinal);
    return DecodePgCidrValue(column.Format, field.Value.Span);
  }

  internal PgCircle DecodePgCircle(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 718, typeof(PgCircle));
    Field field = GetRequiredField(row, ordinal);
    return DecodePgCircleValue(column.Format, field.Value.Span);
  }

  internal PgInet DecodePgInet(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 869, typeof(PgInet));
    Field field = GetRequiredField(row, ordinal);
    return DecodePgInetValue(column.Format, field.Value.Span);
  }

  private static PgNumeric? DecodeNullablePgNumeric(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 1700, typeof(PgNumeric?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodePgNumericValue(column.Format, field.Value.Span);
  }

  private static PgMoney? DecodeNullablePgMoney(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 790, typeof(PgMoney?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodePgMoneyValue(column.Format, field.Value.Span);
  }

  private static PgInterval? DecodeNullablePgInterval(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 1186, typeof(PgInterval?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodePgIntervalValue(column.Format, field.Value.Span);
  }

  private static PgTimeWithTimeZone?
    DecodeNullablePgTimeWithTimeZone(
      ReadOnlyMemory<byte> row,
      int ordinal,
      SqlColumn column)
  {
    EnsureType(column, 1266, typeof(PgTimeWithTimeZone?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodePgTimeWithTimeZoneValue(
        column.Format,
        field.Value.Span);
  }

  private static PgPoint? DecodeNullablePgPoint(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 600, typeof(PgPoint?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodePgPointValue(column.Format, field.Value.Span);
  }

  private static PgLineSegment? DecodeNullablePgLineSegment(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 601, typeof(PgLineSegment?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodePgLineSegmentValue(
        column.Format,
        field.Value.Span);
  }

  private static PgBox? DecodeNullablePgBox(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 603, typeof(PgBox?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodePgBoxValue(column.Format, field.Value.Span);
  }

  private static PgLine? DecodeNullablePgLine(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 628, typeof(PgLine?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodePgLineValue(column.Format, field.Value.Span);
  }

  private static PgCidr? DecodeNullablePgCidr(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 650, typeof(PgCidr?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodePgCidrValue(column.Format, field.Value.Span);
  }

  private static PgCircle? DecodeNullablePgCircle(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 718, typeof(PgCircle?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodePgCircleValue(column.Format, field.Value.Span);
  }

  private static PgInet? DecodeNullablePgInet(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column)
  {
    EnsureType(column, 869, typeof(PgInet?));
    Field field = GetField(row, ordinal);
    return field.IsNull
      ? null
      : DecodePgInetValue(column.Format, field.Value.Span);
  }

  private static Field GetRequiredField(
    ReadOnlyMemory<byte> row,
    int ordinal)
  {
    Field field = GetField(row, ordinal);
    if (field.IsNull)
    {
      throw new InvalidCastException(
        $"Column {ordinal} contains NULL.");
    }

    return field;
  }

  private static Field GetField(
    ReadOnlyMemory<byte> row,
    int ordinal)
  {
    ReadOnlySpan<byte> span = row.Span;
    Ensure(span, 0, sizeof(short));
    int count = BinaryPrimitives.ReadUInt16BigEndian(span);
    if ((uint)ordinal >= (uint)count)
    {
      throw new ArgumentOutOfRangeException(nameof(ordinal));
    }

    int position = sizeof(short);
    for (int i = 0; i < count; i++)
    {
      Ensure(span, position, sizeof(int));
      int length =
        BinaryPrimitives.ReadInt32BigEndian(span[position..]);
      position += sizeof(int);
      if (length < 0)
      {
        if (i == ordinal)
        {
          return new Field(default, IsNull: true);
        }

        continue;
      }

      Ensure(span, position, length);
      if (i == ordinal)
      {
        return new Field(
          row.Slice(position, length),
          IsNull: false);
      }

      position += length;
    }

    throw new InvalidDataException(
      "PostgreSQL row ended before the requested field.");
  }

  private static bool DecodeBooleanValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeBoolean(value)
      : PgTextCodec.DecodeBoolean(value);

  private static short DecodeInt16Value(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeInt16(value)
      : PgTextCodec.DecodeInt16(value);

  private static int DecodeInt32Value(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeInt32(value)
      : PgTextCodec.DecodeInt32(value);

  private static long DecodeInt64Value(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeInt64(value)
      : PgTextCodec.DecodeInt64(value);

  private static float DecodeFloatValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeFloat(value)
      : PgTextCodec.DecodeFloat(value);

  private static double DecodeDoubleValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeDouble(value)
      : PgTextCodec.DecodeDouble(value);

  private static decimal DecodeDecimalValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeDecimal(value)
      : PgTextCodec.DecodeDecimal(value);

  private static Guid DecodeGuidValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeGuid(value)
      : PgTextCodec.DecodeGuid(value);

  private static DateOnly DecodeDateOnlyValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeDateOnly(value)
      : PgTextCodec.DecodeDateOnly(value);

  private static TimeOnly DecodeTimeOnlyValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeTimeOnly(value)
      : PgTextCodec.DecodeTimeOnly(value);

  private static DateTime DecodeDateTimeValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeDateTime(value)
      : PgTextCodec.DecodeDateTime(value);

  private static DateTimeOffset DecodeDateTimeOffsetValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeDateTimeOffset(value)
      : PgTextCodec.DecodeDateTimeOffset(value);

  private static JsonElement DecodeJsonValue(
    SqlColumn column,
    ReadOnlyMemory<byte> value) =>
    column.Format == SqlDataFormat.Binary
      ? column.TypeId == 3802
        ? PgBinaryCodec.DecodeJsonb(value)
        : PgBinaryCodec.DecodeJson(value)
      : PgTextCodec.DecodeJson(value);

  private static PgNumeric DecodePgNumericValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeNumeric(value)
      : PgTextCodec.DecodeNumeric(value);

  private static PgMoney DecodePgMoneyValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeMoney(value)
      : PgTextCodec.DecodeMoney(value);

  private static PgInterval DecodePgIntervalValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeInterval(value)
      : PgTextCodec.DecodeInterval(value);

  private static PgTimeWithTimeZone
    DecodePgTimeWithTimeZoneValue(
      SqlDataFormat format,
      ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeTimeWithTimeZone(value)
      : PgTextCodec.DecodeTimeWithTimeZone(value);

  private static PgPoint DecodePgPointValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodePoint(value)
      : PgTextCodec.DecodePoint(value);

  private static PgLineSegment DecodePgLineSegmentValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeLineSegment(value)
      : PgTextCodec.DecodeLineSegment(value);

  private static PgPath DecodePgPathValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodePath(value)
      : PgTextCodec.DecodePath(value);

  private static PgBox DecodePgBoxValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeBox(value)
      : PgTextCodec.DecodeBox(value);

  private static PgPolygon DecodePgPolygonValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodePolygon(value)
      : PgTextCodec.DecodePolygon(value);

  private static PgLine DecodePgLineValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeLine(value)
      : PgTextCodec.DecodeLine(value);

  private static PgCidr DecodePgCidrValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeCidr(value)
      : PgTextCodec.DecodeCidr(value);

  private static PgCircle DecodePgCircleValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeCircle(value)
      : PgTextCodec.DecodeCircle(value);

  private static PgInet DecodePgInetValue(
    SqlDataFormat format,
    ReadOnlySpan<byte> value) =>
    format == SqlDataFormat.Binary
      ? PgBinaryCodec.DecodeInet(value)
      : PgTextCodec.DecodeInet(value);

  private static bool IsStringType(uint typeId) =>
    typeId is 18 or 19 or 25 or 705 or 1042 or 1043 or 3614 or 3615 ||
    typeId is not (
      16 or 17 or 20 or 21 or 23 or 26 or 114 or 142 or 600 or 601 or
      602 or 603 or 604 or 628 or 650 or 700 or 701 or 718 or 774 or
      790 or 829 or 869 or 1082 or 1083 or 1114 or 1184 or 1186 or
      1266 or 1560 or 1562 or 1700 or 2278 or 2950 or 3802 or
      1000 or 1001 or 1002 or 1003 or 1005 or 1007 or 1009 or 1015 or
      1016 or 1017 or 1018 or 1019 or 1020 or 1021 or 1022 or 1027 or
      1041 or 1115 or 1182 or 1183 or 1185 or 1187 or 1231 or 1270 or
      199 or 629 or 651 or 719 or 791 or 2951 or 3807);

  private static bool IsArrayType(uint typeId) =>
    typeId is
      1000 or 1001 or 1002 or 1003 or 1005 or 1007 or 1009 or 1015 or
      1016 or 1017 or 1018 or 1019 or 1020 or 1021 or 1022 or 1027 or
      1041 or 1115 or 1182 or 1183 or 1185 or 1187 or 1231 or 1270 or
      199 or 629 or 651 or 719 or 791 or 2951 or 3807;

  private static bool CanDecodeAsString(SqlColumn column) =>
    IsKnownFormat(column.Format) &&
    (column.Format == SqlDataFormat.Text &&
     IsStringType(column.TypeId) ||
     column.TypeId is 18 or 19 or 25 or 1042 or 1043);

  private static void EnsureType(
    SqlColumn column,
    uint expectedTypeId,
    Type requestedType)
  {
    if (column.TypeId != expectedTypeId ||
        !IsKnownFormat(column.Format))
    {
      throw CannotRead(column, requestedType);
    }
  }

  private static void EnsureStringType(
    SqlColumn column,
    Type requestedType)
  {
    if (!CanDecodeAsString(column))
    {
      throw CannotRead(column, requestedType);
    }
  }

  private static void EnsureJsonType(
    SqlColumn column,
    Type requestedType)
  {
    if (column.TypeId is not (114 or 3802) ||
        !IsKnownFormat(column.Format))
    {
      throw CannotRead(column, requestedType);
    }
  }

  private static void EnsureArrayType(
    SqlColumn column,
    Type requestedType)
  {
    if (!IsArrayType(column.TypeId) ||
        !IsKnownFormat(column.Format))
    {
      throw CannotRead(column, requestedType);
    }
  }

  private static void EnsureFormat(
    SqlColumn column,
    Type requestedType)
  {
    if (!IsKnownFormat(column.Format))
    {
      throw CannotRead(column, requestedType);
    }
  }

  private static bool IsKnownFormat(SqlDataFormat format) =>
    format is SqlDataFormat.Text or SqlDataFormat.Binary;

  private static InvalidCastException CannotRead(
    SqlColumn column,
    Type requestedType) =>
    new(
      $"PostgreSQL type OID {column.TypeId} in {column.Format} format " +
      $"cannot be read as {requestedType.FullName}.");

  private static void Ensure(
    ReadOnlySpan<byte> value,
    int position,
    int length)
  {
    if (length < 0 ||
        position < 0 ||
        position > value.Length - length)
    {
      throw new InvalidDataException(
        "PostgreSQL row is truncated.");
    }
  }

  private readonly record struct Field(
    ReadOnlyMemory<byte> Value,
    bool IsNull);
}
