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
    PgValue GetValue(int position);

    /// <summary>
    /// Gets the number of values in this tuple.
    /// </summary>
    int Size { get; }
}

/// <summary>
/// Base implementation of ITuple.
/// </summary>
public abstract class TupleBase : ITuple
{
    public abstract PgValue GetValue(int position);
    public abstract int Size { get; }
}

/// <summary>
/// A mutable tuple for building parameter values.
/// </summary>
public sealed class Tuple : TupleBase
{
    private readonly List<PgValue> _values = new();

    public override int Size => _values.Count;

    public override PgValue GetValue(int position) => _values[position];

    public Tuple AddValue(object? value)
    {
        _values.Add(PgValue.From(value));
        return this;
    }

    public Tuple AddValue(PgValue value)
    {
        _values.Add(value);
        return this;
    }

    public Tuple SetValue(int position, object? value)
    {
        while (_values.Count <= position)
        {
            _values.Add(PgValue.Null);
        }
        _values[position] = PgValue.From(value);
        return this;
    }

    public Tuple SetValue(int position, PgValue value)
    {
        while (_values.Count <= position)
        {
            _values.Add(PgValue.Null);
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
