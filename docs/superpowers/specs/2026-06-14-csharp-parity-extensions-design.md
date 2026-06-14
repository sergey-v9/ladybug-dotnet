# Design Spec — LadybugDB C# Bindings: Parity, Extensions & Async (2026-06)

Status: **approved** (2026-06-14). Scope decided with the user: **P0–P3 ("everything")**,
**both** meanings of extensions, **full async**, **split packaging** with all packages
targeting **`net10.0;netstandard2.0`** (match core).

Companion analysis: [`../../parity-2026-06/01-situation-analysis.md`](../../parity-2026-06/01-situation-analysis.md),
[`../../parity-2026-06/02-parity-matrix.md`](../../parity-2026-06/02-parity-matrix.md).
Plans: [`../../parity-2026-06/03-high-level-plan.md`](../../parity-2026-06/03-high-level-plan.md) + `plan-<WS>.md`.

## 1. Goal

Bring the C# binding to **best-in-class parity** with the upstream C API and the sibling
bindings (python/java/nodejs/rust), fix correctness defects, and add a **.NET-idiomatic
ecosystem layer** (DI/health/resilience/streaming/export), **first-class async**, **Arrow
interop**, **engine-extension support**, **OpenTelemetry**, and **source-generated POCO
mapping** — without regressing the binding's existing strengths (typed value
materialization, RID-based native packaging, AOT-friendly core).

We re-implement ideas seen in the reference `ladybug.net` **with our own approach**; we do
not copy its code.

## 2. Non-goals

- No rewrite of the working core (Database/Connection/QueryResult/Value/PreparedStatement).
- No change to the upstream engine or its C API.
- No copying of reference source; no `kuzu_*` naming.
- Progress-callback parity (Node-only, not in the stable C ABI) is **out of scope** this round.
- Tracking `origin/main` past 0.17.2 is out of scope now (the native bump is 0.17.0.1 → 0.17.2).

## 3. Scope (itemized)

### P0 — correctness / must-fix
- **ns2.0 native loading on Unix** — the custom `DllImport` resolver is gated behind
  `#if NET7_0_OR_GREATER`; ns2.0 consumers on Linux/macOS can't find `liblbug.so/.dylib`.
  Provide an ns2.0-compatible resolution path.
- **Multi-statement result truncation** — wrap `lbug_query_result_has_next_query_result` /
  `_get_next_query_result`; multi-statement queries must return all result sets.
- **Engine-extension native symbol visibility** — verify and fix Linux loading so a
  dynamically-loaded extension `.so` resolves engine symbols (Java/Node/Rust do this via
  `RTLD_GLOBAL`/`-rdynamic`; we do nothing today).
- **DECIMAL fidelity** — eliminate the silent `decimal`-or-`string` ambiguity (see §5.2).

### P1 — core C-API parity
- Connection: `SetQueryTimeout`, `Interrupt`, `SetMaxThreadsForExec`/`GetMaxThreadsForExec`.
- QueryResult: `QuerySummary` (compiling/execution time), per-column **logical type**
  (`get_column_data_type`), `ResetIterator`.
- Parameter binding: DECIMAL, BLOB (`byte[]`), `Int128`, struct, map, explicit
  `timestamp_ns/ms/sec`; first-class fixed `ARRAY` and `UNION` reads.
- Value construction wrappers backing the above (`create_int128/decimal/internal_id/
  timestamp_ns/ms/sec/struct/map/uuid/null_with_data_type`).

### P2 — high-value capability
- **`LadybugDB.Extensions`** package — DI, `LadybugOptions`, health check, resilience
  executor (timeout/retry/circuit-breaker, no Polly), `IAsyncEnumerable` streaming sugar,
  `IRowAccessor`, `QueryResultExtensions` (ToDictionaries/Select/ToList/Scalar/ToJson/
  ToJsonArray/ToDataTable/ToCsv).
- **First-class async** in core — `ExecuteAsync`/`QueryAsync`/`PrepareAsync` (Task),
  `StreamAsync` (`IAsyncEnumerable`), `CancellationToken` → `Interrupt`.
- **`LadybugDB.Arrow`** package — Apache.Arrow `RecordBatch` export + Arrow ingest + CSR.
- **Engine-extension ergonomics** — `Connection.InstallExtension`/`LoadExtension` helpers.

### P3 — differentiators
- **OpenTelemetry** — `ActivitySource` + `Meter` in core (zero-cost when unobserved).
- **Source-generated POCO mapping** — `[LadybugRow]` + Roslyn generator → `Map<T>()`.
- **Test/parity harness** — upstream C-API test ports, a differential smoke runner, a
  BenchmarkDotNet `--ci-gate`, native-gated test pattern, parity-matrix bookkeeping.

## 4. Architecture

