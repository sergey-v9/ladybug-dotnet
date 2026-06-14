# High-Level Implementation Plan — Parity, Extensions & Async

> Orchestration contract for the 2026-06 effort. Spec:
> [`../superpowers/specs/2026-06-14-csharp-parity-extensions-design.md`](../superpowers/specs/2026-06-14-csharp-parity-extensions-design.md).
> Detailed per-workstream plans: `plan-<WS>.md` (this directory).
> **For agentic workers:** use `superpowers:subagent-driven-development` to execute each
> `plan-<WS>.md` task-by-task. Steps use `- [ ]` checkboxes.
>
> ⚠️ **[`04-coordination-decisions.md`](04-coordination-decisions.md) is the authoritative
> addendum and OVERRIDES anything below or in `plan-*.md` that conflicts.** Notably: the
> native engine target is **v0.17.1** (package `0.17.1.0`), NOT 0.17.2 (unpublished); native
> loading is consolidated in WS-F (Phase 1); a Phase-1 "glue" step makes the core classes
> `partial` and adds the instrumentation seam stub; WS-K owns `.slnx`.

**Goal:** Close C-API/sibling parity, fix correctness defects, and add a .NET ecosystem
layer (DI/health/resilience/streaming/export), first-class async, Arrow interop, engine
extensions, OpenTelemetry, and source-generated POCO mapping — without regressing the
binding's typed materialization, RID packaging, or AOT-friendly core.

**Branch:** `feature/parity-extensions-2026-06` (off `main`).

---

## 1. Phases & sequencing

```
Phase 1  Foundation     A (interop) · K (native 0.17.2 + package skeletons) · load fixes
                         └─ commit to integration branch BEFORE Phase 2
Phase 2  Parallel build  B · C · D · E · F · H · I   (each its own worktree/branch)
                         └─ merge gate (conflict-resolution + build)
Phase 3  Ecosystem+tests G (needs D) · J (parity/bench harness)
Phase 4  Harden          L (docs/examples) · adversarial review · native-gated verify · integrate
```

**Why Phase 1 is a barrier:** every Phase-2 workstream consumes the new P/Invoke surface
(WS-A) and the new project skeletons (WS-K). They must exist and build first.

## 2. Workstreams

| WS | Title | Phase | Depends on | Owned files (no other WS edits these) | Done = |
|---|---|---|---|---|---|
| **A** | Interop expansion | 1 | — | `Interop/Native.cs`, `Interop/Native.LibraryImport.cs`, `Interop/Native.DllImport.cs`, `Interop/NativeTypes.cs`, `test/.../StructLayoutTests.cs` | all §3 functions declared on both TFMs; ABI tests green |
| **K** | Native 0.17.2 + packaging | 1 | — | `version.txt`, `cake/**`, `nuget/**`, `Directory.Build.props`, `LadybugDB.slnx`, new `.csproj` skeletons, `.github/workflows/**` | natives at 0.17.2; 3 new projects build empty; family packs |
| **B** | Connection control + result surface | 2 | A | `Connection.cs`, `QueryResult.cs`, `FlatTuple.cs`, new `QuerySummary.cs`, `LogicalType.cs`, `ColumnSchema.cs` | §4.1 contract; tests |
| **C** | Type fidelity + binding | 2 | A | `Value.cs`, `PreparedStatement.cs`, `DataTypeId.cs`, `GraphTypes.cs`, new `LadybugDecimal.cs` | §4.2 contract; tests |
| **D** | Async surface | 2 | A, B(seams) | new `Connection.Async.cs`, `QueryResult.Async.cs` (partials) | §4.3 contract; tests |
| **E** | Arrow interop | 2 | A, B(seam) | new `LadybugDB.Arrow/**`; adds `QueryResult.Arrow.cs` partial (raw seam) | export+ingest+CSR; tests |
| **F** | Engine extensions | 2 | A | new `Connection.Extensions.cs` (partial), `Interop/Native.cs` resolver | install/load helpers + global-symbol load; native-gated test |
| **H** | OpenTelemetry | 2 | A, B(seam) | new `Diagnostics/LadybugDiagnostics.cs`; B exposes the hook | activity+meter; tests |
| **I** | POCO source generator | 2 | A | new `LadybugDB.SourceGen/**`, new `Mapping/LadybugRowAttribute.cs` | `Map<T>()` generates; tests |
| **G** | `LadybugDB.Extensions` package | 3 | D | new `LadybugDB.Extensions/**`, new `LadybugDB.Tests.Extensions/**` | DI/health/resilience/stream/rows/export; fake tests |
| **J** | Test + parity harness | 3 | A–F | new `LadybugDB.Tests.Parity/**`, `LadybugDB.Benchmarks/**`, `plan` parity docs | ported tests + smoke runner + `--ci-gate` |
| **L** | Docs + examples | 4 | all | `README.md`, `MAINTAINING.md`, new `examples/**` | docs/examples updated |

