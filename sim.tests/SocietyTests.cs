using Cascade.Sim.Conflict;
using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Tests;

/// <summary>Politics, needs, factions, precedents, and the information war.</summary>
public class SocietyTests
{
    [Fact]
    public void StartingTrustIs58AndCapitalIs120()
    {
        var sim = TestContent.NewScenario();
        Assert.InRange(sim.World.Politics.Trust[0].ToDoubleForUi(), 57.5, 58.5);
        Assert.Equal(Fixed.FromInt(120), sim.World.Politics.PoliticalCapital[0]);
    }

    [Fact]
    public void ApprovalFollowsTheFormula()
    {
        var sim = TestContent.NewSim();
        sim.StepDay();
        var w = sim.World;
        var sat = Fixed.Zero; var align = Fixed.Zero; long pop = 0;
        for (int s = 0; s < w.Segments.Count; s++)
        {
            sat += w.Segments.Satisfaction[s] * w.Segments.Population[s];
            align += w.Segments.Align[s] * w.Segments.Population[s];
            pop += w.Segments.Population[s];
        }
        var expected = (Fixed.Parse("0.6") * sat + Fixed.Parse("0.4") * align) / pop;
        Assert.Equal(expected, w.Politics.Approval[0]);
    }

    [Fact]
    public void BlackoutHurtsOssenFamiliesPowerNeedAndApproval()
    {
        var sim = TestContent.NewSim();
        sim.RunThrough(3);
        var approval = sim.World.Politics.Approval[0];
        Veyl.TripOssen(sim);
        sim.RunThrough(14);
        var w = sim.World;
        int families = w.Segments.IdOf("ossen_families");
        int capital = w.Segments.IdOf("capital_professionals");
        Assert.True(w.Segments.Need[w.Segments.At(families, Need.Power)] == Fixed.Zero);
        Assert.True(w.Segments.NeedSmoothed[w.Segments.At(families, Need.Power)] < Fixed.FromInt(50)); // half-life 7 days
        Assert.Equal(Fixed.Hundred, w.Segments.Need[w.Segments.At(capital, Need.Power)]);
        Assert.True(w.Politics.Approval[0] < approval);
    }

    [Fact]
    public void PoliticalCapitalGrowsWeekly()
    {
        var sim = TestContent.NewSim();
        sim.RunThrough(6);
        var before = sim.World.Politics.PoliticalCapital[0];
        var approval = sim.World.Politics.Approval[0];
        sim.StepDay(); // day 7, a Monday: + 2 + 0.1 × (Approval − 50), no rally
        var expected = before + Fixed.FromInt(2) + Fixed.Parse("0.1") * (sim.World.Politics.Approval[0] - Fixed.FromInt(50));
        Assert.Equal(expected, sim.World.Politics.PoliticalCapital[0]);
        Assert.True(approval > Fixed.Zero);
    }

    [Fact]
    public void EmergencyAndPrecedentsCostLessEachTime()
    {
        var sim = TestContent.NewSim();
        var w = sim.World;
        sim.StepDay(); // past Monday's weekly capital
        var pc0 = w.Politics.PoliticalCapital[0];
        sim.Orders.Enqueue(new DeclareEmergencyOrder(0));
        sim.StepDay();
        Assert.True(w.Politics.EmergencyActive[0]);
        Assert.Equal(pc0 - Fixed.FromInt(30), w.Politics.PoliticalCapital[0]);
        Assert.Equal(Fixed.FromInt(5), w.Politics.Backsliding[0]); // first use of a precedent

        int ossen = Veyl.Province(sim, "kestria_east_ossen");
        int veyl = Veyl.Province(sim, "veyl");
        int interior = Veyl.Province(sim, "kestria_interior");
        var pc1 = w.Politics.PoliticalCapital[0];
        sim.Orders.Enqueue(new UseEmergencyPowerOrder(0, "internet_shutdown", ossen));   // 20
        sim.Orders.Enqueue(new UseEmergencyPowerOrder(0, "internet_shutdown", veyl));    // 12
        sim.Orders.Enqueue(new UseEmergencyPowerOrder(0, "internet_shutdown", interior)); // 7.2
        sim.StepDay();
        Assert.Equal(pc1 - Fixed.FromInt(20) - Fixed.FromInt(12) - Fixed.Parse("7.2"), w.Politics.PoliticalCapital[0]);
        Assert.True(w.Provinces.InternetShutdown[ossen]);
        int families = w.Segments.IdOf("ossen_families");
        Assert.Equal(Fixed.Zero, w.Segments.Need[w.Segments.At(families, Need.Connectivity)]);
    }

