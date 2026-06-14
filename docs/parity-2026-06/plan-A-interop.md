# WS-A: Interop Expansion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]` checkboxes.

**Goal:** Declare P/Invoke for every native function in high-level-plan §3 on **both** TFM partial files (net7+ `[LibraryImport]` source-gen and ns2.0 `[DllImport]` + manual UTF-8 forwarders), add the `LbugQuerySummary` and Arrow C-Data structs to `NativeTypes.cs`, and guard the ABI plus the cross-TFM declaration parity with pure-managed tests.

**Architecture:** The interop surface is split into three files in `src/LadybugDB/Interop/`: `NativeTypes.cs` (blittable `[StructLayout(LayoutKind.Sequential)]` mirrors of C structs/enums, TFM-agnostic), `Native.LibraryImport.cs` (`#if NET7_0_OR_GREATER`, source-generated `[LibraryImport]` with `StringMarshalling.Utf8`), and `Native.DllImport.cs` (`#if NETSTANDARD2_0`, classic `[DllImport(..., CallingConvention.Cdecl)]` with private `*Raw` externs that take `byte[]` and internal forwarders that call `ToUtf8`). The two declaration files are hand-kept identical; this WS adds a source-text parity test that fails the build if they drift. Tests are pure managed (`Marshal.SizeOf`/`OffsetOf`, file parsing) so they need no native library and are **not** native-gated.

**Tech Stack:** C# (net10.0 + netstandard2.0 dual-target core; net10.0 single-target test project), `System.Runtime.InteropServices` source-generated marshalling, xUnit + `Xunit.SkippableFact`, `Marshal` reflection for ABI guards.

---

## Files

**Modify**
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Interop\NativeTypes.cs` — add `LbugQuerySummary`, `ArrowSchema`, `ArrowArray` structs.
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Interop\Native.LibraryImport.cs` — add all §3 `[LibraryImport]` declarations.
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Interop\Native.DllImport.cs` — add all §3 `[DllImport]` declarations + UTF-8 forwarders.
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Interop\Native.cs` — add `GetLastError()` convenience wrapper only (no new constants/resolver — the loader work is WS-F).

**Create**
- *(none — all interop code lands in the four existing `Interop/*.cs` files above.)*

**Test (Modify)**
- `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\StructLayoutTests.cs` — add `LbugQuerySummary`, `ArrowSchema`, `ArrowArray` ABI guards **and** the new `InteropDeclarationParity` source-text cross-check test class.

**Reference (read-only, do not edit)**
- `W:\code\ladybug\src\include\c_api\lbug.h` — authoritative native signatures.
- `W:\code\ladybug\tools\csharp_api\docs\parity-2026-06\03-high-level-plan.md` §3 — the pinned interop list.

---

## Conventions to follow (from `AGENTS.md` "Interop contract")

- **Two declaration files, kept identical.** Every function added to `Native.LibraryImport.cs` gets a matching entry in `Native.DllImport.cs` with the *same* internal C# method name and signature.
- **net7+ (`Native.LibraryImport.cs`):** `[LibraryImport(LibraryName, EntryPoint = "...")]` `internal static partial`. String params use `StringMarshalling = StringMarshalling.Utf8` on the attribute. `bool` returns use `[return: MarshalAs(UnmanagedType.U1)]`; `bool` params use `[MarshalAs(UnmanagedType.U1)] bool`.
- **ns2.0 (`Native.DllImport.cs`):** `[DllImport(LibraryName, EntryPoint = "...", CallingConvention = Conv)]` where `Conv == CallingConvention.Cdecl`. For functions with `const char*` params, declare a `private static extern ... Raw(...)` taking `byte[]` for each string, then an `internal static ... (...)` forwarder that calls `ToUtf8(s)`. Functions with no string params are declared `internal static extern` directly.
- **Return styles (verified against `lbug.h`):** some return `lbug_state` → `LbugState`; some return `double`/`bool`/`uint64_t` directly; value creators return `lbug_value*` → `IntPtr`; `char**` out-params and `char*` returns are `IntPtr` (caller frees via `lbug_destroy_string` / `Native.TakeString`).
- **`lbug_logical_type` is `{ void* }`** → already mirrored as `LbugLogicalType`. Functions taking `lbug_logical_type*` use `ref LbugLogicalType`; out-params use `out LbugLogicalType`.
- **`lbug_query_summary` is `{ void* }`** → new `LbugQuerySummary` mirror, one pointer wide.
- **`struct ArrowSchema*` / `struct ArrowArray*`** out-params are passed as `out ArrowSchema` / `out ArrowArray` (blittable mirrors added to `NativeTypes.cs`).

---

## Exact native signatures (verified against `W:\code\ladybug\src\include\c_api\lbug.h`)

For implementer reference; each appears inline in the relevant task.

```
// Connection (lbug.h:381,391,465,472)
lbug_state lbug_connection_set_max_num_thread_for_exec(lbug_connection*, uint64_t num_threads);
lbug_state lbug_connection_get_max_num_thread_for_exec(lbug_connection*, uint64_t* out_result);
void       lbug_connection_interrupt(lbug_connection*);
lbug_state lbug_connection_set_query_timeout(lbug_connection*, uint64_t timeout_in_ms);

// QueryResult (lbug.h:720,733,756,764,777,788,802)
lbug_state lbug_query_result_get_column_data_type(lbug_query_result*, uint64_t index, lbug_logical_type* out);
lbug_state lbug_query_result_get_query_summary(lbug_query_result*, lbug_query_summary* out);
bool       lbug_query_result_has_next_query_result(lbug_query_result*);
lbug_state lbug_query_result_get_next_query_result(lbug_query_result*, lbug_query_result* out);
void       lbug_query_result_reset_iterator(lbug_query_result*);
lbug_state lbug_query_result_get_arrow_schema(lbug_query_result*, struct ArrowSchema* out);
lbug_state lbug_query_result_get_next_arrow_chunk(lbug_query_result*, int64_t chunk_size, struct ArrowArray* out);

// QuerySummary (lbug.h:1544,1549,1554)
void   lbug_query_summary_destroy(lbug_query_summary*);
double lbug_query_summary_get_compiling_time(lbug_query_summary*);
double lbug_query_summary_get_execution_time(lbug_query_summary*);

// LogicalType / data_type (lbug.h:846,857,869,877)
void       lbug_data_type_clone(lbug_logical_type*, lbug_logical_type* out);
bool       lbug_data_type_equals(lbug_logical_type*, lbug_logical_type*);
lbug_state lbug_data_type_get_child_type(lbug_logical_type*, lbug_logical_type* out);
lbug_state lbug_data_type_get_num_elements_in_array(lbug_logical_type*, uint64_t* out);

// Value creators (lbug.h:890,901,967,987,994,1006,1012,1018,1080,1094,1101,1107,1154)
lbug_value* lbug_value_create_null_with_data_type(lbug_logical_type*);
void        lbug_value_set_null(lbug_value*, bool is_null);
lbug_value* lbug_value_create_int128(lbug_int128_t val_);
lbug_value* lbug_value_create_decimal(const char* val_, uint32_t precision, uint32_t scale);
lbug_value* lbug_value_create_internal_id(lbug_internal_id_t val_);
lbug_value* lbug_value_create_timestamp_ns(lbug_timestamp_ns_t val_);
lbug_value* lbug_value_create_timestamp_ms(lbug_timestamp_ms_t val_);
lbug_value* lbug_value_create_timestamp_sec(lbug_timestamp_sec_t val_);
lbug_state  lbug_value_create_struct(uint64_t num_fields, const char** field_names, lbug_value** field_values, lbug_value** out);
lbug_state  lbug_value_create_map(uint64_t num_fields, lbug_value** keys, lbug_value** values, lbug_value** out);
lbug_value* lbug_value_clone(lbug_value*);
void        lbug_value_copy(lbug_value* value, lbug_value* other);
lbug_state  lbug_value_get_struct_field_index(lbug_value*, const char* field_name, uint64_t* out);

// Arrow ingest, for WS-E (lbug.h:426,436,450,459)
lbug_state lbug_connection_create_arrow_table(lbug_connection*, const char* table_name, struct ArrowSchema*, struct ArrowArray* arrays, uint64_t num_arrays, lbug_query_result* out);
lbug_state lbug_connection_create_arrow_rel_table(lbug_connection*, const char* table_name, const char* src, const char* dst, struct ArrowSchema*, struct ArrowArray* arrays, uint64_t num_arrays, lbug_query_result* out);
lbug_state lbug_connection_create_arrow_rel_table_csr(lbug_connection*, const char* table_name, const char* src, const char* dst, struct ArrowSchema* indices_schema, struct ArrowArray* indices_arrays, uint64_t num_indices_arrays, struct ArrowSchema* indptr_schema, struct ArrowArray* indptr_arrays, uint64_t num_indptr_arrays, const char* dst_col_name, lbug_query_result* out);
lbug_state lbug_connection_drop_arrow_table(lbug_connection*, const char* table_name, lbug_query_result* out);

// Util (lbug.h:1294,1301,1686)
lbug_state lbug_int128_t_from_string(const char* str, lbug_int128_t* out);
lbug_state lbug_int128_t_to_string(lbug_int128_t val, char** out);
char*      lbug_get_last_error();
```

