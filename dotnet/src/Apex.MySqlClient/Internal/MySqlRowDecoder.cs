/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Apex.SqlClient;
using Apex.SqlClient.Internal;

namespace Apex.MySqlClient.Internal;

/// <summary>
/// Decodes the rows of one result set. An instance is bound to the column definitions and to
/// the protocol, text for COM_QUERY and binary for prepared statements.
/// </summary>
internal sealed class MySqlRowDecoder : ISqlRowDecoder
{
  private readonly Utf8StringCache _strings;
  private readonly MySqlZeroDateBehavior _zeroDates;
  private MySqlColumnMetadata[] _metadata = [];
  private SqlColumn[] _columns = [];
  private bool _binary;
  private int _nullBitmapLength;

  internal MySqlRowDecoder(Utf8StringCache strings, MySqlZeroDateBehavior zeroDates)
  {
    _strings = strings;
    _zeroDates = zeroDates;
  }

  internal SqlColumn[] Columns => _columns;

  internal bool IsBinary => _binary;

  internal int FieldCount => _metadata.Length;

  internal void SetColumns(MySqlColumnMetadata[] metadata, bool binary)
  {
    _metadata = metadata;
    _binary = binary;
    _nullBitmapLength = (metadata.Length + 9) / 8;
    SqlColumn[] columns = new SqlColumn[metadata.Length];
    for (int i = 0; i < columns.Length; i++)
    {
      columns[i] = MySqlColumnCodec.ToColumn(metadata[i], binary);
    }

    _columns = columns;
  }

  internal void ValidateRow(ReadOnlySpan<byte> row)
  {
    if (_binary)
    {
      if (row.Length < 1 + _nullBitmapLength || row[0] != MySqlProtocol.OkHeader)
      {
        throw new InvalidDataException("MySQL binary row header is invalid.");
      }

      ReadOnlySpan<byte> bitmap = row.Slice(1, _nullBitmapLength);
      MySqlPayloadReader reader = new(row[(1 + _nullBitmapLength)..]);
      for (int i = 0; i < _metadata.Length; i++)
      {
        if (!IsNullInBitmap(bitmap, i))
        {
          SkipBinaryValue(ref reader, _metadata[i].Type);
        }
      }

      if (reader.Remaining != 0)
      {
        throw new InvalidDataException("MySQL binary row has trailing data.");
      }

      return;
    }

    MySqlPayloadReader text = new(row);
    for (int i = 0; i < _metadata.Length; i++)
    {
      _ = text.ReadLengthEncodedSpan(out _);
    }

    if (text.Remaining != 0)
    {
      throw new InvalidDataException("MySQL text row has trailing data.");
    }
  }

  public int GetFieldCount(ReadOnlySpan<byte> row) => _metadata.Length;

  public bool IsNull(ReadOnlySpan<byte> row, int ordinal)
  {
    ReadOnlySpan<byte> value = GetField(row, ordinal, out bool isNull);
    return isNull ||
      (_zeroDates == MySqlZeroDateBehavior.Null &&
       IsZeroTemporal(value, _metadata[ordinal].Type));
  }

  public object? Decode(ReadOnlySpan<byte> row, int ordinal, SqlColumn column) =>
    DecodeObject(row, ordinal);

  public T Decode<T>(ReadOnlySpan<byte> row, int ordinal, SqlColumn column) =>
    Decode<T>(row, ordinal);

  internal object? DecodeObject(ReadOnlySpan<byte> row, int ordinal)
  {
    ReadOnlySpan<byte> value = GetField(row, ordinal, out bool isNull);
    if (isNull)
    {
      return null;
    }

