using System;
using System.IO;
using System.Runtime.InteropServices;

namespace LadybugDB.Interop;

/// <summary>
/// Seeds the engine's extension cache with the ABI-matched <c>fts</c>/<c>vector</c> extensions that
/// ship in the native package, so <c>INSTALL &lt;name&gt;</c> / <c>LOAD EXTENSION &lt;name&gt;</c>
/// resolve our build instead of downloading a version-mismatched one.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists: a main-tracking dev native (engine <c>0.18.0-dev</c>) is built from a source
/// commit for which upstream publishes <b>no</b> extensions — only released tags get extension
/// builds. The engine's <c>INSTALL fts</c> therefore downloads the last published set (<c>0.17.0</c>),
/// whose ABI does not match (e.g. an undefined <c>Catalog::createIndex</c>), and <c>LOAD</c> crashes.
/// The native package consequently ships <c>fts</c>/<c>vector</c> built from the SAME engine commit,
/// flat next to the engine library as <c>runtimes/&lt;rid&gt;/native/lib&lt;name&gt;.lbug_extension</c> plus
/// a <c>lbug_extension_abi_version.txt</c> marker (flat so NuGet reliably copies them to consumer
/// output), and this seeder copies them into the per-name cache layout the engine reads.
/// </para>
/// <para>
/// The engine resolves an official extension from
/// <c>{home}/.lbdb/extension/{LBUG_EXTENSION_VERSION}/{os}_{arch}/{name}/lib{name}.lbug_extension</c>
/// (<c>home</c> = <c>%USERPROFILE%</c> on Windows, <c>$HOME</c> elsewhere), and <c>INSTALL</c> skips the
/// download when that file already exists. Pre-seeding it therefore makes both the binding's helpers
/// <i>and</i> raw-Cypher <c>INSTALL/LOAD</c> (what consumers issue directly) pick up the matched build,
/// with no per-connection <c>SET</c> and no network. Best-effort: any failure leaves the engine's normal
/// download path intact.
/// </para>
/// </remarks>
internal static class ExtensionStaging
{
    // The only extensions we build-and-ship from source (Graphiti's search path needs both).
    private static readonly string[] ShippedExtensions = { "fts", "vector" };

    // Flat marker file (sits beside the engine lib) holding the engine's LBUG_EXTENSION_VERSION.
    private const string AbiMarkerFileName = "lbug_extension_abi_version.txt";

    /// <summary>
    /// Copies any bundled, ABI-matched extensions into the engine's cache. Never throws: a missing
    /// bundle (e.g. a release-track package that ships no extensions), a read-only home directory, or
    /// any I/O error simply leaves the engine to resolve extensions the usual way.
    /// </summary>
    internal static void TrySeedBundledExtensions()
    {
        try
        {
            SeedBundledExtensions();
        }
        catch
        {
            // Best-effort only — see remarks. A genuine load failure still surfaces at LOAD time.
        }
    }

    private static void SeedBundledExtensions()
    {
        string home = UserHomeDirectory();
        if (string.IsNullOrEmpty(home))
        {
            return;
        }

        if (!TryLocateBundle(out string bundleDir, out string abiVersion))
        {
            return;
        }

        string platform = ExtensionPlatform();
        string cacheRoot = Path.Combine(home, ".lbdb", "extension", abiVersion, platform);

        foreach (string name in ShippedExtensions)
        {
            string fileName = "lib" + name + ".lbug_extension";
            string source = Path.Combine(bundleDir, fileName);
            if (!File.Exists(source))
            {
                continue;
            }

            string destinationDir = Path.Combine(cacheRoot, name);
            string destination = Path.Combine(destinationDir, fileName);

            // Overwrite a differing cache entry: a previously downloaded (ABI-mismatched) extension
            // must be replaced by our matched build. Skip when already identical to avoid churn.
            if (IsSameFile(source, destination))
            {
                continue;
            }

            Directory.CreateDirectory(destinationDir);
            File.Copy(source, destination, overwrite: true);
        }
    }

    /// <summary>
    /// Finds the bundled extensions among the native probe directories by locating the flat ABI marker
    /// (the engine's compile-time <c>LBUG_EXTENSION_VERSION</c>). Returns the first probe directory that
    /// carries it — covering both the package layout (<c>runtimes/&lt;rid&gt;/native</c>) and the
    /// output-flat layout the test/consumer output uses.
    /// </summary>
    private static bool TryLocateBundle(out string bundleDir, out string abiVersion)
    {
        foreach (string probe in Native.GetNativeProbeDirectories())
        {
            string marker = Path.Combine(probe, AbiMarkerFileName);
            if (!File.Exists(marker))
            {
                continue;
            }

            string version = File.ReadAllText(marker).Trim();
            if (version.Length != 0)
            {
                bundleDir = probe;
                abiVersion = version;
                return true;
            }
        }

        bundleDir = string.Empty;
        abiVersion = string.Empty;
        return false;
    }

    // Mirrors the engine's ClientContext::getUserHomeDir(): USERPROFILE on Windows, HOME elsewhere.
    private static string UserHomeDirectory() =>
        (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Environment.GetEnvironmentVariable("USERPROFILE")
            : Environment.GetEnvironmentVariable("HOME")) ?? string.Empty;

    // Mirrors the engine's extension::getPlatform() = getOS() + "_" + getArch(). We build the natives
    // with the modern libstdc++ ABI, so Linux is "linux" (never "linux_old").
    private static string ExtensionPlatform()
    {
        string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
            : "linux";

        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "amd64"
        };

        return os + "_" + arch;
    }

    private static bool IsSameFile(string source, string destination)
    {
        if (!File.Exists(destination))
        {
            return false;
        }

        var src = new FileInfo(source);
        var dst = new FileInfo(destination);

        // Length is the reliable discriminator between a source-built and a downloaded extension; the
        // timestamp guard keeps a fresh copy from being redone every process start once seeded.
        return src.Length == dst.Length && dst.LastWriteTimeUtc >= src.LastWriteTimeUtc;
    }
}
