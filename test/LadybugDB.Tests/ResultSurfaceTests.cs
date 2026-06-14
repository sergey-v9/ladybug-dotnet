using System.Collections.Generic;
using System.Linq;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests;

/// <summary>Native-gated tests for the WS-B QueryResult surface (summary, columns, multi-statement).</summary>
public sealed class ResultSurfaceTests
{
    private static (Database, Connection) NewGraph(string dbPath)
    {
        var db = new Database(dbPath);
        var conn = new Connection(db);
        conn.Query("CREATE NODE TABLE Person(name STRING, age INT64, PRIMARY KEY(name))").Dispose();
        conn.Query("CREATE (:Person {name: 'Alice', age: 30})").Dispose();
        conn.Query("CREATE (:Person {name: 'Bob', age: 42})").Dispose();
        return (db, conn);
    }

    [SkippableFact]
    public void Summary_ReportsNonNegativeTimings()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            (Database db, Connection conn) = NewGraph(dbPath);
            using (db)
            using (conn)
            {
                using QueryResult result = conn.Query("MATCH (p:Person) RETURN p.name");
                QuerySummary summary = result.Summary;

                Assert.True(summary.CompilingTimeMs >= 0.0);
                Assert.True(summary.ExecutionTimeMs >= 0.0);

                // Cached: a second access returns an equal value.
                Assert.Equal(summary, result.Summary);
            }
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public void GetColumnType_ReturnsPerColumnLogicalTypes()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            (Database db, Connection conn) = NewGraph(dbPath);
            using (db)
            using (conn)
            {
                using QueryResult result = conn.Query("MATCH (p:Person) RETURN p.name, p.age");

                Assert.Equal(DataTypeId.String, result.GetColumnType(0).Id);
                Assert.Equal(DataTypeId.Int64, result.GetColumnType(1).Id);
                Assert.Equal("STRING", result.GetColumnType(0).ToString());
            }
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public void Columns_ExposesNameAndType_AndIsCached()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            (Database db, Connection conn) = NewGraph(dbPath);
            using (db)
            using (conn)
            {
                using QueryResult result = conn.Query("MATCH (p:Person) RETURN p.name, p.age");

                IReadOnlyList<ColumnSchema> columns = result.Columns;
                Assert.Equal(2, columns.Count);
                Assert.Equal("p.name", columns[0].Name);
                Assert.Equal(DataTypeId.String, columns[0].Type.Id);
                Assert.Equal("p.age", columns[1].Name);
                Assert.Equal(DataTypeId.Int64, columns[1].Type.Id);

                Assert.Same(columns, result.Columns); // cached: same instance
            }
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public void ResetIterator_AllowsReReadingAllRows()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            (Database db, Connection conn) = NewGraph(dbPath);
            using (db)
            using (conn)
            {
                using QueryResult result = conn.Query("MATCH (p:Person) RETURN p.name ORDER BY p.name");

                List<object?[]> first = result.Rows().ToList();
                Assert.Equal(2, first.Count);
                Assert.False(result.HasNext());

                result.ResetIterator();

                Assert.True(result.HasNext());
                List<object?[]> second = result.Rows().ToList();
                Assert.Equal(2, second.Count);
                Assert.Equal(first[0][0], second[0][0]);
            }
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public void QueryAll_ReturnsEveryResultSet()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            (Database db, Connection conn) = NewGraph(dbPath);
            using (db)
            using (conn)
            {
                IReadOnlyList<QueryResult> results = conn.QueryAll(
                    "MATCH (p:Person) RETURN p.name ORDER BY p.name; " +
                    "MATCH (p:Person) RETURN count(*) AS c;");

                try
                {
                    Assert.Equal(2, results.Count);

                    List<object?[]> names = results[0].Rows().ToList();
                    Assert.Equal(new object?[] { "Alice" }, names[0]);
                    Assert.Equal(new object?[] { "Bob" }, names[1]);

                    object?[] countRow = results[1].Rows().Single();
                    Assert.Equal(2L, countRow[0]);
                }
                finally
                {
                    foreach (QueryResult r in results)
                    {
                        r.Dispose();
                    }
                }
            }
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public void HasNextQueryResult_ChainWalksManually()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            (Database db, Connection conn) = NewGraph(dbPath);
            using (db)
            using (conn)
            {
                using QueryResult first = conn.Query(
                    "MATCH (p:Person) RETURN count(*) AS c; MATCH (p:Person) RETURN p.name;");

                Assert.True(first.HasNextQueryResult());
                using QueryResult second = first.GetNextQueryResult();
                Assert.False(second.HasNextQueryResult());
                Assert.Equal(2, second.Rows().Count()); // two Person nodes (Alice, Bob)
            }
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }
}
