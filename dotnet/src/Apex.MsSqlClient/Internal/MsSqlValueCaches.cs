/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

namespace Apex.MsSqlClient.Internal;

internal sealed class MsSqlStringCache
{
    private readonly object _gate = new();
    private readonly int _maximumByteLength;
    private Entry[] _entries;
    private bool _enabled;

    internal MsSqlStringCache(int capacity, int maximumByteLength)
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

    internal string GetString(ReadOnlySpan<byte> value, int codePage)
    {
        var encoding = TdsCollationCodec.GetEncoding(codePage);
        if (value.Length > _maximumByteLength)
        {
            return encoding.GetString(value);
        }

        var hash = Hash(value, codePage);
        lock (_gate)
        {
            if (!_enabled)
            {
                return encoding.GetString(value);
            }

            var entries = _entries;
            var index = (int)hash & (entries.Length - 1);
            ref var entry = ref entries[index];
            if (entry._hash == hash &&
                entry._codePage == codePage &&
                entry._bytes is not null &&
                entry._bytes.AsSpan().SequenceEqual(value))
            {
                return entry._value!;
            }

            var decoded = encoding.GetString(value);
            if (entry._candidateHash == hash &&
                entry._candidateLength == value.Length &&
                entry._candidateCodePage == codePage)
            {
                entry._hash = hash;
                entry._codePage = codePage;
                entry._bytes = value.ToArray();
                entry._value = decoded;
                entry._candidateHash = 0;
                entry._candidateLength = 0;
                entry._candidateCodePage = 0;
            }
            else
            {
                entry._candidateHash = hash;
                entry._candidateLength = value.Length;
                entry._candidateCodePage = codePage;
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

    private static ulong Hash(ReadOnlySpan<byte> value, int codePage)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var item in value)
        {
            hash ^= item;
            hash *= prime;
        }

        hash ^= checked((uint)codePage);
        hash *= prime;
        hash ^= checked((uint)value.Length);
        hash *= prime;
        return hash == 0 ? 1 : hash;
    }

    private struct Entry
    {
        internal ulong _hash;
        internal int _codePage;
        internal byte[]? _bytes;
        internal string? _value;
        internal ulong _candidateHash;
        internal int _candidateLength;
        internal int _candidateCodePage;
    }
}

internal static class MsSqlBoxedScalarCache
{
    private const int Minimum = -128;
    private const int Maximum = 255;
    private static readonly object[] s_bytes = CreateBytes();
    private static readonly object[] s_int16Values =
      Create(static value => (object)(short)value);
    private static readonly object[] s_int32Values =
      Create(static value => value);
    private static readonly object[] s_int64Values =
      Create(static value => (object)(long)value);
    private static readonly object s_true = true;
    private static readonly object s_false = false;

    internal static object Box(bool value) => value ? s_true : s_false;

    internal static object Box(byte value) => s_bytes[value];

    internal static object Box(short value) =>
      value is >= Minimum and <= Maximum
        ? s_int16Values[value - Minimum]
        : value;

    internal static object Box(int value) =>
      value is >= Minimum and <= Maximum
        ? s_int32Values[value - Minimum]
        : value;

    internal static object Box(long value) =>
      value is >= Minimum and <= Maximum
        ? s_int64Values[value - Minimum]
        : value;

    private static object[] Create(Func<int, object> factory)
    {
        var values = new object[Maximum - Minimum + 1];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = factory(i + Minimum);
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
