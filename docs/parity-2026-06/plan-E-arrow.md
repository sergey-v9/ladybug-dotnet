# WS-E: Arrow Interop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]` checkboxes.

**Goal:** Ship Apache.Arrow round-trip interop for LadybugDB — a raw C-Data-Interface seam in the
core `QueryResult` (zero managed-Arrow dependency) plus a new `LadybugDB.Arrow` package that reads
query results as `Apache.Arrow.RecordBatch`es and ingests `RecordBatch`es back as engine tables.

**Architecture:** Core gains a `QueryResult.Arrow.cs` partial exposing two `IntPtr`-backed
`IDisposable` handles (`ArrowSchemaHandle`/`ArrowArrayHandle`) over
`lbug_query_result_get_arrow_schema` / `lbug_query_result_get_next_arrow_chunk`, and an `internal`
ingest seam on `Connection` for the `lbug_connection_create_arrow_table` family. The new
`LadybugDB.Arrow` assembly layers `Apache.Arrow` on top: `ReadSchema`/`ReadBatches` use Apache.Arrow's
`CArrowSchemaImporter`/`CArrowArrayImporter` over the raw handles; `CreateArrowTable` exports a
`RecordBatch` via `CArrowSchemaExporter`/`CArrowArrayExporter` and transfers ownership to lbug.

**Tech Stack:** C# (dual TFM `net10.0;netstandard2.0`), P/Invoke, `Apache.Arrow` (ns2.0-compatible),
xUnit + `Xunit.SkippableFact` with the `TestEnvironment.NativeAvailable` native gate.

---

## Files

**Create (core project `src/LadybugDB/`):**
- `src/LadybugDB/QueryResult.Arrow.cs` — `partial class QueryResult`: `GetArrowSchema()` /
  `GetNextArrowChunk(long)` returning `ArrowSchemaHandle` / `ArrowArrayHandle`.
- `src/LadybugDB/Arrow/ArrowSchemaHandle.cs` — `public sealed class ArrowSchemaHandle : IDisposable` (IntPtr-backed).
- `src/LadybugDB/Arrow/ArrowArrayHandle.cs` — `public sealed class ArrowArrayHandle : IDisposable` (IntPtr-backed).
- `src/LadybugDB/Connection.Arrow.cs` — `partial class Connection`: `internal` ingest seam wrapping
  the `lbug_connection_create_arrow_table` family.

**Create (new package `src/LadybugDB.Arrow/`):**
- `src/LadybugDB.Arrow/LadybugDB.Arrow.csproj` — references `Apache.Arrow` + `LadybugDB`.
- `src/LadybugDB.Arrow/LadybugArrow.cs` — `public static class LadybugArrow` with `ReadSchema` /
  `ReadBatches` / `CreateArrowTable` extension methods.

**Create (new test project `test/LadybugDB.Tests.Arrow/`):**
- `test/LadybugDB.Tests.Arrow/LadybugDB.Tests.Arrow.csproj`
- `test/LadybugDB.Tests.Arrow/ArrowHandleTests.cs` — native-gated raw-seam round-trip.
- `test/LadybugDB.Tests.Arrow/LadybugArrowTests.cs` — native-gated `RecordBatch` round-trip.

**Modify:**
- `src/LadybugDB/QueryResult.cs` — change `public sealed class QueryResult` to
  `public sealed partial class QueryResult` (one-token change; B owns this file but the partial
  keyword is a pre-agreed seam — coordinate so B lands it; if B has not, this plan lands it).
- `src/LadybugDB/Connection.cs` — change `public sealed class Connection` to
  `public sealed partial class Connection` (same seam coordination as above).
- `src/LadybugDB/LadybugDB.csproj` — add `<InternalsVisibleTo Include="LadybugDB.Arrow" />`.
- `LadybugDB.slnx` — register `src/LadybugDB.Arrow/LadybugDB.Arrow.csproj` and
  `test/LadybugDB.Tests.Arrow/LadybugDB.Tests.Arrow.csproj` (WS-K may have created the skeleton; if
  the `LadybugDB.Arrow` project already exists from WS-K, replace its placeholder content rather than
  re-creating the `.csproj`).

**Depends on (Phase-1 WS-A interop, must exist before Phase 2):** `Native.QueryResultGetArrowSchema`,
`Native.QueryResultGetNextArrowChunk`, `Native.ConnectionCreateArrowTable`,
`Native.ConnectionCreateArrowRelTable`, `Native.ConnectionCreateArrowRelTableCsr`,
`Native.ConnectionDropArrowTable`, `Native.GetLastError`. **Verification Task 0 below probes these and
declares thin local interop shims only if WS-A has not yet landed them**, so this plan can build and
test independently.

---

## Conventions (read once)

- All paths are absolute. Build: `dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug`.
- Test one project: `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Arrow\LadybugDB.Tests.Arrow.csproj -c Debug`.
- Native-dependent tests use `[SkippableFact]` + `Skip.IfNot(TestEnvironment.NativeAvailable, "...")`.
  Pure-managed tests (handle lifetime over fakes) use plain `[Fact]`.
- Commit messages end with the trailer:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- Native ownership rule from `lbug.h`: for `lbug_query_result_get_arrow_schema` /
  `_get_next_arrow_chunk` **the caller owns the filled struct and must call its `release` callback**.
  For the `lbug_connection_create_arrow_table` family **ownership of the schema and arrays is
  TRANSFERRED to lbug on success OR failure — the caller must NOT release them afterward**.

---

## Task 0 — Confirm Apache.Arrow ns2.0 support and pin the version

- [ ] Run `dotnet package search Apache.Arrow --exact-match --format json` (or
      `dotnet add ... --version` dry run) and record the latest stable version that ships an
      `netstandard2.0` (or `netstandard2.1`/`net8.0` + ns2.0) asset. Apache.Arrow ships ns2.0 assets;
      pin a concrete version (use `18.1.0` as the floor — adjust to the resolved latest stable).
