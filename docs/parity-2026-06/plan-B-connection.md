# WS-B: Connection Control + Result Surface Implementation Plan
> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]` checkboxes.
**Goal:** Add the synchronous connection-control surface (`Interrupt`/`SetQueryTimeout`/`SetMaxThreadsForExec`/`GetMaxThreadsForExec`/`QueryAll`) and the rich result surface (`Summary`/`Columns`/`GetColumnType`/`ResetIterator`/`HasNextQueryResult`/`GetNextQueryResult`) plus the new `QuerySummary`, `ColumnSchema`, and `LogicalType` value types, while converting `Connection` and `QueryResult` to `partial class` with the named seams D/E/F/H extend.
**Architecture:** `Connection.cs` and `QueryResult.cs` become `partial class` so async (D), Arrow (E), engine-extension (F), and OTel (H) workstreams add their own partial files without editing B's. B exposes a thin internal execution seam (`ExecuteInstrumented`) for H to wrap, and an internal native-handle accessor (`ref LbugQueryResult`) for E's raw Arrow partial. New managed types `LogicalType`/`ColumnSchema`/`QuerySummary` wrap the engine's logical-type and query-summary handles; `QueryAll` walks the `has_next_query_result`/`get_next_query_result` chain so multi-statement queries return every result set.
**Tech Stack:** C# dual-target `net10.0;netstandard2.0`; xUnit + `Xunit.SkippableFact` with the `TestEnvironment.NativeAvailable` / `LADYBUG_REQUIRE_NATIVE` native gate; P/Invoke surface supplied by WS-A (`LadybugDB.Interop.Native`).

---

## Files

**Create**
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\LogicalType.cs` — public `sealed class LogicalType` wrapping `lbug_logical_type`.
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\ColumnSchema.cs` — public `sealed record ColumnSchema(string Name, LogicalType Type)`.
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\QuerySummary.cs` — public `readonly record struct QuerySummary(double CompilingTimeMs, double ExecutionTimeMs)`.
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Connection.Control.cs` — partial `Connection`: `Interrupt`/`SetQueryTimeout`/`SetMaxThreadsForExec`/`GetMaxThreadsForExec`/`QueryAll`.

**Modify**
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Connection.cs` — `sealed class` → `sealed partial class`; add the internal `ExecuteInstrumented` seam (H hook) and an internal `WithGate`/handle accessor used by `Connection.Control.cs`.
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\QueryResult.cs` — `sealed class` → `sealed partial class`; add `Summary`/`Columns`/`GetColumnType`/`ResetIterator`/`HasNextQueryResult`/`GetNextQueryResult`; add the internal `ref LbugQueryResult` seam (E hook) and internal ctor flag for chained results.
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\FlatTuple.cs` — move disposal to the `Interlocked`/`Volatile` pattern (design §7) for thread-safety consistency.

**Test**
- `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LogicalTypeTests.cs` — managed-only (NOT gated): `LogicalType.ToString` rendering, `ColumnSchema`/`QuerySummary` value semantics.
- `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\ConnectionControlTests.cs` — `SkippableFact` native-gated: timeout, max-threads round-trip, interrupt.
- `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\ResultSurfaceTests.cs` — `SkippableFact` native-gated: `Summary`, `Columns`/`GetColumnType`, `ResetIterator`, multi-statement `QueryAll`/`HasNextQueryResult`/`GetNextQueryResult`.

**Consumed from WS-A (Phase 1; must already exist & build before B starts):**
P/Invoke methods on `LadybugDB.Interop.Native` (declared on both `Native.LibraryImport.cs` and `Native.DllImport.cs`) and the `LbugQuerySummary` struct in `NativeTypes.cs`:
- `ConnectionInterrupt(ref LbugConnection)`
- `ConnectionSetQueryTimeout(ref LbugConnection, ulong) -> LbugState`
- `ConnectionSetMaxNumThreadForExec(ref LbugConnection, ulong) -> LbugState`
- `ConnectionGetMaxNumThreadForExec(ref LbugConnection, out ulong) -> LbugState`
- `QueryResultGetQuerySummary(ref LbugQueryResult, out LbugQuerySummary) -> LbugState`
- `QuerySummaryGetCompilingTime(ref LbugQuerySummary) -> double`
- `QuerySummaryGetExecutionTime(ref LbugQuerySummary) -> double`
- `QuerySummaryDestroy(ref LbugQuerySummary)`
- `QueryResultGetColumnDataType(ref LbugQueryResult, ulong, out LbugLogicalType) -> LbugState`
- `QueryResultResetIterator(ref LbugQueryResult)`
- `QueryResultHasNextQueryResult(ref LbugQueryResult) -> bool`
- `QueryResultGetNextQueryResult(ref LbugQueryResult, out LbugQueryResult) -> LbugState`
- `DataTypeClone(ref LbugLogicalType, out LbugLogicalType)`
- `DataTypeGetChildType(ref LbugLogicalType, out LbugLogicalType) -> LbugState`
- `DataTypeGetNumElementsInArray(ref LbugLogicalType, out ulong) -> LbugState`
- (already present) `DataTypeGetId(ref LbugLogicalType) -> LbugDataTypeId`, `DataTypeDestroy(ref LbugLogicalType)`.

> **If a consumed `Native.*` method or `LbugQuerySummary` is missing when B starts**, that is a WS-A gap — stop and flag it; do not declare interop in B's owned files (B does not own the `Interop/` directory).

---

## Build & test commands (used throughout)

- Build everything: `dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug`
- Run the unit tests:
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug`
- Run one class only (managed-only fast loop):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~LogicalTypeTests"`
- Native-gated classes auto-skip when `TestEnvironment.NativeAvailable` is false; to force them on a machine with the native lib staged, set `LADYBUG_REQUIRE_NATIVE=1` (PowerShell: `$env:LADYBUG_REQUIRE_NATIVE = '1'`).

---

## TASK 0 — Convert `Connection`/`QueryResult` to `partial class` (the seam foundation)

This must land first so D/E/F/H can add their partial files. It is a pure refactor: no behavior change, so the existing suite is the test.

