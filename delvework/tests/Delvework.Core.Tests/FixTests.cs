namespace Delvework.Core.Tests;

public class FixTests
{
    private static Fix P(string s) => Fix.TryParse(s, out var f) ? f : throw new FormatException(s);

    [Theory]
    [InlineData("12.375", 811008)]
    [InlineData("0.5", 32768)]
    [InlineData("3", 196608)]
    [InlineData(".25", 16384)]
    public void ParsesDecimalsExactly(string text, long raw)
    {
        Assert.Equal(raw, P(text).Raw);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("1.2.3")]
    [InlineData("-1")]
    [InlineData("1e5")]
    [InlineData("1234567890123456")]
    public void RejectsNonDecimals(string text)
    {
        Assert.False(Fix.TryParse(text, out _));
    }

    [Theory]
    [InlineData("2", "2.0")]
    [InlineData("0.1", "0.1")]
    [InlineData("2.5", "2.5")]
    [InlineData("0.33333", "0.3333")]
    [InlineData("0.99999", "1.0")]
    public void FormatsWithUpToFourDecimals(string text, string expected)
    {
        Assert.Equal(expected, P(text).ToString());
    }

    [Fact]
    public void ArithmeticIsExactWherePossible()
    {
        Assert.Equal(P("3.75"), P("1.5") * P("2.5"));
        Assert.Equal(P("0.5"), Fix.FromInt(1) / Fix.FromInt(2));
        Assert.Equal(P("4"), P("1.5") + P("2.5"));
        Assert.Equal(Fix.FromInt(-1), P("1.5") - P("2.5"));
        Assert.Equal(P("0.25"), Fix.Ratio(1, 4));
    }

    [Fact]
    public void RoundingHelpers()
    {
        var x = -P("2.5");
        Assert.Equal(-3, x.Floor());
        Assert.Equal(-2, x.Truncate());
        Assert.Equal(3, P("2.5").Round());
        Assert.True(P("4").IsInteger);
        Assert.False(P("4.5").IsInteger);
    }

    [Fact]
    public void ModuloFollowsTheDivisorSign()
    {
        Assert.Equal(P("0.5"), -P("7.5") % Fix.FromInt(2));
        Assert.Equal(-P("0.5"), P("7.5") % Fix.FromInt(-2));
    }

    [Fact]
    public void PowerHandlesNegativeExponents()
    {
        Assert.Equal(P("8"), Fix.Pow(Fix.FromInt(2), 3));
        Assert.Equal(P("0.125"), Fix.Pow(Fix.FromInt(2), -3));
        Assert.Equal(Fix.One, Fix.Pow(P("7.25"), 0));
        Assert.Throws<OverflowException>(() => Fix.Pow(Fix.FromInt(2), long.MinValue));
    }

    [Fact]
    public void OverflowAndDivisionByZeroThrow()
    {
        Assert.Throws<OverflowException>(() => Fix.FromInt(long.MaxValue / 1000) * Fix.FromInt(1_000_000));
        Assert.Throws<DivideByZeroException>(() => Fix.One / Fix.Zero);
    }

    [Fact]
    public void ComparesAndOrders()
    {
        Assert.True(P("1.5") < P("2"));
        Assert.Equal(P("1.5"), Fix.Min(P("1.5"), P("2")));
        Assert.Equal(P("2"), Fix.Max(P("1.5"), P("2")));
        Assert.Equal(P("1.5"), Fix.Abs(-P("1.5")));
    }
}
