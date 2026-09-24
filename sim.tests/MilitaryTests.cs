using Cascade.Sim.Conflict;
using Cascade.Sim.Core;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Tests;

public class MilitaryTests
{
    [Fact]
    public void SpecWorkedExample_MarenAgainstDravek()
    {
        // Spec Worked example, Day 3 of the Maren Gambit.
        var fifty = Fixed.FromInt(50);
        var maren = Military.CombatPower(Fixed.FromInt(4000), Fixed.FromInt(70), fifty, Fine.Parse("0.9"), Fixed.Parse("1.1"));
        var dravek = Military.CombatPower(Fixed.FromInt(10000), Fixed.FromInt(35), fifty, Fine.Parse("0.6"), Fixed.Parse("0.8"));
        Assert.Equal(Fixed.FromInt(5544), maren);
        Assert.Equal(Fixed.FromInt(3360), dravek);

        var pMaren = Military.Detection(Fixed.FromInt(75), Fixed.FromInt(40));
        var pDravek = Military.Detection(Fixed.FromInt(40), Fixed.FromInt(60));
        Assert.Equal("0.97", pMaren.ToFixed().ToString(2));
        Assert.Equal("0.12", pDravek.ToFixed().ToString(2));

        // "Force ratio ρ is about 8.9"
        var rho = Military.ForceRatio(maren, pMaren, dravek, pDravek, Fixed.Parse("1.5"));
        Assert.InRange(rho.ToDoubleForUi(), 8.8, 9.0);

        // "Dravek loses about 1.3% of strength a day, roughly 128 soldiers; Maren loses about 2."
        var k = Fine.Parse("0.01");
        var dravekLoss = Military.LossShare(k, maren, pMaren, dravek, Fine.Parse("0.2"));
        var marenLoss = Military.LossShare(k, dravek, pDravek, maren, Fine.Parse("0.4"));
        Assert.InRange(dravekLoss.ToDoubleForUi(), 0.0125, 0.0131);
        Assert.InRange(Fixed.FromInt(10000).Times(dravekLoss).ToDoubleForUi(), 125, 131);
        Assert.InRange(Fixed.FromInt(4000).Times(marenLoss).ToDoubleForUi(), 1.5, 2.5);

        // "Maren advances near its 3 km per day maximum."
        var v = Military.Advance(Fixed.FromInt(3), rho, Fixed.Parse("1.5"), Fixed.One);
        Assert.InRange(v.ToDoubleForUi(), 2.99, 3.0);
        Assert.Equal(Fixed.Zero, Military.Advance(Fixed.FromInt(3), Fixed.Parse("1.4"), Fixed.Parse("1.5"), Fixed.One));

        // "If Dravek's jamming cut Maren's recon from 75 to 45, Maren's detection falls to 0.62 and Dravek's daily losses
        // fall by about a third."
        var jammed = Military.Detection(Fixed.FromInt(45), Fixed.FromInt(40));
        Assert.Equal("0.62", jammed.ToFixed().ToString(2));
        var lossJammed = Military.LossShare(k, maren, jammed, dravek, Fine.Parse("0.2"));
        Assert.InRange(1 - lossJammed.ToDoubleForUi() / dravekLoss.ToDoubleForUi(), 0.33, 0.39);
    }

    [Fact]
    public void BorderClashKillsTwoAndStartsRallyAndLegitimacy()
    {
        var sim = TestContent.NewScenario();
        sim.RunThrough(3);
        var w = sim.World;
        int kestria = w.Nations.Player;
        int border = w.Brigades.IdOf("kestria_border_bde");
        Assert.Equal(Fixed.FromInt(2), w.Brigades.Killed[border]);
        Assert.Equal(Fixed.FromInt(3000 - 8), w.Brigades.Strength[border]); // 2 killed, 6 wounded
        Assert.Equal(Fixed.FromInt(20), w.Politics.Rally[kestria]);
        Assert.Equal(Fixed.FromInt(70), w.Politics.Legitimacy[kestria]);
        Assert.Equal(4, sim.Balance.Escalation.RungOf(Escalation.Meter(w, 0, 1)));
    }

    [Fact]
    public void WoundedReturnAfter60Days()
    {
        var sim = TestContent.NewScenario();
        sim.RunThrough(62);
        int border = sim.World.Brigades.IdOf("kestria_border_bde");
        Assert.Equal(Fixed.FromInt(3000 - 8), sim.World.Brigades.Strength[border]);
        sim.StepDay(); // day 63: half of the 6 wounded return
        Assert.Equal(Fixed.FromInt(3000 - 5), sim.World.Brigades.Strength[border]);
    }

