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
    //
    // NAT-5: the path is passed as a NUL-terminated UTF-8 byte[] rather than CharSet.Ansi/LPStr. dlopen
    // treats the path as raw bytes, but the LPStr/ANSI marshaller does a lossy narrow conversion on
    // Unix, so a bundled path under a base directory containing non-ASCII characters would be mangled
    // and the library would not be found. Encoding to UTF-8 ourselves makes the byte path exact and is
    // identical on net10.0 and netstandard2.0.
    [DllImport("libdl.so.2", EntryPoint = "dlopen", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr DlOpenGlibc(byte[] fileName, int flags);

    [DllImport("libdl", EntryPoint = "dlopen", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr DlOpenLegacy(byte[] fileName, int flags);

    /// <summary>
    /// Encodes <paramref name="path"/> to a NUL-terminated UTF-8 byte buffer for <c>dlopen</c>. Returns
    /// <see langword="null"/> for a <see langword="null"/> input. The trailing <c>0</c> terminates the
    /// C string; the encoding is locale-independent so non-ASCII paths survive intact.
    /// </summary>
    internal static byte[]? ToNullTerminatedUtf8(string? path)
    {
        if (path is null)
        {
            return null;
        }

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(path);
        var buffer = new byte[bytes.Length + 1];
        Array.Copy(bytes, buffer, bytes.Length);
        buffer[bytes.Length] = 0;
        return buffer;
    }

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

        byte[] path = ToNullTerminatedUtf8(fileName)!;
        int flags = GlobalLoadFlags;
        try
        {
            handle = DlOpenGlibc(path, flags);
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
                handle = DlOpenLegacy(path, flags);
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
