using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Economy;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Tests;

public class GridAndFabTests
{
    // ---- The M2 milestone test (spec Scenario regression tests, D-015) ----

    [Fact]
    public void TrippingThreeEasternSubstationsCutsLegacyChipOutput35To45Percent()
    {
        // Trip on day 10 so there are 7 full days of history: compare days 3–9 with days 10–16.
        var sim = TestContent.NewSim();
        Veyl.TripOssen(sim, day: 10, hour: 2);
        var daily = new List<double>();
        while (sim.Day <= 16)
        {
            sim.StepDay();
            daily.Add(Veyl.ChipOutput(sim));
        }
        double before = daily.Skip(3).Take(7).Average();
        double after = daily.Skip(10).Take(7).Average();
        double cut = 1 - after / before;
        Assert.InRange(cut, 0.35, 0.45);
    }

    [Fact]
    public void Day4AttackCutsLegacyChipOutput35To45Percent()
    {
        // The scenario's own timing: only days 0–3 exist before the Day 4 attack.
        var sim = TestContent.NewSim();
        Veyl.TripOssen(sim);
        var daily = new List<double>();
        while (sim.Day <= 10)
        {
            sim.StepDay();
            daily.Add(Veyl.ChipOutput(sim));
        }
        double cut = 1 - daily.Skip(4).Take(7).Average() / daily.Take(4).Average();
        Assert.InRange(cut, 0.35, 0.45);
    }

    // ---- Grid ----

    [Fact]
    public void DamagedSubstationBlacksOutEverythingBehindIt()
    {
        var sim = TestContent.NewSim();
        Veyl.TripOssen(sim);
        sim.RunThrough(4);
        var w = sim.World;
        Assert.Equal(Fine.Zero, w.Loads.ServedLast[Veyl.Load(sim, "ossen_north_homes")]);
        Assert.Equal(Fine.Zero, w.Loads.ServedLast[Veyl.Load(sim, "tessera_fab_3")]);
        Assert.Equal(Fine.One, w.Loads.ServedLast[Veyl.Load(sim, "ossen_west_homes")]);
        Assert.Equal(4_100_000, SimSnapshot.Of(sim).Province("kestria_east_ossen").PeopleWithoutPower);
        // Down from 02:00: 2 of 24 hours powered on day 4.
        Assert.Equal(Fine.Ratio(2, 24), w.Loads.PowerRatio[Veyl.Load(sim, "ossen_north_homes")]);
        Assert.False(w.Provinces.Collapsed[Veyl.Province(sim, "kestria_east_ossen")]); // 68% of load < 75% (D-028)
    }

    [Fact]
    public void ShortSupplyShedsLowTiersFirst()
    {
        // Lose most of Ossen's generation: homes (Low) go dark before the hospital (Critical) or the fab (Normal).
        var sim = TestContent.NewSim();
        var w = sim.World;
        foreach (var key in new[] { "ossen_gas", "ossen_dam" })
            w.Plants.Available.Init(w.Plants.IdOf(key), Fine.Parse("0.2"));
        sim.StepDay();
        Assert.Equal(Fine.One, w.Loads.ServedLast[Veyl.Load(sim, "ossen_general")]);
        Assert.Equal(Fine.One, w.Loads.ServedLast[Veyl.Load(sim, "tessera_fab_3")]);
        Assert.True(w.Loads.ServedLast[Veyl.Load(sim, "ossen_west_homes")] < Fine.One);
        // Homes share the shortfall proportionally (same tier).
        Assert.Equal(w.Loads.ServedLast[Veyl.Load(sim, "ossen_north_homes")], w.Loads.ServedLast[Veyl.Load(sim, "ossen_west_homes")]);
    }

    [Fact]
    public void TieLinesCarrySurplusToNeighbours()
    {
        // Veyl's own plant (60 MW) can't cover its 133 MW; the tie-line from Ossen does.
        var sim = TestContent.NewSim();
        sim.StepDay();
        var w = sim.World;
        Assert.Equal(Fine.One, w.Loads.ServedLast[Veyl.Load(sim, "veyl_homes")]);
        int tie = w.TieLines.IdOf("veyl~kestria_east_ossen");
        Assert.True(w.TieLines.Flow[tie] < Fixed.Zero); // B (Ossen) → A (Veyl)
    }