- [ ] Run the existing suite to capture a green baseline:
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug`
  Expected: PASS (native-gated cases skip if no native lib; managed cases pass).
- [ ] In `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Connection.cs`, change the class header from
  `public sealed class Connection : IDisposable` to
  `public sealed partial class Connection : IDisposable`.
- [ ] In `W:\code\ladybug\tools\csharp_api\src\LadybugDB\QueryResult.cs`, change the class header from
  `public sealed class QueryResult : IDisposable` to
  `public sealed partial class QueryResult : IDisposable`.
- [ ] Build: `dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug` — expected PASS (both TFMs compile; `partial` is a no-op with a single file).
- [ ] Re-run the suite (`dotnet test ...LadybugDB.Tests.csproj -c Debug`) — expected PASS unchanged.
- [ ] Commit:
  `git add -A && git commit -m "refactor(ws-b): make Connection and QueryResult partial for D/E/F/H seams"`

---

## TASK 1 — `QuerySummary` value type (managed-only, NOT gated)

- [ ] Create the failing test file
  `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LogicalTypeTests.cs` with the `QuerySummary` case (the file grows over Tasks 1–3):
  ```csharp
  using LadybugDB;
  using Xunit;

  namespace LadybugDB.Tests;

  /// <summary>
  /// Managed-only value-semantics and rendering tests for the WS-B result-surface types. These do
  /// not touch the native library and therefore must NOT be gated on TestEnvironment.NativeAvailable.
  /// </summary>
  public sealed class LogicalTypeTests
  {
      [Fact]
      public void QuerySummary_HasValueEquality_AndCarriesTimings()
      {
          var a = new QuerySummary(1.5, 2.5);
          var b = new QuerySummary(1.5, 2.5);

          Assert.Equal(1.5, a.CompilingTimeMs);
          Assert.Equal(2.5, a.ExecutionTimeMs);
          Assert.Equal(a, b);
          Assert.Equal(a.GetHashCode(), b.GetHashCode());
          Assert.NotEqual(a, new QuerySummary(1.5, 9.9));
      }
  }
  ```
- [ ] Run it (expected FAIL — `QuerySummary` does not exist, compile error):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~LogicalTypeTests"`
- [ ] Create `W:\code\ladybug\tools\csharp_api\src\LadybugDB\QuerySummary.cs`:
  ```csharp
  namespace LadybugDB;

  /// <summary>
  /// Timing breakdown for an executed query: the planner's compilation time and the executor's run
  /// time, both in milliseconds. Sourced from the engine's <c>lbug_query_summary</c>.
  /// </summary>
  public readonly record struct QuerySummary(double CompilingTimeMs, double ExecutionTimeMs);
  ```
- [ ] Run the filtered test — expected PASS.
- [ ] Commit:
  `git add -A && git commit -m "feat(ws-b): add QuerySummary value type"`

---

## TASK 2 — `LogicalType` rendering (managed-only, NOT gated)

`LogicalType` wraps `lbug_logical_type`, but its `ToString` rendering is pure managed logic over `Id`/`ChildType`/`FixedArraySize`. To keep this test native-free, expose an `internal` constructor that builds a `LogicalType` from already-decoded fields (no native handle). The native-backed factory lands in Task 6.

- [ ] Add the rendering test to `LogicalTypeTests.cs` (append inside the class):
  ```csharp
      [Fact]
      public void LogicalType_ToString_RendersScalarsNestedAndArrays()
      {
          var int64 = LogicalType.CreateForTests(DataTypeId.Int64, child: null, fixedArraySize: null);
          Assert.Equal("INT64", int64.ToString());

          var listOfString = LogicalType.CreateForTests(
              DataTypeId.List,
              child: LogicalType.CreateForTests(DataTypeId.String, null, null),
              fixedArraySize: null);
          Assert.Equal("LIST(STRING)", listOfString.ToString());

          var arrayOfDouble = LogicalType.CreateForTests(
              DataTypeId.Array,
              child: LogicalType.CreateForTests(DataTypeId.Double, null, null),
              fixedArraySize: 3);
          Assert.Equal("ARRAY(DOUBLE, 3)", arrayOfDouble.ToString());
          Assert.Equal(DataTypeId.Array, arrayOfDouble.Id);
          Assert.Equal(3UL, arrayOfDouble.FixedArraySize);
          Assert.Equal(DataTypeId.Double, arrayOfDouble.ChildType!.Id);
      }
  ```
- [ ] Run it (expected FAIL — `LogicalType` does not exist):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~LogicalTypeTests"`
- [ ] Create `W:\code\ladybug\tools\csharp_api\src\LadybugDB\LogicalType.cs` with the managed-only shape (the native factory is added in Task 6; here only the test seam + rendering exist so the file compiles and the test passes):
  ```csharp
  using System.Globalization;

  namespace LadybugDB;

  /// <summary>
  /// The logical data type of a result column or value: its <see cref="DataTypeId"/>, the element
  /// type for LIST/ARRAY columns (<see cref="ChildType"/>), and the fixed length for ARRAY columns
  /// (<see cref="FixedArraySize"/>). Wraps the engine's <c>lbug_logical_type</c>.
  /// </summary>
  public sealed class LogicalType
  {
      /// <summary>The logical type identifier.</summary>
      public DataTypeId Id { get; }

      /// <summary>The element type for LIST and ARRAY types; <see langword="null"/> otherwise.</summary>
      public LogicalType? ChildType { get; }

      /// <summary>The fixed element count for ARRAY types; <see langword="null"/> otherwise.</summary>
      public ulong? FixedArraySize { get; }

      private LogicalType(DataTypeId id, LogicalType? childType, ulong? fixedArraySize)
      {
          Id = id;
          ChildType = childType;
          FixedArraySize = fixedArraySize;
      }

      /// <summary>Test-only factory that builds a <see cref="LogicalType"/> from decoded fields
      /// without touching the native library. Used by managed-only rendering tests.</summary>
      internal static LogicalType CreateForTests(DataTypeId id, LogicalType? child, ulong? fixedArraySize)
          => new(id, child, fixedArraySize);

      /// <inheritdoc />
      public override string ToString()
      {
          string name = Id.ToString().ToUpperInvariant();
          return Id switch
          {
              DataTypeId.List when ChildType is not null => $"LIST({ChildType})",
              DataTypeId.Array when ChildType is not null && FixedArraySize is ulong n
                  => $"ARRAY({ChildType}, {n.ToString(CultureInfo.InvariantCulture)})",
              _ => name,
          };
      }
  }
  ```
  > Note: `DataTypeId.Int64.ToString()` yields `"Int64"`; `.ToUpperInvariant()` gives `"INT64"`. `String` → `"STRING"`, `Double` → `"DOUBLE"`, matching the pinned examples.