### 4.1 Packages (all target `net10.0;netstandard2.0`)

| Package | New? | Deps | Contents |
|---|---|---|---|
| `LadybugDB` (core) | existing | none (3rd-party) | engine binding, async, raw Arrow C-Data export, engine-ext loading, OTel source, POCO source-gen analyzer |
| `LadybugDB.Extensions` | **new** | `Microsoft.Extensions.*` abstractions, `Microsoft.Bcl.AsyncInterfaces` (ns2.0), `System.Text.Json` | DI/health/resilience/streaming/row-access/export |
| `LadybugDB.Arrow` | **new** | `Apache.Arrow` | friendly Arrow `RecordBatch`/CSR over core's raw export |
| `LadybugDB.Native.*` | existing | — | meta + 5 RID natives, **bumped to 0.17.2** |

**ns2.0 feasibility is a Phase-1 verification task.** If a specific dependency has no
acceptable ns2.0 asset (the prime suspect is `Microsoft.Extensions.Diagnostics.HealthChecks`),
the fallback is to keep the package multi-targeted and `#if NET`-gate *only* the affected
type — not to drop ns2.0 for the whole package. Dropping ns2.0 from any package is a flagged
decision, not a default.

### 4.2 Test / bench projects
- `LadybugDB.Tests` (existing) — extended.
- `LadybugDB.Tests.Parity` (**new**) — upstream C-API test ports, native-gated.
- `LadybugDB.Tests.Extensions` (**new**) — Extensions layer with fakes.
- `LadybugDB.Benchmarks` (**new**) — BenchmarkDotNet + `--ci-gate`.

## 5. Key design decisions

1. **Async is honest.** Sync engine calls are offloaded with `Task.Run` over the existing
   per-`Connection` serialization (the Python `AsyncConnection` model). `CancellationToken`
   registration calls `lbug_connection_interrupt`. No fake `Task.FromResult` wrappers.
2. **DECIMAL → `LadybugDecimal`.** Reading a DECIMAL returns `decimal` when it fits, else a
   lossless `LadybugDecimal` (unscaled `BigInteger` + `byte Scale`) with `ToDecimal()` /
   `ToString()` / `TryToDecimal()`. **A DECIMAL column never silently returns a bare
   `string`.** Binding accepts `decimal`, `LadybugDecimal`, and `BigInteger` via
   `lbug_value_create_decimal`. `BigInteger` (`System.Runtime.Numerics`) is ns2.0-safe.
3. **POCO mapping via source generator** shipped as an analyzer inside the core package.
   `[LadybugRow]` on a record/class generates a mapper; `QueryResult.Map<T>()` /
   `MapAsync<T>()`. No runtime reflection → AOT-safe. Analyzer targets ns2.0 (required).
4. **OpenTelemetry in core, lightweight.** A static `ActivitySource("LadybugDB")` + `Meter`
   (`db.query.count`/`.errors`/`.duration`) with zero cost when no listener is attached
   (`System.Diagnostics.DiagnosticSource`, ns2.0-safe). Registration sugar lives in Extensions.
5. **Arrow split raw/friendly.** Core exposes the raw Arrow C-Data-Interface export
   (`ArrowSchema`/`ArrowArray` out-params, no managed Arrow dep). `LadybugDB.Arrow` adds the
   `Apache.Arrow`-typed `RecordBatch`/CSR layer. Keeps Apache.Arrow off core and Extensions.
6. **Engine extensions.** `INSTALL`/`LOAD EXTENSION` run through the normal query path; we add
   `Connection.InstallExtension(name)`/`LoadExtension(name)` sugar. The real fix is loading
   `liblbug` with **global symbol visibility** on Linux/macOS (custom resolver using
   `dlopen(..., RTLD_NOW|RTLD_GLOBAL)` or equivalent) so extension `.so`s resolve symbols.
   Verified against a real extension (e.g. `json`/`fts`) under the native-gated tests.
7. **Thread-safety cleanup.** `FlatTuple`/`Value` disposal moves to the `Interlocked`/`Volatile`
   pattern used elsewhere.
8. **Naming/compat.** All new public API is additive and source-compatible with the current
   surface; no breaking renames. `lbug_*` interop naming throughout.

## 6. Workstreams (decomposed by file ownership to keep parallel agents conflict-free)

Phase/owner detail and dependencies are in `03-high-level-plan.md`. IDs:

