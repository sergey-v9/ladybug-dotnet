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
/// <remarks>
/// Compiled on <b>both</b> target frameworks (net10.0 and netstandard2.0). The net7+ resolver in
/// <c>Native.cs</c> prefers this over <c>NativeLibrary</c> on
/// Unix; the netstandard2.0 pre-load path (which has neither <c>NativeLibrary</c> nor a module
/// initializer) calls <see cref="TryGlobalLoad"/> directly to seed the global symbol scope before
/// the runtime resolves the first <c>DllImport</c>.
/// </remarks>
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

    // libdl entry points. "libdl.so.2" is the modern glibc soname; the bare "libdl" string is also
    // tried because on musl (Alpine) and macOS dl* live in libc/libSystem and a bare "dl" resolves
    // there. Both DllImports are declared with the same EntryPoint so a single managed name maps to
    // whichever shared object provides dlopen on the host.
    [DllImport("libdl.so.2", EntryPoint = "dlopen", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern IntPtr DlOpenGlibc([MarshalAs(UnmanagedType.LPStr)] string fileName, int flags);

    [DllImport("libdl", EntryPoint = "dlopen", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern IntPtr DlOpenLegacy([MarshalAs(UnmanagedType.LPStr)] string fileName, int flags);

    /// <summary>
    /// Attempts to <c>dlopen(fileName, RTLD_NOW | RTLD_GLOBAL)</c>. Returns <see langword="false"/> on
    /// Windows, on a <see langword="null"/> <paramref name="fileName"/>, on any
    /// <see cref="DllNotFoundException"/>/<see cref="EntryPointNotFoundException"/> resolving libdl,
    /// or when <c>dlopen</c> returns <see cref="IntPtr.Zero"/>. Never throws for a missing soname.
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
