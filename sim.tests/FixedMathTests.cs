using Cascade.Sim.Core;

namespace Cascade.Sim.Tests;

public class FixedMathTests
{
    // Tests may use doubles as the reference; the sim itself never does.
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("0.5")]
    [InlineData("-2.7368")]
    [InlineData("3.5")]
    [InlineData("-10")]
    [InlineData("10")]
    [InlineData("-18")]
    [InlineData("20")]
    [InlineData("25")]
    public void ExpMatchesReference(string x)
    {
        var got = FixedMath.Exp(Fine.Parse(x)).ToDoubleForUi();
        var want = Math.Exp(double.Parse(x, System.Globalization.CultureInfo.InvariantCulture));
        // Within 1.5 units of the last decimal, or 1e-15 relative for large results.
        Assert.True(Math.Abs(got - want) <= Math.Max(1.5e-8, want * 1e-15), $"exp({x}) = {got}, want {want}");
    }

    [Fact]
    public void ExpEdges()
    {
        Assert.Equal(Fine.One, FixedMath.Exp(Fine.Zero));
        Assert.Equal(Fine.Zero, FixedMath.Exp(Fine.FromInt(-40)));
        Assert.Throws<OverflowException>(() => FixedMath.Exp(Fine.FromInt(26)));
    }

    [Fact]
    public void SigmoidIsSymmetricAndBounded()
    {
        Assert.Equal(Fine.Parse("0.5"), FixedMath.Sigmoid(Fine.Zero));
        var a = FixedMath.Sigmoid(Fine.Parse("1.7"));
        var b = FixedMath.Sigmoid(Fine.Parse("-1.7"));
        Assert.True(Math.Abs((a + b - Fine.One).Raw) <= 1);
        Assert.Equal(Fine.One, FixedMath.Sigmoid(Fine.FromInt(30)));
        Assert.Equal(Fine.Zero, FixedMath.Sigmoid(Fine.FromInt(-30)));
    }

    // The spec's own worked numbers, reproduced in fixed point.

    [Fact]
    public void SpecWorkedExample_DetectionProbabilities()
    {
        // Worked example: Maren recon 75 vs Dravek concealment 40 → p ≈ 0.97; Dravek 40 vs 60 → p ≈ 0.12.
        var maren = FixedMath.Sigmoid(Fixed.Ratio(75 - 40, 10));
        var dravek = FixedMath.Sigmoid(Fixed.Ratio(40 - 60, 10));
        Assert.Equal("0.97", maren.ToFixed().ToString(2));
        Assert.Equal("0.12", dravek.ToFixed().ToString(2));
    }

    [Fact]
    public void SpecSampleRecord_FabYieldAt780Days()
    {
        // Fab yield Y(X) = Ymax − (Ymax − Y0)·e^(−X/285), legacy Ymax 0.92, Y0 0.30.
        // The sample record lists experience_days 780 with yield 0.88.
        var ymax = Fixed.Parse("0.92");
        var y0 = Fixed.Parse("0.30");
        var decay = FixedMath.Exp(-Fixed.Ratio(780, 285).ToFine());
        var y = ymax - (ymax - y0).Times(decay);
        Assert.Equal("0.88", y.ToString(2));
    }

    [Fact]
    public void SpecAttributionConfidence()
    {
        // c(t) = cmax(1 − e^(−t/14)); at t = 14 a direct operation (cmax 0.9) sits at 0.9 × 0.632 ≈ 0.569.
        var c = Fixed.Parse("0.9").Times(Fine.One - FixedMath.Exp(Fine.FromInt(-1)));
        Assert.Equal("0.569", c.ToString(3));
    }
}