    MySqlColumnMetadata metadata = _metadata[ordinal];
    bool unsigned = metadata.IsUnsigned;
    switch (metadata.Type)
    {
      case MySqlType.Tiny:
        return unsigned
          ? BoxedScalarCache.Box(checked((byte)ReadUInt64(value, metadata)))
          : BoxedScalarCache.Box(checked((sbyte)ReadInt64(value, metadata)));
      case MySqlType.Short:
        return unsigned
          ? BoxedScalarCache.Box(checked((ushort)ReadUInt64(value, metadata)))
          : BoxedScalarCache.Box(checked((short)ReadInt64(value, metadata)));
      case MySqlType.Year:
        return BoxedScalarCache.Box(checked((int)ReadInt64(value, metadata)));
      case MySqlType.Int24:
      case MySqlType.Long:
        return unsigned
          ? BoxedScalarCache.Box(checked((uint)ReadUInt64(value, metadata)))
          : BoxedScalarCache.Box(checked((int)ReadInt64(value, metadata)));
      case MySqlType.LongLong:
        return unsigned
          ? BoxedScalarCache.Box(ReadUInt64(value, metadata))
          : BoxedScalarCache.Box(ReadInt64(value, metadata));
      case MySqlType.Bit:
        return BoxedScalarCache.Box(MySqlValueCodec.ParseBit(value));
      case MySqlType.Float:
        return _binary
          ? BitConverter.Int32BitsToSingle(ReadBinaryInt32(value))
          : MySqlValueCodec.ParseSingle(value);
      case MySqlType.Double:
        return _binary
          ? BitConverter.Int64BitsToDouble(ReadBinaryInt64(value))
          : MySqlValueCodec.ParseDouble(value);
      case MySqlType.Decimal:
      case MySqlType.NewDecimal:
        return MySqlDecimal.Parse(_strings.GetString(value));
      case MySqlType.Date:
      case MySqlType.NewDate:
        return DecodeDateObject(value);
      case MySqlType.DateTime:
      case MySqlType.DateTime2:
      case MySqlType.Timestamp:
      case MySqlType.Timestamp2:
        return DecodeDateTimeObject(value);
      case MySqlType.Time:
      case MySqlType.Time2:
        return DecodeTime(value);
      case MySqlType.Json:
        return DecodeJson(value);
      case MySqlType.Null:
        return null;
      case MySqlType.Geometry:
      case MySqlType.Vector:
        return value.ToArray();
      default:
        return IsBinaryContent(metadata)
          ? value.ToArray()
          : _strings.GetString(value);
    }
  }

  internal T Decode<T>(ReadOnlySpan<byte> row, int ordinal)
  {
    ReadOnlySpan<byte> value = GetField(row, ordinal, out bool isNull);
    if (isNull)
    {
      return default(T) is null
        ? default!
        : throw new InvalidCastException($"Column {ordinal} contains NULL.");
    }

    MySqlColumnMetadata metadata = _metadata[ordinal];
    if (metadata.Type == MySqlType.Json)
    {
      return DecodeJsonValue<T>(value, ordinal);
    }

    if (typeof(T) == typeof(bool))
    {
      bool result = ReadInt64(value, metadata) != 0;
      return Unsafe.As<bool, T>(ref result);
    }

    if (typeof(T) == typeof(sbyte))
    {
      sbyte result = checked((sbyte)ReadInt64(value, metadata));
      return Unsafe.As<sbyte, T>(ref result);
    }

    if (typeof(T) == typeof(byte))
    {
      byte result = checked((byte)ReadUInt64(value, metadata));
      return Unsafe.As<byte, T>(ref result);
    }

    if (typeof(T) == typeof(short))
    {
      short result = checked((short)ReadInt64(value, metadata));
      return Unsafe.As<short, T>(ref result);
    }

    if (typeof(T) == typeof(ushort))
    {
      ushort result = checked((ushort)ReadUInt64(value, metadata));
      return Unsafe.As<ushort, T>(ref result);
    }

    if (typeof(T) == typeof(int))
    {
      int result = checked((int)ReadInt64(value, metadata));
      return Unsafe.As<int, T>(ref result);
    }

    if (typeof(T) == typeof(uint))
    {
      uint result = checked((uint)ReadUInt64(value, metadata));
      return Unsafe.As<uint, T>(ref result);
    }

    if (typeof(T) == typeof(long))
    {
      long result = ReadInt64(value, metadata);
      return Unsafe.As<long, T>(ref result);
    }

    if (typeof(T) == typeof(ulong))
    {
      ulong result = ReadUInt64(value, metadata);
      return Unsafe.As<ulong, T>(ref result);
    }

    if (typeof(T) == typeof(float))
    {
      float result = (float)ReadDouble(value, metadata);
      return Unsafe.As<float, T>(ref result);
    }

    if (typeof(T) == typeof(double))
    {
      double result = ReadDouble(value, metadata);
      return Unsafe.As<double, T>(ref result);
    }

    if (typeof(T) == typeof(decimal))
    {
      decimal result = ReadDecimal(value, metadata);
      return Unsafe.As<decimal, T>(ref result);
    }

    if (typeof(T) == typeof(MySqlDecimal))
    {
      if (metadata.Type is not (MySqlType.Decimal or MySqlType.NewDecimal))
      {
        throw new InvalidCastException(
          $"MySQL type 0x{(byte)metadata.Type:X2} cannot be read as a decimal.");
      }

      MySqlDecimal result = MySqlDecimal.Parse(_strings.GetString(value));
      return Unsafe.As<MySqlDecimal, T>(ref result);
    }

    if (typeof(T) == typeof(string))
    {
      string result = ReadString(value, metadata);
      return Unsafe.As<string, T>(ref result);
    }

    if (typeof(T) == typeof(byte[]))
    {
      byte[] result = ReadBytes(value, metadata);
      return Unsafe.As<byte[], T>(ref result);
    }

    if (typeof(T) == typeof(DateOnly))
    {
      DateOnly result = ReadDateOnly(value, metadata);
      return Unsafe.As<DateOnly, T>(ref result);
    }

    if (typeof(T) == typeof(TimeOnly))
    {
      TimeOnly result = ReadTimeOnly(value, metadata);
      return Unsafe.As<TimeOnly, T>(ref result);
    }

    if (typeof(T) == typeof(TimeSpan))
    {
      TimeSpan result = ReadTimeSpan(value, metadata);
      return Unsafe.As<TimeSpan, T>(ref result);
    }

    if (typeof(T) == typeof(DateTime))
    {
      DateTime result = ReadDateTime(value, metadata);
      return Unsafe.As<DateTime, T>(ref result);
    }

    if (typeof(T) == typeof(DateTimeOffset))
    {
      DateTimeOffset result = new(ReadDateTime(value, metadata), TimeSpan.Zero);
      return Unsafe.As<DateTimeOffset, T>(ref result);
    }

    if (typeof(T) == typeof(Guid))
    {
      Guid result = ReadGuid(value, metadata);
      return Unsafe.As<Guid, T>(ref result);
    }

    object? decoded = DecodeObject(row, ordinal);
    if (decoded is T typed)
    {
      return typed;
    }

    throw new InvalidCastException(
      $"Column {ordinal} contains {decoded?.GetType().FullName ?? "NULL"}, " +
      $"not {typeof(T).FullName}.");
  }

