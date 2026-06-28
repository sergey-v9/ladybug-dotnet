# Next directions — distribution hardening · idiomatic & low-allocation C#

Handoff doc for the agent picking up after the 2026-06 parity/extensions/async effort.
Read this, then `04-coordination-decisions.md` (authoritative) and `05-review-and-fixes.md`.

Two parallel tracks. The test suite (258 tests) is the behavior guardrail for both — neither track
should change observable behavior or drop `netstandard2.0` reach or AOT-compatibility.

## Where things stand

The 2026-06 parity work is **integrated on branch `dev`** (tracks `fork/dev` =
github.com/sergey-v9/ladybug-dotnet). Upstream `origin` (LadybugDB/ladybug-dotnet) is untouched.
Shipped: full C-API parity + correctness fixes, **async**, **engine extensions** (+ Linux `RTLD_GLOBAL`
loader fix), **`LadybugDB.Arrow`**, **`LadybugDB.Extensions`**, **OpenTelemetry**, a **POCO source
generator**; native pin **v0.17.1**; 5-lens adversarial review (13 fixes); **258 tests** green on
`net10.0` + `netstandard2.0`; docs + 5 examples; Graphiti.Core search path covered.

**Two newer `dev` commits set the distribution context:** `6f3dbed` publishes **dev packages to GitHub
Packages**; `53e5ab5` **probes NuGet runtime assets in the native resolver** (`Native.cs` +
`ResolverTests.cs`) — i.e. how the engine library is found **when consumed as a package**.

---

# Direction 1 — Cross-platform packaged-consumption verification & perf baselines

**Gap:** the suite runs against a **locally-staged** native on only **2 of 5 shipped RIDs** (`ci.yml`:
`linux-x64`, `win-x64`). The **packaged-consumption resolver path** that `53e5ab5` changed — what real
consumers (Graphiti.Core) hit when they restore the published build — is **never exercised in CI**, and
macOS/ARM native paths are shipped-but-unrun. The BenchmarkDotNet `--ci-gate` has **no committed
baseline**, so it's inert.

### A. Verify native loading **from the published packages** on all 5 RIDs
Add a CI job that, on a matrix of `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, creates
a throwaway consumer project referencing the just-packed `LadybugDB` + `LadybugDB.Native.<rid>` (from
`artifacts/` or the GitHub-Packages feed) and runs a Cypher round-trip **plus** the `fts`/`vector`
extension round-trips against the **packaged** native — exercising `53e5ab5`'s resolver probe.
Runners: `linux-arm64` → `ubuntu-24.04-arm`; `osx-arm64` → `macos-latest`; `osx-x64` → `macos-13`.

### B. Expand the locally-staged native test matrix to all 5 RIDs
Cheaper companion to A: extend the `ci.yml` `test` job matrix (today `linux-x64`, `win-x64`) to the 3
missing RIDs so the full suite runs everywhere we ship. The RID→asset map in `cake/BuildContext.cs` is
already complete for all 5.

### C. Establish committed performance baselines
Run `benchmarks/LadybugDB.Benchmarks` (BenchmarkDotNet + `--ci-gate` + `DifferentialRunner`), add a
`[MemoryDiagnoser]`, capture a **committed baseline** (latency + alloc bytes/op), and wire `--ci-gate`
into CI (informational first) with `LADYBUG_BENCH_RATIO_MAX`. This baseline is the measuring stick for
Direction 2.

---

# Direction 2 — Idiomatic, modern, low-allocation C# (benchmark-driven)

A code-quality pass over `src/` (not tests): make the binding read like modern idiomatic C# and cut
allocations / GC pressure on the hot paths. **Measure first (Direction 1 / C), then optimize against the
numbers** — do not micro-optimize cold paths or trade readability for non-measurable gains.

### D. Cut allocations on the hot paths
The dominant allocator is the **result-reading path**. Profile it with `[MemoryDiagnoser]`, then target:
- **`QueryResult.Rows()` / `FlatTuple.GetValue` / `Value.GetValue()`** (`QueryResult.cs`, `FlatTuple.cs`,
  `Value.cs`): every row allocates a fresh `object?[]` and **boxes** every scalar; nested values allocate
  `List`/`Dictionary`. Options: (1) steer typed consumers to the allocation-light source-gen `Map<T>()`
  path (no boxing) and benchmark the delta; (2) add low-allocation typed accessors on `FlatTuple`
  (`GetInt64(col)`/`GetString(col)`/…) so a typed read never creates a `Value` wrapper or boxes;
  (3) reuse per-query scratch (column count/types are already cached — extend that idea).
- **UTF-8 marshaling** (`Interop/Native.cs` `ToUtf8`/`TakeString`, `PreparedStatement.cs` `ToBlobLiteral`):
  on ns2.0 `ToUtf8` allocates a `byte[]` per bound string; `ToBlobLiteral` builds an intermediate hex
  string. Use `stackalloc`/pooled (`ArrayPool<byte>`/`ArrayPool<char>`) buffers and `string.Create` on
  net7+ (`#if`-guarded), keeping the ns2.0 path correct.
