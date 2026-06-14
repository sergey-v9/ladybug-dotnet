using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests;

/// <summary>
/// Lifetime-safety tests for the disposal guards on <see cref="Value"/> and the
/// Interrupt/Dispose race on <see cref="Connection"/> (review findings CONC-1, CONC-2).
/// </summary>
public sealed class DisposalSafetyTests
{
    // CONC-1: an owned Value must dispose idempotently and throw after disposal, and a concurrent
    // double-dispose must never double-free the native handle (which the plain-bool guard allowed).
    [SkippableFact]
    public void Value_DoubleDispose_IsIdempotent_AndThrowsAfterDispose()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        using var db = new Database(string.Empty);
        using var conn = new Connection(db);

        using QueryResult result = conn.Query("RETURN 42 AS x");
        Value value = MaterializeOwnedValue(result);

        Assert.Equal(42L, value.GetValue());

        value.Dispose();
        value.Dispose(); // second dispose must be a safe no-op (no double-free)

        Assert.Throws<ObjectDisposedException>(() => value.GetValue());
        Assert.Null(value.ToString());
    }

    // CONC-1 (the race the bool->int conversion closes): many threads disposing the SAME Value must
    // drive the native destroy at most once. The plain-bool guard had a non-atomic check-then-set so
    // two threads could both pass the check; the engine happens to make this destroy a no-op for an
    // owned value, so this is a best-effort non-crash smoke for the atomic Interlocked.Exchange guard
    // rather than a deterministic repro. The idempotency/throws contract above is the load-bearing test.
    [SkippableFact]
    public void Value_ConcurrentDispose_DoesNotDoubleFree()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        for (int iteration = 0; iteration < 200; iteration++)
        {
            using var db = new Database(string.Empty);
            using var conn = new Connection(db);
            using QueryResult result = conn.Query("RETURN 7 AS x");
            Value value = MaterializeOwnedValue(result);

            const int threads = 8;
            using var ready = new Barrier(threads);
            Task[] racers = Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
            {
                ready.SignalAndWait();
                value.Dispose();
            })).ToArray();

            Task.WaitAll(racers);
        }
    }

    // CONC-2: after Dispose, Interrupt() must be a safe no-op (it must NOT touch the freed handle and
    // must NOT throw) — Interrupt is intentionally gate-free, so a disposal that runs concurrently with
    // an in-flight interrupt cannot be guarded by the query gate; the handle lifetime lock + _disposed
    // check is what keeps it safe. Before the fix Interrupt() threw ObjectDisposedException here.
    [SkippableFact]
    public void Interrupt_AfterDispose_IsSafeNoOp()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        using var db = new Database(string.Empty);
        var conn = new Connection(db);

        conn.Dispose();

        // Must not throw and must not call lbug_connection_interrupt on the freed handle.
        conn.Interrupt();
        conn.Interrupt();
    }

    // CONC-2: a concurrent dispose/interrupt loop must never use-after-free the native handle. Not a
    // deterministic repro (the window is tiny), but a best-effort stress that crashes the runner if the
    // handle lifetime lock is removed or Interrupt stops honoring _disposed under it.
    [SkippableFact]
    public void Interrupt_RacingDispose_DoesNotCrash()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        for (int iteration = 0; iteration < 500; iteration++)
        {
            using var db = new Database(string.Empty);
            var conn = new Connection(db);

            using var ready = new Barrier(2);
            var interrupter = Task.Run(() =>
            {
                ready.SignalAndWait();
                for (int i = 0; i < 50; i++)
                {
                    conn.Interrupt();
                }
            });
            var disposer = Task.Run(() =>
            {
                ready.SignalAndWait();
                conn.Dispose();
            });

            Task.WaitAll(interrupter, disposer);
        }
    }

    // Reads field 0 from the first row and returns a Value we OWN (it is a copy the engine hands back
    // via lbug_flat_tuple_get_value with IsOwnedByCpp clear), so a double native-destroy is observable.
    private static Value MaterializeOwnedValue(QueryResult result)
    {
        Assert.True(result.HasNext());
        using FlatTuple tuple = result.GetNext();
        return tuple.GetValue(0UL);
    }
}