    [Fact]
    public void FourthUseNormalizesAndCostsDignity()
    {
        var sim = TestContent.NewSim();
        var w = sim.World;
        w.Politics.PoliticalCapital.Init(0, Fixed.FromInt(300));
        sim.Orders.Enqueue(new DeclareEmergencyOrder(0));
        sim.StepDay();
        for (int i = 0; i < 4; i++)
        {
            sim.Orders.Enqueue(new UseEmergencyPowerOrder(0, "curfew"));
            sim.StepDay();
            sim.Orders.Enqueue(new EndEmergencyOrder(0));
            sim.Orders.Enqueue(new DeclareEmergencyOrder(0));
            sim.StepDay();
        }
        Assert.Equal(Fixed.FromInt(3), w.Politics.DignityPenalty[0]);
        Assert.Contains(w.Log.Entries, e => e.Kind == "precedent" && e.Text.Contains("no longer surprises"));
    }

    [Fact]
    public void EndingAnEmergencyCostsTenPerMonth()
    {
        var sim = TestContent.NewSim();
        var w = sim.World;
        sim.Orders.Enqueue(new DeclareEmergencyOrder(0));
        sim.RunThrough(40);
        var before = w.Politics.PoliticalCapital[0];
        sim.Orders.Enqueue(new EndEmergencyOrder(0));
        sim.StepDay(); // day 41: 41 days → 2 months
        Assert.False(w.Politics.EmergencyActive[0]);
        Assert.Equal(before - Fixed.FromInt(20), w.Politics.PoliticalCapital[0]);
    }

