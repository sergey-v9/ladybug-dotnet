using System;
using LadybugDB.Interop;

namespace LadybugDB;

/// <summary>
/// Internal Arrow ingest seam consumed by the <c>LadybugDB.Arrow</c> package via
/// <c>InternalsVisibleTo</c>. The engine MOVES the inner C-Data buffers out of the shells (nulling
/// each shell's <c>release</c> callback) on success OR failure, per the C API contract — the caller
/// must NOT re-release the contents, but it still owns and must free the outer shell allocations
/// themselves once this returns (ARROW-1).
/// </summary>
public sealed partial class Connection
{
    /// <summary>
    /// Ingests Arrow C-Data structs as a node table. <paramref name="schemaPtr"/> and
    /// <paramref name="arraysPtr"/> point at filled C-Data shells; the engine moves their contents out
    /// (success OR failure). Returns the result handle of the underlying CREATE; throws on failure. The
    /// caller still owns the outer shell allocations and frees them after this returns.
    /// </summary>
    internal QueryResult CreateArrowTableInternal(string tableName, IntPtr schemaPtr, IntPtr arraysPtr, ulong numArrays)
    {
        tableName = ThrowHelpers.ThrowIfNull(tableName, nameof(tableName));

        // ARROW-1: pass the caller's original C-Data shell pointers straight through. The previous
        // PtrToStructure round-trip handed the engine a pinned COPY and left the real shells (which the
        // caller in LadybugDB.Arrow frees afterward) pointing at buffers the engine never saw.
        lock (_gate)
        {
            ThrowIfDisposed();
            LbugState state = Native.ConnectionCreateArrowTable(ref _handle, tableName, schemaPtr, arraysPtr, numArrays, out LbugQueryResult resultHandle);
            return Finish(state, resultHandle);
        }
    }

    /// <summary>
    /// Ingests Arrow C-Data structs as a relationship table between <paramref name="fromTable"/> and
    /// <paramref name="toTable"/>. The engine moves the shell contents out (success OR failure).
    /// Returns the result handle of the underlying CREATE; throws on failure. The caller still owns the
    /// outer shell allocations and frees them after this returns.
    /// </summary>
    internal QueryResult CreateArrowRelTableInternal(string tableName, string fromTable, string toTable, IntPtr schemaPtr, IntPtr arraysPtr, ulong numArrays)
    {
        tableName = ThrowHelpers.ThrowIfNull(tableName, nameof(tableName));
        fromTable = ThrowHelpers.ThrowIfNull(fromTable, nameof(fromTable));
        toTable = ThrowHelpers.ThrowIfNull(toTable, nameof(toTable));

        // ARROW-1: pass the caller's original C-Data shell pointers straight through (see
        // CreateArrowTableInternal). The caller in LadybugDB.Arrow frees the shells after this returns.
        lock (_gate)
        {
            ThrowIfDisposed();
            LbugState state = Native.ConnectionCreateArrowRelTable(ref _handle, tableName, fromTable, toTable, schemaPtr, arraysPtr, numArrays, out LbugQueryResult resultHandle);
            return Finish(state, resultHandle);
        }
    }
}
