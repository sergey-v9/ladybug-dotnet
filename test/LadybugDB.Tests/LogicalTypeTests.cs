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

    [Fact]
    public void LogicalType_ToString_RendersScalarsNestedAndArrays()
    {
        var int64 = LogicalType.CreateForTests(DataTypeId.Int64, child: null, fixedArraySize: null);
        Assert.Equal("INT64", int64.ToString());

        var listOfString = LogicalType.CreateForTests(
            DataTypeId.List,
            child: LogicalType.CreateForTests(DataTypeId.String, null, null),
            fixedArraySize: null);
        Assert.Equal("LIST(STRING)", listOfString.ToString());

        var arrayOfDouble = LogicalType.CreateForTests(
            DataTypeId.Array,
            child: LogicalType.CreateForTests(DataTypeId.Double, null, null),
            fixedArraySize: 3);
        Assert.Equal("ARRAY(DOUBLE, 3)", arrayOfDouble.ToString());
        Assert.Equal(DataTypeId.Array, arrayOfDouble.Id);
        Assert.Equal(3UL, arrayOfDouble.FixedArraySize);
        Assert.Equal(DataTypeId.Double, arrayOfDouble.ChildType!.Id);
    }

    [Fact]
    public void ColumnSchema_HasValueEquality_OverNameAndType()
    {
        var t1 = LogicalType.CreateForTests(DataTypeId.Int64, null, null);
        var t2 = LogicalType.CreateForTests(DataTypeId.Int64, null, null);

        var a = new ColumnSchema("age", t1);
        var b = new ColumnSchema("age", t1);

        Assert.Equal("age", a.Name);
        Assert.Same(t1, a.Type);
        Assert.Equal(a, b);                       // same Name + same LogicalType reference
        Assert.NotEqual(a, new ColumnSchema("name", t1));
        Assert.NotEqual(a, new ColumnSchema("age", t2)); // different LogicalType reference
    }
}
