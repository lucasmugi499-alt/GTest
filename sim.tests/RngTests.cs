using Cascade.Sim.Core;

namespace Cascade.Sim.Tests;

public class RngTests
{
    [Fact]
    public void SameCoordinatesSameRoll()
    {
        Assert.Equal(Rng.Hash(42, 7, 3, 5, 0), Rng.Hash(42, 7, 3, 5, 0));
    }

    [Fact]
    public void EveryCoordinateMatters()
    {
        ulong baseline = Rng.Hash(42, 7, 3, 5, 0);
        Assert.NotEqual(baseline, Rng.Hash(43, 7, 3, 5, 0));
        Assert.NotEqual(baseline, Rng.Hash(42, 8, 3, 5, 0));
        Assert.NotEqual(baseline, Rng.Hash(42, 7, 4, 5, 0));
        Assert.NotEqual(baseline, Rng.Hash(42, 7, 3, 6, 0));
        Assert.NotEqual(baseline, Rng.Hash(42, 7, 3, 5, 1));
    }

    [Fact]
    public void AlgorithmIsLocked()
    {
        // If this fails, the RNG changed and every saved replay is invalid. Only update deliberately.
        // Values cross-checked against an independent Python implementation of the same SplitMix64 chain.
        Assert.Equal(0xBC131D95D28477ADUL, Rng.Hash(0, 0, 0, 0, 0));
        Assert.Equal(0xC766ACC1B14D0ABDUL, Rng.Hash(20310303, 4, 7, 2, 3));
    }

    [Fact]
    public void StreamsDoNotDependOnWhatElseRan()
    {
        // Two streams for different entities: drawing from one never shifts the other.
        var a1 = new RngStream(1, 10, 3, 100);
        var a2 = new RngStream(1, 10, 3, 100);
        var other = new RngStream(1, 10, 3, 101);
        a1.NextU64();
        other.NextU64(); other.NextU64();
        a2.NextU64();
        Assert.Equal(a1.NextU64(), a2.NextU64());
    }

    [Fact]
    public void UniformChanceAndNormalLookRight()
    {
        var s = new RngStream(99, 0, 0, 0);
        const int n = 100_000;
        long sum = 0; int hits = 0;
        var mean = Fixed.FromInt(60); var sd = Fixed.FromInt(10);
        long nsum = 0, nsq = 0;
        for (int i = 0; i < n; i++)
        {
            var u = s.NextFine();
            Assert.InRange(u.Raw, 0, Fine.Scale - 1);
            sum += u.Raw;
            if (s.Chance(Fine.Parse("0.3"))) hits++;
            var x = s.NextNormal(mean, sd).ToDoubleForUi();
            nsum += (long)(x * 1000); nsq += (long)((x - 60) * (x - 60) * 1000);
        }
        Assert.InRange(sum / (double)n / Fine.Scale, 0.495, 0.505);
        Assert.InRange(hits / (double)n, 0.295, 0.305);
        Assert.InRange(nsum / 1000.0 / n, 59.8, 60.2);
        Assert.InRange(Math.Sqrt(nsq / 1000.0 / n), 9.8, 10.2);
    }

    [Fact]
    public void NextIntStaysInRange()
    {
        var s = new RngStream(5, 0, 0, 0);
        var seen = new bool[6];
        for (int i = 0; i < 1000; i++)
        {
            int v = s.NextInt(1, 7);
            Assert.InRange(v, 1, 6);
            seen[v - 1] = true;
        }
        Assert.All(seen, Assert.True);
    }

    [Fact]
    public void TickKeysNeverCollide()
    {
        var keys = new HashSet<ulong>();
        for (int day = 0; day < 100; day++)
        {
            for (int h = 0; h < 24; h++) Assert.True(keys.Add(Rng.TickKey(day, (TickSlot)h)));
            Assert.True(keys.Add(Rng.TickKey(day, TickSlot.Daily)));
            Assert.True(keys.Add(Rng.TickKey(day, TickSlot.Weekly)));
            Assert.True(keys.Add(Rng.TickKey(day, TickSlot.Monthly)));
        }
    }
}
