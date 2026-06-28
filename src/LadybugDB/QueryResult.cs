using System;
using System.Collections.Generic;
using System.Threading;
using LadybugDB.Interop;

namespace LadybugDB;

/// <summary>
/// The result of executing a Cypher query: column metadata plus a forward-only stream of rows.
/// </summary>
public sealed partial class QueryResult : IDisposable
{
    private LbugQueryResult _handle;
    private int _disposed;
    private string[]? _columnNames;
    private QuerySummary? _summary;
    private ColumnSchema[]? _columns;

    internal QueryResult(LbugQueryResult handle)
    {
        _handle = handle;
    }

    /// <summary>Whether the query executed successfully.</summary>
    public bool IsSuccess => Volatile.Read(ref _disposed) == 0 && Native.QueryResultIsSuccess(ref _handle);

    /// <summary>The error message for a failed query, or <see langword="null"/> on success.</summary>
    public string? GetErrorMessage()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return null;
        }

        return Native.TakeString(Native.QueryResultGetErrorMessage(ref _handle));
    }

    /// <summary>Number of columns in the result.</summary>
    public ulong ColumnCount
    {
        get
        {
            ThrowIfDisposed();
            return Native.QueryResultGetNumColumns(ref _handle);
        }
    }

    /// <summary>Number of tuples (rows) in the result.</summary>
    public ulong RowCount
    {
        get
        {
            ThrowIfDisposed();
            return Native.QueryResultGetNumTuples(ref _handle);
        }
    }

    /// <summary>Compilation and execution timings for this query, read lazily and cached.</summary>
    public QuerySummary Summary
    {
        get
        {
            ThrowIfDisposed();
            if (_summary is QuerySummary cached)
            {
                return cached;
            }

            LbugState state = Native.QueryResultGetQuerySummary(ref _handle, out LbugQuerySummary native);
            if (state != LbugState.Success)
            {
                throw new LadybugException("Failed to read the query summary.");
            }

            try
            {
                var summary = new QuerySummary(
                    Native.QuerySummaryGetCompilingTime(ref native),
                    Native.QuerySummaryGetExecutionTime(ref native));
                _summary = summary;
                return summary;
            }
            finally
            {
                Native.QuerySummaryDestroy(ref native);
            }
        }
    }

    /// <summary>The name of the column at the given zero-based index.</summary>
    public string GetColumnName(ulong index)
    {
        ThrowIfDisposed();
        LbugState state = Native.QueryResultGetColumnName(ref _handle, index, out IntPtr pointer);
        if (state != LbugState.Success)
        {
            throw new LadybugException($"Failed to read column name at index {index}.");
        }

        return Native.TakeString(pointer) ?? string.Empty;
    }

    /// <summary>The logical type of the column at the given zero-based index.</summary>
    public LogicalType GetColumnType(ulong index)
    {
        ThrowIfDisposed();
        LbugState state = Native.QueryResultGetColumnDataType(ref _handle, index, out LbugLogicalType native);
        if (state != LbugState.Success)
        {
            throw new LadybugException($"Failed to read the data type of column {index}.");
        }

        return LogicalType.FromOwnedHandle(ref native);
    }

    /// <summary>All column names, cached after first access.</summary>
    public IReadOnlyList<string> ColumnNames
    {
        get
        {
            if (_columnNames is null)
            {
                ulong count = ColumnCount;
                var names = new string[count];
                for (ulong i = 0; i < count; i++)
                {
                    names[i] = GetColumnName(i);
                }

                _columnNames = names;
            }

            return _columnNames;
        }
    }

    /// <summary>All columns (name + logical type), built once and cached.</summary>
    public IReadOnlyList<ColumnSchema> Columns
    {
        get
        {
            ThrowIfDisposed();
            if (_columns is not null)
            {
                return _columns;
            }

            int count = checked((int)ColumnCount);
            var schemas = new ColumnSchema[count];
            for (int i = 0; i < count; i++)
            {
                schemas[i] = new ColumnSchema(GetColumnName((ulong)i), GetColumnType((ulong)i));
            }

            _columns = schemas;
            return _columns;
        }
    }

    /// <summary>Whether another tuple is available from the current iterator position.</summary>
    public bool HasNext()
    {
        ThrowIfDisposed();
        return Native.QueryResultHasNext(ref _handle);
    }

    /// <summary>
    /// Advances to and returns the next tuple. The returned <see cref="FlatTuple"/> shares the
    /// engine's reusable buffer; consume it before calling this again.
    /// </summary>
    public FlatTuple GetNext()
    {
        ThrowIfDisposed();
        LbugState state = Native.QueryResultGetNext(ref _handle, out LbugFlatTuple tuple);
        if (state != LbugState.Success)
        {
            throw new LadybugException("Failed to advance the query result.");
        }

        return new FlatTuple(tuple);
    }

    /// <summary>Rewinds the tuple iterator to the first row so the result can be re-read.</summary>
    public void ResetIterator()
    {
        ThrowIfDisposed();
        Native.QueryResultResetIterator(ref _handle);
    }

    /// <summary>Whether another result set follows this one (multi-statement queries).</summary>
    public bool HasNextQueryResult()
    {
        ThrowIfDisposed();
        return Native.QueryResultHasNextQueryResult(ref _handle);
    }

    /// <summary>
    /// Returns the next result set in a multi-statement query. The returned result is an independent
    /// <see cref="QueryResult"/> that owns and destroys its own native handle.
    /// </summary>
    public QueryResult GetNextQueryResult()
    {
        ThrowIfDisposed();
        LbugState state = Native.QueryResultGetNextQueryResult(ref _handle, out LbugQueryResult next);
        if (state != LbugState.Success)
        {
            throw new LadybugException("Failed to advance to the next query result.");
        }

        return new QueryResult(next);
    }

    /// <summary>
    /// Enumerates the result as fully materialized rows. Each row is read into managed memory before
    /// the iterator advances, which makes it safe against the engine's reused tuple buffer.
    /// </summary>
    public IEnumerable<object?[]> Rows()
    {
        ThrowIfDisposed();
        if (!HasNext())
        {
            yield break;
        }

        IReadOnlyList<ColumnSchema> schemas = Columns;
        int columns = schemas.Count;
        do
        {
            using FlatTuple tuple = GetNext();
            var row = new object?[columns];
            for (int i = 0; i < columns; i++)
            {
                row[i] = ReadCell(tuple, i, schemas[i].Type.Id);
            }

            yield return row;
        }
        while (HasNext());
    }

    /// <inheritdoc />
    public override string? ToString()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return null;
        }

        return Native.TakeString(Native.QueryResultToString(ref _handle));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Native.QueryResultDestroy(ref _handle);
    }

    /// <summary>
    /// Internal seam for the Arrow partial (WS-E): runs <paramref name="action"/> against the raw
    /// native query-result handle under the disposal guard. Not part of the public surface.
    /// </summary>
    internal T WithHandle<T>(HandleFunc<T> action)
    {
        ThrowIfDisposed();
        return action(ref _handle);
    }

    /// <summary>Delegate that operates on the native query-result handle.</summary>
    internal delegate T HandleFunc<T>(ref Interop.LbugQueryResult handle);

    private static object? ReadCell(FlatTuple tuple, int index, DataTypeId typeId)
    {
        switch (typeId)
        {
            case DataTypeId.Bool:
                return tuple.TryGetBool(index, out bool boolValue) ? boolValue : null;
            case DataTypeId.Int8:
                return tuple.TryGetInt8(index, out sbyte int8Value) ? int8Value : null;
            case DataTypeId.Int16:
                return tuple.TryGetInt16(index, out short int16Value) ? int16Value : null;
            case DataTypeId.Int32:
                return tuple.TryGetInt32(index, out int int32Value) ? int32Value : null;
            case DataTypeId.Int64:
            case DataTypeId.Serial:
                return tuple.TryGetInt64(index, out long int64Value) ? int64Value : null;
            case DataTypeId.UInt8:
                return tuple.TryGetUInt8(index, out byte uint8Value) ? uint8Value : null;
            case DataTypeId.UInt16:
                return tuple.TryGetUInt16(index, out ushort uint16Value) ? uint16Value : null;
            case DataTypeId.UInt32:
                return tuple.TryGetUInt32(index, out uint uint32Value) ? uint32Value : null;
            case DataTypeId.UInt64:
                return tuple.TryGetUInt64(index, out ulong uint64Value) ? uint64Value : null;
            case DataTypeId.Float:
                return tuple.TryGetFloat(index, out float floatValue) ? floatValue : null;
            case DataTypeId.Double:
                return tuple.TryGetDouble(index, out double doubleValue) ? doubleValue : null;
            case DataTypeId.String:
                return tuple.GetString(index);
            default:
                using (Value value = tuple.GetValue(index))
                {
                    return value.GetValue();
                }
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(QueryResult));
        }
    }
}
