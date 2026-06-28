// Instrumentation seam around query execution (see docs/parity-2026-06/04-coordination-decisions.md
// §D4/§D5 and the OpenTelemetry section of docs/superpowers/specs/2026-06-14-csharp-parity-extensions-design.md).
//
// WS-B's synchronous query/execute path wraps execution in:
//     using var scope = LadybugInstrumentation.StartQuery(cypher);
//     ... scope.SetSuccess() / scope.SetError(ex) / scope.SetCancelled() ...
//
// WS-H owns this file and the sibling LadybugDiagnostics.cs. The seam shape (StartQuery(string)
// -> QueryScope with SetSuccess()/SetError(Exception?)/SetCancelled()/Dispose()) is FROZEN by §D5
// and the already-merged WS-B Connection.cs; the implementation below fills it in with the real
// ActivitySource("LadybugDB") + Meter instrumentation (db.query.count, db.query.errors,
// db.query.duration.ms). It is zero-cost when unobserved: with no ActivityListener sampling the
// source, StartActivity returns null and only a stopwatch timestamp is taken; with no MeterListener,
// the meter Add/Record calls are no-ops.

using System;
using System.Diagnostics;

namespace LadybugDB.Diagnostics;

/// <summary>
/// Internal instrumentation seam called by the query-execution path (WS-B). Begins an activity and
/// times the call; the caller reports the outcome via <see cref="QueryScope"/>. Designed to be cheap
/// when unobserved: when no listener samples the source,
/// <see cref="ActivitySource.StartActivity(string, ActivityKind)"/> returns <see langword="null"/>
/// and only the stopwatch timestamp is taken.
/// </summary>
internal static class LadybugInstrumentation
{
    internal const string QueryActivityName = "LadybugDB.Query";

    /// <summary>Begin an instrumentation scope for a query. Always returns a usable scope.</summary>
    public static QueryScope StartQuery(string cypher)
    {
        Activity? activity = LadybugDiagnostics.ActivitySource.StartActivity(QueryActivityName, ActivityKind.Client);
        if (activity is not null)
        {
            activity.SetTag("db.system", "cypher");
            activity.SetTag("db.query.length", cypher?.Length ?? 0);
        }

        return new QueryScope(activity, Stopwatch.GetTimestamp());
    }
}

/// <summary>
/// Tracks one query execution for instrumentation. Created by <see cref="LadybugInstrumentation.StartQuery"/>.
/// The caller sets the outcome (<see cref="SetSuccess"/>/<see cref="SetError"/>/<see cref="SetCancelled"/>)
/// and disposes the scope, which records <c>db.query.count</c> and the <c>db.query.duration.ms</c>
/// histogram once. The seam shape is frozen by coordination decision §D5; do not change member names
/// or signatures (WS-B's merged query path calls them).
/// </summary>
internal sealed class QueryScope : IDisposable
{
    private readonly Activity? _activity;
    private readonly long _startTimestamp;
    private bool _disposed;

    internal QueryScope(Activity? activity, long startTimestamp)
    {
        _activity = activity;
        _startTimestamp = startTimestamp;
    }

    /// <summary>Marks the query as succeeded (activity status Ok).</summary>
    public void SetSuccess()
    {
        _activity?.SetStatus(ActivityStatusCode.Ok);
    }

    /// <summary>
    /// Marks the query as failed: increments <c>db.query.errors</c> and sets the activity status to
    /// Error (with the exception message, when supplied).
    /// </summary>
    public void SetError(Exception? exception = null)
    {
        LadybugDiagnostics.QueryErrors.Add(1);
        _activity?.SetStatus(ActivityStatusCode.Error, exception?.Message);
    }

    /// <summary>
    /// Marks the query as cancelled. Cancellations are counted as errors: increments
    /// <c>db.query.errors</c> and sets the activity status to Error("cancelled").
    /// </summary>
    public void SetCancelled()
    {
        LadybugDiagnostics.QueryErrors.Add(1);
        _activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
    }

    /// <summary>
    /// Records <c>db.query.count</c> (once) and the <c>db.query.duration.ms</c> histogram, then ends
    /// the activity. Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        LadybugDiagnostics.QueryCount.Add(1);

        // Stopwatch.GetTimestamp()/Frequency are ns2.0-safe; Stopwatch.GetElapsedTime is net7+ and
        // intentionally avoided so both TFMs share one code path.
        double elapsedMs = (Stopwatch.GetTimestamp() - _startTimestamp) * 1000.0 / Stopwatch.Frequency;
        LadybugDiagnostics.QueryDurationMs.Record(elapsedMs);

        _activity?.Dispose();
    }
}
