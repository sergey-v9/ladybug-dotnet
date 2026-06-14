// Phase-1 foundation seam (see docs/parity-2026-06/04-coordination-decisions.md §D4/§D5).
//
// WS-B's synchronous query/execute path wraps execution in:
//     using var scope = LadybugInstrumentation.StartQuery(cypher);
//     ... scope.SetSuccess() / scope.SetError(ex) / scope.SetCancelled() ...
//
// This file ships a NO-OP stub so the core compiles and WS-B can call the seam in Phase 2.
// WS-H OWNS this file in Phase 2 and replaces the internals with the real
// ActivitySource("LadybugDB") + Meter instrumentation (instruments: db.query.count,
// db.query.errors, db.query.duration.ms). Keep the StartQuery/QueryScope shape stable.

using System;

namespace LadybugDB.Diagnostics;

/// <summary>
/// Internal instrumentation seam around query execution. No-op until WS-H wires it to an
/// <c>ActivitySource("LadybugDB")</c> and a meter.
/// </summary>
internal static class LadybugInstrumentation
{
    /// <summary>Begin an instrumentation scope for a query. Always returns a disposable scope.</summary>
    public static QueryScope StartQuery(string cypher) => new QueryScope();
}

/// <summary>
/// Per-query instrumentation scope. Disposing records duration/metrics (once WS-H wires it).
/// </summary>
internal sealed class QueryScope : IDisposable
{
    public void SetSuccess() { }

    public void SetError(Exception? exception = null) { }

    public void SetCancelled() { }

    public void Dispose() { }
}
