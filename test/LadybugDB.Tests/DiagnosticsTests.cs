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

    private static MeterListener CountingListener(
        Dictionary<string, long> longSums,
        List<double> durations)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "LadybugDB")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            lock (longSums)
            {
                longSums.TryGetValue(instrument.Name, out long current);
                longSums[instrument.Name] = current + measurement;
            }
        });
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "db.query.duration.ms")
            {
                lock (durations)
                {
                    durations.Add(measurement);
                }
            }
        });
        listener.Start();
        return listener;
    }

    [Fact]
    public void Scope_records_count_and_duration_each_execution()
    {
        var longSums = new Dictionary<string, long>();
        var durations = new List<double>();
        using MeterListener listener = CountingListener(longSums, durations);

        using (QueryScope scope = LadybugInstrumentation.StartQuery("RETURN 1"))
        {
            scope.SetSuccess();
        }

        using (QueryScope scope = LadybugInstrumentation.StartQuery("RETURN 2"))
        {
            scope.SetSuccess();
        }

        listener.RecordObservableInstruments();

        Assert.Equal(2L, longSums["db.query.count"]);
        Assert.False(longSums.ContainsKey("db.query.errors")); // no errors recorded => no measurement
        Assert.Equal(2, durations.Count);
        Assert.All(durations, d => Assert.True(d >= 0.0));
    }

    [Fact]
    public void Error_increments_error_counter_and_sets_error_status()
    {
        var longSums = new Dictionary<string, long>();
        var durations = new List<double>();
        var stopped = new List<Activity>();
        using ActivityListener activityListener = RecordingListener(stopped);
        using MeterListener meterListener = CountingListener(longSums, durations);

        using (QueryScope scope = LadybugInstrumentation.StartQuery("MATCH (x:Nope) RETURN x"))
        {
            scope.SetError(new InvalidOperationException("Table Nope does not exist"));
        }

        Assert.Equal(1L, longSums["db.query.count"]);
        Assert.Equal(1L, longSums["db.query.errors"]);
        Activity activity = Assert.Single(stopped);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("Table Nope does not exist", activity.StatusDescription);
    }

    [Fact]
    public void Cancellation_is_counted_as_an_error()
    {
        var longSums = new Dictionary<string, long>();
        var durations = new List<double>();
        var stopped = new List<Activity>();
        using ActivityListener activityListener = RecordingListener(stopped);
        using MeterListener meterListener = CountingListener(longSums, durations);

        using (QueryScope scope = LadybugInstrumentation.StartQuery("MATCH (n) RETURN n"))
        {
            scope.SetCancelled();
        }

        Assert.Equal(1L, longSums["db.query.count"]);
        Assert.Equal(1L, longSums["db.query.errors"]);
        Activity activity = Assert.Single(stopped);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("cancelled", activity.StatusDescription);
    }
}