  /// <summary>Locates one field inside a row payload of the bound protocol.</summary>
  internal ReadOnlySpan<byte> GetField(ReadOnlySpan<byte> row, int ordinal, out bool isNull)
  {
    if ((uint)ordinal >= (uint)_metadata.Length)
    {
      throw new ArgumentOutOfRangeException(nameof(ordinal));
    }

    return _binary
      ? GetBinaryField(row, ordinal, out isNull)
      : GetTextField(row, ordinal, out isNull);
  }

  private ReadOnlySpan<byte> GetTextField(ReadOnlySpan<byte> row, int ordinal, out bool isNull)
  {
    MySqlPayloadReader reader = new(row);
    for (int i = 0; i < ordinal; i++)
    {
      _ = reader.ReadLengthEncodedSpan(out _);
    }

    return reader.ReadLengthEncodedSpan(out isNull);
  }

  private ReadOnlySpan<byte> GetBinaryField(ReadOnlySpan<byte> row, int ordinal, out bool isNull)
  {
    if (row.Length < 1 + _nullBitmapLength || row[0] != MySqlProtocol.OkHeader)
    {
      throw new InvalidDataException("MySQL binary row header is invalid.");
    }

    ReadOnlySpan<byte> bitmap = row.Slice(1, _nullBitmapLength);
    if (IsNullInBitmap(bitmap, ordinal))
    {
      isNull = true;
      return default;
    }

    isNull = false;
    MySqlPayloadReader reader = new(row[(1 + _nullBitmapLength)..]);
    for (int i = 0; i < ordinal; i++)
    {
      if (IsNullInBitmap(bitmap, i))
      {
        continue;
      }

      SkipBinaryValue(ref reader, _metadata[i].Type);
    }

    return ReadBinaryValue(ref reader, _metadata[ordinal].Type);
  }

  private static bool IsNullInBitmap(ReadOnlySpan<byte> bitmap, int ordinal)
  {
    int bit = ordinal + 2;
    return (bitmap[bit >> 3] & (1 << (bit & 7))) != 0;
  }

  private static void SkipBinaryValue(scoped ref MySqlPayloadReader reader, MySqlType type) =>
    _ = ReadBinaryValue(ref reader, type);

