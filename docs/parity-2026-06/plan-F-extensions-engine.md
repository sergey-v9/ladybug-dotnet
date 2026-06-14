# WS-F — Engine Extensions + Native Symbol Visibility Implementation Plan
> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]` checkboxes.

**Goal:** Ship `Connection.InstallExtension`/`LoadExtension` sugar over the normal query path and make `liblbug` load with **global symbol visibility** (`dlopen(RTLD_NOW|RTLD_GLOBAL)`) on Linux/macOS so a dynamically-loaded extension `.so` resolves the engine's exported symbols.

**Architecture:** `Connection.Extensions.cs` is a `partial` of the existing `Connection` class; the two helpers just run `INSTALL <name>` / `LOAD EXTENSION <name>` through the existing `Query(...)` path (no new C-API surface exists for extensions). The native loader fix lives in the `SetDllImportResolver` callback in `Interop/Native.cs`: a tiny `libdl` P/Invoke (`UnixNativeMethods.DlOpen`) loads the resolved candidate path with `RTLD_NOW|RTLD_GLOBAL` on Unix and adopts that handle via `NativeLibrary.SetDllImportResolver`'s return value, so the engine's symbols land in the global namespace exactly like Python (`RTLD_GLOBAL|RTLD_NOW`), Java/Node (`RTLD_LAZY|RTLD_GLOBAL`), and Rust (`-rdynamic`) do. Windows is unaffected.

**Tech Stack:** C# (dual TFM `net10.0;netstandard2.0`), P/Invoke (`NativeLibrary` on net7+, `libdl` `dlopen`), xUnit + `Xunit.SkippableFact`, `TestEnvironment.NativeAvailable` native gate.

---

## Files

**Create**
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Connection.Extensions.cs` — `partial class Connection` with `InstallExtension`/`LoadExtension`.
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Interop\UnixNativeMethods.cs` — `libdl` `dlopen`/`dlerror` P/Invoke + `TryGlobalLoad` helper, compiled on **both** TFMs.
- `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\ExtensionTests.cs` — native-gated install/load test + pure-managed argument-validation tests.
- `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\UnixLoaderTests.cs` — pure-managed tests for the `libdl` constants/flags and platform gating (no native engine needed).
- `W:\code\ladybug\tools\csharp_api\docs\native-loading-and-extensions.md` — documents loader behavior (RTLD_GLOBAL) and the extension helpers.

**Modify**
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Interop\Native.cs` — make the net7+ `Resolve` callback prefer a global-visibility `dlopen` on Unix; mark the existing class `partial` if needed (it already is `internal static partial class Native`).
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Connection.cs` — change the class declaration to `public sealed partial class Connection` so the `Connection.Extensions.cs` partial compiles. (Single-token edit; coordinate — WS-D also needs `partial` here. See Open Questions.)

**Test**
- Run: `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug`
- Build (both TFMs): `dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug`

---

## Context the worker must hold

- **No extension C API exists.** Extensions are installed/loaded purely through Cypher: `INSTALL <name>;` then `LOAD EXTENSION <name>;`. Engine error text confirms the exact spelling: *"You can install and load the extension by running 'INSTALL JSON; LOAD EXTENSION JSON;'."* (`W:\code\ladybug\extension\json\test\error.test:14`). So the helpers run those statements through `Connection.Query(...)`.
- **The real fix is symbol visibility.** The engine builds non-API symbols with `__attribute__((visibility("hidden")))` and API symbols with `visibility("default")` (`W:\code\ladybug\src\include\c_api\lbug.h:17-19`). A dynamically-loaded extension `.so` resolves the engine's **C++** symbols at `dlopen` time; that only works if `liblbug` was itself loaded with `RTLD_GLOBAL` (its dynamic symbols promoted into the global scope). `.NET`'s `NativeLibrary.Load`/`TryLoad` uses `RTLD_LOCAL` semantics, so extensions get *undefined symbol* errors. This is the exact problem Java/Node/Rust already solved:
  - Java: `dlopen(path, RTLD_LAZY | RTLD_GLOBAL)` in `W:\code\ladybug\tools\java_api\src\jni\lbug_java.cpp:538`.
  - Node: `process.dlopen(module, path, constants.RTLD_LAZY | constants.RTLD_GLOBAL)` in `W:\code\ladybug\tools\nodejs_api\src_js\lbug_native.js:16-20`.
  - Python: `ctypes.CDLL(path, mode=RTLD_GLOBAL | RTLD_NOW)` in `W:\code\ladybug\tools\python_api\src_py\_lbug_capi.py:189-190` — **the closest analogue to our P/Invoke `dlopen`.**
  - Rust: `cargo:rustc-link-arg=-rdynamic` in `W:\code\ladybug\tools\rust_api\build.rs:21`.
- **Current resolver** (`W:\code\ladybug\tools\csharp_api\src\LadybugDB\Interop\Native.cs:35-66`) is `#if NET7_0_OR_GREATER` only and loads via `NativeLibrary.TryLoad(candidate, assembly, searchPath, out handle)` over `GetCandidateNames()`. There is **no ns2.0 resolver at all today** — that is a separate P0 fix (spec §3, `02-parity-matrix.md:64`) tracked outside this WS; see Open Questions for coordination.
- **`RTLD_NOW`/`RTLD_GLOBAL` values are libc-stable** on Linux/macOS: `RTLD_NOW = 0x2`. `RTLD_GLOBAL` is `0x100` on glibc/Linux and `0x8` on macOS. The loader helper must branch on OS for `RTLD_GLOBAL`.
- **Native staging for tests:** the test csproj copies `lib/runtimes/<rid>/native/*` next to the test output (`LadybugDB.Tests.csproj:24-29`); `TestEnvironment.NativeAvailable` (`TestEnvironment.cs:14,38-57`) is the gate.
- **`INSTALL <name>` reaches the network** (downloads from the extension repository). The native-gated test must therefore also tolerate an offline machine: it attempts install+load and `Skip`s if the failure is a download/network failure, but does **not** skip on a genuine symbol-resolution failure (that is the bug we are proving fixed).

