using System;
using System.Globalization;
using System.Numerics;

namespace LadybugDB;

/// <summary>
/// A lossless fixed-point DECIMAL value: an unscaled <see cref="BigInteger"/> mantissa plus a
/// <see cref="Scale"/> giving the number of fractional digits. The numeric value is
/// <c>Unscaled * 10^-Scale</c>. Unlike <see cref="decimal"/> it has no range or precision limit,
/// so a DECIMAL column is always materialized without loss. Use <see cref="ToDecimal"/> /
/// <see cref="TryToDecimal"/> to project to the CLR <see cref="decimal"/> when it fits.
/// </summary>
public readonly struct LadybugDecimal : IEquatable<LadybugDecimal>
{
    // decimal.MaxValue == 79228162514264337593543950335 (2^96 - 1).
    private static readonly BigInteger MaxDecimalMantissa = BigInteger.Parse("79228162514264337593543950335");

    /// <summary>The unscaled integer mantissa (the value with the decimal point removed).</summary>
    public BigInteger Unscaled { get; }

    /// <summary>The number of fractional digits (the power of ten the mantissa is divided by).</summary>
    public byte Scale { get; }

    /// <summary>Creates a decimal from its unscaled mantissa and scale.</summary>
    public LadybugDecimal(BigInteger unscaled, byte scale)
    {
        Unscaled = unscaled;
        Scale = scale;
    }

    /// <summary>
    /// Projects to a CLR <see cref="decimal"/>. The value fits when the mantissa is within the
    /// 96-bit decimal range and <see cref="Scale"/> is 0–28.
    /// </summary>
    /// <exception cref="OverflowException">The value does not fit in a <see cref="decimal"/>.</exception>
    public decimal ToDecimal()
    {
        if (!TryToDecimal(out decimal value))
        {
            throw new OverflowException("The LadybugDecimal value does not fit in a System.Decimal.");
        }

        return value;
    }

    /// <summary>
    /// Attempts to project to a CLR <see cref="decimal"/> without throwing. Returns <c>false</c>
    /// (and <paramref name="value"/> = 0) when the mantissa or scale is out of decimal range.
    /// </summary>
    public bool TryToDecimal(out decimal value)
    {
        value = 0m;

        // decimal stores a 96-bit unsigned mantissa with a scale of 0..28.
        if (Scale > 28)
        {
            return false;
        }

        BigInteger magnitude = BigInteger.Abs(Unscaled);
        if (magnitude > MaxDecimalMantissa)
        {
            return false;
        }

        byte[] bytes = magnitude.ToByteArray(); // little-endian, two's complement (always non-negative here).
        int lo = 0, mid = 0, hi = 0;
        for (int i = 0; i < bytes.Length && i < 12; i++)
        {
            int shift = (i % 4) * 8;
            int word = i / 4;
            switch (word)
            {
                case 0: lo |= bytes[i] << shift; break;
                case 1: mid |= bytes[i] << shift; break;
                default: hi |= bytes[i] << shift; break;
            }
        }

        value = new decimal(lo, mid, hi, Unscaled.Sign < 0, Scale);
        return true;
    }

    /// <summary>
    /// Parses the exact textual form (e.g. <c>"-12.3400"</c>) into an unscaled mantissa and scale.
    /// Trailing fractional zeros are preserved (they determine the scale). No exponent form.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is null.</exception>
    /// <exception cref="FormatException"><paramref name="s"/> is not a valid fixed-point literal.</exception>
    public static LadybugDecimal Parse(string s)
    {
        s = ThrowHelpers.ThrowIfNull(s, nameof(s));

        string text = s.Trim();
        if (text.Length == 0)
        {
            throw new FormatException("The decimal text is empty.");
        }

        int dot = text.IndexOf('.');
        byte scale;
        string digits;
        if (dot < 0)
        {
            scale = 0;
            digits = text;
        }
        else
        {
            if (text.IndexOf('.', dot + 1) >= 0)
            {
                throw new FormatException($"'{s}' has more than one decimal point.");
            }

            int fractionLength = text.Length - dot - 1;
            if (fractionLength > byte.MaxValue)
            {
                throw new FormatException($"'{s}' has too many fractional digits.");
            }

            scale = (byte)fractionLength;
            digits = text.Substring(0, dot) + text.Substring(dot + 1);
        }

        // BigInteger.Parse rejects an empty mantissa and any non-digit/sign characters.
        BigInteger unscaled = BigInteger.Parse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return new LadybugDecimal(unscaled, scale);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (Scale == 0)
        {
            return Unscaled.ToString(CultureInfo.InvariantCulture);
        }

        bool negative = Unscaled.Sign < 0;
        string digits = BigInteger.Abs(Unscaled).ToString(CultureInfo.InvariantCulture);
        if (digits.Length <= Scale)
        {
            digits = digits.PadLeft(Scale + 1, '0');
        }

        int pointIndex = digits.Length - Scale;
        string text = digits.Substring(0, pointIndex) + "." + digits.Substring(pointIndex);
        return negative ? "-" + text : text;
    }

    /// <inheritdoc />
    public bool Equals(LadybugDecimal other) => Unscaled == other.Unscaled && Scale == other.Scale;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is LadybugDecimal other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (Unscaled, Scale).GetHashCode();

    public static bool operator ==(LadybugDecimal left, LadybugDecimal right) => left.Equals(right);

    public static bool operator !=(LadybugDecimal left, LadybugDecimal right) => !left.Equals(right);
}
