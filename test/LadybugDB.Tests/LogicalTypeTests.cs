using LadybugDB;
using Xunit;

namespace LadybugDB.Tests;

/// <summary>
/// Managed-only value-semantics tests for the WS-B result-surface types that need no native library
/// and therefore must NOT be gated on TestEnvironment.NativeAvailable. (The LogicalType.ToString
/// formatting and the ColumnSchema reference-equality-over-LogicalType assertion moved to the
/// native-gated ResultSurfaceTests once the managed-only LogicalType test factory was removed.)
/// </summary>
public sealed class LogicalTypeTests
{
    [Fact]
    public void QuerySummary_CarriesTimings()
    {
        var a = new QuerySummary(1.5, 2.5);

        Assert.Equal(1.5, a.CompilingTimeMs);
        Assert.Equal(2.5, a.ExecutionTimeMs);
    }
}
