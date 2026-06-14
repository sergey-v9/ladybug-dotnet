using LadybugDB;
using Xunit;

namespace LadybugDB.Tests;

/// <summary>
/// Pure-managed unit tests for the <see cref="Union"/> tagged-value record. The struct shape is
/// managed logic and does not require the native engine, so these are plain facts.
/// </summary>
public sealed class UnionValueTests
{
    [Fact]
    public void Union_exposes_tag_and_value()
    {
        var u = new Union("amount", 42L);
        Assert.Equal("amount", u.Tag);
        Assert.Equal(42L, u.Value);
    }

    [Fact]
    public void Union_equality_is_structural()
    {
        Assert.Equal(new Union("a", 1L), new Union("a", 1L));
        Assert.NotEqual(new Union("a", 1L), new Union("b", 1L));
    }
}
