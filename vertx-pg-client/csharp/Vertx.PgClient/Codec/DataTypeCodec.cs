// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Vertx.PgClient.Data;

namespace Vertx.PgClient.Codec;

/// <summary>
/// Encodes and decodes PostgreSQL data types.
/// </summary>
public static class DataTypeCodec
{
    private static readonly DateOnly LocalDateEpoch = new(2000, 1, 1);
    private static readonly DateTime LocalDateTimeEpoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
    private static readonly Encoding Utf8 = Encoding.UTF8;

    #region Binary Decode

    public static object? DecodeBinary(DataType dataType, ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return null;

        return dataType.Id switch
        {
            DataTypeId.Bool => DecodeBool(buffer),
            DataTypeId.Int2 => DecodeInt16(buffer),
            DataTypeId.Int4 => DecodeInt32(buffer),
            DataTypeId.Int8 => DecodeInt64(buffer),
            DataTypeId.Float4 => DecodeFloat(buffer),
            DataTypeId.Float8 => DecodeDouble(buffer),
            DataTypeId.Char or DataTypeId.Varchar or DataTypeId.Bpchar or DataTypeId.Text or DataTypeId.Name => DecodeString(buffer),
            DataTypeId.Date => DecodeDate(buffer),
            DataTypeId.Time => DecodeTime(buffer),
            DataTypeId.Timetz => DecodeTimeTz(buffer),
            DataTypeId.Timestamp => DecodeTimestamp(buffer),
            DataTypeId.Timestamptz => DecodeTimestampTz(buffer),
            DataTypeId.Bytea => DecodeByteArray(buffer),
            DataTypeId.Uuid => DecodeGuid(buffer),
            DataTypeId.Json or DataTypeId.Jsonb => DecodeJson(buffer),
            DataTypeId.Point => DecodePoint(buffer),
            DataTypeId.Line => DecodeLine(buffer),
            DataTypeId.Lseg => DecodeLineSegment(buffer),
            DataTypeId.Box => DecodeBox(buffer),
            DataTypeId.Circle => DecodeCircle(buffer),
            DataTypeId.Interval => DecodeInterval(buffer),
            DataTypeId.Inet => DecodeInet(buffer),
            DataTypeId.Money => DecodeMoney(buffer),
            DataTypeId.Numeric => DecodeNumeric(buffer),
            _ => DecodeString(buffer) // Unknown types decode as string
        };
    }

    private static bool DecodeBool(ReadOnlySpan<byte> buffer) => buffer[0] != 0;

    private static short DecodeInt16(ReadOnlySpan<byte> buffer) => BinaryPrimitives.ReadInt16BigEndian(buffer);

    private static int DecodeInt32(ReadOnlySpan<byte> buffer) => BinaryPrimitives.ReadInt32BigEndian(buffer);

    private static long DecodeInt64(ReadOnlySpan<byte> buffer) => BinaryPrimitives.ReadInt64BigEndian(buffer);

    private static float DecodeFloat(ReadOnlySpan<byte> buffer)
    {
        int intBits = BinaryPrimitives.ReadInt32BigEndian(buffer);
        return BitConverter.Int32BitsToSingle(intBits);
    }

    private static double DecodeDouble(ReadOnlySpan<byte> buffer)
    {
        long longBits = BinaryPrimitives.ReadInt64BigEndian(buffer);
        return BitConverter.Int64BitsToDouble(longBits);
    }

    private static string DecodeString(ReadOnlySpan<byte> buffer) => Utf8.GetString(buffer);

    private static DateOnly DecodeDate(ReadOnlySpan<byte> buffer)
    {
        int days = BinaryPrimitives.ReadInt32BigEndian(buffer);
        return LocalDateEpoch.AddDays(days);
    }

    private static TimeOnly DecodeTime(ReadOnlySpan<byte> buffer)
    {
        long micros = BinaryPrimitives.ReadInt64BigEndian(buffer);
        return TimeOnly.FromTimeSpan(TimeSpan.FromTicks(micros * 10));
    }

