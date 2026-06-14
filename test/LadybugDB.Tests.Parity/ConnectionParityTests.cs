using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests.Parity;

/// <summary>Port of upstream <c>test/c_api/connection_test.cpp</c>.</summary>
public sealed class ConnectionParityTests
{
    [SkippableFact] // upstream Query
    public void Query_returns_expected_columns_and_count()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
        try
        {
            using QueryResult r = conn.Query("MATCH (a:person) RETURN a.fName");
            Assert.True(r.IsSuccess);
            Assert.Equal(1UL, r.ColumnCount);
            Assert.Equal("a.fName", r.ColumnNames.Single());
            Assert.Equal(8UL, r.RowCount);
        }
        finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
    }

    [SkippableFact] // upstream SetGetMaxNumThreadForExec
    public void Set_and_get_max_threads_for_exec()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
        try
        {
            conn.SetMaxThreadsForExec(4);
            Assert.Equal(4UL, conn.GetMaxThreadsForExec());
            conn.SetMaxThreadsForExec(8);
            Assert.Equal(8UL, conn.GetMaxThreadsForExec());
        }
        finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
    }

    [SkippableFact] // upstream Prepare + Execute: isStudent students count == 3
    public void Prepare_bind_execute_counts_students()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
        try
        {
            using PreparedStatement stmt =
                conn.Prepare("MATCH (a:person) WHERE a.isStudent = $s RETURN COUNT(*)");
            stmt.Bind("s", true);
            using QueryResult r = conn.Execute(stmt);
            Assert.True(r.IsSuccess);
            Assert.Equal(1UL, r.RowCount);
            Assert.Equal(3L, r.Rows().Single()[0]);
        }
        finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
    }

    [SkippableFact] // upstream ExecuteError: binding wrong type / failing execute throws
    public void Execute_with_type_mismatch_throws()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
        try
        {
            using PreparedStatement stmt =
                conn.Prepare("MATCH (a:person) WHERE a.isStudent = $s RETURN COUNT(*)");
            stmt.Bind("s", 30L); // isStudent is BOOL; INT64 bind must fail at execute
            Assert.Throws<LadybugQueryException>(() => conn.Execute(stmt));
        }
        finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
    }

    [SkippableFact] // upstream QueryTimeout
    public void Query_timeout_interrupts_long_query()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);
        conn.SetQueryTimeout(TimeSpan.FromMilliseconds(1));
        LadybugQueryException ex = Assert.Throws<LadybugQueryException>(() => conn.Query(
            "UNWIND RANGE(1,100000) AS x UNWIND RANGE(1,100000) AS y RETURN COUNT(x + y)"));
        Assert.Contains("Interrupted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact] // upstream Interrupt
    public void Interrupt_from_another_thread_cancels_query()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);
        var finished = new ManualResetEventSlim(false);
        var interrupter = Task.Run(() =>
        {
            while (!finished.IsSet)
            {
                Thread.Sleep(50);
                try { conn.Interrupt(); } catch { /* race with dispose at end */ }
            }
        });
        try
        {
            LadybugQueryException ex = Assert.Throws<LadybugQueryException>(() => conn.Query(
                "UNWIND RANGE(1,100000) AS x UNWIND RANGE(1,100000) AS y RETURN COUNT(x + y)"));
            Assert.Contains("Interrupted", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { finished.Set(); interrupter.Wait(); }
    }
}
