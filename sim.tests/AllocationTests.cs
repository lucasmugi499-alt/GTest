using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Economy;

namespace Cascade.Sim.Tests;

/// <summary>Spec Shortage allocation: a_j = d_j · min(1, R_k / D_k), tiers filled in order.</summary>
public class AllocationTests
{
    private static Fixed[] Run(long supply, long[] demand, PriorityTier[] tiers)
    {
        var got = new Fixed[demand.Length];
        Allocation.ByTier(Fixed.FromInt(supply), demand.Select(d => Fixed.FromInt(d)).ToArray(), tiers, got);
        return got;
    }

    [Fact]
    public void EveryoneServedWhenSupplyIsAmple()
    {
        var got = Run(100, [10, 20, 30], [PriorityTier.Low, PriorityTier.Critical, PriorityTier.Normal]);
        Assert.Equal([Fixed.FromInt(10), Fixed.FromInt(20), Fixed.FromInt(30)], got);
    }

    [Fact]
    public void HigherTiersFillFirstAndTheShortTierSharesProportionally()
    {
        // Critical 30 filled; High wants 40+40 but only 50 left → each gets 25; Low gets nothing.
        var got = Run(80,
            [30, 40, 40, 10],
            [PriorityTier.Critical, PriorityTier.High, PriorityTier.High, PriorityTier.Low]);
        Assert.Equal([Fixed.FromInt(30), Fixed.FromInt(25), Fixed.FromInt(25), Fixed.Zero], got);
    }

    [Fact]
    public void ProportionalSharesFollowDemandSize()
    {
        var got = Run(60, [100, 50], [PriorityTier.Normal, PriorityTier.Normal]);
        Assert.Equal(Fixed.FromInt(40), got[0]);
        Assert.Equal(Fixed.FromInt(20), got[1]);
    }

    [Fact]
    public void NeverHandsOutMoreThanTheSupply()
    {
        var got = new Fixed[3];
        var demand = new[] { Fixed.Parse("0.0007"), Fixed.Parse("0.0007"), Fixed.Parse("0.0007") };
        Allocation.ByTier(Fixed.Parse("0.001"), demand, [PriorityTier.Low, PriorityTier.Low, PriorityTier.Low], got);
        Assert.True(got.Sum(g => g.Raw) <= 10);
    }
}