    [Fact]
    public void PlayerCanPutTheFabFirst()
    {
        // Day 10 option B, "Fab first": raise the fab's priority so it keeps power when supply is short.
        var sim = TestContent.NewSim();
        var w = sim.World;
        int fab = Veyl.Facility(sim, "tessera_fab_3");
        foreach (var key in new[] { "ossen_gas", "ossen_dam" })
            w.Plants.Available.Init(w.Plants.IdOf(key), Fine.Zero);
        w.TieLines.Flow.Init(0, Fixed.Zero);
        // With no local generation Ossen lives on the 800 MW tie-line; all Normal industry shares it.
        sim.StepDay();
        var shared = w.Loads.ServedLast[w.Facilities.Load[fab]];
        sim.Orders.Enqueue(new SetPriorityOrder(0, PriorityTarget.Facility, fab, PriorityTier.Critical));
        sim.StepDay();
        Assert.True(shared <= Fine.One);
        Assert.Equal(Fine.One, w.Loads.ServedLast[w.Facilities.Load[fab]]);
    }

    [Fact]
    public void CollapseAndBlackStart()
    {
        // Hit 86% of Ossen's load: the region collapses, then the dam black-starts it at 25% of load a day.
        var sim = TestContent.NewSim();
        Veyl.TripOssen(sim, 2, 0, "ossen_north", "ossen_south", "ossen_west");
        sim.RunThrough(2);
        var w = sim.World;
        int ossen = Veyl.Province(sim, "kestria_east_ossen");
        Assert.True(w.Provinces.Collapsed[ossen]);
        // Industrial substation still intact but the region is restarting: fab only partly served.
        Assert.True(w.Loads.ServedLast[Veyl.Load(sim, "tessera_fab_3")] < Fine.One);
        sim.RunThrough(6);
        Assert.False(w.Provinces.Collapsed[ossen]);
        Assert.Equal(Fine.One, w.Loads.ServedLast[Veyl.Load(sim, "tessera_fab_3")]);
    }

    [Fact]
    public void SpareTransformerRestoresIn14DaysWithFullCrews()
    {
        var sim = TestContent.NewSim();
        Veyl.TripOssen(sim, 4, 2, "ossen_industrial");
        int sub = Veyl.Sub(sim, "ossen_industrial");
        sim.RunThrough(4);
        sim.Orders.Enqueue(new RepairSubstationOrder(0, sub, RepairChoice.Spare));
        sim.RunThrough(17); // days 5..18 of work → 14 days, done at end of day 18's dispatch
        Assert.Equal((int)SubstationState.Damaged, sim.World.Substations.State[sub]);
        sim.StepDay();      // day 18
        Assert.Equal((int)SubstationState.Online, sim.World.Substations.State[sub]);
        Assert.Equal(1, sim.World.Nations.SpareTransformers[0]);
        // Two spares in reserve: the third order is refused.
        Veyl.TripOssen(sim, 20, 2, "ossen_north", "ossen_south");
        sim.RunThrough(20);
        sim.Orders.Enqueue(new RepairSubstationOrder(0, Veyl.Sub(sim, "ossen_north"), RepairChoice.Spare));
        sim.Orders.Enqueue(new RepairSubstationOrder(0, Veyl.Sub(sim, "ossen_south"), RepairChoice.Spare));
        sim.StepDay();
        Assert.False(sim.AppliedOrders[^1].Outcome.Accepted);
        Assert.Equal("No spare transformers left.", sim.AppliedOrders[^1].Outcome.Reason);
    }

