# Upstream engine sync — keeping the bindings moving with `LadybugDB/ladybug`

This binding wraps the engine's C API. It couples to the upstream engine in exactly **two** places, and
keeping in sync means keeping both correct:

1. **The C API surface** — `src/include/c_api/lbug.h` (+ `helpers.h`) in the engine repo. Every P/Invoke
   in `src/LadybugDB/Interop/Native.*.cs` and every struct/enum in `NativeTypes.cs` mirrors it. **If the
   header changes, the interop must change.** If the header is unchanged, the binding is source-compatible
   no matter how much the engine internals moved.
2. **The native binary** — the prebuilt `lbug_shared.dll` / `liblbug.so` / `liblbug.dylib` the binding
   loads at runtime, selected per the version pin.

We run **two tracks** off these:

| Track | Pin | Native source | Versions | Where |
|---|---|---|---|---|
| **Stable / release** | `version.txt` → latest published engine **release** (now `0.17.1.0` → `v0.17.1`) | downloaded release asset (`gh release download`) | `0.17.1.x` | `ci.yml`, `release.yml` |
| **Main-tracking / dev** | `upstream-engine.pin` → an engine **commit** (now `d8277a8e5`, engine `0.18.0`) | **built from engine source** at that commit, per RID | `0.18.0-dev.*` prerelease | `github-packages-dev.yml` (fork-only) → GitHub Packages |

The dev track is how the fork "rides main": it ships a prerelease built against a chosen upstream commit
so we get fixes (e.g. the `d8277a8e5` double-free-on-destroy fix) before they're released.

---

## A) Routine: advance the main-tracking pin (the common case)

Run this whenever you want the fork to move up to a newer upstream `main`.

1. **Fetch upstream** (in the engine checkout, e.g. `W:\code\ladybug`):
   ```
   git -C <engine> fetch origin --tags --prune
   ```
2. **Pick the target commit** — usually `origin/main` HEAD:
   ```
   git -C <engine> rev-parse origin/main           # NEW = the new pin SHA
   git -C <engine> describe --tags origin/main      # e.g. v0.17.1-130-gabcdef0
   ```
   Let `OLD` = the current `commit=` in `upstream-engine.pin`.
3. **Diff the C API — this is the gate that decides whether interop changes are needed:**
   ```
   git -C <engine> diff OLD NEW -- src/include/c_api/
   ```
   - **Empty → no interop changes.** The binding is already source-compatible; skip to step 6.
   - **Non-empty → follow §C** (update interop) before continuing.
   Also glance at the impl for behavior changes (caught by tests, not a blocker):
   ```
   git -C <engine> log --oneline OLD..NEW -- src/c_api/ src/include/c_api/
   ```
4. **Check the engine's declared version** (drives the prerelease number) — only changes occasionally:
   ```
   git -C <engine> show NEW:CMakeLists.txt | grep 'project(Lbug VERSION'
   ```
   If it differs from `engine_version=` in the pin, update that field.
