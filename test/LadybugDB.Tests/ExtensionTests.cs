using System;
using System.Runtime.InteropServices;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests;

/// <summary>
/// Tests for the engine-extension helpers <see cref="Connection.InstallExtension"/> /
/// <see cref="Connection.LoadExtension"/>. The argument-validation paths are native-gated only
/// because constructing a <see cref="Connection"/> needs the native engine; the real install/load
/// round-trip is native-gated and offline-tolerant (it Skips on a download/network failure but
/// FAILS loudly on a genuine symbol-resolution error — that path also validates the RTLD_GLOBAL
/// loader fix end-to-end on Unix).
/// </summary>
public sealed class ExtensionTests
{
    [SkippableFact]
    public void InstallExtension_NullName_ThrowsArgumentNull()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);
            Assert.Throws<ArgumentNullException>(() => conn.InstallExtension(null!));
            Assert.Throws<ArgumentNullException>(() => conn.LoadExtension(null!));
        }
        finally { TestEnvironment.TryDelete(dbPath); }
    }

    [SkippableFact]
    public void InstallExtension_EmptyOrWhitespace_ThrowsArgument()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);
            Assert.Throws<ArgumentException>(() => conn.InstallExtension("   "));
            Assert.Throws<ArgumentException>(() => conn.InstallExtension(""));
            Assert.Throws<ArgumentException>(() => conn.LoadExtension(""));
            Assert.Throws<ArgumentException>(() => conn.LoadExtension("\t"));
        }
        finally { TestEnvironment.TryDelete(dbPath); }
    }

    [SkippableFact]
    public void Extension_NameWithInjectionCharacters_ThrowsArgument()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            // A bare identifier only: anything that could chain a second statement or otherwise
            // escape the single INSTALL/LOAD statement must be rejected.
            Assert.Throws<ArgumentException>(() => conn.InstallExtension("json; DROP"));
            Assert.Throws<ArgumentException>(() => conn.InstallExtension("json extra"));
            Assert.Throws<ArgumentException>(() => conn.InstallExtension("'json'"));
            Assert.Throws<ArgumentException>(() => conn.LoadExtension("json-ext"));
            Assert.Throws<ArgumentException>(() => conn.LoadExtension("../json"));
        }
        finally { TestEnvironment.TryDelete(dbPath); }
    }

    [SkippableFact]
    public void InstallAndLoad_Json_ResolvesExtensionFunction()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            // INSTALL reaches the extension repository over the network; LOAD EXTENSION is the step
            // that dlopen()s the extension .so and triggers symbol resolution against liblbug.
            try
            {
                conn.InstallExtension("json");

                // If LOAD fails with an "undefined symbol" / "cannot resolve" message, that is the
                // RTLD_GLOBAL bug this workstream's loader fix addresses — do NOT skip on that; the
                // catch below re-checks and re-throws for symbol-resolution failures.
                conn.LoadExtension("json");
            }
            catch (LadybugQueryException ex)
            {
                if (IsSymbolResolutionFailure(ex))
                {
                    // The loader regression we guard against: liblbug not loaded with RTLD_GLOBAL.
                    throw;
                }

                throw new Xunit.SkipException(
                    "Extension install/load unavailable (likely offline): " + ex.Message);
            }

            // Prove a function defined *in the extension* actually works end-to-end.
            using QueryResult result = conn.Query("RETURN cast('\"lbug\"' AS JSON) AS j");
            Assert.True(result.IsSuccess);
            Assert.Equal(1UL, result.ColumnCount);
        }
        finally { TestEnvironment.TryDelete(dbPath); }
    }

    [SkippableFact]
    public void LoadExtension_DoesNotFailWithUndefinedSymbol()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return; // Symbol-visibility scoping is a Unix-only concern.
        }

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);
            try
            {
                conn.InstallExtension("json");
                conn.LoadExtension("json");
            }
            catch (LadybugQueryException ex)
            {
                // Offline is acceptable; an undefined-symbol failure is the regression we guard against.
                Assert.False(IsSymbolResolutionFailure(ex),
                    "LOAD EXTENSION failed resolving engine symbols — liblbug was not loaded with RTLD_GLOBAL: " + ex.Message);
                Skip.If(true, "Extension install/load unavailable (likely offline): " + ex.Message);
            }
        }
        finally { TestEnvironment.TryDelete(dbPath); }
    }

    private static bool IsSymbolResolutionFailure(LadybugQueryException ex)
    {
        string m = ex.Message;
        return m.Contains("undefined symbol", StringComparison.OrdinalIgnoreCase)
            || m.Contains("cannot resolve", StringComparison.OrdinalIgnoreCase)
            || m.Contains("symbol not found", StringComparison.OrdinalIgnoreCase);
    }
}