  private static ReadOnlySpan<byte> ReadBinaryValue(
    scoped ref MySqlPayloadReader reader,
    MySqlType type)
  {
    switch (type)
    {
      case MySqlType.Tiny:
        return reader.ReadSpan(1);
      case MySqlType.Short:
      case MySqlType.Year:
        return reader.ReadSpan(2);
      case MySqlType.Int24:
      case MySqlType.Long:
      case MySqlType.Float:
        return reader.ReadSpan(4);
      case MySqlType.LongLong:
      case MySqlType.Double:
        return reader.ReadSpan(8);
      case MySqlType.Date:
      case MySqlType.NewDate:
        {
          int length = reader.ReadByte();
          if (length is not (0 or 4))
          {
            throw new InvalidDataException($"Invalid MySQL binary DATE length {length}.");
          }

          return reader.ReadSpan(length);
        }
      case MySqlType.DateTime:
      case MySqlType.DateTime2:
      case MySqlType.Timestamp:
      case MySqlType.Timestamp2:
        {
          int length = reader.ReadByte();
          if (length is not (0 or 4 or 7 or 11))
          {
            throw new InvalidDataException($"Invalid MySQL binary DATETIME length {length}.");
          }

          return reader.ReadSpan(length);
        }
      case MySqlType.Time:
      case MySqlType.Time2:
        {
          int length = reader.ReadByte();
          if (length is not (0 or 8 or 12))
          {
            throw new InvalidDataException($"Invalid MySQL binary TIME length {length}.");
          }

          return reader.ReadSpan(length);
        }
      case MySqlType.Null:
        return default;
      default:
        return reader.ReadLengthEncodedSpan(out _);
    }
  }

  private object? HandleZeroDate(bool dateOnly) =>
    _zeroDates switch
    {
      MySqlZeroDateBehavior.Null => null,
      MySqlZeroDateBehavior.MinValue => dateOnly ? DateOnly.MinValue : DateTime.MinValue,
      _ => throw new FormatException(
        "MySQL returned a zero date. Set MySqlConnectOptions.ZeroDateBehavior to read it."),
    };

  private object? DecodeDateObject(ReadOnlySpan<byte> value)
  {
    DateOnly? date = DecodeDate(value, out _);
    return date is { } parsed ? parsed : HandleZeroDate(dateOnly: true);
  }

  private object? DecodeDateTimeObject(ReadOnlySpan<byte> value)
  {
    DateTime? timestamp = DecodeDateTime(value, out _);
    return timestamp is { } parsed ? parsed : HandleZeroDate(dateOnly: false);
  }

  private DateOnly ZeroDateOnly() =>
    HandleZeroDate(dateOnly: true) is DateOnly date
      ? date
      : throw new InvalidCastException("The column contains a zero date, which maps to NULL.");

  private DateTime ZeroDateTime() =>
    HandleZeroDate(dateOnly: false) is DateTime timestamp
      ? timestamp
      : throw new InvalidCastException("The column contains a zero date, which maps to NULL.");

  private DateOnly? DecodeDate(ReadOnlySpan<byte> value, out bool isZero)
  {
    if (_binary)
    {
      DateTime timestamp = MySqlValueCodec.ReadBinaryDateTime(value, out isZero);
      return isZero ? null : DateOnly.FromDateTime(timestamp);
    }

    DateOnly date = MySqlValueCodec.ParseDate(value, out isZero);
    return isZero ? null : date;
  }

  private DateTime? DecodeDateTime(ReadOnlySpan<byte> value, out bool isZero)
  {
    DateTime timestamp = _binary
      ? MySqlValueCodec.ReadBinaryDateTime(value, out isZero)
      : MySqlValueCodec.ParseDateTime(value, out isZero);
    return isZero ? null : timestamp;
  }

  private TimeSpan DecodeTime(ReadOnlySpan<byte> value) =>
    _binary ? MySqlValueCodec.ReadBinaryTime(value) : MySqlValueCodec.ParseTime(value);

