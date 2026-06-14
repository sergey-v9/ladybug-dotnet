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
}
