# LadybugDB C# Bindings — Situation Analysis (2026-06)

> Source of truth for the parity/extensions effort. Produced by a 7-agent parallel
> analysis (our binding, the reference `ladybug.net`, the upstream C API, and the
> python/java/nodejs/rust sibling bindings). Companion docs:
> [`02-parity-matrix.md`](02-parity-matrix.md), `03-high-level-plan.md` (after scoping).

## 1. The landscape

| Thing | Location | State |
|---|---|---|
| **Engine** (LadybugDB, a Kuzu fork) | `W:\code\ladybug` | `origin/main` @ `3bd2ead5e` (2026-06-12); release branch `release_0_17` @ "Bump up version to 0.17.2"; latest tag `v0.17.1` |
| **C API ground truth** | `src/include/c_api/lbug.h` (+`helpers.h`) | `LBUG_API lbug_*` exports; **178** exported functions; embeds the Arrow C Data Interface |
| **Our binding** | `tools/csharp_api` (separate git repo, untracked in the monorepo) | Pinned to upstream **0.17.0.1**; ~119/178 C functions wrapped |
| **Reference binding** | `tools/csharp_api/reference/ladybug.net` | Independent 2nd implementation by another author; 3 source projects + rich test suites |
| **Siblings** | `tools/{python,java,nodejs,rust}_api` | Official bindings; Python & Rust are the most complete |

**Version note:** `git diff` of `src/include/c_api/` across `v0.17.0 → release_0_17 (0.17.2) → origin/main` is **empty** — the C API surface is unchanged. Bumping our pin `0.17.0.1 → 0.17.2` is a **native-binary refresh, not an interop change** (low risk).

## 2. What our binding is today

A clean, idiomatic, **typed** embedded binding. Strengths that already exceed the reference:

- **Typed value materialization** — `Value` decodes the full type system into real CLR objects (`Node`/`Rel`/`RecursiveRel` records, `DateOnly`/`DateTime`/`Int128`/`Guid`, nested `List`/`Struct`/`Map`). The reference's *live* path reads everything via `to_string`.
- **Real native packaging** — Cake-built package family: `LadybugDB` (managed, `net10.0;netstandard2.0`) + `LadybugDB.Native.<rid>` ×5 + `LadybugDB.Native` meta. RIDs: win-x64, linux-x64, linux-arm64, osx-x64, osx-arm64. Natives fetched from upstream GitHub releases by `version.txt`.
- **Custom DllImport resolver** (net7+) that remaps `lbug_shared` → `liblbug.so/.dylib` per platform.
- **ABI guard tests** — `StructLayoutTests` assert `Marshal.SizeOf`/field offsets without needing the native lib; `NativeGateTests` hard-fail CI when a per-RID binary is broken.
- Hand-written dual-TFM P/Invoke: `LibraryImport` source-gen on net10.0, classic `DllImport` + manual UTF-8 on netstandard2.0.

Public surface: `Database`, `Connection`, `QueryResult`, `FlatTuple`, `Value`, `PreparedStatement`, `SystemConfig`, `DataTypeId`, `GraphTypes` (`InternalId`/`Interval`/`Node`/`Rel`/`RecursiveRel`), `LadybugVersion`, `LadybugException`/`LadybugQueryException`.

## 3. Gap vs the C API (the parity backbone)

**~59 of 178 C functions are unwrapped.** The ones that matter, grouped:

### 3a. Connection control (every sibling has these; we have none)
- `lbug_connection_set_query_timeout` — **all 4 siblings expose it; we don't.**
- `lbug_connection_interrupt` — cancel a running query (Python/Java/Rust ✓).
- `lbug_connection_set/get_max_num_thread_for_exec` — per-connection thread control (Java/Rust ✓).

