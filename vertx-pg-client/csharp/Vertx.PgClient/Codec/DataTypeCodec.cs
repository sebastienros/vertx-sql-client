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

        return dataType.Id switch
        {
            DataTypeId.Bool => buffer[0] == 't' || buffer[0] == '1',
            DataTypeId.Int2 => ParseInt16Utf8(buffer),
            DataTypeId.Int4 => ParseInt32Utf8(buffer),
            DataTypeId.Int8 => ParseInt64Utf8(buffer),
            DataTypeId.Float4 => ParseSingleUtf8(buffer),
            DataTypeId.Float8 => ParseDoubleUtf8(buffer),
            DataTypeId.Numeric => ParseDecimalUtf8(buffer),
            DataTypeId.Char or DataTypeId.Varchar or DataTypeId.Bpchar or DataTypeId.Text or DataTypeId.Name => Utf8.GetString(buffer),
            DataTypeId.Date => ParseDateOnlyFromUtf8(buffer),
            DataTypeId.Time => ParseTimeOnlyFromUtf8(buffer),
            DataTypeId.Timestamp => ParseDateTimeFromUtf8(buffer),
            DataTypeId.Timestamptz => ParseDateTimeOffsetFromUtf8(buffer),
            DataTypeId.Uuid => ParseGuidUtf8(buffer),
            DataTypeId.Json or DataTypeId.Jsonb => Utf8.GetString(buffer),
            DataTypeId.Bytea => DecodeByteaText(buffer),
            DataTypeId.Point => DecodePointText(buffer),
            DataTypeId.Line => DecodeLineText(buffer),
            DataTypeId.Lseg => DecodeLsegText(buffer),
            DataTypeId.Box => DecodeBoxText(buffer),
            DataTypeId.Path => DecodePathText(buffer),
            DataTypeId.Polygon => DecodePolygonText(buffer),
            DataTypeId.Circle => DecodeCircleText(buffer),
            DataTypeId.Inet => DecodeInetText(buffer),
            DataTypeId.Cidr => DecodeCidrText(buffer),
            DataTypeId.Interval => DecodeIntervalText(buffer),
            DataTypeId.BoolArray => DecodeBoolArrayText(buffer),
            DataTypeId.Int4Array => DecodeInt4ArrayText(buffer),
            DataTypeId.Float8Array => DecodeFloat8ArrayText(buffer),
            DataTypeId.TextArray => DecodeTextArrayText(buffer),
            _ => Utf8.GetString(buffer)
        };
    }

    private static short ParseInt16Utf8(ReadOnlySpan<byte> buffer)
    {
        if (System.Buffers.Text.Utf8Parser.TryParse(buffer, out short value, out _))
            return value;
        throw new FormatException("Invalid Int16 format");
    }

    private static int ParseInt32Utf8(ReadOnlySpan<byte> buffer)
    {
        if (System.Buffers.Text.Utf8Parser.TryParse(buffer, out int value, out _))
            return value;
        throw new FormatException("Invalid Int32 format");
    }

    private static long ParseInt64Utf8(ReadOnlySpan<byte> buffer)
    {
        if (System.Buffers.Text.Utf8Parser.TryParse(buffer, out long value, out _))
            return value;
        throw new FormatException("Invalid Int64 format");
    }

    private static float ParseSingleUtf8(ReadOnlySpan<byte> buffer)
    {
        if (System.Buffers.Text.Utf8Parser.TryParse(buffer, out float value, out _))
            return value;
        throw new FormatException("Invalid Single format");
    }

    private static double ParseDoubleUtf8(ReadOnlySpan<byte> buffer)
    {
        if (System.Buffers.Text.Utf8Parser.TryParse(buffer, out double value, out _))
            return value;
        throw new FormatException("Invalid Double format");
    }

    private static decimal ParseDecimalUtf8(ReadOnlySpan<byte> buffer)
    {
        if (System.Buffers.Text.Utf8Parser.TryParse(buffer, out decimal value, out _))
            return value;
        throw new FormatException("Invalid Decimal format");
    }

    private static Guid ParseGuidUtf8(ReadOnlySpan<byte> buffer)
    {
        if (System.Buffers.Text.Utf8Parser.TryParse(buffer, out Guid value, out _))
            return value;
        throw new FormatException("Invalid Guid format");
    }

    /// <summary>
    /// Converts ASCII UTF-8 bytes to chars without allocation using stackalloc.
    /// PostgreSQL date/time formats are always ASCII, so this is safe.
    /// </summary>
    private static ReadOnlySpan<char> Utf8AsciiToChars(ReadOnlySpan<byte> buffer, Span<char> destination)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            destination[i] = (char)buffer[i];
        }
        return destination.Slice(0, buffer.Length);
    }

    private static DateOnly ParseDateOnlyFromUtf8(ReadOnlySpan<byte> buffer)
    {
        // PostgreSQL date format is ASCII (e.g., "2023-01-15")
        // Max length for date is ~10 chars, use 32 for safety
        Span<char> chars = stackalloc char[Math.Min(buffer.Length, 32)];
        return DateOnly.Parse(Utf8AsciiToChars(buffer, chars));
    }

    private static TimeOnly ParseTimeOnlyFromUtf8(ReadOnlySpan<byte> buffer)
    {
        // PostgreSQL time format is ASCII (e.g., "12:30:45.123456")
        // Max length for time is ~15 chars, use 32 for safety
        Span<char> chars = stackalloc char[Math.Min(buffer.Length, 32)];
        return TimeOnly.Parse(Utf8AsciiToChars(buffer, chars));
    }

    private static DateTime ParseDateTimeFromUtf8(ReadOnlySpan<byte> buffer)
    {
        // PostgreSQL timestamp format is ASCII (e.g., "2023-01-15 12:30:45.123456")
        // Max length is ~26 chars, use 64 for safety
        Span<char> chars = stackalloc char[Math.Min(buffer.Length, 64)];
        return DateTime.Parse(Utf8AsciiToChars(buffer, chars));
    }

    private static DateTimeOffset ParseDateTimeOffsetFromUtf8(ReadOnlySpan<byte> buffer)
    {
        // PostgreSQL timestamptz format is ASCII (e.g., "2023-01-15 12:30:45.123456+00")
        // Max length is ~32 chars, use 64 for safety
        Span<char> chars = stackalloc char[Math.Min(buffer.Length, 64)];
        return DateTimeOffset.Parse(Utf8AsciiToChars(buffer, chars));
    }

    private static byte[] DecodeByteaText(ReadOnlySpan<byte> buffer)
    {
        // PostgreSQL bytea text format is hex: \x48656c6c6f
        if (buffer.Length >= 2 && buffer[0] == '\\' && buffer[1] == 'x')
        {
            // Convert hex portion to string and parse
            return Convert.FromHexString(Utf8.GetString(buffer[2..]));
        }
        
        // Fallback for escape format (legacy)
        return buffer.ToArray();
    }

    private static Point DecodePointText(ReadOnlySpan<byte> buffer)
    {
        // Format: (x,y)
        if (buffer.Length >= 5 && buffer[0] == '(' && buffer[^1] == ')')
        {
            var inner = buffer[1..^1]; // Remove parentheses
            int commaIndex = inner.IndexOf((byte)',');
            if (commaIndex > 0)
            {
                if (System.Buffers.Text.Utf8Parser.TryParse(inner[..commaIndex], out double x, out _) &&
                    System.Buffers.Text.Utf8Parser.TryParse(inner[(commaIndex + 1)..], out double y, out _))
                {
                    return new Point(x, y);
                }
            }
        }
        throw new FormatException($"Invalid point format: {Utf8.GetString(buffer)}");
    }

    private static Interval DecodeIntervalText(ReadOnlySpan<byte> buffer)
    {
        // Format examples: "1 year 2 mons 3 days 04:05:06" or "00:00:00" or "1 year" etc.
        var interval = new Interval();
        
        int i = 0;
        while (i < buffer.Length)
        {
            // Skip whitespace
            while (i < buffer.Length && buffer[i] == ' ') i++;
            if (i >= buffer.Length) break;
            
            // Check for time component (HH:MM:SS or -HH:MM:SS)
            bool isNegativeTime = buffer[i] == '-';
            if (isNegativeTime) i++;
            
            if (i < buffer.Length && buffer[i] >= '0' && buffer[i] <= '9')
            {
                int numStart = i;
                while (i < buffer.Length && ((buffer[i] >= '0' && buffer[i] <= '9') || buffer[i] == '.')) i++;
                
                // Check if this is a time component (contains ':')
                if (i < buffer.Length && buffer[i] == ':')
                {
                    // Parse time component HH:MM:SS.microseconds
                    System.Buffers.Text.Utf8Parser.TryParse(buffer[numStart..i], out int hours, out _);
                    i++; // skip ':'
                    int minStart = i;
                    while (i < buffer.Length && buffer[i] >= '0' && buffer[i] <= '9') i++;
                    System.Buffers.Text.Utf8Parser.TryParse(buffer[minStart..i], out int minutes, out _);
                    
                    int seconds = 0;
                    int microseconds = 0;
                    if (i < buffer.Length && buffer[i] == ':')
                    {
                        i++; // skip ':'
                        int secStart = i;
                        while (i < buffer.Length && buffer[i] >= '0' && buffer[i] <= '9') i++;
                        System.Buffers.Text.Utf8Parser.TryParse(buffer[secStart..i], out seconds, out _);
                        
                        if (i < buffer.Length && buffer[i] == '.')
                        {
                            i++; // skip '.'
                            int microStart = i;
                            while (i < buffer.Length && buffer[i] >= '0' && buffer[i] <= '9') i++;
                            System.Buffers.Text.Utf8Parser.TryParse(buffer[microStart..i], out microseconds, out _);
                            // Pad or truncate to 6 digits
                            int digits = i - microStart;
                            while (digits < 6) { microseconds *= 10; digits++; }
                            while (digits > 6) { microseconds /= 10; digits--; }
                        }
                    }
                    
                    if (isNegativeTime)
                    {
                        hours = -hours;
                        minutes = -minutes;
                        seconds = -seconds;
                        microseconds = -microseconds;
                    }
                    
                    interval.Hours = hours;
                    interval.Minutes = minutes;
                    interval.Seconds = seconds;
                    interval.Microseconds = microseconds;
                }
                else
                {
                    // Parse number followed by unit
                    System.Buffers.Text.Utf8Parser.TryParse(buffer[numStart..i], out int value, out _);
                    if (isNegativeTime) value = -value;
                    
                    // Skip whitespace
                    while (i < buffer.Length && buffer[i] == ' ') i++;
                    
                    // Read unit
                    int unitStart = i;
                    while (i < buffer.Length && ((buffer[i] >= 'a' && buffer[i] <= 'z') || (buffer[i] >= 'A' && buffer[i] <= 'Z'))) i++;
                    var unit = buffer[unitStart..i];
                    
                    if (StartsWithIgnoreCase(unit, "year"u8))
                        interval.Years = value;
                    else if (StartsWithIgnoreCase(unit, "mon"u8))
                        interval.Months = value;
                    else if (StartsWithIgnoreCase(unit, "day"u8))
                        interval.Days = value;
                    else if (StartsWithIgnoreCase(unit, "hour"u8))
                        interval.Hours = value;
                    else if (StartsWithIgnoreCase(unit, "min"u8))
                        interval.Minutes = value;
                    else if (StartsWithIgnoreCase(unit, "sec"u8))
                        interval.Seconds = value;
                }
            }
            else
            {
                i++;
            }
        }
        
        return interval;
    }

    private static bool StartsWithIgnoreCase(ReadOnlySpan<byte> span, ReadOnlySpan<byte> prefix)
    {
        if (span.Length < prefix.Length) return false;
        for (int i = 0; i < prefix.Length; i++)
        {
            byte a = span[i];
            byte b = prefix[i];
            // Convert to lowercase for comparison
            if (a >= 'A' && a <= 'Z') a = (byte)(a + 32);
            if (b >= 'A' && b <= 'Z') b = (byte)(b + 32);
            if (a != b) return false;
        }
        return true;
    }

    private static int[] DecodeInt4ArrayText(ReadOnlySpan<byte> buffer)
    {
        // Format: {1,2,3,4,5}
        if (buffer.Length < 2 || buffer[0] != '{' || buffer[^1] != '}')
            return Array.Empty<int>();
        
        if (buffer.Length == 2) // Empty array "{}"
            return Array.Empty<int>();
        
        var inner = buffer[1..^1];
        var result = new List<int>();
        
        int start = 0;
        for (int i = 0; i <= inner.Length; i++)
        {
            if (i == inner.Length || inner[i] == ',')
            {
                var element = inner[start..i];
                if (element.Length == 4 && 
                    (element[0] == 'N' || element[0] == 'n') &&
                    (element[1] == 'U' || element[1] == 'u') &&
                    (element[2] == 'L' || element[2] == 'l') &&
                    (element[3] == 'L' || element[3] == 'l'))
                {
                    result.Add(0); // NULL handling
                }
                else if (System.Buffers.Text.Utf8Parser.TryParse(element, out int value, out _))
                {
                    result.Add(value);
                }
                start = i + 1;
            }
        }
        
        return result.ToArray();
    }

    private static string[] DecodeTextArrayText(ReadOnlySpan<byte> buffer)
    {
        // Format: {a,b,c} or {"a","b","c"} for quoted strings
        if (buffer.Length < 2 || buffer[0] != '{' || buffer[^1] != '}')
            return Array.Empty<string>();
        
        if (buffer.Length == 2) // Empty array "{}"
            return Array.Empty<string>();
        
        var result = new List<string>();
        int i = 1; // Skip opening brace
        int end = buffer.Length - 1; // Before closing brace
        
        while (i < end)
        {
            if (buffer[i] == '"')
            {
                // Quoted string
                i++; // Skip opening quote
                var sb = new System.Text.StringBuilder();
                while (i < end)
                {
                    if (buffer[i] == '\\' && i + 1 < end)
                    {
                        sb.Append((char)buffer[i + 1]);
                        i += 2;
                    }
                    else if (buffer[i] == '"')
                    {
                        break;
                    }
                    else
                    {
                        sb.Append((char)buffer[i]);
                        i++;
                    }
                }
                result.Add(sb.ToString());
                i++; // Skip closing quote
                if (i < end && buffer[i] == ',')
                    i++; // Skip comma
            }
            else if (buffer[i] == ',')
            {
                i++;
            }
            else
            {
                // Unquoted string
                int start = i;
                while (i < end && buffer[i] != ',')
                    i++;
                var element = buffer[start..i];
                if (element.Length == 4 && 
                    (element[0] == 'N' || element[0] == 'n') &&
                    (element[1] == 'U' || element[1] == 'u') &&
                    (element[2] == 'L' || element[2] == 'l') &&
                    (element[3] == 'L' || element[3] == 'l'))
                {
                    result.Add(null!);
                }
                else
                {
                    result.Add(Utf8.GetString(element));
                }
                if (i < end && buffer[i] == ',')
                    i++;
            }
        }
        
        return result.ToArray();
    }

    private static Line DecodeLineText(ReadOnlySpan<byte> buffer)
    {
        // Format: {A,B,C}
        if (buffer.Length >= 5 && buffer[0] == '{' && buffer[^1] == '}')
        {
            var inner = buffer[1..^1];
            Span<Range> ranges = stackalloc Range[3];
            int count = SplitByteSpan(inner, (byte)',', ranges);
            if (count == 3)
            {
                System.Buffers.Text.Utf8Parser.TryParse(inner[ranges[0]], out double a, out _);
                System.Buffers.Text.Utf8Parser.TryParse(inner[ranges[1]], out double b, out _);
                System.Buffers.Text.Utf8Parser.TryParse(inner[ranges[2]], out double c, out _);
                return new Line(a, b, c);
            }
        }
        throw new FormatException($"Invalid line format: {Utf8.GetString(buffer)}");
    }

    private static LineSegment DecodeLsegText(ReadOnlySpan<byte> buffer)
    {
        // Format: [(x1,y1),(x2,y2)]
        var text = Utf8.GetString(buffer);
        var points = ParsePointList(text);
        if (points.Count == 2)
        {
            return new LineSegment(points[0], points[1]);
        }
        throw new FormatException($"Invalid lseg format: {text}");
    }

    private static Box DecodeBoxText(ReadOnlySpan<byte> buffer)
    {
        // Format: (x1,y1),(x2,y2)
        var text = Utf8.GetString(buffer);
        var points = ParsePointList(text);
        if (points.Count == 2)
        {
            return new Box(points[0], points[1]);
        }
        throw new FormatException($"Invalid box format: {text}");
    }

    private static Data.Path DecodePathText(ReadOnlySpan<byte> buffer)
    {
        // Format: [(x1,y1),(x2,y2),...] for open, ((x1,y1),(x2,y2),...) for closed
        var text = Utf8.GetString(buffer);
        bool isOpen = text.StartsWith('[');
        var points = ParsePointList(text);
        return new Data.Path(isOpen, points);
    }

    private static Polygon DecodePolygonText(ReadOnlySpan<byte> buffer)
    {
        // Format: ((x1,y1),(x2,y2),...)
        var text = Utf8.GetString(buffer);
        var points = ParsePointList(text);
        return new Polygon(points);
    }

    private static Circle DecodeCircleText(ReadOnlySpan<byte> buffer)
    {
        // Format: <(x,y),r>
        var text = Utf8.GetString(buffer);
        if (text.StartsWith('<') && text.EndsWith('>'))
        {
            var inner = text[1..^1];
            // Find the last comma which separates center from radius
            int lastComma = inner.LastIndexOf(',');
            if (lastComma > 0)
            {
                var centerPart = inner[..lastComma];
                var radiusPart = inner[(lastComma + 1)..];
                
                // Parse center point
                var points = ParsePointList(centerPart);
                if (points.Count == 1 && double.TryParse(radiusPart, out double radius))
                {
                    return new Circle(points[0], radius);
                }
            }
        }
        throw new FormatException($"Invalid circle format: {text}");
    }

    private static Inet DecodeInetText(ReadOnlySpan<byte> buffer)
    {
        // Format: 192.168.1.1 or 192.168.1.1/24 or ::1 etc.
        var text = Utf8.GetString(buffer);
        int slashIndex = text.IndexOf('/');
        if (slashIndex >= 0)
        {
            var address = IPAddress.Parse(text[..slashIndex]);
            var netmask = int.Parse(text[(slashIndex + 1)..]);
            return new Inet().SetAddress(address).SetNetmask(netmask);
        }
        else
        {
            var address = IPAddress.Parse(text);
            int defaultNetmask = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            return new Inet().SetAddress(address).SetNetmask(defaultNetmask);
        }
    }

    private static Cidr DecodeCidrText(ReadOnlySpan<byte> buffer)
    {
        // Format: 192.168.1.0/24
        var text = Utf8.GetString(buffer);
        int slashIndex = text.IndexOf('/');
        if (slashIndex >= 0)
        {
            var address = IPAddress.Parse(text[..slashIndex]);
            var netmask = int.Parse(text[(slashIndex + 1)..]);
            return new Cidr().SetAddress(address).SetNetmask(netmask);
        }
        else
        {
            var address = IPAddress.Parse(text);
            int defaultNetmask = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            return new Cidr().SetAddress(address).SetNetmask(defaultNetmask);
        }
    }

    private static bool[] DecodeBoolArrayText(ReadOnlySpan<byte> buffer)
    {
        // Format: {t,f,t}
        if (buffer.Length < 2 || buffer[0] != '{' || buffer[^1] != '}')
            return Array.Empty<bool>();
        
        if (buffer.Length == 2) // Empty array "{}"
            return Array.Empty<bool>();
        
        var inner = buffer[1..^1];
        var result = new List<bool>();
        
        int start = 0;
        for (int i = 0; i <= inner.Length; i++)
        {
            if (i == inner.Length || inner[i] == ',')
            {
                var element = inner[start..i];
                if (element.Length > 0)
                {
                    result.Add(element[0] == 't' || element[0] == 'T' || element[0] == '1');
                }
                start = i + 1;
            }
        }
        
        return result.ToArray();
    }

    private static double[] DecodeFloat8ArrayText(ReadOnlySpan<byte> buffer)
    {
        // Format: {1.1,2.2,3.3}
        if (buffer.Length < 2 || buffer[0] != '{' || buffer[^1] != '}')
            return Array.Empty<double>();
        
        if (buffer.Length == 2) // Empty array "{}"
            return Array.Empty<double>();
        
        var inner = buffer[1..^1];
        var result = new List<double>();
        
        int start = 0;
        for (int i = 0; i <= inner.Length; i++)
        {
            if (i == inner.Length || inner[i] == ',')
            {
                var element = inner[start..i];
                if (System.Buffers.Text.Utf8Parser.TryParse(element, out double value, out _))
                {
                    result.Add(value);
                }
                start = i + 1;
            }
        }
        
        return result.ToArray();
    }

    private static List<Point> ParsePointList(string text)
    {
        // Parse a list of points from formats like ((x1,y1),(x2,y2)) or [(x1,y1),(x2,y2)]
        var points = new List<Point>();
        int i = 0;
        int depth = 0;
        
        while (i < text.Length)
        {
            if (text[i] == '(')
            {
                depth++;
                if (depth == 2) // We're inside an inner point
                {
                    int start = i + 1;
                    int end = text.IndexOf(')', i);
                    if (end > start)
                    {
                        var pointStr = text[start..end];
                        int comma = pointStr.IndexOf(',');
                        if (comma > 0)
                        {
                            if (double.TryParse(pointStr[..comma], out double x) &&
                                double.TryParse(pointStr[(comma + 1)..], out double y))
                            {
                                points.Add(new Point(x, y));
                            }
                        }
                        i = end;
                        depth--;
                    }
                }
            }
            else if (text[i] == '[')
            {
                // Treat [ as depth=1 for open paths like [(x1,y1),(x2,y2)]
                depth = 1;
            }
            else if (text[i] == ')' || text[i] == ']')
            {
                depth = Math.Max(0, depth - 1);
            }
            i++;
        }
        
        // If we didn't find nested points, try parsing as simple point list (for formats like (x,y),(x,y))
        if (points.Count == 0)
        {
            i = 0;
            while (i < text.Length)
            {
                int start = text.IndexOf('(', i);
                if (start < 0) break;
                
                int end = text.IndexOf(')', start);
                if (end < 0) break;
                
                var pointStr = text[(start + 1)..end];
                int comma = pointStr.IndexOf(',');
                if (comma > 0)
                {
                    if (double.TryParse(pointStr[..comma], out double x) &&
                        double.TryParse(pointStr[(comma + 1)..], out double y))
                    {
                        points.Add(new Point(x, y));
                    }
                }
                
                i = end + 1;
            }
        }
        
        return points;
    }

    private static int SplitByteSpan(ReadOnlySpan<byte> span, byte separator, Span<Range> ranges)
    {
        int count = 0;
        int start = 0;
        
        for (int i = 0; i <= span.Length && count < ranges.Length; i++)
        {
            if (i == span.Length || span[i] == separator)
            {
                ranges[count++] = new Range(start, i);
                start = i + 1;
            }
        }
        
        return count;
    }

    #endregion
}