- [ ] Create `W:\code\ladybug\tools\csharp_api\src\LadybugDB.Arrow\LadybugDB.Arrow.csproj` with the
      pinned reference (real content):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net10.0;netstandard2.0</TargetFrameworks>
    <AssemblyName>LadybugDB.Arrow</AssemblyName>
    <RootNamespace>LadybugDB.Arrow</RootNamespace>
    <DebugType>portable</DebugType>
    <IsPackable>true</IsPackable>
  </PropertyGroup>

  <PropertyGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Apache.Arrow" Version="18.1.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\LadybugDB\LadybugDB.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="LadybugDB.Tests.Arrow" />
  </ItemGroup>

</Project>
```

- [ ] Add a temporary marker type so the assembly compiles:
      `W:\code\ladybug\tools\csharp_api\src\LadybugDB.Arrow\LadybugArrow.cs` with
      `namespace LadybugDB.Arrow; public static class LadybugArrow { }`.
- [ ] Register the project in `W:\code\ladybug\tools\csharp_api\LadybugDB.slnx` under `/src/`
      (add `<Project Path="src/LadybugDB.Arrow/LadybugDB.Arrow.csproj" />`).
- [ ] Run `dotnet restore W:\code\ladybug\tools\csharp_api\src\LadybugDB.Arrow\LadybugDB.Arrow.csproj`
      then `dotnet build W:\code\ladybug\tools\csharp_api\src\LadybugDB.Arrow\LadybugDB.Arrow.csproj -c Debug`.
      Expected PASS on **both** TFMs (restore resolves the ns2.0 asset; build prints
      `Build succeeded`). If restore fails to resolve an ns2.0 asset, that is the flagged decision in
      the spec §9 — STOP and surface it (do not silently drop ns2.0).
- [ ] Commit: `chore(arrow): scaffold LadybugDB.Arrow package + pin Apache.Arrow (ns2.0 confirmed)`.

---

## Task 1 — `ArrowSchemaHandle` (raw IntPtr-backed schema, pure-managed lifetime test first)

The handle owns a `Marshal.AllocHGlobal`'d block sized for a C `ArrowSchema` (the producer fills it).
On dispose it invokes the Arrow `release` callback (if the struct is still live) then frees the block.
The release callback is the first `void(*)(ArrowSchema*)` field after the fixed header; per the
C-Data layout `ArrowSchema` is `{ const char* format; const char* name; const char* metadata;
int64_t flags; int64_t n_children; ArrowSchema** children; ArrowSchema* dictionary;
void(*release)(ArrowSchema*); void* private_data; }` = 6 pointers + 2×int64 = a fixed 64-byte block on
64-bit. We allocate `Marshal.SizeOf` of a managed mirror to stay ABI-exact.

- [ ] Write the FAILING test. Create
      `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Arrow\LadybugDB.Tests.Arrow.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Xunit.SkippableFact" Version="1.5.61" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\LadybugDB.Arrow\LadybugDB.Arrow.csproj" />
    <ProjectReference Include="..\..\src\LadybugDB\LadybugDB.csproj" />
  </ItemGroup>

  <!-- Copy the native library next to the test output (mirrors LadybugDB.Tests). -->
  <Target Name="PlaceNativeLibrary" AfterTargets="Build">
    <ItemGroup>
      <_LadybugNative Include="$(NativeLibDir)runtimes\$(TargetRid)\native\*.*" />
    </ItemGroup>
    <Copy SourceFiles="@(_LadybugNative)" DestinationFolder="$(OutputPath)" SkipUnchangedFiles="true" Condition="'@(_LadybugNative)' != ''" />
  </Target>

</Project>
```

- [ ] Create the shared test-environment helper for this project
      `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Arrow\TestEnvironment.cs` (mirror of the
      one in `LadybugDB.Tests`, kept local so this project is standalone):

```csharp
using System;
using System.IO;
using LadybugDB;

namespace LadybugDB.Tests.Arrow;

internal static class TestEnvironment
{
    public static readonly bool NativeAvailable = Probe();

    public static string NewTempDbPath()
        => Path.Combine(Path.GetTempPath(), "ladybug-arrow-" + Guid.NewGuid().ToString("N"));

    public static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch { /* best-effort */ }
    }

    private static bool Probe()
    {
        try { _ = LadybugVersion.StorageVersion; return true; }
        catch (DllNotFoundException) { return false; }
        catch (TypeInitializationException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }
}
```

- [ ] Add the failing test
      `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Arrow\ArrowHandleTests.cs`:

```csharp
using System;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests.Arrow;

public sealed class ArrowHandleTests
{
    [Fact]
    public void SchemaHandle_AllocatesPtr_AndZeroesIt()
    {
        using var handle = ArrowSchemaHandle.Allocate();
        Assert.NotEqual(IntPtr.Zero, handle.Ptr);
        // A freshly-allocated, unfilled ArrowSchema has a null release pointer (offset 56 on x64);
        // Dispose must be safe (no release call) in that state.
    }

    [Fact]
    public void SchemaHandle_DoubleDispose_IsSafe()
    {
        var handle = ArrowSchemaHandle.Allocate();
        IntPtr ptr = handle.Ptr;
        handle.Dispose();
        handle.Dispose(); // idempotent
        Assert.Equal(IntPtr.Zero, handle.Ptr);
        GC.KeepAlive(ptr);
    }

    [Fact]
    public void SchemaHandle_PtrThrowsAfterDispose()
    {
        var handle = ArrowSchemaHandle.Allocate();
        handle.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _ = handle.Ptr);
    }
}
```

- [ ] Register the test project in `LadybugDB.slnx` under `/test/`.
- [ ] Run (expected FAIL — `ArrowSchemaHandle` does not exist yet):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Arrow\LadybugDB.Tests.Arrow.csproj -c Debug`
      → expected `error CS0103: The name 'ArrowSchemaHandle' does not exist`.
