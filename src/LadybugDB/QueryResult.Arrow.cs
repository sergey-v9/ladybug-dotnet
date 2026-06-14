using System.Runtime.InteropServices;
using LadybugDB.Interop;

namespace LadybugDB;

/// <summary>
/// Raw Arrow C-Data-Interface export seam. Exposes the engine's Arrow schema and chunked arrays as
/// IntPtr-backed handles, with no managed Apache.Arrow dependency. The friendly
/// <c>Apache.Arrow</c>-typed layer lives in the separate <c>LadybugDB.Arrow</c> package.
/// </summary>
public sealed partial class QueryResult
{
    /// <summary>
    /// Exports the result's column schema as a raw Arrow C-Data <c>ArrowSchema</c>. The caller owns
    /// the returned handle and must dispose it (which invokes the Arrow release callback).
    /// </summary>
    public ArrowSchemaHandle GetArrowSchema()
    {
        ThrowIfDisposed();
        ArrowSchemaHandle handle = ArrowSchemaHandle.Allocate();
        try
        {
            LbugState state = Native.QueryResultGetArrowSchema(ref _handle, out ArrowSchema schema);
            if (state != LbugState.Success)
            {
                throw new LadybugException("Failed to export the Arrow schema for the query result.");
            }

            // The engine filled a blittable C-Data struct; move it (by value) into the handle's
            // stable unmanaged block so the Apache.Arrow importer can consume it via a pointer.
            Marshal.StructureToPtr(schema, handle.Ptr, fDeleteOld: false);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Exports the next chunk of up to <paramref name="chunkSize"/> rows as a raw Arrow C-Data
    /// <c>ArrowArray</c>. An empty/released chunk (see <see cref="ArrowArrayHandle.IsReleased"/>)
    /// signals end of stream. The caller owns and must dispose the returned handle.
    /// </summary>
    public ArrowArrayHandle GetNextArrowChunk(long chunkSize)
    {
        ThrowIfDisposed();
        ArrowArrayHandle handle = ArrowArrayHandle.Allocate();
        try
        {
            LbugState state = Native.QueryResultGetNextArrowChunk(ref _handle, chunkSize, out ArrowArray array);
            if (state != LbugState.Success)
            {
                throw new LadybugException("Failed to export the next Arrow chunk of the query result.");
            }

            Marshal.StructureToPtr(array, handle.Ptr, fDeleteOld: false);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }
}
