# Feedback: Graphiti's use of the FTS / VECTOR engine extensions

From the Graphiti C# port (a downstream consumer of this binding). Dated 2026-06-14.
Follows the earlier parameter-binding request, which you already resolved — thank you;
`List<string>`, arrays, empty lists, `List<float>`, and nulls all bind correctly now and Graphiti
dropped its statement-literalization workaround.

This note is **not a bug report**. It records exactly which engine features Graphiti's search path
depends on, so the in-flight parity/extensions work (`feature/parity-extensions-2026-06`, spec
`docs/superpowers/specs/2026-06-14-csharp-parity-extensions-design.md`) keeps them covered. Where it
matters we reference your own workstream IDs.

## What Graphiti's Ladybug driver executes today (works on win-x64)

Graphiti ports Python Graphiti's Kuzu driver near-verbatim. Its search is **pushed into the engine**
(this matches the Python authors' design intent — the DB does retrieval, the app only does
rank/merge). Concretely, the driver issues:

1. **FTS (full-text / BM25).** At schema build:
   ```
   INSTALL FTS; LOAD EXTENSION FTS;
   CALL CREATE_FTS_INDEX('Entity',        'node_name_and_summary', ['name','summary']);
   CALL CREATE_FTS_INDEX('RelatesToNode_','edge_name_and_fact',     ['name','fact']);
   CALL CREATE_FTS_INDEX('Episodic',      'episode_content',        ['content','source','source_description']);
   CALL CREATE_FTS_INDEX('Community',     'community_name',         ['name']);
   ```
   At query time: `CALL QUERY_FTS_INDEX('Entity', 'node_name_and_summary', $query, TOP := $limit)`
   (and the `cast($query AS STRING)` variant for the edge index). Query text is passed **verbatim**
   (default tokenizer/stemmer/stopwords; default BM25 k/b). Graphiti deliberately does **not** tune
   these — parity with Python requires the defaults.

2. **Vector similarity (exact, no index).** Embeddings are stored as `FLOAT[]` columns and scored
   inline:
   ```
   WITH n, array_cosine_similarity(n.name_embedding, CAST($search_vector AS FLOAT[<dim>])) AS score
   WHERE score > $min_score ...
   ```
   `$search_vector` is bound as a `List<float>` (your binding handles this). The embedding dimension
   `<dim>` is currently **baked into the query string** because the parameter is cast to a fixed-size
   `FLOAT[<dim>]` per call. There is intentionally **no** `CREATE_VECTOR_INDEX` — this mirrors Python,
   which computes cosine inline on every backend and creates no ANN index.

## What matters to us (in priority order)

### 1. Extension loading must work on Linux/macOS, for `fts` **and** `vector` (your P0 / WS-F)

Your spec's P0 "engine-extension native symbol visibility" fix (Linux `.so` loading via global
symbol visibility / `RTLD_GLOBAL`) is **directly on Graphiti's critical path**: every Graphiti graph
needs `LOAD EXTENSION FTS` to even build its indexes. Today Graphiti is only validated on **win-x64**;
the moment it runs in a Linux container the FTS path depends entirely on that fix.

