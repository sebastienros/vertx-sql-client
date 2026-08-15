/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Text;

namespace Apex.MySqlClient.Internal;

/// <summary>
/// Caches decoded UTF-8 strings in a fixed size, two chance table so repeated column values do
/// not allocate a new string on every row.
/// </summary>
internal sealed class Utf8StringCache
{
    private static readonly Encoding s_utf8 = new UTF8Encoding(false, true);
    private readonly object _gate = new();
    private readonly int _maximumByteLength;
    private Entry[] _entries;
    private bool _enabled;

    internal Utf8StringCache(int capacity, int maximumByteLength)
    {
        if (capacity <= 0 || maximumByteLength <= 0)
        {
            _entries = [];
            return;
        }

        var normalizedCapacity = 1;
        while (normalizedCapacity < capacity)
        {
            normalizedCapacity <<= 1;
        }

        _entries = new Entry[normalizedCapacity];
        _maximumByteLength = maximumByteLength;
        _enabled = true;
    }

    internal string GetString(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
        {
            return string.Empty;
        }

        if (value.Length > _maximumByteLength)
        {
            return s_utf8.GetString(value);
        }

        var hash = Hash(value);
        lock (_gate)
        {
            if (!_enabled)
            {
                return s_utf8.GetString(value);
            }

            var entries = _entries;
            var index = (int)hash & (entries.Length - 1);
            ref var entry = ref entries[index];
            if (entry._hash == hash &&
                entry._utf8 is not null &&
                entry._utf8.AsSpan().SequenceEqual(value))
            {
                return entry._value!;
            }

            var decoded = s_utf8.GetString(value);
            if (entry._candidateHash == hash && entry._candidateLength == value.Length)
            {
                entry._hash = hash;
                entry._utf8 = value.ToArray();
                entry._value = decoded;
                entry._candidateHash = 0;
                entry._candidateLength = 0;
            }
            else
            {
                entry._candidateHash = hash;
                entry._candidateLength = value.Length;
            }

            return decoded;
        }
    }

    internal void Disable()
    {
        lock (_gate)
        {
            _enabled = false;
            _entries = [];
        }
    }

    private static ulong Hash(ReadOnlySpan<byte> value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var item in value)
        {
            hash ^= item;
            hash *= prime;
        }

        hash ^= (ulong)value.Length;
        hash *= prime;
        return hash == 0 ? 1 : hash;
    }

    private struct Entry
    {
        internal ulong _hash;
        internal byte[]? _utf8;
        internal string? _value;
        internal ulong _candidateHash;
        internal int _candidateLength;
    }
}

/// <summary>Reuses boxes for the small scalar values that dominate result sets.</summary>
internal static class BoxedScalarCache
{
    private const int Minimum = -128;
    private const int Maximum = 255;
    private static readonly object[] s_sByteValues = CreateSBytes();
    private static readonly object[] s_byteValues = CreateBytes();
    private static readonly object[] s_int16Values = Create(static value => (object)(short)value);
    private static readonly object[] s_uInt16Values = CreateUnsigned(static value => (object)(ushort)value);
    private static readonly object[] s_int32Values = Create(static value => value);
    private static readonly object[] s_uInt32Values = CreateUnsigned(static value => (object)(uint)value);
    private static readonly object[] s_int64Values = Create(static value => (object)(long)value);
    private static readonly object[] s_uInt64Values = CreateUnsigned(static value => (object)(ulong)value);
    private static readonly object s_true = true;
    private static readonly object s_false = false;

    internal static object Box(bool value) => value ? s_true : s_false;

    internal static object Box(sbyte value) => s_sByteValues[value - sbyte.MinValue];

    internal static object Box(byte value) => s_byteValues[value];

    internal static object Box(short value) =>
      value is >= Minimum and <= Maximum ? s_int16Values[value - Minimum] : value;

    internal static object Box(ushort value) =>
      value <= Maximum ? s_uInt16Values[value] : value;

    internal static object Box(int value) =>
      value is >= Minimum and <= Maximum ? s_int32Values[value - Minimum] : value;

    internal static object Box(uint value) =>
      value <= Maximum ? s_uInt32Values[value] : value;

    internal static object Box(long value) =>
      value is >= Minimum and <= Maximum ? s_int64Values[value - Minimum] : value;

    internal static object Box(ulong value) =>
      value <= Maximum ? s_uInt64Values[value] : value;

    private static object[] Create(Func<int, object> factory)
    {
        var values = new object[Maximum - Minimum + 1];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = factory(i + Minimum);
        }

        return values;
    }

    private static object[] CreateUnsigned(Func<int, object> factory)
    {
        var values = new object[Maximum + 1];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = factory(i);
        }

        return values;
    }

    private static object[] CreateSBytes()
    {
        var values = new object[byte.MaxValue + 1];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (sbyte)(i + sbyte.MinValue);
        }

        return values;
    }

    private static object[] CreateBytes()
    {
        var values = new object[byte.MaxValue + 1];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (byte)i;
        }

        return values;
    }
}