### 3b. QueryResult depth
- `lbug_query_result_get_query_summary` + `lbug_query_summary_get_compiling_time/_execution_time` — **no query timing exposed at all** (all 4 siblings expose it).
- `lbug_query_result_has_next_query_result` / `get_next_query_result` — **multi-statement queries silently return only the first result set** (Python/Java/Node ✓). Correctness gap.
- `lbug_query_result_reset_iterator` — re-iterate results (Java/Python/Node ✓).
- `lbug_query_result_get_column_data_type` — **per-column logical type**; we expose names only, no typed schema.

### 3c. Arrow interop (Python/Node/Rust ✓; Java ✗; we ✗)
- `get_arrow_schema`, `get_next_arrow_chunk` (export); `create_arrow_table`/`_rel_table`/`_rel_table_csr`/`drop_arrow_table` (zero-copy ingest). High-throughput + GNN/dataframe path. The Arrow C Data Interface is already in `lbug.h`.

### 3d. Value construction (limits parameter binding)
- Missing creators: `create_int128`, `create_decimal`, `create_internal_id`, `create_timestamp_ns/ms/sec`, `create_struct`, `create_map`, `create_uuid`, `create_null_with_data_type`. Consequence: **cannot bind DECIMAL, BLOB, Int128, struct, or map parameters** (`Bind(object?)` throws `NotSupportedException` for these).
- `lbug_get_last_error`, `lbug_value_clone/copy`, temporal `tm` helpers, `int128 from/to_string`, `node/rel_val_to_string` — minor/utility.

## 4. Correctness & quality risks found in our code

