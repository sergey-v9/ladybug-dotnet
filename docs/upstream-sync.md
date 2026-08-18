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
| **Stable / release** | `version.txt` → latest published engine **release** (now `0.19.1` → `v0.19.1`) | downloaded release asset (`gh release download`); extensions downloaded by the engine at `INSTALL` | `0.19.1.x` | `ci.yml`, `release.yml` |
| **Main-tracking / dev** | `upstream-engine.pin` → an engine **commit** (now `v0.19.1` / `554c1e71`, engine `0.19.1`) | **built from engine source** at that commit, per RID — `lbug_shared` **and** the `fts`/`vector` extensions | `0.19.1-dev.*` prerelease | `github-packages-dev.yml` (fork-only) → GitHub Packages |

> **Why the dev track also builds the extensions.** Upstream publishes extensions only for **released
> tags**. When the dev pin rides ahead of the latest extension release, the engine's `INSTALL fts` can
> download a mismatched extension and fail on undefined symbols. The dev workflow therefore builds
> `fts`/`vector` from the same engine commit and ships them in `LadybugDB.Native.<rid>` **flat** next to the engine library —
> `runtimes/<rid>/native/lib<name>.lbug_extension` plus a `lbug_extension_abi_version.txt` marker. At
> load the binding pre-seeds the engine's cache from those files
> (`src/LadybugDB/Interop/ExtensionStaging.cs`), so both the binding's helpers and a consumer's raw
> `INSTALL`/`LOAD EXTENSION` resolve the ABI-matched build with no network. See §E.

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
   builds `lbug_shared` **and the `fts`/`vector` extensions** from `LadybugDB/ladybug@NEW` **per RID**
   (`win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`), stages the extensions + an
   `extension/ABI_VERSION` marker alongside the native, runs the full test suite against the host build,
   packs the `engine_version-dev.<run>.<attempt>.eng-<engineShort>` prerelease family, publishes it to
   GitHub Packages, and then the `consume-published` matrix restores the published packages on all five
   RIDs and runs a Cypher + `fts` + `vector` round-trip against the source-built native. **Green CI = the
   fork now rides that upstream head.** (Extensions are ABI-matched and shipped — see §E.)
9. **(optional) tag** the bindings commit, e.g. `git tag dev/0.19.1-eng-<short> && git push fork --tags`,
   so the exact (binding, engine) pair is recoverable by name.

### Reproduce the native build locally
```
pwsh scripts/build-native-from-pin.ps1            # builds engine@pin (lbug_shared + fts/vector) + stages
dotnet test LadybugDB.slnx -c Release             # run the suite against the source-built native
```
The script stages the extensions under `lib/runtimes/<host-rid>/native/extension/` with an `ABI_VERSION`
marker, exactly like CI, so the `fts`/`vector` `SearchExtensionsTests` exercise the ABI-matched build.
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
not added by engine `0.19.1` either — the current C API bump adds pushed-down SQL inspection only, not
fixed-size array value construction.

**What's needed upstream, then here:** an `lbug_value_create_array`-style constructor in `lbug.h` (taking a
child logical type + fixed length), after which we add a typed binding helper so a bound `float[]`/
`IReadOnlyList<float>` materializes as a `FLOAT[N]` value. Until that ships, the supported route is the
existing `List<float>` + `CAST($v AS FLOAT[N])` path, which is pinned by
`SearchExtensionsTests.Vector_InlineCosineSimilarity_FiltersAndRanks` and works.

---

## E) Shipping ABI-matched `fts`/`vector` extensions (dev track)

The dev track builds the engine from an **unreleased** commit, and upstream builds extensions only for
released tags — so there is no matching extension to download. The workflow builds and ships them; the
binding seeds them. The moving parts:

1. **Build (CI / local script).** `-DBUILD_EXTENSIONS="fts;vector"` plus the targets
   `lbug_fts_extension lbug_vector_extension` in the same configure as `lbug_shared`. `lbug` (the static
   lib Windows extensions link against) and `lbug_shared` share an OBJECT-library, so the extra Windows
   target is a link, not a second compile. Outputs land at `engine/extension/<name>/build/lib<name>.lbug_extension`.
2. **Stage / pack.** Staged **flat** under `runtimes/<rid>/native/` as `lib<name>.lbug_extension` with a
   `lbug_extension_abi_version.txt` marker — flat because NuGet reliably copies top-level
   `runtimes/<rid>/native/` files to consumer output, whereas nested subdirs are not guaranteed.
   `cake/native/LadybugDB.Native.Runtime.csproj` globs `runtimes\<rid>\native\**\*`, so they ship
   automatically — **no nuspec change**. (The test project copies the same files to its bin so the host
   Test gate exercises them.)
3. **Pre-seed (runtime).** `src/LadybugDB/Interop/ExtensionStaging.cs`, invoked once from
   `Native.EnsureLoaded()`, copies the bundled extensions into
   `{home}/.lbdb/extension/{ABI_VERSION}/{os}_{arch}/{name}/lib<name>.lbug_extension`
   (`home` = `%USERPROFILE%`/`$HOME`). The engine's `INSTALL` **skips the download when that file
   exists**, and `LOAD EXTENSION <name>` loads it — so even a consumer's raw-Cypher `INSTALL/LOAD` gets
   the matched build. Best-effort: failure leaves the engine's normal download path intact.

**Pin-bump gate (`ABI_VERSION` = engine `LBUG_EXTENSION_VERSION`).** The cache directory the engine reads
is keyed by the engine's compile-time `LBUG_EXTENSION_VERSION` (in engine `CMakeLists.txt`, currently
`0.19.0`), **not** `engine_version`. CI and the local script read it from the engine source at build
time, so a bump is picked up automatically — but if a future engine bump changes that constant, confirm a
green `fts`/`vector` round-trip after advancing the pin (the `SearchExtensionsTests` are the cross-check;
they FAIL, not skip, on an ABI/undefined-symbol error).

---

## Current state (2026-08-18)

- **Upstream released `v0.19.1`** (2026-08-04, commit `554c1e71`). Both tracks now point at it.
- **Pin:** `upstream-engine.pin` → `LadybugDB/ladybug@554c1e71` (`v0.19.1`, engine `0.19.1`) — pinned **at
  the release** (110 commits over the previous `0cda4fff` pin).
- **C API check:** `git diff v0.18.0 v0.19.1 -- src/include/c_api/` adds
  `lbug_connection_get_pushed_sql`; structs/enums are unchanged. The binding exposes it as
  `Connection.GetPushedSql`.
- **Stable pin (`version.txt`):** `0.19.1` → downloads the `v0.19.1` release natives.
- **Extension ABI:** `LBUG_EXTENSION_VERSION` is `0.19.0`. The dev track still source-builds + ships
  `fts`/`vector` and pre-seeds them (§E), so the ABI marker auto-updates to `0.19.0`.
- **Prior milestone:** first green cross-RID dev publish `0.18.0-dev.18.1.eng-d8277a8e5` (run #18, all five
  RIDs green through `build-native` → `publish` → `consume-published`; extension files verified in the
  published nupkgs). The `v0.19.1` bump republishes on the next `dev` push.
- Verification of the source-built native across all 5 RIDs runs in CI (the dev workflow + `consume-published`).
