using System;
using System.Linq;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests.Parity;

/// <summary>Port of upstream <c>test/c_api/database_test.cpp</c>.</summary>
public sealed class DatabaseParityTests
{
    [SkippableFact] // upstream CreationAndDestroy
    public void Open_and_dispose_roundtrip()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        string path = ParityEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(path);
            using var conn = new Connection(db);
            using QueryResult r = conn.Query("RETURN 1");
            Assert.True(r.IsSuccess);
        }
        finally { ParityEnvironment.TryDelete(path); }
    }

    [SkippableFact] // upstream CreationInMemory
    public void In_memory_database_opens()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);
        using QueryResult r = conn.Query("RETURN 1 + 1");
        Assert.Equal(2L, r.Rows().Single()[0]);
    }

    [SkippableFact] // upstream CreationReadOnly: writes must fail against a read-only open
    public void Read_only_database_rejects_writes()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        string path = ParityEnvironment.NewTempDbPath();
        try
        {
            using (var rw = new Database(path))
            using (var conn = new Connection(rw))
            {
                conn.Query("CREATE NODE TABLE T(id INT64, PRIMARY KEY(id))").Dispose();
            }

            using var ro = new Database(path, new SystemConfig { ReadOnly = true });
            using var roConn = new Connection(ro);
            Assert.Throws<LadybugQueryException>(
                () => roConn.Query("CREATE NODE TABLE U(id INT64, PRIMARY KEY(id))"));
        }
        finally { ParityEnvironment.TryDelete(path); }
    }

    [SkippableFact] // upstream UseConnectionAfterDatabaseDestroy
    public void Query_after_database_dispose_fails_without_crashing()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        string path = ParityEnvironment.NewTempDbPath();
        try
        {
            var db = new Database(path);
            var conn = new Connection(db);
            db.Dispose();
            Assert.ThrowsAny<Exception>(() => conn.Query("RETURN 0"));
            conn.Dispose();
        }
        finally { ParityEnvironment.TryDelete(path); }
    }
}
