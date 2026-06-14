using System;
using System.Runtime.InteropServices;
using LadybugDB.Interop;
using Xunit;

namespace LadybugDB.Tests;

/// <summary>
/// Pure-managed checks for the global-visibility loader primitive. No native engine required:
/// we assert the platform-correct RTLD flag math and that the helper is a no-op on Windows.
/// These tests are ungated [Fact]s and must pass on every OS.
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
    public void TryGlobalLoad_NullName_ReturnsFalse()
    {
        bool loaded = UnixNativeMethods.TryGlobalLoad(null!, out IntPtr handle);
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

    [Fact]
    public void GlobalLoadFlags_IncludeNowAndGlobal()
    {
        // Guards the ns2.0 resolver contract: whoever wires the ns2.0 path must use these exact flags.
        Assert.True((UnixNativeMethods.GlobalLoadFlags & UnixNativeMethods.RtldNow) == UnixNativeMethods.RtldNow);
        Assert.True((UnixNativeMethods.GlobalLoadFlags & UnixNativeMethods.RtldGlobal) == UnixNativeMethods.RtldGlobal);
    }

    [Fact]
    public void ToNullTerminatedUtf8_EncodesExactBytesWithTerminator()
    {
        // NAT-5: the dlopen path must be exact UTF-8 (not a lossy ANSI narrow) so a bundled path under
        // a base directory with non-ASCII characters is not mangled.
        Assert.Null(UnixNativeMethods.ToNullTerminatedUtf8(null));

        byte[] ascii = UnixNativeMethods.ToNullTerminatedUtf8("liblbug.so")!;
        Assert.Equal(
            new byte[] { 0x6c, 0x69, 0x62, 0x6c, 0x62, 0x75, 0x67, 0x2e, 0x73, 0x6f, 0x00 },
            ascii);

        // "café/x": é is two UTF-8 bytes (0xC3 0xA9), proving no lossy narrowing, plus a NUL terminator.
        byte[] nonAscii = UnixNativeMethods.ToNullTerminatedUtf8("café/x")!;
        byte[] expected = System.Text.Encoding.UTF8.GetBytes("café/x");
        Assert.Equal(expected.Length + 1, nonAscii.Length);
        Assert.Equal((byte)0, nonAscii[nonAscii.Length - 1]);
        Assert.Contains((byte)0xC3, nonAscii);
        Assert.Contains((byte)0xA9, nonAscii);
    }
}