Mapping notes:
- `lbug_int128_t` → `LbugInt128` (exists). `lbug_internal_id_t` → `LbugInternalId` (exists). `lbug_timestamp_ns_t`/`_ms_t`/`_sec_t` are all single-`int64` → `LbugTimestamp` (exists, shared).
- `const char**`/`char**` out-params → `out IntPtr`. `uint32_t` → `uint`. `int64_t` → `long`.
- `lbug_value**` arrays (struct/map creators) → `[In] IntPtr[]` (mirror the existing `ValueCreateList` pattern at `Native.LibraryImport.cs:231` / `Native.DllImport.cs:305`).
- `const char**` arrays (struct field names) → `[In] IntPtr[]` (array of pointers to UTF-8 strings the caller pins/marshals; C# callers in WS-C build the buffer).

---

## TASK 1 — `LbugQuerySummary` struct mirror + ABI guard

The query-summary handle is `typedef struct { void* _query_summary; } lbug_query_summary;` (lbug.h:281-283) — a single opaque pointer, exactly like `LbugLogicalType`.

- [ ] **Write the failing test.** In `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\StructLayoutTests.cs`, add `typeof(LbugQuerySummary)` to the existing `SinglePointerHandle_IsOnePointerWide` `[Theory]` (after the `LbugLogicalType` line ~34):

  ```csharp
      [InlineData(typeof(LbugQuerySummary))]
  ```

- [ ] **Run it — expect FAIL (compile error: type does not exist).**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~StructLayoutTests"
  ```
  Expected: build fails with `CS0246: The type or namespace name 'LbugQuerySummary' could not be found`.

- [ ] **Implement.** In `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Interop\NativeTypes.cs`, after the `LbugLogicalType` struct (~line 125), add:

  ```csharp
  /// <summary>Mirror of the C <c>lbug_query_summary</c> handle ({ void* }).</summary>
  [StructLayout(LayoutKind.Sequential)]
  internal struct LbugQuerySummary
  {
      public IntPtr QuerySummary;
  }
  ```

- [ ] **Run it — expect PASS.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~StructLayoutTests"
  ```
  Expected: all `StructLayoutTests` pass, including the new `LbugQuerySummary` row.

- [ ] **Commit.**
  ```
  git add -A && git commit -m "feat(interop): add LbugQuerySummary handle mirror + ABI guard"
  ```

---

## TASK 2 — `ArrowSchema` / `ArrowArray` C-Data struct mirrors + ABI guard

These are the standard Arrow C-Data-Interface structs (lbug.h:64-95). `ArrowSchema` has 7 fields up to `release` + `private_data` = 7 pointers + 2 int64 → on 64-bit, 9 × 8 = 72 bytes. `ArrowArray` has 5 × int64 + 5 pointers = 80 bytes. Both are passed by `out` pointer; we mirror them as blittable sequential structs (function-pointer fields modeled as `IntPtr`).

- [ ] **Write the failing test.** In `StructLayoutTests.cs`, add a new `[Fact]` (after `SystemConfig_FieldOffsets_MatchNativeAbi`):

  ```csharp
      // Arrow C-Data-Interface structs (lbug.h:64-95). Sizes are pointer-width dependent;
      // assert the field count works out on 64-bit (the only supported architectures).
      [Fact]
      public void ArrowSchema_MatchesCDataInterfaceLayout()
      {
          // 3 char* + 2 int64 + 2 struct** + release fn-ptr + private_data = 7 ptr + 2 int64.
          Assert.Equal(IntPtr.Size * 7 + 8 * 2, Marshal.SizeOf<ArrowSchema>());
          Assert.Equal(0, (int)Marshal.OffsetOf<ArrowSchema>(nameof(ArrowSchema.Format)));
          Assert.Equal(IntPtr.Size, (int)Marshal.OffsetOf<ArrowSchema>(nameof(ArrowSchema.Name)));
      }

      [Fact]
      public void ArrowArray_MatchesCDataInterfaceLayout()
      {
          // 5 int64 + buffers + children + dictionary + release + private_data = 5 int64 + 5 ptr.
          Assert.Equal(8 * 5 + IntPtr.Size * 5, Marshal.SizeOf<ArrowArray>());
          Assert.Equal(0, (int)Marshal.OffsetOf<ArrowArray>(nameof(ArrowArray.Length)));
          Assert.Equal(8, (int)Marshal.OffsetOf<ArrowArray>(nameof(ArrowArray.NullCount)));
      }
  ```

- [ ] **Run it — expect FAIL (compile error: types do not exist).**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~StructLayoutTests"
  ```
  Expected: `CS0246` for `ArrowSchema` and `ArrowArray`.

- [ ] **Implement.** In `NativeTypes.cs`, after `LbugInterval` (end of file ~line 161), add:

  ```csharp
  /// <summary>
  /// Mirror of the Arrow C-Data-Interface <c>ArrowSchema</c> (lbug.h:64-78). Function-pointer and
  /// opaque fields are modeled as <see cref="IntPtr"/> so the struct stays blittable. WS-E owns the
  /// managed Arrow layer; this is the raw export/ingest seam only.
  /// </summary>
  [StructLayout(LayoutKind.Sequential)]
  internal struct ArrowSchema
  {
      public IntPtr Format;       // const char*
      public IntPtr Name;         // const char*
      public IntPtr Metadata;     // const char*
      public long Flags;          // int64_t
      public long NChildren;      // int64_t
      public IntPtr Children;     // ArrowSchema**
      public IntPtr Dictionary;   // ArrowSchema*
      public IntPtr Release;      // void (*)(ArrowSchema*)
      public IntPtr PrivateData;  // void*
  }

  /// <summary>Mirror of the Arrow C-Data-Interface <c>ArrowArray</c> (lbug.h:80-95).</summary>
  [StructLayout(LayoutKind.Sequential)]
  internal struct ArrowArray
  {
      public long Length;         // int64_t
      public long NullCount;      // int64_t
      public long Offset;         // int64_t
      public long NBuffers;       // int64_t
      public long NChildren;      // int64_t
      public IntPtr Buffers;      // const void**
      public IntPtr Children;     // ArrowArray**
      public IntPtr Dictionary;   // ArrowArray*
      public IntPtr Release;      // void (*)(ArrowArray*)
      public IntPtr PrivateData;  // void*
  }
  ```

- [ ] **Run it — expect PASS.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~StructLayoutTests"
  ```
  Expected: the two new Arrow facts pass.

- [ ] **Commit.**
  ```
  git add -A && git commit -m "feat(interop): add ArrowSchema/ArrowArray C-Data mirrors + ABI guards"
  ```

---

## TASK 3 — Connection control P/Invoke (interrupt, timeout, max-threads)

`lbug_connection_interrupt` returns `void`; the other three return `lbug_state`. None take string params, so the ns2.0 side needs no `ToUtf8` forwarder.

- [ ] **Write the failing test.** Create the declaration-parity test scaffold now — it is the spec that forces both files to stay in sync, and it is pure-managed (no native lib). In `StructLayoutTests.cs`, add a new test class at the end of the file:

  ```csharp
  /// <summary>
  /// Cross-TFM guard: <c>Native.LibraryImport.cs</c> (net7+) and <c>Native.DllImport.cs</c> (ns2.0)
  /// are hand-kept identical. On net10.0 only the LibraryImport partial compiles, so reflection over
  /// the loaded assembly cannot see the ns2.0 declarations. We instead parse both source files and
  /// assert they declare the SAME set of native entry points (the <c>EntryPoint = "..."</c> strings).
  /// Pure text inspection — no native library required.
  /// </summary>
  public sealed class InteropDeclarationParity
  {
      private static readonly System.Text.RegularExpressions.Regex EntryPointPattern =
          new(@"EntryPoint\s*=\s*""(?<ep>[A-Za-z0-9_]+)""");

      private static string InteropDir([System.Runtime.CompilerServices.CallerFilePath] string? thisFile = null)
      {
          // thisFile = ...\test\LadybugDB.Tests\StructLayoutTests.cs
          string testDir = System.IO.Path.GetDirectoryName(thisFile!)!;             // ...\LadybugDB.Tests
          string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(testDir, "..", ".."));
          return System.IO.Path.Combine(root, "src", "LadybugDB", "Interop");
      }

      private static System.Collections.Generic.HashSet<string> EntryPoints(string fileName)
      {
          string path = System.IO.Path.Combine(InteropDir(), fileName);
          string text = System.IO.File.ReadAllText(path);
          var set = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
          foreach (System.Text.RegularExpressions.Match m in EntryPointPattern.Matches(text))
          {
              set.Add(m.Groups["ep"].Value);
          }

          return set;
      }

      [Fact]
      public void LibraryImport_And_DllImport_DeclareTheSameEntryPoints()
      {
          var lib = EntryPoints("Native.LibraryImport.cs");
          var dll = EntryPoints("Native.DllImport.cs");

          var onlyInLib = new System.Collections.Generic.SortedSet<string>(lib);
          onlyInLib.ExceptWith(dll);
          var onlyInDll = new System.Collections.Generic.SortedSet<string>(dll);
          onlyInDll.ExceptWith(lib);

          Assert.True(
              onlyInLib.Count == 0 && onlyInDll.Count == 0,
              $"Interop declaration drift.\n  Only in LibraryImport: {string.Join(", ", onlyInLib)}\n  Only in DllImport: {string.Join(", ", onlyInDll)}");
      }

      [Fact]
      public void BothFiles_DeclareTheRequiredNewEntryPoints()
      {
          var lib = EntryPoints("Native.LibraryImport.cs");
          string[] required =
          {
              "lbug_connection_interrupt",
              "lbug_connection_set_query_timeout",
              "lbug_connection_set_max_num_thread_for_exec",
              "lbug_connection_get_max_num_thread_for_exec",
          };

          foreach (string ep in required)
          {
              Assert.Contains(ep, lib);
          }
      }
  }
  ```

- [ ] **Run it — expect FAIL.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~InteropDeclarationParity"
  ```
  Expected: `BothFiles_DeclareTheRequiredNewEntryPoints` fails (`Assert.Contains` cannot find `lbug_connection_interrupt`). The parity test passes (both files lack them equally) — that is fine.

- [ ] **Implement (net7+).** In `Native.LibraryImport.cs`, after the `ConnectionExecute` declaration (~line 40), add a `// ---- Connection control` block:

  ```csharp
      // ---- Connection control ----------------------------------------------------------------------
      [LibraryImport(LibraryName, EntryPoint = "lbug_connection_interrupt")]
      internal static partial void ConnectionInterrupt(ref LbugConnection connection);

      [LibraryImport(LibraryName, EntryPoint = "lbug_connection_set_query_timeout")]
      internal static partial LbugState ConnectionSetQueryTimeout(ref LbugConnection connection, ulong timeoutInMs);

      [LibraryImport(LibraryName, EntryPoint = "lbug_connection_set_max_num_thread_for_exec")]
      internal static partial LbugState ConnectionSetMaxNumThreadForExec(ref LbugConnection connection, ulong numThreads);

      [LibraryImport(LibraryName, EntryPoint = "lbug_connection_get_max_num_thread_for_exec")]
      internal static partial LbugState ConnectionGetMaxNumThreadForExec(ref LbugConnection connection, out ulong outResult);
  ```

- [ ] **Implement (ns2.0).** In `Native.DllImport.cs`, after the `ConnectionExecute` declaration (~line 51), add the mirror block:

  ```csharp
      // ---- Connection control ----------------------------------------------------------------------
      [DllImport(LibraryName, EntryPoint = "lbug_connection_interrupt", CallingConvention = Conv)]
      internal static extern void ConnectionInterrupt(ref LbugConnection connection);

      [DllImport(LibraryName, EntryPoint = "lbug_connection_set_query_timeout", CallingConvention = Conv)]
      internal static extern LbugState ConnectionSetQueryTimeout(ref LbugConnection connection, ulong timeoutInMs);

      [DllImport(LibraryName, EntryPoint = "lbug_connection_set_max_num_thread_for_exec", CallingConvention = Conv)]
      internal static extern LbugState ConnectionSetMaxNumThreadForExec(ref LbugConnection connection, ulong numThreads);

      [DllImport(LibraryName, EntryPoint = "lbug_connection_get_max_num_thread_for_exec", CallingConvention = Conv)]
      internal static extern LbugState ConnectionGetMaxNumThreadForExec(ref LbugConnection connection, out ulong outResult);
  ```

- [ ] **Run it — expect PASS.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~InteropDeclarationParity"
  ```
  Expected: both `InteropDeclarationParity` tests pass.

- [ ] **Build both TFMs to confirm the source generator accepts the new `[LibraryImport]` shapes.**
  ```
  dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug
  ```
  Expected: build succeeds for `net10.0` and `netstandard2.0` (no `SYSLIB1051`/marshalling diagnostics).

- [ ] **Commit.**
  ```
  git add -A && git commit -m "feat(interop): declare connection control P/Invoke (interrupt/timeout/threads) on both TFMs + parity guard"
  ```

---

## TASK 4 — QuerySummary P/Invoke (get summary, compiling/execution time, destroy)

`lbug_query_result_get_query_summary` returns `lbug_state` and fills `lbug_query_summary*`. `_get_compiling_time`/`_get_execution_time` return `double` directly. `_destroy` returns `void`. None take strings.

- [ ] **Write the failing test.** In `StructLayoutTests.cs`, extend `BothFiles_DeclareTheRequiredNewEntryPoints`'s `required` array (Task 3) by adding these entries:

  ```csharp
              "lbug_query_result_get_query_summary",
              "lbug_query_summary_get_compiling_time",
              "lbug_query_summary_get_execution_time",
              "lbug_query_summary_destroy",
  ```

- [ ] **Run it — expect FAIL.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~InteropDeclarationParity"
  ```
  Expected: `Assert.Contains` fails on `lbug_query_result_get_query_summary`.

- [ ] **Implement (net7+).** In `Native.LibraryImport.cs`, after `QueryResultGetNumTuples` (~line 135) add to the `QueryResult` region:

  ```csharp
      [LibraryImport(LibraryName, EntryPoint = "lbug_query_result_get_query_summary")]
      internal static partial LbugState QueryResultGetQuerySummary(ref LbugQueryResult queryResult, out LbugQuerySummary outQuerySummary);
  ```

  and add a new `// ---- QuerySummary` region after the `FlatTuple` region (after `FlatTupleToString`, ~line 155):

  ```csharp
      // ---- QuerySummary ----------------------------------------------------------------------------
      [LibraryImport(LibraryName, EntryPoint = "lbug_query_summary_destroy")]
      internal static partial void QuerySummaryDestroy(ref LbugQuerySummary querySummary);

      [LibraryImport(LibraryName, EntryPoint = "lbug_query_summary_get_compiling_time")]
      internal static partial double QuerySummaryGetCompilingTime(ref LbugQuerySummary querySummary);

      [LibraryImport(LibraryName, EntryPoint = "lbug_query_summary_get_execution_time")]
      internal static partial double QuerySummaryGetExecutionTime(ref LbugQuerySummary querySummary);
  ```

- [ ] **Implement (ns2.0).** In `Native.DllImport.cs`, after `QueryResultGetNumTuples` (~line 206) add:

  ```csharp
      [DllImport(LibraryName, EntryPoint = "lbug_query_result_get_query_summary", CallingConvention = Conv)]
      internal static extern LbugState QueryResultGetQuerySummary(ref LbugQueryResult queryResult, out LbugQuerySummary outQuerySummary);
  ```

  and after the `FlatTuple` region (after `FlatTupleToString`, ~line 226):

  ```csharp
      // ---- QuerySummary ----------------------------------------------------------------------------
      [DllImport(LibraryName, EntryPoint = "lbug_query_summary_destroy", CallingConvention = Conv)]
      internal static extern void QuerySummaryDestroy(ref LbugQuerySummary querySummary);

      [DllImport(LibraryName, EntryPoint = "lbug_query_summary_get_compiling_time", CallingConvention = Conv)]
      internal static extern double QuerySummaryGetCompilingTime(ref LbugQuerySummary querySummary);

      [DllImport(LibraryName, EntryPoint = "lbug_query_summary_get_execution_time", CallingConvention = Conv)]
      internal static extern double QuerySummaryGetExecutionTime(ref LbugQuerySummary querySummary);
  ```

- [ ] **Run it — expect PASS.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~InteropDeclarationParity"
  ```
  Expected: parity + required-entry-point tests pass.

- [ ] **Commit.**
  ```
  git add -A && git commit -m "feat(interop): declare query-summary P/Invoke (get/compiling/execution/destroy) on both TFMs"
  ```

---

## TASK 5 — QueryResult surface P/Invoke (column type, reset, multi-result chain)

`lbug_query_result_get_column_data_type` and `_get_next_query_result` return `lbug_state` with out-params; `_has_next_query_result` returns `bool`; `_reset_iterator` returns `void`. None take strings.

- [ ] **Write the failing test.** Extend the `required` array (Task 3) with:

  ```csharp
              "lbug_query_result_get_column_data_type",
              "lbug_query_result_reset_iterator",
              "lbug_query_result_has_next_query_result",
              "lbug_query_result_get_next_query_result",
  ```

- [ ] **Run it — expect FAIL.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~InteropDeclarationParity"
  ```
  Expected: `Assert.Contains` fails on `lbug_query_result_get_column_data_type`.

- [ ] **Implement (net7+).** In `Native.LibraryImport.cs`, in the `QueryResult` region after `QueryResultGetQuerySummary` (Task 4), add:

  ```csharp
      [LibraryImport(LibraryName, EntryPoint = "lbug_query_result_get_column_data_type")]
      internal static partial LbugState QueryResultGetColumnDataType(ref LbugQueryResult queryResult, ulong index, out LbugLogicalType outColumnDataType);

      [LibraryImport(LibraryName, EntryPoint = "lbug_query_result_reset_iterator")]
      internal static partial void QueryResultResetIterator(ref LbugQueryResult queryResult);

      [LibraryImport(LibraryName, EntryPoint = "lbug_query_result_has_next_query_result")]
      [return: MarshalAs(UnmanagedType.U1)]
      internal static partial bool QueryResultHasNextQueryResult(ref LbugQueryResult queryResult);

      [LibraryImport(LibraryName, EntryPoint = "lbug_query_result_get_next_query_result")]
      internal static partial LbugState QueryResultGetNextQueryResult(ref LbugQueryResult queryResult, out LbugQueryResult outNextQueryResult);
  ```

- [ ] **Implement (ns2.0).** In `Native.DllImport.cs`, in the `QueryResult` region after `QueryResultGetQuerySummary` (Task 4), add:

  ```csharp
      [DllImport(LibraryName, EntryPoint = "lbug_query_result_get_column_data_type", CallingConvention = Conv)]
      internal static extern LbugState QueryResultGetColumnDataType(ref LbugQueryResult queryResult, ulong index, out LbugLogicalType outColumnDataType);

      [DllImport(LibraryName, EntryPoint = "lbug_query_result_reset_iterator", CallingConvention = Conv)]
      internal static extern void QueryResultResetIterator(ref LbugQueryResult queryResult);

      [DllImport(LibraryName, EntryPoint = "lbug_query_result_has_next_query_result", CallingConvention = Conv)]
      [return: MarshalAs(UnmanagedType.U1)]
      internal static extern bool QueryResultHasNextQueryResult(ref LbugQueryResult queryResult);

      [DllImport(LibraryName, EntryPoint = "lbug_query_result_get_next_query_result", CallingConvention = Conv)]
      internal static extern LbugState QueryResultGetNextQueryResult(ref LbugQueryResult queryResult, out LbugQueryResult outNextQueryResult);
  ```

- [ ] **Run it — expect PASS.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~InteropDeclarationParity"
  ```

- [ ] **Commit.**
  ```
  git add -A && git commit -m "feat(interop): declare query-result column-type/reset/multi-result P/Invoke on both TFMs"
  ```

---

## TASK 6 — Arrow export P/Invoke (schema + chunk)

`lbug_query_result_get_arrow_schema` returns `lbug_state` + `struct ArrowSchema* out`; `_get_next_arrow_chunk` takes `int64_t chunk_size` and returns `lbug_state` + `struct ArrowArray* out`. Use the `ArrowSchema`/`ArrowArray` mirrors from Task 2.

- [ ] **Write the failing test.** Extend the `required` array (Task 3) with:

  ```csharp
              "lbug_query_result_get_arrow_schema",
              "lbug_query_result_get_next_arrow_chunk",
  ```

- [ ] **Run it — expect FAIL.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~InteropDeclarationParity"
  ```

- [ ] **Implement (net7+).** In `Native.LibraryImport.cs`, in the `QueryResult` region after `QueryResultGetNextQueryResult` (Task 5), add:

  ```csharp
      [LibraryImport(LibraryName, EntryPoint = "lbug_query_result_get_arrow_schema")]
      internal static partial LbugState QueryResultGetArrowSchema(ref LbugQueryResult queryResult, out ArrowSchema outSchema);

      [LibraryImport(LibraryName, EntryPoint = "lbug_query_result_get_next_arrow_chunk")]
      internal static partial LbugState QueryResultGetNextArrowChunk(ref LbugQueryResult queryResult, long chunkSize, out ArrowArray outArrowArray);
  ```

- [ ] **Implement (ns2.0).** In `Native.DllImport.cs`, in the `QueryResult` region after `QueryResultGetNextQueryResult` (Task 5), add:

  ```csharp
      [DllImport(LibraryName, EntryPoint = "lbug_query_result_get_arrow_schema", CallingConvention = Conv)]
      internal static extern LbugState QueryResultGetArrowSchema(ref LbugQueryResult queryResult, out ArrowSchema outSchema);

      [DllImport(LibraryName, EntryPoint = "lbug_query_result_get_next_arrow_chunk", CallingConvention = Conv)]
      internal static extern LbugState QueryResultGetNextArrowChunk(ref LbugQueryResult queryResult, long chunkSize, out ArrowArray outArrowArray);
  ```

- [ ] **Run it — expect PASS.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~InteropDeclarationParity"
  ```

- [ ] **Build both TFMs to confirm the `out ArrowSchema`/`out ArrowArray` marshalling is accepted (blittable struct → no source-gen diagnostic).**
  ```
  dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug
  ```

- [ ] **Commit.**
  ```
  git add -A && git commit -m "feat(interop): declare query-result Arrow export P/Invoke (schema/chunk) on both TFMs"
  ```

---

## TASK 7 — LogicalType (data_type) P/Invoke (clone, equals, child type, array size)

`lbug_data_type_clone` returns `void` + out `lbug_logical_type*`; `_equals` returns `bool`; `_get_child_type` returns `lbug_state` + out type; `_get_num_elements_in_array` returns `lbug_state` + `uint64_t* out`. None take strings.

- [ ] **Write the failing test.** Extend the `required` array (Task 3) with:

  ```csharp
              "lbug_data_type_clone",
              "lbug_data_type_equals",
              "lbug_data_type_get_child_type",
              "lbug_data_type_get_num_elements_in_array",
  ```

- [ ] **Run it — expect FAIL.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~InteropDeclarationParity"
  ```

- [ ] **Implement (net7+).** In `Native.LibraryImport.cs`, in the `DataType` region after `DataTypeDestroy` (~line 168), add:

  ```csharp
      [LibraryImport(LibraryName, EntryPoint = "lbug_data_type_clone")]
      internal static partial void DataTypeClone(ref LbugLogicalType dataType, out LbugLogicalType outType);

      [LibraryImport(LibraryName, EntryPoint = "lbug_data_type_equals")]
      [return: MarshalAs(UnmanagedType.U1)]
      internal static partial bool DataTypeEquals(ref LbugLogicalType dataType1, ref LbugLogicalType dataType2);

      [LibraryImport(LibraryName, EntryPoint = "lbug_data_type_get_child_type")]
      internal static partial LbugState DataTypeGetChildType(ref LbugLogicalType dataType, out LbugLogicalType outResult);

      [LibraryImport(LibraryName, EntryPoint = "lbug_data_type_get_num_elements_in_array")]
      internal static partial LbugState DataTypeGetNumElementsInArray(ref LbugLogicalType dataType, out ulong outResult);
  ```

- [ ] **Implement (ns2.0).** In `Native.DllImport.cs`, in the `DataType` region after `DataTypeDestroy` (~line 239), add:

  ```csharp
      [DllImport(LibraryName, EntryPoint = "lbug_data_type_clone", CallingConvention = Conv)]
      internal static extern void DataTypeClone(ref LbugLogicalType dataType, out LbugLogicalType outType);

      [DllImport(LibraryName, EntryPoint = "lbug_data_type_equals", CallingConvention = Conv)]
      [return: MarshalAs(UnmanagedType.U1)]
      internal static extern bool DataTypeEquals(ref LbugLogicalType dataType1, ref LbugLogicalType dataType2);

      [DllImport(LibraryName, EntryPoint = "lbug_data_type_get_child_type", CallingConvention = Conv)]
      internal static extern LbugState DataTypeGetChildType(ref LbugLogicalType dataType, out LbugLogicalType outResult);

      [DllImport(LibraryName, EntryPoint = "lbug_data_type_get_num_elements_in_array", CallingConvention = Conv)]
      internal static extern LbugState DataTypeGetNumElementsInArray(ref LbugLogicalType dataType, out ulong outResult);
  ```

- [ ] **Run it — expect PASS.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~InteropDeclarationParity"
  ```

- [ ] **Commit.**
  ```
  git add -A && git commit -m "feat(interop): declare logical-type P/Invoke (clone/equals/child/array-size) on both TFMs"
  ```

---

## TASK 8 — Value creators: numeric/temporal (int128, decimal, internal_id, timestamp ns/ms/sec)

All return `lbug_value*` → `IntPtr`. `lbug_value_create_decimal` takes `const char* val_` (UTF-8 string) plus two `uint32_t` — this is the only one in this task needing the ns2.0 `*Raw`/`ToUtf8` forwarder pattern. The `int128`/`internal_id`/`timestamp_*` creators take blittable by-value structs.

- [ ] **Write the failing test.** Extend the `required` array (Task 3) with:

  ```csharp
              "lbug_value_create_int128",
              "lbug_value_create_decimal",
              "lbug_value_create_internal_id",
              "lbug_value_create_timestamp_ns",
              "lbug_value_create_timestamp_ms",
              "lbug_value_create_timestamp_sec",
  ```

- [ ] **Run it — expect FAIL.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~InteropDeclarationParity"
  ```

- [ ] **Implement (net7+).** In `Native.LibraryImport.cs`, in the `Value` region after `ValueCreateInterval` (~line 229), add:

  ```csharp
      [LibraryImport(LibraryName, EntryPoint = "lbug_value_create_int128")]
      internal static partial IntPtr ValueCreateInt128(LbugInt128 value);

      [LibraryImport(LibraryName, EntryPoint = "lbug_value_create_decimal", StringMarshalling = StringMarshalling.Utf8)]
      internal static partial IntPtr ValueCreateDecimal(string value, uint precision, uint scale);

      [LibraryImport(LibraryName, EntryPoint = "lbug_value_create_internal_id")]
      internal static partial IntPtr ValueCreateInternalId(LbugInternalId value);

      [LibraryImport(LibraryName, EntryPoint = "lbug_value_create_timestamp_ns")]
      internal static partial IntPtr ValueCreateTimestampNs(LbugTimestamp value);

      [LibraryImport(LibraryName, EntryPoint = "lbug_value_create_timestamp_ms")]
      internal static partial IntPtr ValueCreateTimestampMs(LbugTimestamp value);

      [LibraryImport(LibraryName, EntryPoint = "lbug_value_create_timestamp_sec")]
      internal static partial IntPtr ValueCreateTimestampSec(LbugTimestamp value);
  ```

- [ ] **Implement (ns2.0).** In `Native.DllImport.cs`, in the `Value` region after `ValueCreateInterval` (~line 303), add the blittable creators directly and the decimal creator via a `Raw`/`ToUtf8` forwarder (mirror `ValueCreateString` at lines 287-291):

  ```csharp
      [DllImport(LibraryName, EntryPoint = "lbug_value_create_int128", CallingConvention = Conv)]
      internal static extern IntPtr ValueCreateInt128(LbugInt128 value);

      [DllImport(LibraryName, EntryPoint = "lbug_value_create_decimal", CallingConvention = Conv)]
      private static extern IntPtr ValueCreateDecimalRaw(byte[] value, uint precision, uint scale);

      internal static IntPtr ValueCreateDecimal(string value, uint precision, uint scale)
          => ValueCreateDecimalRaw(ToUtf8(value), precision, scale);

      [DllImport(LibraryName, EntryPoint = "lbug_value_create_internal_id", CallingConvention = Conv)]
      internal static extern IntPtr ValueCreateInternalId(LbugInternalId value);

      [DllImport(LibraryName, EntryPoint = "lbug_value_create_timestamp_ns", CallingConvention = Conv)]
      internal static extern IntPtr ValueCreateTimestampNs(LbugTimestamp value);

      [DllImport(LibraryName, EntryPoint = "lbug_value_create_timestamp_ms", CallingConvention = Conv)]
      internal static extern IntPtr ValueCreateTimestampMs(LbugTimestamp value);

      [DllImport(LibraryName, EntryPoint = "lbug_value_create_timestamp_sec", CallingConvention = Conv)]
      internal static extern IntPtr ValueCreateTimestampSec(LbugTimestamp value);
  ```

  > **Parity note:** the `EntryPoint` strings match across files even though the ns2.0 decimal creator is split into `ValueCreateDecimalRaw` + a forwarder — the parity test keys on `EntryPoint = "..."`, and `lbug_value_create_decimal` appears once in each file. Do **not** add an `EntryPoint` to the forwarder (it has no `[DllImport]`).

- [ ] **Run it — expect PASS.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~InteropDeclarationParity"
  ```

- [ ] **Commit.**
  ```
  git add -A && git commit -m "feat(interop): declare numeric/temporal value creators (int128/decimal/internal_id/timestamp) on both TFMs"
  ```

---

## TASK 9 — Value creators: composite + lifecycle (struct, map, null-with-type, set-null, clone, copy, struct-field-index)

`lbug_value_create_struct` / `_create_map` return `lbug_state` with `lbug_value**` array params and an out-param — mirror the existing `ValueCreateList` shape (`[In] IntPtr[]` + `out IntPtr`). `_create_struct` also takes `const char** field_names` → `[In] IntPtr[]` (array of UTF-8 string pointers; WS-C pins them). `_create_null_with_data_type` and `_clone` return `lbug_value*`. `_set_null` and `_copy` return `void`. `_get_struct_field_index` returns `lbug_state` and takes `const char* field_name` (needs ns2.0 forwarder).

- [ ] **Write the failing test.** Extend the `required` array (Task 3) with:

  ```csharp
              "lbug_value_create_null_with_data_type",
              "lbug_value_set_null",
              "lbug_value_create_struct",
              "lbug_value_create_map",
              "lbug_value_clone",
              "lbug_value_copy",
              "lbug_value_get_struct_field_index",
  ```

- [ ] **Run it — expect FAIL.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~InteropDeclarationParity"
  ```

- [ ] **Implement (net7+).** In `Native.LibraryImport.cs`, in the `Value` region after the Task 8 creators, add:

  ```csharp
      [LibraryImport(LibraryName, EntryPoint = "lbug_value_create_null_with_data_type")]
      internal static partial IntPtr ValueCreateNullWithDataType(ref LbugLogicalType dataType);

      [LibraryImport(LibraryName, EntryPoint = "lbug_value_set_null")]
      internal static partial void ValueSetNull(ref LbugValue value, [MarshalAs(UnmanagedType.U1)] bool isNull);

      [LibraryImport(LibraryName, EntryPoint = "lbug_value_create_struct")]
      internal static partial LbugState ValueCreateStruct(ulong numFields, [In] IntPtr[] fieldNames, [In] IntPtr[] fieldValues, out IntPtr outValue);

      [LibraryImport(LibraryName, EntryPoint = "lbug_value_create_map")]
      internal static partial LbugState ValueCreateMap(ulong numFields, [In] IntPtr[] keys, [In] IntPtr[] values, out IntPtr outValue);

      [LibraryImport(LibraryName, EntryPoint = "lbug_value_clone")]
      internal static partial IntPtr ValueClone(ref LbugValue value);

      [LibraryImport(LibraryName, EntryPoint = "lbug_value_copy")]
      internal static partial void ValueCopy(ref LbugValue value, ref LbugValue other);

      [LibraryImport(LibraryName, EntryPoint = "lbug_value_get_struct_field_index", StringMarshalling = StringMarshalling.Utf8)]
      internal static partial LbugState ValueGetStructFieldIndex(ref LbugValue value, string fieldName, out ulong outResult);
  ```

- [ ] **Implement (ns2.0).** In `Native.DllImport.cs`, in the `Value` region after the Task 8 creators, add (the field-index lookup uses a `Raw`/`ToUtf8` forwarder):

  ```csharp
      [DllImport(LibraryName, EntryPoint = "lbug_value_create_null_with_data_type", CallingConvention = Conv)]
      internal static extern IntPtr ValueCreateNullWithDataType(ref LbugLogicalType dataType);

      [DllImport(LibraryName, EntryPoint = "lbug_value_set_null", CallingConvention = Conv)]
      internal static extern void ValueSetNull(ref LbugValue value, [MarshalAs(UnmanagedType.U1)] bool isNull);

      [DllImport(LibraryName, EntryPoint = "lbug_value_create_struct", CallingConvention = Conv)]
      internal static extern LbugState ValueCreateStruct(ulong numFields, [In] IntPtr[] fieldNames, [In] IntPtr[] fieldValues, out IntPtr outValue);

      [DllImport(LibraryName, EntryPoint = "lbug_value_create_map", CallingConvention = Conv)]
      internal static extern LbugState ValueCreateMap(ulong numFields, [In] IntPtr[] keys, [In] IntPtr[] values, out IntPtr outValue);

      [DllImport(LibraryName, EntryPoint = "lbug_value_clone", CallingConvention = Conv)]
      internal static extern IntPtr ValueClone(ref LbugValue value);

      [DllImport(LibraryName, EntryPoint = "lbug_value_copy", CallingConvention = Conv)]
      internal static extern void ValueCopy(ref LbugValue value, ref LbugValue other);

      [DllImport(LibraryName, EntryPoint = "lbug_value_get_struct_field_index", CallingConvention = Conv)]
      private static extern LbugState ValueGetStructFieldIndexRaw(ref LbugValue value, byte[] fieldName, out ulong outResult);

      internal static LbugState ValueGetStructFieldIndex(ref LbugValue value, string fieldName, out ulong outResult)
          => ValueGetStructFieldIndexRaw(ref value, ToUtf8(fieldName), out outResult);
  ```

- [ ] **Run it — expect PASS.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~InteropDeclarationParity"
  ```

- [ ] **Build both TFMs (the `[In] IntPtr[]` array params are the riskiest source-gen shape; confirm no `SYSLIB` diagnostic).**
  ```
  dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug
  ```

- [ ] **Commit.**
  ```
  git add -A && git commit -m "feat(interop): declare composite/lifecycle value APIs (struct/map/null/clone/copy/field-index) on both TFMs"
  ```

---

## TASK 10 — Arrow ingest P/Invoke (create table / rel table / CSR / drop) — for WS-E

All return `lbug_state`. All take `const char*` name params (need ns2.0 forwarders) plus `struct ArrowSchema*` / `struct ArrowArray*` pointers and a final `lbug_query_result* out`. Pass Arrow structs by `ref ArrowSchema`/`ref ArrowArray` (caller owns and transfers ownership; engine releases). `dst_col_name` may be NULL — declare it `byte[]?` on ns2.0 / `string?` on net7+ so callers can pass null.

- [ ] **Write the failing test.** Extend the `required` array (Task 3) with:

  ```csharp
              "lbug_connection_create_arrow_table",
              "lbug_connection_create_arrow_rel_table",
              "lbug_connection_create_arrow_rel_table_csr",
              "lbug_connection_drop_arrow_table",
  ```

- [ ] **Run it — expect FAIL.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~InteropDeclarationParity"
  ```

- [ ] **Implement (net7+).** In `Native.LibraryImport.cs`, in the `Connection control` region (after Task 3's connection block), add an `// ---- Arrow ingest` block:

  ```csharp
      // ---- Arrow ingest (for WS-E) -----------------------------------------------------------------
      [LibraryImport(LibraryName, EntryPoint = "lbug_connection_create_arrow_table", StringMarshalling = StringMarshalling.Utf8)]
      internal static partial LbugState ConnectionCreateArrowTable(ref LbugConnection connection, string tableName, ref ArrowSchema schema, ref ArrowArray arrays, ulong numArrays, out LbugQueryResult outQueryResult);

      [LibraryImport(LibraryName, EntryPoint = "lbug_connection_create_arrow_rel_table", StringMarshalling = StringMarshalling.Utf8)]
      internal static partial LbugState ConnectionCreateArrowRelTable(ref LbugConnection connection, string tableName, string srcTableName, string dstTableName, ref ArrowSchema schema, ref ArrowArray arrays, ulong numArrays, out LbugQueryResult outQueryResult);

      [LibraryImport(LibraryName, EntryPoint = "lbug_connection_create_arrow_rel_table_csr", StringMarshalling = StringMarshalling.Utf8)]
      internal static partial LbugState ConnectionCreateArrowRelTableCsr(ref LbugConnection connection, string tableName, string srcTableName, string dstTableName, ref ArrowSchema indicesSchema, ref ArrowArray indicesArrays, ulong numIndicesArrays, ref ArrowSchema indptrSchema, ref ArrowArray indptrArrays, ulong numIndptrArrays, string? dstColName, out LbugQueryResult outQueryResult);

      [LibraryImport(LibraryName, EntryPoint = "lbug_connection_drop_arrow_table", StringMarshalling = StringMarshalling.Utf8)]
      internal static partial LbugState ConnectionDropArrowTable(ref LbugConnection connection, string tableName, out LbugQueryResult outQueryResult);
  ```

- [ ] **Implement (ns2.0).** In `Native.DllImport.cs`, in the `Connection control` region (after Task 3's connection block), add with `Raw`/`ToUtf8` forwarders. `dst_col_name` NULL is represented as a `null` `byte[]`:

  ```csharp
      // ---- Arrow ingest (for WS-E) -----------------------------------------------------------------
      [DllImport(LibraryName, EntryPoint = "lbug_connection_create_arrow_table", CallingConvention = Conv)]
      private static extern LbugState ConnectionCreateArrowTableRaw(ref LbugConnection connection, byte[] tableName, ref ArrowSchema schema, ref ArrowArray arrays, ulong numArrays, out LbugQueryResult outQueryResult);

      internal static LbugState ConnectionCreateArrowTable(ref LbugConnection connection, string tableName, ref ArrowSchema schema, ref ArrowArray arrays, ulong numArrays, out LbugQueryResult outQueryResult)
          => ConnectionCreateArrowTableRaw(ref connection, ToUtf8(tableName), ref schema, ref arrays, numArrays, out outQueryResult);

      [DllImport(LibraryName, EntryPoint = "lbug_connection_create_arrow_rel_table", CallingConvention = Conv)]
      private static extern LbugState ConnectionCreateArrowRelTableRaw(ref LbugConnection connection, byte[] tableName, byte[] srcTableName, byte[] dstTableName, ref ArrowSchema schema, ref ArrowArray arrays, ulong numArrays, out LbugQueryResult outQueryResult);

      internal static LbugState ConnectionCreateArrowRelTable(ref LbugConnection connection, string tableName, string srcTableName, string dstTableName, ref ArrowSchema schema, ref ArrowArray arrays, ulong numArrays, out LbugQueryResult outQueryResult)
          => ConnectionCreateArrowRelTableRaw(ref connection, ToUtf8(tableName), ToUtf8(srcTableName), ToUtf8(dstTableName), ref schema, ref arrays, numArrays, out outQueryResult);

      [DllImport(LibraryName, EntryPoint = "lbug_connection_create_arrow_rel_table_csr", CallingConvention = Conv)]
      private static extern LbugState ConnectionCreateArrowRelTableCsrRaw(ref LbugConnection connection, byte[] tableName, byte[] srcTableName, byte[] dstTableName, ref ArrowSchema indicesSchema, ref ArrowArray indicesArrays, ulong numIndicesArrays, ref ArrowSchema indptrSchema, ref ArrowArray indptrArrays, ulong numIndptrArrays, byte[]? dstColName, out LbugQueryResult outQueryResult);

      internal static LbugState ConnectionCreateArrowRelTableCsr(ref LbugConnection connection, string tableName, string srcTableName, string dstTableName, ref ArrowSchema indicesSchema, ref ArrowArray indicesArrays, ulong numIndicesArrays, ref ArrowSchema indptrSchema, ref ArrowArray indptrArrays, ulong numIndptrArrays, string? dstColName, out LbugQueryResult outQueryResult)
          => ConnectionCreateArrowRelTableCsrRaw(ref connection, ToUtf8(tableName), ToUtf8(srcTableName), ToUtf8(dstTableName), ref indicesSchema, ref indicesArrays, numIndicesArrays, ref indptrSchema, ref indptrArrays, numIndptrArrays, dstColName is null ? null : ToUtf8(dstColName), out outQueryResult);

      [DllImport(LibraryName, EntryPoint = "lbug_connection_drop_arrow_table", CallingConvention = Conv)]
      private static extern LbugState ConnectionDropArrowTableRaw(ref LbugConnection connection, byte[] tableName, out LbugQueryResult outQueryResult);

      internal static LbugState ConnectionDropArrowTable(ref LbugConnection connection, string tableName, out LbugQueryResult outQueryResult)
          => ConnectionDropArrowTableRaw(ref connection, ToUtf8(tableName), out outQueryResult);
  ```

  > **ns2.0 nullable note:** if the project does not enable nullable reference types for ns2.0, drop the `?` on `byte[]? dstColName`/`string? dstColName` (a `byte[]` param accepts `null` regardless). Confirm by checking `<Nullable>` in `LadybugDB.csproj`; keep annotations consistent with the existing file.

- [ ] **Run it — expect PASS.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~InteropDeclarationParity"
  ```

- [ ] **Build both TFMs.**
  ```
  dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug
  ```

- [ ] **Commit.**
  ```
  git add -A && git commit -m "feat(interop): declare Arrow ingest P/Invoke (create table/rel/CSR/drop) on both TFMs"
  ```

---

## TASK 11 — Util P/Invoke (last error, int128 string conversions) + `GetLastError` wrapper

`lbug_get_last_error` returns `char*` → `IntPtr` (caller frees via `lbug_destroy_string`); add a `Native.GetLastError()` convenience wrapper next to the existing `GetVersion()` (Native.cs:70). `lbug_int128_t_from_string` takes `const char* str` (ns2.0 forwarder) + `lbug_int128_t* out`; `lbug_int128_t_to_string` takes `lbug_int128_t` by value + `char** out` (out `IntPtr`, caller frees).

- [ ] **Write the failing test.** Extend the `required` array (Task 3) with:

  ```csharp
              "lbug_get_last_error",
              "lbug_int128_t_from_string",
              "lbug_int128_t_to_string",
  ```

- [ ] **Run it — expect FAIL.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~InteropDeclarationParity"
  ```

- [ ] **Implement (net7+).** In `Native.LibraryImport.cs`, after the `Numeric / temporal` region (after `ValueGetBlob`, ~line 315), add a `// ---- Util` block:

  ```csharp
      // ---- Util ------------------------------------------------------------------------------------
      [LibraryImport(LibraryName, EntryPoint = "lbug_get_last_error")]
      internal static partial IntPtr GetLastErrorPtr();

      [LibraryImport(LibraryName, EntryPoint = "lbug_int128_t_from_string", StringMarshalling = StringMarshalling.Utf8)]
      internal static partial LbugState Int128FromString(string str, out LbugInt128 outResult);

      [LibraryImport(LibraryName, EntryPoint = "lbug_int128_t_to_string")]
      internal static partial LbugState Int128ToString(LbugInt128 value, out IntPtr outResult);
  ```

- [ ] **Implement (ns2.0).** In `Native.DllImport.cs`, after the `Numeric / temporal` region (after `ValueGetBlob`, ~line 389), add:

  ```csharp
      // ---- Util ------------------------------------------------------------------------------------
      [DllImport(LibraryName, EntryPoint = "lbug_get_last_error", CallingConvention = Conv)]
      internal static extern IntPtr GetLastErrorPtr();

      [DllImport(LibraryName, EntryPoint = "lbug_int128_t_from_string", CallingConvention = Conv)]
      private static extern LbugState Int128FromStringRaw(byte[] str, out LbugInt128 outResult);

      internal static LbugState Int128FromString(string str, out LbugInt128 outResult)
          => Int128FromStringRaw(ToUtf8(str), out outResult);

      [DllImport(LibraryName, EntryPoint = "lbug_int128_t_to_string", CallingConvention = Conv)]
      internal static extern LbugState Int128ToString(LbugInt128 value, out IntPtr outResult);
  ```

- [ ] **Implement the wrapper.** In `Native.cs`, after `GetVersion()` (line 70), add:

  ```csharp
      /// <summary>Convenience wrapper for <c>lbug_get_last_error</c> (consumes and frees the message).</summary>
      internal static string? GetLastError() => TakeString(GetLastErrorPtr());
  ```

- [ ] **Add a wrapper unit test (pure-managed, no native).** In `StructLayoutTests.cs` `InteropDeclarationParity` class, add:

  ```csharp
      [Fact]
      public void GetLastError_Wrapper_IsDeclaredAgainstBothFiles()
      {
          // The convenience wrapper lives in Native.cs; assert the underlying entry point exists in both.
          Assert.Contains("lbug_get_last_error", EntryPoints("Native.LibraryImport.cs"));
          Assert.Contains("lbug_get_last_error", EntryPoints("Native.DllImport.cs"));
      }
  ```

- [ ] **Run it — expect PASS.**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~InteropDeclarationParity"
  ```

- [ ] **Commit.**
  ```
  git add -A && git commit -m "feat(interop): declare util P/Invoke (last-error/int128-string) + GetLastError wrapper on both TFMs"
  ```

---

## TASK 12 — Final full-suite verification + both-TFM build gate

- [ ] **Build both TFMs (Debug).**
  ```
  dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug
  ```
  Expected: `net10.0` and `netstandard2.0` build clean; **no** `SYSLIB1050`–`SYSLIB1054` source-gen marshalling diagnostics on any new `[LibraryImport]`.

- [ ] **Run the entire test project (native tests will skip when native is absent; managed ABI + parity tests always run).**
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug
  ```
  Expected: `StructLayoutTests` and `InteropDeclarationParity` all pass; existing `SmokeTests`/`TypeMappingTests`/`PreparedStatementTests`/`NativeGateTests` are unchanged (skip without native, pass with it).

- [ ] **Optional native gate (only where the native lib is present).** Confirm nothing regressed at the ABI boundary by forcing the gate on:
  ```
  $env:LADYBUG_REQUIRE_NATIVE=1; dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug; Remove-Item Env:\LADYBUG_REQUIRE_NATIVE
  ```
  Expected: with native present, all tests pass; with native absent this turns skips into failures (so only run where the lib is available — see `AGENTS.md:55-56`).

- [ ] **Final parity sweep — confirm the complete §3 list is present in both files.** Run the parity test in isolation one more time and eyeball the diff between the two interop files:
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~InteropDeclarationParity.LibraryImport_And_DllImport_DeclareTheSameEntryPoints"
  ```
  Expected: PASS (zero entry points unique to either file).

- [ ] **Commit (if any cleanup was needed); otherwise this gate is the natural end of WS-A.**
  ```
  git add -A && git commit -m "test(interop): final WS-A verification — both TFMs build, ABI + declaration-parity green"
  ```

---

## Self-review / done criteria

Tied to the WS-A "Done" column in `03-high-level-plan.md` (*"all §3 functions declared on both TFMs; ABI tests green"*):

- [ ] **Every function in high-level-plan §3 is declared on BOTH `Native.LibraryImport.cs` and `Native.DllImport.cs`** — Connection (4), QueryResult (7: column-type, query-summary, reset, has-next-result, get-next-result, arrow-schema, arrow-chunk), QuerySummary (3), LogicalType (4), Value creators/lifecycle (13), Arrow ingest (4), Util (3). The `InteropDeclarationParity.LibraryImport_And_DllImport_DeclareTheSameEntryPoints` test proves no drift.
- [ ] **`BothFiles_DeclareTheRequiredNewEntryPoints` lists and asserts the full §3 entry-point set** (kept in lockstep as each task extended the `required` array).
- [ ] **Return styles match `lbug.h`:** `lbug_state` → `LbugState`; `double` → `double` (summary times); `bool` → `[return: MarshalAs(UnmanagedType.U1)] bool`; `lbug_value*` → `IntPtr`; `char*`/`char**` → `IntPtr` with `Native.TakeString`/`lbug_destroy_string` ownership. Verified function-by-function against the signature block above.
- [ ] **ns2.0 string params use the `*Raw` + `ToUtf8` forwarder pattern** (decimal creator, struct-field-index, int128-from-string, all four Arrow-ingest name params); net7+ uses `StringMarshalling.Utf8`. Functions with no string params are declared directly on both sides.
- [ ] **`LbugQuerySummary`, `ArrowSchema`, `ArrowArray` are mirrored in `NativeTypes.cs`** with `[StructLayout(LayoutKind.Sequential)]` and guarded in `StructLayoutTests` (`Marshal.SizeOf`/`OffsetOf`, no native lib).
- [ ] **`Native.GetLastError()` convenience wrapper added** alongside `GetVersion()`, freeing via `TakeString`.
- [ ] **All ABI + parity tests are pure-managed and NOT native-gated** (no `Skip.IfNot(TestEnvironment.NativeAvailable, ...)`); they run and pass regardless of whether the native library is present.
- [ ] **`dotnet build LadybugDB.slnx -c Debug` is green on `net10.0` AND `netstandard2.0`** with no source-gen marshalling diagnostics.
- [ ] **`dotnet test` for `LadybugDB.Tests` is green** (managed tests pass; native-gated tests skip cleanly without native, pass with it).
- [ ] **No loader/resolver changes were made** (`Native.cs` `GetCandidateNames`/`Resolve` untouched — global-symbol visibility is WS-F's job); WS-A only added the `GetLastError` wrapper to `Native.cs`.
- [ ] **No public API surface added** — all new declarations are `internal`; the public-facing wrappers (Connection/QueryResult/Value methods) are owned by WS-B/C/D/E/F and consume this interop.
