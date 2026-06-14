# WS-J: Test + Parity Harness Implementation Plan
> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]` checkboxes.

**Goal:** Stand up a native-gated xUnit parity test project that ports the meaningful upstream C-API gtest cases, a BenchmarkDotNet harness with a unit-testable `--ci-gate` latency regression mode, and a differential smoke runner — plus a `parity-tracking.md` that maps every upstream `test/c_api/*.cpp` file to its ported status and pins the upstream tag (0.17.2).

**Architecture:** Three new projects (`test/LadybugDB.Tests.Parity`, `benchmarks/LadybugDB.Benchmarks`, and a tiny `benchmarks/LadybugDB.Benchmarks.Tests`) referencing the existing `src/LadybugDB`. Parity tests rebuild the upstream `tinysnb`-style person graph in-process with Cypher `CREATE`s (the engine test harness/CSV dataset is unavailable to a managed consumer), reuse the existing `TestEnvironment.NativeAvailable` skip gate, and assert the same observable behavior the gtests assert through the new public surface landed by WS-A–F. The `--ci-gate` ratio math lives in a pure, native-free class so it has a non-gated unit test; BenchmarkDotNet itself only runs under an opt-in env flag so `dotnet test` never invokes it.

**Tech Stack:** .NET (`net10.0` for test/bench projects — they are not packable, so they need not multi-target; the parity project mirrors the existing `LadybugDB.Tests.csproj` exactly), xUnit 2.9.2 + `Xunit.SkippableFact` 1.5.61, BenchmarkDotNet, Cake Frosting (slnx + bench target wiring coordinated with WS-K).

---

## Files

**Create**
- `test/LadybugDB.Tests.Parity/LadybugDB.Tests.Parity.csproj` — parity test project (mirror of `LadybugDB.Tests.csproj`).
- `test/LadybugDB.Tests.Parity/ParityEnvironment.cs` — native gate + temp-db helpers + the in-process `tinysnb`-style fixture builder.
- `test/LadybugDB.Tests.Parity/VersionParityTests.cs` — port of `version_test.cpp`.
- `test/LadybugDB.Tests.Parity/DatabaseParityTests.cs` — port of `database_test.cpp`.
- `test/LadybugDB.Tests.Parity/ConnectionParityTests.cs` — port of `connection_test.cpp`.
- `test/LadybugDB.Tests.Parity/QueryResultParityTests.cs` — port of `query_result_test.cpp`.
- `test/LadybugDB.Tests.Parity/DataTypeParityTests.cs` — port of `data_type_test.cpp`.
- `test/LadybugDB.Tests.Parity/FlatTupleParityTests.cs` — port of `flat_tuple_test.cpp`.
- `test/LadybugDB.Tests.Parity/PreparedStatementParityTests.cs` — port of `prepared_statement_test.cpp` binds.
- `test/LadybugDB.Tests.Parity/ValueParityTests.cs` — port of `value_test.cpp` read/accessor cases.
- `test/LadybugDB.Tests.Parity/DifferentialSmokeTests.cs` — wraps the differential smoke runner as native-gated facts.
- `benchmarks/LadybugDB.Benchmarks/LadybugDB.Benchmarks.csproj` — BenchmarkDotNet console.
- `benchmarks/LadybugDB.Benchmarks/CiGate.cs` — pure latency-ratio gate logic (no native, no BDN).
- `benchmarks/LadybugDB.Benchmarks/QueryBenchmarks.cs` — BDN benchmark definitions.
- `benchmarks/LadybugDB.Benchmarks/DifferentialRunner.cs` — surface-comparison smoke runner (shared by bench + parity tests).
- `benchmarks/LadybugDB.Benchmarks/Program.cs` — entry point dispatching `--ci-gate` / `--diff` / BDN.
- `benchmarks/LadybugDB.Benchmarks.Tests/LadybugDB.Benchmarks.Tests.csproj` — non-gated unit tests for `CiGate`.
- `benchmarks/LadybugDB.Benchmarks.Tests/CiGateTests.cs` — pure unit tests (no native).
- `docs/parity-2026-06/parity-tracking.md` — upstream-file → ported-status map, pins tag 0.17.2.

**Modify**
- `LadybugDB.slnx` — add the three new projects under `/test/` and a new `/benchmarks/` folder (coordinate with WS-K, which also edits this file).

**Test**
- All `*ParityTests.cs` are `[SkippableFact]`/`[SkippableTheory]` gated on `ParityEnvironment.NativeAvailable`.
- `CiGateTests.cs` is plain `[Fact]` (pure managed logic, never gated, never touches native).

---

## Conventions this plan follows (verified against the repo)

- Native gate pattern: copied from `test/LadybugDB.Tests/TestEnvironment.cs` and `SmokeTests.cs` — `Skip.IfNot(... NativeAvailable, "Native Ladybug library is not available.")` at the top of every native-dependent fact. Probe via `LadybugVersion.StorageVersion` catching `DllNotFoundException`/`TypeInitializationException`/`EntryPointNotFoundException`.
- Test projects are `net10.0` only, `<IsPackable>false</IsPackable>`, `<IsTestProject>true</IsTestProject>`, and carry the `PlaceNativeLibrary` AfterTargets="Build" target verbatim so the staged native lib lands next to the test output (see `test/LadybugDB.Tests/LadybugDB.Tests.csproj:23-29`).
- Build: `dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug`. Test a project: `dotnet test <path-to-csproj> -c Debug`.
- The upstream gtests load the `dataset/tinysnb` CSV dataset via the engine's `BaseGraphTest`/`CApiTest` harness. That harness and its CSVs are **not** reachable from a managed NuGet consumer, so each ported test rebuilds the slice of the `person`/`knows` schema it needs with Cypher `CREATE` statements in a fresh temp DB. Expected values are adjusted to the in-process fixture (documented per test). This is the same self-contained approach `SmokeTests`/`TypeMappingTests` already use.
- Commit messages end with the repo's required co-author trailer.

## Dependency note (read before starting)

WS-J is Phase 3: it consumes the **merged** public surface from WS-A–F. Specifically the ported tests call: `Connection.QueryAll`, `Connection.Interrupt`, `Connection.SetQueryTimeout`, `Connection.SetMaxThreadsForExec`/`GetMaxThreadsForExec` (WS-B §4.1); `QueryResult.Summary`, `.Columns`, `.GetColumnType`, `.ResetIterator`, `.HasNextQueryResult`, `.GetNextQueryResult`, `QuerySummary`, `ColumnSchema`, `LogicalType` (WS-B §4.1); `Value.GetDecimal`/`LadybugDecimal`, the new `Bind` overloads (WS-C §4.2). If a member is not yet merged when you reach its task, **write the failing test anyway** (it will fail to compile, which is the expected red state) and leave the task checkbox unchecked with a one-line note `BLOCKED: needs WS-<x> <member>` rather than deleting the test — do not invent a different signature than the §4 contract.

---

## TASK 1 — Parity test project skeleton + native gate/fixture helper

- [ ] Create `test/LadybugDB.Tests.Parity/LadybugDB.Tests.Parity.csproj` mirroring the existing test project:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
      <IsPackable>false</IsPackable>
      <IsTestProject>true</IsTestProject>
    </PropertyGroup>

    <ItemGroup>
      <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
      <PackageReference Include="xunit" Version="2.9.2" />
      <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
        <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
        <PrivateAssets>all</PrivateAssets>
      </PackageReference>
      <PackageReference Include="Xunit.SkippableFact" Version="1.5.61" />
    </ItemGroup>

    <ItemGroup>
      <ProjectReference Include="..\..\src\LadybugDB\LadybugDB.csproj" />
    </ItemGroup>

    <!-- Copy the native library next to the test output if it has been placed under lib/runtimes/<rid>/native. -->
    <Target Name="PlaceNativeLibrary" AfterTargets="Build">
      <ItemGroup>
        <_LadybugNative Include="$(NativeLibDir)runtimes\$(TargetRid)\native\*.*" />
      </ItemGroup>
      <Copy SourceFiles="@(_LadybugNative)" DestinationFolder="$(OutputPath)" SkipUnchangedFiles="true" Condition="'@(_LadybugNative)' != ''" />
    </Target>

  </Project>
  ```
- [ ] Create `test/LadybugDB.Tests.Parity/ParityEnvironment.cs` — native gate + temp-db helpers + the in-process fixture builder. This is the failing-test target's support code; write the helper first because every subsequent task uses it. Real code:
  ```csharp
  using System;
  using System.IO;
  using LadybugDB;

  namespace LadybugDB.Tests.Parity;

  /// <summary>
  /// Native-availability gate and shared fixtures for the upstream C-API parity ports. Mirrors
  /// <c>LadybugDB.Tests.TestEnvironment</c>; the parity ports cannot reach the engine's gtest harness
  /// or its <c>dataset/tinysnb</c> CSVs, so <see cref="CreatePersonGraph"/> rebuilds the slice of the
  /// upstream schema each port needs in a fresh temp database.
  /// </summary>
  internal static class ParityEnvironment
  {
      public static readonly bool NativeAvailable = Probe();

      public static string NewTempDbPath()
          => Path.Combine(Path.GetTempPath(), "ladybug-parity-" + Guid.NewGuid().ToString("N"));

      public static void TryDelete(string path)
      {
          try
          {
              if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
              else if (File.Exists(path)) File.Delete(path);
          }
          catch { /* best-effort */ }
      }

      /// <summary>
      /// Opens a fresh database and loads a small person graph matching the columns the upstream
      /// tinysnb ports read: fName (STRING), age (INT64), height (FLOAT), isStudent (BOOL),
      /// registerTime (TIMESTAMP), birthdate (DATE), plus a single knows edge. Eight people are
      /// inserted so num-tuples assertions match the upstream "MATCH (a:person)" counts.
      /// Caller owns the returned database and connection.
      /// </summary>
      public static (Database Db, Connection Conn) CreatePersonGraph(out string dbPath)
      {
          dbPath = NewTempDbPath();
          var db = new Database(dbPath);
          var conn = new Connection(db);

          conn.Query(
              "CREATE NODE TABLE person(" +
              "fName STRING, age INT64, height FLOAT, isStudent BOOL, " +
              "registerTime TIMESTAMP, birthdate DATE, PRIMARY KEY(fName))").Dispose();
          conn.Query("CREATE REL TABLE knows(FROM person TO person, since INT64)").Dispose();

          // (name, age, height, isStudent) — three students so the isStudent=true count is 3,
          // matching the upstream "WHERE a.isStudent = true RETURN COUNT(*)" == 3.
          (string Name, long Age, float Height, bool IsStudent)[] people =
          {
              ("Alice", 35, 1.731f, true),
              ("Bob", 30, 1.7f, true),
              ("Carol", 45, 1.6f, false),
              ("Dan", 20, 1.5f, true),
              ("Elizabeth", 20, 1.75f, false),
              ("Farooq", 25, 1.8f, false),
              ("Greg", 40, 1.9f, false),
              ("Hubert", 83, 1.6f, false),
          };
          foreach ((string name, long age, float height, bool isStudent) in people)
          {
              using PreparedStatement insert = conn.Prepare(
                  "CREATE (:person {fName: $n, age: $a, height: $h, isStudent: $s, " +
                  "registerTime: timestamp('2020-01-15 10:30:00'), birthdate: date('1985-06-01')})");
              insert.Bind("n", name).Bind("a", age).Bind("h", height).Bind("s", isStudent);
              insert.Execute().Dispose();
          }

          conn.Query("MATCH (a:person {fName:'Alice'}), (b:person {fName:'Bob'}) " +
                     "CREATE (a)-[:knows {since: 2011}]->(b)").Dispose();

          return (db, conn);
      }

      private static bool Probe()
      {
          try { _ = LadybugVersion.StorageVersion; return true; }
          catch (DllNotFoundException) { return false; }
          catch (TypeInitializationException) { return false; }
          catch (EntryPointNotFoundException) { return false; }
      }
  }
  ```
- [ ] Add the project to the solution and confirm it builds (no tests yet). Run:
  `dotnet sln W:\code\ladybug\tools\csharp_api\LadybugDB.slnx add W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\LadybugDB.Tests.Parity.csproj`
  then `dotnet build W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\LadybugDB.Tests.Parity.csproj -c Debug` — **expected PASS** (skeleton compiles; `CreatePersonGraph` uses only existing `Database`/`Connection`/`PreparedStatement` API).
- [ ] Commit: `test(parity): scaffold native-gated parity project + in-process person-graph fixture`

> Note on slnx: `dotnet sln add` on a `.slnx` writes a `<Project Path="...">` under a guessed folder. Verify the entry landed and, if it is not under a `/test/` `<Folder>`, fix it by hand to match the existing layout (`LadybugDB.slnx:5-7`). WS-K also edits this file; if a merge conflict arises, keep both WS-K's and WS-J's `<Project>` entries.

---

## TASK 2 — Port `version_test.cpp` (smallest, proves the harness)

Upstream asserts `lbug_get_version() == LBUG_CMAKE_VERSION` (non-empty), and storage version matches the on-disk `LBUG` magic header. The managed surface exposes only `LadybugVersion.Version`/`.StorageVersion`, so port the observable invariants: version non-empty, storage version > 0, and stable across calls.

- [ ] Write the FAILING test `test/LadybugDB.Tests.Parity/VersionParityTests.cs`:
  ```csharp
  using LadybugDB;
  using Xunit;

  namespace LadybugDB.Tests.Parity;

  /// <summary>Port of upstream <c>test/c_api/version_test.cpp</c> (GetVersion, GetStorageVersion).</summary>
  public sealed class VersionParityTests
  {
      [SkippableFact]
      public void GetVersion_is_nonempty_and_stable()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string v1 = LadybugVersion.Version;
          string v2 = LadybugVersion.Version;
          Assert.False(string.IsNullOrWhiteSpace(v1));
          Assert.Equal(v1, v2);
      }

      [SkippableFact]
      public void GetStorageVersion_is_positive()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          Assert.True(LadybugVersion.StorageVersion > 0);
      }
  }
  ```
- [ ] Run / expected PASS-or-SKIP (no native lib staged locally → SKIP; with native staged → PASS):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\LadybugDB.Tests.Parity.csproj -c Debug --filter FullyQualifiedName~VersionParityTests`
  Expected output: `Passed!  - Failed: 0` with these 2 tests passed or skipped. (No native gate failure, no compile error.)
