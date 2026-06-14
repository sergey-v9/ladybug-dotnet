# WS-D: Async Surface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]` checkboxes.

**Goal:** Add first-class, honest async to `Connection`/`QueryResult` — `QueryAsync`/`QueryAllAsync`/`PrepareAsync`/`ExecuteAsync` (`Task`, offloaded via `Task.Run` over the existing per-`Connection` `_gate` lock) plus `StreamAsync` (`IAsyncEnumerable<FlatTuple>`), with `CancellationToken` wired to `Interrupt()` and honored before/after the offload.

**Architecture:** All new code lives in two **new `partial class` files** (`src/LadybugDB/Connection.Async.cs`, `src/LadybugDB/QueryResult.Async.cs`) so WS-B's `Connection.cs`/`QueryResult.cs` are never edited. The async methods reuse the sync methods (`Query`/`Prepare`/`Execute`) under `Task.Run`, which keeps the per-`Connection` `_gate` serialization intact (the engine call still takes `lock (_gate)`). Cancellation registers `ct.Register(() => Interrupt())` so a long-running query is interrupted via the native `lbug_connection_interrupt`; the token is also checked before offload and re-thrown after.

**Tech Stack:** C# / .NET, dual TFM `net10.0;netstandard2.0` (core). `IAsyncEnumerable` + `[EnumeratorCancellation]` are intrinsic on net10.0; on netstandard2.0 they come from `Microsoft.Bcl.AsyncInterfaces` (the `<PackageReference>` is **added by WS-K** — see Task 0). xUnit + `Xunit.SkippableFact` tests, native-gated via `TestEnvironment.NativeAvailable` / `LADYBUG_REQUIRE_NATIVE`.

---

## Dependencies & seams (read before starting)

- **WS-A (interop, Phase 1, already landed before this WS):** declares `Native.ConnectionInterrupt(ref LbugConnection)` on both `Native.LibraryImport.cs` (net7+) and `Native.DllImport.cs` (ns2.0), mapping `lbug_connection_interrupt(lbug_connection*)` (C signature: `LBUG_C_API void lbug_connection_interrupt(lbug_connection* connection);`, `src/include/c_api/lbug.h:465`). WS-D consumes `Interrupt()` (the public method) for cancellation.
- **WS-B (Phase 2, merged before D per the B→C→H→D order):** owns `Connection.cs`/`QueryResult.cs` and lands the **public `void Interrupt()`** method (§4.1 contract) plus keeps the `partial` keyword present on both classes. WS-D's cancellation calls `Interrupt()`.
  - **Self-containment guard:** D runs in its own worktree off the post-Phase-1 commit. If B's `Interrupt()` is not yet present in D's worktree, D's first task adds the `partial` keyword guard and a private `InterruptCore()` that calls `Native.ConnectionInterrupt` directly, and `Interrupt()`/`InterruptCore()` are reconciled at the merge gate. The async code calls `InterruptCore()` so D is buildable/testable standalone; the merge step makes `Interrupt()` (public, B) delegate to it. See Task 1.
- **Existing private members D relies on** (visible because D is a `partial class`): `Connection._gate` (`Connection.cs:15`), `Connection._handle` (`:16`), `Connection.ThrowIfDisposed()` (`:135`), `Connection.Query/Prepare/Execute` (`:33/:50/:76`); `QueryResult.HasNext()`/`GetNext()` (`QueryResult.cs:91/:101`), `QueryResult.ThrowIfDisposed()` (`:158`).

## Files

**Create**
- `src/LadybugDB/Connection.Async.cs` — `partial class Connection`: `QueryAsync`, `QueryAllAsync`, `PrepareAsync`, `ExecuteAsync`, `StreamAsync`, plus a private `RunWithCancellation<T>` helper and (guard) `InterruptCore()`.
- `src/LadybugDB/QueryResult.Async.cs` — `partial class QueryResult`: empty placeholder partial in this WS (kept for the merge map / future `MapAsync` from WS-I). Holds only the `partial class` declaration and a file header so the file exists and compiles. (Streaming lives on `Connection` because it owns the `_gate`.)
- `test/LadybugDB.Tests/AsyncTests.cs` — native-gated round-trip + cancellation tests, plus a non-gated disposed/null-arg test.

