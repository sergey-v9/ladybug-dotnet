using System;
using System.Collections.Generic;
using LadybugDB.Extensions;
using Xunit;

namespace LadybugDB.Tests.Extensions;

public sealed class RowAccessorTests
{
    private static RowAccessor Make()
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = 0,
            ["Age"] = 1,
        };
        return new RowAccessor(new object?[] { "Alice", 30L }, index);
    }

    [Fact]
    public void Get_by_index_converts_to_target_type()
    {
        IRowAccessor row = Make();
        Assert.Equal("Alice", row.Get<string>(0));
        Assert.Equal(30, row.Get<int>(1));   // Convert.ChangeType long -> int
    }

    [Fact]
    public void Get_by_name_is_case_insensitive()
    {
        IRowAccessor row = Make();
        Assert.Equal("Alice", row.Get<string>("name"));
        Assert.Equal(30L, row.Get<long>("AGE"));
    }

    [Fact]
    public void Get_unknown_column_throws_keynotfound()
    {
        IRowAccessor row = Make();
        Assert.Throws<KeyNotFoundException>(() => row.Get<string>("missing"));
    }

    [Fact]
    public void Get_null_value_throws_invalidoperation()
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["x"] = 0 };
        IRowAccessor row = new RowAccessor(new object?[] { null }, index);
        Assert.Throws<InvalidOperationException>(() => row.Get<int>(0));
    }

    [Fact]
    public void GetOrDefault_returns_fallback_for_null_or_missing()
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["x"] = 0 };
        IRowAccessor row = new RowAccessor(new object?[] { null }, index);
        Assert.Equal(-1, row.GetOrDefault("x", -1));
        Assert.Equal(7, row.GetOrDefault("absent", 7));
    }
}
