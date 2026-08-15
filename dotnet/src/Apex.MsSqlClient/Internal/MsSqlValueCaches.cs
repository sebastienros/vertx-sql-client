/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Text;

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

    int normalizedCapacity = 1;
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
    Encoding encoding = TdsCollationCodec.GetEncoding(codePage);
    if (value.Length > _maximumByteLength)
    {
      return encoding.GetString(value);
    }

    ulong hash = Hash(value, codePage);
    lock (_gate)
    {
      if (!_enabled)
      {
        return encoding.GetString(value);
      }

      Entry[] entries = _entries;
      int index = (int)hash & (entries.Length - 1);
      ref Entry entry = ref entries[index];
      if (entry.Hash == hash &&
          entry.CodePage == codePage &&
          entry.Bytes is not null &&
          entry.Bytes.AsSpan().SequenceEqual(value))
      {
        return entry.Value!;
      }

      string decoded = encoding.GetString(value);
      if (entry.CandidateHash == hash &&
          entry.CandidateLength == value.Length &&
          entry.CandidateCodePage == codePage)
      {
        entry.Hash = hash;
        entry.CodePage = codePage;
        entry.Bytes = value.ToArray();
        entry.Value = decoded;
        entry.CandidateHash = 0;
        entry.CandidateLength = 0;
        entry.CandidateCodePage = 0;
      }
      else
      {
        entry.CandidateHash = hash;
        entry.CandidateLength = value.Length;
        entry.CandidateCodePage = codePage;
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
    ulong hash = offset;
    foreach (byte item in value)
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
    internal ulong Hash;
    internal int CodePage;
    internal byte[]? Bytes;
    internal string? Value;
    internal ulong CandidateHash;
    internal int CandidateLength;
    internal int CandidateCodePage;
  }
}
