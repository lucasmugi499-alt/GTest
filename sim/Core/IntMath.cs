namespace Cascade.Sim.Core;

/// <summary>Integer helpers shared by the fixed-point types. Rounding is to nearest, ties away from zero (D-002).</summary>
internal static class IntMath
{
    public static Int128 DivRound(Int128 n, Int128 d)
    {
        if (d == 0) throw new DivideByZeroException();
        Int128 q = n / d;
        Int128 r = n % d;
        if (r == 0) return q;
        // Compare 2|r| with |d| without overflow risk (values here are far below Int128 limits).
        if (Int128.Abs(r) * 2 >= Int128.Abs(d))
            q += (n < 0) == (d < 0) ? 1 : -1;
        return q;
    }

    public static long ToLong(Int128 v)
    {
        if (v > long.MaxValue || v < long.MinValue)
            throw new OverflowException("Fixed-point value out of 64-bit range.");
        return (long)v;
    }

    /// <summary>Parses a plain decimal string ("-12.345") into an integer at the given number of decimals. No exponents.</summary>
    public static long ParseScaled(string s, int decimals, string typeName)
    {
        if (string.IsNullOrWhiteSpace(s)) throw new FormatException($"{typeName}: empty number.");
        s = s.Trim();
        int i = 0;
        bool neg = false;
        if (s[0] is '-' or '+') { neg = s[0] == '-'; i = 1; }
        Int128 whole = 0, frac = 0;
        int fracDigits = 0;
        bool seenDot = false, seenDigit = false;
        for (; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '_') continue; // YAML-style digit separators: 1_000_000
            if (c == '.')
            {
                if (seenDot) throw new FormatException($"{typeName}: '{s}' has two decimal points.");
                seenDot = true;
                continue;
            }
            if (c < '0' || c > '9') throw new FormatException($"{typeName}: '{s}' is not a plain decimal number.");
            seenDigit = true;
            if (seenDot)
            {
                if (++fracDigits > decimals)
                    throw new FormatException($"{typeName}: '{s}' has more than {decimals} decimal places.");
                frac = frac * 10 + (c - '0');
            }
            else
            {
                whole = whole * 10 + (c - '0');
                if (whole > long.MaxValue) throw new OverflowException($"{typeName}: '{s}' is too large.");
            }
        }
        if (!seenDigit) throw new FormatException($"{typeName}: '{s}' has no digits.");
        for (int k = fracDigits; k < decimals; k++) frac *= 10;
        Int128 scale = Pow10(decimals);
        Int128 raw = whole * scale + frac;
        return ToLong(neg ? -raw : raw);
    }

    public static string FormatScaled(long raw, int decimals, bool trim)
    {
        long scale = (long)Pow10(decimals);
        bool neg = raw < 0;
        UInt128 abs = neg ? (UInt128)(-(Int128)raw) : (UInt128)raw;
        UInt128 whole = abs / (UInt128)scale;
        UInt128 frac = abs % (UInt128)scale;
        string f = frac.ToString().PadLeft(decimals, '0');
        if (trim) f = f.TrimEnd('0');
        string sign = neg ? "-" : "";
        return f.Length == 0 ? $"{sign}{whole}" : $"{sign}{whole}.{f}";
    }

    public static Int128 Pow10(int n)
    {
        Int128 r = 1;
        for (int i = 0; i < n; i++) r *= 10;
        return r;
    }
}