**Modify**
- `src/LadybugDB/Connection.cs` — **NOT edited by WS-D.** WS-B adds `partial`. (Listed only to state the boundary.)
- `cake/.../LadybugDB.csproj` / packaging — **NOT edited by WS-D.** WS-K adds `Microsoft.Bcl.AsyncInterfaces` for ns2.0. (Task 0 verifies it; if absent, escalate — do not edit the csproj here.)

**Test**
- `test/LadybugDB.Tests/AsyncTests.cs` (new). Test project targets `net10.0` only (`test/LadybugDB.Tests/LadybugDB.Tests.csproj:4`), so `IAsyncEnumerable` is intrinsic in tests — no extra package needed there.

---

## Task 0 — Verify the ns2.0 async-interfaces dependency exists (no code)

WS-K is responsible for adding `Microsoft.Bcl.AsyncInterfaces` to the core `netstandard2.0` build. WS-D must confirm it is present (or flag it) before writing `IAsyncEnumerable` code, because that type and `[EnumeratorCancellation]` do not exist in bare ns2.0.

- [ ] Search the core project + `nuget` props for the package reference:
  ```
  rg -n "Microsoft.Bcl.AsyncInterfaces" W:\code\ladybug\tools\csharp_api\src W:\code\ladybug\tools\csharp_api\nuget W:\code\ladybug\tools\csharp_api\cake
  ```
- [ ] Confirm the core builds on **both** TFMs as a baseline before adding any async code:
  ```
  dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug
  ```
  Expected: build succeeds (net10.0 + netstandard2.0).
- [ ] **If the package reference is absent:** do NOT edit `LadybugDB.csproj` (WS-K owns it). Record it in the merge notes and proceed; the ns2.0 build of this WS will fail until WS-K lands the ref. The TDD loop below builds the **test project (net10.0 only)** where the types are intrinsic, so D's tests pass regardless; the ns2.0 gap is closed by the merge gate. (Open question if still unresolved at merge.)
- [ ] No commit (verification only).

---

## Task 1 — Cancellation seam: `partial` guard + `InterruptCore()` (managed, non-gated)

