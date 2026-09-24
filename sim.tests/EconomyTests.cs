using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Economy;
using Cascade.Sim.Scheduling;

namespace Cascade.Sim.Tests;

public class EconomyTests
{
    [Fact]
    public void StartingReadoutsMatchTheScenario()
    {
        // D-006: Magnet Days of Cover 45, legacy chip output 100%, nobody dark, no crisis.
        var sim = TestContent.NewSim();
        var snap = SimSnapshot.Of(sim);
        foreach (var (good, days) in sim.Scenario.InitialCoverDays)
            Assert.InRange(snap.Good(good).DaysOfCover!.Value, days.ToDoubleForUi() - 0.05, days.ToDoubleForUi() + 0.05);
        Assert.Equal(45.0, snap.Good("rare_earth_magnet").DaysOfCover!.Value, 1);
    }

    [Fact]
    public void QuietWorldStaysSteady()
    {
        var sim = TestContent.NewSim();
        sim.StepDay();
        double day0 = Veyl.ChipOutput(sim);
        sim.RunThrough(30);
        var snap = SimSnapshot.Of(sim);
        Assert.InRange(Veyl.ChipOutput(sim) / day0, 0.99, 1.05); // efficiency and yield creep up slowly
        Assert.All(snap.Provinces.Where(p => p.Owner == "kestria"), p => Assert.Equal(0, p.PeopleWithoutPower));
        Assert.All(snap.Provinces, p => Assert.False(p.InCrisis));
        Assert.All(snap.Goods.Where(g => g.DaysOfCover is not null), g => Assert.InRange(g.DaysOfCover!.Value, 14, 70));
    }

    [Fact]
    public void FabShareOfNationalCapacityIsForty()
    {
        // D-015 setup: Tessera Fab 3 is ~40% of national legacy output.
        var sim = TestContent.NewSim();
        sim.StepDay();
        var snap = SimSnapshot.Of(sim);
        var fab3 = snap.Facilities.Single(f => f.Id == "tessera_fab_3").OutputToday;
        Assert.InRange(fab3 / snap.Good("legacy_chip").ProducedToday, 0.39, 0.41);
    }

    [Fact]
    public void ProductionFollowsTheSpecFormula()
    {
        // Q = C · E · min(1, S/(rC)) · P · L · (1 − D), with E including the Just-in-Time bonus (D-031).
        var sim = TestContent.NewSim();
        int line = Veyl.Facility(sim, "ardent_drone_line");
        var f = sim.World.Facilities;
        var e = EconomyRules.EffectiveEfficiency(sim.Balance, f.Efficiency[line], sim.World.Nations.Doctrine[0]);
        var expected = f.Capacity[line] * e;
        sim.StepDay();
        Assert.Equal(expected, f.RunToday[line]);
    }

    [Fact]
    public void ShortInputCutsOutputProportionally()
    {
        // The drone line claims r × C = 300 controllers a day. Give it 150 and it runs at half: C × E × 0.5.
        // (Controllers aren't imported, so nothing else lands on day 0.)
        var sim = TestContent.NewSim();
        var w = sim.World;
        int line = Veyl.Facility(sim, "ardent_drone_line");
        int at = w.Stocks.At(Veyl.Province(sim, "kestria_interior"), Veyl.Good(sim, "flight_controller"));
        w.Stocks.Stock.Init(at, Fixed.FromInt(150));
        var e = EconomyRules.EffectiveEfficiency(sim.Balance, w.Facilities.Efficiency[line], w.Nations.Doctrine[0]);
        sim.StepDay();
        Assert.Equal((w.Facilities.Capacity[line] * e).Times(Fine.Parse("0.5")), w.Facilities.RunToday[line]);
    }

    [Fact]
    public void PriorityDecidesWhoGetsScarceInputs()
    {
        // Ossen East chips: the controller plant (military, Normal) and civil industry (Normal) share a shortage
        // proportionally, until the player moves the plant to Critical and it takes its full claim first.
        var sim = TestContent.NewSim();
        var w = sim.World;
        int plant = Veyl.Facility(sim, "ardent_controller_plant");
        int at = w.Stocks.At(Veyl.Province(sim, "kestria_east_ossen"), Veyl.Good(sim, "legacy_chip"));
        w.Stocks.Stock.Init(at, Fixed.FromInt(3_000));
        sim.StepDay();
        var shared = w.Facilities.RunToday[plant];
        w.Stocks.Stock.Init(at, Fixed.FromInt(3_000));
        sim.Orders.Enqueue(new SetPriorityOrder(0, PriorityTarget.Facility, plant, PriorityTier.Critical));
        sim.StepDay();
        Assert.True(shared < Fixed.FromInt(10));
        Assert.True(w.Facilities.RunToday[plant] > Fixed.FromInt(300));
    }

    [Fact]
    public void EfficiencyGrowsTowardMax()
    {
        var sim = TestContent.NewSim();
        int fab = Veyl.Facility(sim, "tessera_fab_3");
        var f = sim.World.Facilities;
        sim.StepDay();
        // D-049: E + g (E_max − E) u with u = output ÷ capacity: 0.94 + 0.01 × 0.06 × u
        var u = (f.RunToday[fab] / EconomyRules.EffectiveCapacity(sim.World, sim.Balance, fab)).ToFine();
        Assert.True(u > Fine.Zero && u < Fine.One);
        Assert.Equal(Fixed.Parse("0.94") + Fixed.Parse("0.0006").Times(u), f.Efficiency[fab]);
    }

