// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Vertx.PgClient.Codec;

/// <summary>
/// Buffer utilities for PostgreSQL protocol.
/// </summary>
internal static class BufferUtils
{
    private static readonly Encoding Utf8 = Encoding.UTF8;

    /// <summary>
    /// Reads a null-terminated UTF-8 string from the buffer.
    /// </summary>
    public static string ReadCStringUtf8(ReadOnlySpan<byte> buffer, ref int position)
    {
        int start = position;
        int len = 0;
        while (buffer[position + len] != 0)
        {
            len++;
        }
        var result = Utf8.GetString(buffer.Slice(start, len));
        position += len + 1; // +1 for null terminator
        return result;
    }

    /// <summary>
    /// Reads a null-terminated UTF-8 string from the buffer.
    /// </summary>
    public static string ReadCString(ReadOnlySpan<byte> buffer, out int bytesRead)
    {
        int len = 0;
        while (len < buffer.Length && buffer[len] != 0)
        {
            len++;
        }
        var result = Utf8.GetString(buffer.Slice(0, len));
        bytesRead = len + 1; // +1 for null terminator
        return result;
    }

    /// <summary>
    /// Reads a null-terminated UTF-8 string from the sequence.
    /// </summary>
    public static string ReadCStringUtf8(ref SequenceReader<byte> reader)
    {
        if (reader.TryReadTo(out ReadOnlySpan<byte> data, 0))
        {
            return Utf8.GetString(data);
        }
        throw new InvalidOperationException("Expected null-terminated string");
    }

    /// <summary>
    /// Writes a null-terminated UTF-8 string to the buffer.
    /// </summary>
    public static int WriteCStringUtf8(Span<byte> buffer, string s)
    {
        int bytesWritten = Utf8.GetBytes(s, buffer);
        buffer[bytesWritten] = 0;
        return bytesWritten + 1;
    }

    /// <summary>
    /// Writes a null-terminated UTF-8 string to the buffer writer.
    /// </summary>
    public static void WriteCStringUtf8(IBufferWriter<byte> writer, string s)
    {
        int byteCount = Utf8.GetByteCount(s);
        var span = writer.GetSpan(byteCount + 1);
        Utf8.GetBytes(s, span);
        span[byteCount] = 0;
        writer.Advance(byteCount + 1);
    }

    /// <summary>
    /// Writes bytes with a null terminator.
    /// </summary>
    public static void WriteCString(IBufferWriter<byte> writer, ReadOnlySpan<byte> bytes)
    {
        var span = writer.GetSpan(bytes.Length + 1);
        bytes.CopyTo(span);
        span[bytes.Length] = 0;
        writer.Advance(bytes.Length + 1);
    }

    /// <summary>
    /// Estimates the byte count for a UTF-8 string with null terminator.
    /// </summary>
    public static int EstimateCStringUtf8(string s) => Utf8.GetByteCount(s) + 1;

    /// <summary>
    /// Reads a big-endian 32-bit integer.
    /// </summary>
    public static int ReadInt32BigEndian(ReadOnlySpan<byte> buffer, ref int position)
    {
        var value = BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(position));
        position += 4;
        return value;
    }

    /// <summary>
    /// Reads a big-endian 16-bit integer.
    /// </summary>
    public static short ReadInt16BigEndian(ReadOnlySpan<byte> buffer, ref int position)
    {
        var value = BinaryPrimitives.ReadInt16BigEndian(buffer.Slice(position));
        position += 2;
        return value;
    }

    /// <summary>
    /// Reads a big-endian unsigned 16-bit integer.
    /// </summary>
    public static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> buffer, ref int position)
    {
        var value = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(position));
        position += 2;
        return value;
    }

    /// <summary>
    /// Writes a big-endian 32-bit integer.
    /// </summary>
    public static void WriteInt32BigEndian(IBufferWriter<byte> writer, int value)
    {
        var span = writer.GetSpan(4);
        BinaryPrimitives.WriteInt32BigEndian(span, value);
        writer.Advance(4);
    }

    /// <summary>
    /// Writes a big-endian 16-bit integer.
    /// </summary>
    public static void WriteInt16BigEndian(IBufferWriter<byte> writer, short value)
    {
        var span = writer.GetSpan(2);
        BinaryPrimitives.WriteInt16BigEndian(span, value);
        writer.Advance(2);
    }

    /// <summary>
    /// Writes a single byte.
    /// </summary>
    public static void WriteByte(IBufferWriter<byte> writer, byte value)
    {
        var span = writer.GetSpan(1);
        span[0] = value;
        writer.Advance(1);
    }

    /// <summary>
    /// Writes bytes to the buffer.
    /// </summary>
    public static void WriteBytes(IBufferWriter<byte> writer, ReadOnlySpan<byte> bytes)
    {
        var span = writer.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        writer.Advance(bytes.Length);
    }

    /// <summary>
    /// Converts a buffer to a hex string.
    /// </summary>
    public static void WriteHexString(ReadOnlySpan<byte> buffer, IBufferWriter<byte> writer)
    {
        var span = writer.GetSpan(buffer.Length * 2);
        for (int i = 0; i < buffer.Length; i++)
        {
            int b = buffer[i];
            span[i * 2] = (byte)Bin2Hex(b >> 4);
            span[i * 2 + 1] = (byte)Bin2Hex(b & 0x0F);
        }
        writer.Advance(buffer.Length * 2);
    }

    private static int Bin2Hex(int digit)
    {
        int isLessOrEqual9 = (digit - 10) >> 31;
        int bin2hexAsciiDistance = 48 + ((~isLessOrEqual9) & 39);
        return digit + bin2hexAsciiDistance;
    }
}
