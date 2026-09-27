using System.Globalization;
using System.Text;

namespace Delvework.Core;

/// <summary>
/// Q47.16 fixed-point number. The simulation and Glyph's <c>float</c> use this instead of
/// IEEE doubles so results are bit-identical on every platform.
/// </summary>
public readonly struct Fix : IEquatable<Fix>, IComparable<Fix>
{
    public const int FracBits = 16;
    public const long OneRaw = 1L << FracBits;

    public static readonly Fix Zero = new(0);
    public static readonly Fix One = new(OneRaw);
    public static readonly Fix Half = new(OneRaw / 2);

    public readonly long Raw;

    private Fix(long raw) => Raw = raw;

    public static Fix FromRaw(long raw) => new(raw);

    public static Fix FromInt(long value) => new(checked(value * OneRaw));

    /// <summary>Exact ratio a/b, truncated toward zero.</summary>
    public static Fix Ratio(long numerator, long denominator) => FromInt(numerator) / FromInt(denominator);

    public bool IsInteger => (Raw & (OneRaw - 1)) == 0;

    /// <summary>Largest integer not greater than this value.</summary>
    public long Floor() => Raw >> FracBits;

    /// <summary>Integer part, truncated toward zero (like Python's <c>int()</c>).</summary>
    public long Truncate() => Raw >= 0 ? Raw >> FracBits : -((-Raw) >> FracBits);

    public long Round() => (Raw + OneRaw / 2) >> FracBits;

    public static Fix operator +(Fix a, Fix b) => new(checked(a.Raw + b.Raw));
    public static Fix operator -(Fix a, Fix b) => new(checked(a.Raw - b.Raw));
    public static Fix operator -(Fix a) => new(checked(-a.Raw));

    public static Fix operator *(Fix a, Fix b)
    {
        var product = (Int128)a.Raw * b.Raw >> FracBits;
        return new(checked((long)product));
    }

    public static Fix operator /(Fix a, Fix b)
    {
        if (b.Raw == 0) throw new DivideByZeroException();
        var quotient = ((Int128)a.Raw << FracBits) / b.Raw;
        return new(checked((long)quotient));
    }

    /// <summary>Python-style modulo: the result has the sign of the divisor.</summary>
    public static Fix operator %(Fix a, Fix b)
    {
        if (b.Raw == 0) throw new DivideByZeroException();
        var r = a.Raw % b.Raw;
        if (r != 0 && (r < 0) != (b.Raw < 0)) r += b.Raw;
        return new(r);
    }

    public static bool operator ==(Fix a, Fix b) => a.Raw == b.Raw;
    public static bool operator !=(Fix a, Fix b) => a.Raw != b.Raw;
    public static bool operator <(Fix a, Fix b) => a.Raw < b.Raw;
    public static bool operator >(Fix a, Fix b) => a.Raw > b.Raw;
    public static bool operator <=(Fix a, Fix b) => a.Raw <= b.Raw;
    public static bool operator >=(Fix a, Fix b) => a.Raw >= b.Raw;

    public static Fix Min(Fix a, Fix b) => a.Raw <= b.Raw ? a : b;
    public static Fix Max(Fix a, Fix b) => a.Raw >= b.Raw ? a : b;
    public static Fix Abs(Fix a) => a.Raw < 0 ? -a : a;

    /// <summary>Integer power by repeated squaring. Negative exponents give the reciprocal.</summary>
    public static Fix Pow(Fix x, long exponent)
    {
        if (exponent == long.MinValue) throw new OverflowException();
        if (exponent < 0) return One / Pow(x, -exponent);
        var result = One;
        var b = x;
        var e = exponent;
        while (e > 0)
        {
            if ((e & 1) != 0) result *= b;
            e >>= 1;
            if (e > 0) b *= b;
        }
        return result;
    }

    /// <summary>
    /// Parse a plain decimal literal such as <c>12.375</c> exactly, without going through
    /// floating point. Returns false for anything else.
    /// </summary>
    public static bool TryParse(string text, out Fix value)
    {
        value = Zero;
        if (string.IsNullOrEmpty(text)) return false;
        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var intPart = dot < 0 ? text : text[..dot];
        var fracPart = dot < 0 ? "" : text[(dot + 1)..];
        if (intPart.Length == 0 && fracPart.Length == 0) return false;
        if (!intPart.All(char.IsAsciiDigit) || !fracPart.All(char.IsAsciiDigit)) return false;
        if (intPart.Length > 15) return false;

        long whole = intPart.Length == 0 ? 0 : long.Parse(intPart, CultureInfo.InvariantCulture);
        if (fracPart.Length > 9) fracPart = fracPart[..9];
        long frac = 0;
        long scale = 1;
        foreach (var c in fracPart)
        {
            frac = frac * 10 + (c - '0');
            scale *= 10;
        }
        var fracRaw = (frac * OneRaw + scale / 2) / scale;
        value = new(whole * OneRaw + fracRaw);
        return true;
    }

    /// <summary>Formats with up to 4 decimals and at least one, like <c>2.0</c> or <c>0.3333</c>.</summary>
    public override string ToString()
    {
        var neg = Raw < 0;
        var abs = neg ? -(Int128)Raw : Raw;
        var whole = abs >> FracBits;
        var frac = abs & (OneRaw - 1);
        var digits = (frac * 10000 + OneRaw / 2) / OneRaw;
        if (digits >= 10000)
        {
            whole += 1;
            digits -= 10000;
        }
        var sb = new StringBuilder();
        if (neg && (whole != 0 || digits != 0)) sb.Append('-');
        sb.Append(whole.ToString(CultureInfo.InvariantCulture));
        sb.Append('.');
        var fracText = ((int)digits).ToString("D4", CultureInfo.InvariantCulture).TrimEnd('0');
        sb.Append(fracText.Length == 0 ? "0" : fracText);
        return sb.ToString();
    }

    public bool Equals(Fix other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fix f && f.Raw == Raw;
    public override int GetHashCode() => Raw.GetHashCode();
    public int CompareTo(Fix other) => Raw.CompareTo(other.Raw);
}
