# WS-G — LadybugDB.Extensions Package Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]` checkboxes.

**Goal:** Ship a `LadybugDB.Extensions` package (DI, health-check, resilience executor, row access, streaming, and result-export helpers) plus a fully fake-based `LadybugDB.Tests.Extensions` suite that needs no native engine.

**Architecture:** The package depends on a minimal in-house executor seam (`ILadybugExecutor` over a `LadybugExecutionResult` record) so every behavior is unit-testable with fakes — mirroring the reference's `INativeLibrary` idea but with our own, smaller surface. The pinned `QueryResultExtensions` (`this QueryResult r`, §4.8) delegate to internal pure helpers that read an `IResultData` view (columns + materialized rows); the real adapter wraps core's public `QueryResult.ColumnNames`/`Rows()`, a fake adapter drives all logic tests without native. Async streaming layers `IAsyncEnumerable<IRowAccessor>` over WS-D's `Connection.StreamAsync`/`QueryAsync` (§4.3). Resilience is hand-rolled (timeout via linked CTS, transient-aware retry, lock-guarded circuit breaker) — no Polly.

**Tech Stack:** C# (`net10.0;netstandard2.0`), `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` (ns2.0 `#if`-gated), `Microsoft.Bcl.AsyncInterfaces` (ns2.0), `System.Text.Json`, `System.Data` (`DataTable`). Tests: xUnit + `Xunit.SkippableFact` (gate only the few native-touching tests; all logic tests ungated).

---

## Files

**Create — package (`src/LadybugDB.Extensions/`):**
- `src/LadybugDB.Extensions/LadybugDB.Extensions.csproj`
- `src/LadybugDB.Extensions/Compat/IsExternalInit.cs` (ns2.0 record/`init` polyfill)
- `src/LadybugDB.Extensions/ILadybugExecutor.cs` (seam + `LadybugExecutionResult` record)
- `src/LadybugDB.Extensions/LadybugConnectionExecutor.cs` (real executor over core `Connection`, WS-D async)
- `src/LadybugDB.Extensions/LadybugOptions.cs`
- `src/LadybugDB.Extensions/LadybugResilienceOptions.cs`
- `src/LadybugDB.Extensions/ResilientLadybugExecutor.cs`
- `src/LadybugDB.Extensions/LadybugHealthCheck.cs`
- `src/LadybugDB.Extensions/IRowAccessor.cs` (interface + internal `RowAccessor`)
- `src/LadybugDB.Extensions/ResultData.cs` (internal `IResultData` + adapters + pure helpers)
- `src/LadybugDB.Extensions/QueryResultExtensions.cs` (pinned §4.8 surface on `this QueryResult`)
- `src/LadybugDB.Extensions/ExecutorStreamingExtensions.cs` (`StreamAsync` over the executor seam)
- `src/LadybugDB.Extensions/LadybugServiceCollectionExtensions.cs` (pinned §4.8 DI surface)

**Create — tests (`test/LadybugDB.Tests.Extensions/`):**
- `test/LadybugDB.Tests.Extensions/LadybugDB.Tests.Extensions.csproj`
- `test/LadybugDB.Tests.Extensions/Fakes/FakeLadybugExecutor.cs`
- `test/LadybugDB.Tests.Extensions/Fakes/FakeResultData.cs`
- `test/LadybugDB.Tests.Extensions/RowAccessorTests.cs`
- `test/LadybugDB.Tests.Extensions/ResultDataHelperTests.cs`
- `test/LadybugDB.Tests.Extensions/ResilientLadybugExecutorTests.cs`
- `test/LadybugDB.Tests.Extensions/LadybugHealthCheckTests.cs`
- `test/LadybugDB.Tests.Extensions/StreamingTests.cs`
- `test/LadybugDB.Tests.Extensions/ServiceCollectionTests.cs`

**Modify:**
- `LadybugDB.slnx` (register the two new projects)
- `src/LadybugDB/LadybugDB.csproj` (add `<InternalsVisibleTo Include="LadybugDB.Extensions" />` so the real adapter can read core internals if needed; and `LadybugDB.Tests.Extensions` is NOT needed — tests use public surface + fakes)

---

## Preconditions & contract notes

- **WS-D dependency (hard):** this plan calls `Connection.QueryAsync(string, CancellationToken)` and
  `Connection.StreamAsync(string, CancellationToken)` returning `Task<QueryResult>` and
  `IAsyncEnumerable<FlatTuple>` exactly as pinned in high-level-plan §4.3. The package's
  `LadybugConnectionExecutor` (the only file that touches core async) is the single integration point;
  if WS-D has not merged when this WS runs, build `LadybugConnectionExecutor` against those exact
  signatures — the fakes carry all test coverage so the suite stays green regardless.
- **§4.8 is pinned — match exactly.** `AddLadybug`/`AddLadybugHealthCheck`/`AddLadybugResilience`,
  `IRowAccessor.{Get<T>(int), Get<T>(string), GetOrDefault<T>(string, T)}`, and the seven
  `QueryResultExtensions` methods (`ToDictionaries`, `Select<T>`, `Scalar<T>`, `ToJson`,
  `ToJsonArray`, `ToDataTable`, `ToCsv(char separator = ',')`). Do not rename or re-signature them.
- **ns2.0 health-check gate (from WS-K note):** if
  `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` resolves on ns2.0 (it does — it is a
  netstandard2.0 package), keep `LadybugHealthCheck` and `AddLadybugHealthCheck` unconditional. Only if
  a Phase-1 restore proves otherwise, wrap *those two types only* in `#if NET || …` and keep the rest of
  the package multi-targeted. Task 1 verifies this empirically before committing the rest.
- **No native in tests:** the executor seam returns a materialized `LadybugExecutionResult`; the
  `QueryResultExtensions` logic is exercised through `IResultData` fakes. No test constructs a core
  `QueryResult` (it is `sealed` with an internal native-handle ctor).
- Commit messages end with:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

---

## TASK 1 — Project skeleton + slnx wiring (verify ns2.0 dep feasibility)

- [ ] Create `src/LadybugDB.Extensions/LadybugDB.Extensions.csproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
      <TargetFrameworks>net10.0;netstandard2.0</TargetFrameworks>
      <AssemblyName>LadybugDB.Extensions</AssemblyName>
      <RootNamespace>LadybugDB.Extensions</RootNamespace>
      <DebugType>portable</DebugType>
      <IsPackable>true</IsPackable>
    </PropertyGroup>

    <PropertyGroup Condition="'$(TargetFramework)' == 'net10.0'">
      <IsAotCompatible>true</IsAotCompatible>
    </PropertyGroup>

    <ItemGroup>
      <InternalsVisibleTo Include="LadybugDB.Tests.Extensions" />
    </ItemGroup>

    <ItemGroup>
      <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="9.0.0" />
      <PackageReference Include="Microsoft.Extensions.Options" Version="9.0.0" />
      <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions" Version="9.0.0" />
      <PackageReference Include="System.Text.Json" Version="9.0.0" />
    </ItemGroup>

    <ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.0'">
      <PackageReference Include="Microsoft.Bcl.AsyncInterfaces" Version="9.0.0" />
    </ItemGroup>

    <ItemGroup>
      <ProjectReference Include="..\LadybugDB\LadybugDB.csproj" />
    </ItemGroup>

  </Project>
  ```
- [ ] Create `src/LadybugDB.Extensions/Compat/IsExternalInit.cs` (records/`init` on ns2.0):
  ```csharp
  #if NETSTANDARD2_0
  namespace System.Runtime.CompilerServices
  {
      /// <summary>Polyfill enabling C# <c>init</c> accessors and records on netstandard2.0.</summary>
      internal static class IsExternalInit
      {
      }
  }
  #endif
  ```
- [ ] Add a temporary placeholder so the project compiles: create
  `src/LadybugDB.Extensions/LadybugOptions.cs` with the real final content:
  ```csharp
  namespace LadybugDB.Extensions;

  /// <summary>Configuration for the Ladybug database registered through dependency injection.</summary>
  public sealed class LadybugOptions
  {
      /// <summary>Filesystem path to the database directory. Empty string opens an in-memory database.</summary>
      public string DatabasePath { get; set; } = string.Empty;

      /// <summary>Opens the database read-only when <see langword="true"/>.</summary>
      public bool ReadOnly { get; set; }

      /// <summary>Maximum threads for query execution; 0 leaves the engine default in place.</summary>
      public ulong MaxThreads { get; set; }

      /// <summary>Buffer-pool size in bytes; 0 leaves the engine default in place.</summary>
      public ulong BufferPoolSize { get; set; }
  }
  ```
