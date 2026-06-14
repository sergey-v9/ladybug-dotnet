# WS-K — Native 0.17.2 + Packaging Skeletons Implementation Plan
> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]` checkboxes.

**Goal:** Bump the native engine pin to 0.17.2, add buildable-but-empty `LadybugDB.Extensions`, `LadybugDB.Arrow`, and `LadybugDB.SourceGen` library/analyzer skeletons plus the `LadybugDB.Tests.Parity`, `LadybugDB.Tests.Extensions`, and `LadybugDB.Benchmarks` project skeletons, wire them all into the solution and the Cake `Pack`/`VerifyPackages` pipeline, and complete the Phase‑1 netstandard2.0 dependency feasibility check.

**Architecture:** All new library projects multi-target `net10.0;netstandard2.0` (matching core) and import `nuget/nuget-package.props` style metadata; `LadybugDB.SourceGen` is a `netstandard2.0` Roslyn analyzer that ships *inside* the core `LadybugDB` package as an `analyzers/dotnet/cs` asset (no separate NuGet id). The Cake pipeline (`cake/**`) reads `version.txt` as the single source of truth and stages prebuilt `liblbug-*` assets per RID; `VerifyPackagesTask` is extended to assert the new packages and the bundled analyzer.

**Tech Stack:** .NET 10 SDK (10.0.301) + `netstandard2.0`; MSBuild SDK-style projects; Cake.Frosting 6.2.0 build host; xUnit 2.9.2 + Xunit.SkippableFact 1.5.61 for tests; BenchmarkDotNet for benchmarks; `.slnx` solution format.

---

## Files

**Modify**
- `W:\code\ladybug\tools\csharp_api\version.txt` — `0.17.0.1` → `0.17.2`.
- `W:\code\ladybug\tools\csharp_api\LadybugDB.slnx` — add all new projects.
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj` — reference `LadybugDB.SourceGen` as an analyzer + pack it into the core package.
- `W:\code\ladybug\tools\csharp_api\cake\BuildContext.cs` — add paths for the new packable projects.
- `W:\code\ladybug\tools\csharp_api\cake\Tasks\Pipeline.cs` — pack the new `LadybugDB.Extensions` / `LadybugDB.Arrow` packages.
- `W:\code\ladybug\tools\csharp_api\cake\Tasks\VerifyPackagesTask.cs` — assert the new packages + the bundled analyzer.
- `W:\code\ladybug\tools\csharp_api\.github\workflows\ci.yml` — extend trigger `paths` for the new dirs.
- `W:\code\ladybug\tools\csharp_api\.github\workflows\release.yml` — document the new package ids in the trusted-publishing comment block.
- `W:\code\ladybug\tools\csharp_api\nuget\nuget-package.props` — no change required (core metadata stays); a sibling `nuget\library-package.props` is created (see below).

**Create — library skeletons**
- `W:\code\ladybug\tools\csharp_api\nuget\library-package.props` — shared package metadata for the new library packages.
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB.Extensions\LadybugDB.Extensions.csproj`
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB.Extensions\AssemblyMarker.cs`
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB.Arrow\LadybugDB.Arrow.csproj`
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB.Arrow\AssemblyMarker.cs`
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB.SourceGen\LadybugDB.SourceGen.csproj`
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB.SourceGen\PlaceholderGenerator.cs`

**Create — test / bench skeletons**
- `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\LadybugDB.Tests.Parity.csproj`
- `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\TestEnvironment.cs`
- `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\SkeletonTests.cs`
- `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj`
- `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\SkeletonTests.cs`
- `W:\code\ladybug\tools\csharp_api\benchmarks\LadybugDB.Benchmarks\LadybugDB.Benchmarks.csproj`
- `W:\code\ladybug\tools\csharp_api\benchmarks\LadybugDB.Benchmarks\Program.cs`

**Create — feasibility doc**
- `W:\code\ladybug\tools\csharp_api\docs\parity-2026-06\K-ns20-feasibility.md`

**Test (existing, used as gates)**
- `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\StructLayoutTests.cs` — unchanged by K, stays green (native pin must not alter ABI).
- The Cake `VerifyPackages` task is itself the packaging "test" — it throws `CakeException` on a missing package/entry.

---

## Important pre-flight finding (read before Task 1)

`version.txt` must move to `0.17.2` per the pinned contract (high-level plan §2, design §4.1 / §3 "the native bump is 0.17.0.1 → 0.17.2"). **However, at planning time the upstream engine repo `LadybugDB/ladybug` has no `v0.17.2` release — the latest is `v0.17.1`.** `gh release view v0.17.2 --repo LadybugDB/ladybug` returns `release not found`; `v0.17.1` carries exactly the asset names `BuildContext.NativeAssets` expects (`liblbug-windows-x86_64.zip`, `liblbug-linux-x86_64.tar.gz`, `liblbug-linux-aarch64.tar.gz`, `liblbug-osx-x86_64.tar.gz`, `liblbug-osx-arm64.tar.gz`).

Consequence for FetchNatives: when the worker runs `FetchNatives` after the bump, `EngineVersion` derives to `v0.17.2` and `gh release download v0.17.2` will fail. **Task 8 includes a probe step and a documented fallback**: if `v0.17.2` is absent, run the native-staging Cake targets with `--engine-version v0.17.1` (the binding ABI is identical across 0.17.0→0.17.2 per design §3/§9) and record the discrepancy as an open question for the orchestrator. Do **not** silently downgrade `version.txt`; the package-family version stays `0.17.2`, only the *fetched native asset* falls back. This is surfaced in the returned `openQuestions`.

---

## TASK 1 — Bump `version.txt` to 0.17.2 (drives the whole family)

- [ ] **Read** `W:\code\ladybug\tools\csharp_api\version.txt` to confirm it is exactly `0.17.0.1`.
- [ ] **Write the failing check first.** Run the version-derivation assertion through the build host (no separate test file needed — `BuildContext` is the unit under test). Run:
  ```powershell
  dotnet run --project W:\code\ladybug\tools\csharp_api\cake\LadybugDB.Build.csproj -- --target Restore --dryrun
  ```
  Then capture the *current* derived version by invoking the host's version logic indirectly — run:
  ```powershell
  Get-Content W:\code\ladybug\tools\csharp_api\version.txt
  ```
  **Expected (pre-change):** prints `0.17.0.1`. This is the "red" baseline: the family is still on the old engine pin.
- [ ] **Edit** `W:\code\ladybug\tools\csharp_api\version.txt`: replace the single line `0.17.0.1` with `0.17.2`.
- [ ] **Run / expected PASS:**
  ```powershell
  Get-Content W:\code\ladybug\tools\csharp_api\version.txt
  ```
  **Expected:** prints `0.17.2`. Then confirm the host derives the engine release correctly:
  ```powershell
  dotnet build W:\code\ladybug\tools\csharp_api\cake\LadybugDB.Build.csproj -c Debug --nologo
  ```
  **Expected:** `Build succeeded. 0 Error(s)` (`BuildContext.DeriveEngineVersion` accepts the 3-segment `0.17.2`; `StripVersionSuffixes` leaves it intact).
- [ ] **Commit:**
  ```powershell
  git -C W:\code\ladybug\tools\csharp_api add version.txt
  git -C W:\code\ladybug\tools\csharp_api commit -m @'
chore(native): bump engine pin to 0.17.2

API verified stable across 0.17.0 -> 0.17.2; updates the single
source of truth for the package family version.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
  ```

---

## TASK 2 — ns2.0 dependency feasibility doc (Phase‑1 verification)

This is the mandated Phase‑1 check (design §4.1, plan risk row "ns2.0 dep gaps"). The conclusions below are **verified against nuget.org at planning time** — the worker re-confirms with the same commands and records actual versions.

- [ ] **Create** `W:\code\ladybug\tools\csharp_api\docs\parity-2026-06\K-ns20-feasibility.md` with this content:
  ````markdown
  # WS-K — netstandard2.0 dependency feasibility (Phase 1)

  Goal: confirm every third-party dependency the new packages take has an acceptable
  `netstandard2.0` asset, so all packages can multi-target `net10.0;netstandard2.0`
  (design §4.1). If any dependency lacks ns2.0, the fallback is to keep the package
  multi-targeted and `#if NET`-gate **only** the affected type — never to drop ns2.0
  for the whole package.

  ## Verification commands

  ```bash
  # lib/ TFMs inside a given package version:
  curl -s "https://api.nuget.org/v3-flatcontainer/<id-lower>/<ver>/<id-lower>.<ver>.nupkg" -o pkg.nupkg
  unzip -l pkg.nupkg | grep -i 'lib/'
  ```

  ## Results (verified 2026-06-14)

  | Dependency (package) | Used by | ns2.0 asset? | Chosen floor | Notes |
  |---|---|---|---|---|
  | `Microsoft.Extensions.DependencyInjection.Abstractions` | Extensions (WS-G) | ✅ | 8.0.0 | `IServiceCollection`; ns2.0 ships. Latest stable line 10.0.x. |
  | `Microsoft.Extensions.Options` | Extensions (WS-G) | ✅ | 8.0.0 | ns2.0 ships. |
  | `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` | Extensions health (WS-G) | ✅ | 8.0.0 | **`IHealthCheck` lives in `.Abstractions`, which HAS `lib/netstandard2.0/`** (verified 8.0.0 and 9.0.0). No `#if NET` gate needed. |
  | `Microsoft.Extensions.Diagnostics.HealthChecks` (impl) | Extensions health (WS-G) | ✅ | 8.0.0 | Only needed if the registration sugar requires the concrete builder; `.Abstractions` alone covers `IHealthCheck`. |
  | `Microsoft.Bcl.AsyncInterfaces` | Extensions (`IAsyncEnumerable` on ns2.0) | ✅ | 8.0.0 | Provides `IAsyncEnumerable<T>` for ns2.0; net10 has it intrinsically. Reference only `Condition="'$(TargetFramework)'=='netstandard2.0'"`. |
  | `System.Text.Json` | Extensions (`ToJson`) | ✅ | 8.0.0 | ns2.0 ships. |
  | `System.Runtime.Numerics` (`BigInteger`) | core DECIMAL (WS-C) | ✅ | built-in | ns2.0-safe (design §5.2). |
  | `Apache.Arrow` | Arrow package (WS-E) | ✅ | 18.0.0 | `lib/netstandard2.0/Apache.Arrow.dll` verified present in 18.0.0. |
  | `System.Diagnostics.DiagnosticSource` | core OTel (WS-H) | ✅ | 8.0.0 | `ActivitySource`/`Meter` on ns2.0 (design §5.4). |
  | `Microsoft.CodeAnalysis.CSharp` | SourceGen analyzer (WS-I) | n/a | 4.8.0 | Analyzer itself targets ns2.0; referenced `PrivateAssets="all"`. |
  | `BenchmarkDotNet` | Benchmarks (WS-J) | n/a | 0.14.0 | Benchmark host is `net10.0` only; not packed. |

  ## Conclusion

  **No `#if NET` gate is required for any planned dependency** — including HealthChecks,
  because `IHealthCheck` is in `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions`
  which ships `netstandard2.0`. All new library packages multi-target `net10.0;netstandard2.0`.

  ## Documented fallback (if a future dependency lacks ns2.0)

  Keep the package multi-targeted; wrap the single affected public type in
  `#if NET` … `#endif`, and reference the dependency with
  `Condition="'$(TargetFramework)' != 'netstandard2.0'"`. Dropping ns2.0 from a whole
  package is a flagged decision, not a default.
  ````
- [ ] **Run / expected PASS — re-verify the two riskiest claims** (HealthChecks ns2.0 and Apache.Arrow ns2.0):
  ```bash
  curl -s "https://api.nuget.org/v3-flatcontainer/microsoft.extensions.diagnostics.healthchecks.abstractions/8.0.0/microsoft.extensions.diagnostics.healthchecks.abstractions.8.0.0.nupkg" -o /tmp/hc.nupkg && unzip -l /tmp/hc.nupkg | grep -i 'lib/netstandard2.0'
  curl -s "https://api.nuget.org/v3-flatcontainer/apache.arrow/18.0.0/apache.arrow.18.0.0.nupkg" -o /tmp/arrow.nupkg && unzip -l /tmp/arrow.nupkg | grep -i 'lib/netstandard2.0'
  ```
  **Expected:** both `grep`s print a `lib/netstandard2.0/...dll` line. If either is empty, update the table + apply the documented `#if NET` fallback and note it in `openQuestions`.
- [ ] **Commit:**
  ```powershell
  git -C W:\code\ladybug\tools\csharp_api add docs/parity-2026-06/K-ns20-feasibility.md
  git -C W:\code\ladybug\tools\csharp_api commit -m @'
docs(parity): record ns2.0 dependency feasibility check

Confirms Microsoft.Extensions.* abstractions, Microsoft.Bcl.AsyncInterfaces,
HealthChecks.Abstractions, and Apache.Arrow all ship netstandard2.0 assets;
no #if NET gate needed. Documents the per-type fallback otherwise.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
  ```

---

## TASK 3 — Shared `library-package.props` for the new library packages

Mirrors `nuget/nuget-package.props` metadata but with a per-project `PackageId`/`Description` and *no* core-specific README pin. Packs are opt-in via `IsPackable`.

- [ ] **Create** `W:\code\ladybug\tools\csharp_api\nuget\library-package.props`:
  ```xml
  <Project>

    <!-- Shared NuGet metadata for the LadybugDB satellite library packages
         (LadybugDB.Extensions, LadybugDB.Arrow). Each importer sets its own
         PackageId/Title/Description; common fields and versioning live here. -->
    <PropertyGroup>
      <!-- Single source of truth: version.txt at the binding root (e.g. 0.17.2). The cake/
           pipeline also reads this file; this default only applies to a direct `dotnet pack`. -->
      <Version Condition="'$(Version)' == ''">$([System.IO.File]::ReadAllText('$(CSharpDir)version.txt').Trim())</Version>
      <Authors>LadybugDB</Authors>
      <Company>LadybugDB</Company>
      <Product>Ladybug</Product>
      <Copyright>Copyright (c) LadybugDB</Copyright>
      <PackageLicenseExpression>MIT</PackageLicenseExpression>
      <PackageProjectUrl>https://github.com/ladybugdb/ladybug</PackageProjectUrl>
      <RepositoryUrl>https://github.com/LadybugDB/ladybug-dotnet</RepositoryUrl>
      <RepositoryType>git</RepositoryType>
      <PublishRepositoryUrl>true</PublishRepositoryUrl>
      <PackageReadmeFile>README.md</PackageReadmeFile>
      <IncludeSymbols>true</IncludeSymbols>
      <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    </PropertyGroup>

    <ItemGroup>
      <None Include="$(CSharpDir)README.md" Pack="true" PackagePath="\" Condition="Exists('$(CSharpDir)README.md')" />
    </ItemGroup>

  </Project>
  ```
- [ ] **Run / expected PASS** (props file is XML — verify it parses by building the existing solution, which is unaffected but proves no stray import broke):
  ```powershell
  dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug --nologo
  ```
  **Expected:** `Build succeeded. 0 Error(s)` (file is not yet imported anywhere; this just confirms it is well-formed and the repo still builds).
- [ ] **Commit:**
  ```powershell
  git -C W:\code\ladybug\tools\csharp_api add nuget/library-package.props
  git -C W:\code\ladybug\tools\csharp_api commit -m @'
build(nuget): add shared metadata props for satellite library packages

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
  ```

---

## TASK 4 — `LadybugDB.SourceGen` analyzer skeleton (netstandard2.0)

The generator ships *inside* the core package as an analyzer asset (design §5.3) — it is **not** a separately published NuGet id. The skeleton is an empty `IIncrementalGenerator` that emits nothing.

- [ ] **Write the failing build first.** Create `W:\code\ladybug\tools\csharp_api\src\LadybugDB.SourceGen\LadybugDB.SourceGen.csproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

    <!-- Roslyn source generator for [LadybugRow] POCO mapping (WS-I fills it in).
         Ships as an analyzer asset inside the core LadybugDB package, NOT as its own
         NuGet id. Must target netstandard2.0 (Roslyn analyzer requirement). -->
    <PropertyGroup>
      <TargetFramework>netstandard2.0</TargetFramework>
      <RootNamespace>LadybugDB.SourceGen</RootNamespace>
      <IsPackable>false</IsPackable>
      <IsRoslynComponent>true</IsRoslynComponent>
      <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
      <!-- Analyzers run in the compiler; no doc file, no implicit usings to keep it self-contained. -->
      <GenerateDocumentationFile>false</GenerateDocumentationFile>
      <ImplicitUsings>disable</ImplicitUsings>
      <Nullable>enable</Nullable>
    </PropertyGroup>

    <ItemGroup>
      <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
    </ItemGroup>

  </Project>
  ```
- [ ] Create the placeholder generator `W:\code\ladybug\tools\csharp_api\src\LadybugDB.SourceGen\PlaceholderGenerator.cs`:
  ```csharp
  using Microsoft.CodeAnalysis;

  namespace LadybugDB.SourceGen;

  /// <summary>
  /// Placeholder incremental generator so the analyzer project builds and packs as an
  /// analyzer asset inside the core LadybugDB package. WS-I replaces the body with the
  /// real <c>[LadybugRow]</c> POCO mapper. Emitting nothing keeps the core package
  /// behavior unchanged until WS-I lands.
  /// </summary>
  [Generator(LanguageNames.CSharp)]
  public sealed class PlaceholderGenerator : IIncrementalGenerator
  {
      public void Initialize(IncrementalGeneratorInitializationContext context)
      {
          // Intentionally emits no source. WS-I implements the real generator here.
      }
  }
  ```
- [ ] **Run / expected FAIL then PASS.** Because the project is not in the solution yet, build it directly:
  ```powershell
  dotnet build W:\code\ladybug\tools\csharp_api\src\LadybugDB.SourceGen\LadybugDB.SourceGen.csproj -c Debug --nologo
  ```
  **Expected:** `Build succeeded. 0 Error(s)` (single ns2.0 TFM, only `Microsoft.CodeAnalysis.CSharp` referenced). If the root `Directory.Build.props` `GenerateDocumentationFile=true` collides, the project-level `false` overrides it — confirm no CS1591 noise.
- [ ] **Commit:**
  ```powershell
  git -C W:\code\ladybug\tools\csharp_api add src/LadybugDB.SourceGen
  git -C W:\code\ladybug\tools\csharp_api commit -m @'
feat(sourcegen): add empty Roslyn analyzer skeleton (ns2.0)

Placeholder IIncrementalGenerator; WS-I implements [LadybugRow] mapping.
Ships as an analyzer asset inside the core package.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
  ```

---

## TASK 5 — Bundle the analyzer into the core `LadybugDB` package

The analyzer must (a) be referenced by core so the generator runs during core's own compile and (b) be packed into the core `.nupkg` under `analyzers/dotnet/cs` with no compile-time/runtime reference leaking out.

- [ ] **Write the failing assertion first** — extend `VerifyPackagesTask` later (Task 11) will assert the bundled DLL; for now prove the pack *contains* it via a direct pack. First confirm it is currently **absent** (red):
  ```powershell
  dotnet pack W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj -c Debug -o W:\code\ladybug\tools\csharp_api\artifacts\tmp-k5 --nologo -p:Version=0.17.2
  ```
  Then list the analyzer entries:
  ```powershell
  $z=[System.IO.Compression.ZipFile]::OpenRead((Get-ChildItem W:\code\ladybug\tools\csharp_api\artifacts\tmp-k5\LadybugDB.0.17.2.nupkg).FullName); $z.Entries | ? { $_.FullName -like 'analyzers/*' } | % FullName; $z.Dispose()
  ```
  **Expected (red):** prints nothing (no analyzer asset yet).
- [ ] **Edit** `W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj`. Add the analyzer project reference (`ReferenceOutputAssembly=false` so no assembly reference leaks; `OutputItemType=Analyzer` so it runs at compile time) and a pack target. Insert after the existing `<ItemGroup>` with `InternalsVisibleTo`, before the `<Import .../>`:
  ```xml
    <!-- The POCO source generator runs during this package's own compile AND ships inside this
         package as an analyzer asset (design 5.3). ReferenceOutputAssembly=false keeps the
         analyzer DLL out of the managed reference closure. -->
    <ItemGroup>
      <ProjectReference Include="..\LadybugDB.SourceGen\LadybugDB.SourceGen.csproj"
                        OutputItemType="Analyzer"
                        ReferenceOutputAssembly="false" />
    </ItemGroup>

    <!-- Pack the built generator under analyzers/dotnet/cs so consumers of the LadybugDB package
         get [LadybugRow] mapping with no extra package reference. -->
    <Target Name="PackLadybugAnalyzer" BeforeTargets="GenerateNuspec" DependsOnTargets="ResolveProjectReferences">
      <ItemGroup>
        <_LadybugAnalyzerDll Include="$(MSBuildThisFileDirectory)..\LadybugDB.SourceGen\bin\$(Configuration)\netstandard2.0\LadybugDB.SourceGen.dll" />
        <None Include="@(_LadybugAnalyzerDll)"
              Condition="Exists('%(FullPath)')"
              Pack="true"
              PackagePath="analyzers/dotnet/cs"
              Visible="false" />
      </ItemGroup>
    </Target>
  ```
- [ ] **Run / expected PASS.** Re-pack and re-list:
  ```powershell
  Remove-Item -Recurse -Force W:\code\ladybug\tools\csharp_api\artifacts\tmp-k5 -ErrorAction SilentlyContinue
  dotnet pack W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj -c Debug -o W:\code\ladybug\tools\csharp_api\artifacts\tmp-k5 --nologo -p:Version=0.17.2
  $z=[System.IO.Compression.ZipFile]::OpenRead((Get-ChildItem W:\code\ladybug\tools\csharp_api\artifacts\tmp-k5\LadybugDB.0.17.2.nupkg).FullName); $z.Entries | ? { $_.FullName -like 'analyzers/*' } | % FullName; $z.Dispose()
  ```
  **Expected (green):** prints `analyzers/dotnet/cs/LadybugDB.SourceGen.dll`. Also confirm the core still builds both TFMs and the analyzer does not break compile:
  ```powershell
  dotnet build W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj -c Debug --nologo
  ```
  **Expected:** `Build succeeded. 0 Error(s)`.
- [ ] Clean the temp artifacts: `Remove-Item -Recurse -Force W:\code\ladybug\tools\csharp_api\artifacts\tmp-k5`.
- [ ] **Commit:**
  ```powershell
  git -C W:\code\ladybug\tools\csharp_api add src/LadybugDB/LadybugDB.csproj
  git -C W:\code\ladybug\tools\csharp_api commit -m @'
build(core): bundle source generator as analyzer asset

References LadybugDB.SourceGen with OutputItemType=Analyzer and packs its
DLL under analyzers/dotnet/cs in the core package (design 5.3).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
  ```

---

## TASK 6 — `LadybugDB.Extensions` library skeleton (packable, multi-TFM)

- [ ] **Create** `W:\code\ladybug\tools\csharp_api\src\LadybugDB.Extensions\LadybugDB.Extensions.csproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
      <TargetFrameworks>net10.0;netstandard2.0</TargetFrameworks>
      <AssemblyName>LadybugDB.Extensions</AssemblyName>
      <RootNamespace>LadybugDB.Extensions</RootNamespace>
      <DebugType>portable</DebugType>
      <IsPackable>true</IsPackable>
      <PackageId>LadybugDB.Extensions</PackageId>
      <Title>LadybugDB.Extensions - DI, health, resilience, streaming &amp; export</Title>
      <Description>.NET ecosystem layer for LadybugDB: dependency injection, health checks, a resilience executor, IAsyncEnumerable streaming sugar, row access, and query-result export (JSON/CSV/DataTable).</Description>
      <PackageTags>graph;graph-database;ladybug;dependency-injection;healthcheck;resilience;export</PackageTags>
    </PropertyGroup>

    <PropertyGroup Condition="'$(TargetFramework)' == 'net10.0'">
      <IsAotCompatible>true</IsAotCompatible>
    </PropertyGroup>

    <ItemGroup>
      <ProjectReference Include="..\LadybugDB\LadybugDB.csproj" />
    </ItemGroup>

    <ItemGroup>
      <InternalsVisibleTo Include="LadybugDB.Tests.Extensions" />
    </ItemGroup>

    <Import Project="$(CSharpDir)nuget\library-package.props" />

  </Project>
  ```
  > Note: WS-G adds the actual `Microsoft.Extensions.*` / `Microsoft.Bcl.AsyncInterfaces` `PackageReference`s at the versions pinned in `K-ns20-feasibility.md`. The skeleton intentionally has none yet so it builds empty.
- [ ] **Create** the marker source `W:\code\ladybug\tools\csharp_api\src\LadybugDB.Extensions\AssemblyMarker.cs`:
  ```csharp
  namespace LadybugDB.Extensions;

  /// <summary>
  /// Placeholder so the assembly has at least one type and packs cleanly. WS-G replaces this
  /// with the DI/health/resilience/streaming/export surface.
  /// </summary>
  internal static class AssemblyMarker
  {
      internal const string Name = "LadybugDB.Extensions";
  }
  ```
- [ ] **Run / expected PASS** (build both TFMs directly):
  ```powershell
  dotnet build W:\code\ladybug\tools\csharp_api\src\LadybugDB.Extensions\LadybugDB.Extensions.csproj -c Debug --nologo
  ```
  **Expected:** `Build succeeded. 0 Error(s)` with output for both `net10.0` and `netstandard2.0` (it transitively pulls the core project, which already multi-targets).
- [ ] **Commit:**
  ```powershell
  git -C W:\code\ladybug\tools\csharp_api add src/LadybugDB.Extensions
  git -C W:\code\ladybug\tools\csharp_api commit -m @'
feat(extensions): add empty LadybugDB.Extensions package skeleton

Multi-targets net10.0;netstandard2.0, references core, packs with
library metadata. WS-G fills in DI/health/resilience/export.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
  ```

---

## TASK 7 — `LadybugDB.Arrow` library skeleton (packable, multi-TFM)

- [ ] **Create** `W:\code\ladybug\tools\csharp_api\src\LadybugDB.Arrow\LadybugDB.Arrow.csproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
      <TargetFrameworks>net10.0;netstandard2.0</TargetFrameworks>
      <AssemblyName>LadybugDB.Arrow</AssemblyName>
      <RootNamespace>LadybugDB.Arrow</RootNamespace>
      <DebugType>portable</DebugType>
      <IsPackable>true</IsPackable>
      <PackageId>LadybugDB.Arrow</PackageId>
      <Title>LadybugDB.Arrow - Apache Arrow interop</Title>
      <Description>Apache.Arrow-typed RecordBatch export, Arrow ingest, and CSR over LadybugDB's raw Arrow C-Data-Interface export. Keeps the Apache.Arrow dependency off the core and Extensions packages.</Description>
      <PackageTags>graph;graph-database;ladybug;apache-arrow;arrow;interop</PackageTags>
    </PropertyGroup>

    <PropertyGroup Condition="'$(TargetFramework)' == 'net10.0'">
      <IsAotCompatible>true</IsAotCompatible>
    </PropertyGroup>

    <ItemGroup>
      <ProjectReference Include="..\LadybugDB\LadybugDB.csproj" />
    </ItemGroup>

    <Import Project="$(CSharpDir)nuget\library-package.props" />

  </Project>
  ```
  > Note: WS-E adds `<PackageReference Include="Apache.Arrow" Version="18.0.0" />` at the version pinned in `K-ns20-feasibility.md`. The skeleton has none so it builds empty.
- [ ] **Create** `W:\code\ladybug\tools\csharp_api\src\LadybugDB.Arrow\AssemblyMarker.cs`:
  ```csharp
  namespace LadybugDB.Arrow;

  /// <summary>
  /// Placeholder so the assembly has at least one type and packs cleanly. WS-E replaces this
  /// with the Apache.Arrow RecordBatch/CSR surface.
  /// </summary>
  internal static class AssemblyMarker
  {
      internal const string Name = "LadybugDB.Arrow";
  }
  ```
- [ ] **Run / expected PASS:**
  ```powershell
  dotnet build W:\code\ladybug\tools\csharp_api\src\LadybugDB.Arrow\LadybugDB.Arrow.csproj -c Debug --nologo
  ```
  **Expected:** `Build succeeded. 0 Error(s)` for both TFMs.
- [ ] **Commit:**
  ```powershell
  git -C W:\code\ladybug\tools\csharp_api add src/LadybugDB.Arrow
  git -C W:\code\ladybug\tools\csharp_api commit -m @'
feat(arrow): add empty LadybugDB.Arrow package skeleton

Multi-targets net10.0;netstandard2.0, references core, packs with
library metadata. WS-E fills in Apache.Arrow RecordBatch/CSR.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
  ```

---

## TASK 8 — Wire all new projects into `LadybugDB.slnx`; verify FetchNatives

- [ ] **Edit** `W:\code\ladybug\tools\csharp_api\LadybugDB.slnx` to add the three src libraries and three test/bench skeletons (those are created in Tasks 9–10; add them here now so the solution is authoritative, then build after each project exists). Replace the file with:
  ```xml
  <Solution>
    <Folder Name="/src/">
      <Project Path="src/LadybugDB/LadybugDB.csproj" />
      <Project Path="src/LadybugDB.Extensions/LadybugDB.Extensions.csproj" />
      <Project Path="src/LadybugDB.Arrow/LadybugDB.Arrow.csproj" />
      <Project Path="src/LadybugDB.SourceGen/LadybugDB.SourceGen.csproj" />
    </Folder>
    <Folder Name="/test/">
      <Project Path="test/LadybugDB.Tests/LadybugDB.Tests.csproj" />
      <Project Path="test/LadybugDB.Tests.Parity/LadybugDB.Tests.Parity.csproj" />
      <Project Path="test/LadybugDB.Tests.Extensions/LadybugDB.Tests.Extensions.csproj" />
    </Folder>
    <Folder Name="/benchmarks/">
      <Project Path="benchmarks/LadybugDB.Benchmarks/LadybugDB.Benchmarks.csproj" />
    </Folder>
  </Solution>
  ```
  > The test/bench `.csproj`s referenced here are created in Tasks 9 and 10. If executing strictly in order, perform this edit's *solution build verification* step after Task 10. The slnx edit itself is committed now; the green build gate is the last step of Task 10.
- [ ] **Run / expected PASS (partial — src only so far):** confirm the three src skeletons + core all build via the solution restore:
  ```powershell
  dotnet restore W:\code\ladybug\tools\csharp_api\LadybugDB.slnx
  ```
  **Expected:** restore succeeds for the src projects (test/bench restore until Tasks 9–10 add them; if the .slnx already lists not-yet-created projects, complete Tasks 9–10 before the full solution build at Task 10's end).
- [ ] **Verify FetchNatives against the bumped pin (the engine-asset probe).** Run the Cake `FetchNatives` target, which derives `EngineVersion=v0.17.2` from the bumped `version.txt`:
  ```powershell
  dotnet run --project W:\code\ladybug\tools\csharp_api\cake\LadybugDB.Build.csproj -- --target FetchNatives
  ```
  **Expected — two outcomes:**
  - **If `v0.17.2` exists upstream:** it stages `lib\runtimes\<hostRid>\native\<lib>` and logs `staged ...`. Done.
  - **If `v0.17.2` is absent (current planning-time reality — `gh release view v0.17.2` returns "release not found"):** the `gh release download` step throws `CakeException: gh release download failed for ...`. **This is expected; do NOT downgrade version.txt.** Fall back to the existing v0.17.1 native, which is ABI-identical (design §3/§9):
    ```powershell
    dotnet run --project W:\code\ladybug\tools\csharp_api\cake\LadybugDB.Build.csproj -- --target FetchNatives --engine-version v0.17.1
    ```
    **Expected:** `staged <hostRid> -> ...`. Record in `openQuestions` that `v0.17.2` natives are not yet published and the family currently fetches `v0.17.1` natives under a `0.17.2` package version.
- [ ] **Commit:**
  ```powershell
  git -C W:\code\ladybug\tools\csharp_api add LadybugDB.slnx
  git -C W:\code\ladybug\tools\csharp_api commit -m @'
build(sln): add new library, test, and benchmark projects to solution

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
  ```

---

## TASK 9 — `LadybugDB.Tests.Parity` skeleton (native-gated)

Ports of upstream C-API gtests live here (WS-J). The skeleton ships its own `TestEnvironment` (the existing one in `LadybugDB.Tests` is `internal`) and one trivial non-gated test plus one `SkippableFact` gated on `TestEnvironment.NativeAvailable`, matching the repo pattern.

- [ ] **Create** `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\LadybugDB.Tests.Parity.csproj` (mirrors the existing test csproj — net10.0 only, SkippableFact, references core, copies the native lib):
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
      <ProjectReference Include="..\..\src\LadybugDB\LadybugDB.csproj" />
    </ItemGroup>

    <!-- Copy the native library next to the test output if staged under lib/runtimes/<rid>/native. -->
    <Target Name="PlaceNativeLibrary" AfterTargets="Build">
      <ItemGroup>
        <_LadybugNative Include="$(NativeLibDir)runtimes\$(TargetRid)\native\*.*" />
      </ItemGroup>
      <Copy SourceFiles="@(_LadybugNative)" DestinationFolder="$(OutputPath)" SkipUnchangedFiles="true" Condition="'@(_LadybugNative)' != ''" />
    </Target>

  </Project>
  ```
- [ ] **Create** `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\TestEnvironment.cs` (the parity project's own copy of the native gate; the original is `internal` to `LadybugDB.Tests`):
  ```csharp
  using System;
  using LadybugDB;

  namespace LadybugDB.Tests.Parity;

  /// <summary>Native-availability detection for the parity suite. Tests skip (not fail) when absent,
  /// unless LADYBUG_REQUIRE_NATIVE=1 turns the skip into a hard failure in release CI.</summary>
  internal static class TestEnvironment
  {
      public static readonly bool NativeAvailable = Probe();

      private static bool Probe()
      {
          try
          {
              _ = LadybugVersion.StorageVersion;
              return true;
          }
          catch (DllNotFoundException) { return false; }
          catch (TypeInitializationException) { return false; }
          catch (EntryPointNotFoundException) { return false; }
      }
  }
  ```
- [ ] **Create the failing/skipping skeleton test** `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\SkeletonTests.cs`:
  ```csharp
  using Xunit;

  namespace LadybugDB.Tests.Parity;

  /// <summary>Skeleton so the parity project builds and runs. WS-J adds the ported C-API gtests.</summary>
  public sealed class SkeletonTests
  {
      // Pure-managed sanity check — must NOT be native-gated (proves the harness runs without the engine).
      [Fact]
      public void Harness_IsWired()
      {
          Assert.True(true);
      }

      // Native-gated example following the repo pattern: skips when the engine is unavailable.
      [SkippableFact]
      public void NativeVersion_IsReadable_WhenNativePresent()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "native Ladybug library not available");
          Assert.False(string.IsNullOrEmpty(LadybugDB.LadybugVersion.Version));
      }
  }
  ```
- [ ] **Run / expected PASS:**
  ```powershell
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\LadybugDB.Tests.Parity.csproj -c Debug --nologo
  ```
  **Expected:** the run succeeds; `Harness_IsWired` passes, `NativeVersion_IsReadable_WhenNativePresent` either passes (native staged in Task 8) or is **skipped** (native absent) — never fails. Summary: `Passed!` or `Passed! ... Skipped: 1`.
- [ ] **Commit:**
  ```powershell
  git -C W:\code\ladybug\tools\csharp_api add test/LadybugDB.Tests.Parity
  git -C W:\code\ladybug\tools\csharp_api commit -m @'
test(parity): add native-gated parity test project skeleton

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
  ```

---

## TASK 10 — `LadybugDB.Tests.Extensions` + `LadybugDB.Benchmarks` skeletons; full solution gate

### 10a — Extensions test project (fakes, NOT native-gated)

- [ ] **Create** `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj`:
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
    </ItemGroup>

    <ItemGroup>
      <ProjectReference Include="..\..\src\LadybugDB.Extensions\LadybugDB.Extensions.csproj" />
    </ItemGroup>

  </Project>
  ```
  > No `Xunit.SkippableFact` and no native-copy target: the Extensions layer is tested with fakes (design §8) and must run without the engine.
- [ ] **Create** `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\SkeletonTests.cs`:
  ```csharp
  using Xunit;

  namespace LadybugDB.Tests.Extensions;

  /// <summary>Skeleton so the Extensions test project builds and runs without the native engine.
  /// WS-G adds DI/health/resilience/streaming/export tests using fakes.</summary>
  public sealed class SkeletonTests
  {
      [Fact]
      public void Harness_IsWired()
      {
          Assert.True(true);
      }
  }
  ```

### 10b — Benchmarks project (net10.0 console, not packed)

- [ ] **Create** `W:\code\ladybug\tools\csharp_api\benchmarks\LadybugDB.Benchmarks\LadybugDB.Benchmarks.csproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
      <OutputType>Exe</OutputType>
      <TargetFramework>net10.0</TargetFramework>
      <RootNamespace>LadybugDB.Benchmarks</RootNamespace>
      <IsPackable>false</IsPackable>
      <!-- BenchmarkDotNet wants Release; this skeleton just needs to build and exit cleanly. -->
      <GenerateDocumentationFile>false</GenerateDocumentationFile>
    </PropertyGroup>

    <ItemGroup>
      <PackageReference Include="BenchmarkDotNet" Version="0.14.0" />
    </ItemGroup>

    <ItemGroup>
      <ProjectReference Include="..\..\src\LadybugDB\LadybugDB.csproj" />
    </ItemGroup>

  </Project>
  ```
- [ ] **Create** `W:\code\ladybug\tools\csharp_api\benchmarks\LadybugDB.Benchmarks\Program.cs`:
  ```csharp
  // Skeleton benchmark host. WS-J adds the real BenchmarkDotNet suites and the --ci-gate guard.
  // Returns 0 so the project builds and a no-arg run exits cleanly.
  namespace LadybugDB.Benchmarks;

  internal static class Program
  {
      private static int Main(string[] args)
      {
          System.Console.WriteLine("LadybugDB.Benchmarks skeleton. WS-J adds the BenchmarkDotNet suites.");
          return 0;
      }
  }
  ```

### 10c — Build + test the full solution (the WS-K gate)

- [ ] **Run / expected PASS — build everything across both TFMs:**
  ```powershell
  dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug --nologo
  ```
  **Expected:** `Build succeeded. 0 Error(s)`. Every project from `LadybugDB.slnx` builds: core (net10.0 + ns2.0), Extensions (both TFMs), Arrow (both TFMs), SourceGen (ns2.0), all three test projects (net10.0), Benchmarks (net10.0).
- [ ] **Run / expected PASS — run the non-native test projects (must pass with no engine):**
  ```powershell
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --nologo
  ```
  **Expected:** `Passed! - Failed: 0`.
- [ ] **Commit:**
  ```powershell
  git -C W:\code\ladybug\tools\csharp_api add test/LadybugDB.Tests.Extensions benchmarks/LadybugDB.Benchmarks
  git -C W:\code\ladybug\tools\csharp_api commit -m @'
test(extensions,bench): add Extensions test + Benchmarks skeletons

Extensions tests run with fakes (no native); Benchmarks is a net10.0
console host. Full solution builds across both TFMs.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
  ```

---

## TASK 11 — Cake: pack the new library packages; extend `VerifyPackages`

### 11a — Add paths to `BuildContext`

- [ ] **Edit** `W:\code\ladybug\tools\csharp_api\cake\BuildContext.cs`. In the constructor, after the existing `ManagedProject = ...` assignment (line 53), add:
  ```csharp
          ExtensionsProject = Path.Combine(Root, "src", "LadybugDB.Extensions", "LadybugDB.Extensions.csproj");
          ArrowProject = Path.Combine(Root, "src", "LadybugDB.Arrow", "LadybugDB.Arrow.csproj");
  ```
- [ ] In the property block, after `public string ManagedProject { get; }` (line 71), add:
  ```csharp
      public string ExtensionsProject { get; }
      public string ArrowProject { get; }
  ```

### 11b — Add pack tasks

- [ ] **Edit** `W:\code\ladybug\tools\csharp_api\cake\Tasks\Pipeline.cs`. After `PackManagedTask` (ends line 119), add two analogous tasks. They mirror `PackManagedTask` exactly (same MSBuild `Version`/`Commit` wiring):
  ```csharp
  /// <summary>Packs the LadybugDB.Extensions satellite package (DI/health/resilience/export).</summary>
  [TaskName("PackExtensions")]
  [IsDependentOn(typeof(RestoreTask))]
  public sealed class PackExtensionsTask : FrostingTask<BuildContext>
  {
      public override void Run(BuildContext context)
      {
          var msbuild = new DotNetMSBuildSettings()
              .WithProperty("Version", context.Version)
              .WithProperty("ContinuousIntegrationBuild", "true");

          if (!string.IsNullOrEmpty(context.Commit))
          {
              msbuild.WithProperty("RepositoryCommit", context.Commit);
          }

          context.DotNetPack(context.ExtensionsProject, new DotNetPackSettings
          {
              Configuration = context.BuildConfiguration,
              OutputDirectory = context.ArtifactsDir,
              MSBuildSettings = msbuild,
          });
      }
  }

  /// <summary>Packs the LadybugDB.Arrow satellite package (Apache.Arrow interop).</summary>
  [TaskName("PackArrow")]
  [IsDependentOn(typeof(RestoreTask))]
  public sealed class PackArrowTask : FrostingTask<BuildContext>
  {
      public override void Run(BuildContext context)
      {
          var msbuild = new DotNetMSBuildSettings()
              .WithProperty("Version", context.Version)
              .WithProperty("ContinuousIntegrationBuild", "true");

          if (!string.IsNullOrEmpty(context.Commit))
          {
              msbuild.WithProperty("RepositoryCommit", context.Commit);
          }

          context.DotNetPack(context.ArrowProject, new DotNetPackSettings
          {
              Configuration = context.BuildConfiguration,
              OutputDirectory = context.ArtifactsDir,
              MSBuildSettings = msbuild,
          });
      }
  }
  ```

### 11c — Make `VerifyPackages` depend on the new pack tasks and assert them

- [ ] **Edit** `W:\code\ladybug\tools\csharp_api\cake\Tasks\VerifyPackagesTask.cs`. Add two `[IsDependentOn]` attributes above the class (after the existing three, lines 14–16):
  ```csharp
  [IsDependentOn(typeof(PackExtensionsTask))]
  [IsDependentOn(typeof(PackArrowTask))]
  ```
- [ ] In `Run`, after the existing core `Require(packages, "LadybugDB", ...)` block (ends line 28), add assertions for the new packages and the bundled analyzer. Insert:
  ```csharp
          // The core package must also carry the bundled source-generator analyzer asset.
          Require(packages, "LadybugDB", errors,
              p => RequireFile(p, "analyzers/dotnet/cs/LadybugDB.SourceGen.dll", errors));

          Require(packages, "LadybugDB.Extensions", errors, p =>
          {
              RequireFile(p, "lib/net10.0/LadybugDB.Extensions.dll", errors);
              RequireFile(p, "lib/netstandard2.0/LadybugDB.Extensions.dll", errors);
          });

          Require(packages, "LadybugDB.Arrow", errors, p =>
          {
              RequireFile(p, "lib/net10.0/LadybugDB.Arrow.dll", errors);
              RequireFile(p, "lib/netstandard2.0/LadybugDB.Arrow.dll", errors);
          });
  ```
  > `Require` is idempotent for the same id — calling it twice for `"LadybugDB"` just runs both checks against the same package, which is the intended behavior.
- [ ] **Run / expected PASS — full pack + verify via Cake:**
  ```powershell
  dotnet run --project W:\code\ladybug\tools\csharp_api\cake\LadybugDB.Build.csproj -- --target Pack --configuration Debug
  ```
  > If FetchNatives fails on the missing `v0.17.2` engine (see Task 8), append `--engine-version v0.17.1`. `PackRuntimes`/`PackNativeMeta` need staged natives; the managed/Extensions/Arrow/verify path does not.
  **Expected:** the run reaches `VerifyPackages` and logs `verified N package(s) in .../artifacts`. The artifacts dir contains `LadybugDB.0.17.2.nupkg`, `LadybugDB.Extensions.0.17.2.nupkg`, `LadybugDB.Arrow.0.17.2.nupkg`, the five `LadybugDB.Native.<rid>.0.17.2.nupkg`, and `LadybugDB.Native.0.17.2.nupkg`. No `CakeException: Package validation failed`.
- [ ] **If `VerifyPackages` reports a missing entry**, fix the offending csproj/target (typical cause: analyzer DLL path casing or a TFM-missing pack) and re-run. Do not weaken the assertion.
- [ ] **Commit:**
  ```powershell
  git -C W:\code\ladybug\tools\csharp_api add cake/BuildContext.cs cake/Tasks/Pipeline.cs cake/Tasks/VerifyPackagesTask.cs
  git -C W:\code\ladybug\tools\csharp_api commit -m @'
build(cake): pack and verify Extensions + Arrow packages and bundled analyzer

Adds PackExtensions/PackArrow tasks, BuildContext paths, and extends
VerifyPackages to assert the new packages plus the analyzers/dotnet/cs asset.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
  ```

---

## TASK 12 — CI workflow trigger paths + release doc

- [ ] **Edit** `W:\code\ladybug\tools\csharp_api\.github\workflows\ci.yml`. The `push.paths` and `pull_request.paths` lists currently key on `src/**`, `test/**`, `cake/**`, `nuget/**`, `**/*.props`, `version.txt`, `LadybugDB.slnx`, and the workflow files. `src/**` and `test/**` already cover the new src/test projects, but `benchmarks/**` is new. Add `- 'benchmarks/**'` to **both** the `push.paths` (after line 23 `- '.github/workflows/release.yml'`... insert before it under each block) and `pull_request.paths` lists. Concretely, under `push:` `paths:` add a line:
  ```yaml
      - 'benchmarks/**'
  ```
  and the identical line under `pull_request:` `paths:`.
- [ ] **Edit** `W:\code\ladybug\tools\csharp_api\.github\workflows\release.yml`. Update the one-time-setup comment block (lines 14–19) so the trusted-publishing policy enumerates the **new** package ids. Replace the line:
  ```
  #   - nuget.org trusted publishing policy covering every package id: LadybugDB, LadybugDB.Native, and
  #     LadybugDB.Native.{win-x64, linux-x64, linux-arm64, osx-x64, osx-arm64}
  ```
  with:
  ```
  #   - nuget.org trusted publishing policy covering every package id: LadybugDB, LadybugDB.Extensions,
  #     LadybugDB.Arrow, LadybugDB.Native, and
  #     LadybugDB.Native.{win-x64, linux-x64, linux-arm64, osx-x64, osx-arm64}
  ```
  > No job logic changes: `release.yml` already pushes `artifacts/*.nupkg`, so the new packages publish automatically once the pipeline produces them (Task 11). The comment is the actionable maintainer note.
- [ ] **Run / expected PASS — lint the YAML by parsing it** (no CI runner locally; validate structure):
  ```powershell
  dotnet tool list -g | Out-Null  # noop; YAML check below
  python -c "import yaml,sys; [yaml.safe_load(open(p)) for p in ['W:/code/ladybug/tools/csharp_api/.github/workflows/ci.yml','W:/code/ladybug/tools/csharp_api/.github/workflows/release.yml']]; print('yaml ok')"
  ```
  **Expected:** prints `yaml ok` (both workflows still parse).
- [ ] **Commit:**
  ```powershell
  git -C W:\code\ladybug\tools\csharp_api add .github/workflows/ci.yml .github/workflows/release.yml
  git -C W:\code\ladybug\tools\csharp_api commit -m @'
ci: trigger on benchmarks/** and document new publishable package ids

Adds benchmarks/** to CI path filters; notes LadybugDB.Extensions and
LadybugDB.Arrow must be covered by the nuget.org trusted-publishing policy.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
  ```

---

## TASK 13 — Final WS-K verification sweep

- [ ] **Run — clean full build, both TFMs:**
  ```powershell
  dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug --nologo
  ```
  **Expected:** `Build succeeded. 0 Error(s)`.
- [ ] **Run — ABI guard still green (native pin must not have changed any struct):**
  ```powershell
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~StructLayoutTests" --nologo
  ```
  **Expected:** `Passed!` (the 0.17.2 bump does not alter any `Lbug*` struct layout — verified the header exposes the same version functions; if WS-A adds `LbugQuerySummary`, that is WS-A's ABI test, not K's).
- [ ] **Run — non-native skeleton tests pass:**
  ```powershell
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Extensions\LadybugDB.Tests.Extensions.csproj -c Debug --nologo
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests.Parity\LadybugDB.Tests.Parity.csproj -c Debug --nologo
  ```
  **Expected:** both `Passed!` (parity's native-gated test skips when no engine, passes when staged).
- [ ] **Run — dry pack + verify the whole family:**
  ```powershell
  dotnet run --project W:\code\ladybug\tools\csharp_api\cake\LadybugDB.Build.csproj -- --target Pack --configuration Debug
  ```
  (append `--engine-version v0.17.1` if `v0.17.2` natives are still unpublished). **Expected:** `verified N package(s)`; artifacts include the three managed packages (`LadybugDB`, `LadybugDB.Extensions`, `LadybugDB.Arrow`), five per-RID native packages, and the meta-package.
- [ ] **No commit** (verification only). If everything is green, WS-K is done.

---

## Self-review / done criteria (tied to WS-K "Done =" in the high-level plan §2)

WS-K Done = *"natives at 0.17.2; 3 new projects build empty; family packs."*

- [ ] `version.txt` is exactly `0.17.2`; `BuildContext` derives engine `v0.17.2` (with the documented v0.17.1 native fallback flagged as an open question while the upstream release is unpublished).
- [ ] Cake `FetchNatives` stages a host native for the bumped pin (or the flagged v0.17.1 fallback) — verified by a staged `lib/runtimes/<rid>/native/<lib>`.
- [ ] Three new **library** projects build empty across **both** TFMs: `LadybugDB.Extensions` (`net10.0;netstandard2.0`), `LadybugDB.Arrow` (`net10.0;netstandard2.0`), `LadybugDB.SourceGen` (`netstandard2.0` analyzer).
- [ ] Three new **test/bench** projects build and run: `LadybugDB.Tests.Parity` (native-gated `SkippableFact` + a non-gated test), `LadybugDB.Tests.Extensions` (fakes, not native-gated), `LadybugDB.Benchmarks` (`net10.0` console).
- [ ] `LadybugDB.SourceGen` ships **inside** the core `LadybugDB` package as `analyzers/dotnet/cs/LadybugDB.SourceGen.dll` (no separate NuGet id); verified by `VerifyPackages` + a direct nupkg listing.
- [ ] Library packages carry metadata mirroring `nuget/nuget-package.props` (via `nuget/library-package.props`), single-sourced from `version.txt`.
- [ ] `VerifyPackagesTask` asserts `LadybugDB.Extensions`, `LadybugDB.Arrow` (both TFM `lib/` entries), and the bundled analyzer — and still asserts the existing core + per-RID native + meta packages.
- [ ] All new projects are in `LadybugDB.slnx`; `dotnet build LadybugDB.slnx -c Debug` is green on both TFMs.
- [ ] A dry `Pack` produces and verifies the full family (managed + Extensions + Arrow + per-RID native + meta) with no `CakeException`.
- [ ] Phase‑1 ns2.0 feasibility documented in `docs/parity-2026-06/K-ns20-feasibility.md`: `Microsoft.Extensions.*` abstractions, `Microsoft.Bcl.AsyncInterfaces`, HealthChecks (`.Abstractions` has ns2.0 → no `#if NET` gate), and `Apache.Arrow` 18.0.0 all confirmed with `netstandard2.0` assets; per-type `#if NET` fallback documented for the general case.
- [ ] `StructLayoutTests` remains green (engine bump introduced no ABI change owned by K).
- [ ] CI path filters include `benchmarks/**`; release-workflow maintainer note lists the new publishable ids.
- [ ] No skeleton leaks a non-empty public surface that would collide with WS-E/G/I (markers are `internal`; SourceGen emits nothing).
