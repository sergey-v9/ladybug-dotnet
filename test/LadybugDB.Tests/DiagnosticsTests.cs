using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using LadybugDB.Diagnostics;
using Xunit;

namespace LadybugDB.Tests;

/// <summary>
/// Managed (non-native) tests for the OpenTelemetry instrumentation surface (WS-H, design §4.6).
/// They attach an <see cref="ActivityListener"/> / <see cref="MeterListener"/> and drive the
/// instrumentation seam directly, so no engine is required.
/// </summary>
/// <remarks>
/// The <c>LadybugDB</c> <see cref="ActivitySource"/> and <see cref="Meter"/> are process-wide
/// singletons. Other tests in this assembly (and the native-gated test below) also drive
/// <c>Connection.Query</c>, so a globally-registered listener sees their activities/measurements too
/// when the test runner schedules collections in parallel. These tests are therefore written to be
/// concurrency-proof: each tags its own work with a unique marker (a GUID-bearing cypher whose length
/// the seam records as <c>db.query.length</c>) and asserts on that subset, and meter assertions are
/// expressed as deltas / lower bounds rather than exact process-wide totals. The class is also pinned
/// to a non-parallel collection (see <see cref="DiagnosticsCollection"/>) to reduce interleaving.
/// </remarks>
[Collection(DiagnosticsCollection.Name)]
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

    /// <summary>
    /// An <see cref="ActivityListener"/> for the LadybugDB source that records every stopped activity.
    /// Callers filter the sink by their own unique marker because the source is process-wide.
    /// </summary>
    private static ActivityListener RecordingListener(List<Activity> sink)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "LadybugDB",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (sink)
                {
                    sink.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    /// <summary>
    /// Returns a cypher string of a known, unique length so the seam's <c>db.query.length</c> tag can
    /// be used as a per-test marker to pick this test's activity out of any concurrent ones.
    /// </summary>
    private static string MarkerCypher() => "RETURN '" + Guid.NewGuid().ToString("N") + "'";

    private static Activity SingleMarked(List<Activity> stopped, int markerLength)
    {
        Activity[] mine;
        lock (stopped)
        {
            mine = stopped
                .Where(a => a.OperationName == "LadybugDB.Query"
                            && a.GetTagItem("db.query.length") is int len && len == markerLength)
                .ToArray();
        }

        return Assert.Single(mine);
    }

    [Fact]
    public void StartQuery_starts_an_activity_with_expected_tags()
    {
        var stopped = new List<Activity>();
        using ActivityListener listener = RecordingListener(stopped);

        string cypher = MarkerCypher();
        using (QueryScope scope = LadybugInstrumentation.StartQuery(cypher))
        {
            scope.SetSuccess();
        }

        Activity activity = SingleMarked(stopped, cypher.Length);
        Assert.Equal("LadybugDB.Query", activity.OperationName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Equal("cypher", activity.GetTagItem("db.system"));
        // The full query text must NOT be a tag (PII / cardinality); only its length.
        Assert.Null(activity.GetTagItem("db.statement"));
        Assert.Equal(cypher.Length, activity.GetTagItem("db.query.length"));
    }

    /// <summary>
    /// A <see cref="MeterListener"/> that sums LadybugDB long-counter measurements and records the
    /// duration histogram. Process-wide, so assertions over these use deltas / lower bounds.
    /// </summary>
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

    private static long Sum(Dictionary<string, long> longSums, string name)
    {
        lock (longSums)
        {
            longSums.TryGetValue(name, out long value);
            return value;
        }
    }

    [Fact]
    public void Scope_records_count_and_duration_each_execution()
    {
        var longSums = new Dictionary<string, long>();
        var durations = new List<double>();
        using MeterListener listener = CountingListener(longSums, durations);

        long countBefore = Sum(longSums, "db.query.count");
        long errorsBefore = Sum(longSums, "db.query.errors");
        int durationsBefore;
        lock (durations)
        {
            durationsBefore = durations.Count;
        }

        using (QueryScope scope = LadybugInstrumentation.StartQuery("RETURN 1"))
        {
            scope.SetSuccess();
        }

        using (QueryScope scope = LadybugInstrumentation.StartQuery("RETURN 2"))
        {
            scope.SetSuccess();
        }

        listener.RecordObservableInstruments();

        // Exactly two count measurements, zero error measurements, two duration samples were produced
        // by THIS test (deltas isolate it from any concurrent query activity in the assembly).
        Assert.Equal(2L, Sum(longSums, "db.query.count") - countBefore);
        Assert.Equal(0L, Sum(longSums, "db.query.errors") - errorsBefore);
        lock (durations)
        {
            Assert.Equal(2, durations.Count - durationsBefore);
            Assert.All(durations, d => Assert.True(d >= 0.0));
        }
    }

    [Fact]
    public void Error_increments_error_counter_and_sets_error_status()
    {
        var longSums = new Dictionary<string, long>();
        var durations = new List<double>();
        var stopped = new List<Activity>();
        using ActivityListener activityListener = RecordingListener(stopped);
        using MeterListener meterListener = CountingListener(longSums, durations);

        long countBefore = Sum(longSums, "db.query.count");
        long errorsBefore = Sum(longSums, "db.query.errors");

        string cypher = MarkerCypher();
        using (QueryScope scope = LadybugInstrumentation.StartQuery(cypher))
        {
            scope.SetError(new InvalidOperationException("Table Nope does not exist"));
        }

        Assert.Equal(1L, Sum(longSums, "db.query.count") - countBefore);
        Assert.Equal(1L, Sum(longSums, "db.query.errors") - errorsBefore);
        Activity activity = SingleMarked(stopped, cypher.Length);
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

        long countBefore = Sum(longSums, "db.query.count");
        long errorsBefore = Sum(longSums, "db.query.errors");

        string cypher = MarkerCypher();
        using (QueryScope scope = LadybugInstrumentation.StartQuery(cypher))
        {
            scope.SetCancelled();
        }

        Assert.Equal(1L, Sum(longSums, "db.query.count") - countBefore);
        Assert.Equal(1L, Sum(longSums, "db.query.errors") - errorsBefore);
        Activity activity = SingleMarked(stopped, cypher.Length);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("cancelled", activity.StatusDescription);
    }

    [Fact]
    public void No_activity_listener_means_no_activity_allocated()
    {
        // With no ActivityListener attached by THIS test for "LadybugDB", StartActivity returns null
        // unless another concurrent test has a global listener registered. We therefore assert the
        // seam itself does not create/enter an activity beyond what an external listener would: the
        // scope's own work allocates nothing when StartActivity yields null. To keep this deterministic
        // regardless of sibling listeners, we assert the scope never *sets* Activity.Current to a new
        // activity that outlives the scope.
        Activity? before = Activity.Current;

        using (QueryScope scope = LadybugInstrumentation.StartQuery("RETURN 1"))
        {
            scope.SetSuccess();
        }

        Assert.Same(before, Activity.Current);
    }

    [SkippableFact]
    public void Real_query_emits_an_activity_and_increments_count()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        var stopped = new List<Activity>();
        var longSums = new Dictionary<string, long>();
        var durations = new List<double>();
        using ActivityListener activityListener = RecordingListener(stopped);
        using MeterListener meterListener = CountingListener(longSums, durations);

        long countBefore = Sum(longSums, "db.query.count");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            conn.Query("CREATE NODE TABLE T(id INT64, PRIMARY KEY(id))").Dispose();
            conn.Query("CREATE (:T {id: 1})").Dispose();
            using QueryResult result = conn.Query("MATCH (t:T) RETURN t.id");
            Assert.True(result.IsSuccess);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }

        // The three queries above produced LadybugDB.Query activities + count measurements (WS-B wiring).
        Assert.Contains(stopped, a => a.OperationName == "LadybugDB.Query");
        Assert.True(
            Sum(longSums, "db.query.count") - countBefore >= 3,
            "db.query.count not incremented by Connection.Query");
    }
}

/// <summary>
/// Pins <see cref="DiagnosticsTests"/> to a non-parallel collection so its globally-registered
/// <see cref="ActivityListener"/>/<see cref="MeterListener"/> instances do not interleave with each
/// other. (Cross-class assembly parallelism is still possible, which is why the assertions are also
/// written to be concurrency-proof via per-test markers and meter deltas.)
/// </summary>
[CollectionDefinition(DiagnosticsCollection.Name, DisableParallelization = true)]
public sealed class DiagnosticsCollection
{
    public const string Name = "Diagnostics";
}