- [ ] Add the `internal` test-seam visibility — it is already covered by the existing
  `<InternalsVisibleTo Include="LadybugDB.Tests" />` in `LadybugDB.csproj` (line 16). No project change needed; verify by building.
- [ ] Run the filtered test — expected PASS.
- [ ] Commit:
  `git add -A && git commit -m "feat(ws-b): add LogicalType with scalar/nested/array rendering"`

---

## TASK 3 — `ColumnSchema` value type (managed-only, NOT gated)

- [ ] Add the test to `LogicalTypeTests.cs` (append inside the class):
  ```csharp
      [Fact]
      public void ColumnSchema_HasValueEquality_OverNameAndType()
      {
          var t1 = LogicalType.CreateForTests(DataTypeId.Int64, null, null);
          var t2 = LogicalType.CreateForTests(DataTypeId.Int64, null, null);

          var a = new ColumnSchema("age", t1);
          var b = new ColumnSchema("age", t1);

          Assert.Equal("age", a.Name);
          Assert.Same(t1, a.Type);
          Assert.Equal(a, b);                       // same Name + same LogicalType reference
          Assert.NotEqual(a, new ColumnSchema("name", t1));
          Assert.NotEqual(a, new ColumnSchema("age", t2)); // different LogicalType reference
      }
  ```
  > `LogicalType` is a reference type with default (reference) equality, so two distinct instances are unequal — the test reflects that.
- [ ] Run it (expected FAIL — `ColumnSchema` does not exist):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~LogicalTypeTests"`
- [ ] Create `W:\code\ladybug\tools\csharp_api\src\LadybugDB\ColumnSchema.cs`:
  ```csharp
  namespace LadybugDB;

  /// <summary>The name and logical type of a single result column.</summary>
  public sealed record ColumnSchema(string Name, LogicalType Type);
  ```
- [ ] Run the filtered test — expected PASS.
- [ ] Commit:
  `git add -A && git commit -m "feat(ws-b): add ColumnSchema record"`

---

## TASK 4 — `Connection.SetQueryTimeout` (native-gated round-trip)

Create the control partial and the first method. The timeout method has no readback in the C API, so the round-trip test proves it by observing that a tiny timeout makes a heavy query fail with a timeout error.

- [ ] Create the test file
  `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\ConnectionControlTests.cs`:
  ```csharp
  using System;
  using LadybugDB;
  using Xunit;

  namespace LadybugDB.Tests;

  /// <summary>
  /// Native-gated round-trip tests for the WS-B connection-control surface. They skip when the
  /// native library is unavailable.
  /// </summary>
  public sealed class ConnectionControlTests
  {
      [SkippableFact]
      public void SetQueryTimeout_TinyTimeout_AbortsHeavyQuery()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              conn.Query("UNWIND range(1, 200000) AS x CREATE (:N {v: x})").Dispose();

              conn.SetQueryTimeout(TimeSpan.FromMilliseconds(1));

              // A heavy cartesian product should exceed a 1ms budget and surface as a query failure.
              Assert.Throws<LadybugQueryException>(() =>
                  conn.Query("MATCH (a:N), (b:N), (c:N) RETURN count(*)").Dispose());
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  }
  ```
- [ ] Run it (expected FAIL to compile — `SetQueryTimeout` does not exist):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~ConnectionControlTests"`
- [ ] First, add the internal gate helper to `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Connection.cs` so the control partial can reuse the existing `_gate` lock and disposal guard. Insert this method just below `ThrowIfDisposed()` (after line 143, before the closing brace):
  ```csharp
      /// <summary>Runs <paramref name="action"/> under the connection's serialization gate and
      /// disposal guard. Used by the WS-B control partial (Connection.Control.cs).</summary>
      internal T WithGate<T>(NativeHandleFunc<T> action)
      {
          lock (_gate)
          {
              ThrowIfDisposed();
              return action(ref _handle);
          }
      }

      /// <summary>Delegate that operates on the native connection handle under the gate.</summary>
      internal delegate T NativeHandleFunc<T>(ref Interop.LbugConnection handle);

      /// <summary>Runs an action that returns nothing under the gate (e.g. interrupt).</summary>
      internal void WithGate(NativeHandleAction action)
      {
          lock (_gate)
          {
              ThrowIfDisposed();
              action(ref _handle);
          }
      }

      /// <summary>Delegate that operates on the native connection handle under the gate.</summary>
      internal delegate void NativeHandleAction(ref Interop.LbugConnection handle);
  ```
  > `Interrupt` must NOT take the gate (it intentionally fires while another thread holds it executing a query); it is handled separately in Task 6. `SetQueryTimeout`/`SetMaxThreadsForExec`/`GetMaxThreadsForExec` are config calls and run under the gate via `WithGate`.
- [ ] Create `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Connection.Control.cs`:
  ```csharp
  using System;
  using System.Collections.Generic;
  using LadybugDB.Interop;

  namespace LadybugDB;

  public sealed partial class Connection
  {
      /// <summary>Sets the per-query execution timeout. The engine aborts a query that exceeds it.</summary>
      /// <param name="timeout">The timeout; rounded to whole milliseconds (the engine's unit).</param>
      public void SetQueryTimeout(TimeSpan timeout)
      {
          if (timeout < TimeSpan.Zero)
          {
              throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be non-negative.");
          }

          ulong ms = (ulong)Math.Max(0, (long)timeout.TotalMilliseconds);
          LbugState state = WithGate((ref LbugConnection h) => Native.ConnectionSetQueryTimeout(ref h, ms));
          if (state != LbugState.Success)
          {
              throw new LadybugException("Failed to set the query timeout.");
          }
      }
  }
  ```
- [ ] Build: `dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug` — expected PASS (both TFMs).
- [ ] Run the filtered test — expected PASS when native present, SKIP otherwise:
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~ConnectionControlTests"`
- [ ] Commit:
  `git add -A && git commit -m "feat(ws-b): Connection.SetQueryTimeout + gate seam"`

---

## TASK 5 — `SetMaxThreadsForExec` / `GetMaxThreadsForExec` (native-gated round-trip)

These have a real getter, so the round-trip is exact.

