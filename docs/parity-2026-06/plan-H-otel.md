# WS-H OpenTelemetry Instrumentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]` checkboxes.

**Goal:** Add a zero-cost-when-unobserved `ActivitySource` + `Meter` ("LadybugDB") in core and a seam that WS-B's query-execution path calls to start an activity, record `db.query.count`/`db.query.errors`/`db.query.duration.ms`, and set Ok/Error status (cancellations counted as errors).

**Architecture:** A static `LadybugDiagnostics` (public, per §4.6) owns the `ActivitySource` and the `Meter` + its three instruments. An `internal` `LadybugInstrumentation.StartQuery(cypher)` returns a disposable `QueryScope` struct that begins an `Activity` and starts a timestamp; B's seam calls `scope.SetSuccess(rowCount?)` / `scope.SetError(message)` / `scope.SetCancelled()` then disposes it (records the duration histogram + counters). The whole path is allocation-light and no-ops when no `ActivityListener`/`MeterListener` is attached. WS-H never edits B's `Connection.cs`/`QueryResult.cs`; B calls the seam this plan pins.

**Tech Stack:** C#, dual TFM `net10.0;netstandard2.0`, `System.Diagnostics.DiagnosticSource` NuGet (supplies `ActivitySource` + `System.Diagnostics.Metrics` on ns2.0), xUnit + `Xunit.SkippableFact`, `ActivityListener`/`MeterListener` for managed tests.

---

## Files

**Create**
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Diagnostics\LadybugDiagnostics.cs` — public static `LadybugDiagnostics` (ActivitySource + Meter + instruments) **and** the `internal` `LadybugInstrumentation` seam + `QueryScope`.
- `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\DiagnosticsTests.cs` — managed (non-gated) listener-wiring tests + one native-gated end-to-end test.

**Modify**
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj` — add `System.Diagnostics.DiagnosticSource` PackageReference (ns2.0 needs it; net10.0 too for `Meter`/`Counter`/`Histogram` API stability across both TFMs).