- [ ] Implement `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Arrow\ArrowSchemaHandle.cs`:

```csharp
using System;
using System.Runtime.InteropServices;

namespace LadybugDB;

/// <summary>
/// Owns an unmanaged Arrow C-Data-Interface <c>ArrowSchema</c> block. The native engine fills the
/// block via <see cref="QueryResult.GetArrowSchema"/>; this wrapper invokes the Arrow
/// <c>release</c> callback (when present) and frees the block on disposal. IntPtr-backed so the core
/// assembly takes no managed-Arrow dependency.
/// </summary>
public sealed class ArrowSchemaHandle : IDisposable
{
    // C-Data ArrowSchema: 3 pointers (format/name/metadata) + int64 flags + int64 n_children
    //   + 2 pointers (children/dictionary) + release fn ptr + private_data ptr.
    // The release callback is the 8th machine-word-sized slot after 3 ptrs and 2 int64:
    //   offset = 3*IntPtr + 2*8 + 2*IntPtr = 5*IntPtr + 16 (== 56 on x64).
    private static readonly int Size = 5 * IntPtr.Size + 16 + 2 * IntPtr.Size; // == 64 on x64
    private static readonly int ReleaseOffset = 5 * IntPtr.Size + 16;          // == 56 on x64

    private IntPtr _ptr;
    private int _disposed;

    private ArrowSchemaHandle(IntPtr ptr) => _ptr = ptr;

    /// <summary>Allocates a zero-initialized unmanaged ArrowSchema block for the producer to fill.</summary>
    public static ArrowSchemaHandle Allocate()
    {
        IntPtr ptr = Marshal.AllocHGlobal(Size);
        for (int i = 0; i < Size; i++)
        {
            Marshal.WriteByte(ptr, i, 0);
        }

        return new ArrowSchemaHandle(ptr);
    }

    /// <summary>The pointer to the unmanaged ArrowSchema block. Throws once disposed.</summary>
    public IntPtr Ptr
    {
        get
        {
            if (System.Threading.Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(ArrowSchemaHandle));
            }

            return _ptr;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        IntPtr ptr = _ptr;
        _ptr = IntPtr.Zero;
        if (ptr == IntPtr.Zero)
        {
            return;
        }

        IntPtr release = Marshal.ReadIntPtr(ptr, ReleaseOffset);
        if (release != IntPtr.Zero)
        {
#if NET7_0_OR_GREATER
            var fn = (delegate* unmanaged[Cdecl]<IntPtr, void>)release;
            fn(ptr);
#else
            var fn = Marshal.GetDelegateForFunctionPointer<ReleaseFn>(release);
            fn(ptr);
#endif
        }

        Marshal.FreeHGlobal(ptr);
    }

#if !NET7_0_OR_GREATER
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ReleaseFn(IntPtr schema);
#endif
}
```

> NOTE: the `delegate* unmanaged` form requires `unsafe`. `AllowUnsafeBlocks` is already `true` in
> `Directory.Build.props`. If the file needs it explicitly, wrap the net7 branch in `unsafe { }` —
> add the `unsafe` keyword to the method or block. Verify the build; if CS0214 appears, mark
> `Dispose` as `unsafe` under the `NET7_0_OR_GREATER` path only.

- [ ] Run (expected PASS):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Arrow\LadybugDB.Tests.Arrow.csproj -c Debug --filter "FullyQualifiedName~ArrowHandleTests"`
      → 3 tests pass.
- [ ] Commit: `feat(arrow): add IntPtr-backed ArrowSchemaHandle with release-callback disposal`.

---

## Task 2 — `ArrowArrayHandle` (raw IntPtr-backed array, mirror of Task 1)

C-Data `ArrowArray` layout: `{ int64 length; int64 null_count; int64 offset; int64 n_buffers;
int64 n_children; const void** buffers; ArrowArray** children; ArrowArray* dictionary;
void(*release)(ArrowArray*); void* private_data; }` = 5×int64 + 5 pointers; release is the 4th
pointer (after buffers/children/dictionary), at offset `5*8 + 3*IntPtr`.

- [ ] Add the failing test to
      `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Arrow\ArrowHandleTests.cs`:

```csharp
    [Fact]
    public void ArrayHandle_AllocatesPtr_AndDoubleDisposeIsSafe()
    {
        var handle = ArrowArrayHandle.Allocate();
        Assert.NotEqual(IntPtr.Zero, handle.Ptr);
        handle.Dispose();
        handle.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _ = handle.Ptr);
    }
```

- [ ] Run (expected FAIL — `ArrowArrayHandle` undefined):
      `dotnet test ...\LadybugDB.Tests.Arrow.csproj -c Debug --filter "FullyQualifiedName~ArrayHandle_AllocatesPtr"`
      → `error CS0103: The name 'ArrowArrayHandle' does not exist`.
- [ ] Implement `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Arrow\ArrowArrayHandle.cs`:

```csharp
using System;
using System.Runtime.InteropServices;

namespace LadybugDB;

/// <summary>
/// Owns an unmanaged Arrow C-Data-Interface <c>ArrowArray</c> block (one chunk of a query result).
/// Mirrors <see cref="ArrowSchemaHandle"/>: the engine fills it via
/// <see cref="QueryResult.GetNextArrowChunk"/>, and this wrapper invokes the Arrow <c>release</c>
/// callback (when present) and frees the block on disposal.
/// </summary>
public sealed class ArrowArrayHandle : IDisposable
{
    // ArrowArray: 5 * int64 + 5 pointers. release is the 4th pointer (after buffers, children,
    //   dictionary): offset = 5*8 + 3*IntPtr (== 64 on x64). Total size = 5*8 + 5*IntPtr (== 80 on x64).
    private static readonly int Size = 5 * 8 + 5 * IntPtr.Size;
    private static readonly int ReleaseOffset = 5 * 8 + 3 * IntPtr.Size;

    private IntPtr _ptr;
    private int _disposed;

