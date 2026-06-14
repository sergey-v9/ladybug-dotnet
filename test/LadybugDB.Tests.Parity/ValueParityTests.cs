using System;
using System.Collections.Generic;
using System.Linq;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests.Parity;

/// <summary>
/// Port of the read/accessor cases of upstream <c>test/c_api/value_test.cpp</c>. Constructor cases
/// (<c>lbug_value_create_*</c>) are out of scope: the managed binding materializes values, it does
/// not expose value construction. Reads are driven through Cypher literals / the person graph.
/// </summary>
public sealed class ValueParityTests
{
    private static object? Scalar(Connection conn, string cypher)
    {
        using QueryResult r = conn.Query(cypher);
        return r.Rows().Single()[0];
    }

    [SkippableFact] // GetBool / GetInt* / GetUInt* / GetFloat / GetDouble / GetString
    public void Primitive_reads_map_to_clr_types()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);

        Assert.Equal(true, Scalar(conn, "RETURN true"));
        Assert.Equal((sbyte)8, Scalar(conn, "RETURN cast(8 AS INT8)"));
        Assert.Equal((short)16, Scalar(conn, "RETURN cast(16 AS INT16)"));
        Assert.Equal(32, Scalar(conn, "RETURN cast(32 AS INT32)"));
        Assert.Equal(64L, Scalar(conn, "RETURN cast(64 AS INT64)"));
        Assert.Equal((byte)8, Scalar(conn, "RETURN cast(8 AS UINT8)"));
        Assert.Equal((ushort)16, Scalar(conn, "RETURN cast(16 AS UINT16)"));
        Assert.Equal(32u, Scalar(conn, "RETURN cast(32 AS UINT32)"));
        Assert.Equal(64ul, Scalar(conn, "RETURN cast(64 AS UINT64)"));
        Assert.Equal(1.5f, Scalar(conn, "RETURN cast(1.5 AS FLOAT)"));
        Assert.Equal(2.5d, Scalar(conn, "RETURN cast(2.5 AS DOUBLE)"));
        Assert.Equal("hello", Scalar(conn, "RETURN 'hello'"));
    }

    [SkippableFact] // GetInt128 round-trip
    public void Int128_reads_and_round_trips()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);
        Assert.Equal(
            Int128.Parse("123456789012345678901234567890"),
            Scalar(conn, "RETURN cast(123456789012345678901234567890 AS INT128)"));
    }

    [SkippableFact] // GetDate / GetTimestamp
    public void Temporal_reads_map_to_clr_types()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);
        Assert.Equal(new DateOnly(2020, 1, 15), Scalar(conn, "RETURN date('2020-01-15')"));
        Assert.Equal(
            new DateTime(2020, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            Scalar(conn, "RETURN timestamp('2020-01-15 10:30:00')"));
    }

    [SkippableFact] // GetBlob / GetUUID
    public void Blob_and_uuid_reads()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, Scalar(conn, @"RETURN BLOB('\xAA\xBB')"));
        object? uuid = Scalar(conn, "RETURN UUID('a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11')");
        Assert.Equal(Guid.Parse("a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11"), uuid);
    }

    [SkippableFact] // GetListElement / GetListSize
    public void List_element_reads()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);
        var list = Assert.IsType<object?[]>(Scalar(conn, "RETURN [10, 20, 30]"));
        Assert.Equal(3, list.Length);
        Assert.Equal(10L, list[0]);
        Assert.Equal(20L, list[1]);
    }

    [SkippableFact] // GetStructFieldName / GetStructFieldValue
    public void Struct_field_reads()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);
        var s = Assert.IsType<Dictionary<string, object?>>(Scalar(conn, "RETURN {a: 1, b: 'x'}"));
        Assert.Equal(1L, s["a"]);
        Assert.Equal("x", s["b"]);
    }

    [SkippableFact] // getMapKey / getMapValue
    public void Map_entry_reads()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);
        object? m = Scalar(conn, "RETURN map(['k1','k2'], [1, 2])");
        var dict = Assert.IsType<Dictionary<object, object?>>(m);
        Assert.Equal(1L, dict["k1"]);
        Assert.Equal(2L, dict["k2"]);
    }

    [SkippableFact] // NodeVal property + id reads
    public void Node_value_reads()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
        try
        {
            using QueryResult r = conn.Query("MATCH (p:person {fName:'Alice'}) RETURN p");
            var node = Assert.IsType<Node>(r.Rows().Single()[0]);
            Assert.Equal("person", node.Label);
            Assert.Equal("Alice", node.Properties["fName"]);
            Assert.Equal(35L, node.Properties["age"]);
        }
        finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
    }

    [SkippableFact] // RelVal property reads
    public void Rel_value_reads()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
        try
        {
            using QueryResult r = conn.Query(
                "MATCH (:person {fName:'Alice'})-[k:knows]->(:person {fName:'Bob'}) RETURN k");
            var rel = Assert.IsType<Rel>(r.Rows().Single()[0]);
            Assert.Equal("knows", rel.Label);
            Assert.Equal(2011L, rel.Properties["since"]);
        }
        finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
    }

    [SkippableFact] // getDecimalAsString -> WS-C LadybugDecimal / GetDecimal (never a bare string)
    public void Decimal_read_is_lossless_and_never_a_bare_string()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);

        using QueryResult r = conn.Query("RETURN cast(123.45 AS DECIMAL(5,2)) AS d");
        object? value = r.Rows().Single()[0];
        Assert.IsNotType<string>(value);                 // the P0 fix: never silently a string
        Assert.Equal(123.45m, Assert.IsType<decimal>(value));

        using QueryResult r2 = conn.Query("RETURN cast(123.45 AS DECIMAL(5,2)) AS d");
        using FlatTuple t = r2.GetNext();
        using Value v = t.GetValue(0);
        LadybugDecimal d = v.GetDecimal();               // deterministic accessor
        Assert.Equal(123.45m, d.ToDecimal());
        Assert.Equal("123.45", d.ToString());
    }
}
