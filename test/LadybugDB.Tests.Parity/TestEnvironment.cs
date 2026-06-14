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