- [ ] Add the test to `ConnectionControlTests.cs` (append inside the class):
  ```csharp
      [SkippableFact]
      public void MaxThreadsForExec_RoundTrips()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              conn.SetMaxThreadsForExec(3);
              Assert.Equal(3UL, conn.GetMaxThreadsForExec());

              conn.SetMaxThreadsForExec(1);
              Assert.Equal(1UL, conn.GetMaxThreadsForExec());
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
- [ ] Run it (expected FAIL to compile — methods do not exist):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~ConnectionControlTests"`
- [ ] Add to `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Connection.Control.cs` (inside the partial class):
  ```csharp
      /// <summary>Sets the maximum number of threads the engine may use to execute a query.</summary>
      public void SetMaxThreadsForExec(ulong numThreads)
      {
          LbugState state = WithGate((ref LbugConnection h) => Native.ConnectionSetMaxNumThreadForExec(ref h, numThreads));
          if (state != LbugState.Success)
          {
              throw new LadybugException("Failed to set the maximum execution threads.");
          }
      }

      /// <summary>Returns the maximum number of threads the engine may use to execute a query.</summary>
      public ulong GetMaxThreadsForExec()
      {
          ulong result = WithGate((ref LbugConnection h) =>
          {
              LbugState state = Native.ConnectionGetMaxNumThreadForExec(ref h, out ulong value);
              if (state != LbugState.Success)
              {
                  throw new LadybugException("Failed to read the maximum execution threads.");
              }

              return value;
          });

          return result;
      }
  ```
- [ ] Build: `dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug` — expected PASS.
- [ ] Run the filtered test — expected PASS (native) / SKIP.
- [ ] Commit:
  `git add -A && git commit -m "feat(ws-b): Connection.Set/GetMaxThreadsForExec round-trip"`

---

## TASK 6 — `Connection.Interrupt` + native-backed `LogicalType` factory (native-gated)

`Interrupt` must fire without taking the gate (it cancels a query another thread is running). It also needs the native `LogicalType` factory, which we add now because `GetColumnType` (Task 8) and `Columns` (Task 9) depend on it.

- [ ] Add the interrupt test to `ConnectionControlTests.cs`. It starts a heavy query on a background thread and interrupts it from the main thread, asserting the query throws:
  ```csharp
      [SkippableFact]
      public void Interrupt_AbortsRunningQuery()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);
              conn.Query("UNWIND range(1, 200000) AS x CREATE (:N {v: x})").Dispose();

              Exception? captured = null;
              var worker = new System.Threading.Thread(() =>
              {
                  try
                  {
                      conn.Query("MATCH (a:N), (b:N), (c:N) RETURN count(*)").Dispose();
                  }
                  catch (Exception ex)
                  {
                      captured = ex;
                  }
              });

              worker.Start();
              System.Threading.Thread.Sleep(50); // let the query start
              conn.Interrupt();
              worker.Join(TimeSpan.FromSeconds(30));

              Assert.False(worker.IsAlive, "Interrupt did not unblock the query thread.");
              Assert.IsType<LadybugQueryException>(captured);
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
- [ ] Run it (expected FAIL to compile — `Interrupt` does not exist):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~ConnectionControlTests"`
- [ ] Add `Interrupt` to `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Connection.Control.cs` (it deliberately bypasses `_gate`, only honoring the disposal guard, so it can fire mid-query):
  ```csharp
      /// <summary>Interrupts the query currently executing on this connection, if any. Safe to call
      /// from another thread while a query runs; it intentionally does not take the connection gate.</summary>
      public void Interrupt()
      {
          ThrowIfDisposed();
          Native.ConnectionInterrupt(ref _handleForInterrupt);
      }
  ```
  > `_gate` and `_handle` are `private` in `Connection.cs`. `Interrupt` cannot lock `_gate` (it would deadlock behind the running query) and cannot pass `_handle` by `ref` from a partial method that doesn't see the field unless it is at least `private` within the same class — partials share members, so `_handle`/`_gate`/`ThrowIfDisposed()` are visible here. Use `_handle` directly. Replace the body with:
  ```csharp
      public void Interrupt()
      {
          ThrowIfDisposed();
          Native.ConnectionInterrupt(ref _handle);
      }
  ```
  (Remove the `_handleForInterrupt` reference; it was a placeholder.)
- [ ] Add the native-backed factory to `W:\code\ladybug\tools\csharp_api\src\LadybugDB\LogicalType.cs`. Add `using LadybugDB.Interop;` at the top, and inside the class add a factory that consumes an owned `LbugLogicalType` handle, decoding `Id`/`ChildType`/`FixedArraySize` and destroying every handle it touches (the managed `LogicalType` is a fully-decoded snapshot — it holds no native handle):
  ```csharp
      /// <summary>
      /// Builds a fully-decoded <see cref="LogicalType"/> from an owned native logical-type handle and
      /// destroys that handle (and any child handles it walks). The returned managed type holds no
      /// native resources.
      /// </summary>
      internal static LogicalType FromOwnedHandle(ref Interop.LbugLogicalType handle)
      {
          var id = (DataTypeId)Interop.Native.DataTypeGetId(ref handle);

          LogicalType? child = null;
          ulong? fixedSize = null;

          if (id is DataTypeId.List or DataTypeId.Array)
          {
              if (Interop.Native.DataTypeGetChildType(ref handle, out Interop.LbugLogicalType childHandle) == Interop.LbugState.Success)
              {
                  child = FromOwnedHandle(ref childHandle); // recursion destroys childHandle
              }
          }

          if (id is DataTypeId.Array)
          {
              if (Interop.Native.DataTypeGetNumElementsInArray(ref handle, out ulong n) == Interop.LbugState.Success)
              {
                  fixedSize = n;
              }
          }

          Interop.Native.DataTypeDestroy(ref handle);
          return new LogicalType(id, child, fixedSize);
      }
  ```
- [ ] Build: `dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug` — expected PASS.
- [ ] Run the filtered control test — expected PASS (native) / SKIP. Also re-run the managed-only `LogicalTypeTests` to confirm the factory addition did not break rendering:
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~LogicalTypeTests|FullyQualifiedName~ConnectionControlTests"`
- [ ] Commit:
  `git add -A && git commit -m "feat(ws-b): Connection.Interrupt + native LogicalType factory"`

---

## TASK 7 — `QueryResult.Summary` (native-gated)

