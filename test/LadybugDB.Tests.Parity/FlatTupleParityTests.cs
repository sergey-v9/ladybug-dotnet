using System;
using System.Linq;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests.Parity;

/// <summary>Port of upstream <c>test/c_api/flat_tuple_test.cpp</c>.</summary>
public sealed class FlatTupleParityTests
{
    [SkippableFact] // upstream GetValue: typed access to each column of the first tuple
    public void GetValue_reads_typed_columns()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
        try
        {
            using QueryResult r = conn.Query(
                "MATCH (a:person) RETURN a.fName, a.age, a.height ORDER BY a.fName LIMIT 1");
            Assert.True(r.HasNext());
            using FlatTuple t = r.GetNext();

            using (Value name = t.GetValue(0))
            {
                Assert.Equal(DataTypeId.String, name.DataTypeId);
                Assert.Equal("Alice", name.GetValue());
            }
            using (Value age = t.GetValue(1))
            {
                Assert.Equal(DataTypeId.Int64, age.DataTypeId);
                Assert.Equal(35L, age.GetValue());
            }
            using (Value height = t.GetValue(2))
            {
                Assert.Equal(DataTypeId.Float, height.DataTypeId);
                Assert.Equal(1.731f, Assert.IsType<float>(height.GetValue()), 3);
            }
        }
        finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
    }

    [SkippableFact]
    public void Typed_accessors_read_scalars_without_value_wrapper()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        string path = ParityEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(path);
            using var conn = new Connection(db);
            using QueryResult r = conn.Query(
                "RETURN 'Alice' AS name, 35 AS age, CAST(1.25 AS FLOAT) AS height, true AS flag, NULL AS missing");
            using FlatTuple t = r.GetNext();

            Assert.Equal("Alice", t.GetString(0));
            Assert.Equal(35L, t.GetInt64(1));
            Assert.Equal(1.25f, t.GetFloat(2), 3);
            Assert.True(t.GetBool(3));
            Assert.True(t.IsNull(4));
            Assert.Equal(0L, t.GetInt64OrDefault(4));
            Assert.Throws<LadybugException>(() => t.GetInt64(0));
        }
        finally { ParityEnvironment.TryDelete(path); }
    }

    [SkippableFact] // upstream GetValue out-of-range index
    public void GetValue_out_of_range_throws()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
        try
        {
            using QueryResult r = conn.Query("MATCH (a:person) RETURN a.fName LIMIT 1");
            using FlatTuple t = r.GetNext();
            Assert.ThrowsAny<Exception>(() => t.GetValue(222));
        }
        finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
    }

    [SkippableFact] // upstream ToString: pipe-delimited row text
    public void Tuple_to_string_is_pipe_delimited()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
        try
        {
            using QueryResult r = conn.Query(
                "MATCH (a:person) RETURN a.fName, a.age ORDER BY a.fName LIMIT 1");
            using FlatTuple t = r.GetNext();
            string s = t.ToString() ?? string.Empty;
            Assert.Contains("Alice", s);
            Assert.Contains("|", s);
        }
        finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
    }
}
