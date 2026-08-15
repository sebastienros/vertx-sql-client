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
  private readonly object?[]? _values;
  private readonly SqlRowPage? _page;
  private readonly int _offset;
  private readonly int _length;

  internal SqlRow(IReadOnlyList<SqlColumn> columns, object?[] values)
  {
    if (columns.Count != values.Length)
    {
      throw new ArgumentException("Column and value counts must match.", nameof(values));
    }

    _columns = columns;
    _values = values;
  }

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

  public int Count => _values?.Length ?? _page!.Decoder.GetFieldCount(RowSpan);

  public object? this[int ordinal] =>
    _values is not null
      ? _values[ordinal]
      : _page!.Decoder.Decode(RowSpan, ordinal, _columns[ordinal]);

  public object? this[string name] => this[GetOrdinal(name)];

  public bool IsNull(int ordinal) =>
    _values is not null
      ? _values[ordinal] is null
      : _page!.Decoder.IsNull(RowSpan, ordinal);

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

  public T Get<T>(int ordinal)
  {
    if (_values is null)
    {
      return _page!.Decoder.Decode<T>(RowSpan, ordinal, _columns[ordinal]);
    }

    object? value = _values[ordinal];
    if (value is null)
    {
      if (default(T) is null)
      {
        return default!;
      }

      throw new InvalidCastException($"Column {ordinal} contains NULL.");
    }

    if (value is T typed)
    {
      return typed;
    }

    throw new InvalidCastException(
        $"Column {ordinal} contains {value.GetType().FullName}, not {typeof(T).FullName}.");
  }

  public T Get<T>(string name) => Get<T>(GetOrdinal(name));

  public bool GetBoolean(int ordinal) => Get<bool>(ordinal);

  public bool GetBoolean(string name) => GetBoolean(GetOrdinal(name));

  public short GetInt16(int ordinal) => Get<short>(ordinal);

  public short GetInt16(string name) => GetInt16(GetOrdinal(name));

  public int GetInt32(int ordinal) => Get<int>(ordinal);

  public int GetInt32(string name) => GetInt32(GetOrdinal(name));

  public long GetInt64(int ordinal) => Get<long>(ordinal);

  public long GetInt64(string name) => GetInt64(GetOrdinal(name));

  public float GetFloat(int ordinal) => Get<float>(ordinal);

  public float GetFloat(string name) => GetFloat(GetOrdinal(name));

  public double GetDouble(int ordinal) => Get<double>(ordinal);

  public double GetDouble(string name) => GetDouble(GetOrdinal(name));

  public string GetString(int ordinal) => Get<string>(ordinal);

  public string GetString(string name) => GetString(GetOrdinal(name));

  public Guid GetGuid(int ordinal) => Get<Guid>(ordinal);

  public Guid GetGuid(string name) => GetGuid(GetOrdinal(name));

  public DateOnly GetDateOnly(int ordinal) => Get<DateOnly>(ordinal);

  public DateOnly GetDateOnly(string name) => GetDateOnly(GetOrdinal(name));

  public TimeOnly GetTimeOnly(int ordinal) => Get<TimeOnly>(ordinal);

  public TimeOnly GetTimeOnly(string name) => GetTimeOnly(GetOrdinal(name));

  public DateTime GetDateTime(int ordinal) => Get<DateTime>(ordinal);

  public DateTime GetDateTime(string name) => GetDateTime(GetOrdinal(name));

  public DateTimeOffset GetDateTimeOffset(int ordinal) =>
    Get<DateTimeOffset>(ordinal);

  public DateTimeOffset GetDateTimeOffset(string name) =>
    GetDateTimeOffset(GetOrdinal(name));

  public byte[] GetBytes(int ordinal) => Get<byte[]>(ordinal);

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

  private ReadOnlySpan<byte> RowSpan =>
    _page!.Data.AsSpan(_offset, _length);
}