    [Fact]
    public void MobileUnitGives30PercentAfter7DaysAndReturnsAfterTheRealRepair()
    {
        var sim = TestContent.NewSim();
        Veyl.TripOssen(sim, 4, 2, "ossen_south");
        int sub = Veyl.Sub(sim, "ossen_south");
        sim.RunThrough(4);
        sim.Orders.Enqueue(new RepairSubstationOrder(0, sub, RepairChoice.Mobile));
        sim.RunThrough(10); // work days 5–10
        Assert.Equal(Fine.Zero, sim.World.Substations.CapacityFactor(sub, sim.Balance.Grid));
        sim.StepDay(); // day 11: 7th day of work
        Assert.Equal(Fine.Parse("0.3"), sim.World.Substations.CapacityFactor(sub, sim.Balance.Grid));
        // 30% of 900 MW = 270 MW: the hospital (Critical) is back on grid, homes (Low) get what's left.
        Assert.Equal(Fine.One, sim.World.Loads.ServedLast[Veyl.Load(sim, "ossen_general")]);
        Assert.Equal(1, sim.World.Nations.MobileSubstations[0]);

        sim.Orders.Enqueue(new RepairSubstationOrder(0, sub, RepairChoice.Spare));
        sim.RunThrough(27);
        Assert.Equal((int)SubstationState.Online, sim.World.Substations.State[sub]);
        Assert.Equal(2, sim.World.Nations.MobileSubstations[0]); // unit returned
    }

    [Fact]
    public void FewerLinemenSlowRepairs()
    {
        // Mobilization will drain linemen (M3). With half of Ossen's 1,200 linemen and 3 spare-sized jobs
        // needing 900, repairs run at 600/900 speed.
        var sim = TestContent.NewSim();
        var w = sim.World;
        int ossen = Veyl.Province(sim, "kestria_east_ossen");
        int pool = w.Labour.PoolId("grid_linemen");
        w.Labour.Available.Init(w.Labour.Index(ossen, pool), Fixed.FromInt(600));
        w.Nations.MobileSubstations.Init(0, 3);
        Veyl.TripOssen(sim);
        sim.RunThrough(4);
        foreach (var key in Veyl.AttackedSubstations)
            sim.Orders.Enqueue(new RepairSubstationOrder(0, Veyl.Sub(sim, key), RepairChoice.Mobile));
        sim.RunThrough(5);
        Assert.Equal(Fixed.Parse("0.6667"), w.Substations.MobileProgress[Veyl.Sub(sim, "ossen_north")]);
    }

    [Fact]
    public void NewTransformerTakesYearsAndIsSeeded()
    {
        int Days(ulong seed)
        {
            var sim = TestContent.NewSim(seed);
            Veyl.TripOssen(sim, 1, 2, "ossen_west");
            sim.RunThrough(1);
            sim.Orders.Enqueue(new RepairSubstationOrder(0, Veyl.Sub(sim, "ossen_west"), RepairChoice.NewTransformer));
            sim.StepDay();
            return (int)sim.World.Substations.RepairRequired[Veyl.Sub(sim, "ossen_west")].RoundToInt();
        }
        Assert.InRange(Days(1), 730, 1460);
        Assert.Equal(Days(1), Days(1));
        Assert.NotEqual(Days(1), Days(2));
    }

    // ---- Blackout clocks ----

    [Fact]
    public void CellTowersDieByDawn()
    {
        // Concept: "Cell towers die by dawn." 6-hour tanks from 02:00 → dead at 08:00; they ran 8 of 24 hours.
        var sim = TestContent.NewSim();
        Veyl.TripOssen(sim);
        sim.RunThrough(4);
        int cells = Veyl.Load(sim, "ossen_north_cells");
        Assert.Equal(Fixed.Zero, sim.World.Loads.FuelHours[cells]);
        Assert.Equal(Fine.Ratio(8, 24), sim.World.Loads.ServiceAvailability[cells]);
    }

    [Fact]
    public void HospitalRunsOutAfter60HoursWithoutRoadAccess()
    {
        var sim = TestContent.NewSim();
        int ossen = Veyl.Province(sim, "kestria_east_ossen");
        sim.World.Provinces.RoadAccess.Init(ossen, false);
        Veyl.TripOssen(sim);
        int hospital = Veyl.Load(sim, "ossen_general");
        sim.RunThrough(5);
        Assert.Equal(Fine.One, sim.World.Loads.ServiceAvailability[hospital]);
        sim.StepDay(); // day 6: tank empties at 14:00 (02:00 day 4 + 60 h)
        Assert.Equal(Fine.Ratio(14, 24), sim.World.Loads.ServiceAvailability[hospital]);
        sim.StepDay();
        Assert.Equal(Fine.Zero, sim.World.Loads.ServiceAvailability[hospital]);
    }

