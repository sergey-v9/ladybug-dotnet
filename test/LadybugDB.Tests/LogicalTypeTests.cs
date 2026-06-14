using LadybugDB;
using Xunit;

namespace LadybugDB.Tests;

/// <summary>
/// Managed-only value-semantics and rendering tests for the WS-B result-surface types. These do
/// not touch the native library and therefore must NOT be gated on TestEnvironment.NativeAvailable.
/// </summary>
public sealed class LogicalTypeTests
{
    [Fact]
    public void QuerySummary_HasValueEquality_AndCarriesTimings()
    {
        var a = new QuerySummary(1.5, 2.5);
        var b = new QuerySummary(1.5, 2.5);

        Assert.Equal(1.5, a.CompilingTimeMs);
        Assert.Equal(2.5, a.ExecutionTimeMs);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new QuerySummary(1.5, 9.9));
    }
}
