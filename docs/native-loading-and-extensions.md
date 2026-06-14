# Native loading & engine extensions

This document describes how the `LadybugDB` binding locates and loads the native engine
(`liblbug` / `lbug_shared`) across target frameworks and operating systems, and why that loading
strategy is what makes Ladybug **engine extensions** (`json`, `fts`, `vector`, `httpfs`, …) usable
from C#.

## How the native engine is located

The shipped native asset is named `liblbug.so` / `liblbug.dylib` / `lbug_shared.dll`, but the
managed `DllImport`/`LibraryImport` declarations all use the canonical import name **`lbug_shared`**
(`Native.LibraryName`). The binding therefore has to remap that import name onto whichever file
actually ships for the current RID. There are two code paths, one per target framework:

| Target framework | Mechanism | Where |
|---|---|---|
| `net10.0` (net7.0+) | `NativeLibrary.SetDllImportResolver` registers a custom resolver | `Native.Resolve` |
| `netstandard2.0` | a static constructor **pre-loads** `liblbug.*` before the first P/Invoke | `Native.PreloadUnixGlobal` |

Both paths funnel through the single idempotent entry point `Native.EnsureLoaded()`
(`src/LadybugDB/Interop/Native.cs`). On net7+ it is invoked from a `[ModuleInitializer]` (and, as a
safety net, from the type's static constructor); on netstandard2.0 — which has neither
`SetDllImportResolver` nor `[ModuleInitializer]` — the static constructor is the equivalent hook,
because the runtime guarantees it runs before the first access to any `Native` member, i.e. before
the first `DllImport` call.

Candidate names are probed in priority order (`Native.GetCandidateNames`):

| OS | Order |
|---|---|
| Windows | `lbug_shared`, `lbug_shared.dll`, `liblbug` |
| macOS | `liblbug.dylib`, `liblbug`, `lbug_shared` |
| Linux | `liblbug.so`, `liblbug`, `lbug_shared` |

For each candidate the resolver first tries the **bundled** library next to the application
(`AppContext.BaseDirectory`, which is single-file- and AOT-safe), then the bare soname (so a
system-installed engine on the loader search path is also found). See
`examples/native-loading/README.md` for the bundled-vs-system staging scenarios.

### The netstandard2.0 P0 fix

Before this change the custom resolver was gated behind `#if NET7_0_OR_GREATER`, so **netstandard2.0
consumers on Linux/macOS had no resolver at all** — the runtime looked for `liblbug_shared.so` /
`lbug_shared`, never found the shipped `liblbug.*`, and failed to load the engine. The
netstandard2.0 path added here pre-`dlopen`s `liblbug.*` into the process (with global visibility,
below) during `Native`'s static initialization, so the subsequent `DllImport("lbug_shared")` lookup
is satisfied by the already-loaded library. Windows netstandard2.0 was never affected (the PE loader
finds `lbug_shared.dll` itself) and is skipped.

`EnsureLoaded()` is guarded by an `Interlocked` flag, so it runs its setup exactly once and never
throws: a genuinely missing engine surfaces later as a `DllNotFoundException` at the first real
P/Invoke, not as a loader crash.

## Why RTLD_GLOBAL

Ladybug engine extensions are installed and loaded through the normal Cypher path — there is no
dedicated extension C API:

```cypher
INSTALL json;
LOAD EXTENSION json;
```

`LOAD EXTENSION` `dlopen`s the extension's shared object, which must resolve symbols *exported by the
engine* at load time. The engine builds its non-API symbols with `visibility("hidden")` and its API
symbols with `visibility("default")` (`src/include/c_api/lbug.h`), so an extension `.so` can only
find the symbols it needs if `liblbug` itself was loaded with its dynamic symbols promoted into the
**global** namespace.

`.NET`'s `NativeLibrary.Load`/`TryLoad` loads with `RTLD_LOCAL` semantics on Linux/macOS, which keeps
those symbols private — so without intervention, `LOAD EXTENSION` fails with *undefined symbol*
errors. The sibling bindings all solve the same problem:

| Binding | Mechanism |
|---|---|
| Python | `ctypes.CDLL(path, mode=RTLD_GLOBAL \| RTLD_NOW)` |
| Java   | `dlopen(path, RTLD_LAZY \| RTLD_GLOBAL)` |
| Node   | `process.dlopen(module, path, RTLD_LAZY \| RTLD_GLOBAL)` |
| Rust   | `-rdynamic` link arg |

## What this binding does

On Linux/macOS the resolver (and the netstandard2.0 pre-load) call
`UnixNativeMethods.TryGlobalLoad` (`src/LadybugDB/Interop/UnixNativeMethods.cs`) **before** falling
back to `NativeLibrary.TryLoad`. `TryGlobalLoad` `dlopen`s the resolved library path with
`RTLD_NOW | RTLD_GLOBAL`:

- `RTLD_NOW` (`0x2`) resolves all undefined symbols immediately;
- `RTLD_GLOBAL` promotes the engine's symbols into the global scope — its value is `0x100` on
  glibc/Linux and `0x8` on macOS, so the helper branches on OS.

`dlopen` itself is reached through `libdl.so.2` (modern glibc) with a fallback to a bare `libdl`
(covering musl/Alpine and macOS, where `dl*` live in libc/libSystem). On net7+ the handle returned by
`TryGlobalLoad` is adopted by the runtime (the resolver returns it), so every subsequent P/Invoke
binds to the globally-scoped engine and `LOAD EXTENSION` resolves cleanly. If the global `dlopen`
fails for any reason, the resolver falls back to `NativeLibrary.TryLoad`, preserving the
bundled/system loading behavior described in `examples/native-loading/README.md`.

**Windows is unaffected.** The PE loader has no local/global scope distinction, so `TryGlobalLoad` is
a no-op there and the resolver skips straight to the normal load path.

## Using extensions from C#

Extensions run through the normal query path. Today you can install and load them directly:

```csharp
using var db = new Database(path);
using var conn = new Connection(db);

conn.Query("INSTALL json");          // contacts the extension repository — needs network
conn.Query("LOAD EXTENSION json");   // dlopen()s the extension .so; resolves engine symbols

using var result = conn.Query("RETURN cast('lbug' AS JSON) AS j");
```

Ergonomic `Connection.InstallExtension(name)` / `LoadExtension(name)` helpers (which validate the
name and issue the `INSTALL` / `LOAD EXTENSION` statements for you) are added in a later phase; the
**loader** behavior documented above is the part that makes either form work on Linux/macOS.

## Verifying the fix

The loader's flag math and platform gating are covered by ungated unit tests
(`test/LadybugDB.Tests/UnixLoaderTests.cs`, `ResolverTests.cs`) that run on every OS without a native
engine. The end-to-end behavior — that a real extension (`json`) loads and resolves the engine's
symbols rather than failing with *undefined symbol* — is exercised by native-gated, offline-tolerant
tests on a host where the native library and the extension repository are reachable.
