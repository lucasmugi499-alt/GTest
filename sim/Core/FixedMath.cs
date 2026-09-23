namespace Cascade.Sim.Core;

/// <summary>
/// Deterministic transcendental functions in pure integer math (D-002: no floats in the sim).
/// Internally works at 18 decimal places in 128-bit integers, then rounds once to 8.
/// </summary>
public static class FixedMath
{
    private static readonly Int128 One18 = 1_000_000_000_000_000_000;
    private static readonly Int128 Ln2At18 = 693_147_180_559_945_309; // ln 2 × 10^18
    private const long Fine18Ratio = 10_000_000_000;                   // 10^18 / 10^8

    /// <summary>Largest input to <see cref="Exp"/>; e^25 ≈ 7.2e10 still fits the 8-decimal type.</summary>
    public static readonly Fine ExpMaxInput = Fine.FromInt(25);
    /// <summary>Below this, e^x rounds to 0 at 8 decimals.</summary>
    public static readonly Fine ExpMinInput = Fine.FromInt(-25);

    /// <summary>e^x. Throws above <see cref="ExpMaxInput"/>, returns 0 below <see cref="ExpMinInput"/>.</summary>
    public static Fine Exp(Fine x)
    {
        if (x < ExpMinInput) return Fine.Zero;
        if (x > ExpMaxInput) throw new OverflowException($"Exp({x}) exceeds the fixed-point range.");

        // Range reduction: x = k·ln2 + r with |r| <= ln2/2, then e^x = 2^k · e^r.
        Int128 xh = (Int128)x.Raw * Fine18Ratio;
        Int128 k = IntMath.DivRound(xh, Ln2At18);
        Int128 r = xh - k * Ln2At18;

        // Taylor series for e^r; |r| < 0.35 so terms vanish within ~20 steps.
        Int128 sum = One18, term = One18;
        for (int n = 1; n < 40; n++)
        {
            term = IntMath.DivRound(term * r, One18 * n);
            if (term == 0) break;
            sum += term;
        }

        Int128 scaled = k >= 0 ? sum << (int)k : IntMath.DivRound(sum, (Int128)1 << (int)-k);
        return Fine.FromRaw(IntMath.ToLong(IntMath.DivRound(scaled, Fine18Ratio)));
    }

    public static Fine Exp(Fixed x) => Exp(x.ToFine());

    /// <summary>The logistic curve σ(x) = 1 / (1 + e^(−x)). Spec Notation.</summary>
    public static Fine Sigmoid(Fine x)
    {
        if (x > ExpMaxInput) return Fine.One;
        if (x < ExpMinInput) return Fine.Zero;
        if (x.Raw >= 0)
        {
            Fine e = Exp(-x);
            return Fine.FromRaw(IntMath.ToLong(IntMath.DivRound((Int128)Fine.Scale * Fine.Scale, Fine.Scale + e.Raw)));
        }
        else
        {
            Fine e = Exp(x);
            return Fine.FromRaw(IntMath.ToLong(IntMath.DivRound((Int128)e.Raw * Fine.Scale, Fine.Scale + e.Raw)));
        }
    }

    public static Fine Sigmoid(Fixed x) => Sigmoid(x.ToFine());
}