5. **Update `upstream-engine.pin`**: set `commit=NEW`, `describe=...`, `engine_version=...` (if changed),
   `updated=<today>`, and a one-line `note=` (e.g. what fix you're picking up). Leave `capi_stable_since`
   at the last release where the header matched (only move it if §C changed the header).
6. **Write the `consumer_impact=` one-liner (REQUIRED — never skip this).** Every bump MUST carry a
   structured `consumer_impact=` line so downstream consumers (e.g. Graphiti) read the impact off the pin
   instead of re-deriving it from the engine diff each cycle. Format:
   ```
   consumer_impact=interop=<none|added:fn,…|changed:fn,…>; fts_scoring=<unchanged-from-vX.Y.Z|...>; new_ddl=<NONE|DDL,…>; fixes=<crash/behavior fixes worth naming>; note=<re-verify / rebuild caveats, or omit>
   ```
   - `interop=none` is the common case (header byte-identical → §A.3 diff empty). If §C ran, summarize the
     interop delta (`interop=added:lbug_value_create_array` etc.).
   - `fts_scoring=` — does BM25 ordering move? Pin it to the last release it matched, or call out the shift.
   - `new_ddl=` — DDL/procedures newly reachable from raw Cypher (e.g. `DROP_FTS_INDEX`); `NONE` if none.
   - `fixes=` — crash/behavior fixes the source-built native picks up (named, comma-separated).
   - `note=` — any re-verify/rebuild caveat on first open of the new native; omit if there's nothing.
7. **Commit and push to the fork's `dev`:**
   ```
   git add upstream-engine.pin   # + any interop changes from §C
   git commit -m "chore(upstream): track engine main @ <short> (<describe>)"
   git push fork dev
   ```
8. **CI does the rest.** The push triggers `github-packages-dev.yml`, which:
   builds `lbug_shared` from `LadybugDB/ladybug@NEW` **per RID** (`win-x64`, `linux-x64`, `linux-arm64`,
   `osx-x64`, `osx-arm64`), runs the full test suite against the host build, packs the
   `engine_version-dev.<run>.<attempt>.eng-<engineShort>` prerelease family, publishes it to GitHub
   Packages, and then the `consume-published` matrix restores the published packages on all five RIDs and
   runs a Cypher + `fts` + `vector` round-trip against the source-built native. **Green CI = the fork now
   rides that upstream head.**
9. **(optional) tag** the bindings commit, e.g. `git tag dev/0.18.0-eng-<short> && git push fork --tags`,
   so the exact (binding, engine) pair is recoverable by name.

### Reproduce the native build locally
```
pwsh scripts/build-native-from-pin.ps1            # builds engine@pin for the host RID + stages it
dotnet test LadybugDB.slnx -c Release             # run the suite against the source-built native
```
Requires CMake + Ninja + a C++ toolchain (MSVC on Windows). See `scripts/build-native-and-test.ps1` for
the toolchain bootstrap.

---

## B) Routine: adopt a new published engine RELEASE (the stable track)

When upstream publishes a real release (e.g. `v0.17.2` or `v0.18.0`):

1. `git -C <engine> fetch origin --tags`; confirm the release exists: `gh release view vX.Y.Z --repo LadybugDB/ladybug`.
2. **Diff the C API** `vOLD..vX.Y.Z -- src/include/c_api/` (same gate as §A.3; do §C if it changed).
3. Set `version.txt` to `X.Y.Z.0`.
4. `./build.ps1 --target Test` then `--target Pack` (downloads the release natives, runs the suite, packs).
5. Tag `vX.Y.Z.0` and push to the **release** repo per `release.yml` (this is the upstream bindings repo
   flow, not the fork). Update `upstream-engine.pin` to the release tag's commit too, so dev re-aligns.

---

## C) When the C API header DID change (interop update)

Follow the existing **ABI Update Checklist** in `MAINTAINING.md`. In short:

1. Read the diff of `src/include/c_api/lbug.h` (+ `helpers.h`). For each **added** function, add a P/Invoke
   to BOTH `src/LadybugDB/Interop/Native.LibraryImport.cs` and `Native.DllImport.cs` (the
   `InteropDeclarationParity` test enforces they stay in lockstep). For **changed** signatures, update both.
   For **removed** functions, remove both + any high-level callers.
2. For struct/enum changes, update `src/LadybugDB/Interop/NativeTypes.cs` and the `StructLayoutTests`
   ABI guards (`Marshal.SizeOf`/`OffsetOf`).
3. Surface genuinely new capabilities in the high-level API only if they're worth exposing; otherwise just
   keep the interop in parity.
4. Build both TFMs (`net10.0` + `netstandard2.0`), run the suite against a native built from the new commit.
5. If the header moved, update `capi_stable_since` in `upstream-engine.pin` to the new baseline.

---

## D) Engine-feature backlog (needs upstream)

Asks from consumers that the binding **cannot satisfy alone** because they require a C API addition in the
engine first. Each entry stays here until the upstream surface lands; advancing the pin (§A) is when to
re-check whether a new header makes one actionable.

### Fixed-size `FLOAT[N]` parameter binding (raised by Graphiti)

**Ask:** bind a value that carries its own fixed-size `FLOAT[N]` logical type, so Graphiti can drop the
per-call `CAST($search_vector AS FLOAT[<dim>])` it templates into its vector-search Cypher at 3 sites in
`LadybugSearchStatementBuilder.cs` (and stop baking the embedding dimension into the query string).

**Why the binding can't do it alone:** the engine C API (`src/include/c_api/lbug.h`) has **no fixed-`ARRAY`
value constructor** — only `lbug_value_create_list`, which produces a variable-length `LIST`, not a
fixed-size `ARRAY`. A bound value therefore cannot carry a `FLOAT[N]` logical type today. The header is
**byte-identical `v0.17.1..main`**, so engine `0.18.0` does **not** add one either — this is not unblocked
by the current pin bump.

**What's needed upstream, then here:** an `lbug_value_create_array`-style constructor in `lbug.h` (taking a
child logical type + fixed length), after which we add a typed binding helper so a bound `float[]`/
`IReadOnlyList<float>` materializes as a `FLOAT[N]` value. Until that ships, the supported route is the
existing `List<float>` + `CAST($v AS FLOAT[N])` path, which is pinned by
`SearchExtensionsTests.Vector_InlineCosineSimilarity_FiltersAndRanks` and works.

---

## Current state (2026-06-28)

- **Pin:** `upstream-engine.pin` → `LadybugDB/ladybug@d8277a8e5` (`v0.17.1-102-gd8277a8e5`, engine `0.18.0`).
- **C API check:** `git diff v0.17.1 d8277a8e5 -- src/include/c_api/` is **empty** — the header is
  byte-identical across the 102 commits since `v0.17.1`. **No interop changes were needed**; the only
  binding-relevant upstream change is the `connection.cpp`/`database.cpp` double-free-on-destroy fix
  (behavioral, picked up automatically by the source-built native).
- **Stable pin (`version.txt`):** `0.17.1.0` (latest published release) — unchanged.
- Verification of the source-built native across all 5 RIDs runs in CI (the dev workflow + `consume-published`).