---

## TASK 1 — `Connection` becomes `partial` (enables the partial file)

- [ ] **Write the failing test.** Create `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\ExtensionTests.cs` with a compile-time-only test that references the not-yet-existing partial methods so the project fails to build until Tasks 1–2 land:
  ```csharp
  using System;
  using LadybugDB;
  using Xunit;

  namespace LadybugDB.Tests;

  /// <summary>
  /// Engine-extension helpers (<see cref="Connection.InstallExtension"/>/<see cref="Connection.LoadExtension"/>)
  /// and the global-symbol-visibility loader fix. Managed-only argument checks are ungated;
  /// the real install/load round-trip is native-gated and offline-tolerant.
  /// </summary>
  public sealed class ExtensionTests
  {
      [Fact]
      public void InstallExtension_NullName_Throws()
      {
          // Resolves only once Connection.Extensions.cs compiles; proves the method exists.
          var ex = Record.Exception(() => typeof(Connection).GetMethod(nameof(Connection.InstallExtension)));
          Assert.Null(ex);
          Assert.NotNull(typeof(Connection).GetMethod(nameof(Connection.InstallExtension)));
          Assert.NotNull(typeof(Connection).GetMethod(nameof(Connection.LoadExtension)));
      }
  }
  ```
- [ ] **Run it / expect FAIL (build error).** `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug` — expected: build fails because `Connection.InstallExtension`/`LoadExtension` do not exist yet.
- [ ] **Minimal implementation.** In `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Connection.cs:12`, change:
  ```csharp
  public sealed class Connection : IDisposable
  ```
  to:
  ```csharp
  public sealed partial class Connection : IDisposable
  ```
  (Only the `partial` keyword is added; nothing else in `Connection.cs` changes.)
- [ ] **Run / still FAIL.** Same command — still fails (the methods themselves are added in Task 2). This confirms the `partial` edit alone is not enough and isolates the change.
- [ ] **Commit.** `git add -A && git commit -m "WS-F: make Connection partial for the extensions helper"`

---

## TASK 2 — `InstallExtension` / `LoadExtension` over the query path

