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
