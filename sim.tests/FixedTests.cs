using Cascade.Sim.Core;

namespace Cascade.Sim.Tests;

public class FixedTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 10_000)]
    [InlineData("0.94", 9_400)]
    [InlineData("-5", -50_000)]
    [InlineData("1_200", 12_000_000)]
    [InlineData("0.0001", 1)]
    [InlineData("-0.5", -5_000)]
    public void ParsesExactDecimals(string text, long raw) => Assert.Equal(raw, Fixed.Parse(text).Raw);

    [Theory]
    [InlineData("0.00001")]  // more than 4 decimals: must be a Fine, not silently rounded
    [InlineData("1e-3")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("")]
    public void RejectsBadNumbers(string text) => Assert.ThrowsAny<FormatException>(() => Fixed.Parse(text));

    [Fact]
    public void FormatsInvariantly()
    {
        Assert.Equal("0.94", Fixed.Parse("0.94").ToString());
        Assert.Equal("-12.5", Fixed.Parse("-12.5").ToString());
        Assert.Equal("3", Fixed.FromInt(3).ToString());
        Assert.Equal("0.9400", Fixed.Parse("0.94").ToString(4));
        Assert.Equal("0.1", Fixed.Parse("0.05").ToString(1)); // tie rounds away from zero
    }

    [Fact]
    public void ArithmeticRoundsToNearestTiesAwayFromZero()
    {
        // 0.0001 × 0.5 = 0.00005 → tie → 0.0001
        Assert.Equal(1, (Fixed.FromRaw(1) * Fixed.Parse("0.5")).Raw);
        Assert.Equal(-1, (Fixed.FromRaw(-1) * Fixed.Parse("0.5")).Raw);
        // 0.0001 × 0.4 = 0.00004 → 0
        Assert.Equal(0, (Fixed.FromRaw(1) * Fixed.Parse("0.4")).Raw);
        Assert.Equal(Fixed.Parse("0.3333"), Fixed.One / Fixed.FromInt(3));
        Assert.Equal(Fixed.Parse("0.6667"), Fixed.FromInt(2) / Fixed.FromInt(3));
        Assert.Equal(Fixed.Parse("-0.6667"), Fixed.FromInt(-2) / Fixed.FromInt(3));
        Assert.Equal(Fixed.Parse("0.6667"), Fixed.Ratio(2, 3));
    }

    [Fact]
    public void LargeProductsUse128BitIntermediates()
    {
        // 34 million people × 0.58 trust share: the raw product (3.4e11 × 5.8e3) overflows 64 bits before scaling back.
        var pop = Fixed.FromInt(34_000_000);
        Assert.Equal(Fixed.FromInt(19_720_000), pop * Fixed.Parse("0.58"));
    }

    [Fact]
    public void OverflowThrowsInsteadOfWrapping()
    {
        var huge = Fixed.FromRaw(long.MaxValue / 2);
        Assert.Throws<OverflowException>(() => huge + huge + huge);
        Assert.Throws<OverflowException>(() => huge * Fixed.FromInt(10));
    }

    [Fact]
    public void FineHoldsSmallHourlyRates()
    {
        // D-002's reason for Fine: γ = 0.02/day as an hourly rate, times a small believing share.
        var gammaHourly = Fine.Parse("0.02") / 24;
        Assert.Equal(83_333, gammaHourly.Raw);                       // 0.00083333
        var drop = gammaHourly * Fine.Parse("0.0015");               // ≈ 0.00000125
        Assert.Equal(125, drop.Raw);
        Assert.Equal(0, drop.ToFixed().Raw);                        // the same value at 4 decimals rounds to zero
    }

    [Fact]
    public void FixedTimesFineScalesStoredValues()
    {
        var population = Fixed.FromInt(4_100_000);
        Assert.Equal(Fixed.FromInt(1_025_000), population.Times(Fine.Parse("0.25")));
        Assert.Equal(Fine.Parse("0.94"), Fixed.Parse("0.94").ToFine());
        Assert.Equal(Fixed.Parse("0.1235"), Fine.Parse("0.12345").ToFixed());
    }
}