- [ ] These ports need no new implementation — they exercise existing `LadybugVersion`. The "red" here is purely "test does not yet exist"; once added and green/skipped, move on.
- [ ] Commit: `test(parity): port version_test.cpp (version + storage version)`

---

## TASK 3 — Port `database_test.cpp` (open/read-only/in-memory/use-after-destroy)

Portable upstream cases: CreationAndDestroy, CreationReadOnly, CreationInMemory, UseConnectionAfterDatabaseDestroy. Skip CreationHomeDir (`~` expansion, env-specific), CreationWithEnableMultiWrites (no managed `isMultiWritesEnabled` accessor), and the CApi-pointer-level CloseQueryResultAfterDestroy (managed disposal is idempotent and covered by the use-after-destroy case).

- [ ] Confirm the managed `SystemConfig` read-only knob and in-memory path exist:
  `dotnet build W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj -c Debug` then inspect `src/LadybugDB/SystemConfig.cs` and `src/LadybugDB/Database.cs` for a `ReadOnly` property and an empty/`:memory:` path constructor overload. (Database accepts a path string; an in-memory DB opens with `""` or `":memory:"` per `database_test.cpp:82-93`.)
- [ ] Write the FAILING test `test/LadybugDB.Tests.Parity/DatabaseParityTests.cs`:
  ```csharp
  using LadybugDB;
  using Xunit;

  namespace LadybugDB.Tests.Parity;

  /// <summary>Port of upstream <c>test/c_api/database_test.cpp</c>.</summary>
  public sealed class DatabaseParityTests
  {
      [SkippableFact] // upstream CreationAndDestroy
      public void Open_and_dispose_roundtrip()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          string path = ParityEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(path);
              using var conn = new Connection(db);
              using QueryResult r = conn.Query("RETURN 1");
              Assert.True(r.IsSuccess);
          }
          finally { ParityEnvironment.TryDelete(path); }
      }

      [SkippableFact] // upstream CreationInMemory
      public void In_memory_database_opens()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);
          using QueryResult r = conn.Query("RETURN 1 + 1");
          Assert.Equal(2L, r.Rows().Single()[0]);
      }

      [SkippableFact] // upstream CreationReadOnly: writes must fail against a read-only open
      public void Read_only_database_rejects_writes()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          string path = ParityEnvironment.NewTempDbPath();
          try
          {
              using (var rw = new Database(path))
              using (var conn = new Connection(rw))
              {
                  conn.Query("CREATE NODE TABLE T(id INT64, PRIMARY KEY(id))").Dispose();
              }

              using var ro = new Database(path, new SystemConfig { ReadOnly = true });
              using var roConn = new Connection(ro);
              Assert.Throws<LadybugQueryException>(
                  () => roConn.Query("CREATE NODE TABLE U(id INT64, PRIMARY KEY(id))"));
          }
          finally { ParityEnvironment.TryDelete(path); }
      }

      [SkippableFact] // upstream UseConnectionAfterDatabaseDestroy
      public void Query_after_database_dispose_fails_without_crashing()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          string path = ParityEnvironment.NewTempDbPath();
          try
          {
              var db = new Database(path);
              var conn = new Connection(db);
              db.Dispose();
              Assert.ThrowsAny<System.Exception>(() => conn.Query("RETURN 0"));
              conn.Dispose();
          }
          finally { ParityEnvironment.TryDelete(path); }
      }
  }
  ```
  Add `using System.Linq;` at the top.