**Contention control:** `Connection.cs`/`QueryResult.cs` are split into `partial class`
files so D/E/F/H never edit B's file. B lands the base `partial class` declarations + the
named seams in §4 during early Phase 2; D/E/F/H add their own partial files.

---

## 3. Shared interop contract (WS-A pins these; everyone else consumes)

Add P/Invoke for these C functions on **both** `Native.LibraryImport.cs` (net7+) and
`Native.DllImport.cs` (ns2.0), following the existing naming convention
(`lbug_connection_interrupt` → `Native.ConnectionInterrupt`, etc.). New native struct
`LbugQuerySummary` mirrored in `NativeTypes.cs` with a `StructLayoutTests` ABI guard.

**Connection:** `lbug_connection_interrupt`, `lbug_connection_set_query_timeout`,
`lbug_connection_set_max_num_thread_for_exec`, `lbug_connection_get_max_num_thread_for_exec`.
**QueryResult:** `lbug_query_result_get_query_summary`,
`lbug_query_summary_get_compiling_time`, `lbug_query_summary_get_execution_time`,
`lbug_query_summary_destroy`, `lbug_query_result_get_column_data_type`,
`lbug_query_result_reset_iterator`, `lbug_query_result_has_next_query_result`,
`lbug_query_result_get_next_query_result`, `lbug_query_result_get_arrow_schema`,
`lbug_query_result_get_next_arrow_chunk`.
**LogicalType:** `lbug_data_type_clone`, `lbug_data_type_equals`,
`lbug_data_type_get_child_type`, `lbug_data_type_get_num_elements_in_array`.
**Value creators:** `lbug_value_create_int128`, `lbug_value_create_decimal`,
`lbug_value_create_internal_id`, `lbug_value_create_timestamp_ns/ms/sec`,
`lbug_value_create_struct`, `lbug_value_create_map`, `lbug_value_create_null_with_data_type`,
`lbug_value_set_null`, `lbug_value_clone`, `lbug_value_copy`,
`lbug_value_get_struct_field_index`.
**Arrow ingest (for E):** `lbug_connection_create_arrow_table`,
`lbug_connection_create_arrow_rel_table`, `lbug_connection_create_arrow_rel_table_csr`,
`lbug_connection_drop_arrow_table`.
**Util:** `lbug_get_last_error`, `lbug_int128_t_from_string`, `lbug_int128_t_to_string`.

## 4. Shared public API contract (pinned; detailed plans must match exactly)