    private ArrowArrayHandle(IntPtr ptr) => _ptr = ptr;

    /// <summary>Allocates a zero-initialized unmanaged ArrowArray block for the producer to fill.</summary>
    public static ArrowArrayHandle Allocate()
    {
        IntPtr ptr = Marshal.AllocHGlobal(Size);
        for (int i = 0; i < Size; i++)
        {
            Marshal.WriteByte(ptr, i, 0);
        }

        return new ArrowArrayHandle(ptr);
    }

    /// <summary>The pointer to the unmanaged ArrowArray block. Throws once disposed.</summary>
    public IntPtr Ptr
    {
        get
        {
            if (System.Threading.Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(ArrowArrayHandle));
            }

            return _ptr;
        }
    }

    /// <summary>True when the engine reported an empty chunk (length 0 / null release): end of stream.</summary>
    public bool IsReleased => Marshal.ReadIntPtr(Ptr, ReleaseOffset) == IntPtr.Zero;

    /// <summary>The chunk row count (ArrowArray.length, field offset 0).</summary>
    public long Length => Marshal.ReadInt64(Ptr, 0);

    /// <inheritdoc />
    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        IntPtr ptr = _ptr;
        _ptr = IntPtr.Zero;
        if (ptr == IntPtr.Zero)
        {
            return;
        }

        IntPtr release = Marshal.ReadIntPtr(ptr, ReleaseOffset);
        if (release != IntPtr.Zero)
        {
#if NET7_0_OR_GREATER
            unsafe
            {
                var fn = (delegate* unmanaged[Cdecl]<IntPtr, void>)release;
                fn(ptr);
            }
#else
            var fn = Marshal.GetDelegateForFunctionPointer<ReleaseFn>(release);
            fn(ptr);
#endif
        }

        Marshal.FreeHGlobal(ptr);
    }

#if !NET7_0_OR_GREATER
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ReleaseFn(IntPtr array);
#endif
}
```

- [ ] Run (expected PASS):
      `dotnet test ...\LadybugDB.Tests.Arrow.csproj -c Debug --filter "FullyQualifiedName~ArrowHandleTests"`
      → 4 tests pass.
- [ ] Commit: `feat(arrow): add IntPtr-backed ArrowArrayHandle with length + end-of-stream probe`.

---

## Task 3 — Make `QueryResult` partial + add the raw export seam (native-gated)

- [ ] Make `QueryResult` partial. In
      `W:\code\ladybug\tools\csharp_api\src\LadybugDB\QueryResult.cs` change line 11 from
      `public sealed class QueryResult : IDisposable` to
      `public sealed partial class QueryResult : IDisposable`. (Coordinate with WS-B which owns this
      file; if B already made it partial, skip.)
- [ ] Confirm the interop shim exists. Search
      `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Interop\Native.LibraryImport.cs` for
      `QueryResultGetArrowSchema`. If WS-A landed it, do nothing. If absent, add a **local** Arrow
      interop partial owned by this WS at
      `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Interop\Native.Arrow.cs` (so the plan is
      self-contained; if WS-A later lands the same entry points, delete this file in the merge):

```csharp
using System;
using System.Runtime.InteropServices;

namespace LadybugDB.Interop;

// Arrow C-Data-Interface interop. Folded into WS-A's Native.* surface on merge if it lands there.
internal static partial class Native
{
#if NET7_0_OR_GREATER
    [LibraryImport(LibraryName, EntryPoint = "lbug_query_result_get_arrow_schema")]
    internal static partial LbugState QueryResultGetArrowSchema(ref LbugQueryResult queryResult, IntPtr outSchema);

    [LibraryImport(LibraryName, EntryPoint = "lbug_query_result_get_next_arrow_chunk")]
    internal static partial LbugState QueryResultGetNextArrowChunk(ref LbugQueryResult queryResult, long chunkSize, IntPtr outArray);

    [LibraryImport(LibraryName, EntryPoint = "lbug_connection_create_arrow_table", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial LbugState ConnectionCreateArrowTable(ref LbugConnection connection, string tableName, IntPtr schema, IntPtr arrays, ulong numArrays, out LbugQueryResult outQueryResult);

    [LibraryImport(LibraryName, EntryPoint = "lbug_connection_drop_arrow_table", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial LbugState ConnectionDropArrowTable(ref LbugConnection connection, string tableName, out LbugQueryResult outQueryResult);
#endif
}
```

> NOTE: if WS-A's signatures differ (e.g. they pass `ref ArrowSchema` structs instead of `IntPtr`),
> prefer WS-A's. This plan deliberately uses `IntPtr` so the seam takes no struct dependency and the
> handles in Tasks 1/2 own allocation. The ns2.0 `DllImport` twin of this shim lives in WS-A's
> `Native.DllImport.cs`; if you must add it locally, mirror with `[DllImport(... CallingConvention =
> Conv)]` and a `byte[]`-marshalled string twin like the other entries in that file.

- [ ] Write the FAILING native-gated test
      `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Arrow\ArrowHandleTests.cs` (append):

```csharp
    [SkippableFact]
    public void GetArrowSchema_ReturnsLiveHandle()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);
            conn.Query("CREATE NODE TABLE T(id INT64, name STRING, PRIMARY KEY(id))").Dispose();
            conn.Query("CREATE (:T {id: 1, name: 'a'})").Dispose();

            using QueryResult r = conn.Query("MATCH (t:T) RETURN t.id, t.name");
            using ArrowSchemaHandle schema = r.GetArrowSchema();
            Assert.NotEqual(IntPtr.Zero, schema.Ptr);

            using ArrowArrayHandle chunk = r.GetNextArrowChunk(1024);
            Assert.NotEqual(IntPtr.Zero, chunk.Ptr);
            Assert.Equal(1L, chunk.Length);
        }
        finally { TestEnvironment.TryDelete(dbPath); }
    }
```

      Add `using Xunit;` is already present; add no new usings.
- [ ] Run (expected FAIL — `GetArrowSchema`/`GetNextArrowChunk` undefined on `QueryResult`):
      `dotnet test ...\LadybugDB.Tests.Arrow.csproj -c Debug --filter "FullyQualifiedName~GetArrowSchema_ReturnsLiveHandle"`
      → `error CS1061: 'QueryResult' does not contain a definition for 'GetArrowSchema'`.
- [ ] Implement `W:\code\ladybug\tools\csharp_api\src\LadybugDB\QueryResult.Arrow.cs`:

```csharp
using System;
using LadybugDB.Interop;

namespace LadybugDB;

/// <summary>
/// Raw Arrow C-Data-Interface export seam. Exposes the engine's Arrow schema and chunked arrays as
/// IntPtr-backed handles, with no managed Apache.Arrow dependency. The friendly
/// <c>Apache.Arrow</c>-typed layer lives in the separate <c>LadybugDB.Arrow</c> package.
/// </summary>
public sealed partial class QueryResult
{
    /// <summary>
    /// Exports the result's column schema as a raw Arrow C-Data <c>ArrowSchema</c>. The caller owns
    /// the returned handle and must dispose it (which invokes the Arrow release callback).
    /// </summary>
    public ArrowSchemaHandle GetArrowSchema()
    {
        ThrowIfDisposed();
        ArrowSchemaHandle handle = ArrowSchemaHandle.Allocate();
        LbugState state = Native.QueryResultGetArrowSchema(ref _handle, handle.Ptr);
        if (state != LbugState.Success)
        {
            handle.Dispose();
            throw new LadybugException("Failed to export the Arrow schema for the query result.");
        }

        return handle;
    }