Request: when WS-F lands, please make the native-gated verification exercise a **full round-trip on
linux-x64** — `INSTALL FTS; LOAD EXTENSION FTS; CREATE_FTS_INDEX(...); QUERY_FTS_INDEX(...)` returning
rows — not just that the `.so` loads. The spec says "verified against a real extension (e.g.
`json`/`fts`)"; please include `fts` explicitly (it's the one Graphiti ships on), and ideally `vector`
too (see #2). If a `fts`/`vector` round-trip on linux-x64 is green in your suite, Graphiti can rely on
it without re-testing the engine itself.

### 2. (Forward-looking, optional) First-class fixed-size `FLOAT[N]` array binding (your WS-C)

WS-C lists "first-class fixed `ARRAY` … reads". If binding could also accept a **fixed-size
`FLOAT[N]` array as a query parameter** (so the value carries its own `FLOAT[N]` logical type),
Graphiti could drop the per-call `CAST($search_vector AS FLOAT[<dim>])` and stop templating the
dimension into the query string. Purely an ergonomics/cleanliness win — the current `List<float>` +
inline `CAST` path works, so this is **not blocking**.

### 3. (Forward-looking, optional) `vector` extension — is HNSW indexing usable from the binding?

Graphiti may later offer an **opt-in** HNSW vector index (`CREATE_VECTOR_INDEX` /
`QUERY_VECTOR_INDEX`) as a large-graph performance tier — this is the one engine capability Graphiti
does *not* currently use (Python uses none either; it'd be a C#-only enhancement, gated behind a flag
and benchmarks, with exact full-scan cosine staying the default). We have **not** committed to it.

If/when we do, all we'd need from the binding is what you already have plus #1: `INSTALL/LOAD vector`
working cross-platform, and binding a `List<float>`/`FLOAT[N]` as the `$query_vector` parameter to
`CALL QUERY_VECTOR_INDEX('Entity','<idx>', $query_vector, $k)`. No typed `CreateVectorIndex()` helper
is needed — raw `Connection.Query(...)` for the DDL is fine. The only ask here is: if it's cheap,
add a `vector` round-trip to the native-gated tests alongside `fts` so we know the path is live.

## Things we do NOT need (so you can de-scope)

- No typed FTS/vector index helper APIs — raw Cypher via `Query`/`Prepare` is exactly right for us.
- No BM25 parameter tuning, custom tokenizers/stemmers/stopwords surfaced in the binding — Graphiti
  must keep engine defaults for Python parity.
- No relationship-FTS index helper — Graphiti indexes the reified `RelatesToNode_` node table, not
  Kuzu relationship tables.

## Pointers

- Graphiti's search statements: `csharp/src/Graphiti.Core.Drivers.Ladybug/Drivers/Ladybug/LadybugSearchStatementBuilder.cs`
- Graphiti's FTS query construction (verbatim-or-empty, mirrors Python Kuzu): `.../LadybugFulltextQuery.cs`
- Python source of truth: `graphiti_core/graph_queries.py` (`get_fulltext_indices`,
  `get_nodes_query`, `get_vector_cosine_func_query` — KUZU branches).

---

## Response (binding maintainers, 2026-06-14)

Addressed on `feature/parity-extensions-2026-06`. The engine-extension loader fix (WS-F: load `liblbug`
with `dlopen(RTLD_NOW | RTLD_GLOBAL)` on Linux/macOS, also wired for `netstandard2.0`) shipped, and we
added end-to-end coverage of your exact search Cypher.

**#1 — FTS + vector on Linux/macOS, full round-trip (your P0 / WS-F): DONE.**
New `test/LadybugDB.Tests/SearchExtensionsTests.cs` (native-gated, offline-tolerant), mirroring your
statements:
- `Fts_InstallLoadCreateIndexAndQuery_ReturnsRankedRows` — `INSTALL FTS; LOAD EXTENSION FTS;
  CALL CREATE_FTS_INDEX('Entity','node_name_and_summary',['name','summary']);` then
  `CALL QUERY_FTS_INDEX(..., $query, TOP := $limit)` and asserts ranked rows come back (verbatim query
  text, parameterized `TOP`, engine-default tokenizer/BM25 — no tuning).
- `Vector_InlineCosineSimilarity_FiltersAndRanks` — your exact
  `array_cosine_similarity(n.name_embedding, CAST($search_vector AS FLOAT[<dim>]))` path with
  `$search_vector` bound as a `List<float>` (built-in function, no extension/network — so this is a
  reliable always-on check that the search path materializes correctly).
- `Vector_Extension_LoadsWithoutUndefinedSymbol` — `INSTALL/LOAD vector`, proving the `vector` `.so`
  resolves engine symbols cross-platform (your #3 "is the path live").

These are **gated to FAIL (not skip) on an "undefined symbol" error** — the precise RTLD_GLOBAL regression
— and only skip on a genuine offline/download failure. Crucially, CI runs the native suite on **both
`ubuntu-latest` (linux-x64) and `windows-latest`** (`.github/workflows/ci.yml`, `--target Test`, no
skips), so a green CI run is your cross-platform guarantee. Locally on win-x64 all three pass with **0
skips** (network reached the extension repo). `fts` and `vector` are both exercised explicitly, as
requested.

**#2 — fixed-size `FLOAT[N]` parameter binding: deferred (optional, non-blocking).**
Your current `List<float>` + inline `CAST($v AS FLOAT[N])` path is the one now pinned by the vector test,
so nothing breaks. We did **not** add `FLOAT[N]`-typed binding this round: the engine C API exposes no
fixed-`ARRAY` value constructor (only `lbug_value_create_list`), so a value that carries its own
`FLOAT[N]` logical type needs a small design pass. Tracked as an optional ergonomics follow-up; say the
word if you want to drop the per-call `CAST` and we'll prioritize it.

**#3 — `vector` HNSW: covered the "path is live" ask only.** Per your de-scope we added **no** typed
`CreateVectorIndex()`/`QueryVectorIndex()` helpers — raw `Connection.Query(...)`/`Prepare(...)` for the
DDL/queries remains the supported path, and binding `List<float>` as `$query_vector` works. The
`vector`-extension load is now in the suite.

**De-scoped, per your note:** no typed FTS/vector index helpers, no BM25/tokenizer/stemmer/stopword knobs
in the binding (engine defaults preserved for Python parity), no relationship-FTS helper.

---

## Consumer wishes — 2026-06-29 (Graphiti, the reference consumer)

Context: Graphiti analyzed the 24 binding commits since its pin (`53e5ab5` / `0.17.1-dev.2.1`). The work is
excellent and almost entirely **perf/allocation** (bind-pooling, pooled struct/map/list staging,
fast-path scalar row materialization, typed `FlatTuple` accessors, pre-sized mappers) — exactly the
direction we want; please keep going. The C API stayed byte-identical, so those land for Graphiti as a
free drop-in. These are our prioritized wishes for the next round.

### 1. (TOP, blocking us) Ship a green `0.18.0-dev` cross-RID publish

The dev feed reschemed to build natives **from source** at the pinned engine commit (`d77c9de` +
`upstream-engine.pin` → engine `d8277a8e5`, `0.18.0-dev.*.eng-<short>`). But the publish run is **red**:
the **linux-x64 and linux-arm64 from-source native builds fail**, and the publish job needs all RIDs, so
**no `0.18.0-dev.*` package exists on the feed**. macOS RIDs already pass — only the Linux source build
is the blocker. Until a green cross-RID publish lands, Graphiti **cannot adopt any engine-level fix**: the
`d8277a8e5` double-free-on-destroy fix, the delete/checkpoint CSR SIGSEGV fix, and the new `DROP_FTS_INDEX`
DDL are all out of reach. This is the single highest-leverage thing for us right now: please get the Linux
from-source native build green and publish one full `0.18.0-dev` set.

### 2. Add a `DROP_FTS_INDEX` round-trip test before we rewrite our FTS-idempotency workaround

Graphiti makes FTS-index creation idempotent across reopen by **catching the `"Index … already exists"`
`BinderException` by message** — brittle. The engine now has `CALL DROP_FTS_INDEX` (and `DROP INDEX [IF
EXISTS]`), which would let us do a clean explicit drop-then-create. Before we switch, please add a
`DROP_FTS_INDEX` round-trip to `SearchExtensionsTests` that **pins the behavior we'll depend on**: (a) it
cleans up the auxiliary docs/terms/appears-in tables (generic `DROP INDEX` does **not** — verify which one
to use for FTS), and (b) it **throws on a missing index** (so a naive drop-then-create is *not* idempotent
and we'll still need a guard). With that test pinning the contract, we can replace the message-catch
safely. (This is gated on #1 — the DDL only reaches us via a published `0.18.0-dev`.)

### 3. (Engine ask) First-class fixed-size `FLOAT[N]` parameter binding — still our one removable workaround

Re-raising the deferred #2 from above. It's the one Graphiti workaround an API addition would genuinely
remove: the per-call `CAST($search_vector AS FLOAT[<dim>])` templated into the query string at 3 sites in
`LadybugSearchStatementBuilder.cs`. As you noted, this needs **engine** surface first — a fixed-`ARRAY`
value constructor in `lbug.h` (today only `lbug_value_create_list`), then a typed binding helper so a
bound value carries its own `FLOAT[N]` logical type. Not blocking (the `List<float>` + `CAST` path is
vector-test-pinned and works), but it's the cleanest single ergonomics win for us — keep it on the
engine-feature backlog.

### 4. A first-class prepare-once / bind-many convenience on `Connection`

We re-`Prepare` the identical Cypher per call in our hot loops (bulk node/edge save, by-uuid delete,
rank-per-uuid). We can already prepare-once/bind-many with the public `Connection.Prepare` /
`PreparedStatement.Bind`, and we're planning that refactor on our side. But a first-class
`Connection.ExecuteMany(cypher, IEnumerable<paramMap>)` (or a documented re-bind pattern) would make the
prepared-statement reuse the **obvious default** for every consumer and put your pooled-bind perf work
(`84417a7`, `52042e5`) on the hot repeated-shape path where it pays most. Nice-to-have, not blocking.

### 5. A one-line "consumer impact" note per bump in `upstream-engine.pin`

`upstream-engine.pin` already asserts the C API is byte-identical — thank you, that's what lets us trust
"bump + verify." A structured one-liner per bump would save us re-deriving the rest each cycle, e.g.:
`consumer_impact: interop=none; fts_scoring=unchanged; new_ddl=DROP_FTS_INDEX`. This cycle we had to
manually confirm the only interop-dir touch was a harmless `Native.cs` test-seam cleanup; a single line
would have made that free.

### 6. Heads-up (not an ask): FTS behavior may shift at `0.18.0`

When we adopt a source-built `0.18.0-dev` native, we expect to re-verify FTS result ordering: the engine's
FTS insert-side fix (`48adaeb`) was partially reverted (`a0c762d`), and the FTS checkpoint signature
(`af55129`) + delete-side bookkeeping (`bdd64e6`) changed — so BM25 ordering may move slightly and FTS
indexes may need a rebuild on first open. If `SearchExtensionsTests` can pin the expected post-`0.18.0`
FTS ranking, that's our cross-check. Just flagging so it's on your radar with #1.

— Graphiti. (The bump/adopt/steer loop on our side is documented in our
`.agents/notes/ladybug-sync-procedure.md`; every workaround we carry shows up here as a standing ask.)

---

## Response — 2026-06-29 (binding maintainers)

Thanks for the structured wishes — the per-wish numbering made this easy to action. Status for all six:

**#1 — Green `0.18.0-dev` cross-RID publish: the Linux from-source build was fixed; validated on the next
dev push.** `.github/workflows/github-packages-dev.yml` was the blocker: the Linux native job installed a
bare `cmake ninja-build` toolchain, which is exactly the from-source failure mode (the shared-lib build
needs OpenSSL + `pkg-config` + `python3` for codegen, which the macOS/Windows runners ship preinstalled —
why only Linux was red). It now installs `libssl-dev pkg-config python3` (plus `make`/`g++-13`/`ccache`) and
builds via the engine's own `make GEN=Ninja` wrapper — the same recipe the engine's
`precompiled-bin-workflow.yml` uses to produce `liblbug.so`, so OpenSSL/codegen are wired correctly — then
stages the SONAME chain from `engine/install`. The `publish` + `consume-published` matrix (all five RIDs,
including linux-x64/linux-arm64) is unchanged. **This is a CI fix: the proof is the next green dev push** —
we are not claiming the cross-RID publish is green until that run lands, but the known Linux failure mode is
addressed.

**#2 — `DROP_FTS_INDEX` round-trip test: DONE (version-tolerant).** Added
`SearchExtensionsTests.DropFtsIndex_RoundTrip_PinsCleanupAndMissingIndexThrows`. It pins the contract you'll
depend on: (a) a clean **drop-then-create** succeeds — recreating the same index after `DROP_FTS_INDEX` is
the observable proof the auxiliary docs/terms/appears-in tables were cleaned (a stale aux table makes the
CREATE fail), and the recreated index answers a live `QUERY_FTS_INDEX`; and (b) **dropping a missing index
throws** (`Assert.Throws<LadybugQueryException>`), so a naive drop-then-create is **not** idempotent on its
own and you still need a guard. We use the FTS-specific `DROP_FTS_INDEX`, not generic `DROP INDEX` (only the
former cleans the FTS aux tables; generic `DROP INDEX` isn't even parseable on some natives). The test is
version-tolerant: it Skips cleanly when the loaded native predates the DDL (a Catalog "function does not
exist" probe on the first drop) and validates for real on the `0.18.0-dev` native in the CI Test gate.

**#3 — Fixed-size `FLOAT[N]` parameter binding: engine-gated, now tracked in the backlog.** Confirmed it
needs **engine** surface first: `lbug.h` has only `lbug_value_create_list` (variable-length `LIST`), no
fixed-`ARRAY` constructor, and the header is byte-identical `v0.17.1..main` so `0.18.0` doesn't add one. We
recorded the full ask — needs an `lbug_value_create_array`-style addition upstream, then a typed binding
helper — in a new **"D) Engine-feature backlog (needs upstream)"** section of `docs/upstream-sync.md`, so it
survives across pin bumps and gets re-checked on each one. Not blocking: the `List<float>` + inline `CAST`
path stays the supported route and is pinned by `Vector_InlineCosineSimilarity_FiltersAndRanks`.

**#4 — Prepare-once / bind-many on `Connection`: DONE.** `Connection.ExecuteMany(cypher, parameterSets)`
prepares once and re-binds every key per parameter set on the same `PreparedStatement` (write path,
disposes each result), with a projected overload `ExecuteMany<T>(cypher, parameterSets, selector)` that maps
one value per set in input order (your rank-per-uuid loop). Async `ExecuteManyAsync` / `ExecuteManyAsync<T>`
mirror them under the connection gate with cancellation honored. This puts the pooled-bind perf work
(`84417a7`, `52042e5`) on the hot repeated-shape path and makes prepared-statement reuse the obvious default.
See `src/LadybugDB/Connection.Batch.cs` and `test/LadybugDB.Tests/ExecuteManyTests.cs`.

**#5 — Per-bump `consumer_impact` line: DONE and made REQUIRED.** `upstream-engine.pin` now carries a
structured one-liner for the current pin:
`consumer_impact=interop=none; fts_scoring=unchanged-from-v0.17.1; new_ddl=DROP_FTS_INDEX,DROP_INDEX_IF_EXISTS;
fixes=double-free-on-destroy,delete/checkpoint-CSR-SIGSEGV; note=re-verify FTS ordering on first 0.18.0 open
(insert-fix partly reverted)`. And `docs/upstream-sync.md` now **requires** it on every bump: a new numbered
pin-advance step ("Write the `consumer_impact=` one-liner — REQUIRED, never skip") with the field format
(`interop=…; fts_scoring=…; new_ddl=…; fixes=…; note=…`) and per-field guidance. You can read the impact off
the pin instead of re-deriving it each cycle.

**#6 — Post-`0.18.0` FTS ranking: assertions strengthened to be score-tweak-resilient.**
`Fts_InstallLoadCreateIndexAndQuery_ReturnsRankedRows` now asserts **relevance properties**, never exact BM25
scores, over a corpus shaped so relevance is unambiguous (not score-margin-dependent): the clearly-most-
relevant entity ranks first, a partial match is present, the irrelevant entity is absent, and scores are
positive and strictly descending (the `QUERY_FTS_INDEX` contract). So the `48adaeb`/`a0c762d` insert-side
churn, the `af55129` checkpoint-signature change, and the `bdd64e6` delete-side bookkeeping can move BM25
k/b without breaking the test, while a genuine ranking regression still fails it. If the index needs a
rebuild on first `0.18.0` open, this is your cross-check.

**Net:** #2/#4/#5/#6 shipped this round (code + docs, suite green on the staged native); #1 is a CI fix
pending its next-push validation; #3 is engine-gated and now on the documented backlog.

---

## Update — 2026-06-29 (binding maintainers): #1 is GREEN and published

**#1 — green `0.18.0-dev` cross-RID publish: DONE.** Published **`0.18.0-dev.18.1.eng-d8277a8e5`** to the
fork's GitHub Packages feed; the dev workflow run is green on **all five RIDs** through `pin` →
`build-native` → `publish` → `consume-published` (the matrix restores the *published* packages on fresh
linux-x64/linux-arm64/win-x64/osx-x64/osx-arm64 runners and runs a Cypher + **fts + vector** round-trip).
You can pin to this version and adopt the engine fixes (`d8277a8e5` double-free-on-destroy,
delete/checkpoint CSR SIGSEGV, and `DROP_FTS_INDEX`).

The fix turned out to be bigger than a CI tweak — it was a **shipping defect**: a main-tracking native
declares engine `0.18.0`, but upstream builds extensions only for released tags, so the engine's
`INSTALL fts` downloaded the mismatched `0.17.0` extension and crashed on an undefined `Catalog::createIndex`
— exactly what your raw `INSTALL FTS; LOAD EXTENSION FTS` would have hit on your machine. So the dev track
now **source-builds `fts`/`vector` from the pinned commit, ships them in `LadybugDB.Native.<rid>`** (flat,
beside the engine lib — verified present in the published nupkgs), **and the binding pre-seeds the engine's
`~/.lbdb` extension cache from them at load** (`src/LadybugDB/Interop/ExtensionStaging.cs`). Net for you:
`INSTALL/LOAD fts`/`vector` resolve the ABI-matched build with no network and no code change on your side.
Mechanism + the per-bump `LBUG_EXTENSION_VERSION` gate are documented in `docs/upstream-sync.md` §E.

**#2 / #6 now validated against the real `0.18.0` native** (not just the staged host build): both run in the
green publish Test gate, so `DropFtsIndex_RoundTrip_*` and the relevance-based FTS ranking assertions are
confirmed on `0.18.0-dev`. If first-open FTS rebuild behavior ever diverges, those tests are the cross-check.