    [Fact]
    public void AngryStrongFactionsAct()
    {
        // Labour: approval under 30 and Leverage above 20 → acts each week with probability 0.1 × Leverage ÷ 50 (~5%).
        // Across ten seeds and thirteen weeks, some must act.
        int acted = 0;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var sim = TestContent.NewSim(seed);
            var w = sim.World;
            int labour = w.Factions.IdOf("labour");
            w.Factions.Standing.Init(labour, Fixed.FromInt(-200));
            w.Factions.Approval.Init(labour, Fixed.FromInt(10));
            sim.RunThrough(90);
            Assert.True(w.Factions.Leverage[labour] > Fixed.FromInt(20));
            if (w.Factions.Actions[labour] > 0)
            {
                acted++;
                Assert.Contains(w.Log.Entries, e => e.Kind == "faction_action" && e.Actor == "labour");
            }
        }
        Assert.True(acted >= 3, $"{acted} of 10 seeds had Labour act");
    }

    [Fact]
    public void WarExhaustionGrowsWithDeathsAndBlackouts()
    {
        var sim = TestContent.NewScenario();
        sim.RunThrough(7); // week of the clash and the blackout
        var x = sim.World.Politics.WarExhaustion[0];
        Assert.True(x > Fixed.Zero);
    }

    // ---- Information ----

    [Fact]
    public void SharesStaySumToOne()
    {
        var sim = TestContent.NewScenario();
        sim.RunThrough(12);
        var nar = sim.World.Narratives;
        int n = nar.IdOf("president_fled");
        for (int s = 0; s < nar.Segments; s++)
        {
            int at = nar.At(n, s);
            var total = nar.S[at] + nar.E[at] + nar.B[at] + nar.R[at];
            Assert.InRange(total.Raw, Fine.Scale - 50, Fine.Scale + 50);
            Assert.True(nar.S[at] >= Fine.Zero && nar.E[at] >= Fine.Zero && nar.B[at] >= Fine.Zero && nar.R[at] >= Fine.Zero);
        }
    }

    [Fact]
    public void DeepfakeTipsWithinDaysAndIsForecast()
    {
        var sim = TestContent.NewScenario();
        sim.RunThrough(4);
        var nar = sim.World.Narratives;
        int n = nar.IdOf("president_fled");
        var hours = nar.RumorHours[n];
        Assert.True(hours > Fixed.Zero, "Rumor Velocity should forecast a tipping point");
        sim.RunThrough(8);
        Assert.Contains(Enumerable.Range(0, nar.Segments), s => nar.Established[nar.At(n, s)]);
        Assert.True(nar.FactionEffectsApplied[n]);
    }

    [Fact]
    public void CounterNarrativeSlowsBelief()
    {
        static Fine Peak(bool counter)
        {
            var sim = TestContent.NewScenario();
            sim.RunThrough(4);
            int n = sim.World.Narratives.IdOf("president_fled");
            if (counter) sim.Orders.Enqueue(new CounterNarrativeOrder(0, n));
            sim.RunThrough(10);
            var nar = sim.World.Narratives;
            return Enumerable.Range(0, nar.Segments).Select(s => nar.B[nar.At(n, s)]).Max();
        }
        Assert.True(Peak(counter: true) < Peak(counter: false));
    }

    [Fact]
    public void CounterNarrativeNeedsTrustAbove50()
    {
        var sim = TestContent.NewScenario();
        foreach (var s in Enumerable.Range(0, sim.World.Segments.Count))
            sim.World.Segments.TrustBase.Init(s, Fixed.FromInt(40));
        sim.RunThrough(4);
        sim.Orders.Enqueue(new CounterNarrativeOrder(0, sim.World.Narratives.IdOf("president_fled")));
        sim.StepDay();
        Assert.False(sim.AppliedOrders[^1].Outcome.Accepted);
    }

    [Fact]
    public void BrightlineCompliesOnlyAboveTheThreshold()
    {
        // K + Loy/2 + P > 100 x_r + c:  0 + 20 + P > 30 + 20 → P must exceed 30.
        static bool Asks(int pressure)
        {
            var sim = TestContent.NewScenario();
            sim.RunThrough(4);
            sim.Orders.Enqueue(new TakedownOrder(0, sim.World.Narratives.IdOf("president_fled"), Fixed.FromInt(pressure), Fixed.Zero));
            sim.StepDay();
            return sim.AppliedOrders[^1].Outcome.Accepted;
        }
        Assert.False(Asks(30));
        Assert.True(Asks(31));
    }

    [Fact]
    public void TakedownAndShutdownSlowSpread()
    {
        static Fine Belief(Action<Simulation> act)
        {
            var sim = TestContent.NewScenario();
            sim.RunThrough(4);
            act(sim);
            sim.RunThrough(9);
            var nar = sim.World.Narratives;
            int n = nar.IdOf("president_fled");
            return nar.B[nar.At(n, sim.World.Segments.IdOf("ossen_families"))];
        }
        var baseline = Belief(_ => { });
        var takedown = Belief(sim => sim.Orders.Enqueue(new TakedownOrder(0, sim.World.Narratives.IdOf("president_fled"), Fixed.FromInt(40), Fixed.Zero)));
        var shutdown = Belief(sim =>
        {
            sim.Orders.Enqueue(new DeclareEmergencyOrder(0));
            sim.Orders.Enqueue(new UseEmergencyPowerOrder(0, "internet_shutdown", Veyl.Province(sim, "kestria_east_ossen")));
        });
        Assert.True(takedown < baseline);
        Assert.True(shutdown < takedown);
    }

    [Fact]
    public void PrebunkingMovesSusceptibleToRejecting()
    {
        var sim = TestContent.NewScenario();
        var nar = sim.World.Narratives;
        int n = nar.IdOf("chips_scandal");
        int seg = sim.World.Segments.IdOf("capital_professionals");
        nar.Active.Init(n, true); // active but nobody believes it yet: only prebunking moves anyone
        sim.Orders.Enqueue(new PrebunkOrder(0, n, [seg]));
        sim.StepDay();
        Assert.Equal(Fine.Parse("0.01"), nar.R[nar.At(n, seg)]);
    }
}
