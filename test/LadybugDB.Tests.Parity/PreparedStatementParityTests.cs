using System;
using System.Linq;
using System.Numerics;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests.Parity;

/// <summary>Port of upstream <c>test/c_api/prepared_statement_test.cpp</c> bind matrix.</summary>
public sealed class PreparedStatementParityTests
{
    [SkippableTheory] // upstream BindBool/BindInt*/BindUInt*/BindDouble/BindString
    [InlineData(true, true)]
    [InlineData((sbyte)7, (sbyte)7)]
    [InlineData((short)9, (short)9)]
    [InlineData(11, 11)]
    [InlineData(13L, 13L)]
    [InlineData(2.5d, 2.5d)]
    [InlineData("hello", "hello")]
    public void Scalar_bind_roundtrips(object bound, object expected)
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);

        using PreparedStatement stmt = conn.Prepare("RETURN $p");
        stmt.Bind("p", bound);
        using QueryResult r = conn.Execute(stmt);
        Assert.Equal(expected, r.Rows().Single()[0]);
    }

    [SkippableFact] // upstream BindFloat (float not allowed in [InlineData]; assert separately)
    public void Bind_float_roundtrips()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);
        using PreparedStatement stmt = conn.Prepare("RETURN $p");
        stmt.Bind("p", 3.5f);
        Assert.Equal(3.5f, conn.Execute(stmt).Rows().Single()[0]);
    }

    [SkippableFact] // upstream IsReadOnly
    public void Read_only_statement_is_distinguished()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);
        using PreparedStatement read = conn.Prepare("RETURN 1");
        Assert.True(read.IsReadOnly);
        using PreparedStatement write =
            conn.Prepare("CREATE NODE TABLE P(name STRING, PRIMARY KEY(name))");
        Assert.False(write.IsReadOnly);
    }

    [SkippableFact] // upstream GetErrorMessage on a binder failure
    public void Prepare_failure_throws_with_message()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);
        LadybugQueryException ex = Assert.Throws<LadybugQueryException>(
            () => conn.Prepare("MATCH (a:personnnn) WHERE a.isStudent = $s RETURN COUNT(*)"));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    // ----- WS-C bind overloads: DATE / INT128 / BLOB -----

    [SkippableFact] // upstream BindDate
    public void Bind_date_roundtrips()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);
        using PreparedStatement stmt = conn.Prepare("RETURN $p");
        stmt.Bind("p", new DateOnly(2020, 1, 15));
        Assert.Equal(new DateOnly(2020, 1, 15), conn.Execute(stmt).Rows().Single()[0]);
    }

    [SkippableFact] // upstream BindInt128 (WS-C BigInteger overload)
    public void Bind_int128_roundtrips()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);
        using PreparedStatement stmt = conn.Prepare("RETURN $p");
        stmt.Bind("p", BigInteger.Parse("123456789012345678901234567890"));
        Assert.Equal(
            Int128.Parse("123456789012345678901234567890"),
            conn.Execute(stmt).Rows().Single()[0]);
    }

    [SkippableFact] // upstream BindValue / BLOB
    public void Bind_blob_roundtrips()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);

        // The C API has no value-level BLOB creator (D7): byte[] binds as an escaped '\xNN…' string
        // literal, so the surrounding query CASTs it back to BLOB to round-trip the bytes.
        using PreparedStatement stmt = conn.Prepare("RETURN CAST($p AS BLOB)");
        stmt.Bind("p", new byte[] { 1, 2, 3, 255 });
        Assert.Equal(new byte[] { 1, 2, 3, 255 }, conn.Execute(stmt).Rows().Single()[0]);
    }
}
