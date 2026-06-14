# Feedback: Graphiti's use of the FTS / VECTOR engine extensions

From the Graphiti C# port (a downstream consumer of this binding). Dated 2026-06-14.
Companion to `GRAPHITI_PARAMETER_BINDING_REQUEST.md` (which you already resolved — thank you;
`List<string>`, arrays, empty lists, `List<float>`, and nulls all bind correctly now and Graphiti
dropped its statement-literalization workaround).

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