    /// <summary>
    /// Exports the next chunk of up to <paramref name="chunkSize"/> rows as a raw Arrow C-Data
    /// <c>ArrowArray</c>. An empty/released chunk (see <see cref="ArrowArrayHandle.IsReleased"/>)
    /// signals end of stream. The caller owns and must dispose the returned handle.
    /// </summary>
    public ArrowArrayHandle GetNextArrowChunk(long chunkSize)
    {
        ThrowIfDisposed();
        ArrowArrayHandle handle = ArrowArrayHandle.Allocate();
        LbugState state = Native.QueryResultGetNextArrowChunk(ref _handle, chunkSize, handle.Ptr);
        if (state != LbugState.Success)
        {
            handle.Dispose();
            throw new LadybugException("Failed to export the next Arrow chunk of the query result.");
        }

        return handle;
    }
}
```

- [ ] Run (expected PASS, native present):
      `dotnet test ...\LadybugDB.Tests.Arrow.csproj -c Debug --filter "FullyQualifiedName~GetArrowSchema_ReturnsLiveHandle"`
      → 1 pass (or skip if native absent — then set `LADYBUG_REQUIRE_NATIVE=1` on a native host to
      verify; do NOT claim pass on a skip).
- [ ] Run the whole core build on both TFMs to confirm the partial compiles for ns2.0:
      `dotnet build W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj -c Debug`
      → `Build succeeded`.
- [ ] Commit: `feat(arrow): expose raw Arrow C-Data export seam on QueryResult`.

---

## Task 4 — `LadybugArrow.ReadSchema` / `ReadBatches` (Apache.Arrow import, native-gated)

Use Apache.Arrow's C-Data importers. `Apache.Arrow.C.CArrowSchemaImporter.ImportSchema(CArrowSchema*)`
and `CArrowArrayImporter.ImportRecordBatch(CArrowArray*, Schema)` consume (and release) the imported
structs, so we hand them the raw handle pointers and **suppress our own release** by transferring
ownership to the importer. To keep ownership clean, we import directly from the handle `Ptr` and then
mark the handle as already-released (the importer's release happened). The simplest correct pattern:
let the importer own the struct and have our handle NOT double-release — we achieve this by passing
the pointer and disposing the handle's allocation (the FreeHGlobal) only, after the importer has
released the contents.

- [ ] Write the FAILING test
      `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Arrow\LadybugArrowTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Apache.Arrow;
using LadybugDB;
using LadybugDB.Arrow;
using Xunit;

namespace LadybugDB.Tests.Arrow;

public sealed class LadybugArrowTests
{
    [SkippableFact]
    public void ReadSchema_And_ReadBatches_RoundTripsAQuery()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);
            conn.Query("CREATE NODE TABLE Person(id INT64, name STRING, PRIMARY KEY(id))").Dispose();
            conn.Query("CREATE (:Person {id: 1, name: 'Alice'})").Dispose();
            conn.Query("CREATE (:Person {id: 2, name: 'Bob'})").Dispose();

            using QueryResult r = conn.Query("MATCH (p:Person) RETURN p.id AS id, p.name AS name ORDER BY id");

            Schema schema = r.ReadSchema();
            Assert.Equal(2, schema.FieldsList.Count);
            Assert.Equal("id", schema.FieldsList[0].Name);
            Assert.Equal("name", schema.FieldsList[1].Name);

            List<RecordBatch> batches = r.ReadBatches().ToList();
            long totalRows = batches.Sum(b => (long)b.Length);
            Assert.Equal(2L, totalRows);

            // Verify the first batch's id column materializes 1,2.
            var idCol = (Int64Array)batches[0].Column("id");
            Assert.Equal(1L, idCol.GetValue(0));
            Assert.Equal(2L, idCol.GetValue(1));

