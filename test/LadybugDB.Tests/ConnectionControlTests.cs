using System;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests;

/// <summary>
/// Native-gated round-trip tests for the WS-B connection-control surface. They skip when the
/// native library is unavailable.
/// </summary>
public sealed class ConnectionControlTests
{
    [SkippableFact]
    public void SetQueryTimeout_TinyTimeout_AbortsHeavyQuery()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            conn.Query("CREATE NODE TABLE N(v INT64, PRIMARY KEY(v))").Dispose();
            conn.Query("UNWIND range(1, 200000) AS x CREATE (:N {v: x})").Dispose();

            conn.SetQueryTimeout(TimeSpan.FromMilliseconds(1));

            // A heavy cartesian product should exceed a 1ms budget and surface as a query failure.
            Assert.Throws<LadybugQueryException>(() =>
                conn.Query("MATCH (a:N), (b:N), (c:N) RETURN count(*)").Dispose());
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public void MaxThreadsForExec_RoundTrips()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            conn.SetMaxThreadsForExec(3);
            Assert.Equal(3UL, conn.GetMaxThreadsForExec());

            conn.SetMaxThreadsForExec(1);
            Assert.Equal(1UL, conn.GetMaxThreadsForExec());
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }
}
