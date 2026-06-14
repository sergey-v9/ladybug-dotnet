using System;
using LadybugDB;
using LadybugDB.Diagnostics;
using Xunit;

namespace LadybugDB.Tests;

/// <summary>
/// Managed-only tests that pin the seams WS-B exposes for H (OTel) and E (Arrow). They assert the
/// default instrumentation seam is a transparent no-op pass-through and require no native library.
/// </summary>
public sealed class SeamTests
{
    [Fact]
    public void LadybugInstrumentation_StartQuery_IsNonNullDisposableNoOp()
    {
        // The Phase-1 instrumentation seam (D4/D5) WS-B wraps the query path with. The no-op stub
        // must hand back a usable scope whose lifecycle calls never throw, so wiring it into the
        // synchronous Query path is safe before WS-H fills in the real ActivitySource/Meter.
        using QueryScope scope = LadybugInstrumentation.StartQuery("RETURN 1");

        Assert.NotNull(scope);
        scope.SetSuccess();
        scope.SetError(new InvalidOperationException("boom"));
        scope.SetCancelled();
    }

    [SkippableFact]
    public void WithHandle_GivesArrowPartialRawHandleAccess_UnderDisposalGuard()
    {
        // The internal seam WS-E's QueryResult.Arrow.cs partial consumes to reach the raw native
        // handle. We exercise it here through InternalsVisibleTo: it must run the callback under the
        // disposal guard and throw ObjectDisposedException once the result is disposed.
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            QueryResult result = conn.Query("RETURN 1 AS x");
            ulong columns = result.WithHandle((ref Interop.LbugQueryResult h) =>
                Interop.Native.QueryResultGetNumColumns(ref h));
            Assert.Equal(1UL, columns);

            result.Dispose();
            Assert.Throws<ObjectDisposedException>(() =>
                result.WithHandle((ref Interop.LbugQueryResult h) =>
                    Interop.Native.QueryResultGetNumColumns(ref h)));
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }
}