- [ ] **Write the failing test.** Replace the body of `ExtensionTests.cs` with the managed argument-validation tests plus the native-gated round-trip:
  ```csharp
  using System;
  using LadybugDB;
  using Xunit;

  namespace LadybugDB.Tests;

  public sealed class ExtensionTests
  {
      [SkippableFact]
      public void InstallExtension_NullName_ThrowsArgumentNull()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);
              Assert.Throws<ArgumentNullException>(() => conn.InstallExtension(null!));
              Assert.Throws<ArgumentNullException>(() => conn.LoadExtension(null!));
          }
          finally { TestEnvironment.TryDelete(dbPath); }
      }

      [SkippableFact]
      public void InstallExtension_EmptyName_ThrowsArgument()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);
              Assert.Throws<ArgumentException>(() => conn.InstallExtension("   "));
              Assert.Throws<ArgumentException>(() => conn.LoadExtension(""));
          }
          finally { TestEnvironment.TryDelete(dbPath); }
      }

      [SkippableFact]
      public void InstallAndLoad_Json_ResolvesExtensionFunction()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              // INSTALL reaches the extension repository over the network; LOAD EXTENSION is the
              // step that dlopen()s the extension .so and triggers symbol resolution against liblbug.
              try
              {
                  conn.InstallExtension("json");
              }
              catch (LadybugQueryException ex) when (IsOffline(ex))
              {
                  throw new Xunit.SkipException("Extension repository is unreachable (offline): " + ex.Message);
              }

              // If LOAD fails with an "undefined symbol" / "cannot resolve" message, that is the
              // RTLD_GLOBAL bug this workstream fixes — do NOT skip; let it fail loudly.
              conn.LoadExtension("json");

              // Prove a function defined *in the extension* actually works end-to-end.
              using QueryResult result = conn.Query("RETURN cast('lbug' AS JSON) AS j");
              Assert.True(result.IsSuccess);
              Assert.Equal(1UL, result.ColumnCount);
              Assert.Equal(1UL, result.RowCount);
          }
          finally { TestEnvironment.TryDelete(dbPath); }
      }

      private static bool IsOffline(LadybugQueryException ex)
      {
          string m = ex.Message;
          return m.Contains("download", StringComparison.OrdinalIgnoreCase)
              || m.Contains("network", StringComparison.OrdinalIgnoreCase)
              || m.Contains("Could not connect", StringComparison.OrdinalIgnoreCase)
              || m.Contains("curl", StringComparison.OrdinalIgnoreCase)
              || m.Contains("Failed to read", StringComparison.OrdinalIgnoreCase)
              || m.Contains("io error", StringComparison.OrdinalIgnoreCase)
              || m.Contains("HTTP", StringComparison.OrdinalIgnoreCase);
      }
  }
  ```
- [ ] **Run it / expect FAIL (build error).** `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug` — expected: still a build failure (`InstallExtension`/`LoadExtension` undefined). On a machine with no native lib, the native-gated tests will Skip once the build compiles.
- [ ] **Minimal implementation.** Create `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Connection.Extensions.cs`:
  ```csharp
  using System;

  namespace LadybugDB;

  /// <summary>
  /// Engine-extension helpers. Ladybug has no dedicated C API for extensions: installation and
  /// loading run through the normal Cypher query path (<c>INSTALL &lt;name&gt;</c> / <c>LOAD
  /// EXTENSION &lt;name&gt;</c>). For a dynamically loaded extension to resolve the engine's symbols
  /// on Linux/macOS, <c>liblbug</c> must have been loaded with global symbol visibility — see the
  /// resolver in <c>Interop/Native.cs</c> and <c>docs/native-loading-and-extensions.md</c>.
  /// </summary>
  public sealed partial class Connection
  {
      /// <summary>
      /// Installs an engine extension by name (runs <c>INSTALL &lt;name&gt;</c>). Installation contacts
      /// the extension repository and may require network access.
      /// </summary>
      /// <param name="name">The extension name, e.g. <c>"json"</c>, <c>"fts"</c>, <c>"vector"</c>.</param>
      /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
      /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
      /// <exception cref="LadybugQueryException">Installation fails (e.g. unknown extension or no network).</exception>
      public void InstallExtension(string name)
      {
          string ext = ValidateExtensionName(name);
          using QueryResult result = Query("INSTALL " + ext);
          _ = result;
      }

      /// <summary>
      /// Loads a previously installed engine extension by name (runs <c>LOAD EXTENSION &lt;name&gt;</c>).
      /// </summary>
      /// <param name="name">The extension name, e.g. <c>"json"</c>, <c>"fts"</c>, <c>"vector"</c>.</param>
      /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
      /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
      /// <exception cref="LadybugQueryException">Loading fails (e.g. extension not installed, or — on a
      /// broken loader — the extension cannot resolve the engine's symbols).</exception>
      public void LoadExtension(string name)
      {
          string ext = ValidateExtensionName(name);
          using QueryResult result = Query("LOAD EXTENSION " + ext);
          _ = result;
      }

      private static string ValidateExtensionName(string name)
      {
          if (name is null)
          {
              throw new ArgumentNullException(nameof(name));
          }

          string trimmed = name.Trim();
          if (trimmed.Length == 0)
          {
              throw new ArgumentException("Extension name must not be empty or whitespace.", nameof(name));
          }

          // Defensive: an extension name is a bare identifier; reject statement-injection characters so
          // the helper can only ever issue a single INSTALL/LOAD statement.
          foreach (char c in trimmed)
          {
              bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                  || (c >= '0' && c <= '9') || c == '_';
              if (!ok)
              {
                  throw new ArgumentException(
                      "Extension name must be a bare identifier (letters, digits, underscore).",
                      nameof(name));
              }
          }

          return trimmed;
      }
  }
  ```
