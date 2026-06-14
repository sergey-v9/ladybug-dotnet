using System;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests.Mapping;

public sealed class LadybugRowAttributeTests
{
    [Fact]
    public void Attribute_targets_class_and_struct_only_and_is_sealed()
    {
        Type t = typeof(LadybugRowAttribute);
        Assert.True(t.IsSealed);
        Assert.True(typeof(Attribute).IsAssignableFrom(t));

        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(t, typeof(AttributeUsageAttribute))!;
        Assert.Equal(AttributeTargets.Class | AttributeTargets.Struct, usage.ValidOn);
    }
}
