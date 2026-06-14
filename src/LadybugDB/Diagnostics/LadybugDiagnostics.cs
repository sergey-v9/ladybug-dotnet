using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace LadybugDB.Diagnostics;

/// <summary>
/// OpenTelemetry instrumentation for the Ladybug binding: a static <see cref="ActivitySource"/> and a
/// <see cref="Meter"/> named <c>LadybugDB</c>. All work is zero-cost when no listener is attached
/// (an <see cref="ActivitySource"/> that nobody is recording returns <see langword="null"/> activities,
/// and a <see cref="Meter"/> with no listener does not allocate per measurement). Subscribe via the
/// OpenTelemetry SDK (<c>AddSource("LadybugDB")</c> / <c>AddMeter("LadybugDB")</c>) or the registration
/// sugar in <c>LadybugDB.Extensions</c>.
/// </summary>
public static class LadybugDiagnostics
{
    /// <summary>The shared source/meter name used for both the <see cref="ActivitySource"/> and <see cref="Meter"/>.</summary>
    public const string SourceName = "LadybugDB";

    /// <summary>Activity source for query execution spans.</summary>
    public static ActivitySource ActivitySource { get; } = new(SourceName);

    internal static readonly Meter Meter = new(SourceName);

    /// <summary>Count of query executions attempted (incremented once per execution).</summary>
    internal static readonly Counter<long> QueryCount =
        Meter.CreateCounter<long>("db.query.count");

    /// <summary>Count of query executions that failed (including cancellations).</summary>
    internal static readonly Counter<long> QueryErrors =
        Meter.CreateCounter<long>("db.query.errors");

    /// <summary>Wall-clock duration of query executions, in milliseconds.</summary>
    internal static readonly Histogram<double> QueryDurationMs =
        Meter.CreateHistogram<double>("db.query.duration.ms");

    /// <summary>
    /// Forces the static instruments to be constructed. The static initializer already does this on
    /// first access; this method gives callers (and tests) a no-side-effect way to trigger it.
    /// </summary>
    public static void EnsureInitialized()
    {
        // Referencing the fields is enough to run the type initializer.
        _ = ActivitySource;
        _ = QueryCount;
        _ = QueryErrors;
        _ = QueryDurationMs;
    }
}
