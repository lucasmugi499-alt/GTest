using System.Runtime.InteropServices;

namespace Cascade.Sim.Core;

/// <summary>
/// A stored sim value: 64-bit integer at 4 decimal places (raw 10,000 = 1.0). Spec Conventions + D-002.
/// Multiplication and division go through 128-bit intermediates and round once, to nearest, ties away from zero.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Fixed : IEquatable<Fixed>, IComparable<Fixed>
{
    public const int Decimals = 4;
    public const long Scale = 10_000;

    public readonly long Raw;

    private Fixed(long raw) => Raw = raw;

    public static Fixed FromRaw(long raw) => new(raw);
    public static Fixed FromInt(long v) => new(checked(v * Scale));
    /// <summary>num / den, rounded to 4 decimals. Use for exact ratios without going through floats.</summary>
    public static Fixed Ratio(long num, long den) => new(IntMath.ToLong(IntMath.DivRound((Int128)num * Scale, den)));
    public static Fixed Parse(string s) => new(IntMath.ParseScaled(s, Decimals, nameof(Fixed)));

    public static readonly Fixed Zero = new(0);
    public static readonly Fixed One = new(Scale);
    public static readonly Fixed Hundred = new(100 * Scale);

    public static Fixed operator +(Fixed a, Fixed b) => new(checked(a.Raw + b.Raw));
    public static Fixed operator -(Fixed a, Fixed b) => new(checked(a.Raw - b.Raw));
    public static Fixed operator -(Fixed a) => new(checked(-a.Raw));
    public static Fixed operator *(Fixed a, Fixed b) => new(IntMath.ToLong(IntMath.DivRound((Int128)a.Raw * b.Raw, Scale)));
    public static Fixed operator /(Fixed a, Fixed b) => new(IntMath.ToLong(IntMath.DivRound((Int128)a.Raw * Scale, b.Raw)));
    public static Fixed operator *(Fixed a, long k) => new(checked(a.Raw * k));
    public static Fixed operator /(Fixed a, long k) => new(IntMath.ToLong(IntMath.DivRound(a.Raw, k)));

    /// <summary>Scales a stored value by a fine-grained share or probability, e.g. population × belief share.</summary>
    public Fixed Times(Fine f) => new(IntMath.ToLong(IntMath.DivRound((Int128)Raw * f.Raw, Fine.Scale)));

    public Fine ToFine() => Fine.FromRaw(checked(Raw * (Fine.Scale / Scale)));
    /// <summary>Whole part, rounded to nearest.</summary>
    public long RoundToInt() => IntMath.ToLong(IntMath.DivRound(Raw, Scale));

    public static bool operator ==(Fixed a, Fixed b) => a.Raw == b.Raw;
    public static bool operator !=(Fixed a, Fixed b) => a.Raw != b.Raw;
    public static bool operator <(Fixed a, Fixed b) => a.Raw < b.Raw;
    public static bool operator >(Fixed a, Fixed b) => a.Raw > b.Raw;
    public static bool operator <=(Fixed a, Fixed b) => a.Raw <= b.Raw;
    public static bool operator >=(Fixed a, Fixed b) => a.Raw >= b.Raw;

    public static Fixed Min(Fixed a, Fixed b) => a.Raw <= b.Raw ? a : b;
    public static Fixed Max(Fixed a, Fixed b) => a.Raw >= b.Raw ? a : b;
    public static Fixed Clamp(Fixed x, Fixed lo, Fixed hi) => Max(lo, Min(hi, x));
    public Fixed Abs() => Raw < 0 ? -this : this;

    public bool Equals(Fixed other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fixed f && f.Raw == Raw;
    public override int GetHashCode() => Raw.GetHashCode();
    public int CompareTo(Fixed other) => Raw.CompareTo(other.Raw);

    public override string ToString() => IntMath.FormatScaled(Raw, Decimals, trim: true);
    public string ToString(int decimals) => IntMath.FormatScaled(IntMath.ToLong(IntMath.DivRound(Raw, (long)IntMath.Pow10(Decimals - decimals))), decimals, trim: false);

    /// <summary>For display only. The sim never uses floats.</summary>
    public double ToDoubleForUi() => Raw / (double)Scale;
}