    private static DateTimeOffset DecodeTimeTz(ReadOnlySpan<byte> buffer)
    {
        long micros = BinaryPrimitives.ReadInt64BigEndian(buffer);
        int offsetSeconds = BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(8));
        var time = TimeSpan.FromTicks(micros * 10);
        // PostgreSQL stores offset as seconds from UTC (negated)
        var offset = TimeSpan.FromSeconds(-offsetSeconds);
        return new DateTimeOffset(DateTime.Today.Add(time), offset);
    }

    private static DateTime DecodeTimestamp(ReadOnlySpan<byte> buffer)
    {
        long micros = BinaryPrimitives.ReadInt64BigEndian(buffer);
        return LocalDateTimeEpoch.AddTicks(micros * 10);
    }

    private static DateTimeOffset DecodeTimestampTz(ReadOnlySpan<byte> buffer)
    {
        long micros = BinaryPrimitives.ReadInt64BigEndian(buffer);
        return new DateTimeOffset(LocalDateTimeEpoch.AddTicks(micros * 10), TimeSpan.Zero);
    }

    private static byte[] DecodeByteArray(ReadOnlySpan<byte> buffer) => buffer.ToArray();

    private static Guid DecodeGuid(ReadOnlySpan<byte> buffer)
    {
        // PostgreSQL sends UUID in network byte order (big-endian)
        // .NET Guid expects mixed-endian format
        Span<byte> bytes = stackalloc byte[16];
        buffer.CopyTo(bytes);
        
        // Swap bytes for the first three components (little-endian in .NET)
        // time_low (4 bytes)
        (bytes[0], bytes[3]) = (bytes[3], bytes[0]);
        (bytes[1], bytes[2]) = (bytes[2], bytes[1]);
        // time_mid (2 bytes)
        (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
        // time_hi_and_version (2 bytes)
        (bytes[6], bytes[7]) = (bytes[7], bytes[6]);
        
        return new Guid(bytes);
    }

    private static string DecodeJson(ReadOnlySpan<byte> buffer)
    {
        // JSONB has a version byte prefix
        if (buffer.Length > 0 && buffer[0] == 1)
        {
            return Utf8.GetString(buffer.Slice(1));
        }
        return Utf8.GetString(buffer);
    }

    private static Point DecodePoint(ReadOnlySpan<byte> buffer)
    {
        double x = DecodeDouble(buffer);
        double y = DecodeDouble(buffer.Slice(8));
        return new Point(x, y);
    }

    private static Line DecodeLine(ReadOnlySpan<byte> buffer)
    {
        double a = DecodeDouble(buffer);
        double b = DecodeDouble(buffer.Slice(8));
        double c = DecodeDouble(buffer.Slice(16));
        return new Line(a, b, c);
    }

    private static LineSegment DecodeLineSegment(ReadOnlySpan<byte> buffer)
    {
        var p1 = DecodePoint(buffer);
        var p2 = DecodePoint(buffer.Slice(16));
        return new LineSegment(p1, p2);
    }

    private static Box DecodeBox(ReadOnlySpan<byte> buffer)
    {
        var upperRight = DecodePoint(buffer);
        var lowerLeft = DecodePoint(buffer.Slice(16));
        return new Box(upperRight, lowerLeft);
    }

    private static Circle DecodeCircle(ReadOnlySpan<byte> buffer)
    {
        var center = DecodePoint(buffer);
        double radius = DecodeDouble(buffer.Slice(16));
        return new Circle(center, radius);
    }

    private static Interval DecodeInterval(ReadOnlySpan<byte> buffer)
    {
        long micros = BinaryPrimitives.ReadInt64BigEndian(buffer);
        int days = BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(8));
        int months = BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(12));

        int years = months / 12;
        months %= 12;
        
        int seconds = (int)(micros / 1_000_000);
        int microseconds = (int)(micros % 1_000_000);
        int hours = seconds / 3600;
        seconds %= 3600;
        int minutes = seconds / 60;
        seconds %= 60;

        return new Interval(years, months, days, hours, minutes, seconds, microseconds);
    }

    private static Inet DecodeInet(ReadOnlySpan<byte> buffer)
    {
        // Format: family (1 byte), netmask (1 byte), is_cidr (1 byte), address length (1 byte), address
        byte family = buffer[0];
        byte netmask = buffer[1];
        // byte isCidr = buffer[2];
        byte addrLen = buffer[3];
        
        var addressBytes = buffer.Slice(4, addrLen).ToArray();
        var address = new IPAddress(addressBytes);
        
        return new Inet().SetAddress(address).SetNetmask(netmask);
    }

    private static Money DecodeMoney(ReadOnlySpan<byte> buffer)
    {
        long cents = BinaryPrimitives.ReadInt64BigEndian(buffer);
        return new Money(cents / 100m);
    }

    private static decimal DecodeNumeric(ReadOnlySpan<byte> buffer)
    {
        // PostgreSQL numeric format:
        // ndigits (2), weight (2), sign (2), dscale (2), digits (2 * ndigits)
        int ndigits = BinaryPrimitives.ReadInt16BigEndian(buffer);
        int weight = BinaryPrimitives.ReadInt16BigEndian(buffer.Slice(2));
        int sign = BinaryPrimitives.ReadInt16BigEndian(buffer.Slice(4));
        int dscale = BinaryPrimitives.ReadInt16BigEndian(buffer.Slice(6));

        if (ndigits == 0)
        {
            return 0m;
        }

        // Each digit is base-10000
        const int nbase = 10000;
        decimal result = 0;
        int offset = 8;
        
        for (int i = 0; i < ndigits; i++)
        {
            short digit = BinaryPrimitives.ReadInt16BigEndian(buffer.Slice(offset));
            result = result * nbase + digit;
            offset += 2;
        }

        // Apply weight (each unit of weight = 4 decimal digits)
        int exponent = (weight + 1 - ndigits) * 4;
        if (exponent > 0)
        {
            for (int i = 0; i < exponent; i++)
            {
                result *= 10;
            }
        }
        else if (exponent < 0)
        {
            for (int i = 0; i < -exponent; i++)
            {
                result /= 10;
            }
        }

        // Apply sign (0 = positive, 0x4000 = negative, 0xC000 = NaN)
        if (sign == 0x4000)
        {
            result = -result;
        }

        return result;
    }

    #endregion

    #region Binary Encode

    public static void EncodeBinary(DataType dataType, object? value, Span<byte> buffer, out int bytesWritten)
    {
        if (value is null)
        {
            bytesWritten = 0;
            return;
        }

        bytesWritten = dataType.Id switch
        {
            DataTypeId.Bool => EncodeBool((bool)value, buffer),
            DataTypeId.Int2 => EncodeInt16(Convert.ToInt16(value), buffer),
            DataTypeId.Int4 => EncodeInt32(Convert.ToInt32(value), buffer),
            DataTypeId.Int8 => EncodeInt64(Convert.ToInt64(value), buffer),
            DataTypeId.Float4 => EncodeFloat(Convert.ToSingle(value), buffer),
            DataTypeId.Float8 => EncodeDouble(Convert.ToDouble(value), buffer),
            DataTypeId.Char or DataTypeId.Varchar or DataTypeId.Bpchar or DataTypeId.Text or DataTypeId.Name => 
                EncodeString((string)value, buffer),
            DataTypeId.Date => EncodeDate((DateOnly)value, buffer),
            DataTypeId.Time => EncodeTime((TimeOnly)value, buffer),
            DataTypeId.Timestamp => EncodeTimestamp((DateTime)value, buffer),
            DataTypeId.Timestamptz => EncodeTimestampTz((DateTimeOffset)value, buffer),
            DataTypeId.Bytea => EncodeByteArray((byte[])value, buffer),
            DataTypeId.Uuid => EncodeGuid((Guid)value, buffer),
            DataTypeId.Json or DataTypeId.Jsonb => EncodeString((string)value, buffer),
            DataTypeId.Point => EncodePoint((Point)value, buffer),
            DataTypeId.Interval => EncodeInterval((Interval)value, buffer),
            _ => EncodeString(value.ToString() ?? "", buffer)
        };
    }

    private static int EncodeBool(bool value, Span<byte> buffer)
    {
        buffer[0] = value ? (byte)1 : (byte)0;
        return 1;
    }

    private static int EncodeInt16(short value, Span<byte> buffer)
    {
        BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        return 2;
    }

    private static int EncodeInt32(int value, Span<byte> buffer)
    {
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        return 4;
    }

    private static int EncodeInt64(long value, Span<byte> buffer)
    {
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        return 8;
    }

    private static int EncodeFloat(float value, Span<byte> buffer)
    {
        int intBits = BitConverter.SingleToInt32Bits(value);
        BinaryPrimitives.WriteInt32BigEndian(buffer, intBits);
        return 4;
    }

    private static int EncodeDouble(double value, Span<byte> buffer)
    {
        long longBits = BitConverter.DoubleToInt64Bits(value);
        BinaryPrimitives.WriteInt64BigEndian(buffer, longBits);
        return 8;
    }

    private static int EncodeString(string value, Span<byte> buffer)
    {
        return Utf8.GetBytes(value, buffer);
    }

    private static int EncodeDate(DateOnly value, Span<byte> buffer)
    {
        int days = value.DayNumber - LocalDateEpoch.DayNumber;
        BinaryPrimitives.WriteInt32BigEndian(buffer, days);
        return 4;
    }

    private static int EncodeTime(TimeOnly value, Span<byte> buffer)
    {
        long micros = value.Ticks / 10;
        BinaryPrimitives.WriteInt64BigEndian(buffer, micros);
        return 8;
    }

    private static int EncodeTimestamp(DateTime value, Span<byte> buffer)
    {
        long micros = (value.Ticks - LocalDateTimeEpoch.Ticks) / 10;
        BinaryPrimitives.WriteInt64BigEndian(buffer, micros);
        return 8;
    }

    private static int EncodeTimestampTz(DateTimeOffset value, Span<byte> buffer)
    {
        var utc = value.UtcDateTime;
        long micros = (utc.Ticks - LocalDateTimeEpoch.Ticks) / 10;
        BinaryPrimitives.WriteInt64BigEndian(buffer, micros);
        return 8;
    }

    private static int EncodeByteArray(byte[] value, Span<byte> buffer)
    {
        value.CopyTo(buffer);
        return value.Length;
    }

    private static int EncodeGuid(Guid value, Span<byte> buffer)
    {
        value.TryWriteBytes(buffer);
        
        // Swap bytes back to network byte order (big-endian)
        (buffer[0], buffer[3]) = (buffer[3], buffer[0]);
        (buffer[1], buffer[2]) = (buffer[2], buffer[1]);
        (buffer[4], buffer[5]) = (buffer[5], buffer[4]);
        (buffer[6], buffer[7]) = (buffer[7], buffer[6]);
        
        return 16;
    }

    private static int EncodePoint(Point value, Span<byte> buffer)
    {
        int written = EncodeDouble(value.X, buffer);
        written += EncodeDouble(value.Y, buffer.Slice(8));
        return written;
    }

    private static int EncodeInterval(Interval value, Span<byte> buffer)
    {
        long micros = (long)value.Hours * 3600_000_000L +
                     (long)value.Minutes * 60_000_000L +
                     (long)value.Seconds * 1_000_000L +
                     value.Microseconds;
        
        int months = value.Years * 12 + value.Months;
        
        BinaryPrimitives.WriteInt64BigEndian(buffer, micros);
        BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(8), value.Days);
        BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(12), months);
        
        return 16;
    }

    #endregion

    #region Text Decode

    public static object? DecodeText(DataType dataType, ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return null;

        var text = Utf8.GetString(buffer);
        
        return dataType.Id switch
        {
            DataTypeId.Bool => text.StartsWith('t') || text == "1",
            DataTypeId.Int2 => short.Parse(text),
            DataTypeId.Int4 => int.Parse(text),
            DataTypeId.Int8 => long.Parse(text),
            DataTypeId.Float4 => float.Parse(text),
            DataTypeId.Float8 => double.Parse(text),
            DataTypeId.Numeric => decimal.Parse(text),
            DataTypeId.Char or DataTypeId.Varchar or DataTypeId.Bpchar or DataTypeId.Text or DataTypeId.Name => text,
            DataTypeId.Date => DateOnly.Parse(text),
            DataTypeId.Time => TimeOnly.Parse(text),
            DataTypeId.Timestamp => DateTime.Parse(text),
            DataTypeId.Timestamptz => DateTimeOffset.Parse(text),
            DataTypeId.Uuid => Guid.Parse(text),
            DataTypeId.Json or DataTypeId.Jsonb => text,
            _ => text
        };
    }

    #endregion
}