            foreach (RecordBatch b in batches) b.Dispose();
        }
        finally { TestEnvironment.TryDelete(dbPath); }
    }
}
```

- [ ] Run (expected FAIL — `ReadSchema`/`ReadBatches` undefined):
      `dotnet test ...\LadybugDB.Tests.Arrow.csproj -c Debug --filter "FullyQualifiedName~ReadSchema_And_ReadBatches"`
      → `error CS1061: 'QueryResult' does not contain a definition for 'ReadSchema'`.
- [ ] Implement the read side in
      `W:\code\ladybug\tools\csharp_api\src\LadybugDB.Arrow\LadybugArrow.cs` (replace the marker
      type body):

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Apache.Arrow;
using Apache.Arrow.C;

namespace LadybugDB.Arrow;

/// <summary>
/// Apache.Arrow-typed interop over LadybugDB's raw Arrow C-Data export/ingest seams. Reads query
/// results as <see cref="Schema"/>/<see cref="RecordBatch"/> and ingests a <see cref="RecordBatch"/>
/// back into the engine as a node table.
/// </summary>
public static class LadybugArrow
{
    /// <summary>Reads the result's Arrow <see cref="Schema"/> (column names + Arrow types).</summary>
    public static Schema ReadSchema(this QueryResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        using ArrowSchemaHandle handle = result.GetArrowSchema();
        unsafe
        {
            return CArrowSchemaImporter.ImportSchema((CArrowSchema*)handle.Ptr);
        }
    }

    /// <summary>
    /// Streams the result as a sequence of <see cref="RecordBatch"/>es of up to
    /// <paramref name="chunkSize"/> rows each. The schema is read once; each batch is imported from a
    /// raw Arrow chunk. Enumeration stops when the engine returns an empty (released) chunk.
    /// </summary>
    public static IEnumerable<RecordBatch> ReadBatches(this QueryResult result, long chunkSize = 1_000_000)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        Schema schema = result.ReadSchema();
        while (true)
        {
            ArrowArrayHandle chunk = result.GetNextArrowChunk(chunkSize);
            if (chunk.IsReleased || chunk.Length == 0)
            {
                chunk.Dispose();
                yield break;
            }

            RecordBatch batch;
            unsafe
            {
                batch = CArrowArrayImporter.ImportRecordBatch((CArrowArray*)chunk.Ptr, schema);
            }

            // The importer took ownership of the ArrowArray contents (and released them); free only
            // our unmanaged allocation without re-invoking release.
            chunk.DetachAndFree();
            yield return batch;
        }
    }
}
```

- [ ] Add `DetachAndFree()` to `ArrowArrayHandle`
      (`W:\code\ladybug\tools\csharp_api\src\LadybugDB\Arrow\ArrowArrayHandle.cs`) so the importer's
      ownership transfer does not double-release:

```csharp
    /// <summary>
    /// Frees only the unmanaged block WITHOUT invoking the Arrow release callback. Call this after a
    /// consumer (e.g. Apache.Arrow's importer) has taken ownership of the struct contents and already
    /// released them, so disposal must not release a second time.
    /// </summary>
    public void DetachAndFree()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        IntPtr ptr = _ptr;
        _ptr = IntPtr.Zero;
        if (ptr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
```

- [ ] Add the mirror `DetachAndFree()` to `ArrowSchemaHandle`
      (`W:\code\ladybug\tools\csharp_api\src\LadybugDB\Arrow\ArrowSchemaHandle.cs`), identical body,
      because `ReadSchema` lets the importer own the schema struct:

```csharp
    /// <summary>Frees the unmanaged block without invoking the Arrow release callback (see ArrowArrayHandle).</summary>
    public void DetachAndFree()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        IntPtr ptr = _ptr;
        _ptr = IntPtr.Zero;
        if (ptr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
```

      Then fix `ReadSchema` to use `DetachAndFree` instead of `using` (the importer released the
      schema contents): change the body to

```csharp
        ArrowSchemaHandle handle = result.GetArrowSchema();
        try
        {
            unsafe
            {
                return CArrowSchemaImporter.ImportSchema((CArrowSchema*)handle.Ptr);
            }
        }
        finally
        {
            handle.DetachAndFree();
        }
```

> RATIONALE: Apache.Arrow's `CArrowSchemaImporter.ImportSchema` and
> `CArrowArrayImporter.ImportRecordBatch` MOVE the struct (they call the producer's release when the
> imported wrapper is disposed). Our handle must therefore NOT also call release — it only owns the
> `Marshal.AllocHGlobal` block, which `DetachAndFree` reclaims. This is the single most error-prone
> point; the round-trip test in this task is the guard.

- [ ] Run (expected PASS, native present):
      `dotnet test ...\LadybugDB.Tests.Arrow.csproj -c Debug --filter "FullyQualifiedName~ReadSchema_And_ReadBatches"`
      → 1 pass. Run twice to confirm no crash from double-free/leak under repeated import.
- [ ] Commit: `feat(arrow): ReadSchema + ReadBatches via Apache.Arrow C-Data importers`.

---

## Task 5 — `LadybugArrow.CreateArrowTable` (Apache.Arrow export → ingest, ownership transfer)

The ingest family TRANSFERS ownership of the schema + arrays to lbug (on success and failure), so we
export the `RecordBatch` into freshly-allocated C-Data structs and **must not release them ourselves**.

- [ ] Add the `internal` ingest seam on `Connection`. Make `Connection` partial: in
      `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Connection.cs` change line 12 from
      `public sealed class Connection : IDisposable` to
      `public sealed partial class Connection : IDisposable` (coordinate with WS-B if it owns the
      file). Then create
      `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Connection.Arrow.cs`:

```csharp
using System;
using LadybugDB.Interop;

namespace LadybugDB;

/// <summary>
/// Internal Arrow ingest seam consumed by the <c>LadybugDB.Arrow</c> package via
/// <c>InternalsVisibleTo</c>. Ownership of the schema and arrays is transferred to the engine on
/// success OR failure, per the C API contract — callers must NOT release them afterward.
/// </summary>
public sealed partial class Connection
{
    /// <summary>
    /// Ingests Arrow C-Data structs as a node table. <paramref name="schemaPtr"/> and
    /// <paramref name="arraysPtr"/> are consumed (ownership transferred to the engine). Returns the
    /// result handle of the underlying CREATE; throws on failure.
    /// </summary>
    internal QueryResult CreateArrowTableInternal(string tableName, IntPtr schemaPtr, IntPtr arraysPtr, ulong numArrays)
    {
        if (tableName is null)
        {
            throw new ArgumentNullException(nameof(tableName));
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            LbugState state = Native.ConnectionCreateArrowTable(ref _handle, tableName, schemaPtr, arraysPtr, numArrays, out LbugQueryResult resultHandle);
            return Finish(state, resultHandle);
        }
    }
}
```