    [Fact]
    public void TrucksRefuelCriticalServicesFirstAndShareWithinATier()
    {
        // 1.5 t/h of trucks for 3.0 t/h of Critical burn (two pumps, one hospital): each gets half its burn,
        // so each Critical tank drains at half speed; the cell towers (High) get nothing.
        var sim = TestContent.NewSim();
        Veyl.TripOssen(sim);
        sim.RunThrough(4);
        var w = sim.World;
        var hospital = w.Loads.FuelHours[Veyl.Load(sim, "ossen_general")];
        var water = w.Loads.FuelHours[Veyl.Load(sim, "ossen_north_water")];
        // 22 dark hours at half net drain → 11 hours used from each tank.
        Assert.Equal(Fixed.FromInt(49), hospital);
        Assert.Equal(Fixed.FromInt(7), water);
    }

    // ---- Crisis Time ----

    [Fact]
    public void BlackoutHoldsOssenInCrisisUntilRepairedPlus48Hours()
    {
        var sim = TestContent.NewSim();
        Veyl.TripOssen(sim, 4, 2, "ossen_industrial");
        int sub = Veyl.Sub(sim, "ossen_industrial");
        int ossen = Veyl.Province(sim, "kestria_east_ossen");
        sim.RunThrough(3);
        Assert.False(sim.World.Provinces.InCrisis[ossen]);
        sim.StepDay();
        Assert.True(sim.World.Provinces.InCrisis[ossen]);
        sim.Orders.Enqueue(new RepairSubstationOrder(0, sub, RepairChoice.Spare));
        sim.RunThrough(18); // repaired at the start of day 18: 24 stable hours
        Assert.Equal((int)SubstationState.Online, sim.World.Substations.State[sub]);
        Assert.True(sim.World.Provinces.InCrisis[ossen]);
        sim.StepDay();      // day 19: 48 stable hours → clears
        Assert.False(sim.World.Provinces.InCrisis[ossen]);
    }

    // ---- Fab ----

    [Fact]
    public void PowerLossScrapsWorkInProcessOnceAndOutputRampsBackOver21Days()
    {
        var sim = TestContent.NewSim();
        Veyl.TripOssen(sim, 4, 2, "ossen_industrial");
        int fab = Veyl.Facility(sim, "tessera_fab_3");
        int sub = Veyl.Sub(sim, "ossen_industrial");
        var f = sim.World.Facilities;
        sim.RunThrough(3);
        var full = f.OutputToday[fab];
        sim.RunThrough(8);
        Assert.Equal(Fixed.FromInt(1200 * 42), f.WipScrapped[fab]); // once, not per dark day
        Assert.Equal(1, f.Interruptions[fab]);
        Assert.Equal(Fixed.Zero, f.OutputToday[fab]);

        sim.Orders.Enqueue(new RepairSubstationOrder(0, sub, RepairChoice.Spare));
        // Power returns at the start of the day the repair finishes: that day is the first full-power day, 1/21.
        while (sim.World.Substations.State[sub] != (int)SubstationState.Online) sim.StepDay();
        var first = f.OutputToday[fab].ToDoubleForUi() / full.ToDoubleForUi();
        Assert.InRange(first, 1.0 / 21 * 0.98, 1.0 / 21 * 1.06);
        for (int k = 0; k < 9; k++) sim.StepDay(); // day 10 of ramp
        Assert.InRange(f.OutputToday[fab].ToDoubleForUi() / full.ToDoubleForUi(), 10.0 / 21 * 0.98, 10.0 / 21 * 1.06);
        for (int k = 0; k < 11; k++) sim.StepDay();
        Assert.InRange(f.OutputToday[fab].ToDoubleForUi() / full.ToDoubleForUi(), 0.99, 1.06);
    }

    [Fact]
    public void YieldFollowsTheExperienceCurve()
    {
        var sim = TestContent.NewSim();
        int fab = Veyl.Facility(sim, "tessera_fab_3");
        sim.StepDay();
        var f = sim.World.Facilities;
        // X = 780 + starts/C; Y = 0.92 − 0.62 e^(−X/285) ≈ 0.880
        Assert.Equal("0.880", f.Yield[fab].ToString(3));
        Assert.True(f.Experience[fab] > Fixed.FromInt(780) && f.Experience[fab] < Fixed.FromInt(781));
    }

