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
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

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
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

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
}