> NOTE: `_gate`, `_handle`, `ThrowIfDisposed()`, and `Finish(...)` are all private members of the
> `Connection` partial declared in `Connection.cs`; partial files in the same assembly share them,
> so no new accessors are needed.

- [ ] Add `<InternalsVisibleTo Include="LadybugDB.Arrow" />` to
      `W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj` (next to the existing
      `LadybugDB.Tests` entry).
- [ ] Write the FAILING test (append to `LadybugArrowTests.cs`):

```csharp
    [SkippableFact]
    public void CreateArrowTable_IngestsARecordBatch()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            var idField = new Field("id", Int64Type.Default, nullable: false);
            var nameField = new Field("name", StringType.Default, nullable: true);
            var schema = new Schema(new[] { idField, nameField }, metadata: null);

            var idArray = new Int64Array.Builder().Append(10).Append(20).Build();
            var nameBuilder = new StringArray.Builder();
            nameBuilder.Append("x"); nameBuilder.Append("y");
            var nameArray = nameBuilder.Build();
            using var batch = new RecordBatch(schema, new IArrowArray[] { idArray, nameArray }, length: 2);

            conn.CreateArrowTable("Imported", batch);

            using QueryResult r = conn.Query("MATCH (n:Imported) RETURN n.id AS id ORDER BY id");
            var ids = r.ReadBatches().SelectMany(b => Enumerable.Range(0, b.Length).Select(i => ((Int64Array)b.Column("id")).GetValue(i))).ToList();
            Assert.Equal(new long?[] { 10L, 20L }, ids);
        }
        finally { TestEnvironment.TryDelete(dbPath); }
    }
```

- [ ] Run (expected FAIL — `CreateArrowTable` undefined on `Connection`):
      `dotnet test ...\LadybugDB.Tests.Arrow.csproj -c Debug --filter "FullyQualifiedName~CreateArrowTable_IngestsARecordBatch"`
      → `error CS1061: 'Connection' does not contain a definition for 'CreateArrowTable'`.
- [ ] Implement the export+ingest extension in
      `W:\code\ladybug\tools\csharp_api\src\LadybugDB.Arrow\LadybugArrow.cs` (append to the class):

```csharp
    /// <summary>
    /// Ingests an Apache.Arrow <see cref="RecordBatch"/> as an in-memory node table named
    /// <paramref name="tableName"/>. Ownership of the exported Arrow structs is transferred to the
    /// engine, matching the native <c>lbug_connection_create_arrow_table</c> contract — the caller
    /// keeps full ownership of the original managed <paramref name="batch"/>.
    /// </summary>
    public static void CreateArrowTable(this Connection connection, string tableName, RecordBatch batch)
    {
        if (connection is null) throw new ArgumentNullException(nameof(connection));
        if (tableName is null) throw new ArgumentNullException(nameof(tableName));
        if (batch is null) throw new ArgumentNullException(nameof(batch));

        // Allocate C-Data structs the engine will OWN after the call (success or failure). Apache.Arrow
        // exports into them; we transfer their pointers and never release them ourselves.
        IntPtr schemaPtr = Marshal.AllocHGlobal(SizeOfCArrowSchema());
        IntPtr arrayPtr = Marshal.AllocHGlobal(SizeOfCArrowArray());
        unsafe
        {
            var cSchema = (CArrowSchema*)schemaPtr;
            var cArray = (CArrowArray*)arrayPtr;
            *cSchema = default;
            *cArray = default;
            CArrowSchemaExporter.ExportSchema(batch.Schema, cSchema);
            CArrowArrayExporter.ExportRecordBatch(batch, cArray);
        }

        // numArrays = 1 (a single contiguous Arrow array/struct for the whole batch).
        using QueryResult result = connection.CreateArrowTableInternal(tableName, schemaPtr, arrayPtr, 1UL);
        // Do NOT free schemaPtr/arrayPtr — ownership transferred to the engine per the C contract.
        GC.KeepAlive(result);
    }

    private static int SizeOfCArrowSchema()
    {
        unsafe { return sizeof(CArrowSchema); }
    }

    private static int SizeOfCArrowArray()
    {
        unsafe { return sizeof(CArrowArray); }
    }
```

> RATIONALE for ownership: `lbug.h` states for `lbug_connection_create_arrow_table` that "Ownership of
> schema and arrays is transferred to lbug on success or failure. The caller must not release them
> after this call." Therefore we (a) export into engine-owned blocks, (b) never call the release
> callback, and (c) never `FreeHGlobal` them. `CArrowSchemaExporter`/`CArrowArrayExporter` populate
> the structs with self-contained release callbacks the engine will invoke.

- [ ] Run (expected PASS, native present):
      `dotnet test ...\LadybugDB.Tests.Arrow.csproj -c Debug --filter "FullyQualifiedName~CreateArrowTable_IngestsARecordBatch"`
      → 1 pass.
- [ ] Commit: `feat(arrow): CreateArrowTable ingest with ownership transfer to the engine`.

---

## Task 6 — Full round-trip integration test (query → RecordBatch → ingest → query)

- [ ] Write the FAILING/PASS round-trip test (append to `LadybugArrowTests.cs`). This is the
      workstream's headline acceptance test from the brief ("round-tripping a small query to a
      RecordBatch and back"):