- **`Extensions` convenience** (`QueryResultExtensions`, `RowAccessor`): `Convert.ChangeType` boxes and
  LINQ allocates iterators/closures; avoid `ChangeType` when the runtime type already matches, and avoid
  LINQ in the streaming inner loop. (These are convenience, not core — weight by benchmark.)

### E. Modernize idioms (TFM-guarded; keep ns2.0 + AOT)
Apply modern language/runtime features on `net10.0`, `#if`-guarding anything net7+/net8+/net9+/net10-only
so `netstandard2.0` still builds:
- `ArgumentNullException.ThrowIfNull(x)` (net7+) and `ObjectDisposedException.ThrowIf(flag, this)` (net8+)
  replacing the hand-written guards repeated across `Connection`/`QueryResult`/`Value`/`PreparedStatement`.
- `Span<T>`/`ReadOnlySpan<char|byte>`, `stackalloc`, `[SkipLocalsInit]` on hot interop; collection
  expressions `[]`; `params ReadOnlySpan<T>` (net9+); `SearchValues<char>` for the extension-name
  bare-identifier validation in `Connection.Extensions.cs`; `string.Create` / `Utf8.FromUtf16`.
- C# 14 / net10 niceties where they genuinely improve clarity (e.g. the `field` keyword, primary
  constructors) — readability first, not for its own sake.
- **Automate the idiom bar:** turn on `EnableNETAnalyzers` + a chosen `AnalysisLevel`/`AnalysisMode` and a
  curated `.editorconfig` ruleset (CA18xx perf rules: prefer `Length`/`Count` over LINQ `Count()`, avoid
  `ToArray`/`ToList` in loops, use `StringComparison`, etc.) so non-idiomatic/allocating patterns are
  caught by the build, not by review. Triage to a clean, warning-free build.

**Guardrails for Direction 2:** behavior identical (the 258 tests must stay green), `netstandard2.0`
still builds, `IsAotCompatible` preserved (no reflection), and every perf change justified by a benchmark
delta recorded against the Direction-1/C baseline.

---

## Smaller deferred backlog (either track, opportunistic)
- **`FLOAT[N]` fixed-array parameter binding** (Graphiti feedback #2): blocked on the engine C API having
  no fixed-`ARRAY` value constructor — needs a design pass or an engine API request.
- **CSR Arrow ingest** (WS-E / D6): wire `lbug_connection_create_arrow_rel_table_csr` + a `CSRResult`
  read shape; the interop (`Native.ConnectionCreateArrowRelTableCsr`) already exists.
- **Upstream tracking**: when engine **v0.17.2+** ships, bump `version.txt` + re-run the ABI checklist.

## Goal (for the new task)

> **Two parallel tracks, both fenced by the existing 258-test suite, `netstandard2.0`, and AOT.**
> **(1) Distribution hardening:** add a CI job that restores the published GitHub-Packages dev build (the
> `runtimes/<rid>/native` resolver path commit `53e5ab5` changed) into a fresh consumer project and runs a
> Cypher round-trip plus the `fts`/`vector` extension round-trips on `win-x64`, `linux-x64`, `linux-arm64`,
> `osx-x64`, `osx-arm64`; extend the locally-staged test matrix to those same five RIDs; and commit a
> BenchmarkDotNet `--ci-gate` + `[MemoryDiagnoser]` baseline.
> **(2) Idiomatic & low-allocation C#:** a benchmark-driven pass over `src/` that cuts allocations / GC
> pressure on the result-reading and marshaling hot paths (kill per-row/per-value boxing via typed
> accessors and the source-gen `Map<T>` path; pool/`stackalloc` UTF-8 buffers) and modernizes idioms
> (`ArgumentNullException.ThrowIfNull`, `ObjectDisposedException.ThrowIf`, `Span`/`SearchValues`,
> collection expressions, analyzers on) — net10.0 fast paths `#if`-guarded so ns2.0 stays green, with each
> perf change justified by a measured delta against the baseline.
