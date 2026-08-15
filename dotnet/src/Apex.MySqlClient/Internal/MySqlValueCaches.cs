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
  private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
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

    int normalizedCapacity = 1;
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
      return Utf8.GetString(value);
    }

    ulong hash = Hash(value);
    lock (_gate)
    {
      if (!_enabled)
      {
        return Utf8.GetString(value);
      }

      Entry[] entries = _entries;
      int index = (int)hash & (entries.Length - 1);
      ref Entry entry = ref entries[index];
      if (entry.Hash == hash &&
          entry.Utf8 is not null &&
          entry.Utf8.AsSpan().SequenceEqual(value))
      {
        return entry.Value!;
      }

      string decoded = Utf8.GetString(value);
      if (entry.CandidateHash == hash && entry.CandidateLength == value.Length)
      {
        entry.Hash = hash;
        entry.Utf8 = value.ToArray();
        entry.Value = decoded;
        entry.CandidateHash = 0;
        entry.CandidateLength = 0;
      }
      else
      {
        entry.CandidateHash = hash;
        entry.CandidateLength = value.Length;
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
    ulong hash = offset;
    foreach (byte item in value)
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
    internal ulong Hash;
    internal byte[]? Utf8;
    internal string? Value;
    internal ulong CandidateHash;
    internal int CandidateLength;
  }
}

/// <summary>Reuses boxes for the small scalar values that dominate result sets.</summary>
internal static class BoxedScalarCache
{
  private const int Minimum = -128;
  private const int Maximum = 255;
  private static readonly object[] SByteValues = CreateSBytes();
  private static readonly object[] ByteValues = CreateBytes();
  private static readonly object[] Int16Values = Create(static value => (object)(short)value);
  private static readonly object[] UInt16Values = CreateUnsigned(static value => (object)(ushort)value);
  private static readonly object[] Int32Values = Create(static value => value);
  private static readonly object[] UInt32Values = CreateUnsigned(static value => (object)(uint)value);
  private static readonly object[] Int64Values = Create(static value => (object)(long)value);
  private static readonly object[] UInt64Values = CreateUnsigned(static value => (object)(ulong)value);
  private static readonly object True = true;
  private static readonly object False = false;

  internal static object Box(bool value) => value ? True : False;

  internal static object Box(sbyte value) => SByteValues[value - sbyte.MinValue];

  internal static object Box(byte value) => ByteValues[value];

  internal static object Box(short value) =>
    value is >= Minimum and <= Maximum ? Int16Values[value - Minimum] : value;

  internal static object Box(ushort value) =>
    value <= Maximum ? UInt16Values[value] : value;

  internal static object Box(int value) =>
    value is >= Minimum and <= Maximum ? Int32Values[value - Minimum] : value;

  internal static object Box(uint value) =>
    value <= Maximum ? UInt32Values[value] : value;

  internal static object Box(long value) =>
    value is >= Minimum and <= Maximum ? Int64Values[value - Minimum] : value;

  internal static object Box(ulong value) =>
    value <= Maximum ? UInt64Values[value] : value;

  private static object[] Create(Func<int, object> factory)
  {
    object[] values = new object[Maximum - Minimum + 1];
    for (int i = 0; i < values.Length; i++)
    {
      values[i] = factory(i + Minimum);
    }

    return values;
  }

  private static object[] CreateUnsigned(Func<int, object> factory)
  {
    object[] values = new object[Maximum + 1];
    for (int i = 0; i < values.Length; i++)
    {
      values[i] = factory(i);
    }

    return values;
  }

  private static object[] CreateSBytes()
  {
    object[] values = new object[byte.MaxValue + 1];
    for (int i = 0; i < values.Length; i++)
    {
      values[i] = (sbyte)(i + sbyte.MinValue);
    }

    return values;
  }

  private static object[] CreateBytes()
  {
    object[] values = new object[byte.MaxValue + 1];
    for (int i = 0; i < values.Length; i++)
    {
      values[i] = (byte)i;
    }

    return values;
  }
}