| WS | Title | Primary files owned |
|---|---|---|
| A | Interop expansion | `Interop/Native*.cs`, `Interop/NativeTypes.cs`, `StructLayoutTests` |
| K | Native 0.17.2 + packaging skeleton | `version.txt`, `cake/**`, new `.csproj`s, `Directory.Build.props`, CI |
| B | Connection control + result surface | `Connection.cs`, `QueryResult.cs`, `FlatTuple.cs`, new `QuerySummary.cs`, `ColumnSchema` |
| C | Type fidelity + binding | `Value.cs`, `PreparedStatement.cs`, `DataTypeId.cs`, new `LadybugDecimal.cs`, `GraphTypes.cs` |
| D | Async surface | new `*.Async.cs` partials on `Connection`/`QueryResult` |
| E | Arrow interop | new `LadybugDB.Arrow/**` + small raw-export hook in `QueryResult` |
| F | Engine extensions | new `Connection.Extensions.cs`, native loader (`Interop/Native.cs` resolver) |
| G | `LadybugDB.Extensions` package | new `LadybugDB.Extensions/**` |
| H | OpenTelemetry | new `Diagnostics/LadybugDiagnostics.cs` + hooks |
| I | POCO source generator | new `LadybugDB.SourceGen/**` + `[LadybugRow]` attribute |
| J | Test + parity harness | new `LadybugDB.Tests.Parity/**`, `LadybugDB.Benchmarks/**`, parity docs |
| L | Docs + examples | `README.md`, `MAINTAINING.md`, new `examples/**` |

**Conflict management:** the core files most contended are `Connection.cs` and
`QueryResult.cs`. B owns their *synchronous* surface; D adds async via **separate `partial`
files** (`Connection.Async.cs`) to avoid editing the same file; F adds engine-ext via
`Connection.Extensions.cs` partial; H adds instrumentation via hooks B exposes. E/H touch
`QueryResult` only through small, pre-agreed seams that B lands in Phase 1's wake.

## 7. Execution model

- Integration branch: **`feature/parity-extensions-2026-06`** (off `main`).
- **Phase 1 (Foundation)** — WS-A + WS-K + the load fixes land on the integration branch and
  are committed *before* Phase 2, because the new P/Invoke surface and package skeletons are
  prerequisites. Run with limited parallelism; verify build + ABI tests.
- **Phase 2 (Parallel build)** — WS-B/C/D/E/F/H/I run as parallel agents, each in its **own
  git worktree/branch** off the post-Phase-1 integration branch. Partial-file ownership keeps
  edits disjoint; an integration step merges them with a conflict-resolution gate.
- **Phase 3 (Ecosystem + tests)** — WS-G (depends on D's async) and WS-J build on the merged
  Phase-2 surface.
- **Phase 4 (Harden)** — WS-L docs/examples, adversarial review per workstream, native-gated
  verification, integrate to `main` (PR or fast-forward, user's choice at the end).
- **Gates:** every workstream ends with `dotnet build` + relevant tests; every phase ends with
  an adversarial review pass before merging up. Native-dependent checks use the existing
  `LADYBUG_REQUIRE_NATIVE` / native-gated pattern.

## 8. Testing strategy

- Keep ABI guard tests (`StructLayoutTests`) green and extend them for new structs
  (`LbugQuerySummary`, any new layout).
- Extend `SmokeTests`/`TypeMappingTests`/`PreparedStatementTests` for new types + binding.
- New `LadybugDB.Tests.Parity` ports upstream C-API gtests (native-gated).
- New `LadybugDB.Tests.Extensions` uses fakes (no native needed) for DI/health/resilience/
  streaming/row-access/export.
- Differential smoke runner compares old vs new surface for output parity.
- `LadybugDB.Benchmarks --ci-gate` guards against latency regressions.

## 9. Risks & mitigations

| Risk | Mitigation |
|---|---|
| ns2.0 dep gaps (HealthChecks) | Phase-1 feasibility check; `#if NET`-gate the single type, don't drop ns2.0 |
| Parallel agents conflict on core files | Partial-file ownership; serialized core-surface edits; merge gate |
| Engine-ext symbol visibility hard to fix on .NET | Verify with a real extension under native-gated tests; document loader behavior |
| Source generator complexity/AOT | Isolate in its own project; ns2.0 analyzer; keep mapping reflection-free |
| Native bump regressions | API verified stable 0.17.0→0.17.2; native-gated smoke + struct-layout tests |
| Apache.Arrow ns2.0 target | Verify version in Phase 1; isolated package limits blast radius |

## 10. Definition of done

- All P0 items fixed with tests proving the fix.
- Parity matrix rows for P1 flip to ✅ (with tests).
- `LadybugDB.Extensions`, `LadybugDB.Arrow` packages build, test (fakes), and pack.
- Async + engine-extension + Arrow + OTel + POCO mapping shipped with tests/examples.
- Native pin at 0.17.2; full package family verifies (`VerifyPackagesTask`).
- Docs/examples updated; parity-matrix bookkeeping reflects reality.
- Green build across `net10.0` + `netstandard2.0`; native-gated tests pass where native present.
