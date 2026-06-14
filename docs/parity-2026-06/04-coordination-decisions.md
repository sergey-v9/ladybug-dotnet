# Coordination Decisions (authoritative addendum)

> Resolutions to the cross-workstream open questions raised by the 12 detailed plans.
> **This document overrides any conflicting detail in `plan-*.md` or `03-high-level-plan.md`.**
> Implementation agents MUST follow these decisions.

## D1 — Native engine target is **v0.17.1**, not 0.17.2

`v0.17.2` is **not a published GitHub release** (it exists only as a commit on the
`release_0_17` branch). `cake` `FetchNatives` downloads *release assets*, and the latest
published release with all five RID natives is **`v0.17.1`** (verified: `liblbug-linux-x86_64.tar.gz`,
`liblbug-linux-aarch64.tar.gz`, `liblbug-osx-x86_64.tar.gz`, `liblbug-osx-arm64.tar.gz`,
`liblbug-windows-x86_64.zip` all present).

- **`version.txt` → `0.17.1.0`** (engine `v0.17.1`, binding revision `.0`).
- Wherever `plan-K`, `plan-J`, `plan-L`, or `03` say "0.17.2", read **"0.17.1"**.
- The C API is verified byte-identical across `v0.17.0 → v0.17.1 → origin/main` for
  `src/include/c_api/`, so this is a native-binary refresh with **no interop change**.
- `parity-tracking.md` (WS-J) pins upstream tag **`v0.17.1`**.

## D2 — WS-A interop is authoritative and lands first

WS-A names every new P/Invoke per the existing convention
(`lbug_connection_interrupt` → `Native.ConnectionInterrupt`,
`lbug_query_result_get_query_summary` → `Native.QueryResultGetQuerySummary`, etc.) and adds
`LbugQuerySummary` (one-pointer struct) + `ArrowSchema`/`ArrowArray` mirrors to
`NativeTypes.cs`. WS-A lands in **Phase 1**; all consumers (B/C/D/E) reference the **landed**
names. If a consumer needs a declaration WS-A didn't add, that is a WS-A bug to fix, not a
reason for the consumer to add interop in its own files.

## D3 — All native-loading work is **WS-F**, split across phases

`Interop/Native.cs` (the resolver) is owned solely by **WS-F**. WS-A does **not** edit the
resolver (it may add a `GetLastError()` wrapper in a separate region only).

- **WS-F1 (Phase 1, foundation):** the loader fixes —
  (a) the **P0 ns2.0 Unix resolver** (spec §3: ns2.0 currently has no resolver), and
  (b) **global symbol visibility** (`dlopen(RTLD_NOW|RTLD_GLOBAL)` on Linux/macOS via a new
  `Interop/UnixNativeMethods.cs`) so engine-extension `.so`s resolve symbols.
  ns2.0 has no `[ModuleInitializer]`, so WS-F1 adds an explicit ns2.0 load path (e.g. a static
  ctor / first-call hook) — this is part of WS-F1.
- **WS-F2 (Phase 2):** the `Connection.Extensions.cs` partial (`InstallExtension`/`LoadExtension`).

## D4 — Phase-1 foundation glue (so Phase-2 partials attach cleanly)

Done in **Phase 1**, before the parallel fan-out, by the foundation agent set:

1. Make `Connection` and `QueryResult` `sealed partial class` (one-line change each). This
   removes the B/D/E/F contention over "who adds `partial`". WS-B keeps the **synchronous**
   members in the primary file; D/E/F add their own `*.partial.cs` files.
2. Add `src/LadybugDB/Diagnostics/LadybugInstrumentation.cs` as a **no-op seam stub**:
   ```csharp
   namespace LadybugDB.Diagnostics;
   internal static class LadybugInstrumentation {
       public static QueryScope StartQuery(string cypher) => new QueryScope();
   }
   internal sealed class QueryScope : IDisposable {
       public void SetSuccess() { }
       public void SetError(Exception? ex = null) { }
       public void SetCancelled() { }
       public void Dispose() { }
   }
   ```
   This lets WS-B call `using var scope = LadybugInstrumentation.StartQuery(cypher);` in its
   query path during Phase 2, and lets WS-H **replace the stub's internals** (it owns this file
   in Phase 2) with the real `ActivitySource`/`Meter` logic — without B and H racing on the
   same file or on ordering.

## D5 — WS-B ↔ WS-H seam (pinned)

- Seam type: `LadybugDB.Diagnostics.LadybugInstrumentation.StartQuery(string) -> QueryScope`
  with `QueryScope` members `SetSuccess()`, `SetError(Exception?)`, `SetCancelled()`,
  `Dispose()` (see D4).
- **WS-B** wraps its synchronous query/execute path in this scope (success/error/cancel).
- **WS-H** owns `Diagnostics/LadybugInstrumentation.cs` + `Diagnostics/LadybugDiagnostics.cs`
  in Phase 2 and fills the scope with the real `ActivitySource("LadybugDB")` + `Meter`.
