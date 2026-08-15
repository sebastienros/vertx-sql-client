/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Diagnostics.CodeAnalysis;
using Apex.SqlClient.Internal;

namespace Apex.SqlClient;

/// <summary>An immutable materialized database row.</summary>
public readonly struct SqlRow
{
  private readonly IReadOnlyList<SqlColumn> _columns;
  private readonly SqlRowPage? _page;
  private readonly int _offset;
  private readonly int _length;

  internal SqlRow(
    IReadOnlyList<SqlColumn> columns,
    SqlRowPage page,
    int offset,
    int length)
  {
    _columns = columns;
    _page = page;
    _offset = offset;
    _length = length;
  }

  public int Count => _page!.Decoder.GetFieldCount(RowMemory);

  public object? this[int ordinal] =>
    _page!.Decoder.DecodeObject(RowMemory, ordinal, _columns[ordinal]);

  public object? this[string name] => this[GetOrdinal(name)];

  public bool IsNull(int ordinal) =>
    _page!.Decoder.IsNull(RowMemory, ordinal);

  public int GetOrdinal(string name)
  {
    ArgumentException.ThrowIfNullOrEmpty(name);
    int hash = StringComparer.Ordinal.GetHashCode(name);
    for (int i = 0; i < _columns.Count; i++)
    {
      string candidate = _columns[i].Name;
      if (StringComparer.Ordinal.GetHashCode(candidate) == hash &&
          string.Equals(candidate, name, StringComparison.Ordinal))
      {
        return i;
      }
    }

    throw new IndexOutOfRangeException($"Column '{name}' does not exist.");
  }

  /// <summary>
  /// Gets a common CLR or provider-specific value without routing through
  /// object decoding.
  /// </summary>
  public T Get<T>(int ordinal) =>
    SqlRowDecoder.Decode<T>(
      _page!.Decoder,
      RowMemory,
      ordinal,
      _columns[ordinal]);

  public T Get<T>(string name) => Get<T>(GetOrdinal(name));

  public bool GetBoolean(int ordinal) =>
    _page!.Decoder.DecodeBoolean(RowMemory, ordinal, _columns[ordinal]);

  public bool GetBoolean(string name) => GetBoolean(GetOrdinal(name));

  public short GetInt16(int ordinal) =>
    _page!.Decoder.DecodeInt16(RowMemory, ordinal, _columns[ordinal]);

  public short GetInt16(string name) => GetInt16(GetOrdinal(name));

  public int GetInt32(int ordinal) =>
    _page!.Decoder.DecodeInt32(RowMemory, ordinal, _columns[ordinal]);

  public int GetInt32(string name) => GetInt32(GetOrdinal(name));

  public long GetInt64(int ordinal) =>
    _page!.Decoder.DecodeInt64(RowMemory, ordinal, _columns[ordinal]);

  public long GetInt64(string name) => GetInt64(GetOrdinal(name));

  public float GetFloat(int ordinal) =>
    _page!.Decoder.DecodeFloat(RowMemory, ordinal, _columns[ordinal]);

  public float GetFloat(string name) => GetFloat(GetOrdinal(name));

  public double GetDouble(int ordinal) =>
    _page!.Decoder.DecodeDouble(RowMemory, ordinal, _columns[ordinal]);

  public double GetDouble(string name) => GetDouble(GetOrdinal(name));

  public decimal GetDecimal(int ordinal) =>
    _page!.Decoder.DecodeDecimal(RowMemory, ordinal, _columns[ordinal]);

  public decimal GetDecimal(string name) => GetDecimal(GetOrdinal(name));

  public string GetString(int ordinal) =>
    _page!.Decoder.DecodeString(RowMemory, ordinal, _columns[ordinal])!;

  public string GetString(string name) => GetString(GetOrdinal(name));

  public Guid GetGuid(int ordinal) =>
    _page!.Decoder.DecodeGuid(RowMemory, ordinal, _columns[ordinal]);

  public Guid GetGuid(string name) => GetGuid(GetOrdinal(name));

  public DateOnly GetDateOnly(int ordinal) =>
    _page!.Decoder.DecodeDateOnly(RowMemory, ordinal, _columns[ordinal]);

  public DateOnly GetDateOnly(string name) => GetDateOnly(GetOrdinal(name));

  public TimeOnly GetTimeOnly(int ordinal) =>
    _page!.Decoder.DecodeTimeOnly(RowMemory, ordinal, _columns[ordinal]);

  public TimeOnly GetTimeOnly(string name) => GetTimeOnly(GetOrdinal(name));

  public DateTime GetDateTime(int ordinal) =>
    _page!.Decoder.DecodeDateTime(RowMemory, ordinal, _columns[ordinal]);

  public DateTime GetDateTime(string name) => GetDateTime(GetOrdinal(name));

  public DateTimeOffset GetDateTimeOffset(int ordinal) =>
    _page!.Decoder.DecodeDateTimeOffset(RowMemory, ordinal, _columns[ordinal]);

  public DateTimeOffset GetDateTimeOffset(string name) =>
    GetDateTimeOffset(GetOrdinal(name));

  public byte[] GetBytes(int ordinal) =>
    _page!.Decoder.DecodeBytes(RowMemory, ordinal, _columns[ordinal])!;

  public byte[] GetBytes(string name) => GetBytes(GetOrdinal(name));

  public bool TryGet<T>(int ordinal, [MaybeNullWhen(false)] out T value)
  {
    if (!IsNull(ordinal))
    {
      try
      {
        value = Get<T>(ordinal);
        return true;
      }
      catch (InvalidCastException)
      {
      }
    }

    value = default;
    return false;
  }

  private ReadOnlyMemory<byte> RowMemory =>
    _page!.Data.AsMemory(_offset, _length);
}