### 4.1 WS-B — Connection control + results
```csharp
// Connection.cs (sync surface)
public void Interrupt();
public void SetQueryTimeout(TimeSpan timeout);              // maps to ms (uint64)
public void SetMaxThreadsForExec(ulong numThreads);
public ulong GetMaxThreadsForExec();
public IReadOnlyList<QueryResult> QueryAll(string cypher);  // walks multi-statement chain

// QueryResult.cs
public QuerySummary Summary { get; }                        // lazy; from get_query_summary
public IReadOnlyList<ColumnSchema> Columns { get; }         // name + logical type, cached
public LogicalType GetColumnType(ulong index);
public void ResetIterator();
public bool HasNextQueryResult();
public QueryResult GetNextQueryResult();                    // ownership: chained result

// New types
public readonly record struct QuerySummary(double CompilingTimeMs, double ExecutionTimeMs);
public sealed record ColumnSchema(string Name, LogicalType Type);
public sealed class LogicalType {                            // wraps lbug_logical_type
    public DataTypeId Id { get; }
    public LogicalType? ChildType { get; }                   // LIST/ARRAY element type
    public ulong? FixedArraySize { get; }                    // ARRAY only
    public override string ToString();                       // e.g. "INT64", "LIST(STRING)"
}
```

### 4.2 WS-C — Type fidelity + binding
```csharp
// New LadybugDecimal.cs — deterministic, lossless DECIMAL
public readonly struct LadybugDecimal {
    public BigInteger Unscaled { get; }
    public byte Scale { get; }
    public decimal ToDecimal();                              // throws if out of range
    public bool TryToDecimal(out decimal value);
    public override string ToString();                       // exact textual form
    public static LadybugDecimal Parse(string s);
}

// Value.cs — DECIMAL never returns a bare string
public object? GetValue();      // DECIMAL -> decimal when representable, else LadybugDecimal
public LadybugDecimal GetDecimal();   // deterministic accessor (always LadybugDecimal)
// ARRAY -> object?[] (fixed length honored); UNION -> tagged value (first-class)

// PreparedStatement.cs — new Bind overloads + Bind(object?) dispatch coverage
public PreparedStatement Bind(string name, decimal value);
public PreparedStatement Bind(string name, LadybugDecimal value);
public PreparedStatement Bind(string name, BigInteger value);   // INT128
public PreparedStatement Bind(string name, byte[] value);       // BLOB
public PreparedStatement Bind(string name, IReadOnlyDictionary<string, object?> value); // STRUCT
public PreparedStatement BindMap(string name, IEnumerable<KeyValuePair<object, object?>> value); // MAP
```

### 4.3 WS-D — Async (partial files; never edit B's file)
```csharp
// Connection.Async.cs
public Task<QueryResult> QueryAsync(string cypher, CancellationToken ct = default);
public Task<IReadOnlyList<QueryResult>> QueryAllAsync(string cypher, CancellationToken ct = default);
public Task<PreparedStatement> PrepareAsync(string cypher, CancellationToken ct = default);
public Task<QueryResult> ExecuteAsync(PreparedStatement stmt, CancellationToken ct = default);
public IAsyncEnumerable<FlatTuple> StreamAsync(string cypher, [EnumeratorCancellation] CancellationToken ct = default);
// ct.Register(() => Interrupt()); offload via Task.Run honoring the existing _gate lock.
```

### 4.4 WS-E — Arrow
```csharp
// Core seam (QueryResult.Arrow.cs, no managed Arrow dep) — raw C-Data-Interface handles
public ArrowSchemaHandle GetArrowSchema();                  // IntPtr-backed, IDisposable
public ArrowArrayHandle GetNextArrowChunk(long chunkSize);  // IntPtr-backed, IDisposable
// LadybugDB.Arrow package (Apache.Arrow):
public static class LadybugArrow {
    public static Apache.Arrow.Schema ReadSchema(this QueryResult r);
    public static IEnumerable<Apache.Arrow.RecordBatch> ReadBatches(this QueryResult r, long chunkSize = 1_000_000);
    public static void CreateArrowTable(this Connection c, string name, Apache.Arrow.RecordBatch batch);
}
```

### 4.5 WS-F — Engine extensions
```csharp
// Connection.Extensions.cs (partial)
public void InstallExtension(string name);   // runs "INSTALL <name>"
public void LoadExtension(string name);      // runs "LOAD EXTENSION <name>"
// Native.cs resolver: load liblbug with global symbol visibility on Linux/macOS
//   (dlopen RTLD_NOW|RTLD_GLOBAL) so extension .so symbols resolve.
```

