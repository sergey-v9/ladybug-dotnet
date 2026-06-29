#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build the engine native (lbug_shared) from the commit pinned in upstream-engine.pin and stage it
    into the binding's runtime folder for the host RID — i.e. the main-tracking "ride upstream" native.

.DESCRIPTION
    Reads upstream-engine.pin (repo + commit), checks that commit out of the engine repo into an isolated
    git worktree (so your normal engine checkout is untouched), builds the shared C-API library with the
    same flags the release uses, locates the produced shared object regardless of its platform name, and
    copies it to lib/runtimes/<host-rid>/native/<canonical>. Then optionally runs the C# suite against it.

    Requires CMake + Ninja + a C++ toolchain (MSVC on Windows; gcc/clang on Linux/macOS). On Windows it
    reuses the vcvars bootstrap from build-native-and-test.ps1 if cl/cmake/ninja are not already on PATH.

.PARAMETER EngineRepo
    Path to a local LadybugDB/ladybug checkout. Defaults to the parent monorepo (../../ from this binding).

.PARAMETER Configuration
    CMake build type: Release (default), RelWithDebInfo, Debug.

.PARAMETER SkipTests
    Build + stage only; do not run dotnet test.
#>
[CmdletBinding()]
param(
    [string]$EngineRepo,
    [ValidateSet('Release', 'RelWithDebInfo', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
function Write-Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }

$csharpDir = Split-Path $PSScriptRoot -Parent

# --- read the pin -------------------------------------------------------------------------------
$pinFile = Join-Path $csharpDir 'upstream-engine.pin'
if (-not (Test-Path $pinFile)) { throw "upstream-engine.pin not found at $pinFile" }
$pin = @{}
foreach ($line in Get-Content $pinFile) {
    if ($line -match '^\s*([a-z_]+)\s*=\s*(.+?)\s*$') { $pin[$matches[1]] = $matches[2] }
}
$commit = $pin['commit']
if (-not $commit) { throw "upstream-engine.pin has no commit= line" }
Write-Step "Pinned engine commit: $commit  ($($pin['describe']))"

# --- locate the engine repo ---------------------------------------------------------------------
if (-not $EngineRepo) { $EngineRepo = (Resolve-Path (Join-Path $csharpDir '..\..')).Path }
if (-not (Test-Path (Join-Path $EngineRepo 'src/include/c_api/lbug.h'))) {
    throw "EngineRepo '$EngineRepo' does not look like LadybugDB/ladybug (no src/include/c_api/lbug.h). Pass -EngineRepo <path>."
}

# --- isolated worktree at the pinned commit (does not disturb your engine checkout) -------------
& git -C $EngineRepo cat-file -e "$commit^{commit}" 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Step "Fetching $commit from origin"
    & git -C $EngineRepo fetch origin $commit --depth 1 2>$null
    if ($LASTEXITCODE -ne 0) { & git -C $EngineRepo fetch origin --tags }
}
$work = Join-Path $EngineRepo (".worktrees/pin-" + $commit.Substring(0, 9))
if (-not (Test-Path $work)) {
    Write-Step "Adding worktree at $work"
    & git -C $EngineRepo worktree add --detach $work $commit
    if ($LASTEXITCODE -ne 0) { throw "git worktree add failed for $commit" }
}
& git -C $work submodule update --init --recursive 2>$null

# --- toolchain (Windows MSVC/cmake/ninja bootstrap; Linux/macOS expect them on PATH) ------------
if ($IsWindows -and -not (Get-Command cl -ErrorAction SilentlyContinue)) {
    Write-Step 'Importing MSVC x64 environment (vcvars64.bat)'
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $vsPath = & $vswhere -latest -prerelease -products * -property installationPath | Select-Object -First 1
        $vcvars = Join-Path $vsPath 'VC\Auxiliary\Build\vcvars64.bat'
        if (Test-Path $vcvars) {
            cmd /c "`"$vcvars`" && set" | ForEach-Object { if ($_ -match '^([^=]+)=(.*)$') { Set-Item -Path ("Env:\" + $matches[1]) -Value $matches[2] } }
        }
    }
}
foreach ($t in 'cmake', 'ninja') {
    if (-not (Get-Command $t -ErrorAction SilentlyContinue)) {
        throw "$t not found on PATH. Install it (Linux: apt install cmake ninja-build; macOS: brew install cmake ninja; Windows: pip install --user cmake ninja)."
    }
}

# --- build the shared C-API library + the fts/vector extensions ---------------------------------
$buildDir = Join-Path $work ("build/" + $Configuration.ToLowerInvariant())
Write-Step "Configuring + building lbug_shared + fts/vector extensions ($Configuration)"
$cfgArgs = @(
    '-B', $buildDir, '-G', 'Ninja', "-DCMAKE_BUILD_TYPE=$Configuration",
    '-DBUILD_SHELL=OFF', '-DBUILD_SINGLE_FILE_HEADER=OFF', '-DBUILD_TESTS=OFF',
    '-DBUILD_EXTENSIONS=fts;vector', '-DCMAKE_POLICY_VERSION_MINIMUM=3.5'
)
# Windows extensions link against the static `lbug` target (no dynamic lookup), so keep it (default ON);
# Linux/macOS extensions resolve engine symbols at runtime, so drop the static lib for speed.
if (-not $IsWindows) { $cfgArgs += '-DBUILD_STATIC_LBUG=OFF' }
$cfgArgs += $work
& cmake @cfgArgs
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed" }
& cmake --build $buildDir --target lbug_shared lbug_fts_extension lbug_vector_extension
if ($LASTEXITCODE -ne 0) { throw "cmake build failed" }

# --- locate the produced shared object (name varies by platform) + stage it ---------------------
$hostRid = ($(if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' })) + '-' +
           ($(if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }))
$canonical = if ($IsWindows) { 'lbug_shared.dll' } elseif ($IsMacOS) { 'liblbug.dylib' } else { 'liblbug.so' }
$ext = if ($IsWindows) { '.dll' } elseif ($IsMacOS) { '.dylib' } else { '.so' }

$built = Get-ChildItem -Path (Join-Path $buildDir 'src') -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match 'lbug' -and $_.Extension -eq $ext -and $_.Name -notmatch '\.lib$' } |
    Sort-Object Length -Descending | Select-Object -First 1
if (-not $built) { throw "Could not find a built *lbug*$ext under $buildDir/src" }

$dest = Join-Path $csharpDir "lib/runtimes/$hostRid/native/$canonical"
New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
Copy-Item $built.FullName -Destination $dest -Force
Write-Host "    staged $($built.Name) -> $dest ($([math]::Round($built.Length/1MB,1)) MB) [rid=$hostRid]" -ForegroundColor Green

# --- stage the ABI-matched fts/vector extensions flat next to the native ------------------------
# These are built from the same engine commit, so their ABI matches (the published 0.17.0 extensions
# do not — they crash a 0.18.0-dev engine with an undefined Catalog::createIndex). Flat layout mirrors
# CI; the binding's pre-seed (src/LadybugDB/Interop/ExtensionStaging.cs) copies these into the engine's
# extension cache. The native dir already exists from the shared-lib stage above.
$nativeDir = Split-Path $dest -Parent
foreach ($name in 'fts', 'vector') {
    $extBuilt = Get-ChildItem -Path (Join-Path $work "extension/$name/build") -Recurse -File `
        -Filter "lib$name.lbug_extension" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $extBuilt) { throw "Could not find lib$name.lbug_extension under $work/extension/$name/build" }
    $extDest = Join-Path $nativeDir "lib$name.lbug_extension"
    Copy-Item $extBuilt.FullName -Destination $extDest -Force
    Write-Host "    staged $($extBuilt.Name) -> $extDest" -ForegroundColor Green
}
# The engine resolves INSTALL/LOAD <name> under .lbdb/extension/<LBUG_EXTENSION_VERSION>/...; record
# that version so the pre-seed targets the right cache dir. It is a compile-time engine constant
# (NOT engine_version) — re-read it on each pin bump.
$cmakeText = Get-Content (Join-Path $work 'CMakeLists.txt') -Raw
$abi = [regex]::Match($cmakeText, 'LBUG_EXTENSION_VERSION="([^"]+)"').Groups[1].Value
if (-not $abi) { throw "Could not read LBUG_EXTENSION_VERSION from $work/CMakeLists.txt" }
Set-Content -Path (Join-Path $nativeDir 'lbug_extension_abi_version.txt') -Value $abi -NoNewline
Write-Host "    extension ABI version: $abi" -ForegroundColor Green

# --- run the suite against the source-built native ----------------------------------------------
if (-not $SkipTests) {
    Write-Step 'Running dotnet test against the engine@pin native'
    $env:LADYBUG_REQUIRE_NATIVE = '1'
    & dotnet test (Join-Path $csharpDir 'LadybugDB.slnx') -c $Configuration -v minimal
}
Write-Step 'Done. (Advance the pin and push fork/dev to ride a newer upstream main — see docs/upstream-sync.md.)'