- [ ] Register both projects in `LadybugDB.slnx`:
  ```xml
  <Solution>
    <Folder Name="/src/">
      <Project Path="src/LadybugDB/LadybugDB.csproj" />
      <Project Path="src/LadybugDB.Extensions/LadybugDB.Extensions.csproj" />
    </Folder>
    <Folder Name="/test/">
      <Project Path="test/LadybugDB.Tests/LadybugDB.Tests.csproj" />
      <Project Path="test/LadybugDB.Tests.Extensions/LadybugDB.Tests.Extensions.csproj" />
    </Folder>
  </Solution>
  ```
- [ ] **Verify ns2.0 dep feasibility (Phase-1 task):** restore + build both TFMs:
  `dotnet build W:\code\ladybug\tools\csharp_api\src\LadybugDB.Extensions\LadybugDB.Extensions.csproj -c Debug`
  Expected: **PASS** on both `net10.0` and `netstandard2.0`. If the HealthChecks abstractions fail to
  restore on ns2.0, wrap `LadybugHealthCheck`/`AddLadybugHealthCheck` in `#if NET` per the WS-K note and
  re-run; record the decision in an OpenQuestion. (Expected outcome: no gate needed — the abstractions
  package targets ns2.0.)
- [ ] Commit: `feat(extensions): scaffold LadybugDB.Extensions project + options`

## TASK 2 — Test project skeleton (xUnit, ungated)

- [ ] Create `test/LadybugDB.Tests.Extensions/LadybugDB.Tests.Extensions.csproj`:
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
      <ProjectReference Include="..\..\src\LadybugDB.Extensions\LadybugDB.Extensions.csproj" />
    </ItemGroup>

  </Project>
  ```
- [ ] Add a trivial sanity test so the runner is wired: create
  `test/LadybugDB.Tests.Extensions/ServiceCollectionTests.cs`:
  ```csharp
  using LadybugDB.Extensions;
  using Xunit;

  namespace LadybugDB.Tests.Extensions;

  public sealed class ServiceCollectionTests
  {
      [Fact]
      public void Options_default_database_path_is_empty()
      {
          var options = new LadybugOptions();
          Assert.Equal(string.Empty, options.DatabasePath);
          Assert.False(options.ReadOnly);
      }
  }
  ```
- [ ] Run (expect PASS):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --filter FullyQualifiedName~ServiceCollectionTests`
  Expected: **1 passed**.
- [ ] Commit: `test(extensions): scaffold LadybugDB.Tests.Extensions project`

## TASK 3 — Executor seam + materialized result (`ILadybugExecutor`, `LadybugExecutionResult`)

- [ ] **Failing test.** Create `test/LadybugDB.Tests.Extensions/Fakes/FakeLadybugExecutor.cs`:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Threading;
  using System.Threading.Tasks;
  using LadybugDB.Extensions;

  namespace LadybugDB.Tests.Extensions.Fakes;

  /// <summary>In-memory executor used by the extension tests; needs no native engine.</summary>
  internal sealed class FakeLadybugExecutor : ILadybugExecutor
  {
      private readonly Func<string, CancellationToken, Task<LadybugExecutionResult>> _onExecute;

      public int ExecuteCount;

      public FakeLadybugExecutor(Func<string, CancellationToken, Task<LadybugExecutionResult>> onExecute)
      {
          _onExecute = onExecute;
          Name = "fake";
      }

      public static FakeLadybugExecutor Returning(LadybugExecutionResult result)
          => new((_, _) => Task.FromResult(result));

      public string Name { get; set; }

      public Task<LadybugExecutionResult> ExecuteAsync(string cypher, CancellationToken cancellationToken = default)
      {
          Interlocked.Increment(ref ExecuteCount);
          return _onExecute(cypher, cancellationToken);
      }
  }
  ```
  Then add the contract test to `ServiceCollectionTests.cs` (temporary home, moved later) — create a new file
  `test/LadybugDB.Tests.Extensions/ExecutorSeamTests.cs`:
  ```csharp
  using System.Collections.Generic;
  using System.Threading.Tasks;
  using LadybugDB.Extensions;
  using LadybugDB.Tests.Extensions.Fakes;
  using Xunit;

  namespace LadybugDB.Tests.Extensions;

  public sealed class ExecutorSeamTests
  {
      [Fact]
      public async Task Executor_returns_materialized_columns_and_rows()
      {
          var result = new LadybugExecutionResult(
              new[] { "name", "age" },
              new List<object?[]> { new object?[] { "Alice", 30L } });
          var executor = FakeLadybugExecutor.Returning(result);

          LadybugExecutionResult actual = await executor.ExecuteAsync("RETURN 1");

          Assert.Equal("fake", executor.Name);
          Assert.Equal(new[] { "name", "age" }, actual.Columns);
          Assert.Single(actual.Rows);
          Assert.Equal("Alice", actual.Rows[0][0]);
          Assert.Equal(30L, actual.Rows[0][1]);
      }
  }
  ```
- [ ] Run (expect FAIL — `ILadybugExecutor`/`LadybugExecutionResult` do not exist):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --filter FullyQualifiedName~ExecutorSeamTests`
  Expected: **build error / FAIL**.
- [ ] **Implement.** Create `src/LadybugDB.Extensions/ILadybugExecutor.cs`:
  ```csharp
  using System.Collections.Generic;
  using System.Threading;
  using System.Threading.Tasks;

  namespace LadybugDB.Extensions;

  /// <summary>
  /// A materialized query result: column names and fully read-into-memory rows. This is the unit the
  /// extension layer operates on, which keeps the package testable without the native engine.
  /// </summary>
  public sealed class LadybugExecutionResult
  {
      /// <summary>Creates a materialized result.</summary>
      public LadybugExecutionResult(IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows)
      {
          Columns = columns;
          Rows = rows;
      }

      /// <summary>Column names in result order.</summary>
      public IReadOnlyList<string> Columns { get; }

      /// <summary>Materialized rows; each row is aligned with <see cref="Columns"/>.</summary>
      public IReadOnlyList<object?[]> Rows { get; }

      /// <summary>An empty result with no columns and no rows.</summary>
      public static LadybugExecutionResult Empty { get; } =
          new(System.Array.Empty<string>(), System.Array.Empty<object?[]>());
  }

  /// <summary>
  /// Minimal execution seam the extensions depend on. The production implementation
  /// (<see cref="LadybugConnectionExecutor"/>) wraps a core <c>Connection</c>; tests supply a fake.
  /// </summary>
  public interface ILadybugExecutor
  {
      /// <summary>A diagnostic name for the executor (surfaced by the health check).</summary>
      string Name { get; }

      /// <summary>Executes a Cypher query and returns its materialized result.</summary>
      Task<LadybugExecutionResult> ExecuteAsync(string cypher, CancellationToken cancellationToken = default);
  }
  ```
- [ ] Run (expect PASS):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --filter FullyQualifiedName~ExecutorSeamTests`
  Expected: **1 passed**.
- [ ] Commit: `feat(extensions): add ILadybugExecutor seam + materialized result`

## TASK 4 — `IResultData` view + pure result helpers (column index, row→dict, scalar, JSON, CSV, DataTable)

This task lands the testable core of `QueryResultExtensions` as internal helpers over an `IResultData`
view so the seven public methods (Task 9) become thin one-liners.

- [ ] **Failing test.** Create `test/LadybugDB.Tests.Extensions/Fakes/FakeResultData.cs`:
  ```csharp
  using System.Collections.Generic;
  using LadybugDB.Extensions;

  namespace LadybugDB.Tests.Extensions.Fakes;

  internal sealed class FakeResultData : IResultData
  {
      public FakeResultData(IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows)
      {
          Columns = columns;
          MaterializedRows = rows;
      }

      public IReadOnlyList<string> Columns { get; }

      public IReadOnlyList<object?[]> MaterializedRows { get; }

      public IEnumerable<object?[]> EnumerateRows() => MaterializedRows;
  }
  ```
  Create `test/LadybugDB.Tests.Extensions/ResultDataHelperTests.cs`:
  ```csharp
  using System.Collections.Generic;
  using System.Data;
  using System.Linq;
  using LadybugDB.Extensions;
  using LadybugDB.Tests.Extensions.Fakes;
  using Xunit;

  namespace LadybugDB.Tests.Extensions;

  public sealed class ResultDataHelperTests
  {
      private static FakeResultData Sample() => new(
          new[] { "name", "age" },
          new List<object?[]>
          {
              new object?[] { "Alice", 30L },
              new object?[] { "Bob", null },
          });

      [Fact]
      public void ToDictionaries_keys_by_column_name()
      {
          var dicts = ResultHelpers.ToDictionaries(Sample()).ToList();
          Assert.Equal(2, dicts.Count);
          Assert.Equal("Alice", dicts[0]["name"]);
          Assert.Equal(30L, dicts[0]["age"]);
          Assert.Null(dicts[1]["age"]);
      }

      [Fact]
      public void Scalar_reads_first_row_named_column_case_insensitively()
      {
          Assert.Equal("Alice", ResultHelpers.Scalar<string>(Sample(), 0));
      }

      [Fact]
      public void ToJsonArray_emits_array_of_objects()
      {
          string json = ResultHelpers.ToJsonArray(Sample());
          Assert.Contains("\"name\":\"Alice\"", json);
          Assert.Contains("\"age\":30", json);
      }

      [Fact]
      public void ToCsv_quotes_fields_with_separator_and_handles_nulls()
      {
          var data = new FakeResultData(
              new[] { "a", "b" },
              new List<object?[]> { new object?[] { "x,y", null } });
          string csv = ResultHelpers.ToCsv(data, ',');
          string[] lines = csv.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
          Assert.Equal("a,b", lines[0]);
          Assert.Equal("\"x,y\",", lines[1]);
      }

      [Fact]
      public void ToDataTable_maps_nulls_to_dbnull()
      {
          DataTable table = ResultHelpers.ToDataTable(Sample());
          Assert.Equal(2, table.Columns.Count);
          Assert.Equal(2, table.Rows.Count);
          Assert.Equal(System.DBNull.Value, table.Rows[1]["age"]);
      }
  }
  ```
- [ ] Run (expect FAIL — `IResultData`/`ResultHelpers` do not exist):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --filter FullyQualifiedName~ResultDataHelperTests`
  Expected: **build error / FAIL**.