- [ ] Create the result-surface test file
  `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\ResultSurfaceTests.cs`:
  ```csharp
  using System.Collections.Generic;
  using System.Linq;
  using LadybugDB;
  using Xunit;

  namespace LadybugDB.Tests;

  /// <summary>Native-gated tests for the WS-B QueryResult surface (summary, columns, multi-statement).</summary>
  public sealed class ResultSurfaceTests
  {
      private static (Database, Connection) NewGraph(string dbPath)
      {
          var db = new Database(dbPath);
          var conn = new Connection(db);
          conn.Query("CREATE NODE TABLE Person(name STRING, age INT64, PRIMARY KEY(name))").Dispose();
          conn.Query("CREATE (:Person {name: 'Alice', age: 30})").Dispose();
          conn.Query("CREATE (:Person {name: 'Bob', age: 42})").Dispose();
          return (db, conn);
      }

      [SkippableFact]
      public void Summary_ReportsNonNegativeTimings()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              (Database db, Connection conn) = NewGraph(dbPath);
              using (db)
              using (conn)
              {
                  using QueryResult result = conn.Query("MATCH (p:Person) RETURN p.name");
                  QuerySummary summary = result.Summary;

                  Assert.True(summary.CompilingTimeMs >= 0.0);
                  Assert.True(summary.ExecutionTimeMs >= 0.0);

                  // Cached: a second access returns an equal value.
                  Assert.Equal(summary, result.Summary);
              }
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  }
  ```
- [ ] Run it (expected FAIL to compile — `Summary` does not exist):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~ResultSurfaceTests"`
- [ ] Add the `Summary` property to `W:\code\ladybug\tools\csharp_api\src\LadybugDB\QueryResult.cs`. Add a backing nullable field next to `_columnNames` (line 15):
  ```csharp
      private QuerySummary? _summary;
  ```
  and add the lazy property (place after the `RowCount` property, around line 54):
  ```csharp
      /// <summary>Compilation and execution timings for this query, read lazily and cached.</summary>
      public QuerySummary Summary
      {
          get
          {
              ThrowIfDisposed();
              if (_summary is QuerySummary cached)
              {
                  return cached;
              }

              LbugState state = Native.QueryResultGetQuerySummary(ref _handle, out LbugQuerySummary native);
              if (state != LbugState.Success)
              {
                  throw new LadybugException("Failed to read the query summary.");
              }

              try
              {
                  var summary = new QuerySummary(
                      Native.QuerySummaryGetCompilingTime(ref native),
                      Native.QuerySummaryGetExecutionTime(ref native));
                  _summary = summary;
                  return summary;
              }
              finally
              {
                  Native.QuerySummaryDestroy(ref native);
              }
          }
      }
  ```
- [ ] Build — expected PASS.
- [ ] Run the filtered test — expected PASS (native) / SKIP.
- [ ] Commit:
  `git add -A && git commit -m "feat(ws-b): QueryResult.Summary (lazy, cached)"`

---

## TASK 8 — `QueryResult.GetColumnType` (native-gated)

- [ ] Add the test to `ResultSurfaceTests.cs` (append inside the class):
  ```csharp
      [SkippableFact]
      public void GetColumnType_ReturnsPerColumnLogicalTypes()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              (Database db, Connection conn) = NewGraph(dbPath);
              using (db)
              using (conn)
              {
                  using QueryResult result = conn.Query("MATCH (p:Person) RETURN p.name, p.age");

                  Assert.Equal(DataTypeId.String, result.GetColumnType(0).Id);
                  Assert.Equal(DataTypeId.Int64, result.GetColumnType(1).Id);
                  Assert.Equal("STRING", result.GetColumnType(0).ToString());
              }
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
- [ ] Run it (expected FAIL to compile — `GetColumnType` does not exist):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~ResultSurfaceTests"`
- [ ] Add `GetColumnType` to `W:\code\ladybug\tools\csharp_api\src\LadybugDB\QueryResult.cs` (place after `GetColumnName`, around line 67):
  ```csharp
      /// <summary>The logical type of the column at the given zero-based index.</summary>
      public LogicalType GetColumnType(ulong index)
      {
          ThrowIfDisposed();
          LbugState state = Native.QueryResultGetColumnDataType(ref _handle, index, out LbugLogicalType native);
          if (state != LbugState.Success)
          {
              throw new LadybugException($"Failed to read the data type of column {index}.");
          }

          return LogicalType.FromOwnedHandle(ref native);
      }
  ```
- [ ] Build — expected PASS.
- [ ] Run the filtered test — expected PASS (native) / SKIP.
- [ ] Commit:
  `git add -A && git commit -m "feat(ws-b): QueryResult.GetColumnType"`

---

## TASK 9 — `QueryResult.Columns` (native-gated)

- [ ] Add the test to `ResultSurfaceTests.cs` (append inside the class):
  ```csharp
      [SkippableFact]
      public void Columns_ExposesNameAndType_AndIsCached()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              (Database db, Connection conn) = NewGraph(dbPath);
              using (db)
              using (conn)
              {
                  using QueryResult result = conn.Query("MATCH (p:Person) RETURN p.name, p.age");

                  IReadOnlyList<ColumnSchema> columns = result.Columns;
                  Assert.Equal(2, columns.Count);
                  Assert.Equal("p.name", columns[0].Name);
                  Assert.Equal(DataTypeId.String, columns[0].Type.Id);
                  Assert.Equal("p.age", columns[1].Name);
                  Assert.Equal(DataTypeId.Int64, columns[1].Type.Id);

                  Assert.Same(columns, result.Columns); // cached: same instance
              }
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
- [ ] Run it (expected FAIL to compile — `Columns` does not exist):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~ResultSurfaceTests"`
- [ ] Add the cached `Columns` property to `W:\code\ladybug\tools\csharp_api\src\LadybugDB\QueryResult.cs`. Add a backing field next to `_summary`:
  ```csharp
      private ColumnSchema[]? _columns;
  ```
  and the property (place after `ColumnNames`, around line 88):
  ```csharp
      /// <summary>All columns (name + logical type), built once and cached.</summary>
      public IReadOnlyList<ColumnSchema> Columns
      {
          get
          {
              ThrowIfDisposed();
              if (_columns is not null)
              {
                  return _columns;
              }

              int count = checked((int)ColumnCount);
              var schemas = new ColumnSchema[count];
              for (int i = 0; i < count; i++)
              {
                  schemas[i] = new ColumnSchema(GetColumnName((ulong)i), GetColumnType((ulong)i));
              }

              _columns = schemas;
              return _columns;
          }
      }
  ```