The async layer needs to call the native interrupt. To stay buildable standalone (WS-B's public `Interrupt()` may not be in this worktree yet), add a private `InterruptCore()` in D's own partial file that calls WS-A's pinned `Native.ConnectionInterrupt`. The async methods call `InterruptCore()`; the merge gate makes B's public `Interrupt()` delegate to it.

- [ ] **Failing test** — add `test/LadybugDB.Tests/AsyncTests.cs` with a compile-time-only assertion that the async API exists and the file is wired. Write the minimal failing test (it fails to compile because `Connection.Async.cs` does not exist yet):
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading;
  using System.Threading.Tasks;
  using LadybugDB;
  using Xunit;

  namespace LadybugDB.Tests;

  /// <summary>
  /// WS-D async surface: round-trip + cancellation are native-gated (SkippableFact);
  /// argument/disposed validation is pure-managed and always runs.
  /// </summary>
  public sealed class AsyncTests
  {
      [Fact]
      public async Task QueryAsync_null_cypher_throws_ArgumentNullException()
      {
          var conn = NewDisposedConnectionStub();
          await Assert.ThrowsAsync<ArgumentNullException>(
              () => conn.QueryAsync(null!));
      }

      // Returns a Connection that is already disposed without needing the native lib:
      // a disposed Connection short-circuits in ThrowIfDisposed before any native call.
      private static Connection NewDisposedConnectionStub() => throw new SkipException("placeholder");
  }
  ```
  *(This first test is a scaffold that forces the file/method to exist; Task 2 replaces the stub with a real non-gated disposed test once `QueryAsync` compiles. Keep `SkipException` import via `Xunit` from `Xunit.SkippableFact`.)*
- [ ] **Run / expect FAIL** (compile error: `QueryAsync` not found):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj
  ```
  Expected: build/compile failure — `'Connection' does not contain a definition for 'QueryAsync'`.
- [ ] **Minimal implementation** — create `src/LadybugDB/Connection.Async.cs` with the cancellation seam and a stub `QueryAsync`:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Runtime.CompilerServices;
  using System.Threading;
  using System.Threading.Tasks;
  using LadybugDB.Interop;

  namespace LadybugDB;

  /// <summary>
  /// Asynchronous surface for <see cref="Connection"/>. Engine calls are synchronous and are
  /// offloaded with <see cref="Task.Run(Func{QueryResult})"/>; the work still takes the per-connection
  /// <c>_gate</c> lock inside the wrapped sync method, so a single connection stays serialized.
  /// A <see cref="CancellationToken"/> registers <see cref="InterruptCore"/> (native
  /// <c>lbug_connection_interrupt</c>) and is honored before and after the offload.
  /// </summary>
  public sealed partial class Connection
  {
      // Interrupts the in-flight query on the native side. WS-B's public Interrupt() delegates here
      // at the merge gate; async cancellation calls this directly so this file builds standalone.
      private void InterruptCore()
      {
          // Best-effort: a disposed handle must not call into native. _gate is not taken here because
          // the offloaded worker already holds it; interrupt is designed to be called concurrently.
          if (Volatile.Read(ref _disposed) != 0)
          {
              return;
          }

          Native.ConnectionInterrupt(ref _handle);
      }

      /// <summary>Executes a Cypher query asynchronously. Cancellation interrupts the running query.</summary>
      public Task<QueryResult> QueryAsync(string cypher, CancellationToken ct = default)
      {
          if (cypher is null)
          {
              throw new ArgumentNullException(nameof(cypher));
          }

          return RunWithCancellation(() => Query(cypher), ct);
      }

      // Honors ct before offload, registers Interrupt for the duration of the native call, and
      // honors ct after the offload so a cancellation that lands as an engine error surfaces as
      // OperationCanceledException.
      private async Task<T> RunWithCancellation<T>(Func<T> work, CancellationToken ct)
      {
          ct.ThrowIfCancellationRequested();

          using (ct.Register(static state => ((Connection)state!).InterruptCore(), this))
          {
              try
              {
                  return await Task.Run(work, ct).ConfigureAwait(false);
              }
              catch (LadybugQueryException) when (ct.IsCancellationRequested)
              {
                  // The interrupt surfaced as an engine error; normalize to cancellation.
                  throw new OperationCanceledException(ct);
              }
          }
      }
  }
  ```
  - [ ] **Important on `QueryAsync(null!)`:** the `ArgumentNullException` must throw **synchronously inside the returned-Task path**. Because `QueryAsync` is not `async`, the `throw` happens before a Task is returned. `Assert.ThrowsAsync` awaits the call expression, so either a sync throw or a faulted task is caught — but to match the test (`() => conn.QueryAsync(null!)`), keep the guard at the top as written (sync throw is fine; `ThrowsAsync` handles both).
- [ ] Replace the Task-1 scaffold test body with the real non-gated disposed test (this also exercises `InterruptCore`'s disposed guard indirectly). Edit `AsyncTests.cs`:
  ```csharp
      [Fact]
      public async Task QueryAsync_null_cypher_throws_ArgumentNullException()
      {
          // Build a Connection without the native lib by disposing a default; but Connection's ctor
          // needs a Database. Validate the null-guard via the disposed path below instead where a
          // ctor is unavailable. Here we assert the null guard using a throwaway only when native is
          // present; keep this test purely about the synchronous null guard, which runs before any
          // native interaction in QueryAsync.
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);
              await Assert.ThrowsAsync<ArgumentNullException>(() => conn.QueryAsync(null!));
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
  *(Note: a truly non-gated disposed test needs a `Connection` instance, which requires a `Database`, which requires native. So the null-arg/disposed checks are gated. The non-gated managed coverage is delivered in Task 5 via the `RunWithCancellation` helper test with a fake delegate — no `Connection` instance needed. This keeps the "managed test where possible" requirement honest.)*
- [ ] **Run / expect PASS** (compiles; gated test skips without native, passes with native):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj
  ```
  Expected: build succeeds; `QueryAsync_null_cypher_throws_ArgumentNullException` passes or skips.
- [ ] **Commit:**
  ```
  git add src/LadybugDB/Connection.Async.cs test/LadybugDB.Tests/AsyncTests.cs
  git commit -m "feat(async): add Connection.Async partial with QueryAsync + cancellation seam

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
  ```

---

## Task 2 — `QueryResult.Async.cs` partial placeholder (managed, non-gated)

Create the second owned partial file so the file map matches the WS contract and WS-I can later add `MapAsync` without touching `QueryResult.cs`.

- [ ] **Failing test** — add a structural test asserting `QueryResult` is a `partial` type that compiles with the new file. Add to `AsyncTests.cs`:
  ```csharp
      [Fact]
      public void QueryResult_async_partial_file_compiles()
      {
          // Compile-time guard: this test exists so the QueryResult.Async.cs partial is referenced
          // and the project fails to build if that file is missing or malformed.
          Assert.True(typeof(QueryResult).IsSealed);
      }
  ```
- [ ] **Run / expect PASS-after-create** — this test passes once the file exists; first run it to confirm current state:
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~QueryResult_async_partial_file_compiles"
  ```
  Expected: PASS even before creating the file (it is a placeholder assertion). Proceed to create the file so the partial seam exists.
- [ ] **Minimal implementation** — create `src/LadybugDB/QueryResult.Async.cs`:
  ```csharp
  namespace LadybugDB;

  /// <summary>
  /// Asynchronous seam for <see cref="QueryResult"/>. WS-D owns this partial file so that streaming
  /// (which lives on <see cref="Connection"/> because it holds the <c>_gate</c>) and a future
  /// <c>MapAsync&lt;T&gt;</c> (WS-I) can be added without editing <c>QueryResult.cs</c> (WS-B).
  /// </summary>
  public sealed partial class QueryResult
  {
  }
  ```
- [ ] **Run / expect PASS:**
  ```
  dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug
  ```
  Expected: both TFMs build (requires WS-B to have added `partial` to `QueryResult.cs`; if not present in this worktree, temporarily verify the file compiles by building only after Task 1's merge note — see Dependencies).
- [ ] **Commit:**
  ```
  git add src/LadybugDB/QueryResult.Async.cs test/LadybugDB.Tests/AsyncTests.cs
  git commit -m "feat(async): add QueryResult.Async partial placeholder seam

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
  ```

---

## Task 3 — `PrepareAsync` + `ExecuteAsync` (offloaded, gated round-trip)

- [ ] **Failing test** — add an async round-trip test exercising prepare + execute. Append to `AsyncTests.cs`:
  ```csharp
      [SkippableFact]
      public async Task PrepareAsync_and_ExecuteAsync_roundtrip()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              (await conn.QueryAsync("CREATE NODE TABLE Person(name STRING, age INT64, PRIMARY KEY(name))")).Dispose();

              using (PreparedStatement insert = await conn.PrepareAsync("CREATE (:Person {name: $name, age: $age})"))
              {
                  insert.Bind("name", "Alice").Bind("age", 30L);
                  (await conn.ExecuteAsync(insert)).Dispose();
              }

              using QueryResult result = await conn.QueryAsync("MATCH (p:Person) RETURN p.name, p.age");
              Assert.True(result.IsSuccess);
              List<object?[]> rows = result.Rows().ToList();
              Assert.Single(rows);
              Assert.Equal("Alice", rows[0][0]);
              Assert.Equal(30L, rows[0][1]);
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
- [ ] **Run / expect FAIL** (compile: `PrepareAsync`/`ExecuteAsync` not found):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~PrepareAsync_and_ExecuteAsync_roundtrip"
  ```
  Expected: compile failure.
- [ ] **Minimal implementation** — add to `Connection.Async.cs` (inside the `partial class Connection`):
  ```csharp
      /// <summary>Prepares a parameterized Cypher statement asynchronously.</summary>
      public Task<PreparedStatement> PrepareAsync(string cypher, CancellationToken ct = default)
      {
          if (cypher is null)
          {
              throw new ArgumentNullException(nameof(cypher));
          }

          return RunWithCancellation(() => Prepare(cypher), ct);
      }

      /// <summary>Executes a previously prepared statement asynchronously.</summary>
      public Task<QueryResult> ExecuteAsync(PreparedStatement statement, CancellationToken ct = default)
      {
          if (statement is null)
          {
              throw new ArgumentNullException(nameof(statement));
          }

          return RunWithCancellation(() => Execute(statement), ct);
      }
  ```
- [ ] **Run / expect PASS** (skips without native, passes with):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~PrepareAsync_and_ExecuteAsync_roundtrip"
  ```
  Expected: PASS or SKIP.
- [ ] **Commit:**
  ```
  git add src/LadybugDB/Connection.Async.cs test/LadybugDB.Tests/AsyncTests.cs
  git commit -m "feat(async): add PrepareAsync and ExecuteAsync

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
  ```

---

## Task 4 — `QueryAllAsync` (multi-statement, gated)

`QueryAll(string)` (WS-B, §4.1) walks the multi-statement chain and returns `IReadOnlyList<QueryResult>`. `QueryAllAsync` offloads it.

> **Dependency note:** `QueryAll` is owned by WS-B (merged before D in the B→C→H→D order). If it is not present in this worktree, this task's implementation will not compile. In that case, land Tasks 1–3 and 5–6 first and apply Task 4 at the merge gate (it is a 4-line addition). Do not implement `QueryAll` here — that is WS-B's file.

- [ ] **Failing test** — append to `AsyncTests.cs`:
  ```csharp
      [SkippableFact]
      public async Task QueryAllAsync_returns_all_statement_results()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              IReadOnlyList<QueryResult> results = await conn.QueryAllAsync(
                  "RETURN 1 AS a; RETURN 2 AS b;");
              try
              {
                  Assert.Equal(2, results.Count);
                  Assert.True(results[0].IsSuccess);
                  Assert.True(results[1].IsSuccess);
              }
              finally
              {
                  foreach (QueryResult r in results)
                  {
                      r.Dispose();
                  }
              }
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
- [ ] **Run / expect FAIL** (compile: `QueryAllAsync` not found):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~QueryAllAsync_returns_all_statement_results"
  ```
  Expected: compile failure.
- [ ] **Minimal implementation** — add to `Connection.Async.cs`:
  ```csharp
      /// <summary>Executes a multi-statement Cypher query asynchronously, returning every result set.</summary>
      public Task<IReadOnlyList<QueryResult>> QueryAllAsync(string cypher, CancellationToken ct = default)
      {
          if (cypher is null)
          {
              throw new ArgumentNullException(nameof(cypher));
          }

          return RunWithCancellation<IReadOnlyList<QueryResult>>(() => QueryAll(cypher), ct);
      }
  ```
- [ ] **Run / expect PASS** (skip/pass):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~QueryAllAsync_returns_all_statement_results"
  ```
  Expected: PASS or SKIP.
- [ ] **Commit:**
  ```
  git add src/LadybugDB/Connection.Async.cs test/LadybugDB.Tests/AsyncTests.cs
  git commit -m "feat(async): add QueryAllAsync over the multi-statement chain

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
  ```

---

## Task 5 — Non-gated cancellation-helper test (pure managed)

This is the always-runs managed test the WS requires: verify `RunWithCancellation` honors a pre-cancelled token **without** any native interaction, using `InternalsVisibleTo` to call a tiny internal test hook. `InternalsVisibleTo("LadybugDB.Tests")` already exists (`LadybugDB.csproj:16`).

- [ ] **Failing test** — append to `AsyncTests.cs`:
  ```csharp
      [Fact]
      public async Task RunWithCancellation_precancelled_token_does_not_invoke_work()
      {
          using var cts = new CancellationTokenSource();
          cts.Cancel();

          bool ran = false;
          await Assert.ThrowsAnyAsync<OperationCanceledException>(
              () => Connection.RunWithCancellationForTests(() => { ran = true; return 0; }, cts.Token));

          Assert.False(ran, "work must not run when the token is already cancelled");
      }
  ```
- [ ] **Run / expect FAIL** (compile: `RunWithCancellationForTests` not found):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~RunWithCancellation_precancelled_token_does_not_invoke_work"
  ```
  Expected: compile failure.
- [ ] **Minimal implementation** — add a static, native-free internal hook to `Connection.Async.cs` (it does **not** register `Interrupt`, since there is no instance/handle; it only exercises the pre/post token checks and offload). Add inside `partial class Connection`:
  ```csharp
      // Native-free test hook for the cancellation contract: honors ct before and after the offload
      // with no native interaction, so the cancellation semantics can be tested without the engine.
      internal static async Task<T> RunWithCancellationForTests<T>(Func<T> work, CancellationToken ct)
      {
          ct.ThrowIfCancellationRequested();
          T result = await Task.Run(work, ct).ConfigureAwait(false);
          ct.ThrowIfCancellationRequested();
          return result;
      }
  ```
- [ ] **Run / expect PASS** (always runs — no native gate):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~RunWithCancellation_precancelled_token_does_not_invoke_work"
  ```
  Expected: PASS (even with no native library).
- [ ] **Commit:**
  ```
  git add src/LadybugDB/Connection.Async.cs test/LadybugDB.Tests/AsyncTests.cs
  git commit -m "test(async): non-gated cancellation-helper test via internal hook

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
  ```

---

## Task 6 — `StreamAsync` (`IAsyncEnumerable<FlatTuple>`, gated)

`StreamAsync` runs the query (offloaded, cancellable) then yields rows. Because `FlatTuple` shares the engine's reused buffer (`FlatTuple.cs:8`), each tuple must be consumed before the next `MoveNextAsync`. The native iteration is synchronous; we offload each `HasNext`/`GetNext` step under the `_gate` via a small private helper, and honor `ct` between rows. The `[EnumeratorCancellation]` attribute flows the `await foreach`'s token into `ct`.

- [ ] **Failing test** — append to `AsyncTests.cs`:
  ```csharp
      [SkippableFact]
      public async Task StreamAsync_yields_all_rows()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              (await conn.QueryAsync("CREATE NODE TABLE Person(name STRING, PRIMARY KEY(name))")).Dispose();
              (await conn.QueryAsync("CREATE (:Person {name: 'Alice'})")).Dispose();
              (await conn.QueryAsync("CREATE (:Person {name: 'Bob'})")).Dispose();

              var names = new List<string?>();
              await foreach (FlatTuple tuple in conn.StreamAsync("MATCH (p:Person) RETURN p.name ORDER BY p.name"))
              {
                  using (tuple)
                  using (Value value = tuple.GetValue(0UL))
                  {
                      names.Add(value.GetValue() as string);
                  }
              }

              Assert.Equal(new[] { "Alice", "Bob" }, names);
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
- [ ] **Run / expect FAIL** (compile: `StreamAsync` not found):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~StreamAsync_yields_all_rows"
  ```
  Expected: compile failure.
- [ ] **Minimal implementation** — add to `Connection.Async.cs`. Note the `using System.Runtime.CompilerServices;` (already added in Task 1) supplies `[EnumeratorCancellation]`:
  ```csharp
      /// <summary>
      /// Streams the rows of a Cypher query as an async sequence. Each yielded <see cref="FlatTuple"/>
      /// shares the engine's reused buffer, so consume (and dispose) it before requesting the next.
      /// Cancellation interrupts the underlying query.
      /// </summary>
      public async IAsyncEnumerable<FlatTuple> StreamAsync(
          string cypher,
          [EnumeratorCancellation] CancellationToken ct = default)
      {
          if (cypher is null)
          {
              throw new ArgumentNullException(nameof(cypher));
          }

          QueryResult result = await QueryAsync(cypher, ct).ConfigureAwait(false);
          try
          {
              while (true)
              {
                  ct.ThrowIfCancellationRequested();
                  bool hasNext = await Task.Run(() => result.HasNext(), ct).ConfigureAwait(false);
                  if (!hasNext)
                  {
                      yield break;
                  }

                  yield return result.GetNext();
              }
          }
          finally
          {
              result.Dispose();
          }
      }
  ```
  - Rationale: `QueryAsync` already offloads + honors cancellation + registers `InterruptCore`. The per-row `HasNext` is offloaded so the enumerator does not block the caller's thread; `GetNext` returns the shared buffer the test consumes immediately. `ThrowIfCancellationRequested` honors `ct` between rows; the `finally` disposes the result on completion, break, or cancellation.
- [ ] **Run / expect PASS** (skip/pass):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~StreamAsync_yields_all_rows"
  ```
  Expected: PASS or SKIP.
- [ ] **Commit:**
  ```
  git add src/LadybugDB/Connection.Async.cs test/LadybugDB.Tests/AsyncTests.cs
  git commit -m "feat(async): add StreamAsync (IAsyncEnumerable<FlatTuple>)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
  ```

---

## Task 7 — Cancellation round-trip test: slow query → cancel → `OperationCanceledException` (gated)

This is the WS's required cancellation test. Start a slow query on a background task, cancel mid-flight, assert `OperationCanceledException`. Build a query slow enough to interrupt reliably (a Cartesian self-join over a generated range).

- [ ] **Failing test** — append to `AsyncTests.cs`:
  ```csharp
      [SkippableFact]
      public async Task QueryAsync_cancellation_interrupts_running_query()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              // Build a deliberately expensive query: a large range cross-joined with itself.
              const string slow =
                  "UNWIND range(1, 4000000) AS a UNWIND range(1, 4000000) AS b RETURN count(*)";

              using var cts = new CancellationTokenSource();
              Task<QueryResult> running = conn.QueryAsync(slow, cts.Token);

              // Give the engine a moment to start, then interrupt.
              cts.CancelAfter(TimeSpan.FromMilliseconds(200));

              await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
              {
                  using QueryResult r = await running;
              });

              // The connection must remain usable after an interrupt.
              using QueryResult ok = await conn.QueryAsync("RETURN 1 AS x");
              Assert.True(ok.IsSuccess);
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
  - Notes: `ThrowsAnyAsync<OperationCanceledException>` accepts both `OperationCanceledException` (raised by `Task.Run`'s token / the post-check) and `TaskCanceledException` (subclass). The normalization in `RunWithCancellation` converts an interrupt-induced `LadybugQueryException` into `OperationCanceledException`, so all interrupt paths satisfy the assert. The follow-up `RETURN 1` proves the connection survives the interrupt.
- [ ] **Run / expect FAIL first if the query is too fast** — if the engine finishes before the cancel lands, the test would see a successful result instead of cancellation. Run it; if it is flaky/too fast, increase the `range(...)` bounds until the cancel reliably wins, then keep that value:
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~QueryAsync_cancellation_interrupts_running_query"
  ```
  Expected (before any tuning): may PASS immediately (implementation from Tasks 1/6 already wires cancellation). If it fails because the query completed first, enlarge the range bounds and re-run.
- [ ] **Implementation:** none beyond Tasks 1/3 — cancellation is already wired via `RunWithCancellation` + `InterruptCore`. This task only adds and tunes the test. (If the assert never sees cancellation even with a huge range, that indicates `Native.ConnectionInterrupt` is not effective for this query shape — record as an open question and try a different slow query, e.g. a long `MATCH` over a created dataset.)
- [ ] **Run / expect PASS** (skip/pass):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~QueryAsync_cancellation_interrupts_running_query"
  ```
  Expected: PASS or SKIP.
- [ ] **Commit:**
  ```
  git add test/LadybugDB.Tests/AsyncTests.cs
  git commit -m "test(async): cancellation interrupts a running query

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
  ```

---

## Task 8 — Full WS verification (both TFMs + full test run)

- [ ] Build the whole solution, both TFMs:
  ```
  dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug
  ```
  Expected: net10.0 + netstandard2.0 build green. (If ns2.0 fails only on `IAsyncEnumerable`/`[EnumeratorCancellation]`, confirm WS-K's `Microsoft.Bcl.AsyncInterfaces` ref is present per Task 0; otherwise flag at merge.)
- [ ] Run the full test project (managed tests run; native-gated tests skip without the lib):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj
  ```
  Expected: all `AsyncTests` pass or skip; the non-gated `RunWithCancellation_precancelled_token_does_not_invoke_work` and `QueryResult_async_partial_file_compiles` pass.
- [ ] If a native library is available, enforce the gate to prove round-trip + cancellation really run:
  ```
  $env:LADYBUG_REQUIRE_NATIVE='1'; dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj; Remove-Item Env:\LADYBUG_REQUIRE_NATIVE
  ```
  Expected: `NativeLibrary_LoadsWhenRequired` passes and the gated async tests execute (not skip).
- [ ] **Commit (if any final touch-ups were needed):**
  ```
  git add -A
  git commit -m "chore(async): WS-D verification pass (both TFMs green)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
  ```

---

## Self-review / done criteria (tied to WS-D Done column: "§4.3 contract; tests")

- [ ] `src/LadybugDB/Connection.Async.cs` and `src/LadybugDB/QueryResult.Async.cs` exist as **new `partial class` files**; `Connection.cs` and `QueryResult.cs` were **never edited** by this WS (verify with `git diff --name-only` — only the two new partials + the test + commits show up).
- [ ] Public surface matches §4.3 **exactly**:
  - `public Task<QueryResult> QueryAsync(string cypher, CancellationToken ct = default)`
  - `public Task<IReadOnlyList<QueryResult>> QueryAllAsync(string cypher, CancellationToken ct = default)`
  - `public Task<PreparedStatement> PrepareAsync(string cypher, CancellationToken ct = default)`
  - `public Task<QueryResult> ExecuteAsync(PreparedStatement stmt, CancellationToken ct = default)`
  - `public IAsyncEnumerable<FlatTuple> StreamAsync(string cypher, [EnumeratorCancellation] CancellationToken ct = default)`
- [ ] Offload is honest: every method runs the **sync** engine call under `Task.Run`, which takes the existing `lock (_gate)` inside `Query`/`Prepare`/`Execute`/`QueryAll` — the connection stays serialized. No `Task.FromResult` fakes.
- [ ] `CancellationToken` is wired to interrupt: `ct.Register(... InterruptCore())` for the native call; `ct` honored **before** offload (`ThrowIfCancellationRequested`) and **after** (token check / normalization to `OperationCanceledException`).
- [ ] Tests present: a native-gated **cancellation** test (slow query → cancel → `OperationCanceledException`, connection still usable), a native-gated **round-trip** test (prepare/execute/query/stream/queryAll), and a **non-gated managed** cancellation-helper test that runs without the engine.
- [ ] `dotnet build LadybugDB.slnx -c Debug` green on **both** `net10.0` and `netstandard2.0` (ns2.0 requires WS-K's `Microsoft.Bcl.AsyncInterfaces`).
- [ ] `dotnet test` green: managed tests pass; gated tests pass-or-skip; under `LADYBUG_REQUIRE_NATIVE=1` (native present) the gated async tests execute.
- [ ] Merge-gate reconciliation noted: WS-B's public `Interrupt()` delegates to `InterruptCore()`; `QueryAll`/`Interrupt` are consumed (not defined) by this WS.