- [ ] **Implement.** Create `src/LadybugDB.Extensions/ResultData.cs`:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Data;
  using System.Globalization;
  using System.Text;
  using System.Text.Json;

  namespace LadybugDB.Extensions;

  /// <summary>
  /// A read-only view over a query result: column names plus a (re-)enumerable row stream. Implemented
  /// by an internal adapter over core's <see cref="QueryResult"/> and by test fakes, so the export
  /// helpers below run with or without the native engine.
  /// </summary>
  public interface IResultData
  {
      /// <summary>Column names in result order.</summary>
      IReadOnlyList<string> Columns { get; }

      /// <summary>Enumerates the rows; each call may stream afresh.</summary>
      IEnumerable<object?[]> EnumerateRows();
  }

  /// <summary>Pure, native-free helpers backing <see cref="QueryResultExtensions"/>.</summary>
  internal static class ResultHelpers
  {
      private static readonly JsonSerializerOptions JsonDefaults = new()
      {
          WriteIndented = false,
      };

      internal static Dictionary<string, int> BuildColumnIndex(IReadOnlyList<string> columns)
      {
          var index = new Dictionary<string, int>(columns.Count, StringComparer.OrdinalIgnoreCase);
          for (int i = 0; i < columns.Count; i++)
          {
              index[columns[i]] = i;
          }

          return index;
      }

      internal static IReadOnlyList<IReadOnlyDictionary<string, object?>> ToDictionaries(IResultData data)
      {
          IReadOnlyList<string> columns = data.Columns;
          var list = new List<IReadOnlyDictionary<string, object?>>();
          foreach (object?[] row in data.EnumerateRows())
          {
              var dict = new Dictionary<string, object?>(columns.Count);
              for (int i = 0; i < columns.Count; i++)
              {
                  dict[columns[i]] = i < row.Length ? row[i] : null;
              }

              list.Add(dict);
          }

          return list;
      }

      internal static IEnumerable<T> Select<T>(IResultData data, Func<IRowAccessor, T> selector)
      {
          Dictionary<string, int> index = BuildColumnIndex(data.Columns);
          foreach (object?[] row in data.EnumerateRows())
          {
              yield return selector(new RowAccessor(row, index));
          }
      }

      internal static T Scalar<T>(IResultData data, int column)
      {
          Dictionary<string, int> index = BuildColumnIndex(data.Columns);
          foreach (object?[] row in data.EnumerateRows())
          {
              return new RowAccessor(row, index).Get<T>(column);
          }

          throw new InvalidOperationException("The query result contains no rows.");
      }

      internal static string ToJson(IResultData data)
      {
          var payload = new Dictionary<string, object?>
          {
              ["columns"] = data.Columns,
              ["rows"] = MaterializeRows(data),
          };

          return JsonSerializer.Serialize(payload, JsonDefaults);
      }

      internal static string ToJsonArray(IResultData data)
          => JsonSerializer.Serialize(ToDictionaries(data), JsonDefaults);

      internal static DataTable ToDataTable(IResultData data)
      {
          var table = new DataTable();
          foreach (string column in data.Columns)
          {
              table.Columns.Add(column, typeof(object));
          }

          foreach (object?[] row in data.EnumerateRows())
          {
              var values = new object?[data.Columns.Count];
              for (int i = 0; i < values.Length; i++)
              {
                  values[i] = (i < row.Length ? row[i] : null) ?? DBNull.Value;
              }

              table.Rows.Add(values);
          }

          return table;
      }

      internal static string ToCsv(IResultData data, char separator)
      {
          var sb = new StringBuilder();
          string sep = separator.ToString();
          AppendCsvLine(sb, data.Columns, sep, separator);
          foreach (object?[] row in data.EnumerateRows())
          {
              var cells = new string[data.Columns.Count];
              for (int i = 0; i < cells.Length; i++)
              {
                  object? value = i < row.Length ? row[i] : null;
                  cells[i] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
              }

              AppendCsvLine(sb, cells, sep, separator);
          }

          return sb.ToString();
      }

      private static List<object?[]> MaterializeRows(IResultData data)
      {
          var rows = new List<object?[]>();
          foreach (object?[] row in data.EnumerateRows())
          {
              rows.Add(row);
          }

          return rows;
      }

      private static void AppendCsvLine(StringBuilder sb, IReadOnlyList<string> cells, string sep, char separator)
      {
          for (int i = 0; i < cells.Count; i++)
          {
              if (i > 0)
              {
                  sb.Append(sep);
              }

              sb.Append(CsvEscape(cells[i], separator));
          }

          sb.Append("\r\n");
      }

      private static string CsvEscape(string value, char separator)
      {
          if (value.IndexOf(separator) >= 0 || value.IndexOf('"') >= 0 ||
              value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
          {
              return "\"" + value.Replace("\"", "\"\"") + "\"";
          }

          return value;
      }
  }
  ```
- [ ] Run (expect PASS):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --filter FullyQualifiedName~ResultDataHelperTests`
  Expected: **5 passed**. (`RowAccessor` is referenced — implement it in Task 5; until then these
  tests fail to compile. Order note: do Task 5 first if you prefer a green intermediate build. The
  steps below assume Task 5 lands the `RowAccessor` type.)
- [ ] Commit: `feat(extensions): add IResultData view + pure export helpers`

> **Ordering:** `ResultHelpers.Select`/`Scalar` reference `RowAccessor`. If you want each task to build
> green in isolation, execute **Task 5 before Task 4's final run**. The plan keeps them adjacent; commit
> Task 5 then re-run Task 4's test command to reach green before committing Task 4.

## TASK 5 — `IRowAccessor` + `RowAccessor` (case-insensitive typed Get/GetOrDefault, `Convert.ChangeType`)

- [ ] **Failing test.** Create `test/LadybugDB.Tests.Extensions/RowAccessorTests.cs`:
  ```csharp
  using System;
  using System.Collections.Generic;
  using LadybugDB.Extensions;
  using Xunit;

  namespace LadybugDB.Tests.Extensions;

  public sealed class RowAccessorTests
  {
      private static RowAccessor Make()
      {
          var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
          {
              ["Name"] = 0,
              ["Age"] = 1,
          };
          return new RowAccessor(new object?[] { "Alice", 30L }, index);
      }

      [Fact]
      public void Get_by_index_converts_to_target_type()
      {
          IRowAccessor row = Make();
          Assert.Equal("Alice", row.Get<string>(0));
          Assert.Equal(30, row.Get<int>(1));   // Convert.ChangeType long -> int
      }

      [Fact]
      public void Get_by_name_is_case_insensitive()
      {
          IRowAccessor row = Make();
          Assert.Equal("Alice", row.Get<string>("name"));
          Assert.Equal(30L, row.Get<long>("AGE"));
      }

      [Fact]
      public void Get_unknown_column_throws_keynotfound()
      {
          IRowAccessor row = Make();
          Assert.Throws<KeyNotFoundException>(() => row.Get<string>("missing"));
      }

      [Fact]
      public void Get_null_value_throws_invalidoperation()
      {
          var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["x"] = 0 };
          IRowAccessor row = new RowAccessor(new object?[] { null }, index);
          Assert.Throws<InvalidOperationException>(() => row.Get<int>(0));
      }

      [Fact]
      public void GetOrDefault_returns_fallback_for_null_or_missing()
      {
          var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["x"] = 0 };
          IRowAccessor row = new RowAccessor(new object?[] { null }, index);
          Assert.Equal(-1, row.GetOrDefault("x", -1));
          Assert.Equal(7, row.GetOrDefault("absent", 7));
      }
  }
  ```
- [ ] Run (expect FAIL — `IRowAccessor`/`RowAccessor` do not exist):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --filter FullyQualifiedName~RowAccessorTests`
  Expected: **build error / FAIL**.
- [ ] **Implement.** Create `src/LadybugDB.Extensions/IRowAccessor.cs`:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Globalization;

  namespace LadybugDB.Extensions;

  /// <summary>Typed, case-insensitive accessor over a single materialized result row.</summary>
  public interface IRowAccessor
  {
      /// <summary>Reads the value at <paramref name="i"/>, converted to <typeparamref name="T"/>.</summary>
      T Get<T>(int i);

      /// <summary>Reads the named column (case-insensitive), converted to <typeparamref name="T"/>.</summary>
      T Get<T>(string name);

      /// <summary>Reads the named column, or returns <paramref name="fallback"/> when null/missing.</summary>
      T GetOrDefault<T>(string name, T fallback);
  }

  /// <summary>In-memory <see cref="IRowAccessor"/> over an <c>object?[]</c> row and a shared column index.</summary>
  internal sealed class RowAccessor : IRowAccessor
  {
      private readonly object?[] _row;
      private readonly IReadOnlyDictionary<string, int> _columnIndex;

      public RowAccessor(object?[] row, IReadOnlyDictionary<string, int> columnIndex)
      {
          _row = row;
          _columnIndex = columnIndex;
      }

      public T Get<T>(int i)
      {
          if (i < 0 || i >= _row.Length)
          {
              throw new ArgumentOutOfRangeException(nameof(i));
          }

          object? value = _row[i];
          if (value is null)
          {
              throw new InvalidOperationException(
                  $"Value at column {i} is null. Use GetOrDefault for nullable access.");
          }

          return Convert<T>(value);
      }

      public T Get<T>(string name)
      {
          if (!_columnIndex.TryGetValue(name, out int i))
          {
              throw new KeyNotFoundException(
                  $"Column '{name}' was not found. Available columns: {string.Join(", ", _columnIndex.Keys)}.");
          }

          return Get<T>(i);
      }

      public T GetOrDefault<T>(string name, T fallback)
      {
          if (!_columnIndex.TryGetValue(name, out int i) || i < 0 || i >= _row.Length)
          {
              return fallback;
          }

          object? value = _row[i];
          return value is null ? fallback : Convert<T>(value);
      }

      private static T Convert<T>(object value)
      {
          if (value is T typed)
          {
              return typed;
          }

          Type target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
          return (T)System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
      }
  }
  ```
- [ ] Run (expect PASS):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --filter FullyQualifiedName~RowAccessorTests`
  Expected: **5 passed**.
- [ ] Re-run Task 4's command now that `RowAccessor` exists (expect PASS):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --filter FullyQualifiedName~ResultDataHelperTests`
  Expected: **5 passed**.
- [ ] Commit: `feat(extensions): add IRowAccessor + RowAccessor with case-insensitive typed access`

## TASK 6 — `LadybugResilienceOptions` + `ResilientLadybugExecutor` (timeout, retry, circuit breaker; no Polly)

- [ ] **Failing test.** Create `test/LadybugDB.Tests.Extensions/ResilientLadybugExecutorTests.cs`:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Threading;
  using System.Threading.Tasks;
  using LadybugDB.Extensions;
  using LadybugDB.Tests.Extensions.Fakes;
  using Microsoft.Extensions.Options;
  using Xunit;

  namespace LadybugDB.Tests.Extensions;

  public sealed class ResilientLadybugExecutorTests
  {
      private static ResilientLadybugExecutor Wrap(ILadybugExecutor inner, LadybugResilienceOptions options)
          => new(inner, Options.Create(options));

      [Fact]
      public async Task Passes_through_on_success()
      {
          var inner = FakeLadybugExecutor.Returning(LadybugExecutionResult.Empty);
          var sut = Wrap(inner, new LadybugResilienceOptions { EnableRetries = false });

          LadybugExecutionResult result = await sut.ExecuteAsync("RETURN 1");

          Assert.Same(LadybugExecutionResult.Empty, result);
          Assert.Equal(1, inner.ExecuteCount);
      }

      [Fact]
      public async Task Retries_transient_failures_then_succeeds()
      {
          int calls = 0;
          var inner = new FakeLadybugExecutor((_, _) =>
          {
              calls++;
              if (calls < 2)
              {
                  throw new TimeoutException("transient");
              }

              return Task.FromResult(LadybugExecutionResult.Empty);
          });
          var sut = Wrap(inner, new LadybugResilienceOptions
          {
              MaxRetryAttempts = 2,
              RetryDelay = TimeSpan.Zero,
          });

          LadybugExecutionResult result = await sut.ExecuteAsync("RETURN 1");

          Assert.Same(LadybugExecutionResult.Empty, result);
          Assert.Equal(2, calls);
      }

      [Fact]
      public async Task Non_transient_failure_is_not_retried()
      {
          int calls = 0;
          var inner = new FakeLadybugExecutor((_, _) =>
          {
              calls++;
              throw new InvalidOperationException("hard failure");
          });
          var sut = Wrap(inner, new LadybugResilienceOptions { MaxRetryAttempts = 5, RetryDelay = TimeSpan.Zero });

          await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync("RETURN 1"));
          Assert.Equal(1, calls);
      }

      [Fact]
      public async Task Timeout_surfaces_as_TimeoutException()
      {
          var inner = new FakeLadybugExecutor(async (_, ct) =>
          {
              await Task.Delay(Timeout.Infinite, ct);
              return LadybugExecutionResult.Empty;
          });
          var sut = Wrap(inner, new LadybugResilienceOptions
          {
              Timeout = TimeSpan.FromMilliseconds(20),
              EnableRetries = false,
          });

          await Assert.ThrowsAsync<TimeoutException>(() => sut.ExecuteAsync("RETURN 1"));
      }

      [Fact]
      public async Task Circuit_opens_after_threshold_then_rejects_fast()
      {
          var inner = new FakeLadybugExecutor((_, _) => throw new TimeoutException("boom"));
          var sut = Wrap(inner, new LadybugResilienceOptions
          {
              EnableRetries = false,
              CircuitBreakerFailureThreshold = 2,
              CircuitBreakerBreakDuration = TimeSpan.FromMinutes(5),
          });

          await Assert.ThrowsAsync<TimeoutException>(() => sut.ExecuteAsync("RETURN 1"));
          await Assert.ThrowsAsync<TimeoutException>(() => sut.ExecuteAsync("RETURN 1"));

          // Circuit now open: rejects without invoking inner.
          int before = inner.ExecuteCount;
          await Assert.ThrowsAsync<LadybugCircuitOpenException>(() => sut.ExecuteAsync("RETURN 1"));
          Assert.Equal(before, inner.ExecuteCount);
      }

      [Fact]
      public async Task External_cancellation_is_not_wrapped_as_timeout()
      {
          using var cts = new CancellationTokenSource();
          var inner = new FakeLadybugExecutor(async (_, ct) =>
          {
              await Task.Delay(Timeout.Infinite, ct);
              return LadybugExecutionResult.Empty;
          });
          var sut = Wrap(inner, new LadybugResilienceOptions { EnableRetries = false });

          cts.CancelAfter(20);
          await Assert.ThrowsAsync<TaskCanceledException>(() => sut.ExecuteAsync("RETURN 1", cts.Token));
      }
  }
  ```
- [ ] Run (expect FAIL — `LadybugResilienceOptions`/`ResilientLadybugExecutor`/`LadybugCircuitOpenException` missing):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --filter FullyQualifiedName~ResilientLadybugExecutorTests`
  Expected: **build error / FAIL**.