- [ ] Run / expected outcome:
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\LadybugDB.Tests.Parity.csproj -c Debug --filter FullyQualifiedName~DatabaseParityTests`
  If `SystemConfig.ReadOnly` or `new Database(path, config)` does not exist yet → **expected FAIL (compile error)**; mark the read-only test `BLOCKED: needs SystemConfig.ReadOnly + Database(path, SystemConfig)` and proceed once present. Otherwise PASS/SKIP.
- [ ] If `SystemConfig`/`Database` already expose those (verify via the read in the first step), no new implementation is needed — green/skip. If the ctor overload genuinely does not exist, that is a core gap to flag, not to implement here (this is a test workstream); record it in **openQuestions** of the structured output and leave the single test blocked.
- [ ] Commit: `test(parity): port database_test.cpp (open, in-memory, read-only, use-after-destroy)`

---

## TASK 4 — Port `connection_test.cpp` (threads/timeout/interrupt/prepare/execute)

Portable upstream cases mapped to the WS-B §4.1 surface: Query (count/columns), SetGetMaxNumThreadForExec → `SetMaxThreadsForExec`/`GetMaxThreadsForExec`, Prepare + Execute (count==1, scalar==3 for the student count), ExecuteError, QueryTimeout → `SetQueryTimeout`, Interrupt → `Interrupt` from a background thread.

- [ ] Write the FAILING test `test/LadybugDB.Tests.Parity/ConnectionParityTests.cs`:
  ```csharp
  using System;
  using System.Linq;
  using System.Threading;
  using System.Threading.Tasks;
  using LadybugDB;
  using Xunit;

  namespace LadybugDB.Tests.Parity;

  /// <summary>Port of upstream <c>test/c_api/connection_test.cpp</c>.</summary>
  public sealed class ConnectionParityTests
  {
      [SkippableFact] // upstream Query
      public void Query_returns_expected_columns_and_count()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
          try
          {
              using QueryResult r = conn.Query("MATCH (a:person) RETURN a.fName");
              Assert.True(r.IsSuccess);
              Assert.Equal(1UL, r.ColumnCount);
              Assert.Equal("a.fName", r.ColumnNames.Single());
              Assert.Equal(8UL, r.RowCount);
          }
          finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
      }

      [SkippableFact] // upstream SetGetMaxNumThreadForExec
      public void Set_and_get_max_threads_for_exec()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
          try
          {
              conn.SetMaxThreadsForExec(4);
              Assert.Equal(4UL, conn.GetMaxThreadsForExec());
              conn.SetMaxThreadsForExec(8);
              Assert.Equal(8UL, conn.GetMaxThreadsForExec());
          }
          finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
      }

      [SkippableFact] // upstream Prepare + Execute: isStudent students count == 3
      public void Prepare_bind_execute_counts_students()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
          try
          {
              using PreparedStatement stmt =
                  conn.Prepare("MATCH (a:person) WHERE a.isStudent = $s RETURN COUNT(*)");
              stmt.Bind("s", true);
              using QueryResult r = conn.Execute(stmt);
              Assert.True(r.IsSuccess);
              Assert.Equal(1UL, r.RowCount);
              Assert.Equal(3L, r.Rows().Single()[0]);
          }
          finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
      }

      [SkippableFact] // upstream ExecuteError: binding wrong type / failing execute throws
      public void Execute_with_type_mismatch_throws()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
          try
          {
              using PreparedStatement stmt =
                  conn.Prepare("MATCH (a:person) WHERE a.isStudent = $s RETURN COUNT(*)");
              stmt.Bind("s", 30L); // isStudent is BOOL; INT64 bind must fail at execute
              Assert.Throws<LadybugQueryException>(() => conn.Execute(stmt));
          }
          finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
      }

      [SkippableFact] // upstream QueryTimeout
      public void Query_timeout_interrupts_long_query()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);
          conn.SetQueryTimeout(TimeSpan.FromMilliseconds(1));
          LadybugQueryException ex = Assert.Throws<LadybugQueryException>(() => conn.Query(
              "UNWIND RANGE(1,100000) AS x UNWIND RANGE(1,100000) AS y RETURN COUNT(x + y)"));
          Assert.Contains("Interrupted", ex.Message, StringComparison.OrdinalIgnoreCase);
      }

      [SkippableFact] // upstream Interrupt
      public void Interrupt_from_another_thread_cancels_query()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);
          var finished = new ManualResetEventSlim(false);
          var interrupter = Task.Run(() =>
          {
              while (!finished.IsSet)
              {
                  Thread.Sleep(50);
                  try { conn.Interrupt(); } catch { /* race with dispose at end */ }
              }
          });
          try
          {
              LadybugQueryException ex = Assert.Throws<LadybugQueryException>(() => conn.Query(
                  "UNWIND RANGE(1,100000) AS x UNWIND RANGE(1,100000) AS y RETURN COUNT(x + y)"));
              Assert.Contains("Interrupted", ex.Message, StringComparison.OrdinalIgnoreCase);
          }
          finally { finished.Set(); interrupter.Wait(); }
      }
  }
  ```
- [ ] Run / expected FAIL until WS-B lands `SetMaxThreadsForExec`/`GetMaxThreadsForExec`/`SetQueryTimeout`/`Interrupt`:
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\LadybugDB.Tests.Parity.csproj -c Debug --filter FullyQualifiedName~ConnectionParityTests`
  Expected red state: compile error `'Connection' does not contain a definition for 'SetMaxThreadsForExec'` if WS-B not yet merged. Once merged → PASS/SKIP.
- [ ] No implementation in this workstream (members are owned by WS-B). When merged, re-run; tests must pass with native staged.
- [ ] Commit: `test(parity): port connection_test.cpp (threads, timeout, interrupt, prepare/execute)`

---

## TASK 5 — Port `query_result_test.cpp` (columns, type, summary, iterator, multi-result)

Portable upstream cases mapped to WS-B §4.1: GetNumColumns, GetColumnName (incl. out-of-range error), GetColumnDataType → `GetColumnType`/`Columns`, GetQuerySummary → `Summary`, GetNext/HasNext, ResetIterator → `ResetIterator`, MultipleQuery → `QueryAll` + `HasNextQueryResult`/`GetNextQueryResult`.

- [ ] Write the FAILING test `test/LadybugDB.Tests.Parity/QueryResultParityTests.cs`:
  ```csharp
  using System;
  using System.Linq;
  using LadybugDB;
  using Xunit;

  namespace LadybugDB.Tests.Parity;

  /// <summary>Port of upstream <c>test/c_api/query_result_test.cpp</c>.</summary>
  public sealed class QueryResultParityTests
  {
      [SkippableFact] // upstream GetNumColumns + GetColumnName
      public void Columns_count_and_names()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
          try
          {
              using QueryResult r = conn.Query("MATCH (a:person) RETURN a.fName, a.age, a.height");
              Assert.Equal(3UL, r.ColumnCount);
              Assert.Equal(new[] { "a.fName", "a.age", "a.height" }, r.ColumnNames.ToArray());
              Assert.Throws<LadybugException>(() => r.GetColumnName(222));
          }
          finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
      }

      [SkippableFact] // upstream GetColumnDataType
      public void Column_logical_types_match()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
          try
          {
              using QueryResult r = conn.Query("MATCH (a:person) RETURN a.fName, a.age, a.height");
              Assert.Equal(DataTypeId.String, r.GetColumnType(0).Id);
              Assert.Equal(DataTypeId.Int64, r.GetColumnType(1).Id);
              Assert.Equal(DataTypeId.Float, r.GetColumnType(2).Id);
              Assert.Equal(DataTypeId.String, r.Columns[0].Type.Id);
              Assert.Equal("a.fName", r.Columns[0].Name);
          }
          finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
      }

      [SkippableFact] // upstream GetQuerySummary
      public void Query_summary_reports_positive_timings()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
          try
          {
              using QueryResult r = conn.Query("MATCH (a:person) RETURN a.fName, a.age, a.height");
              QuerySummary s = r.Summary;
              Assert.True(s.CompilingTimeMs > 0);
              Assert.True(s.ExecutionTimeMs > 0);
          }
          finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
      }

      [SkippableFact] // upstream GetNext + HasNext: ordered read of first two rows
      public void HasNext_getNext_reads_ordered_rows()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
          try
          {
              using QueryResult r = conn.Query("MATCH (a:person) RETURN a.fName, a.age ORDER BY a.fName");
              Assert.True(r.HasNext());
              object?[] first = r.Rows().First();
              Assert.Equal("Alice", first[0]);
              Assert.Equal(35L, first[1]);
          }
          finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
      }

      [SkippableFact] // upstream ResetIterator
      public void Reset_iterator_replays_from_start()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
          try
          {
              using QueryResult r = conn.Query("MATCH (a:person) RETURN a.fName ORDER BY a.fName");
              using (FlatTuple t = r.GetNext()) { Assert.Equal("Alice", t.GetValue(0).GetValue()); }
              r.ResetIterator();
              Assert.True(r.HasNext());
              using (FlatTuple t = r.GetNext()) { Assert.Equal("Alice", t.GetValue(0).GetValue()); }
          }
          finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
      }

      [SkippableFact] // upstream MultipleQuery: three statements -> three result sets
      public void Multi_statement_query_yields_all_result_sets()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);

          System.Collections.Generic.IReadOnlyList<QueryResult> results =
              conn.QueryAll("RETURN 1; RETURN 2; RETURN 3;");
          try
          {
              Assert.Equal(3, results.Count);
              Assert.Equal(1L, results[0].Rows().Single()[0]);
              Assert.Equal(2L, results[1].Rows().Single()[0]);
              Assert.Equal(3L, results[2].Rows().Single()[0]);
          }
          finally { foreach (QueryResult r in results) r.Dispose(); }
      }

      [SkippableFact] // upstream MultipleQuery chain primitives
      public void Result_chain_walks_with_hasNext_getNext_query_result()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);

          using QueryResult first = conn.Query("RETURN 1; RETURN 2; RETURN 3;");
          Assert.True(first.HasNextQueryResult());
          using QueryResult second = first.GetNextQueryResult();
          Assert.Equal(2L, second.Rows().Single()[0]);
          using QueryResult third = first.GetNextQueryResult();
          Assert.Equal(3L, third.Rows().Single()[0]);
          Assert.False(first.HasNextQueryResult());
      }
  }
  ```