    [Fact]
    public void LosingEngineersCutsExperience()
    {
        // Spec: losing engineers multiplies X by (1 − 0.5 × share lost). Lose half of Ossen's → × 0.75.
        var sim = TestContent.NewSim();
        var w = sim.World;
        int fab = Veyl.Facility(sim, "tessera_fab_3");
        int ossen = Veyl.Province(sim, "kestria_east_ossen");
        sim.StepDay();
        var x0 = w.Facilities.Experience[fab];
        int pool = w.Labour.PoolId("process_engineers");
        w.Labour.Available.Init(w.Labour.Index(ossen, pool), Fixed.FromInt(450));
        sim.StepDay();
        var x1 = w.Facilities.Experience[fab];
        // × 0.75, then + today's starts ÷ capacity (about 0.5: labour is also at half)
        Assert.InRange((x1 - x0 * Fixed.Parse("0.75")).ToDoubleForUi(), 0.45, 0.52);
    }

    // ---- Countermeasures ----

    [Fact]
    public void CountermeasuresDecayWeeklyAndFirmwarePatchesHelp()
    {
        var sim = TestContent.NewSim();
        var d = sim.World.Designs;
        int design = sim.Content.Catalog.Design("fpv_strike_mk2");
        sim.StepDay(); // day 0 is a Monday: e = 0.85 × (1 − 0.03 × 1.0)
        Assert.Equal(Fixed.Parse("0.8245"), d.Effectiveness[design]);
        Assert.Equal(Fixed.Parse("0.9358"), d.Cap[design]); // 0.95 × (1 − 0.015) = 0.93575, ties round away

        sim.Orders.Enqueue(new FirmwarePatchOrder(0, design));
        sim.Orders.Enqueue(new FirmwarePatchOrder(0, design)); // bureau busy
        sim.RunThrough(6); // ordered on day 1, done 5 days later
        Assert.False(sim.AppliedOrders[1].Outcome.Accepted);
        Assert.Equal(Fixed.Parse("0.9245"), d.Effectiveness[design]);
        sim.RunThrough(7); // Monday: decays again
        Assert.Equal(Fixed.Parse("0.9245") * Fixed.Parse("0.97"), d.Effectiveness[design]);
    }

    [Fact]
    public void FirmwareCantExceedTheCap()
    {
        var sim = TestContent.NewSim();
        int design = sim.Content.Catalog.Design("fpv_strike_mk2");
        sim.World.Designs.Effectiveness.Init(design, Fixed.Parse("0.9"));
        sim.Orders.Enqueue(new FirmwarePatchOrder(0, design));
        sim.RunThrough(6);
        Assert.Equal(sim.World.Designs.Cap[design], sim.World.Designs.Effectiveness[design]);
    }

    [Fact]
    public void HardwareRevisionResetsToOneAndRetoolsTheLine()
    {
        var sim = TestContent.NewSim();
        int design = sim.Content.Catalog.Design("fpv_strike_mk2");
        int line = Veyl.Facility(sim, "ardent_drone_line");
        sim.Orders.Enqueue(new HardwareRevisionOrder(0, design));
        sim.RunThrough(29);
        var eBefore = sim.World.Facilities.Efficiency[line];
        sim.StepDay(); // day 30: revision lands
        Assert.Equal(Fixed.One, sim.World.Designs.Effectiveness[design]);
        Assert.Equal(Fixed.One, sim.World.Designs.Cap[design]);
        Assert.True(sim.World.Facilities.Efficiency[line] < eBefore * Fixed.Parse("0.81"));
    }

    // ---- Determinism with the economy running ----

    [Fact]
    public void ShockedScenarioIsDeterministicAndHourStepsMatchDaySteps()
    {
        static Simulation Setup()
        {
            var sim = TestContent.NewSim(20310303);
            Veyl.TripOssen(sim);
            var c = sim.Content.Catalog;
            sim.Events.Schedule(new ExportControlEvent(6, sim.World.Nations.IdOf("varan"), [c.Good("rare_earth_magnet"), c.Good("gallium")], true));
            return sim;
        }
        var a = Setup(); var b = Setup();
        a.RunThrough(90);
        while (!b.IsFinished) b.StepHour();
        Assert.Equal(a.StateHash(), b.StateHash());
    }
}