- [ ] **Implement options.** Create `src/LadybugDB.Extensions/LadybugResilienceOptions.cs`:
  ```csharp
  using System;

  namespace LadybugDB.Extensions;

  /// <summary>Settings for <see cref="ResilientLadybugExecutor"/> (timeout, retry, circuit breaker).</summary>
  public sealed class LadybugResilienceOptions
  {
      /// <summary>Maximum wall-clock time for a single execute attempt.</summary>
      public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

      /// <summary>Whether transient failures are retried.</summary>
      public bool EnableRetries { get; set; } = true;

      /// <summary>Number of retries (in addition to the first attempt) for transient failures.</summary>
      public int MaxRetryAttempts { get; set; } = 2;

      /// <summary>Delay between retry attempts.</summary>
      public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);

      /// <summary>Consecutive failures before the circuit opens.</summary>
      public int CircuitBreakerFailureThreshold { get; set; } = 5;

      /// <summary>How long the circuit stays open before allowing a probe.</summary>
      public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(30);
  }
  ```
- [ ] Add the circuit exception to `src/LadybugDB.Extensions/ResilientLadybugExecutor.cs` (see below).
  Create `src/LadybugDB.Extensions/ResilientLadybugExecutor.cs`:
  ```csharp
  using System;
  using System.Threading;
  using System.Threading.Tasks;
  using Microsoft.Extensions.Options;

  namespace LadybugDB.Extensions;

  /// <summary>Thrown when the resilience circuit breaker is open and short-circuits an execute call.</summary>
  public sealed class LadybugCircuitOpenException : InvalidOperationException
  {
      public LadybugCircuitOpenException(string message)
          : base(message)
      {
      }
  }

  /// <summary>
  /// Decorates an <see cref="ILadybugExecutor"/> with a per-call timeout (linked CTS), transient-aware
  /// retry with delay, and a lock-guarded consecutive-failure circuit breaker. No Polly dependency.
  /// </summary>
  public sealed class ResilientLadybugExecutor : ILadybugExecutor
  {
      private readonly ILadybugExecutor _inner;
      private readonly LadybugResilienceOptions _options;
      private readonly object _sync = new();
      private int _consecutiveFailures;
      private DateTimeOffset? _openUntil;

      public ResilientLadybugExecutor(ILadybugExecutor inner, IOptions<LadybugResilienceOptions> options)
      {
          _inner = inner ?? throw new ArgumentNullException(nameof(inner));
          _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
      }

      public string Name => _inner.Name;

      public async Task<LadybugExecutionResult> ExecuteAsync(string cypher, CancellationToken cancellationToken = default)
      {
          ThrowIfCircuitOpen();

          int maxAttempts = Math.Max(1, _options.MaxRetryAttempts + 1);
          Exception? lastError = null;

          for (int attempt = 1; attempt <= maxAttempts; attempt++)
          {
              using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
              timeoutCts.CancelAfter(_options.Timeout);

              try
              {
                  LadybugExecutionResult result = await _inner.ExecuteAsync(cypher, timeoutCts.Token)
                      .ConfigureAwait(false);
                  OnSuccess();
                  return result;
              }
              catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
              {
                  // Caller-driven cancellation: propagate as-is, do not count as a failure.
                  throw;
              }
              catch (OperationCanceledException ex)
              {
                  lastError = new TimeoutException(
                      $"The Ladybug query timed out after {_options.Timeout.TotalSeconds:0.###}s.", ex);
                  OnFailure();
              }
              catch (Exception ex)
              {
                  lastError = ex;
                  OnFailure();
                  if (!IsTransient(ex))
                  {
                      throw;
                  }
              }

              if (!_options.EnableRetries || attempt == maxAttempts)
              {
                  break;
              }

              await Task.Delay(_options.RetryDelay, cancellationToken).ConfigureAwait(false);
          }

          throw lastError ?? new InvalidOperationException("Resilient execution failed without an error.");
      }

      private static bool IsTransient(Exception ex)
          => ex is TimeoutException or OperationCanceledException;

      private void ThrowIfCircuitOpen()
      {
          lock (_sync)
          {
              if (_openUntil is null)
              {
                  return;
              }

              if (DateTimeOffset.UtcNow >= _openUntil.Value)
              {
                  _openUntil = null;
                  _consecutiveFailures = 0;
                  return;
              }

              throw new LadybugCircuitOpenException(
                  $"The Ladybug circuit breaker is open until {_openUntil.Value:O}.");
          }
      }

      private void OnSuccess()
      {
          lock (_sync)
          {
              _consecutiveFailures = 0;
              _openUntil = null;
          }
      }

      private void OnFailure()
      {
          lock (_sync)
          {
              _consecutiveFailures++;
              if (_consecutiveFailures >= Math.Max(1, _options.CircuitBreakerFailureThreshold))
              {
                  _openUntil = DateTimeOffset.UtcNow.Add(_options.CircuitBreakerBreakDuration);
              }
          }
      }
  }
  ```