- [ ] Build — expected PASS.
- [ ] Run the filtered test — expected PASS (native) / SKIP.
- [ ] Commit:
  `git add -A && git commit -m "feat(ws-b): QueryResult.Columns (name + type, cached)"`

---

## TASK 10 — `QueryResult.ResetIterator` (native-gated)

- [ ] Add the test to `ResultSurfaceTests.cs` (append inside the class):
  ```csharp
      [SkippableFact]
      public void ResetIterator_AllowsReReadingAllRows()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              (Database db, Connection conn) = NewGraph(dbPath);
              using (db)
              using (conn)
              {
                  using QueryResult result = conn.Query("MATCH (p:Person) RETURN p.name ORDER BY p.name");

                  List<object?[]> first = result.Rows().ToList();
                  Assert.Equal(2, first.Count);
                  Assert.False(result.HasNext());

                  result.ResetIterator();

                  Assert.True(result.HasNext());
                  List<object?[]> second = result.Rows().ToList();
                  Assert.Equal(2, second.Count);
                  Assert.Equal(first[0][0], second[0][0]);
              }
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
- [ ] Run it (expected FAIL to compile — `ResetIterator` does not exist):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~ResultSurfaceTests"`
- [ ] Add `ResetIterator` to `W:\code\ladybug\tools\csharp_api\src\LadybugDB\QueryResult.cs` (place after `GetNext`, around line 111):
  ```csharp
      /// <summary>Rewinds the tuple iterator to the first row so the result can be re-read.</summary>
      public void ResetIterator()
      {
          ThrowIfDisposed();
          Native.QueryResultResetIterator(ref _handle);
      }
  ```
- [ ] Build — expected PASS.
- [ ] Run the filtered test — expected PASS (native) / SKIP.
- [ ] Commit:
  `git add -A && git commit -m "feat(ws-b): QueryResult.ResetIterator"`

---

## TASK 11 — `HasNextQueryResult` / `GetNextQueryResult` + multi-statement `QueryAll` (native-gated)

This is the P0 multi-statement-truncation fix. `GetNextQueryResult` returns a chained result whose native handle is *not* independently owned in the same way as a fresh query result; to keep disposal correct we add an internal ctor flag `chained` that prevents double-destroy of a result that the parent owns. The engine's `lbug_query_result_get_next_query_result` yields a handle (`_is_owned_by_cpp` is set by the engine); calling `lbug_query_result_destroy` on each is the documented contract, so we destroy each chained result the same way — `QueryAll` returns independent `QueryResult` objects each of which destroys its own handle.

- [ ] Add the multi-statement tests to `ResultSurfaceTests.cs` (append inside the class):
  ```csharp
      [SkippableFact]
      public void QueryAll_ReturnsEveryResultSet()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              (Database db, Connection conn) = NewGraph(dbPath);
              using (db)
              using (conn)
              {
                  IReadOnlyList<QueryResult> results = conn.QueryAll(
                      "MATCH (p:Person) RETURN p.name ORDER BY p.name; " +
                      "MATCH (p:Person) RETURN count(*) AS c;");

                  try
                  {
                      Assert.Equal(2, results.Count);

                      List<object?[]> names = results[0].Rows().ToList();
                      Assert.Equal(new object?[] { "Alice" }, names[0]);
                      Assert.Equal(new object?[] { "Bob" }, names[1]);

                      object?[] countRow = results[1].Rows().Single();
                      Assert.Equal(2L, countRow[0]);
                  }
                  finally
                  {
                      foreach (QueryResult r in results)
                      {
                          r.Dispose();
                      }
                  }
              }
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }

      [SkippableFact]
      public void HasNextQueryResult_ChainWalksManually()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              (Database db, Connection conn) = NewGraph(dbPath);
              using (db)
              using (conn)
              {
                  using QueryResult first = conn.Query(
                      "MATCH (p:Person) RETURN count(*) AS c; MATCH (p:Person) RETURN p.name;");

                  Assert.True(first.HasNextQueryResult());
                  using QueryResult second = first.GetNextQueryResult();
                  Assert.False(second.HasNextQueryResult());
                  Assert.Equal(1, second.Rows().Count());
              }
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
- [ ] Run it (expected FAIL to compile — `QueryAll`/`HasNextQueryResult`/`GetNextQueryResult` do not exist):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~ResultSurfaceTests"`
- [ ] Add `HasNextQueryResult` and `GetNextQueryResult` to `W:\code\ladybug\tools\csharp_api\src\LadybugDB\QueryResult.cs` (place after `ResetIterator`):
  ```csharp
      /// <summary>Whether another result set follows this one (multi-statement queries).</summary>
      public bool HasNextQueryResult()
      {
          ThrowIfDisposed();
          return Native.QueryResultHasNextQueryResult(ref _handle);
      }

      /// <summary>
      /// Returns the next result set in a multi-statement query. The returned result is an independent
      /// <see cref="QueryResult"/> that owns and destroys its own native handle.
      /// </summary>
      public QueryResult GetNextQueryResult()
      {
          ThrowIfDisposed();
          LbugState state = Native.QueryResultGetNextQueryResult(ref _handle, out LbugQueryResult next);
          if (state != LbugState.Success)
          {
              throw new LadybugException("Failed to advance to the next query result.");
          }

          return new QueryResult(next);
      }
  ```
- [ ] Add `QueryAll` to `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Connection.Control.cs` (inside the partial class). It runs the multi-statement query, then walks the chain collecting every result, disposing partial results on failure:
  ```csharp
      /// <summary>
      /// Executes a (possibly multi-statement) Cypher query and returns every result set, walking the
      /// engine's result chain so no statement's output is truncated.
      /// </summary>
      public IReadOnlyList<QueryResult> QueryAll(string cypher)
      {
          QueryResult first = Query(cypher);
          var results = new List<QueryResult> { first };
          try
          {
              QueryResult current = first;
              while (current.HasNextQueryResult())
              {
                  current = current.GetNextQueryResult();
                  results.Add(current);
              }

              return results;
          }
          catch
          {
              foreach (QueryResult r in results)
              {
                  r.Dispose();
              }

              throw;
          }
      }
  ```
  > `Query` already takes the gate and throws `LadybugQueryException` on a failed first statement; the chained results are surfaced even when individual statements have no rows. Each returned `QueryResult` is the caller's to dispose (as the test shows).