  private long ReadInt64(ReadOnlySpan<byte> value, MySqlColumnMetadata metadata)
  {
    switch (metadata.Type)
    {
      case MySqlType.Tiny:
        return _binary
          ? metadata.IsUnsigned ? value[0] : (sbyte)value[0]
          : ParseInteger(value, metadata);
      case MySqlType.Short:
      case MySqlType.Year:
        return _binary
          ? metadata.IsUnsigned
            ? BinaryPrimitives.ReadUInt16LittleEndian(value)
            : BinaryPrimitives.ReadInt16LittleEndian(value)
          : ParseInteger(value, metadata);
      case MySqlType.Int24:
      case MySqlType.Long:
        return _binary
          ? metadata.IsUnsigned
            ? BinaryPrimitives.ReadUInt32LittleEndian(value)
            : BinaryPrimitives.ReadInt32LittleEndian(value)
          : ParseInteger(value, metadata);
      case MySqlType.LongLong:
        if (!_binary)
        {
          return ParseInteger(value, metadata);
        }

        if (!metadata.IsUnsigned)
        {
          return BinaryPrimitives.ReadInt64LittleEndian(value);
        }

        ulong unsigned = BinaryPrimitives.ReadUInt64LittleEndian(value);
        return unsigned <= long.MaxValue
          ? (long)unsigned
          : throw new OverflowException(
            $"MySQL unsigned value {unsigned} does not fit in System.Int64.");
      case MySqlType.Bit:
        ulong bits = MySqlValueCodec.ParseBit(value);
        return bits <= long.MaxValue
          ? (long)bits
          : throw new OverflowException($"MySQL BIT value {bits} does not fit in System.Int64.");
      case MySqlType.Decimal:
      case MySqlType.NewDecimal:
        return checked((long)MySqlValueCodec.ParseDecimal(value));
      default:
        throw new InvalidCastException(
          $"MySQL type 0x{(byte)metadata.Type:X2} cannot be read as an integer.");
    }
  }

  private ulong ReadUInt64(ReadOnlySpan<byte> value, MySqlColumnMetadata metadata)
  {
    if (metadata.Type is MySqlType.Bit)
    {
      return MySqlValueCodec.ParseBit(value);
    }

    if (metadata.Type is MySqlType.LongLong && metadata.IsUnsigned)
    {
      return _binary
        ? BinaryPrimitives.ReadUInt64LittleEndian(value)
        : MySqlValueCodec.ParseUInt64(value);
    }

    long signed = ReadInt64(value, metadata);
    return signed >= 0
      ? (ulong)signed
      : throw new OverflowException(
        $"MySQL value {signed} cannot be read as an unsigned integer.");
  }

  private long ParseInteger(ReadOnlySpan<byte> value, MySqlColumnMetadata metadata)
  {
    if (!metadata.IsUnsigned)
    {
      return MySqlValueCodec.ParseInt64(value);
    }

    ulong unsigned = MySqlValueCodec.ParseUInt64(value);
    return unsigned <= long.MaxValue
      ? (long)unsigned
      : throw new OverflowException(
        $"MySQL unsigned value {unsigned} does not fit in System.Int64.");
  }

  private double ReadDouble(ReadOnlySpan<byte> value, MySqlColumnMetadata metadata) =>
    metadata.Type switch
    {
      MySqlType.Float => _binary
        ? BitConverter.Int32BitsToSingle(ReadBinaryInt32(value))
        : MySqlValueCodec.ParseSingle(value),
      MySqlType.Double => _binary
        ? BitConverter.Int64BitsToDouble(ReadBinaryInt64(value))
        : MySqlValueCodec.ParseDouble(value),
      MySqlType.Decimal or MySqlType.NewDecimal => (double)MySqlValueCodec.ParseDecimal(value),
      _ => metadata.IsUnsigned ? ReadUInt64(value, metadata) : ReadInt64(value, metadata),
    };

  private decimal ReadDecimal(ReadOnlySpan<byte> value, MySqlColumnMetadata metadata) =>
    metadata.Type switch
    {
      MySqlType.Decimal or MySqlType.NewDecimal => MySqlValueCodec.ParseDecimal(value),
      MySqlType.Float or MySqlType.Double => (decimal)ReadDouble(value, metadata),
      _ => metadata.IsUnsigned ? ReadUInt64(value, metadata) : ReadInt64(value, metadata),
    };

