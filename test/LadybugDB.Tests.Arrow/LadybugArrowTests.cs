using System.Collections.Generic;
using System.Linq;
using Apache.Arrow;
using Apache.Arrow.Types;
using LadybugDB;
using LadybugDB.Arrow;
using Xunit;

namespace LadybugDB.Tests.Arrow;

/// <summary>
/// Native-gated round-trips through the Apache.Arrow layer: query results read as
/// <see cref="Schema"/>/<see cref="RecordBatch"/> and ingested back as engine tables.
/// </summary>
public sealed class LadybugArrowTests
{
    [SkippableFact]
    public void ReadSchema_And_ReadBatches_RoundTripsAQuery()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);
            conn.Query("CREATE NODE TABLE Person(id INT64, name STRING, PRIMARY KEY(id))").Dispose();
            conn.Query("CREATE (:Person {id: 1, name: 'Alice'})").Dispose();
            conn.Query("CREATE (:Person {id: 2, name: 'Bob'})").Dispose();

            using QueryResult r = conn.Query("MATCH (p:Person) RETURN p.id AS id, p.name AS name ORDER BY id");

            Schema schema = r.ReadSchema();
            Assert.Equal(2, schema.FieldsList.Count);
            Assert.Equal("id", schema.FieldsList[0].Name);
            Assert.Equal("name", schema.FieldsList[1].Name);

            List<RecordBatch> batches = r.ReadBatches().ToList();
            long totalRows = batches.Sum(b => (long)b.Length);
            Assert.Equal(2L, totalRows);

            var idCol = (Int64Array)batches[0].Column("id");
            Assert.Equal(1L, idCol.GetValue(0));
            Assert.Equal(2L, idCol.GetValue(1));

            foreach (RecordBatch b in batches)
            {
                b.Dispose();
            }
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public void CreateArrowTable_IngestsARecordBatch()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            using RecordBatch batch = BuildPeopleBatch((10, "x"), (20, "y"));
            conn.CreateArrowTable("Imported", batch);

            using QueryResult r = conn.Query("MATCH (n:Imported) RETURN n.id AS id ORDER BY id");
            List<long?> ids = r.ReadBatches()
                .SelectMany(b => Enumerable.Range(0, b.Length).Select(i => ((Int64Array)b.Column("id")).GetValue(i)))
                .ToList();
            Assert.Equal(new long?[] { 10L, 20L }, ids);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public void FullRoundTrip_QueryToRecordBatchAndBackToTable()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);
            conn.Query("CREATE NODE TABLE Src(id INT64, name STRING, PRIMARY KEY(id))").Dispose();
            conn.Query("CREATE (:Src {id: 1, name: 'Alice'})").Dispose();
            conn.Query("CREATE (:Src {id: 2, name: 'Bob'})").Dispose();

            // 1) Query -> Arrow RecordBatches.
            List<RecordBatch> batches;
            using (QueryResult r = conn.Query("MATCH (s:Src) RETURN s.id AS id, s.name AS name ORDER BY id"))
            {
                batches = r.ReadBatches().ToList();
            }

            Assert.Equal(2L, batches.Sum(b => (long)b.Length));

            // 2) Ingest the first batch back as a new table.
            conn.CreateArrowTable("Copy", batches[0]);
            foreach (RecordBatch b in batches)
            {
                b.Dispose();
            }

            // 3) Query the new table and confirm the data survived the round trip.
            using QueryResult check = conn.Query("MATCH (c:Copy) RETURN c.id AS id, c.name AS name ORDER BY id");
            List<object?[]> rows = check.Rows().ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal(1L, rows[0][0]);
            Assert.Equal("Alice", rows[0][1]);
            Assert.Equal(2L, rows[1][0]);
            Assert.Equal("Bob", rows[1][1]);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public void CreateArrowRelTable_IngestsRelationships()
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
            conn.Query("CREATE (:Person {id: 3})").Dispose();

            // create_arrow_rel_table CREATES the rel table from the Arrow data, so it must not
            // already exist. The engine requires endpoint columns named "from"/"to" (node PKs).
            var fromField = new Field("from", Int64Type.Default, nullable: false);
            var toField = new Field("to", Int64Type.Default, nullable: false);
            var sinceField = new Field("since", Int64Type.Default, nullable: false);
            var schema = new Schema(new[] { fromField, toField, sinceField }, metadata: null);

            var fromArray = new Int64Array.Builder().Append(1).Append(2).Build();
            var toArray = new Int64Array.Builder().Append(2).Append(3).Build();
            var sinceArray = new Int64Array.Builder().Append(2020).Append(2021).Build();
            using var batch = new RecordBatch(schema, new IArrowArray[] { fromArray, toArray, sinceArray }, length: 2);

            conn.CreateArrowRelTable("Knows", batch, "Person", "Person");

            using QueryResult r = conn.Query(
                "MATCH (a:Person)-[k:Knows]->(b:Person) RETURN a.id AS src, b.id AS dst, k.since AS since ORDER BY a.id");
            List<object?[]> rows = r.Rows().ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal(new object?[] { 1L, 2L, 2020L }, rows[0]);
            Assert.Equal(new object?[] { 2L, 3L, 2021L }, rows[1]);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    /// <summary>Builds a 2-column (id INT64 non-null, name STRING) RecordBatch from the given rows.</summary>
    private static RecordBatch BuildPeopleBatch(params (long Id, string Name)[] rows)
    {
        var idField = new Field("id", Int64Type.Default, nullable: false);
        var nameField = new Field("name", StringType.Default, nullable: true);
        var schema = new Schema(new[] { idField, nameField }, metadata: null);

        var idBuilder = new Int64Array.Builder();
        var nameBuilder = new StringArray.Builder();
        foreach ((long id, string name) in rows)
        {
            idBuilder.Append(id);
            nameBuilder.Append(name);
        }

        return new RecordBatch(schema, new IArrowArray[] { idBuilder.Build(), nameBuilder.Build() }, rows.Length);
    }
}
