/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Text;

namespace Apex.PgClient.Internal;

internal sealed class Utf8StringCache
{
    private static readonly Encoding s_utf8 = new UTF8Encoding(false, true);
    private readonly object _gate = new();
    private Entry[] _entries;
    private readonly int _maximumByteLength;
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
            if (entry._candidateHash == hash &&
                entry._candidateLength == value.Length)
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

internal static class BoxedScalarCache
{
    private const int Minimum = -128;
    private const int Maximum = 255;
    private static readonly object[] s_int32Values =
      Enumerable.Range(Minimum, Maximum - Minimum + 1)
        .Select(static value => (object)value)
        .ToArray();
    private static readonly object[] s_int16Values =
      Enumerable.Range(Minimum, Maximum - Minimum + 1)
        .Select(static value => (object)(short)value)
        .ToArray();
    private static readonly object[] s_int64Values =
      Enumerable.Range(Minimum, Maximum - Minimum + 1)
        .Select(static value => (object)(long)value)
        .ToArray();
    private static readonly object s_true = true;
    private static readonly object s_false = false;

    internal static object Box(bool value) => value ? s_true : s_false;

    internal static object Box(int value) =>
      value is >= Minimum and <= Maximum
        ? s_int32Values[value - Minimum]
        : value;

    internal static object Box(short value) =>
      value is >= Minimum and <= Maximum
        ? s_int16Values[value - Minimum]
        : value;

    internal static object Box(long value) =>
      value is >= Minimum and <= Maximum
        ? s_int64Values[value - Minimum]
        : value;
}