- [ ] Build — expected PASS.
- [ ] Run the filtered test — expected PASS (native) / SKIP.
- [ ] Commit:
  `git add -A && git commit -m "fix(ws-b): multi-statement QueryAll + Has/GetNextQueryResult (P0)"`

---

## TASK 12 — `FlatTuple` disposal → `Interlocked`/`Volatile` (design §7) (managed-safe)

The existing `FlatTuple` uses a plain `bool _disposed`. Design §7 standardizes disposal on the `Interlocked`/`Volatile` pattern used by `Connection`/`QueryResult`/`Database`. There is no behavioral test hook for the race, so the guard test asserts idempotent dispose and post-dispose throw (both native-free against a synthetic handle is not possible since the ctor is internal and dispose calls native). Use a native-gated idempotency test that exercises the real path.

- [ ] Add a native-gated idempotency test to `ResultSurfaceTests.cs` (append inside the class):
  ```csharp
      [SkippableFact]
      public void FlatTuple_DoubleDispose_IsSafe()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              (Database db, Connection conn) = NewGraph(dbPath);
              using (db)
              using (conn)
              {
                  using QueryResult result = conn.Query("MATCH (p:Person) RETURN p.name");
                  Assert.True(result.HasNext());
                  FlatTuple tuple = result.GetNext();

                  tuple.Dispose();
                  tuple.Dispose(); // must be a no-op, never a double native destroy
                  Assert.Throws<System.ObjectDisposedException>(() => tuple.GetValue(0));
              }
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
- [ ] Run it (expected PASS already for double-dispose with the bool guard, but FAIL for the thread-safety contract is not observable; run to confirm current behavior):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~ResultSurfaceTests"`
  Expected: PASS (native) / SKIP — this test pins the contract before the refactor.
- [ ] Refactor `W:\code\ladybug\tools\csharp_api\src\LadybugDB\FlatTuple.cs` to the `Interlocked`/`Volatile` pattern. Add `using System.Threading;` at the top, change the field to `private int _disposed;`, and update the three sites:
  - `Dispose()`:
    ```csharp
      public void Dispose()
      {
          if (Interlocked.Exchange(ref _disposed, 1) != 0)
          {
              return;
          }

          Native.FlatTupleDestroy(ref _handle);
      }
    ```
  - `ToString()`:
    ```csharp
      public override string? ToString()
      {
          if (Volatile.Read(ref _disposed) != 0)
          {
              return null;
          }

          return Native.TakeString(Native.FlatTupleToString(ref _handle));
      }
    ```
  - `ThrowIfDisposed()`:
    ```csharp
      private void ThrowIfDisposed()
      {
          if (Volatile.Read(ref _disposed) != 0)
          {
              throw new ObjectDisposedException(nameof(FlatTuple));
          }
      }
    ```
- [ ] Build — expected PASS (both TFMs).
- [ ] Run the filtered test — expected PASS (native) / SKIP. Then run the full suite to confirm no regression:
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug`
- [ ] Commit:
  `git add -A && git commit -m "refactor(ws-b): FlatTuple disposal to Interlocked/Volatile (design 7)"`

---

## TASK 13 — Result execution seam for H (OTel) + handle seam for E (Arrow)

H wraps query execution in an `Activity`; E needs raw access to the `QueryResult` native handle for its `QueryResult.Arrow.cs` partial. Land both seams now so the Phase-2 merge of H/E does not touch B's files.

- [ ] Add a managed-only seam test that proves the H instrumentation hook exists and is a no-op by default. Append to a new file
  `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\SeamTests.cs`:
  ```csharp
  using LadybugDB;
  using LadybugDB.Diagnostics;
  using Xunit;

  namespace LadybugDB.Tests;

  /// <summary>
  /// Managed-only tests that pin the seams WS-B exposes for H (OTel) and E (Arrow). They assert the
  /// default instrumentation hook is a transparent pass-through and require no native library.
  /// </summary>
  public sealed class SeamTests
  {
      [Fact]
      public void DefaultQueryInstrumentation_IsPassThrough()
      {
          // The default hook invokes the operation and returns its result unchanged.
          int calls = 0;
          int Result() { calls++; return 42; }

          int value = QueryInstrumentation.Default.Execute("RETURN 1", Result);

          Assert.Equal(1, calls);
          Assert.Equal(42, value);
      }
  }
  ```
  > B defines a minimal `QueryInstrumentation` seam type with a `Default` pass-through; H later replaces `Default` with an `Activity`/`Meter`-recording implementation without editing `QueryResult.cs`/`Connection.cs`. The namespace `LadybugDB.Diagnostics` matches WS-H's owned file `Diagnostics/LadybugDiagnostics.cs`, but the seam *type* is owned by B.
- [ ] Run it (expected FAIL to compile — `QueryInstrumentation` does not exist):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~SeamTests"`
- [ ] Create the seam type
  `W:\code\ladybug\tools\csharp_api\src\LadybugDB\QueryInstrumentation.cs`:
  ```csharp
  using System;

  namespace LadybugDB.Diagnostics;

  /// <summary>
  /// Extension seam for wrapping query execution (timing, tracing, metrics). The core ships a
  /// transparent pass-through; WS-H installs an OpenTelemetry-backed implementation via
  /// <see cref="Current"/> without modifying the connection/result code paths.
  /// </summary>
  public abstract class QueryInstrumentation
  {
      /// <summary>The pass-through instrumentation: runs the operation, returns its result unchanged.</summary>
      public static QueryInstrumentation Default { get; } = new PassThrough();

      /// <summary>The instrumentation the core uses; defaults to <see cref="Default"/>.</summary>
      public static QueryInstrumentation Current { get; set; } = Default;

      /// <summary>Wraps <paramref name="operation"/> (identified by <paramref name="cypher"/>).</summary>
      public abstract T Execute<T>(string cypher, Func<T> operation);

      private sealed class PassThrough : QueryInstrumentation
      {
          public override T Execute<T>(string cypher, Func<T> operation) => operation();
      }
  }
  ```
- [ ] Wire the seam into the synchronous query path. In `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Connection.cs`, wrap the body of `Query` so H can observe it. Replace the `lock` block in `Query` (lines 40-45) with:
  ```csharp
          return Diagnostics.QueryInstrumentation.Current.Execute(cypher, () =>
          {
              lock (_gate)
              {
                  ThrowIfDisposed();
                  LbugState state = Native.ConnectionQuery(ref _handle, cypher, out LbugQueryResult resultHandle);
                  return Finish(state, resultHandle);
              }
          });
  ```