- Canonical instrument names: **`db.query.count`**, **`db.query.errors`**,
  **`db.query.duration.ms`** (histogram). WS-G must not hard-code different metric names.

## D6 — WS-E scope: node + rel ingest + Arrow export; CSR is best-effort

`§4.4` is extended: WS-E ships, in addition to `ReadSchema`/`ReadBatches`/`CreateArrowTable`:
- `public static void CreateArrowRelTable(this Connection c, string name, Apache.Arrow.RecordBatch batch, string fromTable, string toTable);`

CSR export/ingest (`lbug_connection_create_arrow_rel_table_csr`, the `CSRResult` read shape)
is **best-effort**: implement if low-cost; otherwise `log()`-and-defer with a note in
`parity-tracking.md`. Core seam types `ArrowSchemaHandle`/`ArrowArrayHandle` live in the
**`LadybugDB`** namespace (core), returned by the `QueryResult.Arrow.cs` partial.

## D7 — WS-C BLOB & UNION

- **BLOB binding:** the C API has no `lbug_value_create_blob`. Bind `byte[]` as an escaped
  `'\xNN…'` blob literal that the query CASTs to BLOB; document the limitation (this matches
  the siblings, which also lack value-level blob binding). Reading BLOB → `byte[]` is unchanged.
- **UNION:** materialize as a first-class tagged `Union(string Tag, object? Value)` record
  (added to `GraphTypes.cs`). Verify the physical layout against the staged engine under the
  native-gated tests; if the tag is a numeric discriminator rather than a string field, fall
  back to returning the single active member.

## D8 — `LadybugDB.slnx` is owned by WS-K (Phase 1)

To prevent merge conflicts on the solution file, **WS-K registers ALL new projects** in
`LadybugDB.slnx` during Phase 1: `LadybugDB.Extensions`, `LadybugDB.Arrow`,
`LadybugDB.SourceGen`, `LadybugDB.Tests.Parity`, `LadybugDB.Tests.Extensions`,
`LadybugDB.Tests.Arrow`, `LadybugDB.Benchmarks`, `LadybugDB.Benchmarks.Tests`. Other
workstreams **create their project files/content but do NOT edit `.slnx`** (WS-K already
registered them). If a WS needs a project WS-K didn't register, flag it; WS-K adds it.

## D9 — Shared dependency versions pinned centrally (WS-K)

WS-K pins these in `Directory.Build.props` (or a `Directory.Packages.props`) so A/D/G/H/I
agree:
- `Microsoft.Bcl.AsyncInterfaces` — single version (consumed by D, G, I on ns2.0).
- `System.Diagnostics.DiagnosticSource` — single version (consumed by H; ns2.0-safe).
- `Microsoft.Extensions.*` abstractions + `Apache.Arrow` — versions confirmed to ship
  `netstandard2.0` (per WS-K Phase-1 feasibility check). Apache.Arrow target ≥ a version with
  ns2.0 support; HealthChecks `#if NET`-gate fallback only if its chosen version lacks ns2.0.

## D10 — Test project placement

- WS-C extends the existing `test/LadybugDB.Tests` (has `InternalsVisibleTo`).
- WS-E adds `test/LadybugDB.Tests.Arrow`; WS-G adds `test/LadybugDB.Tests.Extensions`;
  WS-J adds `test/LadybugDB.Tests.Parity` + `benchmarks/LadybugDB.Benchmarks(.Tests)`.
- WS-H adds `DiagnosticsTests.cs` to the existing `test/LadybugDB.Tests`.
- All registered in `.slnx` by WS-K (D8).

## D11 — Revised phase plan

```
Phase 1 (Foundation, serialized on the integration branch, then committed):
   WS-A   interop expansion (declarations + structs + ABI/parity tests)
   WS-F1  native-loading fixes (ns2.0 resolver + RTLD_GLOBAL global visibility)
   WS-K   version 0.17.1 + native refresh + package/test/bench skeletons + slnx + dep pinning
   GLUE   make Connection/QueryResult partial; add LadybugInstrumentation seam stub (D4)
   Gate:  dotnet build (net10.0 + netstandard2.0) green; ABI + parity tests green.

Phase 2 (Parallel build, each WS in its own worktree off the Phase-1 commit):
   WS-B (sync surface + seams) → then C, H, D, E, F2, I
   Merge order into integration branch: B → C → H → D → E → F2 → I, building after each.

Phase 3 (Ecosystem + tests):
   WS-G (needs D) , WS-J (parity ports + bench + differential runner)

Phase 4 (Harden):
   WS-L docs/examples ; adversarial review per WS ; native-gated verification ; integrate.
```

## D12 — Out-of-band maintainer actions (flagged to the user; not build blockers)

- **nuget.org trusted-publishing** policy must be extended to cover the two new publishable
  ids `LadybugDB.Extensions` and `LadybugDB.Arrow` before any `v*` release tag, or their
  publish step fails. (Local build/pack/test is unaffected.)
- Deciding whether to later cut/await an upstream **`v0.17.2` engine release** is a separate
  call; this effort ships on `v0.17.1`.