### 4.6 WS-H — OpenTelemetry
```csharp
// Diagnostics/LadybugDiagnostics.cs
public static class LadybugDiagnostics {
    public const string SourceName = "LadybugDB";
    public static ActivitySource ActivitySource { get; }    // "LadybugDB"
    // Meter "LadybugDB": db.query.count, db.query.errors, db.query.duration.ms
}
// B's query path wraps execution in an Activity + records the meter (no-op if unobserved).
```

### 4.7 WS-I — POCO mapping
```csharp
// Mapping/LadybugRowAttribute.cs (core)
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class LadybugRowAttribute : Attribute { }
// Generated (analyzer in core):
public IReadOnlyList<T> QueryResult.Map<T>();               // reflection-free
public IAsyncEnumerable<T> QueryResult.MapAsync<T>(CancellationToken ct = default);
```

### 4.8 WS-G — Extensions package entry points
```csharp
public static class LadybugServiceCollectionExtensions {
    public static IServiceCollection AddLadybug(this IServiceCollection s, Action<LadybugOptions> cfg);
    public static IServiceCollection AddLadybugHealthCheck(this IServiceCollection s, string name = "ladybug");
    public static IServiceCollection AddLadybugResilience(this IServiceCollection s, Action<LadybugResilienceOptions>? cfg = null);
}
public interface IRowAccessor { T Get<T>(int i); T Get<T>(string name); T GetOrDefault<T>(string name, T fallback); }
public static class QueryResultExtensions {
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> ToDictionaries(this QueryResult r);
    public static IEnumerable<T> Select<T>(this QueryResult r, Func<IRowAccessor, T> selector);
    public static T Scalar<T>(this QueryResult r, int column = 0);
    public static string ToJson(this QueryResult r);
    public static string ToCsv(this QueryResult r, char separator = ',');
    public static System.Data.DataTable ToDataTable(this QueryResult r);
}
```

## 5. Execution mechanics

- **Phase 1** runs in the integration branch's main worktree (or a single worktree merged
  immediately). Commit A + K + load fixes; verify `dotnet build` (both TFMs) + ABI tests.
- **Phase 2** dispatches one agent per WS in its **own worktree/branch**
  (`ws/<id>-<slug>` off the post-Phase-1 integration commit). Partial-file ownership keeps
  diffs disjoint; a deterministic **merge step** rebases/merges each `ws/*` branch into the
  integration branch in WS order (B→C→H→D→E→F→I), building after each.
- **Phase 3/4** run on the merged integration branch.
- **Per-task discipline (every plan):** TDD — write failing test, run it (fail), implement,
  run it (pass), commit. Frequent small commits.
- **Gates:** build (`net10.0` + `netstandard2.0`) + tests after every workstream; adversarial
  review per workstream before merge-up; native-gated tests where native is present.

## 6. Verification

- `dotnet build LadybugDB.slnx -c Release` green on both TFMs.
- `dotnet test` green (native-independent tests always; native-gated when `LADYBUG_REQUIRE_NATIVE=1`).
- `cake` `Pack` → `VerifyPackages` green for the expanded family (core + Extensions + Arrow + Native×6).
- Parity matrix P0/P1 rows flip to ✅ with referenced tests.
- `LadybugDB.Benchmarks --ci-gate` under the latency ceiling.

## 7. Detailed plan index (generated next)
`plan-A-interop.md`, `plan-K-packaging.md`, `plan-B-connection.md`, `plan-C-types.md`,
`plan-D-async.md`, `plan-E-arrow.md`, `plan-F-extensions-engine.md`, `plan-H-otel.md`,
`plan-I-sourcegen.md`, `plan-G-extensions-package.md`, `plan-J-testing.md`, `plan-L-docs.md`.
