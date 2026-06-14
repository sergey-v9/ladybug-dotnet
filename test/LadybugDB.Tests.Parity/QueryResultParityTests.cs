using System;
using System.Linq;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests.Parity;

/// <summary>Port of upstream <c>test/c_api/query_result_test.cpp</c>.</summary>
public sealed class QueryResultParityTests
{
    [SkippableFact] // upstream GetNumColumns + GetColumnName
    public void Columns_count_and_names()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
        try
        {
            using QueryResult r = conn.Query("MATCH (a:person) RETURN a.fName, a.age, a.height");
            Assert.Equal(3UL, r.ColumnCount);
            Assert.Equal(new[] { "a.fName", "a.age", "a.height" }, r.ColumnNames.ToArray());
            Assert.Throws<LadybugException>(() => r.GetColumnName(222));
        }
        finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
    }

    [SkippableFact] // upstream GetColumnDataType
    public void Column_logical_types_match()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
        try
        {
            using QueryResult r = conn.Query("MATCH (a:person) RETURN a.fName, a.age, a.height");
            Assert.Equal(DataTypeId.String, r.GetColumnType(0).Id);
            Assert.Equal(DataTypeId.Int64, r.GetColumnType(1).Id);
            Assert.Equal(DataTypeId.Float, r.GetColumnType(2).Id);
            Assert.Equal(DataTypeId.String, r.Columns[0].Type.Id);
            Assert.Equal("a.fName", r.Columns[0].Name);
        }
        finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
    }

    [SkippableFact] // upstream GetQuerySummary
    public void Query_summary_reports_positive_timings()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
        try
        {
            using QueryResult r = conn.Query("MATCH (a:person) RETURN a.fName, a.age, a.height");
            QuerySummary s = r.Summary;
            Assert.True(s.CompilingTimeMs > 0);
            Assert.True(s.ExecutionTimeMs > 0);
        }
        finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
    }

    [SkippableFact] // upstream GetNext + HasNext: ordered read of the first row
    public void HasNext_getNext_reads_ordered_rows()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
        try
        {
            using QueryResult r = conn.Query("MATCH (a:person) RETURN a.fName, a.age ORDER BY a.fName");
            Assert.True(r.HasNext());
            object?[] first = r.Rows().First();
            Assert.Equal("Alice", first[0]);
            Assert.Equal(35L, first[1]);
        }
        finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
    }

    [SkippableFact] // upstream ResetIterator
    public void Reset_iterator_replays_from_start()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
        try
        {
            using QueryResult r = conn.Query("MATCH (a:person) RETURN a.fName ORDER BY a.fName");
            using (FlatTuple t = r.GetNext()) { using Value v = t.GetValue(0); Assert.Equal("Alice", v.GetValue()); }
            r.ResetIterator();
            Assert.True(r.HasNext());
            using (FlatTuple t = r.GetNext()) { using Value v = t.GetValue(0); Assert.Equal("Alice", v.GetValue()); }
        }
        finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
    }

    [SkippableFact] // upstream MultipleQuery: three statements -> three result sets
    public void Multi_statement_query_yields_all_result_sets()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);

        System.Collections.Generic.IReadOnlyList<QueryResult> results =
            conn.QueryAll("RETURN 1; RETURN 2; RETURN 3;");
        try
        {
            Assert.Equal(3, results.Count);
            Assert.Equal(1L, results[0].Rows().Single()[0]);
            Assert.Equal(2L, results[1].Rows().Single()[0]);
            Assert.Equal(3L, results[2].Rows().Single()[0]);
        }
        finally { foreach (QueryResult r in results) r.Dispose(); }
    }

    [SkippableFact] // upstream MultipleQuery chain primitives
    public void Result_chain_walks_with_hasNext_getNext_query_result()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);

        using QueryResult first = conn.Query("RETURN 1; RETURN 2; RETURN 3;");
        Assert.True(first.HasNextQueryResult());
        using QueryResult second = first.GetNextQueryResult();
        Assert.Equal(2L, second.Rows().Single()[0]);
        using QueryResult third = first.GetNextQueryResult();
        Assert.Equal(3L, third.Rows().Single()[0]);
        Assert.False(first.HasNextQueryResult());
    }
}
