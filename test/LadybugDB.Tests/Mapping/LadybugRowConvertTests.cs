using System;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests.Mapping;

/// <summary>
/// Unit tests for <see cref="LadybugRowConvert.To{T}"/> — the conversion shim generated row mappers
/// call. These run without the native engine: they feed the shim the CLR result types
/// <see cref="Value.GetValue"/> actually produces (INT64 -> long, DOUBLE -> double, UUID -> Guid,
/// DATE -> DateOnly, DECIMAL -> decimal) and assert the widened/converted target.
/// </summary>
public sealed class LadybugRowConvertTests
{
    private enum Status : long
    {
        Inactive = 0,
        Active = 1,
        Archived = 2,
    }

    [Fact]
    public void Null_cell_returns_default()
    {
        Assert.Equal(0, LadybugRowConvert.To<int>(null));
        Assert.Null(LadybugRowConvert.To<int?>(null));
        Assert.Null(LadybugRowConvert.To<string>(null));
        Assert.Equal(default(Status), LadybugRowConvert.To<Status>(null));
    }

    [Fact]
    public void Enum_member_over_int64_column()
    {
        // The engine returns an INT64 column as a boxed long; the member is an enum.
        Assert.Equal(Status.Active, LadybugRowConvert.To<Status>(1L));
        Assert.Equal(Status.Archived, LadybugRowConvert.To<Status>(2L));
    }

    [Fact]
    public void Nullable_enum_over_int64_column()
    {
        Assert.Equal(Status.Active, LadybugRowConvert.To<Status?>(1L));
    }

    [Fact]
    public void Int64_column_into_nullable_int()
    {
        Assert.Equal(42, LadybugRowConvert.To<int?>(42L));
    }

    [Fact]
    public void Double_column_into_nullable_float()
    {
        Assert.Equal(1.5f, LadybugRowConvert.To<float?>(1.5d));
    }

    [Fact]
    public void Decimal_column_into_nullable_decimal()
    {
        Assert.Equal(3.14m, LadybugRowConvert.To<decimal?>(3.14m));
    }

    [Fact]
    public void Guid_over_guid_cell()
    {
        var g = Guid.NewGuid();
        Assert.Equal(g, LadybugRowConvert.To<Guid>(g));
        Assert.Equal(g, LadybugRowConvert.To<Guid?>(g));
    }

    [Fact]
    public void Guid_over_string_cell()
    {
        var g = Guid.NewGuid();
        // A UUID that failed Guid.TryParse upstream flows through as a string; also covers STRING
        // columns the user maps to Guid.
        Assert.Equal(g, LadybugRowConvert.To<Guid>(g.ToString()));
        Assert.Equal(g, LadybugRowConvert.To<Guid?>(g.ToString()));
    }

    [Fact]
    public void DateTime_over_date_column()
    {
        // The engine returns DATE as DateOnly; mapping to a DateTime member must cross-convert.
        var date = new DateOnly(2026, 6, 14);
        Assert.Equal(date.ToDateTime(TimeOnly.MinValue), LadybugRowConvert.To<DateTime>(date));
        Assert.Equal(date.ToDateTime(TimeOnly.MinValue), LadybugRowConvert.To<DateTime?>(date));
    }

    [Fact]
    public void DateOnly_over_datetime_cell()
    {
        var dt = new DateTime(2026, 6, 14, 9, 30, 0);
        Assert.Equal(new DateOnly(2026, 6, 14), LadybugRowConvert.To<DateOnly>(dt));
    }

    [Fact]
    public void Fast_path_when_cell_already_target_type()
    {
        Assert.Equal("hi", LadybugRowConvert.To<string>("hi"));
        Assert.Equal(7L, LadybugRowConvert.To<long>(7L));
    }
}
