using System;
using System.Collections.Generic;

namespace LadybugDB.Extensions;

/// <summary>Typed, case-insensitive accessor over a single materialized result row.</summary>
public interface IRowAccessor
{
    /// <summary>Reads the value at <paramref name="i"/>, converted to <typeparamref name="T"/>.</summary>
    T Get<T>(int i);

    /// <summary>Reads the named column (case-insensitive), converted to <typeparamref name="T"/>.</summary>
    T Get<T>(string name);

    /// <summary>Reads the named column, or returns <paramref name="fallback"/> when null/missing.</summary>
    T GetOrDefault<T>(string name, T fallback);
}

/// <summary>In-memory <see cref="IRowAccessor"/> over an <c>object?[]</c> row and a shared column index.</summary>
internal sealed class RowAccessor : IRowAccessor
{
    private readonly object?[] _row;
    private readonly IReadOnlyDictionary<string, int> _columnIndex;

    public RowAccessor(object?[] row, IReadOnlyDictionary<string, int> columnIndex)
    {
        _row = row;
        _columnIndex = columnIndex;
    }

    public T Get<T>(int i)
    {
        if (i < 0 || i >= _row.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(i));
        }

        object? value = _row[i];
        if (value is null)
        {
            throw new InvalidOperationException(
                $"Value at column {i} is null. Use GetOrDefault for nullable access.");
        }

        return Convert<T>(value);
    }

    public T Get<T>(string name)
    {
        if (!_columnIndex.TryGetValue(name, out int i))
        {
            throw new KeyNotFoundException(
                $"Column '{name}' was not found. Available columns: {string.Join(", ", _columnIndex.Keys)}.");
        }

        return Get<T>(i);
    }

    public T GetOrDefault<T>(string name, T fallback)
    {
        if (!_columnIndex.TryGetValue(name, out int i) || i < 0 || i >= _row.Length)
        {
            return fallback;
        }

        object? value = _row[i];
        return value is null ? fallback : Convert<T>(value);
    }

    private static T Convert<T>(object value)
    {
        // Delegate to the same conversion shim the generated row mappers use (LadybugRowConvert.To<T>)
        // so this path handles enum / Guid / nullable-widening / DateOnly <-> DateTime identically
        // (DRY). Callers above have already filtered out null, so the value is non-null here.
        return LadybugRowConvert.To<T>(value);
    }
}