    [Fact]
    public void EfficiencyDoesNotGrowOnADarkDay()
    {
        var sim = TestContent.NewScenario();
        int fab = Veyl.Facility(sim, "tessera_fab_3");
        sim.RunThrough(4); // the Day 4 attack darkens the industrial substation
        var f = sim.World.Facilities;
        var before = f.Efficiency[fab];
        sim.StepDay();
        Assert.Equal(Fixed.Zero, f.RunToday[fab]);
        Assert.Equal(before, f.Efficiency[fab]);
    }

    [Fact]
    public void SwitchingRecipeRetools()
    {
        var sim = TestContent.NewSim();
        int plant = Veyl.Facility(sim, "kestria_thermal_optics");
        sim.Orders.Enqueue(new SwitchRecipeOrder(0, plant, sim.Content.Catalog.Recipe("rf_module_assembly")));      // same line: × 0.8
        sim.StepDay();
        var afterSame = sim.World.Facilities.Efficiency[plant];
        Assert.Equal(Fixed.Parse("0.76"), (Fixed.Parse("0.95") * Fixed.Parse("0.8")));
        Assert.True(afterSame < Fixed.Parse("0.77"));
        sim.Orders.Enqueue(new SwitchRecipeOrder(0, plant, sim.Content.Catalog.Recipe("motor_assembly")));          // new line: × 0.3
        sim.StepDay();
        Assert.True(sim.World.Facilities.Efficiency[plant] < Fixed.Parse("0.24"));
    }

    [Fact]
    public void GoodsMoveBetweenProvinces()
    {
        // Ossen East makes more chips than it uses and ships the rest to the Interior; wafer blanks go the other way.
        var sim = TestContent.NewSim();
        sim.RunThrough(3);
        var w = sim.World;
        int ossen = Veyl.Province(sim, "kestria_east_ossen");
        int interior = Veyl.Province(sim, "kestria_interior");
        int chips = Veyl.Good(sim, "legacy_chip");
        int wafers = Veyl.Good(sim, "wafer_blank");
        Assert.Contains(w.Shipments.Items, s => s.From == ossen && s.To == interior && s.Good == chips);
        Assert.Contains(w.Shipments.Items, s => s.From == interior && s.To == ossen && s.Good == wafers);
    }

    [Fact]
    public void ExportControlsStopNewShipmentsButCargoAtSeaArrives()
    {
        var sim = TestContent.NewSim();
        var catalog = sim.Content.Catalog;
        int magnets = catalog.Good("rare_earth_magnet");
        sim.Events.Schedule(new ExportControlEvent(6, sim.World.Nations.IdOf("varan"), [magnets, catalog.Good("gallium")], true));
        sim.RunThrough(6);
        var before = SimSnapshot.Of(sim).Good("rare_earth_magnet").DaysOfCover!.Value;
        int route = Enumerable.Range(0, sim.World.Imports.Count).Single(r => sim.World.Imports.Good[r] == magnets && sim.World.Imports.Source[r] >= 0);
        Assert.True(sim.World.Imports.Blocked[route]);
        Assert.Equal(Fixed.Zero, sim.World.Imports.ShippedToday[route]);

        sim.RunThrough(25); // 19 more days: the last cargo shipped on day 5 lands on day 26
        var after = SimSnapshot.Of(sim).Good("rare_earth_magnet");
        Assert.InRange(before - after.DaysOfCover!.Value, 18, 20);   // cover counts down about a day per day
        Assert.True(after.Stock > 0);
        Assert.Contains(sim.World.Shipments.Items, s => s.Good == magnets && s.From < 0); // last few still at sea
    }

    [Fact]
    public void DaysOfCoverMatchesTheFormula()
    {
        // DoC = (S + T30) / EMA(b, 7), recomputed here from raw state.
        var sim = TestContent.NewSim();
        sim.RunThrough(9);
        var w = sim.World;
        int g = Veyl.Good(sim, "diesel");
        var s = Fixed.Zero; var ema = Fixed.Zero; var t = Fixed.Zero;
        for (int p = 0; p < w.Provinces.Count; p++)
        {
            if (w.Provinces.Owner[p] != 0) continue;
            s += w.Stocks.Stock[w.Stocks.At(p, g)];
            ema += w.Stocks.BurnEma[w.Stocks.At(p, g)];
        }
        foreach (var sh in w.Shipments.Items) if (sh.Good == g && sh.ArriveDay <= 9 + 30) t += sh.Qty;
        Assert.Equal((s + t) / ema, w.Stocks.DaysOfCover[w.Stocks.AtNation(0, g)]);
    }

    [Fact]
    public void DoctrineSetsTargetStock()
    {
        var b = TestContent.Repo.Balance;
        Assert.Equal(Fixed.FromInt(7), EconomyRules.TargetDays(b, Fine.Zero));
        Assert.Equal(Fixed.FromInt(90), EconomyRules.TargetDays(b, Fine.One));
        // Just-in-Time: +3% efficiency at j = 0, none at j = 1.
        Assert.Equal(Fixed.Parse("0.824"), EconomyRules.EffectiveEfficiency(b, Fixed.Parse("0.8"), Fine.Zero));
        Assert.Equal(Fixed.Parse("0.8"), EconomyRules.EffectiveEfficiency(b, Fixed.Parse("0.8"), Fine.One));
    }
}