    [Fact]
    public void PartialMobilizationTakes14DaysAndDrainsSkilledReservists()
    {
        var sim = TestContent.NewSim();
        var w = sim.World;
        int ossen = Veyl.Province(sim, "kestria_east_ossen");
        int linemen = w.Labour.PoolId("grid_linemen");
        sim.Orders.Enqueue(new SetMobilizationOrder(0, 2));
        sim.RunThrough(13);
        Assert.Equal(0, w.Nations.MobilizationLevel[0]);
        Assert.Equal(Fixed.FromInt(1200), w.Labour.Available[w.Labour.Index(ossen, linemen)]);
        sim.StepDay(); // day 14
        Assert.Equal(2, w.Nations.MobilizationLevel[0]);
        Assert.Equal(Fixed.FromInt(700), w.Labour.Available[w.Labour.Index(ossen, linemen)]);
        int reserve = Enumerable.Range(0, w.Brigades.Count).Single(b => w.Brigades.Reserve[b] && w.Brigades.Nation[b] == 0);
        Assert.Equal(Fixed.FromInt(10_000), w.Brigades.Strength[reserve]); // 40,000 × 0.25 at the front
    }

    [Fact]
    public void ExemptingLinemenKeepsThemHome()
    {
        var sim = TestContent.NewSim();
        var w = sim.World;
        int ossen = Veyl.Province(sim, "kestria_east_ossen");
        int linemen = w.Labour.PoolId("grid_linemen");
        sim.Orders.Enqueue(new ExemptPoolOrder(0, "grid_linemen", true));
        sim.Orders.Enqueue(new SetMobilizationOrder(0, 2));
        sim.RunThrough(14);
        Assert.Equal(Fixed.FromInt(1200), w.Labour.Available[w.Labour.Index(ossen, linemen)]);
        int technicians = w.Labour.PoolId("technicians");
        Assert.Equal(Fixed.FromInt(2600 - 700), w.Labour.Available[w.Labour.Index(ossen, technicians)]);
        int reserve = Enumerable.Range(0, w.Brigades.Count).Single(b => w.Brigades.Reserve[b] && w.Brigades.Nation[b] == 0);
        Assert.Equal(Fixed.FromInt((40_000 - 800) / 4), w.Brigades.Strength[reserve]);
    }

    [Fact]
    public void MobilizationRaisesMilitaryOutputAndStepsDownSlowly()
    {
        var sim = TestContent.NewSim();
        var w = sim.World;
        int line = Veyl.Facility(sim, "ardent_drone_line");
        sim.StepDay();
        var before = w.Facilities.RunToday[line];
        sim.Orders.Enqueue(new SetMobilizationOrder(0, 1));
        sim.RunThrough(9); // level changes in phase 6 on day 8, after that day's production
        Assert.Equal(1, w.Nations.MobilizationLevel[0]);
        Assert.InRange((w.Facilities.RunToday[line] / before).ToDoubleForUi(), 1.19, 1.22); // ×1.2
        sim.Orders.Enqueue(new SetMobilizationOrder(0, 0)); // applies day 10; one level down takes 30 days
        sim.RunThrough(39);
        Assert.Equal(1, w.Nations.MobilizationLevel[0]);
        sim.StepDay(); // 30 days after the order
        Assert.Equal(0, w.Nations.MobilizationLevel[0]);
    }

    [Fact]
    public void AttackingAtVeylFightsWithTheSpecFormulas()
    {
        var sim = TestContent.NewSim();
        var w = sim.World;
        // Give the border brigade drones to fly.
        int veyl = Veyl.Province(sim, "veyl");
        w.Stocks.Stock.Init(w.Stocks.At(veyl, Veyl.Good(sim, "fpv_strike_drone")), Fixed.FromInt(5000));
        sim.Orders.Enqueue(new FrontAttackOrder(0));
        sim.StepDay();
        var f = w.Front;
        Assert.True(f.ActiveUntil[0] >= 0);
        Assert.True(f.DroneDensity[0] > Fixed.Zero && f.DroneDensity[1] > Fixed.Zero);
        Assert.True(f.Detection[0] > Fine.Zero && f.Detection[1] > Fine.Zero);
        int kb = w.Brigades.IdOf("kestria_border_bde"), vb = w.Brigades.IdOf("varan_4th_mech");
        Assert.True(w.Brigades.Strength[kb] < Fixed.FromInt(3000));
        Assert.True(w.Brigades.Strength[vb] < Fixed.FromInt(4000));
        // Attacking is an escalation: a border clash with deaths lifts the meter to at least 45.
        Assert.True(Escalation.Meter(w, 0, 1) >= Fixed.FromInt(45));
    }

    [Fact]
    public void QuietFrontHasNoLosses()
    {
        var sim = TestContent.NewSim();
        sim.RunThrough(20);
        Assert.Equal(Fixed.FromInt(3000), sim.World.Brigades.Strength[sim.World.Brigades.IdOf("kestria_border_bde")]);
    }
}
