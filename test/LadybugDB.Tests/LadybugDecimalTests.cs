using System.Numerics;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests;

/// <summary>
/// Unit tests for <see cref="LadybugDecimal"/>. These exercise pure managed arithmetic and
/// formatting and do NOT require the native engine, so they are plain facts (never gated).
/// </summary>
public sealed class LadybugDecimalTests
{
    [Fact]
    public void ToString_renders_unscaled_with_decimal_point()
    {
        var d = new LadybugDecimal(new BigInteger(12345), 2);
        Assert.Equal("123.45", d.ToString());
    }

    [Fact]
    public void ToString_renders_negative_with_leading_zero()
    {
        var d = new LadybugDecimal(new BigInteger(-5), 3);
        Assert.Equal("-0.005", d.ToString());
    }

    [Fact]
    public void ToString_with_zero_scale_has_no_point()
    {
        var d = new LadybugDecimal(new BigInteger(42), 0);
        Assert.Equal("42", d.ToString());
    }

    [Fact]
    public void Unscaled_and_scale_are_exposed()
    {
        var d = new LadybugDecimal(new BigInteger(12345), 2);
        Assert.Equal(new BigInteger(12345), d.Unscaled);
        Assert.Equal((byte)2, d.Scale);
    }

    [Fact]
    public void ToDecimal_round_trips_an_in_range_value()
    {
        var d = new LadybugDecimal(new BigInteger(12345), 2);
        Assert.Equal(123.45m, d.ToDecimal());
    }

    [Fact]
    public void ToDecimal_round_trips_a_negative_in_range_value()
    {
        var d = new LadybugDecimal(new BigInteger(-12345), 4);
        Assert.Equal(-1.2345m, d.ToDecimal());
    }

    [Fact]
    public void TryToDecimal_returns_true_for_in_range()
    {
        var d = new LadybugDecimal(new BigInteger(1), 0);
        Assert.True(d.TryToDecimal(out decimal value));
        Assert.Equal(1m, value);
    }

    [Fact]
    public void TryToDecimal_returns_false_when_mantissa_exceeds_decimal_range()
    {
        // 31 nines: larger than decimal.MaxValue (~7.9e28), so projection must fail without throwing.
        var huge = BigInteger.Parse("9999999999999999999999999999999");
        var d = new LadybugDecimal(huge, 0);
        Assert.False(d.TryToDecimal(out decimal value));
        Assert.Equal(0m, value);
    }

    [Fact]
    public void TryToDecimal_returns_false_when_scale_exceeds_decimal_limit()
    {
        // decimal supports at most 28-29 fractional digits; scale 30 is out of range.
        var d = new LadybugDecimal(new BigInteger(1), 30);
        Assert.False(d.TryToDecimal(out _));
    }

    [Fact]
    public void ToDecimal_throws_when_out_of_range()
    {
        var huge = BigInteger.Parse("9999999999999999999999999999999");
        var d = new LadybugDecimal(huge, 0);
        Assert.Throws<System.OverflowException>(() => d.ToDecimal());
    }

    [Theory]
    [InlineData("123.45", "12345", 2)]
    [InlineData("-0.005", "-5", 3)]
    [InlineData("42", "42", 0)]
    [InlineData("0.00", "0", 2)]
    [InlineData("-1.2345", "-12345", 4)]
    [InlineData("  7.5  ", "75", 1)]
    public void Parse_reads_unscaled_and_scale(string text, string expectedUnscaled, int expectedScale)
    {
        var d = LadybugDecimal.Parse(text);
        Assert.Equal(BigInteger.Parse(expectedUnscaled), d.Unscaled);
        Assert.Equal((byte)expectedScale, d.Scale);
    }

    [Fact]
    public void Parse_then_ToString_round_trips_exactly()
    {
        Assert.Equal("123.4500", LadybugDecimal.Parse("123.4500").ToString());
        Assert.Equal("-0.005", LadybugDecimal.Parse("-0.005").ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    public void Parse_throws_for_invalid_text(string text)
    {
        Assert.Throws<System.FormatException>(() => LadybugDecimal.Parse(text));
    }

    [Fact]
    public void Parse_throws_for_null()
    {
        Assert.Throws<System.ArgumentNullException>(() => LadybugDecimal.Parse(null!));
    }

    [Fact]
    public void Equality_is_structural()
    {
        Assert.Equal(new LadybugDecimal(new BigInteger(12345), 2), new LadybugDecimal(new BigInteger(12345), 2));
        Assert.NotEqual(new LadybugDecimal(new BigInteger(12345), 2), new LadybugDecimal(new BigInteger(12345), 3));
        Assert.True(new LadybugDecimal(new BigInteger(1), 0) == new LadybugDecimal(new BigInteger(1), 0));
        Assert.True(new LadybugDecimal(new BigInteger(1), 0) != new LadybugDecimal(new BigInteger(2), 0));
    }
}