- [ ] Run (expect PASS):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --filter FullyQualifiedName~ResilientLadybugExecutorTests`
  Expected: **6 passed**.
- [ ] Commit: `feat(extensions): add ResilientLadybugExecutor (timeout/retry/circuit-breaker, no Polly)`

## TASK 7 — `LadybugHealthCheck` (IHealthCheck running a probe query)

- [ ] **Failing test.** Create `test/LadybugDB.Tests.Extensions/LadybugHealthCheckTests.cs`:
  ```csharp
  using System;
  using System.Threading.Tasks;
  using LadybugDB.Extensions;
  using LadybugDB.Tests.Extensions.Fakes;
  using Microsoft.Extensions.Diagnostics.HealthChecks;
  using Xunit;

  namespace LadybugDB.Tests.Extensions;

  public sealed class LadybugHealthCheckTests
  {
      [Fact]
      public async Task Healthy_when_probe_query_succeeds()
      {
          var executor = FakeLadybugExecutor.Returning(
              new LadybugExecutionResult(new[] { "x" }, new[] { new object?[] { 1L } }));
          var check = new LadybugHealthCheck(executor);

          HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

          Assert.Equal(HealthStatus.Healthy, result.Status);
          Assert.Equal(1, executor.ExecuteCount);
      }

      [Fact]
      public async Task Unhealthy_when_probe_query_throws()
      {
          var executor = new FakeLadybugExecutor((_, _) => throw new InvalidOperationException("down"));
          var check = new LadybugHealthCheck(executor);

          HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

          Assert.Equal(HealthStatus.Unhealthy, result.Status);
          Assert.NotNull(result.Exception);
      }

      [Fact]
      public async Task Uses_the_configured_probe_query()
      {
          string? seen = null;
          var executor = new FakeLadybugExecutor((q, _) =>
          {
              seen = q;
              return Task.FromResult(LadybugExecutionResult.Empty);
          });
          var check = new LadybugHealthCheck(executor, probeQuery: "RETURN 42");

          await check.CheckHealthAsync(new HealthCheckContext());

          Assert.Equal("RETURN 42", seen);
      }
  }
  ```
- [ ] Run (expect FAIL — `LadybugHealthCheck` missing):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --filter FullyQualifiedName~LadybugHealthCheckTests`
  Expected: **build error / FAIL**.
- [ ] **Implement.** Create `src/LadybugDB.Extensions/LadybugHealthCheck.cs`:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Threading;
  using System.Threading.Tasks;
  using Microsoft.Extensions.Diagnostics.HealthChecks;

  namespace LadybugDB.Extensions;

  /// <summary>
  /// An <see cref="IHealthCheck"/> that runs a cheap probe query against the Ladybug executor and
  /// reports healthy/unhealthy accordingly.
  /// </summary>
  public sealed class LadybugHealthCheck : IHealthCheck
  {
      private readonly ILadybugExecutor _executor;
      private readonly string _probeQuery;

      public LadybugHealthCheck(ILadybugExecutor executor, string probeQuery = "RETURN 1")
      {
          _executor = executor ?? throw new ArgumentNullException(nameof(executor));
          _probeQuery = probeQuery ?? throw new ArgumentNullException(nameof(probeQuery));
      }

      public async Task<HealthCheckResult> CheckHealthAsync(
          HealthCheckContext context,
          CancellationToken cancellationToken = default)
      {
          try
          {
              LadybugExecutionResult result =
                  await _executor.ExecuteAsync(_probeQuery, cancellationToken).ConfigureAwait(false);

              var data = new Dictionary<string, object>
              {
                  ["executor"] = _executor.Name,
                  ["columns"] = result.Columns.Count,
              };

              return HealthCheckResult.Healthy($"Ladybug executor '{_executor.Name}' is healthy.", data);
          }
          catch (Exception ex)
          {
              return HealthCheckResult.Unhealthy(
                  $"Ladybug executor '{_executor.Name}' health probe failed.", ex);
          }
      }
  }
  ```
- [ ] Run (expect PASS):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --filter FullyQualifiedName~LadybugHealthCheckTests`
  Expected: **3 passed**.
- [ ] Commit: `feat(extensions): add LadybugHealthCheck probe`

## TASK 8 — `ExecutorStreamingExtensions.StreamAsync` (IAsyncEnumerable over the seam)