- [ ] **Run / expect PASS (or SKIP without native).** `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug` — the managed argument-validation paths run when native is present; on a host with native + network the JSON round-trip passes; offline hosts Skip the round-trip only. Confirm `0 failed`.
- [ ] **Commit.** `git add -A && git commit -m "WS-F: add Connection.InstallExtension/LoadExtension over the query path"`

---

## TASK 3 — Unix `dlopen` global-visibility helper (`UnixNativeMethods`)

This task adds the loader primitive on **both** TFMs. It is pure interop with constants; its flag/constant logic is unit-testable without the engine.

- [ ] **Write the failing test.** Create `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\UnixLoaderTests.cs`:
  ```csharp
  using System;
  using System.Runtime.InteropServices;
  using LadybugDB.Interop;
  using Xunit;

  namespace LadybugDB.Tests;

  /// <summary>
  /// Pure-managed checks for the global-visibility loader primitive. No native engine required:
  /// we assert the platform-correct RTLD flag math and that the helper is a no-op on Windows.
  /// </summary>
  public sealed class UnixLoaderTests
  {
      [Fact]
      public void RtldFlags_AreLibcStable()
      {
          Assert.Equal(0x2, UnixNativeMethods.RtldNow);
          // RTLD_GLOBAL differs by platform: 0x100 on glibc/Linux, 0x8 on macOS.
          int expectedGlobal = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 0x8 : 0x100;
          Assert.Equal(expectedGlobal, UnixNativeMethods.RtldGlobal);
          Assert.Equal(UnixNativeMethods.RtldNow | UnixNativeMethods.RtldGlobal, UnixNativeMethods.GlobalLoadFlags);
      }

      [Fact]
      public void TryGlobalLoad_OnWindows_ReturnsFalseAndZero()
      {
          if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
          {
              return; // Windows-only assertion.
          }

          bool loaded = UnixNativeMethods.TryGlobalLoad("lbug_shared", out IntPtr handle);
          Assert.False(loaded);
          Assert.Equal(IntPtr.Zero, handle);
      }

      [Fact]
      public void TryGlobalLoad_BogusPath_ReturnsFalse()
      {
          if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
          {
              return; // Unix-only: a missing soname must fail cleanly, not throw.
          }

          bool loaded = UnixNativeMethods.TryGlobalLoad("definitely-not-a-real-lib.so.999", out IntPtr handle);
          Assert.False(loaded);
          Assert.Equal(IntPtr.Zero, handle);
      }
  }
  ```
