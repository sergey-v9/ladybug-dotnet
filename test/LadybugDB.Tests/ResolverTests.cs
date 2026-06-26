using System;
using System.IO;
using System.Runtime.InteropServices;
using LadybugDB.Interop;
using Xunit;

namespace LadybugDB.Tests;

/// <summary>
/// Pure-managed checks for the native-library resolver wiring. No native engine is required: we
/// only assert that the loader hook is reachable and safe to invoke repeatedly on a host that may
/// have no native library present (it must not throw — a missing engine surfaces later as a
/// <see cref="DllNotFoundException"/> at the first real P/Invoke, not as a resolver crash).
/// </summary>
public sealed class ResolverTests
{
    [Fact]
    public void EnsureLoaded_IsIdempotentAndNeverThrows()
    {
        // The netstandard2.0 pre-load path and the net7+ resolver registration are both funnelled
        // through Native.EnsureLoaded(); calling it must be a safe no-op when already initialized or
        // when no native library exists on disk.
        Native.EnsureLoaded();
        Native.EnsureLoaded();
        Native.EnsureLoaded();
    }

    [Fact]
    public void GetCandidateNames_AreNonEmptyAndPlatformShaped()
    {
        string[] names = Native.GetCandidateNamesForTest();
        Assert.NotEmpty(names);
        Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));

        // The shipped Unix asset is liblbug.*, while the import name is lbug_shared; the resolver must
        // therefore offer the liblbug soname as a candidate on Linux/macOS (this is the P0 the ns2.0
        // resolver fixes). On Windows the canonical lbug_shared name leads.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Contains("lbug_shared", names);
        }
        else
        {
            Assert.Contains(names, n => n.StartsWith("liblbug", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void GetNativeProbeDirectories_IncludeNuGetRuntimeAssetLayout()
    {
        string[] directories = Native.GetNativeProbeDirectoriesForTest();
        Assert.NotEmpty(directories);
        Assert.All(directories, d => Assert.False(string.IsNullOrWhiteSpace(d)));

        string baseDir = AppContext.BaseDirectory;
        Assert.Contains(baseDir, directories);

        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        };
        string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "osx"
                : "linux";

        string runtimeAssetDirectory = Path.Combine(baseDir, "runtimes", os + "-" + arch, "native");
        Assert.Contains(runtimeAssetDirectory, directories);
    }
}
