using System.Collections.Generic;
using System.Linq;
using LadybugDB.Extensions;
using LadybugDB.Tests.Extensions.Fakes;
using Xunit;

namespace LadybugDB.Tests.Extensions;

// Guards the contract the public QueryResultExtensions methods delegate to. The QueryResult-typed
// overloads themselves are smoke-tested under native in QueryResultExtensionsNativeTests.
public sealed class QueryResultExtensionsLogicTests
{
    private static FakeResultData Sample() => new(
        new[] { "name", "age" },
        new List<object?[]> { new object?[] { "Alice", 30L } });

    [Fact]
    public void Select_projects_through_row_accessor()
    {
        var names = ResultHelpers.Select(Sample(), r => r.Get<string>("name")).ToList();
        Assert.Equal(new[] { "Alice" }, names);
    }

    [Fact]
    public void Scalar_default_column_is_zero()
    {
        Assert.Equal("Alice", ResultHelpers.Scalar<string>(Sample(), 0));
    }

    [Fact]
    public void ToJson_includes_columns_and_rows()
    {
        string json = ResultHelpers.ToJson(Sample());
        Assert.Contains("\"columns\"", json);
        Assert.Contains("\"rows\"", json);
        Assert.Contains("Alice", json);
    }
}