```csharp
    [SkippableFact]
    public void FullRoundTrip_QueryToRecordBatchAndBackToTable()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);
            conn.Query("CREATE NODE TABLE Src(id INT64, name STRING, PRIMARY KEY(id))").Dispose();
            conn.Query("CREATE (:Src {id: 1, name: 'Alice'})").Dispose();
            conn.Query("CREATE (:Src {id: 2, name: 'Bob'})").Dispose();

            // 1) Query -> Arrow RecordBatches.
            List<RecordBatch> batches;
            using (QueryResult r = conn.Query("MATCH (s:Src) RETURN s.id AS id, s.name AS name ORDER BY id"))
            {
                batches = r.ReadBatches().ToList();
            }
            Assert.Equal(2L, batches.Sum(b => (long)b.Length));

            // 2) Ingest the first batch back as a new table.
            conn.CreateArrowTable("Copy", batches[0]);
            foreach (var b in batches) b.Dispose();

            // 3) Query the new table and confirm the data survived the round trip.
            using QueryResult check = conn.Query("MATCH (c:Copy) RETURN c.id AS id, c.name AS name ORDER BY id");
            var rows = check.Rows().ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal(1L, rows[0][0]);
            Assert.Equal("Alice", rows[0][1]);
            Assert.Equal(2L, rows[1][0]);
            Assert.Equal("Bob", rows[1][1]);
        }
        finally { TestEnvironment.TryDelete(dbPath); }
    }
```

- [ ] Run (expected PASS):
      `dotnet test ...\LadybugDB.Tests.Arrow.csproj -c Debug --filter "FullyQualifiedName~FullRoundTrip"`
      → 1 pass.
- [ ] Commit: `test(arrow): end-to-end query→RecordBatch→ingest→query round trip`.

---

## Task 7 — ns2.0 build verification + slnx/packaging wiring + final gate

- [ ] Confirm both TFMs build for the whole solution:
      `dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug`
      → `Build succeeded` with `LadybugDB.Arrow` listed for **both** `net10.0` and `netstandard2.0`.
      If `Apache.Arrow`'s ns2.0 asset surfaces an `unsafe`/`CArrow*` API gap on ns2.0, isolate the
      affected member behind `#if NET7_0_OR_GREATER` ONLY for that member (spec §9 fallback) — do not
      drop ns2.0 for the package. Re-run the build.
- [ ] Confirm the package metadata exists for `LadybugDB.Arrow`. Verify (or add) an
      `<Import Project="$(CSharpDir)nuget\nuget-package.props" />` style block, or set the minimal
      pack metadata directly in `LadybugDB.Arrow.csproj` (`<PackageId>LadybugDB.Arrow</PackageId>`,
      `<Description>` and `<Version>` driven by `version.txt`). WS-K owns the canonical packaging
      skeleton; if it has wired `LadybugDB.Arrow` into `cake/`, defer to that and only confirm
      `dotnet pack W:\code\ladybug\tools\csharp_api\src\LadybugDB.Arrow\LadybugDB.Arrow.csproj -c Debug`
      produces a `.nupkg` with a transitive `Apache.Arrow` dependency.
- [ ] Run the full Arrow test project once more end-to-end:
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Arrow\LadybugDB.Tests.Arrow.csproj -c Debug`
      → pure-managed handle tests PASS; native-gated tests PASS (native host) or SKIP (no native).
- [ ] On a native-capable host, prove the gate: set `LADYBUG_REQUIRE_NATIVE=1` and re-run; confirm the
      Arrow round-trip tests execute (do not skip) and pass:
      PowerShell: `$env:LADYBUG_REQUIRE_NATIVE='1'; dotnet test ...\LadybugDB.Tests.Arrow.csproj -c Debug`.
- [ ] Commit: `chore(arrow): verify dual-TFM build, slnx wiring, and pack metadata`.

---

## Self-review / done criteria (ties to WS-E "Done = export+ingest+CSR; tests")

- [ ] **Raw seam shipped:** `QueryResult.GetArrowSchema()` → `ArrowSchemaHandle` and
      `QueryResult.GetNextArrowChunk(long)` → `ArrowArrayHandle`, both `IntPtr`-backed `IDisposable`,
      with no managed-Arrow dependency in core. Matches high-level-plan §4.4 exactly.
- [ ] **Friendly layer shipped:** `LadybugDB.Arrow.LadybugArrow` exposes
      `Schema ReadSchema(this QueryResult)`,
      `IEnumerable<RecordBatch> ReadBatches(this QueryResult, long chunkSize = 1_000_000)`, and
      `void CreateArrowTable(this Connection, string, RecordBatch)` — signatures match §4.4 exactly.
- [ ] **Ownership rules honored and documented:** export handles invoke the Arrow `release` callback
      on `Dispose`; importers use `DetachAndFree` (no double-release); ingest never releases/frees the
      exported structs (engine takes ownership on success OR failure) — verified by repeated-run tests
      with no crash/leak.
- [ ] **Apache.Arrow ns2.0 confirmed:** package restores and the `LadybugDB.Arrow` assembly builds on
      both `net10.0` and `netstandard2.0`; any ns2.0 gap was `#if`-gated per-member, not dropped.
- [ ] **Tests:** `test/LadybugDB.Tests.Arrow` has pure-managed handle-lifetime `[Fact]`s (always run)
      plus `[SkippableFact]` native-gated round-trip tests (raw seam, ReadSchema/ReadBatches,
      CreateArrowTable, and the full query→RecordBatch→ingest→query round trip). All pass with native
      present; skip cleanly without it.
- [ ] **Build green:** `dotnet build LadybugDB.slnx -c Debug` succeeds on both TFMs; `LadybugDB.Arrow`
      registered in `LadybugDB.slnx`; `InternalsVisibleTo LadybugDB.Arrow` added to core.
- [ ] **No regression to WS-B's files:** the only edits to `QueryResult.cs`/`Connection.cs` are the
      single `partial` keyword (a pre-agreed seam); all new code lives in this WS's own partial/new
      files.
- [ ] **CSR scope note recorded:** see open questions — `ReadBatches`/`CreateArrowTable`/CSR coverage.
