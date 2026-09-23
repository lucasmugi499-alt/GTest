using Cascade.Sim.Core;
using Cascade.Sim.Scheduling;

namespace Cascade.Sim.Tests;

/// <summary>Spec Determinism rules, and the M1 milestone test: same seed → same hash at day 90.</summary>
public class DeterminismTests
{
    [Fact]
    public void SameSeedSameHashAtDay90()
    {
        var a = TestContent.NewProbeSim(seed: 20310303);
        var b = TestContent.NewProbeSim(seed: 20310303);
        a.RunThrough(90);
        b.RunThrough(90);
        Assert.Equal(a.StateHash(), b.StateHash());
        // And the probe really did move state, so the test isn't passing on an idle world.
        Assert.NotEqual(TestContent.NewProbeSim(20310303).StateHash(), a.StateHash());
    }

    [Fact]
    public void SameSeedSameHashEveryDay()
    {
        var a = TestContent.NewProbeSim(7);
        var b = TestContent.NewProbeSim(7);
        for (int d = 0; d <= 90; d++)
        {
            a.StepDay();
            b.StepDay();
            Assert.Equal(a.StateHash(), b.StateHash());
        }
    }

    [Fact]
    public void DifferentSeedDifferentHash()
    {
        var a = TestContent.NewProbeSim(1);
        var b = TestContent.NewProbeSim(2);
        a.RunThrough(90);
        b.RunThrough(90);
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void DefaultPipelineIsDeterministicToo()
    {
        var a = TestContent.NewSim(20310303);
        var b = TestContent.NewSim(20310303);
        a.RunThrough(90);
        b.RunThrough(90);
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void SameOrdersReplayToSameHash()
    {
        static ulong Play(IEnumerable<(int day, int province, int value)> script)
        {
            var sim = TestContent.NewProbeSim(99);
            int kestria = sim.World.Nations.Player;
            var byDay = script.ToLookup(s => s.day);
            while (!sim.IsFinished)
            {
                foreach (var s in byDay[sim.Day])
                    sim.Orders.Enqueue(new SetCorruptionOrder(kestria, s.province, Fixed.FromInt(s.value)));
                sim.StepDay();
            }
            return sim.StateHash();
        }

        var script = new[] { (3, 0, 50), (10, 1, 5), (40, 2, 80) };
        Assert.Equal(Play(script), Play(script));
        Assert.NotEqual(Play(script), Play([(3, 0, 50), (10, 1, 5), (41, 2, 80)])); // one day later changes history
    }

    [Fact]
    public void HashCoversPendingOrdersAndEvents()
    {
        var sim = TestContent.NewSim();
        var h0 = sim.StateHash();
        sim.Events.Schedule(new MarkEvent(5, 3, 0, []));
        var h1 = sim.StateHash();
        sim.Orders.Enqueue(new SetCorruptionOrder(0, 0, Fixed.One));
        var h2 = sim.StateHash();
        Assert.NotEqual(h0, h1);
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void StepByHourEqualsStepByDay()
    {
        // The UI plays crisis days hour by hour; that must not change the outcome.
        var byDay = TestContent.NewProbeSim(3);
        var byHour = TestContent.NewProbeSim(3);
        byDay.Events.Schedule(new MarkEvent(4, 2, 0, []));
        byHour.Events.Schedule(new MarkEvent(4, 2, 0, []));
        byDay.RunThrough(10);
        while (byHour.Day <= 10) byHour.StepHour();
        Assert.Equal(byDay.StateHash(), byHour.StateHash());
    }
}