- [ ] **Run it / expect FAIL (build error).** `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug` — expected: build fails (`UnixNativeMethods` does not exist). Note: `UnixNativeMethods` is `internal`; the test project already has `InternalsVisibleTo LadybugDB.Tests` (`LadybugDB.csproj:16`).
- [ ] **Minimal implementation.** Create `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Interop\UnixNativeMethods.cs`:
  ```csharp
  using System;
  using System.Runtime.InteropServices;

  namespace LadybugDB.Interop;

  /// <summary>
  /// Loads the native engine on Linux/macOS with <c>RTLD_NOW | RTLD_GLOBAL</c> so a dynamically
  /// loaded extension <c>.so</c> can resolve the engine's exported symbols (the engine builds its
  /// non-API symbols with hidden visibility; promoting the library to the global scope is what the
  /// Java/Node/Python/Rust bindings do via <c>RTLD_GLOBAL</c>/<c>-rdynamic</c>). On Windows this is a
  /// no-op: the PE loader does not have the local/global scope distinction and the OS resolver
  /// already handles extension symbol resolution.
  /// </summary>
  internal static class UnixNativeMethods
  {
      /// <summary>Resolve all undefined symbols immediately (POSIX <c>RTLD_NOW</c>, libc-stable 0x2).</summary>
      internal const int RtldNow = 0x2;

      /// <summary>
      /// Promote the loaded library's symbols into the global namespace. The value differs by platform:
      /// <c>0x100</c> on glibc/Linux, <c>0x8</c> on macOS.
      /// </summary>
      internal static int RtldGlobal =>
          RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 0x8 : 0x100;

      /// <summary>The combined flags handed to <c>dlopen</c> (<c>RTLD_NOW | RTLD_GLOBAL</c>).</summary>
      internal static int GlobalLoadFlags => RtldNow | RtldGlobal;

      // libdl entry points. ".so.2" is the modern glibc soname; the bare "libdl" string is also tried
      // because on musl (Alpine) and macOS dl* live in libc/libSystem and a bare "dl" resolves there.
      [DllImport("libdl.so.2", EntryPoint = "dlopen", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
      private static extern IntPtr DlOpenGlibc([MarshalAs(UnmanagedType.LPStr)] string fileName, int flags);

      [DllImport("libdl", EntryPoint = "dlopen", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
      private static extern IntPtr DlOpenLegacy([MarshalAs(UnmanagedType.LPStr)] string fileName, int flags);

      /// <summary>
      /// Attempts to <c>dlopen(fileName, RTLD_NOW | RTLD_GLOBAL)</c>. Returns <see langword="false"/> on
      /// Windows, on any <c>DllNotFoundException</c>/<c>EntryPointNotFoundException</c> resolving libdl,
      /// or when <c>dlopen</c> returns <see cref="IntPtr.Zero"/>.
      /// </summary>
      internal static bool TryGlobalLoad(string fileName, out IntPtr handle)
      {
          handle = IntPtr.Zero;
          if (fileName is null || RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
          {
              return false;
          }

          int flags = GlobalLoadFlags;
          try
          {
              handle = DlOpenGlibc(fileName, flags);
          }
          catch (DllNotFoundException)
          {
              handle = IntPtr.Zero;
          }
          catch (EntryPointNotFoundException)
          {
              handle = IntPtr.Zero;
          }

          if (handle == IntPtr.Zero)
          {
              try
              {
                  handle = DlOpenLegacy(fileName, flags);
              }
              catch (DllNotFoundException)
              {
                  handle = IntPtr.Zero;
              }
              catch (EntryPointNotFoundException)
              {
                  handle = IntPtr.Zero;
              }
          }

          return handle != IntPtr.Zero;
      }
  }
  ```
- [ ] **Run / expect PASS.** `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug` — the three `UnixLoaderTests` pass on every OS (each branches on the runtime platform). Confirm `0 failed`.
- [ ] **Commit.** `git add -A && git commit -m "WS-F: add Unix dlopen RTLD_GLOBAL loader primitive (both TFMs)"`

---

## TASK 4 — Wire global-visibility load into the net7+ resolver

The resolver must prefer the global-visibility `dlopen` on Unix, then fall back to `NativeLibrary.TryLoad` (which still covers Windows and the bundled/system search semantics documented in `examples/native-loading/README.md`).