- [ ] **Failing test.** Create `test/LadybugDB.Tests.Extensions/StreamingTests.cs`:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Threading;
  using System.Threading.Tasks;
  using LadybugDB.Extensions;
  using LadybugDB.Tests.Extensions.Fakes;
  using Xunit;

  namespace LadybugDB.Tests.Extensions;

  public sealed class StreamingTests
  {
      [Fact]
      public async Task StreamAsync_yields_one_row_accessor_per_row()
      {
          var executor = FakeLadybugExecutor.Returning(new LadybugExecutionResult(
              new[] { "name" },
              new List<object?[]>
              {
                  new object?[] { "Alice" },
                  new object?[] { "Bob" },
              }));

          var names = new List<string>();
          await foreach (IRowAccessor row in executor.StreamAsync("MATCH (p) RETURN p.name"))
          {
              names.Add(row.Get<string>("name"));
          }

          Assert.Equal(new[] { "Alice", "Bob" }, names);
      }

      [Fact]
      public async Task StreamAsync_with_selector_projects_each_row()
      {
          var executor = FakeLadybugExecutor.Returning(new LadybugExecutionResult(
              new[] { "n" },
              new List<object?[]> { new object?[] { 1L }, new object?[] { 2L } }));

          var sum = 0L;
          await foreach (long n in executor.StreamAsync("RETURN 1", r => r.Get<long>("n")))
          {
              sum += n;
          }

          Assert.Equal(3L, sum);
      }

      [Fact]
      public async Task StreamAsync_honors_cancellation_before_enumeration()
      {
          using var cts = new CancellationTokenSource();
          cts.Cancel();
          var executor = FakeLadybugExecutor.Returning(LadybugExecutionResult.Empty);

          await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
          {
              await foreach (var _ in executor.StreamAsync("RETURN 1", cts.Token))
              {
              }
          });
      }
  }
  ```
- [ ] Run (expect FAIL — `StreamAsync` missing):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --filter FullyQualifiedName~StreamingTests`
  Expected: **build error / FAIL**.
- [ ] **Implement.** Create `src/LadybugDB.Extensions/ExecutorStreamingExtensions.cs`:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Runtime.CompilerServices;
  using System.Threading;
  using System.Threading.Tasks;

  namespace LadybugDB.Extensions;

  /// <summary>
  /// <see cref="IAsyncEnumerable{T}"/> sugar over <see cref="ILadybugExecutor"/>. The executor returns a
  /// materialized result (the core async path streams under the hood via WS-D's
  /// <c>Connection.StreamAsync</c>); these helpers expose rows one-at-a-time as <see cref="IRowAccessor"/>.
  /// </summary>
  public static class ExecutorStreamingExtensions
  {
      /// <summary>Executes <paramref name="cypher"/> and yields one <see cref="IRowAccessor"/> per row.</summary>
      public static async IAsyncEnumerable<IRowAccessor> StreamAsync(
          this ILadybugExecutor executor,
          string cypher,
          [EnumeratorCancellation] CancellationToken cancellationToken = default)
      {
          if (executor is null)
          {
              throw new ArgumentNullException(nameof(executor));
          }

          cancellationToken.ThrowIfCancellationRequested();
          LadybugExecutionResult result =
              await executor.ExecuteAsync(cypher, cancellationToken).ConfigureAwait(false);
          Dictionary<string, int> index = ResultHelpers.BuildColumnIndex(result.Columns);

          foreach (object?[] row in result.Rows)
          {
              cancellationToken.ThrowIfCancellationRequested();
              yield return new RowAccessor(row, index);
          }
      }

      /// <summary>Executes <paramref name="cypher"/> and yields each row projected by <paramref name="selector"/>.</summary>
      public static async IAsyncEnumerable<T> StreamAsync<T>(
          this ILadybugExecutor executor,
          string cypher,
          Func<IRowAccessor, T> selector,
          [EnumeratorCancellation] CancellationToken cancellationToken = default)
      {
          if (executor is null)
          {
              throw new ArgumentNullException(nameof(executor));
          }

          if (selector is null)
          {
              throw new ArgumentNullException(nameof(selector));
          }

          await foreach (IRowAccessor row in executor.StreamAsync(cypher, cancellationToken).ConfigureAwait(false))
          {
              yield return selector(row);
          }
      }
  }
  ```
- [ ] Run (expect PASS):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --filter FullyQualifiedName~StreamingTests`
  Expected: **3 passed**.
- [ ] Commit: `feat(extensions): add IAsyncEnumerable StreamAsync over the executor seam`

## TASK 9 — `QueryResultExtensions` (pinned §4.8 surface) + real `IResultData` adapter over core `QueryResult`

This task wires the pinned public methods on `this QueryResult r` to the pure helpers via a real adapter.
The adapter (`QueryResultData`) reads core's public `QueryResult.ColumnNames` and `QueryResult.Rows()`.
The pure helpers are already covered (Task 4); the only native-touching path is the `QueryResult`
overloads, tested via a `SkippableFact` smoke test.

- [ ] **Failing test (logic, ungated).** Create
  `test/LadybugDB.Tests.Extensions/QueryResultExtensionsLogicTests.cs` — this asserts the pinned
  signatures exist by calling them through reflection-free generic wrappers on the fake data path is not
  possible (they take `QueryResult`), so instead assert the helper parity that the public methods are
  expected to produce, exercising the same `ResultHelpers` the public methods call:
  ```csharp
  using System.Collections.Generic;
  using System.Linq;
  using LadybugDB.Extensions;
  using LadybugDB.Tests.Extensions.Fakes;
  using Xunit;

  namespace LadybugDB.Tests.Extensions;

  // Guards the contract the public QueryResultExtensions methods delegate to. The QueryResult-typed
  // overloads themselves are smoke-tested under native in QueryResultExtensionsNativeTests.
  public sealed class QueryResultExtensionsLogicTests
  {
      private static FakeResultData Sample() => new(
          new[] { "name", "age" },
          new List<object?[]> { new object?[] { "Alice", 30L } });

      [Fact]
      public void Select_projects_through_row_accessor()
      {
          var names = ResultHelpers.Select(Sample(), r => r.Get<string>("name")).ToList();
          Assert.Equal(new[] { "Alice" }, names);
      }

      [Fact]
      public void Scalar_default_column_is_zero()
      {
          Assert.Equal("Alice", ResultHelpers.Scalar<string>(Sample(), 0));
      }

      [Fact]
      public void ToJson_includes_columns_and_rows()
      {
          string json = ResultHelpers.ToJson(Sample());
          Assert.Contains("\"columns\"", json);
          Assert.Contains("\"rows\"", json);
          Assert.Contains("Alice", json);
      }
  }
  ```
- [ ] Run (expect PASS already — `ResultHelpers` exists from Task 4/5; this pins the delegate contract):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --filter FullyQualifiedName~QueryResultExtensionsLogicTests`
  Expected: **3 passed**.
- [ ] **Implement the adapter + pinned public surface.** Append the adapter to
  `src/LadybugDB.Extensions/ResultData.cs` (add this class at the end of the file, inside the namespace):
  ```csharp
  /// <summary>Real <see cref="IResultData"/> over a core <see cref="QueryResult"/>, eagerly materialized.</summary>
  internal sealed class QueryResultData : IResultData
  {
      private readonly IReadOnlyList<object?[]> _rows;

      public QueryResultData(QueryResult result)
      {
          Columns = result.ColumnNames;
          var rows = new List<object?[]>();
          foreach (object?[] row in result.Rows())
          {
              rows.Add(row);
          }

          _rows = rows;
      }

      public IReadOnlyList<string> Columns { get; }

      public IEnumerable<object?[]> EnumerateRows() => _rows;
  }
  ```
  Add `using LadybugDB;` at the top of `ResultData.cs` so `QueryResult` resolves.
  Create `src/LadybugDB.Extensions/QueryResultExtensions.cs`:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Data;
  using LadybugDB;

  namespace LadybugDB.Extensions;

  /// <summary>
  /// Export and projection helpers over a core <see cref="QueryResult"/>: dictionaries, LINQ projection,
  /// scalar extraction, JSON, <see cref="DataTable"/>, and CSV. Each method eagerly materializes the
  /// result's rows once and delegates to the shared, native-free helpers.
  /// </summary>
  public static class QueryResultExtensions
  {
      /// <summary>Projects each row into a dictionary keyed by column name.</summary>
      public static IReadOnlyList<IReadOnlyDictionary<string, object?>> ToDictionaries(this QueryResult r)
          => ResultHelpers.ToDictionaries(View(r));

      /// <summary>Projects each row through <paramref name="selector"/> with an <see cref="IRowAccessor"/>.</summary>
      public static IEnumerable<T> Select<T>(this QueryResult r, Func<IRowAccessor, T> selector)
          => ResultHelpers.Select(View(r), selector ?? throw new ArgumentNullException(nameof(selector)));

      /// <summary>Reads the first row's value at <paramref name="column"/>, converted to <typeparamref name="T"/>.</summary>
      public static T Scalar<T>(this QueryResult r, int column = 0)
          => ResultHelpers.Scalar<T>(View(r), column);

      /// <summary>Serializes the whole result (columns + rows) to JSON.</summary>
      public static string ToJson(this QueryResult r)
          => ResultHelpers.ToJson(View(r));

      /// <summary>Serializes the rows as a JSON array of column-keyed objects.</summary>
      public static string ToJsonArray(this QueryResult r)
          => ResultHelpers.ToJsonArray(View(r));

      /// <summary>Converts the result to a <see cref="DataTable"/> (nulls become <see cref="DBNull"/>).</summary>
      public static DataTable ToDataTable(this QueryResult r)
          => ResultHelpers.ToDataTable(View(r));

      /// <summary>Converts the result to a CSV string using <paramref name="separator"/>.</summary>
      public static string ToCsv(this QueryResult r, char separator = ',')
          => ResultHelpers.ToCsv(View(r), separator);

      private static IResultData View(QueryResult r)
          => new QueryResultData(r ?? throw new ArgumentNullException(nameof(r)));
  }
  ```