**Seam consumed by WS-B (B's files, NOT edited here)**
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Connection.cs` — B's `Query`/`Execute` call `LadybugInstrumentation.StartQuery(...)`. This plan only pins the seam signature; B wires the calls in `plan-B-connection.md`.

---

## Pinned seam contract (WS-B must match this exactly)

WS-H exposes, and WS-B's synchronous query path consumes, this `internal` seam (both live in `LadybugDB`; `InternalsVisibleTo` already lets the test project see them):

```csharp
namespace LadybugDB.Diagnostics;

internal static class LadybugInstrumentation
{
    // Begins an Activity ("LadybugDB.Query", ActivityKind.Client) and starts timing.
    // Always returns a usable scope (even when no listener is attached — then it is a cheap no-op).
    internal static QueryScope StartQuery(string cypher);
}

internal struct QueryScope : IDisposable
{
    internal void SetSuccess(long? rowCount = null); // status Ok; tags db.row_count if provided
    internal void SetError(string? message);         // status Error; increments db.query.errors
    internal void SetCancelled();                     // status Error("cancelled"); increments errors
    internal void Dispose();                          // records db.query.count + duration histogram once
}
```

B's `Connection.Query`/`Connection.Execute` wrap execution as:

```csharp
using var scope = LadybugInstrumentation.StartQuery(cypher);
try
{
    /* existing native call + Finish(...) */
    scope.SetSuccess(/* rowCount or null */);
    return result;
}
catch (LadybugQueryException ex) { scope.SetError(ex.Message); throw; }
```

This keeps `db.query.count` recorded for every attempt, `db.query.errors` only on failure, `db.query.duration.ms` on dispose, and the activity status set. The seam name `LadybugInstrumentation.StartQuery` and the `QueryScope` members are the §4.6 "hook B exposes" — referenced from `03-high-level-plan.md` §4.1/§4.6. If WS-B's plan lands a different seam name, reconcile to **this** one (it is the owned WS-H surface).

---

## TASK 1 — Add the `System.Diagnostics.DiagnosticSource` dependency (ns2.0-safe instruments)

`Meter`/`Counter`/`Histogram` (`System.Diagnostics.Metrics`) and `ActivitySource` are not in the netstandard2.0 BCL; the `System.Diagnostics.DiagnosticSource` package ships ns2.0 assets for both. Add it before writing any diagnostics code so both TFMs compile.

- [ ] Add the package reference. Edit `W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj`: insert a new `ItemGroup` directly after the existing `InternalsVisibleTo` `ItemGroup` (after line 17, before the `<Import .../>`):

```xml
  <ItemGroup>
    <!-- ActivitySource + System.Diagnostics.Metrics (Meter/Counter/Histogram) for netstandard2.0;
         in-box on net10.0 but pinned here so both TFMs use the same API surface. -->
    <PackageReference Include="System.Diagnostics.DiagnosticSource" Version="8.0.1" />
  </ItemGroup>
```

- [ ] Build both TFMs to prove the dependency resolves and the package didn't break the AOT-compatible net10.0 target. Run:

```
dotnet build W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj -c Debug
```

Expected: build succeeds for `net10.0` and `netstandard2.0`, warning-clean (the repo is warning-clean per `Directory.Build.props`). If `System.Diagnostics.DiagnosticSource 8.0.1` reports a transitive net10.0/ns2.0 issue, this is the Phase-1 ns2.0 feasibility flag from spec §4.1 — record it in open questions, do not drop ns2.0.

- [ ] Commit:

```
git add W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj
git commit -m "build(otel): add System.Diagnostics.DiagnosticSource for ns2.0 instruments

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## TASK 2 — `LadybugDiagnostics`: ActivitySource + Meter exist with the pinned names (failing test first)

The §4.6 public surface: `SourceName == "LadybugDB"`, a static `ActivitySource` named `"LadybugDB"`, and a `Meter "LadybugDB"` exposing `db.query.count`, `db.query.errors`, `db.query.duration.ms`. This test asserts the public identity using a `MeterListener` to discover the instrument names and an `ActivityListener` to confirm the source name — no native lib needed, so it is a plain `[Fact]`.

- [ ] Write the failing test. Create `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\DiagnosticsTests.cs`:

```csharp
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
}
```

- [ ] Run the test, expect a COMPILE failure (type `LadybugDiagnostics` does not exist yet). Run:

```
dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~DiagnosticsTests.Source_and_meter_have_the_pinned_names"
```

Expected: build error `CS0103/CS0246` for `LadybugDiagnostics` / `LadybugDB.Diagnostics`.

- [ ] Implement the minimal `LadybugDiagnostics`. Create `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Diagnostics\LadybugDiagnostics.cs`:

```csharp
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
```

- [ ] Run the test again, expect PASS. Run:

```
dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~DiagnosticsTests.Source_and_meter_have_the_pinned_names"
```

Expected: 1 passed.

- [ ] Commit:

```
git add W:\code\ladybug\tools\csharp_api\src\LadybugDB\Diagnostics\LadybugDiagnostics.cs W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\DiagnosticsTests.cs
git commit -m "feat(otel): add LadybugDiagnostics ActivitySource + Meter (db.query.* instruments)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## TASK 3 — `LadybugInstrumentation.StartQuery` starts an activity with expected tags (failing test first)

This is the seam WS-B calls. The test attaches an `ActivityListener` that samples the `LadybugDB` source, drives the seam, and asserts an activity is started with the expected operation name and tags. No native lib — plain `[Fact]`.

- [ ] Add the failing test. Append to `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\DiagnosticsTests.cs` inside the `DiagnosticsTests` class (before the closing brace):

```csharp
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

        using (var scope = LadybugInstrumentation.StartQuery("MATCH (n) RETURN n"))
        {
            scope.SetSuccess(rowCount: 7);
        }

        Activity activity = Assert.Single(stopped);
        Assert.Equal("LadybugDB.Query", activity.OperationName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Equal("cypher", activity.GetTagItem("db.system"));
        Assert.Equal(7L, activity.GetTagItem("db.row_count"));
        // The full query text must NOT be a tag (PII / cardinality); only its length.
        Assert.Null(activity.GetTagItem("db.statement"));
        Assert.Equal(18, activity.GetTagItem("db.query.length"));
    }
```

- [ ] Run, expect COMPILE failure (`LadybugInstrumentation` / `StartQuery` / `QueryScope` not found):

```
dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~DiagnosticsTests.StartQuery_starts_an_activity_with_expected_tags"
```

Expected: build error `CS0103` for `LadybugInstrumentation`.

- [ ] Implement the seam. Append to `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Diagnostics\LadybugDiagnostics.cs` (after the `LadybugDiagnostics` class, same namespace):

```csharp
/// <summary>
/// Internal instrumentation seam called by the query-execution path (WS-B). Begins an activity and
/// times the call; the caller reports the outcome via <see cref="QueryScope"/>. Designed to be cheap
/// when unobserved: when no listener samples the source, <see cref="ActivitySource.StartActivity(string, ActivityKind)"/>
/// returns <see langword="null"/> and only the stopwatch timestamp is taken.
/// </summary>
internal static class LadybugInstrumentation
{
    internal const string QueryActivityName = "LadybugDB.Query";

    internal static QueryScope StartQuery(string cypher)
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
/// and disposes the scope, which records <c>db.query.count</c> and the <c>db.query.duration.ms</c> histogram once.
/// </summary>
internal struct QueryScope : IDisposable
{
    private readonly Activity? _activity;
    private readonly long _startTimestamp;
    private bool _disposed;

    internal QueryScope(Activity? activity, long startTimestamp)
    {
        _activity = activity;
        _startTimestamp = startTimestamp;
        _disposed = false;
    }

    internal void SetSuccess(long? rowCount = null)
    {
        if (_activity is not null)
        {
            _activity.SetStatus(ActivityStatusCode.Ok);
            if (rowCount.HasValue)
            {
                _activity.SetTag("db.row_count", rowCount.Value);
            }
        }
    }

    internal void SetError(string? message)
    {
        LadybugDiagnostics.QueryErrors.Add(1);
        _activity?.SetStatus(ActivityStatusCode.Error, message);
    }

    internal void SetCancelled()
    {
        LadybugDiagnostics.QueryErrors.Add(1);
        _activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        LadybugDiagnostics.QueryCount.Add(1);

        double elapsedMs = (Stopwatch.GetTimestamp() - _startTimestamp) * 1000.0 / Stopwatch.Frequency;
        LadybugDiagnostics.QueryDurationMs.Record(elapsedMs);

        _activity?.Dispose();
    }
}
```

Note: duration uses `Stopwatch.GetTimestamp()` + `Stopwatch.Frequency` (both ns2.0-safe); `Stopwatch.GetElapsedTime` is net7+ and must not be used.

- [ ] Run, expect PASS:

```
dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~DiagnosticsTests.StartQuery_starts_an_activity_with_expected_tags"
```

Expected: 1 passed.

- [ ] Commit:

```
git add W:\code\ladybug\tools\csharp_api\src\LadybugDB\Diagnostics\LadybugDiagnostics.cs W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\DiagnosticsTests.cs
git commit -m "feat(otel): add LadybugInstrumentation.StartQuery seam + QueryScope

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## TASK 4 — `db.query.count` increments and duration is recorded per execution (failing test first)

Asserts the meter side of the seam with a `MeterListener` recording measurements: one `db.query.count` and one `db.query.duration.ms` per scope, regardless of listener presence on the activity side. No native — plain `[Fact]`.

- [ ] Add the failing test. Append inside `DiagnosticsTests`:

```csharp
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

        using (var scope = LadybugInstrumentation.StartQuery("RETURN 1"))
        {
            scope.SetSuccess();
        }

        using (var scope = LadybugInstrumentation.StartQuery("RETURN 2"))
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

        using (var scope = LadybugInstrumentation.StartQuery("MATCH (x:Nope) RETURN x"))
        {
            scope.SetError("Table Nope does not exist");
        }

        Assert.Equal(1L, longSums["db.query.count"]);
        Assert.Equal(1L, longSums["db.query.errors"]);
        Activity activity = Assert.Single(stopped);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public void Cancellation_is_counted_as_an_error()
    {
        var longSums = new Dictionary<string, long>();
        var durations = new List<double>();
        using MeterListener meterListener = CountingListener(longSums, durations);

        using (var scope = LadybugInstrumentation.StartQuery("MATCH (n) RETURN n"))
        {
            scope.SetCancelled();
        }

        Assert.Equal(1L, longSums["db.query.count"]);
        Assert.Equal(1L, longSums["db.query.errors"]);
    }
```

- [ ] Run the three new tests, expect PASS (the implementation from Task 3 already supports them — this task verifies the meter contract):

```
dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~DiagnosticsTests.Scope_records_count_and_duration_each_execution|FullyQualifiedName~DiagnosticsTests.Error_increments_error_counter_and_sets_error_status|FullyQualifiedName~DiagnosticsTests.Cancellation_is_counted_as_an_error"
```

Expected: 3 passed. If `Scope_records_count_and_duration_each_execution` fails because `db.query.errors` has a zero-valued measurement, that means the implementation records errors unconditionally — fix `SetError`/`SetCancelled` to only `Add` on the failure path (already the case in Task 3). Confirm no regression.

- [ ] Commit:

```
git add W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\DiagnosticsTests.cs
git commit -m "test(otel): assert count/error/duration meter contract for QueryScope

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## TASK 5 — Zero-cost when unobserved (failing test first)

Spec §5.4: "zero cost when no listener is attached." Assert that with no `ActivityListener`, `StartQuery` produces a `null` activity (so the hot path allocates no `Activity`), and the scope still records meter measurements only when a `MeterListener` is present. This is the design's correctness guarantee. No native — plain `[Fact]`.

- [ ] Add the failing test. Append inside `DiagnosticsTests`:

```csharp
    [Fact]
    public void No_activity_listener_means_no_activity_allocated()
    {
        // No ActivityListener attached for "LadybugDB" in this test => StartActivity returns null.
        // We assert Activity.Current stays null across the scope, proving nothing was recorded.
        Assert.Null(Activity.Current);

        using (var scope = LadybugInstrumentation.StartQuery("RETURN 1"))
        {
            Assert.Null(Activity.Current); // no activity created/entered
            scope.SetSuccess();
        }

        Assert.Null(Activity.Current);
    }
```

- [ ] Run, expect PASS if no other test leaks a process-wide `ActivityListener`. Run:

```
dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~DiagnosticsTests.No_activity_listener_means_no_activity_allocated"
```

Expected: 1 passed. Note: `ActivityListener` is process-wide, so other tests MUST dispose their listeners (the `using` in `RecordingListener` callers guarantees this). If this test is flaky under parallel execution, the fix is to put `DiagnosticsTests` in its own xUnit collection so listener-bearing tests do not run concurrently with this one — add `[Collection("Diagnostics")]` and a `[CollectionDefinition("Diagnostics", DisableParallelization = true)]` marker class in the same file. Apply that only if a flake is observed.

- [ ] Commit:

```
git add W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\DiagnosticsTests.cs
git commit -m "test(otel): assert no Activity allocated when source is unobserved

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## TASK 6 — End-to-end instrumentation over a real query (native-gated `SkippableFact`)

The listener-wiring tests above are fully managed. This one proves the seam actually surrounds a real engine query once WS-B has wired `Connection.Query` to call `LadybugInstrumentation.StartQuery`. It is native-gated. **Order dependency:** this task lands its test now (Skipped when native absent / when B's wiring is not yet merged) and is *verified green* after the WS-B merge during integration.

- [ ] Add the native-gated test. Append inside `DiagnosticsTests`:

```csharp
    [SkippableFact]
    public void Real_query_emits_an_activity_and_increments_count()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        var stopped = new List<Activity>();
        var longSums = new Dictionary<string, long>();
        var durations = new List<double>();
        using ActivityListener activityListener = RecordingListener(stopped);
        using MeterListener meterListener = CountingListener(longSums, durations);

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

        // At least the three queries above produced activities + count measurements (WS-B wiring).
        Assert.NotEmpty(stopped);
        Assert.True(longSums.TryGetValue("db.query.count", out long count) && count >= 3, "db.query.count not incremented by Connection.Query");
        Assert.Contains(stopped, a => a.OperationName == "LadybugDB.Query");
    }
```

- [ ] Run it. When native is absent it SKIPS (managed CI). Run:

```
dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~DiagnosticsTests.Real_query_emits_an_activity_and_increments_count"
```

Expected (no native, or before WS-B wiring merged): 1 skipped. With native present AND WS-B wiring merged: 1 passed. If native is present but the test FAILS (no activities/count), that is the expected state until WS-B's `Connection.cs` calls the seam — record it and re-verify after the WS-B merge per `03-high-level-plan.md` §5 merge order (B→C→H...).

- [ ] Commit:

```
git add W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\DiagnosticsTests.cs
git commit -m "test(otel): native-gated end-to-end instrumentation over a real query

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## TASK 7 — Full build (both TFMs) + whole test project green

- [ ] Build the whole solution on both TFMs:

```
dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug
```

Expected: succeeds for `net10.0` + `netstandard2.0`, warning-clean.

- [ ] Run the entire test project (managed-only path; native-gated tests skip):

```
dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj
```

Expected: all `DiagnosticsTests` managed tests pass; `Real_query_emits_an_activity_and_increments_count` skips when native is absent; no pre-existing tests regress.

- [ ] If everything is green, commit any incidental fixes (otherwise nothing to commit):

```
git commit -am "chore(otel): finalize WS-H instrumentation; both TFMs green

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>" || echo "nothing to commit"
```

---

## Self-review / done criteria (ties to WS-H "Done = activity+meter; tests")

- [ ] `src/LadybugDB/Diagnostics/LadybugDiagnostics.cs` exists with the §4.6 public surface verbatim: `public const string SourceName = "LadybugDB"`, `public static ActivitySource ActivitySource { get; }` named `"LadybugDB"`, and a `Meter "LadybugDB"` exposing `db.query.count`, `db.query.errors`, `db.query.duration.ms`.
- [ ] The `internal LadybugInstrumentation.StartQuery(string)` → `QueryScope` seam (with `SetSuccess`/`SetError`/`SetCancelled`/`Dispose`) is the single hook WS-B's query path calls; WS-H did **not** edit `Connection.cs` or `QueryResult.cs`.
- [ ] Instrumentation is zero-cost when unobserved: `StartActivity` returns `null` with no listener (asserted), meter `Add`/`Record` are no-ops with no `MeterListener`.
- [ ] ns2.0-safe: depends only on `System.Diagnostics.DiagnosticSource`; no net7+-only API (`Stopwatch.GetElapsedTime` avoided; duration via `Stopwatch.GetTimestamp`/`Stopwatch.Frequency`).
- [ ] Cancellations are counted (`SetCancelled` increments `db.query.errors` and sets Error status) — verified by `Cancellation_is_counted_as_an_error`.
- [ ] Listener-wiring tests are fully managed (plain `[Fact]`, no native gate); the only native-gated test is the end-to-end `SkippableFact`.
- [ ] `dotnet build LadybugDB.slnx -c Debug` is green on both `net10.0` and `netstandard2.0`; `dotnet test` for `LadybugDB.Tests` is green (native-gated test skips without the engine).
- [ ] The seam name/signature pinned in this plan matches what `plan-B-connection.md` consumes; any divergence reconciled to this WS-H surface.
