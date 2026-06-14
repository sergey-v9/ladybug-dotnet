using System.Numerics;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests;

/// <summary>
/// Native-gated round-trips for the new WS-C parameter binders (DECIMAL, INT128, BLOB, STRUCT, MAP).
/// They skip when the native library is absent.
/// </summary>
public sealed class BindingRoundTripTests
{
    [SkippableFact]
    public void Bind_decimal_round_trips()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            using PreparedStatement stmt = conn.Prepare("RETURN CAST($d AS DECIMAL(10, 2)) AS d");
            stmt.Bind("d", 12.34m);
            using QueryResult result = stmt.Execute();

            Assert.Equal(12.34m, Assert.IsType<decimal>(System.Linq.Enumerable.Single(result.Rows())[0]));
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public void Bind_LadybugDecimal_round_trips()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            using PreparedStatement stmt = conn.Prepare("RETURN CAST($d AS DECIMAL(20, 4)) AS d");
            stmt.Bind("d", new LadybugDecimal(new BigInteger(123456789), 4));
            using QueryResult result = stmt.Execute();

            Assert.Equal("12345.6789", System.Linq.Enumerable.Single(result.Rows())[0]?.ToString());
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public void Bind_BigInteger_round_trips_as_int128()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            var big = BigInteger.Parse("123456789012345678901234567890");
            using PreparedStatement stmt = conn.Prepare("RETURN CAST($v AS INT128) AS v");
            stmt.Bind("v", big);
            using QueryResult result = stmt.Execute();

            object? value = System.Linq.Enumerable.Single(result.Rows())[0];
            Assert.Equal(System.Int128.Parse("123456789012345678901234567890"), value);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public void Bind_byte_array_round_trips_as_blob()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            byte[] payload = { 0x00, 0x01, 0xFE, 0xFF, 0x41 };
            using PreparedStatement stmt = conn.Prepare("RETURN CAST($b AS BLOB) AS b");
            stmt.Bind("b", payload);
            using QueryResult result = stmt.Execute();

            var roundTrip = Assert.IsType<byte[]>(System.Linq.Enumerable.Single(result.Rows())[0]);
            Assert.Equal(payload, roundTrip);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public void Bind_dictionary_round_trips_as_struct()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            var fields = new System.Collections.Generic.Dictionary<string, object?>
            {
                ["x"] = 1L,
                ["y"] = "a",
            };
            using PreparedStatement stmt = conn.Prepare("RETURN $s AS s");
            stmt.Bind("s", fields);
            using QueryResult result = stmt.Execute();

            var dict = Assert.IsType<System.Collections.Generic.Dictionary<string, object?>>(
                System.Linq.Enumerable.Single(result.Rows())[0]);
            Assert.Equal(1L, dict["x"]);
            Assert.Equal("a", dict["y"]);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public void BindMap_round_trips_as_map()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            var entries = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<object, object?>>
            {
                new("a", 1L),
                new("b", 2L),
            };
            using PreparedStatement stmt = conn.Prepare("RETURN $m AS m");
            stmt.BindMap("m", entries);
            using QueryResult result = stmt.Execute();

            var map = Assert.IsType<System.Collections.Generic.Dictionary<object, object?>>(
                System.Linq.Enumerable.Single(result.Rows())[0]);
            Assert.Equal(1L, map["a"]);
            Assert.Equal(2L, map["b"]);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public void Dispatch_routes_new_types_via_object_bind()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            var parameters = new System.Collections.Generic.Dictionary<string, object?>
            {
                ["dec"] = 9.99m,
                ["big"] = BigInteger.Parse("170141183460469231731687303715884105727"), // INT128 max
                ["blob"] = new byte[] { 0xDE, 0xAD },
                ["st"] = new System.Collections.Generic.Dictionary<string, object?> { ["k"] = 7L },
            };
            using QueryResult result = conn.Execute(
                "RETURN CAST($dec AS DECIMAL(10,2)) AS dec, CAST($big AS INT128) AS big, CAST($blob AS BLOB) AS blob, $st AS st",
                parameters);

            object?[] row = System.Linq.Enumerable.Single(result.Rows());
            Assert.Equal(9.99m, row[0]);
            Assert.Equal(System.Int128.Parse("170141183460469231731687303715884105727"), row[1]);
            Assert.Equal(new byte[] { 0xDE, 0xAD }, Assert.IsType<byte[]>(row[2]));
            Assert.Equal(7L, Assert.IsType<System.Collections.Generic.Dictionary<string, object?>>(row[3])["k"]);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }
}
