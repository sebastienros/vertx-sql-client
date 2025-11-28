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
    private Tuple()
    {
        _values = new ();
    }

    private Tuple(List<PgValue> values)
    {
        _values = values;
    }

    private readonly List<PgValue> _values;

    public override int Size => _values.Count;

    public override PgValue GetValue(int position) => _values[position];

    public Tuple AddValue<T>(T? value)
    {
        _values.Add(PgValue.From(value));
        return this;
    }

    public Tuple AddValue(PgValue value)
    {
        _values.Add(value);
        return this;
    }

    public Tuple SetValue<T>(int position, T? value)
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

    public static Tuple Create<T>(T? value1)
    {
        return new Tuple([PgValue.From(value1)]);
    }

    public static Tuple Create(object? value1, object? value2)
    {
        return new Tuple([PgValue.From(value1), PgValue.From(value2)]);
    }

    public static Tuple Create(object? value1, object? value2, object? value3)
    {
        return new Tuple([PgValue.From(value1), PgValue.From(value2), PgValue.From(value3)]);
    }

    public static Tuple Create(object? value1, object? value2, object? value3, object? value4)
    {
        return new Tuple([PgValue.From(value1), PgValue.From(value2), PgValue.From(value3), PgValue.From(value4)]);
    }

    public static Tuple Create(object? value1, object? value2, object? value3, object? value4, object? value5)
    {
        return new Tuple([PgValue.From(value1), PgValue.From(value2), PgValue.From(value3), PgValue.From(value4), PgValue.From(value5)]);
    }

    public static Tuple Create(object? value1, object? value2, object? value3, object? value4, object? value5, object? value6)
    {
        return new Tuple([PgValue.From(value1), PgValue.From(value2), PgValue.From(value3), PgValue.From(value4), PgValue.From(value5), PgValue.From(value6)]);
    }
}
