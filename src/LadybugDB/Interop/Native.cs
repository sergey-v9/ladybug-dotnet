using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
#if NET7_0_OR_GREATER
using System.Reflection;
using System.Runtime.CompilerServices;
#endif

namespace LadybugDB.Interop;

/// <summary>
/// Low-level P/Invoke surface for the Ladybug C API. The platform-specific declarations live in
/// <c>Native.LibraryImport.cs</c> (net7.0+) and <c>Native.DllImport.cs</c> (netstandard2.0); this
/// file holds shared constants, the native library resolver, and marshaling helpers.
/// </summary>
internal static partial class Native
{
    /// <summary>
    /// Canonical import name. On Windows the native file is <c>lbug_shared.dll</c>; on Linux/macOS the
    /// resolver (net7.0+) remaps to <c>liblbug.so</c> / <c>liblbug.dylib</c>.
    /// </summary>
    internal const string LibraryName = "lbug_shared";

    // Guards the one-time native-load setup. On net7+ this registers the custom resolver; on
    // netstandard2.0 (no SetDllImportResolver, no [ModuleInitializer]) it pre-dlopen()s the shipped
    // liblbug.* with global symbol visibility so the bare DllImport("lbug_shared") resolves and so a
    // later LOAD EXTENSION finds the engine's symbols.
    private static int _loadInitialized;

    /// <summary>
    /// Ensures the native engine has been wired for loading: registers the custom resolver on net7+
    /// and pre-loads <c>liblbug.*</c> with <c>RTLD_NOW | RTLD_GLOBAL</c> on netstandard2.0/Unix. Safe
    /// to call repeatedly (idempotent) and never throws — a genuinely missing engine surfaces as a
    /// <see cref="DllNotFoundException"/> at the first real P/Invoke, not here.
    /// </summary>
    internal static void EnsureLoaded()
    {
        if (System.Threading.Interlocked.Exchange(ref _loadInitialized, 1) != 0)
        {
            return;
        }

#if NET7_0_OR_GREATER
        NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, Resolve);
#else
        // netstandard2.0 has no resolver hook. Promote liblbug.* into the global symbol scope so the
        // runtime's DllImport("lbug_shared") lookup is satisfied by an already-loaded library and so
        // dynamically loaded extension .so files can resolve the engine's exported symbols. Windows is
        // skipped (TryGlobalLoad is a no-op there; the PE loader finds lbug_shared.dll itself).
        PreloadUnixGlobal();
#endif
    }

    // A static constructor runs before the first access to any Native static member — including the
    // first DllImport call — so it is the netstandard2.0 equivalent of net7+'s [ModuleInitializer].
    // On net7+ the [ModuleInitializer] below also covers the resolver; both route through
    // EnsureLoaded(), which is idempotent.
    static Native() => EnsureLoaded();

#if NET7_0_OR_GREATER
    // The module initializer guarantees the resolver is registered before any P/Invoke into the
    // native library; CA2255's "app code only" guidance does not apply to this scenario.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void RegisterResolver() => EnsureLoaded();

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
                // Prefer bundled/package assets loaded by absolute path so RTLD_GLOBAL applies to
                // exactly that file. A bare-soname dlopen would not find non-system-installed
                // libraries, and package consumers often receive assets under runtimes/<rid>/native.
                foreach (string probeDirectory in GetNativeProbeDirectories())
                {
                    string full = Path.Combine(probeDirectory, candidate);
                    if (File.Exists(full) && UnixNativeMethods.TryGlobalLoad(full, out IntPtr bundled))
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

        foreach (string candidate in GetCandidateNames())
        {
            if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out IntPtr handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }
#else
    private static void PreloadUnixGlobal()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        foreach (string candidate in GetCandidateNames())
        {
            foreach (string probeDirectory in GetNativeProbeDirectories())
            {
                string full = Path.Combine(probeDirectory, candidate);
                if (File.Exists(full) && UnixNativeMethods.TryGlobalLoad(full, out _))
                {
                    return;
                }
            }

            if (UnixNativeMethods.TryGlobalLoad(candidate, out _))
            {
                return;
            }
        }
    }
#endif

    // The app/output directory where per-RID native assets are staged. AppContext.BaseDirectory is
    // single-file- and AOT-safe (unlike Assembly.Location, which is empty in those scenarios) and is
    // available on both target frameworks.
    private static string? TryGetBaseDirectory()
    {
        try
        {
            string baseDir = AppContext.BaseDirectory;
            return string.IsNullOrEmpty(baseDir) ? null : baseDir;
        }
        catch
        {
            return null;
        }
    }

    internal static string[] GetNativeProbeDirectories()
    {
        string? baseDir = TryGetBaseDirectory();
        if (string.IsNullOrEmpty(baseDir))
        {
            return Array.Empty<string>();
        }

        var directories = new List<string> { baseDir! };
        foreach (string rid in GetRuntimeAssetRids())
        {
            directories.Add(Path.Combine(baseDir!, "runtimes", rid, "native"));
        }

        return directories.ToArray();
    }

    private static string[] GetRuntimeAssetRids()
    {
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new[] { "win-" + arch };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new[] { "osx-" + arch };
        }

        return new[] { "linux-" + arch };
    }

    /// <summary>
    /// Candidate native-library names to probe, in priority order. The shipped Unix asset is
    /// <c>liblbug.*</c> even though the import name is <c>lbug_shared</c>, so the <c>liblbug</c> sonames
    /// lead on Linux/macOS. Available on both target frameworks.
    /// </summary>
    internal static string[] GetCandidateNames()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new[] { "lbug_shared", "lbug_shared.dll", "liblbug" };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new[] { "liblbug.dylib", "liblbug", "lbug_shared" };
        }

        return new[] { "liblbug.so", "liblbug", "lbug_shared" };
    }

    /// <summary>Convenience wrapper for <c>lbug_get_version</c> (owns and frees the returned string).</summary>
    internal static string? GetVersion() => TakeString(GetVersionPtr());

    /// <summary>Convenience wrapper for <c>lbug_get_last_error</c> (consumes and frees the message).</summary>
    internal static string? GetLastError() => TakeString(GetLastErrorPtr());

    /// <summary>Decodes a NUL-terminated UTF-8 C string into a managed string without taking ownership.</summary>
    internal static string? PtrToStringUtf8(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
        {
            return null;
        }

#if NET7_0_OR_GREATER
        return Marshal.PtrToStringUTF8(ptr);
#else
        int length = 0;
        while (Marshal.ReadByte(ptr, length) != 0)
        {
            length++;
        }

        if (length == 0)
        {
            return string.Empty;
        }

        byte[] bytes = new byte[length];
        Marshal.Copy(ptr, bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
#endif
    }

    /// <summary>Copies an owned C string into managed memory and frees it via <c>lbug_destroy_string</c>.</summary>
    internal static string? TakeString(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return PtrToStringUtf8(ptr);
        }
        finally
        {
            DestroyString(ptr);
        }
    }

#if NETSTANDARD2_0
    /// <summary>Encodes a managed string as a NUL-terminated UTF-8 byte buffer for marshaling on netstandard2.0.</summary>
    internal static byte[] ToUtf8(string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        byte[] bytes = new byte[byteCount + 1];
        Encoding.UTF8.GetBytes(value, 0, value.Length, bytes, 0);
        bytes[byteCount] = 0;
        return bytes;
    }
#endif
}