- [ ] Run / expected FAIL until WS-B lands `GetColumnType`/`Columns`/`Summary`/`ResetIterator`/`QueryAll`/`HasNextQueryResult`/`GetNextQueryResult`:
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\LadybugDB.Tests.Parity.csproj -c Debug --filter FullyQualifiedName~QueryResultParityTests`
  Expected red: compile errors on the not-yet-merged members; otherwise PASS/SKIP with native.
- [ ] No implementation here (WS-B owns these members). Re-run after merge.
- [ ] Commit: `test(parity): port query_result_test.cpp (columns, types, summary, iterator, multi-result)`

---

## TASK 6 — Port `data_type_test.cpp` via the LogicalType surface

Upstream constructs `lbug_logical_type`s directly and asserts id / clone-equality / array-element-count. The managed binding exposes logical types via `QueryResult.GetColumnType` (WS-B `LogicalType` with `Id`, `ChildType`, `FixedArraySize`, `ToString`). Port the observable invariants by querying values whose column types are INT64, LIST(INT64), and ARRAY(INT64, N).

- [ ] Write the FAILING test `test/LadybugDB.Tests.Parity/DataTypeParityTests.cs`:
  ```csharp
  using LadybugDB;
  using Xunit;

  namespace LadybugDB.Tests.Parity;

  /// <summary>
  /// Port of upstream <c>test/c_api/data_type_test.cpp</c>. The managed binding does not expose a
  /// public logical-type constructor; types are observed through <see cref="QueryResult.GetColumnType"/>.
  /// </summary>
  public sealed class DataTypeParityTests
  {
      [SkippableFact] // upstream GetID for INT64 / LIST
      public void Scalar_and_list_logical_type_ids()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);

          using QueryResult r = conn.Query("RETURN 42 AS scalar, [1, 2, 3] AS list");
          Assert.Equal(DataTypeId.Int64, r.GetColumnType(0).Id);

          LogicalType listType = r.GetColumnType(1);
          Assert.Equal(DataTypeId.List, listType.Id);
          Assert.Equal(DataTypeId.Int64, listType.ChildType!.Id); // upstream getChildType
      }

      [SkippableFact] // upstream GetFixedNumElementsInList: ARRAY honors element count, LIST does not
      public void Array_reports_fixed_size_list_does_not()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);

          conn.Query("CREATE NODE TABLE A(id INT64, v INT64[3], PRIMARY KEY(id))").Dispose();
          conn.Query("CREATE (:A {id: 1, v: [10, 20, 30]})").Dispose();

          using QueryResult arr = conn.Query("MATCH (a:A) RETURN a.v");
          LogicalType arrType = arr.GetColumnType(0);
          Assert.Equal(DataTypeId.Array, arrType.Id);
          Assert.Equal(3UL, arrType.FixedArraySize);

          using QueryResult lst = conn.Query("RETURN [1, 2, 3] AS v");
          Assert.Null(lst.GetColumnType(0).FixedArraySize); // LIST has no fixed size
      }

      [SkippableFact] // upstream Equals/Clone observable as ToString stability
      public void Logical_type_to_string_is_descriptive()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);

          using QueryResult r = conn.Query("RETURN [1, 2, 3] AS list");
          string s = r.GetColumnType(0).ToString();
          Assert.Contains("LIST", s, System.StringComparison.OrdinalIgnoreCase);
          Assert.Contains("INT64", s, System.StringComparison.OrdinalIgnoreCase);
      }
  }
  ```
- [ ] Run / expected FAIL until WS-B's `LogicalType` (with `ChildType`/`FixedArraySize`/`ToString`) is merged:
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\LadybugDB.Tests.Parity.csproj -c Debug --filter FullyQualifiedName~DataTypeParityTests`
- [ ] No implementation here. Re-run after WS-B merge.
- [ ] Commit: `test(parity): port data_type_test.cpp (logical-type id, child type, fixed-array size)`

---

## TASK 7 — Port `flat_tuple_test.cpp` (per-value access + to-string)

Upstream reads `fName`/`age`/`height` from the first ordered tuple, checks the type id and value of each, and asserts out-of-range index errors. The managed `FlatTuple.GetValue(ulong)` + `Value.DataTypeId`/`.GetValue()` cover this.

- [ ] Write the FAILING test `test/LadybugDB.Tests.Parity/FlatTupleParityTests.cs`:
  ```csharp
  using System;
  using System.Linq;
  using LadybugDB;
  using Xunit;

  namespace LadybugDB.Tests.Parity;

  /// <summary>Port of upstream <c>test/c_api/flat_tuple_test.cpp</c>.</summary>
  public sealed class FlatTupleParityTests
  {
      [SkippableFact] // upstream GetValue: typed access to each column of the first tuple
      public void GetValue_reads_typed_columns()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
          try
          {
              using QueryResult r = conn.Query(
                  "MATCH (a:person) RETURN a.fName, a.age, a.height ORDER BY a.fName LIMIT 1");
              Assert.True(r.HasNext());
              using FlatTuple t = r.GetNext();

              using (Value name = t.GetValue(0))
              {
                  Assert.Equal(DataTypeId.String, name.DataTypeId);
                  Assert.Equal("Alice", name.GetValue());
              }
              using (Value age = t.GetValue(1))
              {
                  Assert.Equal(DataTypeId.Int64, age.DataTypeId);
                  Assert.Equal(35L, age.GetValue());
              }
              using (Value height = t.GetValue(2))
              {
                  Assert.Equal(DataTypeId.Float, height.DataTypeId);
                  Assert.Equal(1.731f, Assert.IsType<float>(height.GetValue()), 3);
              }
          }
          finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
      }

      [SkippableFact] // upstream GetValue out-of-range index
      public void GetValue_out_of_range_throws()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
          try
          {
              using QueryResult r = conn.Query("MATCH (a:person) RETURN a.fName LIMIT 1");
              using FlatTuple t = r.GetNext();
              Assert.ThrowsAny<Exception>(() => t.GetValue(222));
          }
          finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
      }

      [SkippableFact] // upstream ToString: pipe-delimited row text
      public void Tuple_to_string_is_pipe_delimited()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
          try
          {
              using QueryResult r = conn.Query(
                  "MATCH (a:person) RETURN a.fName, a.age ORDER BY a.fName LIMIT 1");
              using FlatTuple t = r.GetNext();
              string s = t.ToString() ?? string.Empty;
              Assert.Contains("Alice", s);
              Assert.Contains("|", s);
          }
          finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
      }
  }
  ```
- [ ] Verify `FlatTuple.ToString()` exists; read `src/LadybugDB/FlatTuple.cs`. If `ToString` over `lbug_flat_tuple_to_string` is not yet present, mark the to-string test `BLOCKED: needs FlatTuple.ToString` (a WS-B/result-surface concern) and keep the other two.
- [ ] Run / expected PASS-or-SKIP (these use only existing `FlatTuple.GetValue`/`Value` API, except the optional to-string):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\LadybugDB.Tests.Parity.csproj -c Debug --filter FullyQualifiedName~FlatTupleParityTests`
- [ ] Commit: `test(parity): port flat_tuple_test.cpp (typed value access, range error, to-string)`

---

## TASK 8 — Port `prepared_statement_test.cpp` (the bind matrix)

Upstream has 19 cases; the meaningful managed-portable ones are the typed-bind round-trips. Port a parameterized bind matrix plus IsReadOnly and error-message cases. Use the new WS-C bind overloads where the upstream binds DATE/TIMESTAMP/INTERVAL/INT128/DECIMAL/BLOB, but cover the already-shipped scalar binds first so most of the file is green pre-WS-C.

- [ ] Write the FAILING test `test/LadybugDB.Tests.Parity/PreparedStatementParityTests.cs`:
  ```csharp
  using System;
  using System.Linq;
  using System.Numerics;
  using LadybugDB;
  using Xunit;

  namespace LadybugDB.Tests.Parity;

  /// <summary>Port of upstream <c>test/c_api/prepared_statement_test.cpp</c> bind matrix.</summary>
  public sealed class PreparedStatementParityTests
  {
      [SkippableTheory] // upstream BindBool/BindInt*/BindUInt*/BindDouble/BindFloat/BindString
      [InlineData("RETURN $p", true, true)]
      [InlineData("RETURN $p", (sbyte)7, (sbyte)7)]
      [InlineData("RETURN $p", (short)9, (short)9)]
      [InlineData("RETURN $p", 11, 11)]
      [InlineData("RETURN $p", 13L, 13L)]
      [InlineData("RETURN $p", 2.5d, 2.5d)]
      [InlineData("RETURN $p", "hello", "hello")]
      public void Scalar_bind_roundtrips(string cypher, object bound, object expected)
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);

          using PreparedStatement stmt = conn.Prepare(cypher);
          stmt.Bind("p", bound);
          using QueryResult r = conn.Execute(stmt);
          Assert.Equal(expected, r.Rows().Single()[0]);
      }

      [SkippableFact] // upstream BindFloat (float not allowed in [InlineData]; assert separately)
      public void Bind_float_roundtrips()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);
          using PreparedStatement stmt = conn.Prepare("RETURN $p");
          stmt.Bind("p", 3.5f);
          Assert.Equal(3.5f, conn.Execute(stmt).Rows().Single()[0]);
      }

      [SkippableFact] // upstream IsReadOnly
      public void Read_only_statement_is_distinguished()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);
          using PreparedStatement read = conn.Prepare("RETURN 1");
          Assert.True(read.IsReadOnly);
          using PreparedStatement write =
              conn.Prepare("CREATE NODE TABLE P(name STRING, PRIMARY KEY(name))");
          Assert.False(write.IsReadOnly);
      }

      [SkippableFact] // upstream GetErrorMessage on a binder failure
      public void Prepare_failure_throws_with_message()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);
          LadybugQueryException ex = Assert.Throws<LadybugQueryException>(
              () => conn.Prepare("MATCH (a:personnnn) WHERE a.isStudent = $s RETURN COUNT(*)"));
          Assert.False(string.IsNullOrWhiteSpace(ex.Message));
      }

      // ----- WS-C bind overloads: DATE / TIMESTAMP / INTERVAL / INT128 / DECIMAL / BLOB -----

      [SkippableFact] // upstream BindDate
      public void Bind_date_roundtrips()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);
          using PreparedStatement stmt = conn.Prepare("RETURN $p");
          stmt.Bind("p", new DateOnly(2020, 1, 15));
          Assert.Equal(new DateOnly(2020, 1, 15), conn.Execute(stmt).Rows().Single()[0]);
      }

      [SkippableFact] // upstream BindInt128 (WS-C BigInteger overload)
      public void Bind_int128_roundtrips()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);
          using PreparedStatement stmt = conn.Prepare("RETURN $p");
          stmt.Bind("p", BigInteger.Parse("123456789012345678901234567890"));
          Assert.Equal(
              Int128.Parse("123456789012345678901234567890"),
              conn.Execute(stmt).Rows().Single()[0]);
      }

      [SkippableFact] // upstream BindValue / BLOB
      public void Bind_blob_roundtrips()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);
          using PreparedStatement stmt = conn.Prepare("RETURN $p");
          stmt.Bind("p", new byte[] { 1, 2, 3, 255 });
          Assert.Equal(new byte[] { 1, 2, 3, 255 }, conn.Execute(stmt).Rows().Single()[0]);
      }
  }
  ```
- [ ] Run / expected outcome:
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\LadybugDB.Tests.Parity.csproj -c Debug --filter FullyQualifiedName~PreparedStatementParityTests`
  The scalar/read-only/error tests use the already-shipped `Bind(object?)` dispatch and `IsReadOnly` (confirm `PreparedStatement.IsReadOnly` exists by reading `src/LadybugDB/PreparedStatement.cs`; if absent mark `BLOCKED: needs PreparedStatement.IsReadOnly`). The `BigInteger`/`byte[]` overloads come from WS-C §4.2 — those facts FAIL to compile until WS-C merges; mark them `BLOCKED: needs WS-C Bind(BigInteger)/Bind(byte[])`.
