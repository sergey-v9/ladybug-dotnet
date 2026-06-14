using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using LadybugDB.Diagnostics;
using Xunit;

namespace LadybugDB.Tests;

/// <summary>
/// Managed (non-native) tests for the OpenTelemetry instrumentation surface (WS-H, design §4.6).
/// They attach an <see cref="ActivityListener"/> / <see cref="MeterListener"/> and drive the
/// instrumentation seam directly, so no engine is required.
/// </summary>
public sealed class DiagnosticsTests
{
    [Fact]
    public void Source_and_meter_have_the_pinned_names()
    {
        Assert.Equal("LadybugDB", LadybugDiagnostics.SourceName);
        Assert.Equal("LadybugDB", LadybugDiagnostics.ActivitySource.Name);

        var instruments = new HashSet<string>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "LadybugDB")
                {
                    instruments.Add(instrument.Name);
                }
            },
        };
        meterListener.Start();

        // Touch the static so the Meter and its instruments are constructed.
        LadybugDiagnostics.EnsureInitialized();

        Assert.Contains("db.query.count", instruments);
        Assert.Contains("db.query.errors", instruments);
        Assert.Contains("db.query.duration.ms", instruments);
    }

    private static ActivityListener RecordingListener(List<Activity> sink)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "LadybugDB",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = sink.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public void StartQuery_starts_an_activity_with_expected_tags()
    {
        var stopped = new List<Activity>();
        using ActivityListener listener = RecordingListener(stopped);

        using (QueryScope scope = LadybugInstrumentation.StartQuery("MATCH (n) RETURN n"))
        {
            scope.SetSuccess();
        }

        Activity activity = Assert.Single(stopped);
        Assert.Equal("LadybugDB.Query", activity.OperationName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Equal("cypher", activity.GetTagItem("db.system"));
        // The full query text must NOT be a tag (PII / cardinality); only its length.
        Assert.Null(activity.GetTagItem("db.statement"));
        Assert.Equal(18, activity.GetTagItem("db.query.length"));
    }
}