  private string ReadString(ReadOnlySpan<byte> value, MySqlColumnMetadata metadata)
  {
    if (IsBinaryContent(metadata))
    {
      throw new InvalidCastException(
        $"MySQL type 0x{(byte)metadata.Type:X2} holds binary data and cannot be read as a string.");
    }

    if (!_binary || IsTextEncoded(metadata.Type))
    {
      return _strings.GetString(value);
    }

    return metadata.Type switch
    {
      MySqlType.Tiny or MySqlType.Short or MySqlType.Year or MySqlType.Int24 or
        MySqlType.Long or MySqlType.LongLong =>
        metadata.IsUnsigned
          ? ReadUInt64(value, metadata).ToString(CultureInfo.InvariantCulture)
          : ReadInt64(value, metadata).ToString(CultureInfo.InvariantCulture),
      MySqlType.Float or MySqlType.Double =>
        ReadDouble(value, metadata).ToString(CultureInfo.InvariantCulture),
      MySqlType.Bit => MySqlValueCodec.ParseBit(value).ToString(CultureInfo.InvariantCulture),
      MySqlType.Date or MySqlType.NewDate =>
        ReadDateOnly(value, metadata).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
      MySqlType.DateTime or MySqlType.DateTime2 or MySqlType.Timestamp or MySqlType.Timestamp2 =>
        ReadDateTime(value, metadata)
          .ToString("yyyy-MM-dd HH:mm:ss.FFFFFF", CultureInfo.InvariantCulture),
      MySqlType.Time or MySqlType.Time2 =>
        DecodeTime(value).ToString("c", CultureInfo.InvariantCulture),
      _ => _strings.GetString(value),
    };
  }

  private byte[] ReadBytes(ReadOnlySpan<byte> value, MySqlColumnMetadata metadata)
  {
    if (!_binary || IsTextEncoded(metadata.Type) ||
        metadata.Type is MySqlType.Bit or MySqlType.Geometry or MySqlType.Vector)
    {
      return value.ToArray();
    }

    throw new InvalidCastException(
      $"MySQL type 0x{(byte)metadata.Type:X2} cannot be read as a byte array.");
  }

  private DateOnly ReadDateOnly(ReadOnlySpan<byte> value, MySqlColumnMetadata metadata)
  {
    switch (metadata.Type)
    {
      case MySqlType.Date:
      case MySqlType.NewDate:
        return DecodeDate(value, out _) ?? ZeroDateOnly();
      case MySqlType.DateTime:
      case MySqlType.DateTime2:
      case MySqlType.Timestamp:
      case MySqlType.Timestamp2:
        return DateOnly.FromDateTime(ReadDateTime(value, metadata));
      default:
        throw new InvalidCastException(
          $"MySQL type 0x{(byte)metadata.Type:X2} cannot be read as a date.");
    }
  }

  private DateTime ReadDateTime(ReadOnlySpan<byte> value, MySqlColumnMetadata metadata)
  {
    switch (metadata.Type)
    {
      case MySqlType.Date:
      case MySqlType.NewDate:
        return DecodeDate(value, out _) is { } date
          ? date.ToDateTime(default, DateTimeKind.Unspecified)
          : ZeroDateTime();
      case MySqlType.DateTime:
      case MySqlType.DateTime2:
      case MySqlType.Timestamp:
      case MySqlType.Timestamp2:
        return DecodeDateTime(value, out _) ?? ZeroDateTime();
      default:
        throw new InvalidCastException(
          $"MySQL type 0x{(byte)metadata.Type:X2} cannot be read as a date and time.");
    }
  }

  private TimeSpan ReadTimeSpan(ReadOnlySpan<byte> value, MySqlColumnMetadata metadata) =>
    metadata.Type is MySqlType.Time or MySqlType.Time2
      ? DecodeTime(value)
      : throw new InvalidCastException(
        $"MySQL type 0x{(byte)metadata.Type:X2} cannot be read as a duration.");

  private TimeOnly ReadTimeOnly(ReadOnlySpan<byte> value, MySqlColumnMetadata metadata)
  {
    if (metadata.Type is MySqlType.DateTime or MySqlType.DateTime2 or
        MySqlType.Timestamp or MySqlType.Timestamp2)
    {
      return TimeOnly.FromDateTime(ReadDateTime(value, metadata));
    }

    TimeSpan time = ReadTimeSpan(value, metadata);
    return time >= TimeSpan.Zero && time < TimeSpan.FromDays(1)
      ? TimeOnly.FromTimeSpan(time)
      : throw new InvalidCastException(
        $"MySQL TIME value {time} is outside a single day and cannot be read as a time of day.");
  }