- [ ] **Native smoke test (gated).** Create
  `test/LadybugDB.Tests.Extensions/QueryResultExtensionsNativeTests.cs`:
  ```csharp
  using System.Collections.Generic;
  using System.Data;
  using System.Linq;
  using LadybugDB;
  using LadybugDB.Extensions;
  using Xunit;

  namespace LadybugDB.Tests.Extensions;

  public sealed class QueryResultExtensionsNativeTests
  {
      private static bool NativeAvailable { get; } = Probe();

      [SkippableFact]
      public void ToDictionaries_over_real_query_result()
      {
          Skip.IfNot(NativeAvailable, "Native Ladybug library is not available.");

          using var db = new Database(string.Empty);
          using var conn = new Connection(db);
          using QueryResult result = conn.Query("RETURN 1 AS one, 'hi' AS greeting");

          IReadOnlyList<IReadOnlyDictionary<string, object?>> dicts = result.ToDictionaries();

          Assert.Single(dicts);
          Assert.Equal(1L, dicts[0]["one"]);
          Assert.Equal("hi", dicts[0]["greeting"]);
      }

      [SkippableFact]
      public void Scalar_and_datatable_over_real_query_result()
      {
          Skip.IfNot(NativeAvailable, "Native Ladybug library is not available.");

          using var db = new Database(string.Empty);
          using var conn = new Connection(db);
          using QueryResult result = conn.Query("RETURN 7 AS n");

          Assert.Equal(7, result.Scalar<int>());
          DataTable table = result.ToDataTable();
          Assert.Equal(1, table.Rows.Count);
      }

      private static bool Probe()
      {
          try
          {
              _ = LadybugVersion.StorageVersion;
              return true;
          }
          catch (System.DllNotFoundException) { return false; }
          catch (System.TypeInitializationException) { return false; }
          catch (System.EntryPointNotFoundException) { return false; }
      }
  }
  ```
  > The Extensions test project has no native-staging target; under managed-only CI these SkippableFacts
  > skip. The native-gated run happens in the main `LadybugDB.Tests` project's environment; this smoke
  > test is here for completeness and skips cleanly when native is absent.
- [ ] Run (expect PASS / skips for native, pass for logic):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --filter "FullyQualifiedName~QueryResultExtensions"`
  Expected: logic tests **pass**, native tests **skipped** (managed-only).
- [ ] Commit: `feat(extensions): add pinned QueryResultExtensions over core QueryResult`

## TASK 10 — DI entry points (`AddLadybug`/`AddLadybugHealthCheck`/`AddLadybugResilience`) + real connection executor

- [ ] **Failing test.** Append to `test/LadybugDB.Tests.Extensions/ServiceCollectionTests.cs`:
  ```csharp
  using System;
  using LadybugDB.Extensions;
  using LadybugDB.Tests.Extensions.Fakes;
  using Microsoft.Extensions.DependencyInjection;
  using Microsoft.Extensions.Diagnostics.HealthChecks;
  using Microsoft.Extensions.Options;
  using Xunit;

  // (keep the existing namespace + class; add these methods to ServiceCollectionTests)

      [Fact]
      public void AddLadybug_configures_options_and_registers_executor()
      {
          var services = new ServiceCollection();
          services.AddSingleton<ILadybugExecutor>(FakeLadybugExecutor.Returning(LadybugExecutionResult.Empty));
          services.AddLadybug(o => o.DatabasePath = "/tmp/db");

          using ServiceProvider provider = services.BuildServiceProvider();
          var options = provider.GetRequiredService<IOptions<LadybugOptions>>().Value;
          Assert.Equal("/tmp/db", options.DatabasePath);
          Assert.NotNull(provider.GetRequiredService<ILadybugExecutor>());
      }

      [Fact]
      public void AddLadybugResilience_wraps_the_registered_executor()
      {
          var services = new ServiceCollection();
          services.AddSingleton<ILadybugExecutor>(FakeLadybugExecutor.Returning(LadybugExecutionResult.Empty));
          services.AddLadybugResilience(o => o.MaxRetryAttempts = 1);

          using ServiceProvider provider = services.BuildServiceProvider();
          ILadybugExecutor executor = provider.GetRequiredService<ILadybugExecutor>();
          Assert.IsType<ResilientLadybugExecutor>(executor);
      }

      [Fact]
      public void AddLadybugHealthCheck_registers_a_named_check()
      {
          var services = new ServiceCollection();
          services.AddSingleton<ILadybugExecutor>(FakeLadybugExecutor.Returning(LadybugExecutionResult.Empty));
          services.AddLadybugHealthCheck("graph");

          using ServiceProvider provider = services.BuildServiceProvider();
          var options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
          Assert.Contains(options.Registrations, r => r.Name == "graph");
      }
  ```
- [ ] Run (expect FAIL — DI methods missing):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --filter FullyQualifiedName~ServiceCollectionTests`
  Expected: **build error / FAIL** (sanity test from Task 2 still passes; new ones fail).
- [ ] **Implement the real connection executor.** Create
  `src/LadybugDB.Extensions/LadybugConnectionExecutor.cs`:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Threading;
  using System.Threading.Tasks;
  using LadybugDB;

  namespace LadybugDB.Extensions;

  /// <summary>
  /// Production <see cref="ILadybugExecutor"/> over a core <see cref="Connection"/>. Uses WS-D's async
  /// surface (<c>Connection.QueryAsync</c>) and eagerly materializes each result so callers receive a
  /// detached, thread-safe snapshot.
  /// </summary>
  public sealed class LadybugConnectionExecutor : ILadybugExecutor
  {
      private readonly Connection _connection;

      public LadybugConnectionExecutor(Connection connection, string name = "ladybug")
      {
          _connection = connection ?? throw new ArgumentNullException(nameof(connection));
          Name = name ?? throw new ArgumentNullException(nameof(name));
      }

      public string Name { get; }

      public async Task<LadybugExecutionResult> ExecuteAsync(
          string cypher,
          CancellationToken cancellationToken = default)
      {
          using QueryResult result = await _connection.QueryAsync(cypher, cancellationToken).ConfigureAwait(false);

          IReadOnlyList<string> columns = result.ColumnNames;
          var rows = new List<object?[]>();
          foreach (object?[] row in result.Rows())
          {
              rows.Add(row);
          }

          return new LadybugExecutionResult(columns, rows);
      }
  }
  ```
  > **WS-D coupling:** this is the only file that calls `Connection.QueryAsync`. If WS-D's partial has
  > not merged, this file fails to compile while the rest of the package builds; gate the merge order so
  > D precedes G (per high-level-plan §5 merge order). The fakes keep every other test green.
- [ ] **Implement DI.** Create `src/LadybugDB.Extensions/LadybugServiceCollectionExtensions.cs`:
  ```csharp
  using System;
  using Microsoft.Extensions.DependencyInjection;
  using Microsoft.Extensions.Diagnostics.HealthChecks;
  using Microsoft.Extensions.Options;

  namespace LadybugDB.Extensions;

  /// <summary>Dependency-injection entry points for the Ladybug extensions.</summary>
  public static class LadybugServiceCollectionExtensions
  {
      /// <summary>
      /// Registers <see cref="LadybugOptions"/> from <paramref name="cfg"/>. The caller is expected to
      /// register an <see cref="ILadybugExecutor"/> (e.g. a <see cref="LadybugConnectionExecutor"/> over
      /// a connection it owns); this method wires options and is the anchor other Add* calls build on.
      /// </summary>
      public static IServiceCollection AddLadybug(this IServiceCollection s, Action<LadybugOptions> cfg)
      {
          if (s is null)
          {
              throw new ArgumentNullException(nameof(s));
          }

          if (cfg is null)
          {
              throw new ArgumentNullException(nameof(cfg));
          }

          s.Configure(cfg);
          return s;
      }

      /// <summary>Registers a <see cref="LadybugHealthCheck"/> under <paramref name="name"/>.</summary>
      public static IServiceCollection AddLadybugHealthCheck(this IServiceCollection s, string name = "ladybug")
      {
          if (s is null)
          {
              throw new ArgumentNullException(nameof(s));
          }

          s.AddHealthChecks().Add(new HealthCheckRegistration(
              name,
              provider => new LadybugHealthCheck(provider.GetRequiredService<ILadybugExecutor>()),
              failureStatus: null,
              tags: null));

          return s;
      }

      /// <summary>
      /// Decorates the registered <see cref="ILadybugExecutor"/> with a
      /// <see cref="ResilientLadybugExecutor"/> (timeout/retry/circuit-breaker).
      /// </summary>
      public static IServiceCollection AddLadybugResilience(
          this IServiceCollection s,
          Action<LadybugResilienceOptions>? cfg = null)
      {
          if (s is null)
          {
              throw new ArgumentNullException(nameof(s));
          }

          if (cfg is null)
          {
              s.AddOptions<LadybugResilienceOptions>();
          }
          else
          {
              s.Configure(cfg);
          }

          // Capture the inner executor registered before this call, then replace the public
          // ILadybugExecutor with the resilient decorator.
          ServiceDescriptor? inner = null;
          for (int i = s.Count - 1; i >= 0; i--)
          {
              if (s[i].ServiceType == typeof(ILadybugExecutor))
              {
                  inner = s[i];
                  break;
              }
          }

          if (inner is null)
          {
              throw new InvalidOperationException(
                  "AddLadybugResilience requires an ILadybugExecutor to be registered first.");
          }

          s.AddSingleton(provider =>
          {
              ILadybugExecutor innerExecutor = Materialize(provider, inner);
              IOptions<LadybugResilienceOptions> options =
                  provider.GetRequiredService<IOptions<LadybugResilienceOptions>>();
              return new ResilientLadybugExecutor(innerExecutor, options);
          });

          s.AddSingleton<ILadybugExecutor>(provider => provider.GetRequiredService<ResilientLadybugExecutor>());
          return s;
      }

      private static ILadybugExecutor Materialize(IServiceProvider provider, ServiceDescriptor descriptor)
      {
          if (descriptor.ImplementationInstance is ILadybugExecutor instance)
          {
              return instance;
          }

          if (descriptor.ImplementationFactory is { } factory)
          {
              return (ILadybugExecutor)factory(provider);
          }

          if (descriptor.ImplementationType is { } type)
          {
              return (ILadybugExecutor)ActivatorUtilities.CreateInstance(provider, type);
          }

          throw new InvalidOperationException("The registered ILadybugExecutor cannot be resolved.");
      }
  }
  ```
  > Design note: the final `AddSingleton<ILadybugExecutor>` re-registration wins as the last descriptor,
  > so `GetRequiredService<ILadybugExecutor>()` returns the decorator while the inner executor is still
  > reachable through the captured descriptor inside the factory. This avoids a circular self-resolve.
- [ ] Run (expect PASS):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --filter FullyQualifiedName~ServiceCollectionTests`
  Expected: **4 passed**.