1. **netstandard2.0 has no DllImport resolver** (it's inside `#if NET7_0_OR_GREATER`). On Linux/macOS, `.NET Framework`/ns2.0 consumers rely on the OS loader finding a library literally named `lbug_shared`, but the shipped Unix asset is `liblbug.so/.dylib`. **Real cross-platform load failure** for that audience.
2. **Multi-statement results truncated** — see 3b; a script with multiple statements loses all but the first result.
3. **DECIMAL is lossy** — `GetDecimalAsString` is `decimal.TryParse`'d and *falls back to the raw string* on overflow, so a DECIMAL column returns either `decimal` or `string`. UUID similarly falls back to string.
4. **`DateTimeOffset` loses its zone** — bound/read as UTC micros via `timestamp_tz`; original offset not preserved (always `+00:00` on read).
5. **`FlatTuple`/`Value` disposal is not thread-safe** (plain `bool _disposed`, unlike the `Interlocked` pattern elsewhere) — latent misuse hazard against the engine's reused tuple buffer.
6. **Engine-extension symbol visibility (must verify).** Engine extensions (vector/httpfs/fts/json) load via Cypher `INSTALL x; LOAD EXTENSION x;` — there is **no C API** for them (confirmed). But Java re-`dlopen`s with `RTLD_GLOBAL`, Node sets RTLD flags, and Rust requires `-rdynamic`, all so the dynamically-loaded extension `.so` can resolve the engine's symbols. **Our binding does nothing here** — extension loading on Linux likely fails with undefined-symbol errors. Needs a load-with-global-symbols path. (See §6.)
7. `artifacts/` and `download/` contain committed binaries despite `MAINTAINING.md` saying they're gitignored — possible stale blobs.
8. ARRAY (fixed-size) and UNION are approximated by reusing the List/Struct readers.

## 5. The reference (`ladybug.net`) — what to learn, what to ignore

**Port the ideas (re-implement ourselves, don't copy):**

| Capability | Where (reference) | Value |
|---|---|---|
| **`Ladybug.Extensions` package** — DI registration, `LadybugOptions`, health check, resilience executor (timeout/retry/circuit-breaker, no Polly), `IAsyncEnumerable` streaming, `IRowAccessor` typed row mapping, `QueryResultExtensions` (ToDictionaries/Select/ToList/Scalar/ToJson/ToDataTable/ToCsv) | `src/Ladybug.Extensions/*` | The ".NET ecosystem" layer — **this is almost certainly the "extensions mechanism we forgot."** |
| **Built-in OpenTelemetry** — `ActivitySource` + `Meter` (query count/errors/cancellations + duration histogram) | `src/Ladybug/LadybugClient.cs` | Production observability with zero wiring |
| **`INativeLibrary` seam + fakes** | `src/Ladybug.Native/NativeAbstractions.cs` | Whole stack unit-testable without the engine |
| **Differential parity harness** — `DifferentialSmokeRunner` (order-independent output diff + p50/p95/throughput perf ratios) | `src/Ladybug/Parity/` | Reusable reference-vs-candidate regression gate |
| **Upstream C-API test ports** + `parity-matrix.md`/`upstream-pin.md` bookkeeping | `tests/Ladybug.Tests.Parity/*` | Systematic, pinned parity tracking |
| **BenchmarkDotNet `--ci-gate`** perf regression gate | `benchmarks/` | Drop-in CI perf guard |

**Do NOT copy (we're already better, or it's stale):**
- Naive native loading (no resolver/RID packaging/bundling) — ours is far superior.
- Live values read via `to_string` (no typed extraction) — ours materializes typed objects.
- `kuzu_*` naming / `KuzuNativeMethods` — stale; we use `lbug_*`.
- `LadybugOptions.BufferPoolSize` "not yet consumed by the adapter" — incomplete wiring.

## 6. The two meanings of "extensions" (disambiguation)

The word is overloaded; **both are real work and both were under-served in our binding:**

- **(A) Engine extensions** (vector, httpfs, fts, json, duckdb, postgres, …): installed/loaded via Cypher (`INSTALL x; LOAD EXTENSION x;`). No dedicated C API exists. Our parity work here is (i) a small ergonomic helper (`InstallExtension`/`LoadExtension`), and critically (ii) **loading the native engine with global symbol visibility** so extension `.so`s resolve symbols (the RTLD_GLOBAL/-rdynamic problem the other bindings solved and we didn't).
- **(B) The `.NET Extensions` package** (`Ladybug.Extensions` in the reference): DI, health checks, resilience, streaming, row mapping, export helpers. A whole additive ergonomics library — the visible thing the user likely meant.

## 7. Cross-cutting observations

- **Parity is not uniform across siblings** — Python is the most complete; Rust close behind; Node lacks `interrupt`; Java lacks Arrow; Rust lacks multi-result/`reset_iterator`. The **C API + Python/Rust** are the right "best-in-class" parity targets.
- **Async is a .NET-idiomatic opportunity.** No sibling has true async over the (synchronous) engine except Python's thread-pool `AsyncConnection` and Node's Promises. .NET users expect `Task`/`IAsyncEnumerable`/`CancellationToken`. Mapping `CancellationToken` → `lbug_connection_interrupt` is a natural, differentiating feature.
- **POCO mapping** is absent in *every* binding (the reference's `IRowAccessor` is the closest). A source-generated row→record mapper would make us best-in-class, but it's additive, not parity.
- **Test infrastructure** is our weakest area relative to the reference: we have smoke/struct-layout tests; they have systematic upstream-ported parity tests + a differential harness + perf gate.

## 8. Headline conclusions

1. Our **core engine binding is solid and in several respects better than the reference** (typed materialization, native packaging). The work is **breadth (C-API parity) + an ecosystem layer (.NET Extensions) + async**, not a rewrite.
2. **Highest-value parity gaps:** query timeout, interrupt/cancellation, query summary (timings), multi-statement results, per-column logical types, DECIMAL/BLOB/struct/map parameter binding, Arrow interop.
3. **Highest-value new capability:** the `LadybugDB.Extensions` package (DI/health/resilience/streaming/row-mapping/export) + first-class async.
4. **Must-fix correctness:** netstandard2.0 Unix loading; multi-statement truncation; engine-extension symbol visibility; DECIMAL fidelity.
5. **Version:** bump native pin to 0.17.2 (API-stable, low risk); optionally track `main` later.
