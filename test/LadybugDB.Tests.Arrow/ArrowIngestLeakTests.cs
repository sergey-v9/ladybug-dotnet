using System;
using System.Collections.Generic;
using System.Linq;
using Apache.Arrow;
using Apache.Arrow.C;
using Apache.Arrow.Types;
using LadybugDB;
using LadybugDB.Arrow;
using Xunit;

namespace LadybugDB.Tests.Arrow;

/// <summary>
/// Regression coverage for ARROW-1: the Arrow ingest path used to leak the <c>Create()</c>-allocated
/// outer C-Data shells (~152 bytes per call) on every <see cref="LadybugArrow.CreateArrowTable"/> /
/// <see cref="LadybugArrow.CreateArrowRelTable"/>. The engine moves the inner buffers out, so this was
/// a small unconditional unmanaged leak (no use-after-free). The fix frees the outer shells in a
/// <c>finally</c> via <c>CArrowSchema.Free</c>/<c>CArrowArray.Free</c>.
/// <para>
/// The leak itself (152 B/call) is far smaller than the engine's own per-CREATE catalog/buffer
/// churn, so a process-memory delta cannot isolate it — measured growth is dominated by the engine by
/// orders of magnitude. Instead we (1) pin down the exact freeing primitive the fix relies on with a
/// pure-managed, engine-free unit test that mirrors the fix's allocate -> export -> engine-consumes ->
/// Free sequence (the "new guard logic"), and (2) smoke-test the real ingest path over many
/// iterations to confirm the fixed code runs cleanly end-to-end.
/// </para>
/// </summary>
public sealed class ArrowIngestLeakTests
{
    /// <summary>
    /// Pure-managed proof of the fix's <c>finally</c> logic — no native engine required. Mirrors what
    /// <see cref="LadybugArrow.CreateArrowTable"/> does: allocate the outer shells, export a real batch
    /// into them, let a consumer MOVE the structs out (the importers consume the source exactly like
    /// the engine does — nulling each shell's release callback per the Arrow C-Data move protocol),
    /// then free the shells. Repeated many times: this must neither double-release nor crash, which is
    /// precisely why the fix can safely call <c>Free</c> after the engine call.
    /// </summary>
    [Fact]
    public void Free_AfterConsumerMovedStruct_OnlyFreesShell_NoDoubleRelease()
    {
        using RecordBatch built = BuildSingleRowBatch(7);

        for (int i = 0; i < 10_000; i++)
        {
            // Clone into allocator-backed buffers the C-Data exporter can hand off, fresh each
            // iteration — exactly as the production path does before exporting (a builder-backed batch,
            // or one whose buffers were already moved out by a prior export, fails to re-export).
            using RecordBatch source = built.Clone();
            unsafe
            {
                CArrowSchema* cSchema = CArrowSchema.Create();
                CArrowArray* cArray = CArrowArray.Create();
                Schema? importedSchema = null;
                RecordBatch? importedBatch = null;
                try
                {
                    CArrowSchemaExporter.ExportSchema(source.Schema, cSchema);
                    CArrowArrayExporter.ExportRecordBatch(source, cArray);

                    // A consumer MOVES the C structs out of the shells (same move semantics the engine
                    // uses on ingest): the importer takes ownership of the inner buffers and nulls the
                    // source shell's release callback. After this, the shells are spent.
                    importedSchema = CArrowSchemaImporter.ImportSchema(cSchema);
                    importedBatch = CArrowArrayImporter.ImportRecordBatch(cArray, importedSchema);
                    Assert.Equal(1, importedBatch.Length);
                }
                finally
                {
                    // After the move nulled the release callbacks, Free only FreeHGlobals the shells —
                    // no callback re-invocation, no double-release. This is the exact call the fix makes
                    // after the engine consumes the structs.
                    CArrowArray.Free(cArray);
                    CArrowSchema.Free(cSchema);
                    importedBatch?.Dispose();
                    importedSchema = null;
                }
            }
        }
    }

    /// <summary>
    /// Engine-backed smoke check: the fixed ingest path runs cleanly over many iterations and the data
    /// survives the round trip. Each iteration re-CREATEs the node table from a fresh Arrow export,
    /// exercising the full shell allocate/export/ingest/free seam the fix touches.
    /// </summary>
    [SkippableFact]
    public void RepeatedNodeIngest_CompletesCleanly_SmokeCheck()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            for (int i = 0; i < 100; i++)
            {
                conn.Query("DROP TABLE IF EXISTS Smoke").Dispose();
                using RecordBatch batch = BuildSingleRowBatch(i);
                conn.CreateArrowTable("Smoke", batch);
            }

            // Each call CREATEs the table fresh, so the final state holds exactly the last row.
            using QueryResult r = conn.Query("MATCH (n:Smoke) RETURN n.id AS id");
            List<long?> ids = r.ReadBatches()
                .SelectMany(b => Enumerable.Range(0, b.Length).Select(j => ((Int64Array)b.Column("id")).GetValue(j)))
                .ToList();
            Assert.Equal(new long?[] { 99L }, ids);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    /// <summary>
    /// Engine-backed smoke check for the relationship ingest path (the other call site the fix freed).
    /// Re-CREATEs the rel table from a fresh Arrow export many times and confirms the edges survive.
    /// </summary>
    [SkippableFact]
    public void RepeatedRelIngest_CompletesCleanly_SmokeCheck()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);
            conn.Query("CREATE NODE TABLE Person(id INT64, PRIMARY KEY(id))").Dispose();
            conn.Query("CREATE (:Person {id: 1})").Dispose();
            conn.Query("CREATE (:Person {id: 2})").Dispose();

            for (int i = 0; i < 100; i++)
            {
                conn.Query("DROP TABLE IF EXISTS Knows").Dispose();
                using RecordBatch batch = BuildSingleEdgeBatch(1, 2, since: 2000 + i);
                conn.CreateArrowRelTable("Knows", batch, "Person", "Person");
            }

            using QueryResult r = conn.Query(
                "MATCH (a:Person)-[k:Knows]->(b:Person) RETURN a.id AS src, b.id AS dst, k.since AS since");
            List<object?[]> rows = r.Rows().ToList();
            Assert.Single(rows);
            Assert.Equal(new object?[] { 1L, 2L, 2099L }, rows[0]);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    private static RecordBatch BuildSingleRowBatch(long id)
    {
        var idField = new Field("id", Int64Type.Default, nullable: false);
        var schema = new Schema(new[] { idField }, metadata: null);
        var idArray = new Int64Array.Builder().Append(id).Build();
        return new RecordBatch(schema, new IArrowArray[] { idArray }, length: 1);
    }

    private static RecordBatch BuildSingleEdgeBatch(long from, long to, long since)
    {
        var fromField = new Field("from", Int64Type.Default, nullable: false);
        var toField = new Field("to", Int64Type.Default, nullable: false);
        var sinceField = new Field("since", Int64Type.Default, nullable: false);
        var schema = new Schema(new[] { fromField, toField, sinceField }, metadata: null);

        var fromArray = new Int64Array.Builder().Append(from).Build();
        var toArray = new Int64Array.Builder().Append(to).Build();
        var sinceArray = new Int64Array.Builder().Append(since).Build();
        return new RecordBatch(schema, new IArrowArray[] { fromArray, toArray, sinceArray }, length: 1);
    }
}
