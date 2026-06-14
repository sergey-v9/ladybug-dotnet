using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests;

/// <summary>
/// WS-D async surface: round-trip + cancellation are native-gated (<see cref="SkippableFactAttribute"/>);
/// argument validation and the cancellation-helper contract are pure-managed and always run.
/// </summary>
public sealed class AsyncTests
{
    [SkippableFact]
    public async Task QueryAsync_NullCypher_ThrowsArgumentNullException()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            await Assert.ThrowsAsync<ArgumentNullException>(() => conn.QueryAsync(null!));
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [Fact]
    public void QueryResult_IsSealedPartialType()
    {
        // Structural guard so the QueryResult.Async.cs partial seam stays referenced and the
        // project fails to build if that file is missing or malformed.
        Assert.True(typeof(QueryResult).IsSealed);
    }

    [Fact]
    public async Task RunWithCancellation_PreCancelledToken_DoesNotInvokeWork()
    {
        // Pure-managed: no native interaction. A pre-cancelled token must short-circuit before the
        // offloaded work runs.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        bool ran = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Connection.RunWithCancellationForTests(() => { ran = true; return 0; }, cts.Token));

        Assert.False(ran, "work must not run when the token is already cancelled");
    }

    [Fact]
    public async Task RunWithCancellation_RunningToken_ReturnsResult()
    {
        // Pure-managed happy path: an uncancelled token runs the work and returns its result.
        using var cts = new CancellationTokenSource();

        int result = await Connection.RunWithCancellationForTests(() => 42, cts.Token);

        Assert.Equal(42, result);
    }

    [Fact]
    public void NormalizeCancellation_PreservesOriginalQueryExceptionAsInner()
    {
        // CONC-3: when a LadybugQueryException surfaces while the token is cancelled, normalizing it to
        // OperationCanceledException must keep the original engine error as InnerException so a genuine
        // failure (cancellation that landed for an unrelated reason) is not silently masked.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var original = new LadybugQueryException("syntax error near 'RETURNN'");

        OperationCanceledException normalized =
            Connection.NormalizeCancellationException(original, cts.Token);

        Assert.Same(original, normalized.InnerException);
        Assert.Equal(cts.Token, normalized.CancellationToken);
    }

    [SkippableFact]
    public async Task PrepareAsync_And_ExecuteAsync_RoundTrip()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            (await conn.QueryAsync(
                "CREATE NODE TABLE Person(name STRING, age INT64, PRIMARY KEY(name))")).Dispose();

            using (PreparedStatement insert =
                   await conn.PrepareAsync("CREATE (:Person {name: $name, age: $age})"))
            {
                insert.Bind("name", "Alice").Bind("age", 30L);
                (await conn.ExecuteAsync(insert)).Dispose();
            }

            using QueryResult result = await conn.QueryAsync("MATCH (p:Person) RETURN p.name, p.age");
            Assert.True(result.IsSuccess);
            List<object?[]> rows = result.Rows().ToList();
            Assert.Single(rows);
            Assert.Equal("Alice", rows[0][0]);
            Assert.Equal(30L, rows[0][1]);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public async Task QueryAllAsync_ReturnsAllStatementResults()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            IReadOnlyList<QueryResult> results =
                await conn.QueryAllAsync("RETURN 1 AS a; RETURN 2 AS b;");
            try
            {
                Assert.Equal(2, results.Count);
                Assert.True(results[0].IsSuccess);
                Assert.True(results[1].IsSuccess);
            }
            finally
            {
                foreach (QueryResult r in results)
                {
                    r.Dispose();
                }
            }
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public async Task StreamAsync_YieldsAllRows()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            (await conn.QueryAsync("CREATE NODE TABLE Person(name STRING, PRIMARY KEY(name))")).Dispose();
            (await conn.QueryAsync("CREATE (:Person {name: 'Alice'})")).Dispose();
            (await conn.QueryAsync("CREATE (:Person {name: 'Bob'})")).Dispose();

            var names = new List<string?>();
            await foreach (FlatTuple tuple in conn.StreamAsync(
                               "MATCH (p:Person) RETURN p.name ORDER BY p.name"))
            {
                using (tuple)
                using (Value value = tuple.GetValue(0UL))
                {
                    names.Add(value.GetValue() as string);
                }
            }

            Assert.Equal(new[] { "Alice", "Bob" }, names);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public async Task QueryAsync_Cancellation_InterruptsRunningQuery()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            // Seed a dataset whose triple self-join is expensive enough to interrupt mid-flight
            // (the proven slow-query shape from ConnectionControlTests.Interrupt_AbortsRunningQuery).
            (await conn.QueryAsync("CREATE NODE TABLE N(v INT64, PRIMARY KEY(v))")).Dispose();
            (await conn.QueryAsync("UNWIND range(1, 200000) AS x CREATE (:N {v: x})")).Dispose();

            using var cts = new CancellationTokenSource();
            Task<QueryResult> running =
                conn.QueryAsync("MATCH (a:N), (b:N), (c:N) RETURN count(*)", cts.Token);

            // Let the engine start the heavy query, then cancel -> Interrupt().
            cts.CancelAfter(TimeSpan.FromMilliseconds(100));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                using QueryResult r = await running;
            });

            // The connection must remain usable after an interrupt.
            using QueryResult ok = await conn.QueryAsync("RETURN 1 AS x");
            Assert.True(ok.IsSuccess);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }
}
