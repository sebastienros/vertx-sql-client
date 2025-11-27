// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

namespace Vertx.PgClient;

/// <summary>
/// A general purpose tuple containing values.
/// </summary>
public interface ITuple
{
    /// <summary>
    /// Gets the value at the specified position.
    /// </summary>
    object? GetValue(int position);

    /// <summary>
    /// Gets the number of values in this tuple.
    /// </summary>
    int Size { get; }

    /// <summary>
    /// Gets a boolean value at the specified position.
    /// </summary>
    bool GetBoolean(int position);

    /// <summary>
    /// Gets a short value at the specified position.
    /// </summary>
    short GetShort(int position);

    /// <summary>
    /// Gets an integer value at the specified position.
    /// </summary>
    int GetInteger(int position);

    /// <summary>
    /// Gets a long value at the specified position.
    /// </summary>
    long GetLong(int position);

    /// <summary>
    /// Gets a float value at the specified position.
    /// </summary>
    float GetFloat(int position);

    /// <summary>
    /// Gets a double value at the specified position.
    /// </summary>
    double GetDouble(int position);

    /// <summary>
    /// Gets a string value at the specified position.
    /// </summary>
    string? GetString(int position);

    /// <summary>
    /// Gets a DateTime value at the specified position.
    /// </summary>
    DateTime GetDateTime(int position);

    /// <summary>
    /// Gets a DateTimeOffset value at the specified position.
    /// </summary>
    DateTimeOffset GetDateTimeOffset(int position);

    /// <summary>
    /// Gets a DateOnly value at the specified position.
    /// </summary>
    DateOnly GetDate(int position);

    /// <summary>
    /// Gets a TimeOnly value at the specified position.
    /// </summary>
    TimeOnly GetTime(int position);

    /// <summary>
    /// Gets a Guid value at the specified position.
    /// </summary>
    Guid GetGuid(int position);

    /// <summary>
    /// Gets a decimal value at the specified position.
    /// </summary>
    decimal GetDecimal(int position);

    /// <summary>
    /// Gets a byte array value at the specified position.
    /// </summary>
    byte[]? GetBytes(int position);

    /// <summary>
    /// Gets a typed value at the specified position.
    /// </summary>
    T? Get<T>(int position);
}

/// <summary>
/// Base implementation of ITuple.
/// </summary>
public abstract class TupleBase : ITuple
{
    public abstract object? GetValue(int position);
    public abstract int Size { get; }

    public bool GetBoolean(int position) => Convert.ToBoolean(GetValue(position));
    public short GetShort(int position) => Convert.ToInt16(GetValue(position));
    public int GetInteger(int position) => Convert.ToInt32(GetValue(position));
    public long GetLong(int position) => Convert.ToInt64(GetValue(position));
    public float GetFloat(int position) => Convert.ToSingle(GetValue(position));
    public double GetDouble(int position) => Convert.ToDouble(GetValue(position));
    public string? GetString(int position) => GetValue(position)?.ToString();
    public DateTime GetDateTime(int position) => (DateTime)(GetValue(position) ?? default(DateTime));
    public DateTimeOffset GetDateTimeOffset(int position) => (DateTimeOffset)(GetValue(position) ?? default(DateTimeOffset));
    public DateOnly GetDate(int position) => (DateOnly)(GetValue(position) ?? default(DateOnly));
    public TimeOnly GetTime(int position) => (TimeOnly)(GetValue(position) ?? default(TimeOnly));
    public Guid GetGuid(int position) => (Guid)(GetValue(position) ?? Guid.Empty);
    public decimal GetDecimal(int position) => Convert.ToDecimal(GetValue(position));
    public byte[]? GetBytes(int position) => GetValue(position) as byte[];

    public T? Get<T>(int position)
    {
        var value = GetValue(position);
        if (value is null) return default;
        if (value is T typedValue) return typedValue;
        return (T)Convert.ChangeType(value, typeof(T));
    }
}

/// <summary>
/// A mutable tuple for building parameter values.
/// </summary>
public sealed class Tuple : TupleBase
{
    private readonly List<object?> _values = new();

    public override int Size => _values.Count;

    public override object? GetValue(int position) => _values[position];

    public Tuple AddValue(object? value)
    {
        _values.Add(value);
        return this;
    }

    public Tuple SetValue(int position, object? value)
    {
        while (_values.Count <= position)
        {
            _values.Add(null);
        }
        _values[position] = value;
        return this;
    }

    public static Tuple Of(params object?[] values)
    {
        var tuple = new Tuple();
        foreach (var value in values)
        {
            tuple.AddValue(value);
        }
        return tuple;
    }

    public static Tuple Create() => new();

    public static Tuple Create(object? value1)
    {
        var tuple = new Tuple();
        tuple.AddValue(value1);
        return tuple;
    }

    public static Tuple Create(object? value1, object? value2)
    {
        var tuple = new Tuple();
        tuple.AddValue(value1);
        tuple.AddValue(value2);
        return tuple;
    }

    public static Tuple Create(object? value1, object? value2, object? value3)
    {
        var tuple = new Tuple();
        tuple.AddValue(value1);
        tuple.AddValue(value2);
        tuple.AddValue(value3);
        return tuple;
    }

    public static Tuple Create(object? value1, object? value2, object? value3, object? value4)
    {
        var tuple = new Tuple();
        tuple.AddValue(value1);
        tuple.AddValue(value2);
        tuple.AddValue(value3);
        tuple.AddValue(value4);
        return tuple;
    }

    public static Tuple Create(object? value1, object? value2, object? value3, object? value4, object? value5)
    {
        var tuple = new Tuple();
        tuple.AddValue(value1);
        tuple.AddValue(value2);
        tuple.AddValue(value3);
        tuple.AddValue(value4);
        tuple.AddValue(value5);
        return tuple;
    }

    public static Tuple Create(object? value1, object? value2, object? value3, object? value4, object? value5, object? value6)
    {
        var tuple = new Tuple();
        tuple.AddValue(value1);
        tuple.AddValue(value2);
        tuple.AddValue(value3);
        tuple.AddValue(value4);
        tuple.AddValue(value5);
        tuple.AddValue(value6);
        return tuple;
    }
}