  private Guid ReadGuid(ReadOnlySpan<byte> value, MySqlColumnMetadata metadata)
  {
    if (IsBinaryContent(metadata) && value.Length == 16)
    {
      return new Guid(value, bigEndian: true);
    }

    Span<char> characters = stackalloc char[36];
    if (value.Length is 32 or 36 or 38)
    {
      int length = 0;
      foreach (byte item in value)
      {
        if (length == characters.Length)
        {
          break;
        }

        characters[length++] = (char)item;
      }

      if (Guid.TryParse(characters[..length], out Guid parsed))
      {
        return parsed;
      }
    }

    throw new InvalidCastException(
      $"MySQL type 0x{(byte)metadata.Type:X2} cannot be read as a GUID.");
  }

  private static JsonElement DecodeJson(ReadOnlySpan<byte> value)
  {
    using JsonDocument document = JsonDocument.Parse(value.ToArray());
    return document.RootElement.Clone();
  }

  private static T DecodeJsonValue<T>(ReadOnlySpan<byte> value, int ordinal)
  {
    JsonElement json = DecodeJson(value);
    object decoded;
    if (typeof(T) == typeof(JsonElement) || typeof(T) == typeof(object))
    {
      decoded = json;
    }
    else if (typeof(T) == typeof(bool))
    {
      decoded = json.ValueKind is JsonValueKind.True or JsonValueKind.False
        ? json.GetBoolean()
        : throw JsonCastException<T>(ordinal);
    }
    else if (typeof(T) == typeof(int))
    {
      decoded = json.TryGetInt32(out int parsed)
        ? parsed
        : throw JsonCastException<T>(ordinal);
    }
    else if (typeof(T) == typeof(long))
    {
      decoded = json.TryGetInt64(out long parsed)
        ? parsed
        : throw JsonCastException<T>(ordinal);
    }
    else if (typeof(T) == typeof(float))
    {
      decoded = json.TryGetSingle(out float parsed)
        ? parsed
        : throw JsonCastException<T>(ordinal);
    }
    else if (typeof(T) == typeof(double))
    {
      decoded = json.TryGetDouble(out double parsed)
        ? parsed
        : throw JsonCastException<T>(ordinal);
    }
    else if (typeof(T) == typeof(decimal))
    {
      decoded = json.TryGetDecimal(out decimal parsed)
        ? parsed
        : throw JsonCastException<T>(ordinal);
    }
    else if (typeof(T) == typeof(string))
    {
      decoded = json.ValueKind == JsonValueKind.String
        ? json.GetString()!
        : json.GetRawText();
    }
    else
    {
      throw JsonCastException<T>(ordinal);
    }

    return (T)decoded;
  }

  private static InvalidCastException JsonCastException<T>(int ordinal) =>
    new($"MySQL JSON column {ordinal} cannot be read as {typeof(T).FullName}.");

  private bool IsZeroTemporal(ReadOnlySpan<byte> value, MySqlType type)
  {
    if (type is not (
      MySqlType.Date or MySqlType.NewDate or MySqlType.DateTime or MySqlType.DateTime2 or
      MySqlType.Timestamp or MySqlType.Timestamp2))
    {
      return false;
    }

    return _binary
      ? value.IsEmpty ||
        value.Length >= 4 &&
        value[0] == 0 &&
        value[1] == 0 &&
        value[2] == 0 &&
        value[3] == 0
      : value.StartsWith("0000-00-00"u8);
  }

  private static int ReadBinaryInt32(ReadOnlySpan<byte> value) =>
    BinaryPrimitives.ReadInt32LittleEndian(value);

  private static long ReadBinaryInt64(ReadOnlySpan<byte> value) =>
    BinaryPrimitives.ReadInt64LittleEndian(value);

  private static bool IsTextEncoded(MySqlType type) =>
    type is MySqlType.VarChar or MySqlType.VarString or MySqlType.String or
      MySqlType.Enum or MySqlType.Set or MySqlType.Json or
      MySqlType.TinyBlob or MySqlType.Blob or MySqlType.MediumBlob or MySqlType.LongBlob or
      MySqlType.Decimal or MySqlType.NewDecimal;

  private static bool IsBinaryContent(MySqlColumnMetadata metadata) =>
    metadata.Type is MySqlType.Geometry or MySqlType.Vector ||
    (metadata.IsBinary && metadata.Type is not (
      MySqlType.Decimal or MySqlType.NewDecimal or MySqlType.Json));
}
