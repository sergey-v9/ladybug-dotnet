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
    public void GetColumnType_ToString_RendersScalarListAndFixedArrayExactly()
    {
        // Pins LogicalType.ToString()'s exact formatting on the native path (relocated from the former
        // managed-only LogicalTypeTests.LogicalType_ToString_RendersScalarsNestedAndArrays): a scalar
        // renders as its bare type name, a LIST as LIST(child), and a fixed ARRAY as ARRAY(child, n).
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            // Scalar + LIST from literals.
            using (QueryResult r = conn.Query("RETURN 42 AS scalar, [1, 2, 3] AS list"))
            {
                Assert.Equal("INT64", r.GetColumnType(0).ToString());
                Assert.Equal("LIST(INT64)", r.GetColumnType(1).ToString());
            }

            // Fixed ARRAY from a typed column.
            conn.Query("CREATE NODE TABLE A(id INT64, v INT64[3], PRIMARY KEY(id))").Dispose();
            conn.Query("CREATE (:A {id: 1, v: [10, 20, 30]})").Dispose();
            using (QueryResult arr = conn.Query("MATCH (a:A) RETURN a.v"))
            {
                Assert.Equal("ARRAY(INT64, 3)", arr.GetColumnType(0).ToString());
            }
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public void ColumnSchema_EqualityUsesReferenceEqualityOverLogicalType()
    {
        // Relocated from the former managed-only LogicalTypeTests once the LogicalType test factory was
        // removed: ColumnSchema is a record whose LogicalType member is a class, so record equality
        // compares LogicalType by reference. Two schemas with the same Name and the SAME LogicalType
        // instance are equal; the same Name with a DIFFERENT (distinct) LogicalType instance is not.
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            // Two independent queries yield two distinct LogicalType instances of the same INT64 type.
            using QueryResult r1 = conn.Query("RETURN 1 AS x");
            using QueryResult r2 = conn.Query("RETURN 2 AS y");
            LogicalType t1 = r1.GetColumnType(0);
            LogicalType t2 = r2.GetColumnType(0);
            Assert.NotSame(t1, t2);

            var a = new ColumnSchema("age", t1);
            var b = new ColumnSchema("age", t1);

            Assert.Equal("age", a.Name);             // ctor-arg exposure
            Assert.Same(t1, a.Type);                 // ctor-arg exposure
            Assert.Equal(a, b);                       // same Name + same LogicalType reference
            Assert.NotEqual(a, new ColumnSchema("name", t1));
            Assert.NotEqual(a, new ColumnSchema("age", t2)); // different LogicalType reference
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

    [SkippableFact]
    public void FlatTuple_DoubleDispose_IsSafe()
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
                Assert.True(result.HasNext());
                FlatTuple tuple = result.GetNext();

                tuple.Dispose();
                tuple.Dispose(); // must be a no-op, never a double native destroy
                Assert.Throws<System.ObjectDisposedException>(() => tuple.GetValue(0));
            }
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }
}
