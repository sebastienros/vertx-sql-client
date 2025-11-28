// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using Vertx.PgClient.Codec;

namespace Vertx.PgClient;

/// <summary>
/// A row in a query result.
/// </summary>
public interface IRow : ITuple
{
    /// <summary>
    /// Gets the column name at the specified position.
    /// </summary>
    string GetColumnName(int position);

    /// <summary>
    /// Gets the column index for the specified column name, or -1 if not found.
    /// </summary>
    int GetColumnIndex(string name);

    /// <summary>
    /// Gets the value by column name.
    /// </summary>
    object? GetValue(string columnName);

    /// <summary>
    /// Gets a typed value by column name.
    /// </summary>
    T? Get<T>(string columnName);

    /// <summary>
    /// Gets a boolean value by column name.
    /// </summary>
    bool GetBoolean(string columnName);

    /// <summary>
    /// Gets a short value by column name.
    /// </summary>
    short GetShort(string columnName);

    /// <summary>
    /// Gets an integer value by column name.
    /// </summary>
    int GetInteger(string columnName);

    /// <summary>
    /// Gets a long value by column name.
    /// </summary>
    long GetLong(string columnName);

    /// <summary>
    /// Gets a float value by column name.
    /// </summary>
    float GetFloat(string columnName);

    /// <summary>
    /// Gets a double value by column name.
    /// </summary>
    double GetDouble(string columnName);

    /// <summary>
    /// Gets a string value by column name.
    /// </summary>
    string? GetString(string columnName);

    /// <summary>
    /// Gets an integer array by column name.
    /// </summary>
    int[]? GetIntegerArray(string columnName);

    /// <summary>
    /// Gets a long array by column name.
    /// </summary>
    long[]? GetLongArray(string columnName);

    /// <summary>
    /// Gets a string array by column name.
    /// </summary>
    string?[]? GetStringArray(string columnName);

    /// <summary>
    /// Gets a boolean array by column name.
    /// </summary>
    bool[]? GetBooleanArray(string columnName);

    /// <summary>
    /// Gets a double array by column name.
    /// </summary>
    double[]? GetDoubleArray(string columnName);

    /// <summary>
    /// Gets a Guid array by column name.
    /// </summary>
    Guid[]? GetGuidArray(string columnName);
}

/// <summary>
/// PostgreSQL row implementation.
/// </summary>
public sealed class PgRow : TupleBase, IRow
{
    private readonly object?[] _values;
    private readonly PgRowDescriptor _descriptor;

    internal PgRow(PgRowDescriptor descriptor)
    {
        _descriptor = descriptor;
        _values = new object?[descriptor.Columns.Length];
    }

    public override int Size => _values.Length;

    public override object? GetValue(int position) => _values[position];

    internal void SetValue(int position, object? value) => _values[position] = value;

    public string GetColumnName(int position) => _descriptor.Columns[position].Name;

    public int GetColumnIndex(string name)
    {
        for (int i = 0; i < _descriptor.Columns.Length; i++)
        {
            if (string.Equals(_descriptor.Columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
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
        return index >= 0 ? GetBoolean(index) : default;
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

/// <summary>
/// Row descriptor containing column information.
/// </summary>
public sealed class PgRowDescriptor
{
    public static readonly PgRowDescriptor Empty = new(PgColumnDesc.EmptyColumns);

    public PgColumnDesc[] Columns { get; }

    public PgRowDescriptor(PgColumnDesc[] columns)
    {
        Columns = columns;
    }

    public int ColumnCount => Columns.Length;

    public string? GetColumnName(int index) => index >= 0 && index < Columns.Length ? Columns[index].Name : null;
}
