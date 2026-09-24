using Cascade.Sim.Conflict;
using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Economy;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Tests;

/// <summary>Escalation, Varan's behaviour and cyber operations.</summary>
public class ConflictTests
{
    // ---- Escalation ----

    [Fact]
    public void ScenarioStartsAtRungTwo()
    {
        var sim = TestContent.NewScenario();
        Assert.Equal(Fixed.FromInt(20), Escalation.Meter(sim.World, 0, 1));
        Assert.Equal(2, sim.Balance.Escalation.RungOf(Escalation.Meter(sim.World, 0, 1)));
    }

    [Fact]
    public void RungBandsFollowD003()
    {
        var e = TestContent.Repo.Balance.Escalation;
        Assert.Equal(1, e.RungOf(Fixed.FromInt(14)));
        Assert.Equal(2, e.RungOf(Fixed.FromInt(15)));
        Assert.Equal(4, e.RungOf(Fixed.FromInt(45)));
        Assert.Equal(5, e.RungOf(Fixed.FromInt(60)));
        Assert.Equal(7, e.RungOf(Fixed.FromInt(95)));
    }

    [Fact]
    public void ScriptedWeekMatchesTheConcept()
    {
        // Day 3 clash → rung 4; Day 4 blackout and deepfake; Day 6 export controls. Varan doesn't escalate on its own.
        var sim = TestContent.NewScenario();
        sim.RunThrough(6);
        var w = sim.World;
        Assert.Equal(4, sim.Balance.Escalation.RungOf(Escalation.Meter(w, 0, 1)));
        Assert.Equal(3, Enumerable.Range(0, w.Substations.Count).Count(s => w.Substations.State[s] == (int)SubstationState.Damaged));
        Assert.True(w.Narratives.Active[w.Narratives.IdOf("president_fled")]);
        int magnets = Enumerable.Range(0, w.Imports.Count).Single(r => w.Imports.Good[r] == Veyl.Good(sim, "rare_earth_magnet") && w.Imports.Source[r] >= 0);
        Assert.True(w.Imports.Blocked[magnets]);
        Assert.DoesNotContain(w.Log.Entries, e => e.Kind == "red_line");
        sim.RunThrough(20);
        Assert.Equal(-1, w.Front.ActiveUntil[1]); // no Varani offensive unless provoked
    }

    [Fact]
    public void MeterDecaysWeeklyWhenQuiet()
    {
        var sim = TestContent.NewSim();
        sim.StepDay(); // day 0, a Monday: no action of weight 3+ in the last 7 days
        Assert.Equal(Fixed.FromInt(19), Escalation.Meter(sim.World, 0, 1));
        sim.RunThrough(7);
        Assert.Equal(Fixed.FromInt(18), Escalation.Meter(sim.World, 0, 1));
    }

    [Fact]
    public void VaranAnswersAKestrianActionWithinTwoWeeks()
    {
        // Kestria burns its dam access (disruptive cyber, weight 6): cautious Varan (r = 0.8, D-043) answers with weight ≤ 4.8.
        var sim = TestContent.NewSim();
        var w = sim.World;
        int dam = w.Operations.IdOf("kestria_ossen_dam");
        sim.Orders.Enqueue(new LaunchCyberOperationOrder(0, dam));
        sim.StepDay();
        Assert.Equal((int)OperationState.Used, w.Operations.State[dam]);
        var intent = w.Log.Entries.Single(e => e.Kind == "ai_intent");
        Assert.Equal("influence_campaign", intent.Subject); // the heaviest repertoire action that fits: weight 3
        sim.RunThrough(15);
        Assert.Contains(w.Log.Entries, e => e.Kind == "escalation" && e.Actor == "varan" && e.Subject == "influence_detected");
    }

    [Fact]
    public void CrossingTheRedLineMakesVaranEscalateOnce()
    {
        var sim = TestContent.NewSim();
        var w = sim.World;
        w.Politics.RedLine.Init(1, Fixed.FromInt(25));
        sim.Orders.Enqueue(new LaunchCyberOperationOrder(0, w.Operations.IdOf("kestria_ossen_dam"))); // 20 → 26
        sim.RunThrough(1);
        Assert.Single(w.Log.Entries, e => e.Kind == "red_line");
        Assert.Contains(w.Log.Entries, e => e.Kind == "escalation" && e.Actor == "varan");
        // The step that lifts the meter to the next rung (30+): the cheapest action with that floor, else the heaviest.
        Assert.True(Escalation.Meter(w, 0, 1) >= Fixed.FromInt(30));
    }

    // ---- Cyber ----

    [Fact]
    public void Day4AttackUsesTheSeededAccess()
    {
        var sim = TestContent.NewScenario();
        sim.RunThrough(3);
        sim.StepHour(); sim.StepHour();
        var w = sim.World;
        int op = w.Operations.IdOf("varan_eastern_grid");
        Assert.Equal((int)OperationState.Ready, w.Operations.State[op]);
        sim.StepHour(); // 02:00
        Assert.Equal((int)OperationState.Used, w.Operations.State[op]);
        Assert.Equal((int)SubstationState.Damaged, w.Substations.State[Veyl.Sub(sim, "ossen_industrial")]);
    }