- [ ] Commit: `feat(extensions): add DI entry points + LadybugConnectionExecutor`

## TASK 11 — Full build (both TFMs) + full test sweep + slnx integration

- [ ] Build the whole solution on both TFMs:
  `dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug`
  Expected: **PASS** — `LadybugDB.Extensions` compiles for `net10.0` and `netstandard2.0`; the test
  project compiles for `net10.0`.
- [ ] Run the entire Extensions test suite:
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug`
  Expected: all logic tests **pass**, native smoke tests **skip** (managed-only). No failures.
- [ ] Confirm the core suite still builds/passes (no regressions from the `InternalsVisibleTo` addition):
  `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter FullyQualifiedName~StructLayoutTests`
  Expected: **PASS** (ABI guard unaffected).
- [ ] Commit: `chore(extensions): wire projects into solution; green build + tests`

## TASK 12 — Packaging metadata + family verification note

- [ ] Add NuGet metadata to `src/LadybugDB.Extensions/LadybugDB.Extensions.csproj` (mirror core's
  `nuget-package.props` style but inline, since Extensions is a sibling package):
  ```xml
    <PropertyGroup>
      <Version Condition="'$(Version)' == ''">$([System.IO.File]::ReadAllText('$(CSharpDir)version.txt').Trim())</Version>
      <Authors>LadybugDB</Authors>
      <Company>LadybugDB</Company>
      <Product>Ladybug</Product>
      <PackageId>LadybugDB.Extensions</PackageId>
      <Title>LadybugDB.Extensions - DI, health, resilience, export</Title>
      <Description>Dependency injection, health checks, resilience, streaming, row access, and export (JSON/CSV/DataTable) helpers for the LadybugDB embedded graph database C# binding.</Description>
      <PackageTags>graph;graph-database;cypher;ladybug;dependency-injection;healthcheck;resilience</PackageTags>
      <PackageLicenseExpression>MIT</PackageLicenseExpression>
      <PackageProjectUrl>https://github.com/ladybugdb/ladybug</PackageProjectUrl>
      <RepositoryUrl>https://github.com/LadybugDB/ladybug-dotnet</RepositoryUrl>
      <RepositoryType>git</RepositoryType>
      <PublishRepositoryUrl>true</PublishRepositoryUrl>
      <IncludeSymbols>true</IncludeSymbols>
      <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    </PropertyGroup>
  ```
  (Place this `<PropertyGroup>` alongside the existing one; `$(CSharpDir)` is provided by
  `Directory.Build.props` at the binding root.)
- [ ] Verify pack produces a `.nupkg`:
  `dotnet pack W:\code\ladybug\tools\csharp_api\src\LadybugDB.Extensions\LadybugDB.Extensions.csproj -c Debug -o W:\code\ladybug\tools\csharp_api\artifacts\ws-g-pack-check`
  Expected: **PASS**; a `LadybugDB.Extensions.0.17.0.1.nupkg` is produced. (WS-K owns the canonical
  `cake`/`VerifyPackages` wiring; this step only proves the project is packable. Leave a note in the
  PR for WS-K to add `LadybugDB.Extensions` to `VerifyPackages` and the release matrix.)
- [ ] Clean the throwaway pack output:
  `Remove-Item -Recurse -Force W:\code\ladybug\tools\csharp_api\artifacts\ws-g-pack-check`
- [ ] Commit: `chore(extensions): add NuGet package metadata`

---

## Self-review / done criteria

Tie-out to the WS-G "Done =" column (high-level-plan §2: *DI/health/resilience/stream/rows/export; fake tests*):

- [ ] **DI:** `AddLadybug`, `AddLadybugHealthCheck(name = "ladybug")`, `AddLadybugResilience(cfg? )`
  present with the exact §4.8 signatures; covered by `ServiceCollectionTests`.
- [ ] **Health:** `LadybugHealthCheck : IHealthCheck` runs a probe query (`RETURN 1` default), returns
  Healthy/Unhealthy; covered by `LadybugHealthCheckTests`. ns2.0 builds (HealthChecks abstractions
  restore) — verified in Task 1; if not, only those two types are `#if`-gated (noted as OpenQuestion).
- [ ] **Resilience:** `ResilientLadybugExecutor` implements per-call timeout (linked CTS), transient-aware
  retry with delay, and a lock-guarded circuit breaker; **no Polly** package reference; covered by
  `ResilientLadybugExecutorTests` (success, retry, non-transient, timeout, circuit-open, external-cancel).
- [ ] **Rows:** `IRowAccessor` with `Get<T>(int)`, `Get<T>(string)`, `GetOrDefault<T>(string, T)` —
  case-insensitive, `Convert.ChangeType`; covered by `RowAccessorTests`.
- [ ] **Streaming:** `ExecutorStreamingExtensions.StreamAsync` yields `IAsyncEnumerable<IRowAccessor>`
  (and a selector overload) over the executor seam; covered by `StreamingTests`.
- [ ] **Export:** `QueryResultExtensions` with `ToDictionaries`, `Select<T>`, `Scalar<T>(column = 0)`,
  `ToJson`, `ToJsonArray`, `ToDataTable`, `ToCsv(separator = ',')` — exact §4.8 surface on
  `this QueryResult r`; logic covered by `ResultDataHelperTests`/`QueryResultExtensionsLogicTests`,
  native path smoke-tested under `SkippableFact`.
- [ ] **Seam:** `ILadybugExecutor` + `LadybugExecutionResult` + `IResultData` make the package
  unit-testable; `LadybugConnectionExecutor` is the single WS-D integration point (`Connection.QueryAsync`).
- [ ] **Fakes only / no native:** every non-`SkippableFact` test passes with no engine present.
- [ ] **Both TFMs:** `dotnet build LadybugDB.slnx -c Debug` green for `net10.0` + `netstandard2.0`
  (records/`init` polyfilled via `Compat/IsExternalInit.cs`; `Microsoft.Bcl.AsyncInterfaces` referenced
  on ns2.0 for `IAsyncEnumerable`).
- [ ] **Solution wired:** both new projects registered in `LadybugDB.slnx`; package packs.
- [ ] **No regressions:** core `StructLayoutTests` still green after the `InternalsVisibleTo` edit.
