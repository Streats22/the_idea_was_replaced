namespace Delvework.Core.Glyph;

/// <summary>Python-style integer division: results round toward negative infinity.</summary>
public static class Arith
{
    public static long FloorDiv(long a, long b)
    {
        if (b == 0) throw new DivideByZeroException();
        if (a == long.MinValue && b == -1) throw new OverflowException();
        var q = a / b;
        if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
        return q;
    }

    public static long Mod(long a, long b)
    {
        if (b == 0) throw new DivideByZeroException();
        if (b == -1) return 0;
        var r = a % b;
        if (r != 0 && ((r < 0) != (b < 0))) r += b;
        return r;
    }

    public static long Pow(long x, long e)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(e);
        long result = 1;
        while (e > 0)
        {
            if ((e & 1) != 0) result = checked(result * x);
            e >>= 1;
            if (e > 0) x = checked(x * x);
        }
        return result;
    }
}
