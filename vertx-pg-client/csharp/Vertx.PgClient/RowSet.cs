// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using System.Collections;
using Vertx.PgClient.Codec;

namespace Vertx.PgClient;

/// <summary>
/// Result of a SQL query execution.
/// </summary>
public interface IRowSet : IEnumerable<IRow>
{
    /// <summary>
    /// Gets the row descriptor for this result set.
    /// </summary>
    PgRowDescriptor RowDescriptor { get; }

    /// <summary>
    /// Gets the number of rows affected by the query.
    /// </summary>
    int RowCount { get; }

    /// <summary>
    /// Gets the number of columns in the result.
    /// </summary>
    int ColumnCount { get; }

    /// <summary>
    /// Gets the column names.
    /// </summary>
    IReadOnlyList<string> ColumnNames { get; }

    /// <summary>
    /// Gets the next result set if this is a multi-result query.
    /// </summary>
    IRowSet? Next { get; }
}

/// <summary>
/// A set of rows from a PostgreSQL query.
/// </summary>
public sealed class RowSet : IRowSet
{
    private readonly List<IRow> _rows;

    public PgRowDescriptor RowDescriptor { get; }
    public int RowCount { get; private set; }
    public int ColumnCount => RowDescriptor.ColumnCount;
    public IRowSet? Next { get; internal set; }

    private string[]? _columnNames;
    public IReadOnlyList<string> ColumnNames => _columnNames ??= RowDescriptor.Columns.Select(c => c.Name).ToArray();

    public RowSet(PgRowDescriptor descriptor)
    {
        _rows = new List<IRow>();
        RowDescriptor = descriptor;
    }

    public RowSet(IEnumerable<Row> rows, string[] columnNames, int rowCount)
    {
        _rows = rows.Cast<IRow>().ToList();
        RowDescriptor = new PgRowDescriptor(
            columnNames.Select(n => PgColumnDesc.ForName(n)).ToArray()
        );
        RowCount = rowCount;
    }

    internal void AddRow(IRow row)
    {
        _rows.Add(row);
    }

    internal void SetRowCount(int count)
    {
        RowCount = count;
    }

    public IEnumerator<IRow> GetEnumerator() => _rows.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Gets the number of rows in the result set.
    /// </summary>
    public int Count => _rows.Count;

    /// <summary>
    /// Gets a row by index.
    /// </summary>
    public IRow this[int index] => _rows[index];
}

/// <summary>
/// Simple row implementation for internal use.
/// </summary>
public sealed class Row : TupleBase, IRow
{
    private readonly object?[] _values;
    private readonly string[] _columnNames;

    public Row(object?[] values, string[] columnNames)
    {
        _values = values;
        _columnNames = columnNames;
    }

    public override int Size => _values.Length;

    public override object? GetValue(int position) => _values[position];

    public string GetColumnName(int position) => position >= 0 && position < _columnNames.Length 
        ? _columnNames[position] 
        : throw new ArgumentOutOfRangeException(nameof(position));

    public int GetColumnIndex(string name)
    {
        for (int i = 0; i < _columnNames.Length; i++)
        {
            if (string.Equals(_columnNames[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    public object? GetValue(string columnName)
    {
        int index = GetColumnIndex(columnName);
        return index >= 0 ? GetValue(index) : null;
    }

    public T? Get<T>(string columnName)
    {
        int index = GetColumnIndex(columnName);
        return index >= 0 ? Get<T>(index) : default;
    }

    public bool GetBoolean(string columnName)
    {
        int index = GetColumnIndex(columnName);
        return index >= 0 && GetBoolean(index);
    }

    public short GetShort(string columnName)
    {
        int index = GetColumnIndex(columnName);
        return index >= 0 ? GetShort(index) : default;
    }

    public int GetInteger(string columnName)
    {
        int index = GetColumnIndex(columnName);
        return index >= 0 ? GetInteger(index) : default;
    }

    public long GetLong(string columnName)
    {
        int index = GetColumnIndex(columnName);
        return index >= 0 ? GetLong(index) : default;
    }

    public float GetFloat(string columnName)
    {
        int index = GetColumnIndex(columnName);
        return index >= 0 ? GetFloat(index) : default;
    }

    public double GetDouble(string columnName)
    {
        int index = GetColumnIndex(columnName);
        return index >= 0 ? GetDouble(index) : default;
    }

    public string? GetString(string columnName)
    {
        int index = GetColumnIndex(columnName);
        return index >= 0 ? GetString(index) : null;
    }

    public int[]? GetIntegerArray(string columnName)
    {
        int index = GetColumnIndex(columnName);
        return index >= 0 ? GetIntegerArray(index) : null;
    }

    public long[]? GetLongArray(string columnName)
    {
        int index = GetColumnIndex(columnName);
        return index >= 0 ? GetLongArray(index) : null;
    }

    public string?[]? GetStringArray(string columnName)
    {
        int index = GetColumnIndex(columnName);
        return index >= 0 ? GetStringArray(index) : null;
    }

    public bool[]? GetBooleanArray(string columnName)
    {
        int index = GetColumnIndex(columnName);
        return index >= 0 ? GetBooleanArray(index) : null;
    }

    public double[]? GetDoubleArray(string columnName)
    {
        int index = GetColumnIndex(columnName);
        return index >= 0 ? GetDoubleArray(index) : null;
    }

    public Guid[]? GetGuidArray(string columnName)
    {
        int index = GetColumnIndex(columnName);
        return index >= 0 ? GetGuidArray(index) : null;
    }
}