- [ ] Add the internal Arrow handle seam to `W:\code\ladybug\tools\csharp_api\src\LadybugDB\QueryResult.cs` (place near the bottom, before `ThrowIfDisposed`). E's `QueryResult.Arrow.cs` partial calls this to reach the raw handle under the disposal guard:
  ```csharp
      /// <summary>
      /// Internal seam for the Arrow partial (WS-E): runs <paramref name="action"/> against the raw
      /// native query-result handle under the disposal guard. Not part of the public surface.
      /// </summary>
      internal T WithHandle<T>(HandleFunc<T> action)
      {
          ThrowIfDisposed();
          return action(ref _handle);
      }

      /// <summary>Delegate that operates on the native query-result handle.</summary>
      internal delegate T HandleFunc<T>(ref Interop.LbugQueryResult handle);
  ```
- [ ] Build — expected PASS (both TFMs).
- [ ] Run the seam test — expected PASS:
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~SeamTests"`
- [ ] Run the full suite to confirm the `Query` rewrap did not regress anything:
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug`
- [ ] Commit:
  `git add -A && git commit -m "feat(ws-b): query instrumentation seam (H) + Arrow handle seam (E)"`

---

## TASK 14 — Extend `StructLayoutTests` for `LbugQuerySummary` (managed-only, NOT gated)

WS-A adds the `LbugQuerySummary` struct (`{ void* _query_summary; }`) to `NativeTypes.cs`. B owns the ABI guard for the result surface, so add the layout assertion here.

- [ ] Add the assertion to `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\StructLayoutTests.cs`. Extend the existing `SinglePointerHandle_IsOnePointerWide` theory with the new type by adding one `[InlineData(typeof(LbugQuerySummary))]` line under the existing entries (after line 33):
  ```csharp
      [InlineData(typeof(LbugQuerySummary))]
  ```
- [ ] Run it (expected FAIL to compile if WS-A has not yet added `LbugQuerySummary`; if it is present, expected PASS):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~StructLayoutTests"`
  > If this fails because `LbugQuerySummary` is absent, that is a WS-A dependency gap — flag it and do not add the struct from B (B does not own `Interop/NativeTypes.cs`).
- [ ] Expected PASS once `LbugQuerySummary` exists (it is a single pointer = `IntPtr.Size`).
- [ ] Commit:
  `git add -A && git commit -m "test(ws-b): ABI guard for LbugQuerySummary single-pointer layout"`

---

## TASK 15 — Final full-suite gate + both-TFM build

- [ ] Build the whole solution in Debug across both TFMs:
  `dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug`
  Expected: PASS for `net10.0` and `netstandard2.0`.
- [ ] Run the full test project:
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug`
  Expected: all managed-only tests (`LogicalTypeTests`, `SeamTests`, `StructLayoutTests`) PASS; native-gated tests (`ConnectionControlTests`, `ResultSurfaceTests`) PASS when native is present, otherwise SKIP.
- [ ] If a native lib is staged, force the gate to verify the round-trips really run:
  PowerShell: `$env:LADYBUG_REQUIRE_NATIVE = '1'; dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug; Remove-Item Env:\LADYBUG_REQUIRE_NATIVE`
  Expected: `NativeGateTests.NativeLibrary_LoadsWhenRequired` passes and all native-gated cases run (no skips).
- [ ] Commit any final touch-ups:
  `git add -A && git commit -m "chore(ws-b): finalize connection control + result surface workstream"`

---

## Self-review / done criteria (tied to WS-B "Done = §4.1 contract; tests")

- [ ] **§4.1 Connection surface present & exact:** `void Interrupt()`, `void SetQueryTimeout(TimeSpan)`, `void SetMaxThreadsForExec(ulong)`, `ulong GetMaxThreadsForExec()`, `IReadOnlyList<QueryResult> QueryAll(string)` — names/signatures match the pinned contract.
- [ ] **§4.1 QueryResult surface present & exact:** `QuerySummary Summary { get; }`, `IReadOnlyList<ColumnSchema> Columns { get; }`, `LogicalType GetColumnType(ulong)`, `void ResetIterator()`, `bool HasNextQueryResult()`, `QueryResult GetNextQueryResult()`.
- [ ] **§4.1 new types present & exact:** `readonly record struct QuerySummary(double CompilingTimeMs, double ExecutionTimeMs)`; `sealed record ColumnSchema(string Name, LogicalType Type)`; `sealed class LogicalType` with `DataTypeId Id`, `LogicalType? ChildType`, `ulong? FixedArraySize`, `override string ToString()`.
- [ ] **Partial-class seams landed:** `Connection` and `QueryResult` are `sealed partial class`; B added `Connection.Control.cs`, `QueryInstrumentation.cs` (H seam), the `QueryResult.WithHandle` Arrow seam (E), and wrapped `Query` in `QueryInstrumentation.Current` — D/E/F/H can add partial files without editing B's `.cs` files.
- [ ] **Multi-statement P0 fixed:** `QueryAll` walks `HasNextQueryResult`/`GetNextQueryResult`; the `QueryAll_ReturnsEveryResultSet` and `HasNextQueryResult_ChainWalksManually` tests prove no truncation.
- [ ] **Test gating correct:** `LogicalTypeTests`, `SeamTests`, `StructLayoutTests` are `[Fact]`/`[Theory]` and NOT gated; `ConnectionControlTests`/`ResultSurfaceTests` are `[SkippableFact]` gated on `TestEnvironment.NativeAvailable`.
- [ ] **Disposal hygiene:** `LogicalType` holds no native handle (fully decoded; every walked `LbugLogicalType` destroyed in `FromOwnedHandle`); `Summary` destroys its `LbugQuerySummary`; `FlatTuple` uses `Interlocked`/`Volatile` (design §7).
- [ ] **Build green both TFMs:** `dotnet build LadybugDB.slnx -c Debug` passes on `net10.0` and `netstandard2.0`.
- [ ] **ABI guard extended:** `StructLayoutTests` asserts `LbugQuerySummary` is one pointer wide.
- [ ] **No interop declared in B's files:** all `Native.*` methods consumed are WS-A-owned; `LbugQuerySummary` is WS-A-owned in `NativeTypes.cs`.
