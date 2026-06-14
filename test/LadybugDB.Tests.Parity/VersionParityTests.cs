using LadybugDB;
using Xunit;

namespace LadybugDB.Tests.Parity;

/// <summary>Port of upstream <c>test/c_api/version_test.cpp</c> (GetVersion, GetStorageVersion).</summary>
public sealed class VersionParityTests
{
    [SkippableFact]
    public void GetVersion_is_nonempty_and_stable()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string v1 = LadybugVersion.Version;
        string v2 = LadybugVersion.Version;
        Assert.False(string.IsNullOrWhiteSpace(v1));
        Assert.Equal(v1, v2);
    }

    [SkippableFact]
    public void GetStorageVersion_is_positive()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        Assert.True(LadybugVersion.StorageVersion > 0);
    }
}