- [ ] **Write the failing test.** Add to `ExtensionTests.cs` a native-gated assertion that the load path used is global-visibility-capable. Because we cannot read `RTLD_GLOBAL` back from a handle portably, assert the observable behavior: with native present, `LoadExtension("json")` must NOT fail with an undefined-symbol error (the bug's signature). Append this method to `ExtensionTests`:
  ```csharp
      [SkippableFact]
      public void LoadExtension_DoesNotFailWithUndefinedSymbol()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");
          if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                  System.Runtime.InteropServices.OSPlatform.Windows))
          {
              return; // Symbol-visibility scoping is a Unix-only concern.
          }

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);
              try
              {
                  conn.InstallExtension("json");
                  conn.LoadExtension("json");
              }
              catch (LadybugQueryException ex)
              {
                  // Offline is acceptable; an undefined-symbol failure is the regression we guard against.
                  bool undefinedSymbol = ex.Message.Contains("undefined symbol", StringComparison.OrdinalIgnoreCase)
                      || ex.Message.Contains("cannot resolve", StringComparison.OrdinalIgnoreCase)
                      || ex.Message.Contains("symbol not found", StringComparison.OrdinalIgnoreCase);
                  Assert.False(undefinedSymbol,
                      "LOAD EXTENSION failed resolving engine symbols — liblbug was not loaded with RTLD_GLOBAL: " + ex.Message);
                  Skip.If(true, "Extension install/load unavailable (likely offline): " + ex.Message);
              }
          }
          finally { TestEnvironment.TryDelete(dbPath); }
      }
  ```
- [ ] **Run it / expect FAIL or SKIP.** `dotnet test ... LadybugDB.Tests.csproj -c Debug`. On a Linux host with native+network and the **un-patched** resolver, this fails with the undefined-symbol assertion (proving the bug). Without native, it Skips. On Windows it returns early. (If you cannot reproduce on Linux locally, rely on the negative assertion landing in CI's native-gated job; the test is still correct.)
- [ ] **Minimal implementation.** Edit `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Interop\Native.cs`. Replace the `Resolve` method body (lines 35-51) so Unix tries the global load first:
  ```csharp
      private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
      {
          if (libraryName != LibraryName)
          {
              return IntPtr.Zero;
          }

          // On Linux/macOS the engine must be promoted into the global symbol scope (RTLD_GLOBAL) so a
          // dynamically loaded extension .so resolves its symbols. NativeLibrary.Load uses RTLD_LOCAL
          // semantics, so we dlopen() the resolved path ourselves first. See
          // docs/native-loading-and-extensions.md. Windows has no such scoping and falls straight through.
          if (!OperatingSystem.IsWindows())
          {
              foreach (string candidate in GetCandidateNames())
              {
                  if (UnixNativeMethods.TryGlobalLoad(candidate, out IntPtr globalHandle))
                  {
                      return globalHandle;
                  }
              }
          }

          foreach (string candidate in GetCandidateNames())
          {
              if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out IntPtr handle))
              {
                  return handle;
              }
          }

          return IntPtr.Zero;
      }
  ```
  Note: `GetCandidateNames()` returns bare sonames (`liblbug.so`, `liblbug`, `lbug_shared`). For the **bundled** case where the engine sits next to the assembly, a bare-soname `dlopen` may not find it; the subsequent `NativeLibrary.TryLoad(candidate, assembly, ...)` covers that path (it probes the assembly directory). To make the bundled engine load *globally*, also try the assembly-relative absolute path first — append it at the top of the candidate loop:
  ```csharp
          string? assemblyDir = System.IO.Path.GetDirectoryName(assembly.Location);
  ```
  and inside the Unix branch, before the bare-soname loop, try each `GetCandidateNames()` joined to `assemblyDir` (when non-empty) so a bundled `liblbug.so` is `dlopen`ed by absolute path with `RTLD_GLOBAL`. Concretely, replace the Unix branch with:
  ```csharp
          if (!OperatingSystem.IsWindows())
          {
              string? assemblyDir = System.IO.Path.GetDirectoryName(assembly.Location);
              foreach (string candidate in GetCandidateNames())
              {
                  if (!string.IsNullOrEmpty(assemblyDir))
                  {
                      string full = System.IO.Path.Combine(assemblyDir!, candidate);
                      if (System.IO.File.Exists(full) && UnixNativeMethods.TryGlobalLoad(full, out IntPtr bundled))
                      {
                          return bundled;
                      }
                  }

                  if (UnixNativeMethods.TryGlobalLoad(candidate, out IntPtr system))
                  {
                      return system;
                  }
              }
          }
  ```
  Leave `GetCandidateNames()` (lines 53-66) and everything else in `Native.cs` unchanged.
- [ ] **Run / expect PASS (or SKIP without native/network).** `dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug` then `dotnet test ... LadybugDB.Tests.csproj -c Debug`. With native+network on Linux the JSON round-trip and the undefined-symbol guard both pass; on Windows they pass/return-early; offline hosts Skip the network-dependent assertions. Confirm `0 failed`.
- [ ] **Commit.** `git add -A && git commit -m "WS-F: load liblbug with RTLD_GLOBAL on Unix so extensions resolve engine symbols"`

---

## TASK 5 — Build both TFMs and confirm no ns2.0 regression

`UnixNativeMethods` compiles on both TFMs; `Resolve` is net7+-only (the `#if NET7_0_OR_GREATER` block). On ns2.0 the resolver does not yet exist (separate P0 fix — see Open Questions), but `UnixNativeMethods` must still compile and is ready for the ns2.0 resolver to call `TryGlobalLoad` when that fix lands.

- [ ] **Write the failing test.** Add a guard test asserting `UnixNativeMethods.TryGlobalLoad` is reachable on the ns2.0-shaped API surface (compile-only). Append to `UnixLoaderTests.cs`:
  ```csharp
      [Fact]
      public void GlobalLoadFlags_IncludeNowAndGlobal()
      {
          // Guards the ns2.0 resolver contract: whoever wires the ns2.0 path must use these exact flags.
          Assert.True((UnixNativeMethods.GlobalLoadFlags & UnixNativeMethods.RtldNow) == UnixNativeMethods.RtldNow);
          Assert.True((UnixNativeMethods.GlobalLoadFlags & UnixNativeMethods.RtldGlobal) == UnixNativeMethods.RtldGlobal);
      }
  ```
- [ ] **Run it / expect PASS on net10.0.** `dotnet test ... LadybugDB.Tests.csproj -c Debug` (the test project is `net10.0` only — `LadybugDB.Tests.csproj:4`). Confirm `0 failed`.
- [ ] **Build the library on both TFMs / expect PASS.** `dotnet build W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj -c Debug` — must succeed for `net10.0` **and** `netstandard2.0` (the `<TargetFrameworks>net10.0;netstandard2.0</TargetFrameworks>` in `LadybugDB.csproj:4`). Verify the `netstandard2.0` output is produced and there are no `OperatingSystem.IsWindows()`-style net7+ APIs leaking into shared code (it is confined to the `#if NET7_0_OR_GREATER` block in `Native.cs`; `UnixNativeMethods` uses `RuntimeInformation`, which is ns2.0-safe).
- [ ] **Full solution build / expect PASS.** `dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug`. Confirm `Build succeeded`.
- [ ] **Commit.** `git add -A && git commit -m "WS-F: guard the ns2.0 global-load flag contract; verify dual-TFM build"`

---

## TASK 6 — Document the loader behavior + extension helpers

- [ ] **Write the doc (no test).** Create `W:\code\ladybug\tools\csharp_api\docs\native-loading-and-extensions.md`:
  ```markdown
  # Native loading & engine extensions

  ## Why RTLD_GLOBAL

  Ladybug engine extensions (`json`, `fts`, `vector`, `httpfs`, …) are installed and loaded through
  the normal Cypher path — there is no dedicated extension C API:

  ```cypher
  INSTALL json;
  LOAD EXTENSION json;
  ```

  `LOAD EXTENSION` `dlopen`s the extension's shared object, which must resolve symbols *exported by the
  engine* at load time. The engine builds its non-API symbols with `visibility("hidden")`
  (`src/include/c_api/lbug.h`), so an extension can only find them if `liblbug` itself was loaded with
  its dynamic symbols promoted into the **global** namespace.

  `.NET`'s `NativeLibrary.Load`/`TryLoad` loads with `RTLD_LOCAL` semantics on Linux/macOS, which keeps
  those symbols private — so without intervention, `LOAD EXTENSION` fails with *undefined symbol* errors.
  The sibling bindings solve the same problem:

  | Binding | Mechanism |
  |---|---|
  | Python | `ctypes.CDLL(path, mode=RTLD_GLOBAL \| RTLD_NOW)` |
  | Java   | `dlopen(path, RTLD_LAZY \| RTLD_GLOBAL)` |
  | Node   | `process.dlopen(module, path, RTLD_LAZY \| RTLD_GLOBAL)` |
  | Rust   | `-rdynamic` link arg |

  ## What this binding does

  The native-library resolver (`src/LadybugDB/Interop/Native.cs`) registered via
  `NativeLibrary.SetDllImportResolver` first calls `UnixNativeMethods.TryGlobalLoad`, which
  `dlopen`s the resolved library path with `RTLD_NOW | RTLD_GLOBAL` (`RTLD_GLOBAL` is `0x100` on
  glibc/Linux and `0x8` on macOS). The returned handle is adopted by the runtime, so all subsequent
  P/Invoke calls bind to the globally-scoped engine and `LOAD EXTENSION` resolves cleanly. If the
  global `dlopen` fails (e.g. libdl resolution issues), the resolver falls back to
  `NativeLibrary.TryLoad`, preserving the bundled/system loading behavior described in
  `examples/native-loading/README.md`. **Windows is unaffected** — the PE loader has no local/global
  scope distinction, so the resolver skips the `dlopen` path entirely there.

  ## Using extensions from C#

  ```csharp
  using var db = new Database(path);
  using var conn = new Connection(db);

  conn.InstallExtension("json");   // INSTALL json;  (contacts the extension repository — needs network)
  conn.LoadExtension("json");      // LOAD EXTENSION json;

  using var result = conn.Query("RETURN cast('lbug' AS JSON) AS j");
  ```

  `InstallExtension`/`LoadExtension` validate the name as a bare identifier and run a single
  `INSTALL`/`LOAD EXTENSION` statement through the connection's query path.
  ```
- [ ] **Verify the doc renders / links resolve.** Open the file and confirm the relative paths (`src/include/c_api/lbug.h`, `src/LadybugDB/Interop/Native.cs`, `examples/native-loading/README.md`) are correct relative to the repo. No command needed beyond a visual read.
- [ ] **Commit.** `git add -A && git commit -m "WS-F: document RTLD_GLOBAL loader behavior and extension helpers"`

---

## Self-review / done criteria

Tied to the WS-F Done column ("install/load helpers + global-symbol load; native-gated test"):

- [ ] `Connection.InstallExtension(string)` and `Connection.LoadExtension(string)` exist on the public surface, match §4.5 exactly (run `INSTALL <name>` / `LOAD EXTENSION <name>` via the query path), validate the name, and live in the `Connection.Extensions.cs` **partial** file (no edits to B's `Connection.cs` beyond adding `partial`).
- [ ] The net7+ resolver in `Interop/Native.cs` loads `liblbug` with `RTLD_NOW | RTLD_GLOBAL` on Linux/macOS (via `UnixNativeMethods.TryGlobalLoad`), tries the bundled assembly-relative path first, and falls back to `NativeLibrary.TryLoad`; Windows is untouched.
- [ ] `UnixNativeMethods` compiles on **both** `net10.0` and `netstandard2.0` and exposes `RtldNow`/`RtldGlobal`/`GlobalLoadFlags`/`TryGlobalLoad` so the separate ns2.0 resolver fix can reuse it.
- [ ] Pure-managed tests (`UnixLoaderTests`, the argument-validation `ExtensionTests`) run **ungated** and pass on every OS.
- [ ] The native-gated `ExtensionTests.InstallAndLoad_Json_ResolvesExtensionFunction` and `LoadExtension_DoesNotFailWithUndefinedSymbol` are `SkippableFact` gated on `TestEnvironment.NativeAvailable`, are **offline-tolerant** (Skip on network failure), but **fail loudly** on an undefined-symbol/symbol-resolution error (the actual bug).
- [ ] `dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug` is green on both TFMs.
- [ ] `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug` reports `0 failed` (native-gated tests Skip without native/network; with `LADYBUG_REQUIRE_NATIVE=1` on a native-staged Linux host they run for real).
- [ ] `docs/native-loading-and-extensions.md` documents the RTLD_GLOBAL rationale, the cross-binding table, and the helper usage.
- [ ] Loader behavior verified against a real extension (`json`) under native-gated tests, satisfying spec §5.6 ("Verified against a real extension … under the native-gated tests").

## Open Questions (cross-workstream)

- **`Native.cs` resolver ownership overlaps with the ns2.0 P0 fix.** The high-level plan lists `Interop/Native.cs` under **both** WS-A (interop expansion, owns `Interop/Native.cs`) and WS-F (resolver). The ns2.0-resolver P0 fix (spec §3: "ns2.0 native loading on Unix") is not assigned a letter in §6 and is described as a Phase-1 "load fix." This plan deliberately (a) keeps the net7+ resolver change minimal and self-contained, and (b) factors the `dlopen` primitive into `UnixNativeMethods` so the ns2.0 resolver — whoever lands it — calls `UnixNativeMethods.TryGlobalLoad` with the same `GlobalLoadFlags`. **Coordination needed:** confirm whether the ns2.0 resolver is authored here (WS-F), in WS-A, or as a standalone Phase-1 fix, to avoid a merge conflict on `Native.cs`. If WS-F must also author the ns2.0 resolver, add a Task 4b that introduces an ns2.0 `[ModuleInitializer]`-equivalent (ns2.0 has no `[ModuleInitializer]`; use a static-constructor trigger on first `Native` access or a `RuntimeHelpers.RunModuleConstructor` shim) that calls `TryGlobalLoad` directly.
- **`Connection` `partial` keyword** is also required by WS-D (`Connection.Async.cs`). Whichever of B/D/F lands first adds `partial`; the merge step must not double-add it. This plan adds it in Task 1; if WS-B already declares `public sealed partial class Connection`, skip Task 1's implementation step (the test still passes).
- **macOS `RTLD_GLOBAL` value.** Confirmed `0x8` on Darwin vs `0x100` on glibc; if a future musl/BSD target appears, `RtldGlobal` may need another branch. Not blocking now.
