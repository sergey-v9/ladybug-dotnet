using System;
using System.Runtime.InteropServices;
using LadybugDB.Interop;

namespace LadybugDB;

/// <summary>
/// Internal Arrow ingest seam consumed by the <c>LadybugDB.Arrow</c> package via
/// <c>InternalsVisibleTo</c>. Ownership of the schema and arrays is transferred to the engine on
/// success OR failure, per the C API contract — callers must NOT release them afterward.
/// </summary>
public sealed partial class Connection
{
    /// <summary>
    /// Ingests Arrow C-Data structs as a node table. <paramref name="schemaPtr"/> and
    /// <paramref name="arraysPtr"/> point at filled C-Data blocks whose ownership is transferred to
    /// the engine (success OR failure). Returns the result handle of the underlying CREATE; throws
    /// on failure.
    /// </summary>
    internal QueryResult CreateArrowTableInternal(string tableName, IntPtr schemaPtr, IntPtr arraysPtr, ulong numArrays)
    {
        if (tableName is null)
        {
            throw new ArgumentNullException(nameof(tableName));
        }

        ArrowSchema schema = Marshal.PtrToStructure<ArrowSchema>(schemaPtr);
        ArrowArray arrays = Marshal.PtrToStructure<ArrowArray>(arraysPtr);

        lock (_gate)
        {
            ThrowIfDisposed();
            LbugState state = Native.ConnectionCreateArrowTable(ref _handle, tableName, ref schema, ref arrays, numArrays, out LbugQueryResult resultHandle);
            return Finish(state, resultHandle);
        }
    }

    /// <summary>
    /// Ingests Arrow C-Data structs as a relationship table between <paramref name="fromTable"/> and
    /// <paramref name="toTable"/>. Ownership of the schema and arrays is transferred to the engine
    /// (success OR failure). Returns the result handle of the underlying CREATE; throws on failure.
    /// </summary>
    internal QueryResult CreateArrowRelTableInternal(string tableName, string fromTable, string toTable, IntPtr schemaPtr, IntPtr arraysPtr, ulong numArrays)
    {
        if (tableName is null)
        {
            throw new ArgumentNullException(nameof(tableName));
        }

        if (fromTable is null)
        {
            throw new ArgumentNullException(nameof(fromTable));
        }

        if (toTable is null)
        {
            throw new ArgumentNullException(nameof(toTable));
        }

        ArrowSchema schema = Marshal.PtrToStructure<ArrowSchema>(schemaPtr);
        ArrowArray arrays = Marshal.PtrToStructure<ArrowArray>(arraysPtr);

        lock (_gate)
        {
            ThrowIfDisposed();
            LbugState state = Native.ConnectionCreateArrowRelTable(ref _handle, tableName, fromTable, toTable, ref schema, ref arrays, numArrays, out LbugQueryResult resultHandle);
            return Finish(state, resultHandle);
        }
    }
}
