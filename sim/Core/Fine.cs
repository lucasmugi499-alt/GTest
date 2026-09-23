using System.Runtime.InteropServices;

namespace Cascade.Sim.Core;

/// <summary>
/// A population share or probability: 64-bit integer at 8 decimal places (raw 100,000,000 = 1.0). D-002.
/// Used where 4 decimals would round small hourly rates to zero (narrative shares, per-hour probabilities).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Fine : IEquatable<Fine>, IComparable<Fine>
{
    public const int Decimals = 8;
    public const long Scale = 100_000_000;

    public readonly long Raw;

    private Fine(long raw) => Raw = raw;

    public static Fine FromRaw(long raw) => new(raw);
    public static Fine FromInt(long v) => new(checked(v * Scale));
    public static Fine Ratio(long num, long den) => new(IntMath.ToLong(IntMath.DivRound((Int128)num * Scale, den)));
    public static Fine Parse(string s) => new(IntMath.ParseScaled(s, Decimals, nameof(Fine)));

    public static readonly Fine Zero = new(0);
    public static readonly Fine One = new(Scale);

    public static Fine operator +(Fine a, Fine b) => new(checked(a.Raw + b.Raw));
    public static Fine operator -(Fine a, Fine b) => new(checked(a.Raw - b.Raw));
    public static Fine operator -(Fine a) => new(checked(-a.Raw));
    public static Fine operator *(Fine a, Fine b) => new(IntMath.ToLong(IntMath.DivRound((Int128)a.Raw * b.Raw, Scale)));
    public static Fine operator /(Fine a, Fine b) => new(IntMath.ToLong(IntMath.DivRound((Int128)a.Raw * Scale, b.Raw)));
    public static Fine operator *(Fine a, long k) => new(checked(a.Raw * k));
    public static Fine operator /(Fine a, long k) => new(IntMath.ToLong(IntMath.DivRound(a.Raw, k)));

    /// <summary>Rounds to the 4-decimal stored type.</summary>
    public Fixed ToFixed() => Fixed.FromRaw(IntMath.ToLong(IntMath.DivRound(Raw, Scale / Fixed.Scale)));

    public static bool operator ==(Fine a, Fine b) => a.Raw == b.Raw;
    public static bool operator !=(Fine a, Fine b) => a.Raw != b.Raw;
    public static bool operator <(Fine a, Fine b) => a.Raw < b.Raw;
    public static bool operator >(Fine a, Fine b) => a.Raw > b.Raw;
    public static bool operator <=(Fine a, Fine b) => a.Raw <= b.Raw;
    public static bool operator >=(Fine a, Fine b) => a.Raw >= b.Raw;

    public static Fine Min(Fine a, Fine b) => a.Raw <= b.Raw ? a : b;
    public static Fine Max(Fine a, Fine b) => a.Raw >= b.Raw ? a : b;
    public static Fine Clamp(Fine x, Fine lo, Fine hi) => Max(lo, Min(hi, x));
    public static Fine Clamp01(Fine x) => Clamp(x, Zero, One);

    public bool Equals(Fine other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fine f && f.Raw == Raw;
    public override int GetHashCode() => Raw.GetHashCode();
    public int CompareTo(Fine other) => Raw.CompareTo(other.Raw);

    public override string ToString() => IntMath.FormatScaled(Raw, Decimals, trim: true);

    /// <summary>For display only. The sim never uses floats.</summary>
    public double ToDoubleForUi() => Raw / (double)Scale;
}
