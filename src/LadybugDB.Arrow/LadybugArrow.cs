using System;
using System.Collections.Generic;
using Apache.Arrow;
using Apache.Arrow.C;

namespace LadybugDB.Arrow;

/// <summary>
/// Apache.Arrow-typed interop over LadybugDB's raw Arrow C-Data export/ingest seams. Reads query
/// results as <see cref="Schema"/>/<see cref="RecordBatch"/> and ingests a <see cref="RecordBatch"/>
/// back into the engine as a node or relationship table.
/// </summary>
public static partial class LadybugArrow
{
    /// <summary>Reads the result's Arrow <see cref="Schema"/> (column names + Arrow types).</summary>
    public static Schema ReadSchema(this QueryResult result)
    {
        ThrowHelpers.ThrowIfNull(result, nameof(result));

        ArrowSchemaHandle handle = result.GetArrowSchema();
        try
        {
            // ImportSchema MOVES the C struct (it owns the contents and calls release on disposal of
            // the returned managed wrapper), so the handle must free only its block, not re-release.
            unsafe
            {
                return CArrowSchemaImporter.ImportSchema((CArrowSchema*)handle.Ptr);
            }
        }
        finally
        {
            handle.DetachAndFree();
        }
    }

    /// <summary>
    /// Streams the result as a sequence of <see cref="RecordBatch"/>es of up to
    /// <paramref name="chunkSize"/> rows each. The schema is read once; each batch is imported from a
    /// raw Arrow chunk. Enumeration stops when the engine returns an empty (released) chunk.
    /// </summary>
    public static IEnumerable<RecordBatch> ReadBatches(this QueryResult result, long chunkSize = 1_000_000)
    {
        ThrowHelpers.ThrowIfNull(result, nameof(result));

        Schema schema = result.ReadSchema();
        while (true)
        {
            ArrowArrayHandle chunk = result.GetNextArrowChunk(chunkSize);
            if (chunk.IsReleased || chunk.Length == 0)
            {
                chunk.Dispose();
                yield break;
            }

            RecordBatch batch;
            unsafe
            {
                batch = CArrowArrayImporter.ImportRecordBatch((CArrowArray*)chunk.Ptr, schema);
            }

            // The importer took ownership of the ArrowArray contents (and releases them on the
            // batch's disposal); free only our unmanaged allocation without re-invoking release.
            chunk.DetachAndFree();
            yield return batch;
        }
    }

    /// <summary>
    /// Ingests an Apache.Arrow <see cref="RecordBatch"/> as an in-memory node table named
    /// <paramref name="tableName"/>. Ownership of the exported Arrow structs is transferred to the
    /// engine, matching the native <c>lbug_connection_create_arrow_table</c> contract — the caller
    /// keeps full ownership of the original managed <paramref name="batch"/>.
    /// </summary>
    public static void CreateArrowTable(this Connection connection, string tableName, RecordBatch batch)
    {
        ThrowHelpers.ThrowIfNull(connection, nameof(connection));
        ThrowHelpers.ThrowIfNull(tableName, nameof(tableName));
        ThrowHelpers.ThrowIfNull(batch, nameof(batch));

        // Clone into Arrow-owned (allocator-backed) memory first: a batch IMPORTED from the engine's
        // C-Data export wraps externally-owned buffers that the C-Data exporter cannot re-export
        // ("failed on buffer #0"). The clone owns native buffers the exporter can hand off cleanly.
        using RecordBatch exportable = batch.Clone();
        unsafe
        {
            // Allocate the outer C-Data shells via Apache.Arrow's own allocator (HGlobal). The engine
            // MOVES the inner buffers out of these shells and nulls each shell's release callback
            // (success OR failure), so we must NOT re-release the contents — but we DO own the outer
            // shell allocations and free them in a finally below (ARROW-1: they used to leak).
            CArrowSchema* cSchema = CArrowSchema.Create();
            CArrowArray* cArray = CArrowArray.Create();
            try
            {
                CArrowSchemaExporter.ExportSchema(exportable.Schema, cSchema);
                CArrowArrayExporter.ExportRecordBatch(exportable, cArray);

                // numArrays = 1: a single contiguous Arrow struct-array for the whole batch.
                using QueryResult result = connection.CreateArrowTableInternal(tableName, (IntPtr)cSchema, (IntPtr)cArray, 1UL);
                GC.KeepAlive(result);
            }
            finally
            {
                // After the engine nulled the release callbacks, Free only FreeHGlobals the shells (no
                // double-release). Runs even if the export or ingest threw, freeing the shells we own.
                CArrowArray.Free(cArray);
                CArrowSchema.Free(cSchema);
            }
        }
    }

    /// <summary>
    /// Ingests an Apache.Arrow <see cref="RecordBatch"/> as an in-memory relationship table named
    /// <paramref name="tableName"/> connecting <paramref name="fromTable"/> to
    /// <paramref name="toTable"/>. The batch must carry the internal-id columns the engine expects
    /// (typically <c>FROM</c>/<c>TO</c> plus any rel properties). Ownership of the exported Arrow
    /// structs is transferred to the engine; the caller keeps the managed <paramref name="batch"/>.
    /// </summary>
    public static void CreateArrowRelTable(this Connection connection, string tableName, RecordBatch batch, string fromTable, string toTable)
    {
        ThrowHelpers.ThrowIfNull(connection, nameof(connection));
        ThrowHelpers.ThrowIfNull(tableName, nameof(tableName));
        ThrowHelpers.ThrowIfNull(batch, nameof(batch));
        ThrowHelpers.ThrowIfNull(fromTable, nameof(fromTable));
        ThrowHelpers.ThrowIfNull(toTable, nameof(toTable));

        // See CreateArrowTable: clone into allocator-backed memory so an imported batch re-exports,
        // and free the outer C-Data shells in a finally after the engine moves their contents out.
        using RecordBatch exportable = batch.Clone();
        unsafe
        {
            CArrowSchema* cSchema = CArrowSchema.Create();
            CArrowArray* cArray = CArrowArray.Create();
            try
            {
                CArrowSchemaExporter.ExportSchema(exportable.Schema, cSchema);
                CArrowArrayExporter.ExportRecordBatch(exportable, cArray);

                using QueryResult result = connection.CreateArrowRelTableInternal(tableName, fromTable, toTable, (IntPtr)cSchema, (IntPtr)cArray, 1UL);
                GC.KeepAlive(result);
            }
            finally
            {
                // After the engine nulled the release callbacks, Free only FreeHGlobals the shells.
                CArrowArray.Free(cArray);
                CArrowSchema.Free(cSchema);
            }
        }

        // NOTE (CSR best-effort, D6): lbug_connection_create_arrow_rel_table_csr (CSR ingest, and the
        // CSRResult read shape) is intentionally deferred. The interop shim exists
        // (Native.ConnectionCreateArrowRelTableCsr), but a correct managed surface needs paired
        // indices+indptr Arrow exports and a CSR result reader, which exceeds the low-cost bar for
        // this workstream. Tracked in docs/parity-2026-06/parity-tracking.md.
    }
}