- [ ] No implementation here. Re-run after WS-C merge.
- [ ] Commit: `test(parity): port prepared_statement_test.cpp bind matrix (scalars + WS-C overloads)`

---

## TASK 9 — Port `value_test.cpp` (read + accessor cases through the materializer)

The upstream value tests largely cover the C value *constructors* (`lbug_value_create_*`), which the managed binding does not expose. Port the *read*/*accessor* halves that go through the materializer: typed reads (GetInt8…GetUInt64, GetFloat/Double, GetString, GetBlob, GetUUID, GetInternalID), Int128 string round-trip, temporal reads, list/struct/map element reads, node/rel property reads, and the DECIMAL-as-text case (`getDecimalAsString` → WS-C `LadybugDecimal`/`GetDecimal`).

- [ ] Write the FAILING test `test/LadybugDB.Tests.Parity/ValueParityTests.cs`:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Numerics;
  using LadybugDB;
  using Xunit;

  namespace LadybugDB.Tests.Parity;

  /// <summary>
  /// Port of the read/accessor cases of upstream <c>test/c_api/value_test.cpp</c>. Constructor cases
  /// (<c>lbug_value_create_*</c>) are out of scope: the managed binding materializes values, it does
  /// not expose value construction. Reads are driven through Cypher literals / the person graph.
  /// </summary>
  public sealed class ValueParityTests
  {
      private static object? Scalar(Connection conn, string cypher)
      {
          using QueryResult r = conn.Query(cypher);
          return r.Rows().Single()[0];
      }

      [SkippableFact] // GetBool / GetInt* / GetUInt* / GetFloat / GetDouble / GetString
      public void Primitive_reads_map_to_clr_types()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);

          Assert.Equal(true, Scalar(conn, "RETURN true"));
          Assert.Equal((sbyte)8, Scalar(conn, "RETURN cast(8 AS INT8)"));
          Assert.Equal((short)16, Scalar(conn, "RETURN cast(16 AS INT16)"));
          Assert.Equal(32, Scalar(conn, "RETURN cast(32 AS INT32)"));
          Assert.Equal(64L, Scalar(conn, "RETURN cast(64 AS INT64)"));
          Assert.Equal((byte)8, Scalar(conn, "RETURN cast(8 AS UINT8)"));
          Assert.Equal((ushort)16, Scalar(conn, "RETURN cast(16 AS UINT16)"));
          Assert.Equal(32u, Scalar(conn, "RETURN cast(32 AS UINT32)"));
          Assert.Equal(64ul, Scalar(conn, "RETURN cast(64 AS UINT64)"));
          Assert.Equal(1.5f, Scalar(conn, "RETURN cast(1.5 AS FLOAT)"));
          Assert.Equal(2.5d, Scalar(conn, "RETURN cast(2.5 AS DOUBLE)"));
          Assert.Equal("hello", Scalar(conn, "RETURN 'hello'"));
      }

      [SkippableFact] // GetInt128 + StringToInt128 + Int128ToString round-trip
      public void Int128_reads_and_round_trips()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);
          Assert.Equal(
              Int128.Parse("123456789012345678901234567890"),
              Scalar(conn, "RETURN cast(123456789012345678901234567890 AS INT128)"));
      }

      [SkippableFact] // GetDate / GetTimestamp
      public void Temporal_reads_map_to_clr_types()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);
          Assert.Equal(new DateOnly(2020, 1, 15), Scalar(conn, "RETURN date('2020-01-15')"));
          Assert.Equal(
              new DateTime(2020, 1, 15, 10, 30, 0, DateTimeKind.Utc),
              Scalar(conn, "RETURN timestamp('2020-01-15 10:30:00')"));
      }

      [SkippableFact] // GetBlob / GetUUID
      public void Blob_and_uuid_reads()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);
          Assert.Equal(new byte[] { 0xAA, 0xBB }, Scalar(conn, @"RETURN BLOB('\xAA\xBB')"));
          object? uuid = Scalar(conn, "RETURN UUID('a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11')");
          Assert.Equal(Guid.Parse("a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11"), uuid);
      }

      [SkippableFact] // GetListElement / GetListSize
      public void List_element_reads()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);
          var list = Assert.IsType<object?[]>(Scalar(conn, "RETURN [10, 20, 30]"));
          Assert.Equal(3, list.Length);
          Assert.Equal(20L, list[1]);
      }

      [SkippableFact] // GetStructFieldName / GetStructFieldValue
      public void Struct_field_reads()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);
          var s = Assert.IsType<Dictionary<string, object?>>(Scalar(conn, "RETURN {a: 1, b: 'x'}"));
          Assert.Equal(1L, s["a"]);
          Assert.Equal("x", s["b"]);
      }

      [SkippableFact] // getMapKey / getMapValue
      public void Map_entry_reads()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);
          object? m = Scalar(conn, "RETURN map(['k1','k2'], [1, 2])");
          var dict = Assert.IsType<Dictionary<object, object?>>(m);
          Assert.Equal(1L, dict["k1"]);
          Assert.Equal(2L, dict["k2"]);
      }

      [SkippableFact] // NodeVal property + id reads
      public void Node_value_reads()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
          try
          {
              using QueryResult r = conn.Query("MATCH (p:person {fName:'Alice'}) RETURN p");
              var node = Assert.IsType<Node>(r.Rows().Single()[0]);
              Assert.Equal("person", node.Label);
              Assert.Equal("Alice", node.Properties["fName"]);
              Assert.Equal(35L, node.Properties["age"]);
          }
          finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
      }

      [SkippableFact] // RelVal property reads
      public void Rel_value_reads()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          (Database db, Connection conn) = ParityEnvironment.CreatePersonGraph(out string path);
          try
          {
              using QueryResult r = conn.Query(
                  "MATCH (:person {fName:'Alice'})-[k:knows]->(:person {fName:'Bob'}) RETURN k");
              var rel = Assert.IsType<Rel>(r.Rows().Single()[0]);
              Assert.Equal("knows", rel.Label);
              Assert.Equal(2011L, rel.Properties["since"]);
          }
          finally { conn.Dispose(); db.Dispose(); ParityEnvironment.TryDelete(path); }
      }

      [SkippableFact] // getDecimalAsString -> WS-C LadybugDecimal / GetDecimal (never a bare string)
      public void Decimal_read_is_lossless_and_never_a_bare_string()
      {
          Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          using var db = new Database(":memory:");
          using var conn = new Connection(db);

          using QueryResult r = conn.Query("RETURN cast(123.45 AS DECIMAL(5,2)) AS d");
          object? value = r.Rows().Single()[0];
          Assert.IsNotType<string>(value);                 // the P0 fix: never silently a string
          Assert.Equal(123.45m, Assert.IsType<decimal>(value));

          using QueryResult r2 = conn.Query("RETURN cast(123.45 AS DECIMAL(5,2)) AS d");
          using FlatTuple t = r2.GetNext();
          using Value v = t.GetValue(0);
          LadybugDecimal d = v.GetDecimal();               // deterministic accessor
          Assert.Equal(123.45m, d.ToDecimal());
          Assert.Equal("123.45", d.ToString());
      }
  }
  ```
- [ ] Run / expected outcome:
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\LadybugDB.Tests.Parity.csproj -c Debug --filter FullyQualifiedName~ValueParityTests`
  Primitive/temporal/list/struct/node/rel reads use the already-shipped materializer (PASS/SKIP). The map test depends on the materializer's map shape — if the binding materializes MAP as `Dictionary<object,object?>` confirm via `src/LadybugDB/Value.cs`; adjust the asserted type to whatever `Value.GetValue()` returns for MAP (do not change the materializer). The DECIMAL test FAILs to compile until WS-C lands `LadybugDecimal`/`Value.GetDecimal` and the never-a-string fix; mark it `BLOCKED: needs WS-C LadybugDecimal/GetDecimal`.
- [ ] No implementation here. Re-run after WS-C merge; if a read does not match (e.g., UUID materializes as string not Guid), record the discrepancy in **openQuestions** rather than weakening the assert silently.
- [ ] Commit: `test(parity): port value_test.cpp read/accessor cases (primitives, temporal, nested, graph, decimal)`

---

## TASK 10 — Benchmark project skeleton + the pure `CiGate` (non-gated unit test first)

The `--ci-gate` math must be unit-testable without native or BenchmarkDotNet. Build `CiGate` as a pure class, test it with a plain `[Fact]`, then wire BDN around it.

- [ ] Create `benchmarks/LadybugDB.Benchmarks/LadybugDB.Benchmarks.csproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
      <OutputType>Exe</OutputType>
      <TargetFramework>net10.0</TargetFramework>
      <IsPackable>false</IsPackable>
      <Nullable>enable</Nullable>
      <ImplicitUsings>enable</ImplicitUsings>
    </PropertyGroup>

    <ItemGroup>
      <PackageReference Include="BenchmarkDotNet" Version="0.14.0" />
    </ItemGroup>

    <ItemGroup>
      <ProjectReference Include="..\..\src\LadybugDB\LadybugDB.csproj" />
    </ItemGroup>

    <!-- The bench needs the native lib to actually run; staged copy is best-effort. -->
    <Target Name="PlaceNativeLibrary" AfterTargets="Build">
      <ItemGroup>
        <_LadybugNative Include="$(NativeLibDir)runtimes\$(TargetRid)\native\*.*" />
      </ItemGroup>
      <Copy SourceFiles="@(_LadybugNative)" DestinationFolder="$(OutputPath)" SkipUnchangedFiles="true" Condition="'@(_LadybugNative)' != ''" />
    </Target>

  </Project>
  ```
- [ ] Create `benchmarks/LadybugDB.Benchmarks/CiGate.cs` — pure logic, no native, no BDN types:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.Linq;

  namespace LadybugDB.Benchmarks;

  /// <summary>One benchmark's mean latency in milliseconds.</summary>
  public readonly record struct LatencySample(string Name, double MeanMs);

  /// <summary>A single gate decision for one benchmark.</summary>
  public readonly record struct GateResult(string Name, double Ratio, double Ceiling, bool Passed)
  {
      public override string ToString() =>
          $"{(Passed ? "PASS" : "FAIL")} {Name}: ratio {Ratio.ToString("0.###", CultureInfo.InvariantCulture)} " +
          $"(ceiling {Ceiling.ToString("0.###", CultureInfo.InvariantCulture)})";
  }

  /// <summary>
  /// Pure latency-regression gate: for each candidate sample with a matching baseline, computes the
  /// candidate/baseline mean ratio and fails when it exceeds the ceiling. The ceiling comes from
  /// <c>LADYBUG_BENCH_MAX_RATIO</c> (default 1.25 = allow 25% regression). Native- and BDN-free so it
  /// is unit-tested without staging an engine.
  /// </summary>
  public static class CiGate
  {
      public const double DefaultCeiling = 1.25;
      public const string CeilingEnvVar = "LADYBUG_BENCH_MAX_RATIO";

      public static double ResolveCeiling(string? raw, double fallback = DefaultCeiling) =>
          double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0
              ? v
              : fallback;

      /// <summary>
      /// Evaluates each candidate against the baseline of the same name. Candidates with no baseline
      /// are reported as passing (a new benchmark cannot regress). Throws <see cref="ArgumentException"/>
      /// if a baseline mean is non-positive (a corrupt baseline, not a regression).
      /// </summary>
      public static IReadOnlyList<GateResult> Evaluate(
          IEnumerable<LatencySample> baseline,
          IEnumerable<LatencySample> candidate,
          double ceiling)
      {
          Dictionary<string, double> baselineByName =
              baseline.ToDictionary(b => b.Name, b => b.MeanMs, StringComparer.Ordinal);

          var results = new List<GateResult>();
          foreach (LatencySample c in candidate)
          {
              if (!baselineByName.TryGetValue(c.Name, out double baseMs))
              {
                  results.Add(new GateResult(c.Name, 1.0, ceiling, Passed: true));
                  continue;
              }

              if (baseMs <= 0)
              {
                  throw new ArgumentException($"Baseline mean for '{c.Name}' is non-positive ({baseMs}).");
              }

              double ratio = c.MeanMs / baseMs;
              results.Add(new GateResult(c.Name, ratio, ceiling, Passed: ratio <= ceiling));
          }

          return results;
      }

      /// <summary>True when every gate result passed.</summary>
      public static bool AllPassed(IReadOnlyList<GateResult> results) => results.All(r => r.Passed);
  }
  ```
- [ ] Create `benchmarks/LadybugDB.Benchmarks.Tests/LadybugDB.Benchmarks.Tests.csproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
      <IsPackable>false</IsPackable>
      <IsTestProject>true</IsTestProject>
      <Nullable>enable</Nullable>
    </PropertyGroup>

    <ItemGroup>
      <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
      <PackageReference Include="xunit" Version="2.9.2" />
      <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
        <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
        <PrivateAssets>all</PrivateAssets>
      </PackageReference>
    </ItemGroup>

    <ItemGroup>
      <ProjectReference Include="..\LadybugDB.Benchmarks\LadybugDB.Benchmarks.csproj" />
    </ItemGroup>

  </Project>
  ```
- [ ] Write the FAILING test `benchmarks/LadybugDB.Benchmarks.Tests/CiGateTests.cs` (plain `[Fact]`, NEVER native-gated):
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using LadybugDB.Benchmarks;
  using Xunit;

  namespace LadybugDB.Benchmarks.Tests;

  public sealed class CiGateTests
  {
      private static readonly LatencySample[] Baseline =
      {
          new("Query", 10.0),
          new("Prepare", 5.0),
      };

      [Fact]
      public void Within_ceiling_passes()
      {
          var candidate = new[] { new LatencySample("Query", 11.0), new LatencySample("Prepare", 5.5) };
          IReadOnlyList<GateResult> results = CiGate.Evaluate(Baseline, candidate, ceiling: 1.25);
          Assert.True(CiGate.AllPassed(results));
      }

      [Fact]
      public void Over_ceiling_fails_the_offending_benchmark()
      {
          var candidate = new[] { new LatencySample("Query", 14.0), new LatencySample("Prepare", 5.0) };
          IReadOnlyList<GateResult> results = CiGate.Evaluate(Baseline, candidate, ceiling: 1.25);
          Assert.False(CiGate.AllPassed(results));
          GateResult query = results.Single(r => r.Name == "Query");
          Assert.False(query.Passed);
          Assert.Equal(1.4, query.Ratio, 3);
      }

      [Fact]
      public void New_benchmark_without_baseline_passes()
      {
          var candidate = new[] { new LatencySample("BrandNew", 99.0) };
          IReadOnlyList<GateResult> results = CiGate.Evaluate(Baseline, candidate, ceiling: 1.25);
          Assert.True(CiGate.AllPassed(results));
      }

      [Fact]
      public void Non_positive_baseline_throws()
      {
          var bad = new[] { new LatencySample("Query", 0.0) };
          var candidate = new[] { new LatencySample("Query", 10.0) };
          Assert.Throws<ArgumentException>(() => CiGate.Evaluate(bad, candidate, ceiling: 1.25));
      }

      [Fact]
      public void Resolve_ceiling_parses_env_or_falls_back()
      {
          Assert.Equal(1.5, CiGate.ResolveCeiling("1.5"));
          Assert.Equal(CiGate.DefaultCeiling, CiGate.ResolveCeiling(null));
          Assert.Equal(CiGate.DefaultCeiling, CiGate.ResolveCeiling("garbage"));
          Assert.Equal(CiGate.DefaultCeiling, CiGate.ResolveCeiling("-2"));
      }
  }
  ```
- [ ] Run / expected FAIL first (no `CiGate.cs` compiled yet if you write the test before the class — but here the class is in step 2). Add both projects to the solution, then run:
  ```
  dotnet sln W:\code\ladybug\tools\csharp_api\LadybugDB.slnx add W:\code\ladybug\tools\csharp_api\benchmarks\LadybugDB.Benchmarks\LadybugDB.Benchmarks.csproj
  dotnet sln W:\code\ladybug\tools\csharp_api\LadybugDB.slnx add W:\code\ladybug\tools\csharp_api\benchmarks\LadybugDB.Benchmarks.Tests\LadybugDB.Benchmarks.Tests.csproj
  dotnet test W:\code\ladybug\tools\csharp_api\benchmarks\LadybugDB.Benchmarks.Tests\LadybugDB.Benchmarks.Tests.csproj -c Debug
  ```
  Expected: **5 tests pass** (`Passed!  - Failed: 0`), with NO skips — this is the required non-gated unit test of the gate logic.
- [ ] Commit: `bench: add CiGate latency-ratio logic with non-gated unit tests`

---

## TASK 11 — BenchmarkDotNet definitions + `Program.cs` dispatch (`--ci-gate` / `--diff` / BDN)

- [ ] Create `benchmarks/LadybugDB.Benchmarks/QueryBenchmarks.cs` — BDN benchmarks over the real engine (only run under BDN, never under `dotnet test`):
  ```csharp
  using System.Linq;
  using BenchmarkDotNet.Attributes;
  using LadybugDB;

  namespace LadybugDB.Benchmarks;

  /// <summary>
  /// Latency benchmarks over an in-memory database. Names here must match the names used by the
  /// baseline JSON consumed by the <c>--ci-gate</c> path (see <see cref="CiGate"/>).
  /// </summary>
  [MemoryDiagnoser]
  public class QueryBenchmarks
  {
      private Database _db = null!;
      private Connection _conn = null!;
      private PreparedStatement _prepared = null!;

      [GlobalSetup]
      public void Setup()
      {
          _db = new Database(":memory:");
          _conn = new Connection(_db);
          _conn.Query("CREATE NODE TABLE Person(name STRING, age INT64, PRIMARY KEY(name))").Dispose();
          for (int i = 0; i < 1000; i++)
          {
              using PreparedStatement ins = _conn.Prepare("CREATE (:Person {name: $n, age: $a})");
              ins.Bind("n", "p" + i).Bind("a", (long)i);
              ins.Execute().Dispose();
          }
          _prepared = _conn.Prepare("MATCH (p:Person) WHERE p.age >= $a RETURN p.name");
      }

      [GlobalCleanup]
      public void Cleanup()
      {
          _prepared.Dispose();
          _conn.Dispose();
          _db.Dispose();
      }

      [Benchmark]
      public int Query()
      {
          using QueryResult r = _conn.Query("MATCH (p:Person) RETURN p.name, p.age");
          return r.Rows().Count();
      }

      [Benchmark]
      public int Prepare()
      {
          using PreparedStatement p = _conn.Prepare("MATCH (p:Person) WHERE p.age >= $a RETURN p.name");
          return p is null ? 0 : 1;
      }

      [Benchmark]
      public int Execute()
      {
          _prepared.Bind("a", 500L);
          using QueryResult r = _conn.Execute(_prepared);
          return r.Rows().Count();
      }
  }
  ```
- [ ] Create `benchmarks/LadybugDB.Benchmarks/Program.cs` — dispatch. `--ci-gate <baseline.json> <candidate.json>` runs the pure gate and sets the process exit code; `--diff` runs the differential runner; no recognized flag → BenchmarkDotNet:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Text.Json;
  using BenchmarkDotNet.Running;
  using LadybugDB.Benchmarks;

  if (args.Length > 0 && args[0] == "--ci-gate")
  {
      if (args.Length < 3)
      {
          Console.Error.WriteLine("usage: --ci-gate <baseline.json> <candidate.json>");
          return 2;
      }

      List<LatencySample> baseline = LoadSamples(args[1]);
      List<LatencySample> candidate = LoadSamples(args[2]);
      double ceiling = CiGate.ResolveCeiling(Environment.GetEnvironmentVariable(CiGate.CeilingEnvVar));

      IReadOnlyList<GateResult> results = CiGate.Evaluate(baseline, candidate, ceiling);
      foreach (GateResult r in results)
      {
          Console.WriteLine(r.ToString());
      }

      bool ok = CiGate.AllPassed(results);
      Console.WriteLine(ok ? "ci-gate: PASS" : "ci-gate: FAIL");
      return ok ? 0 : 1;
  }

  if (args.Length > 0 && args[0] == "--diff")
  {
      return DifferentialRunner.Run(Console.Out) ? 0 : 1;
  }

  BenchmarkRunner.Run<QueryBenchmarks>();
  return 0;

  static List<LatencySample> LoadSamples(string path)
  {
      // Accepts either our own [{ "Name": ..., "MeanMs": ... }] array, or a BenchmarkDotNet
      // "*-report-full.json" file (Benchmarks[].{FullName|Method}, Statistics.Mean in ns).
      using FileStream fs = File.OpenRead(path);
      using JsonDocument doc = JsonDocument.Parse(fs);
      JsonElement root = doc.RootElement;

      if (root.ValueKind == JsonValueKind.Array)
      {
          return root.EnumerateArray()
              .Select(e => new LatencySample(
                  e.GetProperty("Name").GetString() ?? "",
                  e.GetProperty("MeanMs").GetDouble()))
              .ToList();
      }

      var samples = new List<LatencySample>();
      foreach (JsonElement b in root.GetProperty("Benchmarks").EnumerateArray())
      {
          string name = b.TryGetProperty("Method", out JsonElement m) ? m.GetString() ?? "" : "";
          double meanNs = b.GetProperty("Statistics").GetProperty("Mean").GetDouble();
          samples.Add(new LatencySample(name, meanNs / 1_000_000.0)); // ns -> ms
      }

      return samples;
  }
  ```
- [ ] Build the bench project (it must compile even though running BDN needs native at runtime):
  `dotnet build W:\code\ladybug\tools\csharp_api\benchmarks\LadybugDB.Benchmarks\LadybugDB.Benchmarks.csproj -c Debug` — **expected PASS** (compile only).
- [ ] Smoke the `--ci-gate` path end-to-end with two tiny JSON files (no native, no BDN) to prove the exit-code wiring:
  ```
  pwsh -NoProfile -Command "Set-Content -Path $env:TEMP\base.json -Value '[{\"Name\":\"Query\",\"MeanMs\":10}]'; Set-Content -Path $env:TEMP\cand.json -Value '[{\"Name\":\"Query\",\"MeanMs\":20}]'; dotnet run --project W:\code\ladybug\tools\csharp_api\benchmarks\LadybugDB.Benchmarks\LadybugDB.Benchmarks.csproj -c Debug -- --ci-gate $env:TEMP\base.json $env:TEMP\cand.json; Write-Host \"exit=$LASTEXITCODE\""
  ```
  **Expected:** prints `FAIL Query: ratio 2 (ceiling 1.25)`, `ci-gate: FAIL`, and `exit=1`. Re-run with `MeanMs:11` for the candidate → `ci-gate: PASS`, `exit=0`.
- [ ] Commit: `bench: add BenchmarkDotNet query benchmarks + --ci-gate/--diff dispatch`

---

## TASK 12 — Differential smoke runner + native-gated wrapper test

The differential smoke runner exercises the public surface end-to-end and compares semantically equivalent paths (e.g. `Query` row materialization vs `Rows()` vs `Execute` of the same Cypher) so a regression in any one path surfaces as a mismatch. It returns a bool + writes a report line per check, so both `Program.cs --diff` and a native-gated xUnit test can drive it.

- [ ] Create `benchmarks/LadybugDB.Benchmarks/DifferentialRunner.cs`:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using LadybugDB;

  namespace LadybugDB.Benchmarks;

  /// <summary>
  /// Differential smoke runner: drives the public surface over an in-memory database along multiple
  /// equivalent paths and asserts they agree. A divergence (e.g. <c>Query</c> + <c>Rows()</c> vs
  /// <c>Execute</c> of the same Cypher) indicates a surface regression. Native-dependent; the caller
  /// must guarantee the engine is loadable.
  /// </summary>
  public static class DifferentialRunner
  {
      /// <summary>Runs all checks, writing one line per check. Returns true when all agree.</summary>
      public static bool Run(TextWriter log)
      {
          var checks = new List<(string Name, Func<bool> Check)>
          {
              ("scalar_query_vs_execute", ScalarQueryVsExecute),
              ("rows_match_columncount", RowsMatchColumnCount),
              ("ordered_read_is_stable", OrderedReadIsStable),
          };

          bool allOk = true;
          foreach ((string name, Func<bool> check) in checks)
          {
              bool ok;
              try { ok = check(); }
              catch (Exception ex) { ok = false; log.WriteLine($"ERROR {name}: {ex.Message}"); }
              if (!ok) { allOk = false; }
              log.WriteLine($"{(ok ? "OK" : "MISMATCH")} {name}");
          }

          return allOk;
      }

      private static bool ScalarQueryVsExecute()
      {
          using var db = new Database(":memory:");
          using var conn = new Connection(db);

          object? viaQuery;
          using (QueryResult r = conn.Query("RETURN 1 + 1")) { viaQuery = r.Rows().Single()[0]; }

          object? viaExecute;
          using (PreparedStatement p = conn.Prepare("RETURN 1 + 1"))
          using (QueryResult r = conn.Execute(p)) { viaExecute = r.Rows().Single()[0]; }

          return Equals(viaQuery, viaExecute) && Equals(viaQuery, 2L);
      }

      private static bool RowsMatchColumnCount()
      {
          using var db = new Database(":memory:");
          using var conn = new Connection(db);
          using QueryResult r = conn.Query("RETURN 1 AS a, 'x' AS b, true AS c");
          object?[] row = r.Rows().Single();
          return row.Length == (int)r.ColumnCount && r.ColumnCount == 3UL;
      }

      private static bool OrderedReadIsStable()
      {
          using var db = new Database(":memory:");
          using var conn = new Connection(db);
          conn.Query("CREATE NODE TABLE N(id INT64, PRIMARY KEY(id))").Dispose();
          for (int i = 0; i < 5; i++) { conn.Query($"CREATE (:N {{id: {i}}})").Dispose(); }

          long[] first;
          using (QueryResult r = conn.Query("MATCH (n:N) RETURN n.id ORDER BY n.id"))
          { first = r.Rows().Select(x => (long)x[0]!).ToArray(); }

          long[] second;
          using (QueryResult r = conn.Query("MATCH (n:N) RETURN n.id ORDER BY n.id"))
          { second = r.Rows().Select(x => (long)x[0]!).ToArray(); }

          return first.SequenceEqual(second) && first.SequenceEqual(new long[] { 0, 1, 2, 3, 4 });
      }
  }
  ```
- [ ] Write the FAILING native-gated wrapper test `test/LadybugDB.Tests.Parity/DifferentialSmokeTests.cs` (referencing the bench project's runner). First add a project reference so the parity project can see `DifferentialRunner`:
  - Add to `test/LadybugDB.Tests.Parity/LadybugDB.Tests.Parity.csproj` inside the existing `<ItemGroup>` with the `src` reference:
    ```xml
      <ProjectReference Include="..\..\benchmarks\LadybugDB.Benchmarks\LadybugDB.Benchmarks.csproj" />
    ```
  - Test:
    ```csharp
    using System.IO;
    using LadybugDB.Benchmarks;
    using Xunit;

    namespace LadybugDB.Tests.Parity;

    /// <summary>Native-gated wrapper that runs the differential smoke runner as part of the suite.</summary>
    public sealed class DifferentialSmokeTests
    {
        [SkippableFact]
        public void Differential_runner_reports_no_mismatches()
        {
            Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");

            using var log = new StringWriter();
            bool ok = DifferentialRunner.Run(log);
            Assert.True(ok, log.ToString());
        }
    }
    ```
- [ ] Run / expected outcome:
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\LadybugDB.Tests.Parity.csproj -c Debug --filter FullyQualifiedName~DifferentialSmokeTests`
  Without native staged → **SKIP** (1 skipped). With native staged → **PASS**. Compile must succeed once the bench project reference is added.
- [ ] Also smoke the `--diff` CLI path manually if native is staged:
  `dotnet run --project W:\code\ladybug\tools\csharp_api\benchmarks\LadybugDB.Benchmarks\LadybugDB.Benchmarks.csproj -c Debug -- --diff` — expects three `OK ...` lines and exit 0.
- [ ] Commit: `bench: add differential smoke runner + native-gated suite wrapper`

---

## TASK 13 — `parity-tracking.md` (upstream-file → status, pin tag 0.17.2)

- [ ] Create `docs/parity-2026-06/parity-tracking.md`:
  ```markdown
  # C-API Parity Tracking (2026-06)

  Maps each upstream C-API gtest source under `test/c_api/` to its C# port in
  `test/LadybugDB.Tests.Parity/`. Ports are native-gated (`ParityEnvironment.NativeAvailable`)
  and rebuild a small in-process person graph instead of loading the engine's `dataset/tinysnb`
  CSVs (unavailable to a managed NuGet consumer).

  **Upstream pin:** `LadybugDB/ladybug` tag **v0.17.2** (matches the native bump in `version.txt`
  driven by WS-K). Re-port when the pin moves; re-sync expected values against that tag's
  `src/include/c_api/lbug.h` and `test/c_api/*.cpp`.

  Legend: ✅ ported · 🟡 partially ported (constructor-only or env-specific cases intentionally
  skipped) · ⬜ not ported (with reason).

  | Upstream file | C# port | Status | Notes |
  |---|---|:--:|---|
  | `version_test.cpp` | `VersionParityTests.cs` | ✅ | On-disk magic-header check replaced by storage-version > 0 (managed surface exposes no file path). |
  | `database_test.cpp` | `DatabaseParityTests.cs` | 🟡 | Ported: open/in-memory/read-only/use-after-destroy. Skipped: HomeDir (`~`), EnableMultiWrites (no managed accessor), C-pointer close-after-destroy ordering. |
  | `connection_test.cpp` | `ConnectionParityTests.cs` | ✅ | Query/threads/timeout/interrupt/prepare/execute. Null-handle C cases are not expressible (managed ctor throws). |
  | `query_result_test.cpp` | `QueryResultParityTests.cs` | ✅ | Columns/types/summary/iterator/reset/multi-result via WS-B surface. Arrow-schema case is commented-out upstream; covered by WS-E, not here. |
  | `data_type_test.cpp` | `DataTypeParityTests.cs` | 🟡 | Observed via `QueryResult.GetColumnType` (no public logical-type constructor). Id/child-type/fixed-array-size covered. |
  | `flat_tuple_test.cpp` | `FlatTupleParityTests.cs` | ✅ | Typed value access, out-of-range error, pipe-delimited to-string. |
  | `prepared_statement_test.cpp` | `PreparedStatementParityTests.cs` | 🟡 | Scalar binds + read-only + error ported; DATE/INT128/BLOB use WS-C overloads. INTERVAL/explicit timestamp-precision binds tracked as follow-ups. |
  | `value_test.cpp` | `ValueParityTests.cs` | 🟡 | Read/accessor cases ported (primitives, temporal, list/struct/map, node/rel, decimal-as-text). `lbug_value_create_*` constructor cases are out of scope (no managed value-construction surface). |

  ## Follow-ups
  - INTERVAL and explicit `timestamp_ns/ms/sec` bind ports once WS-C lands those overloads.
  - UNION first-class read parity once WS-C exposes a tagged-union read shape.
  - Arrow differential parity lives with WS-E (`LadybugDB.Arrow`), referenced from the diff runner.
  ```
- [ ] Validate the table renders (no broken pipes) and the per-row Status matches the real checkbox state of Tasks 2–9. Run `dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug` to confirm nothing else broke.
- [ ] Commit: `docs(parity): add parity-tracking map pinned to upstream v0.17.2`

---

## TASK 14 — Wire bench into the Cake pipeline (coordinate with WS-K) + final solution build

WS-K owns `cake/**` and `version.txt`; this task adds a `BenchCiGate` Cake task only if WS-K has not already provided a bench hook. Keep the edit additive and isolated so it does not conflict with WS-K's RID/pack tasks.

- [ ] Read `cake/Tasks/Pipeline.cs` and confirm whether a bench/gate task already exists (WS-K may add one). If absent, append a new task file `cake/Tasks/BenchTask.cs` (a new file avoids conflicting with WS-K's edits to `Pipeline.cs`):
  ```csharp
  using Cake.Common.Tools.DotNet;
  using Cake.Common.Tools.DotNet.Run;
  using Cake.Frosting;

  namespace LadybugDB.Build.Tasks;

  /// <summary>
  /// Runs the benchmark project's <c>--ci-gate</c> over committed baseline/candidate JSON when both
  /// are present. Opt-in: only runs when BENCH_BASELINE and BENCH_CANDIDATE env vars point at files,
  /// so the default Test/Pack flow is unaffected. Latency ceiling comes from LADYBUG_BENCH_MAX_RATIO.
  /// </summary>
  [TaskName("BenchCiGate")]
  public sealed class BenchCiGateTask : FrostingTask<BuildContext>
  {
      public override bool ShouldRun(BuildContext context)
      {
          string? baseline = System.Environment.GetEnvironmentVariable("BENCH_BASELINE");
          string? candidate = System.Environment.GetEnvironmentVariable("BENCH_CANDIDATE");
          return File.Exists(baseline) && File.Exists(candidate);
      }

      public override void Run(BuildContext context)
      {
          string baseline = System.Environment.GetEnvironmentVariable("BENCH_BASELINE")!;
          string candidate = System.Environment.GetEnvironmentVariable("BENCH_CANDIDATE")!;
          string project = Path.Combine(context.Root, "benchmarks", "LadybugDB.Benchmarks", "LadybugDB.Benchmarks.csproj");

          context.DotNetRun(project, new DotNetRunSettings
          {
              Configuration = context.BuildConfiguration,
              ArgumentCustomization = a => a.Append("--").Append("--ci-gate").AppendQuoted(baseline).AppendQuoted(candidate),
          });
      }
  }
  ```
  (Add `using System.IO;` at the top.)
- [ ] Run the gate's own unit tests through the new bench-tests project one more time to confirm the whole solution is green:
  ```
  dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug
  dotnet test W:\code\ladybug\tools\csharp_api\benchmarks\LadybugDB.Benchmarks.Tests\LadybugDB.Benchmarks.Tests.csproj -c Debug
  ```
  **Expected:** solution builds clean; CiGate tests pass (5 passed, 0 skipped).
- [ ] Run the full parity project to confirm it builds and skips cleanly without native:
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\LadybugDB.Tests.Parity.csproj -c Debug`
  **Expected (no native staged):** all native-gated facts **skipped**, 0 failed. (If WS-B/C members are not yet merged, the project will not compile — in that case leave Task 14 unchecked with `BLOCKED: needs WS-B/WS-C merge` and re-run after the merge gate.)
- [ ] Commit: `bench(ci): add opt-in BenchCiGate Cake task + verify full-solution build`

---

## Self-review / done criteria (tied to WS-J Done column: "ported tests + smoke runner + `--ci-gate`")

- [ ] `test/LadybugDB.Tests.Parity/**` exists with one port file per upstream `test/c_api/*.cpp` (8 ports: version, database, connection, query_result, data_type, flat_tuple, prepared_statement, value), each native-gated via `ParityEnvironment.NativeAvailable` and skipping (not failing) when native is absent.
- [ ] Every ported test maps to a real upstream gtest case (named in a comment) and asserts the same observable behavior through the §4 public surface — no invented signatures; blocked members are marked `BLOCKED: needs WS-<x> <member>`, not silently re-shaped.
- [ ] `benchmarks/LadybugDB.Benchmarks/**` builds: `QueryBenchmarks` (BDN), `CiGate` (pure), `DifferentialRunner`, and a `Program.cs` dispatching `--ci-gate` (exit 0/1 by ratio vs `LADYBUG_BENCH_MAX_RATIO` ceiling), `--diff`, and default BDN.
- [ ] `benchmarks/LadybugDB.Benchmarks.Tests/CiGateTests.cs` is **non-gated** (`[Fact]`, no native, no BDN) and passes — proving the gate math, env-ceiling resolution, new-benchmark pass-through, and corrupt-baseline throw.
- [ ] The `--ci-gate` exit-code wiring is verified end-to-end with tiny JSON files (FAIL→exit 1, PASS→exit 0) and accepts both the simple `[{Name,MeanMs}]` array and a BenchmarkDotNet `*-report-full.json`.
- [ ] The differential smoke runner runs from both the CLI (`--diff`) and a native-gated xUnit wrapper (`DifferentialSmokeTests`) and reports no mismatches with native staged.
- [ ] `docs/parity-2026-06/parity-tracking.md` maps each upstream file to its port + status and pins upstream tag **v0.17.2** (consistent with WS-K's `version.txt` bump).
- [ ] All four new projects are registered in `LadybugDB.slnx` (parity under `/test/`, bench + bench-tests under `/benchmarks/`), coexisting with WS-K's edits to the same file.
- [ ] `dotnet build LadybugDB.slnx -c Debug` is green; `dotnet test` on the parity project skips cleanly without native; `dotnet test` on the bench-tests project passes without native.
- [ ] No native binaries committed (everything under `lib/` stays gitignored); no struct/enum/signature edits in this workstream (test-only).
