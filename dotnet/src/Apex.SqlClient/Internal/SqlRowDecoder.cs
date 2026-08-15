/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Apex.SqlClient.Internal;

internal interface ISqlRowDecoder
{
  int GetFieldCount(ReadOnlyMemory<byte> row);

  bool IsNull(ReadOnlyMemory<byte> row, int ordinal);

  object? DecodeObject(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  bool DecodeBoolean(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  bool? DecodeNullableBoolean(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  short DecodeInt16(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  short? DecodeNullableInt16(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  int DecodeInt32(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  int? DecodeNullableInt32(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  long DecodeInt64(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  long? DecodeNullableInt64(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  float DecodeFloat(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  float? DecodeNullableFloat(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  double DecodeDouble(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  double? DecodeNullableDouble(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  decimal DecodeDecimal(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  decimal? DecodeNullableDecimal(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  string? DecodeString(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  byte[]? DecodeBytes(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  ReadOnlyMemory<byte> DecodeReadOnlyMemory(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  ReadOnlyMemory<byte>? DecodeNullableReadOnlyMemory(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  Guid DecodeGuid(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  Guid? DecodeNullableGuid(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  DateOnly DecodeDateOnly(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  DateOnly? DecodeNullableDateOnly(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  TimeOnly DecodeTimeOnly(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  TimeOnly? DecodeNullableTimeOnly(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  DateTime DecodeDateTime(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  DateTime? DecodeNullableDateTime(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  DateTimeOffset DecodeDateTimeOffset(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  DateTimeOffset? DecodeNullableDateTimeOffset(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  JsonElement DecodeJsonElement(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  JsonElement? DecodeNullableJsonElement(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  object?[]? DecodeArray(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);

  T DecodeProviderSpecific<T>(
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column);
}

internal static class SqlRowDecoder
{
  internal static T Decode<T>(
    ISqlRowDecoder decoder,
    ReadOnlyMemory<byte> row,
    int ordinal,
    SqlColumn column,
    bool copyReadOnlyMemory = false)
  {
    if (typeof(T) == typeof(int))
    {
      int value = decoder.DecodeInt32(row, ordinal, column);
      return Unsafe.As<int, T>(ref value);
    }

    if (typeof(T) == typeof(string))
    {
      string? value = decoder.DecodeString(row, ordinal, column);
      return Unsafe.As<string?, T>(ref value);
    }

    if (typeof(T) == typeof(long))
    {
      long value = decoder.DecodeInt64(row, ordinal, column);
      return Unsafe.As<long, T>(ref value);
    }

    if (typeof(T) == typeof(bool))
    {
      bool value = decoder.DecodeBoolean(row, ordinal, column);
      return Unsafe.As<bool, T>(ref value);
    }

    if (typeof(T) == typeof(short))
    {
      short value = decoder.DecodeInt16(row, ordinal, column);
      return Unsafe.As<short, T>(ref value);
    }

    if (typeof(T) == typeof(float))
    {
      float value = decoder.DecodeFloat(row, ordinal, column);
      return Unsafe.As<float, T>(ref value);
    }

    if (typeof(T) == typeof(double))
    {
      double value = decoder.DecodeDouble(row, ordinal, column);
      return Unsafe.As<double, T>(ref value);
    }

    if (typeof(T) == typeof(decimal))
    {
      decimal value = decoder.DecodeDecimal(row, ordinal, column);
      return Unsafe.As<decimal, T>(ref value);
    }

    if (typeof(T) == typeof(Guid))
    {
      Guid value = decoder.DecodeGuid(row, ordinal, column);
      return Unsafe.As<Guid, T>(ref value);
    }

    if (typeof(T) == typeof(DateOnly))
    {
      DateOnly value = decoder.DecodeDateOnly(row, ordinal, column);
      return Unsafe.As<DateOnly, T>(ref value);
    }

    if (typeof(T) == typeof(TimeOnly))
    {
      TimeOnly value = decoder.DecodeTimeOnly(row, ordinal, column);
      return Unsafe.As<TimeOnly, T>(ref value);
    }

    if (typeof(T) == typeof(DateTime))
    {
      DateTime value = decoder.DecodeDateTime(row, ordinal, column);
      return Unsafe.As<DateTime, T>(ref value);
    }

    if (typeof(T) == typeof(DateTimeOffset))
    {
      DateTimeOffset value = decoder.DecodeDateTimeOffset(row, ordinal, column);
      return Unsafe.As<DateTimeOffset, T>(ref value);
    }

    if (typeof(T) == typeof(byte[]))
    {
      byte[]? value = decoder.DecodeBytes(row, ordinal, column);
      return Unsafe.As<byte[]?, T>(ref value);
    }

    if (typeof(T) == typeof(ReadOnlyMemory<byte>))
    {
      ReadOnlyMemory<byte> value =
        decoder.DecodeReadOnlyMemory(row, ordinal, column);
      if (copyReadOnlyMemory)
      {
        value = value.ToArray();
      }

      return Unsafe.As<ReadOnlyMemory<byte>, T>(ref value);
    }

    if (typeof(T) == typeof(JsonElement))
    {
      JsonElement value = decoder.DecodeJsonElement(row, ordinal, column);
      return Unsafe.As<JsonElement, T>(ref value);
    }

    if (typeof(T) == typeof(object?[]))
    {
      object?[]? value = decoder.DecodeArray(row, ordinal, column);
      return Unsafe.As<object?[]?, T>(ref value);
    }

    if (typeof(T) == typeof(object))
    {
      return (T)decoder.DecodeObject(row, ordinal, column)!;
    }

    if (typeof(T) == typeof(int?))
    {
      int? value = decoder.DecodeNullableInt32(row, ordinal, column);
      return Unsafe.As<int?, T>(ref value);
    }

    if (typeof(T) == typeof(long?))
    {
      long? value = decoder.DecodeNullableInt64(row, ordinal, column);
      return Unsafe.As<long?, T>(ref value);
    }

    if (typeof(T) == typeof(bool?))
    {
      bool? value = decoder.DecodeNullableBoolean(row, ordinal, column);
      return Unsafe.As<bool?, T>(ref value);
    }

    if (typeof(T) == typeof(short?))
    {
      short? value = decoder.DecodeNullableInt16(row, ordinal, column);
      return Unsafe.As<short?, T>(ref value);
    }

    if (typeof(T) == typeof(float?))
    {
      float? value = decoder.DecodeNullableFloat(row, ordinal, column);
      return Unsafe.As<float?, T>(ref value);
    }

    if (typeof(T) == typeof(double?))
    {
      double? value = decoder.DecodeNullableDouble(row, ordinal, column);
      return Unsafe.As<double?, T>(ref value);
    }

    if (typeof(T) == typeof(decimal?))
    {
      decimal? value = decoder.DecodeNullableDecimal(row, ordinal, column);
      return Unsafe.As<decimal?, T>(ref value);
    }

    if (typeof(T) == typeof(Guid?))
    {
      Guid? value = decoder.DecodeNullableGuid(row, ordinal, column);
      return Unsafe.As<Guid?, T>(ref value);
    }

    if (typeof(T) == typeof(DateOnly?))
    {
      DateOnly? value = decoder.DecodeNullableDateOnly(row, ordinal, column);
      return Unsafe.As<DateOnly?, T>(ref value);
    }

    if (typeof(T) == typeof(TimeOnly?))
    {
      TimeOnly? value = decoder.DecodeNullableTimeOnly(row, ordinal, column);
      return Unsafe.As<TimeOnly?, T>(ref value);
    }

    if (typeof(T) == typeof(DateTime?))
    {
      DateTime? value = decoder.DecodeNullableDateTime(row, ordinal, column);
      return Unsafe.As<DateTime?, T>(ref value);
    }

    if (typeof(T) == typeof(DateTimeOffset?))
    {
      DateTimeOffset? value = decoder.DecodeNullableDateTimeOffset(row, ordinal, column);
      return Unsafe.As<DateTimeOffset?, T>(ref value);
    }

    if (typeof(T) == typeof(ReadOnlyMemory<byte>?))
    {
      ReadOnlyMemory<byte>? value;
      value = decoder.DecodeNullableReadOnlyMemory(
        row,
        ordinal,
        column);
      if (copyReadOnlyMemory && value.HasValue)
      {
        value = value.Value.ToArray();
      }

      return Unsafe.As<ReadOnlyMemory<byte>?, T>(ref value);
    }

    if (typeof(T) == typeof(JsonElement?))
    {
      JsonElement? value = decoder.DecodeNullableJsonElement(row, ordinal, column);
      return Unsafe.As<JsonElement?, T>(ref value);
    }

    return decoder.DecodeProviderSpecific<T>(row, ordinal, column);
  }
}
