using System;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests.Arrow;

/// <summary>
/// Pure-managed lifetime tests for the IntPtr-backed Arrow C-Data handles (no native engine needed)
/// plus native-gated round-trips over the raw export seam.
/// </summary>
public sealed class ArrowHandleTests
{
    [Fact]
    public void SchemaHandle_AllocatesPtr_AndZeroesIt()
    {
        using var handle = ArrowSchemaHandle.Allocate();
        Assert.NotEqual(IntPtr.Zero, handle.Ptr);
        // A freshly-allocated, unfilled ArrowSchema has a null release pointer; Dispose must be safe
        // (it must not invoke a release callback) in that state.
    }

    [Fact]
    public void SchemaHandle_DoubleDispose_IsSafe()
    {
        var handle = ArrowSchemaHandle.Allocate();
        IntPtr ptr = handle.Ptr;
        handle.Dispose();
        handle.Dispose(); // idempotent
        GC.KeepAlive(ptr);
    }

    [Fact]
    public void SchemaHandle_PtrThrowsAfterDispose()
    {
        var handle = ArrowSchemaHandle.Allocate();
        handle.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = handle.Ptr; });
    }

    [Fact]
    public void ArrayHandle_AllocatesPtr_AndDoubleDisposeIsSafe()
    {
        var handle = ArrowArrayHandle.Allocate();
        Assert.NotEqual(IntPtr.Zero, handle.Ptr);
        handle.Dispose();
        handle.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = handle.Ptr; });
    }

    [Fact]
    public void ArrayHandle_FreshlyAllocated_IsReleased_AndZeroLength()
    {
        using var handle = ArrowArrayHandle.Allocate();
        // Zero-initialized block: null release pointer => treated as end-of-stream; length 0.
        Assert.True(handle.IsReleased);
        Assert.Equal(0L, handle.Length);
    }

    [SkippableFact]
    public void GetArrowSchema_ReturnsLiveHandle()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);
            conn.Query("CREATE NODE TABLE T(id INT64, name STRING, PRIMARY KEY(id))").Dispose();
            conn.Query("CREATE (:T {id: 1, name: 'a'})").Dispose();

            using QueryResult r = conn.Query("MATCH (t:T) RETURN t.id, t.name");
            using ArrowSchemaHandle schema = r.GetArrowSchema();
            Assert.NotEqual(IntPtr.Zero, schema.Ptr);

            using ArrowArrayHandle chunk = r.GetNextArrowChunk(1024);
            Assert.NotEqual(IntPtr.Zero, chunk.Ptr);
            Assert.Equal(1L, chunk.Length);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }
}