    [Fact]
    public void AttributionConfidenceGrowsTowardTheProxyCap()
    {
        // c(t) = 0.6 (1 − e^(−t/14)) through a proxy: 0.379 after 14 days.
        Assert.Equal("0.379", Cyber.Attribution(TestContent.Repo.Balance, true, 14).ToFixed().ToString(3));
        Assert.Equal("0.569", Cyber.Attribution(TestContent.Repo.Balance, false, 14).ToFixed().ToString(3));
        var sim = TestContent.NewScenario();
        sim.RunThrough(18);
        var att = sim.World.Operations.Attribution[sim.World.Operations.IdOf("varan_eastern_grid")];
        Assert.Equal(Cyber.Attribution(sim.Balance, true, 14), att);
    }

    [Fact]
    public void DisruptionLastsTheSpecHours()
    {
        // 12 × (A/50) × payload × (1 − analog) = 12 × 0.9 × 1 × 0.8 = 8.64 hours for the Interior foothold.
        var b = TestContent.Repo.Balance;
        Assert.Equal(Fixed.Parse("8.64"), Cyber.DisruptionHours(b, Fixed.FromInt(45), Fixed.One, Fine.Parse("0.2")));
    }

    [Fact]
    public void DefusedSeedTurnsDay4IntoADetectedIntrusionAndAShortInteriorBlackout()
    {
        // D-014: a forensic sweep between Day 0 and Day 2 finds the access; on Day 4 Varan's attempt is caught (+4)
        // and it hits two Interior substations instead, with the disruption effect. The fab never goes dark.
        var sim = TestContent.NewScenario();
        var w = sim.World;
        int ossen = Veyl.Province(sim, "kestria_east_ossen");
        sim.Orders.Enqueue(new ForensicSweepOrder(0, ossen));
        sim.StepDay();
        int op = w.Operations.IdOf("varan_eastern_grid");
        Assert.Equal((int)OperationState.Detected, w.Operations.State[op]);
        Assert.Equal(Fixed.Zero, w.Operations.Access[op]);
        Assert.Equal(Fixed.FromInt(50), w.Operations.Defence[op]);

        sim.RunThrough(3);
        sim.StepHour(); sim.StepHour(); sim.StepHour(); // through 02:00 on day 4
        Assert.Contains(w.Log.Entries, e => e.Day == 4 && e.Subject == "cyber_intrusion_detected");
        Assert.Equal((int)SubstationState.Tripped, w.Substations.State[Veyl.Sub(sim, "interior_north")]);
        Assert.Equal((int)SubstationState.Online, w.Substations.State[Veyl.Sub(sim, "ossen_industrial")]);
        sim.StepDay();
        Assert.Equal((int)SubstationState.Online, w.Substations.State[Veyl.Sub(sim, "interior_north")]); // back within the day
        sim.RunThrough(10);
        Assert.Equal(0, w.Facilities.Interruptions[Veyl.Facility(sim, "tessera_fab_3")]);
    }

    [Fact]
    public void ForensicSweepCostsFifteenCapital()
    {
        var sim = TestContent.NewScenario();
        sim.StepDay(); // day 0 is a Monday: weekly capital lands then
        var before = sim.World.Politics.PoliticalCapital[0];
        sim.Orders.Enqueue(new ForensicSweepOrder(0, Veyl.Province(sim, "kestria_east_ossen")));
        sim.StepDay();
        Assert.Equal(before - Fixed.FromInt(15), sim.World.Politics.PoliticalCapital[0]);
    }

    [Fact]
    public void AccessGrowsMonthly()
    {
        // ΔA = Sk/10 × v: Varan skill 65, eastern vulnerability 1.2 → +7.8 on 1 April (day 29).
        var sim = TestContent.NewSim(seed: 3);
        var w = sim.World;
        int op = w.Operations.IdOf("varan_eastern_grid");
        sim.RunThrough(29);
        if (w.Operations.State[op] == (int)OperationState.Ready)
            Assert.Equal(Fixed.Parse("82.8"), w.Operations.Access[op]);
    }

    [Fact]
    public void PatchCloseFootholdOnTheSameVendor()
    {
        var sim = TestContent.NewSim();
        int op = sim.World.Operations.IdOf("varan_interior_grid");
        sim.Events.Schedule(new PatchEvent(2, op));
        sim.RunThrough(2);
        Assert.Equal((int)OperationState.Patched, sim.World.Operations.State[op]);
        Assert.Equal(Fixed.Zero, sim.World.Operations.Access[op]);
    }

    // ---- Markets ----

    [Fact]
    public void WarRiskPremiumsFollowTheRung()
    {
        var sim = TestContent.NewScenario();
        sim.RunThrough(7); // Monday after the clash: rung 4 → ×5 ("premiums up 400%")
        var w = sim.World;
        Assert.Equal(Fixed.FromInt(5), w.Politics.InsuranceMultiplier[0]);
        int calling = w.Politics.LinesCalling[0];
        Assert.InRange(calling, 0, 5);
        int magnets = Enumerable.Range(0, w.Imports.Count).Single(r => w.Imports.Good[r] == Veyl.Good(sim, "wafer_blank"));
        Assert.Equal(Fine.Ratio(calling, 5), w.Imports.CapacityFactor[magnets]);
    }

    [Fact]
    public void FullScenarioIsDeterministic()
    {
        var a = TestContent.NewScenario(7);
        var b = TestContent.NewScenario(7);
        a.Orders.Enqueue(new SetMobilizationOrder(0, 2));
        b.Orders.Enqueue(new SetMobilizationOrder(0, 2));
        a.RunThrough(90);
        while (!b.IsFinished) b.StepHour();
        Assert.Equal(a.StateHash(), b.StateHash());
    }
}
