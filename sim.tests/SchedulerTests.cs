using Cascade.Sim.Core;
using Cascade.Sim.Scheduling;

namespace Cascade.Sim.Tests;

public class SchedulerTests
{
    private static (Simulation sim, Trace trace) TracedSim(Action<SimulationOptions>? tweak = null)
    {
        var trace = new Trace();
        var o = new SimulationOptions();
        for (int i = 0; i < 12; i++)
        {
            var id = (PhaseId)i;
            if (id is PhaseId.Orders or PhaseId.ScheduledEvents) continue; // keep the built-ins
            o.Phases[id] = Simulation.HourlyPhases.Contains(id) ? new TraceHourlyPhase(id, trace) : new TracePhase(id, trace);
        }
        o.Weekly = [new TraceSystem(SystemId.WeeklyMarkets, trace), new TraceSystem(SystemId.WeeklyForecast, trace)];
        o.Monthly = [new TraceSystem(SystemId.MonthlyResearch, trace)];
        tweak?.Invoke(o);
        return (TestContent.NewSim(options: o), trace);
    }

    [Fact]
    public void DailyPhasesRunInSpecOrderThenWeekly()
    {
        var (sim, trace) = TracedSim();
        sim.StepDay(); // Day 0 is a Monday (D-004)
        Assert.Equal(
            ["d0:2", "d0:3", "d0:4", "d0:5", "d0:6", "d0:7", "d0:8", "d0:9", "d0:10", "d0:11",
             "d0:WeeklyMarkets", "d0:WeeklyForecast"],
            trace.Calls);
        Assert.Equal(1, sim.Day);
    }

    [Fact]
    public void WeeklyOnMondaysMonthlyOnTheFirst()
    {
        var (sim, trace) = TracedSim();
        sim.RunThrough(40);
        var weeklyDays = trace.Calls.Where(c => c.EndsWith("WeeklyMarkets")).Select(c => c.Split(':')[0]).ToList();
        var monthlyDays = trace.Calls.Where(c => c.EndsWith("MonthlyResearch")).Select(c => c.Split(':')[0]).ToList();
        Assert.Equal(["d0", "d7", "d14", "d21", "d28", "d35"], weeklyDays);
        Assert.Equal(["d29"], monthlyDays); // 1 April 2031 is day 29
        Assert.Equal(new DateOnly(2031, 4, 1), sim.Calendar.DateOf(29));
    }

    [Fact]
    public void NormalDaysHaveNoCrisisHours()
    {
        var (sim, trace) = TracedSim();
        sim.RunThrough(10);
        Assert.DoesNotContain(trace.Calls, c => c.Contains('h'));
        Assert.False(sim.World.Provinces.InCrisis[0]);
    }

    [Fact]
    public void TimedEventPutsProvinceInCrisisTimeAndFiresAtItsHour()
    {
        var log = new List<string>();
        var (sim, trace) = TracedSim();
        int ossen = sim.World.Provinces.IdOf("kestria_east_ossen");
        sim.Events.Schedule(new MarkEvent(day: 4, hour: 2, province: ossen, log));

        sim.RunThrough(3);
        Assert.Empty(log);

        // Day 4: hours 0 and 1 pass quietly, the event fires in hour 2 (the 02:14 attack lands in the 02:00 hour).
        Assert.Equal(StepResult.HourAdvanced, sim.StepHour());
        Assert.True(sim.IsCrisisDay);
        Assert.Equal([ossen], sim.CrisisProvinces);
        sim.StepHour();
        Assert.Empty(log);
        sim.StepHour();
        Assert.Equal(["d4h2:ev4/2"], log);

        sim.StepDay();
        // Hourly phases (2, 5, 7, 8, and the narrative) ran 24 times each, before the daily pass.
        int traced = Simulation.HourlyPhases.Length - 2; // orders and events are the real built-ins, not traced
        var day4 = trace.Calls.Where(c => c.StartsWith("d4")).ToList();
        Assert.Equal(24 * traced, day4.Count(c => c.StartsWith("d4h")));
        Assert.Equal("d4h0:2", day4[0]);
        Assert.Equal("d4:2", day4[24 * traced]);
    }

    [Fact]
    public void CrisisClearsAfter48StableHours()
    {
        var (sim, _) = TracedSim();
        int veyl = sim.World.Provinces.IdOf("veyl");
        sim.Events.Schedule(new MarkEvent(day: 2, hour: 5, province: veyl, []));

        sim.RunThrough(1);
        Assert.False(sim.World.Provinces.InCrisis[veyl]);
        sim.StepDay(); // day 2: 24 stable hours
        Assert.True(sim.World.Provinces.InCrisis[veyl]);
        sim.StepDay(); // day 3: 48 stable hours → clears at end of day
        Assert.False(sim.World.Provinces.InCrisis[veyl]);
        sim.StepDay(); // day 4: normal again
        Assert.False(sim.IsCrisisDay);
    }

    [Fact]
    public void UnstableSignalHoldsCrisisUntilStable()
    {
        var (sim, _) = TracedSim(o => o.CrisisSignals.Add(new HighCorruptionSignal()));
        int veyl = sim.World.Provinces.IdOf("veyl");
        int kestria = sim.World.Nations.Player;

        sim.Orders.Enqueue(new SetCorruptionOrder(kestria, veyl, Fixed.FromInt(95)));
        sim.StepDay(); // day 0: order applies; the flag is decided at the start of the next day
        for (int d = 1; d <= 5; d++)
        {
            sim.StepDay();
            Assert.True(sim.World.Provinces.InCrisis[veyl], $"day {d}");
            Assert.Equal(0, sim.World.Provinces.StableHours[veyl]);
        }

        sim.Orders.Enqueue(new SetCorruptionOrder(kestria, veyl, Fixed.FromInt(20)));
        sim.StepHour(); // day 6 hour 0: order applies in the hourly orders pass, the hour counts as stable
        Assert.Equal(1, sim.World.Provinces.StableHours[veyl]);
        sim.StepDay();  // day 6 ends with 24 stable hours
        Assert.True(sim.World.Provinces.InCrisis[veyl]);
        sim.StepDay();  // day 7 reaches 48
        Assert.False(sim.World.Provinces.InCrisis[veyl]);
    }

    [Fact]
    public void WritesAreInvisibleUntilThePhaseCommits()
    {
        var c = TestContent.NewSim().World.Provinces.Corruption;
        var before = c[0];
        c.Set(0, Fixed.FromInt(77));
        Assert.Equal(before, c[0]);                    // the phase still reads the committed value
        Assert.Equal(Fixed.FromInt(77), c.Pending(0));  // but can see its own pending write
        c.Commit();
        Assert.Equal(Fixed.FromInt(77), c[0]);
    }

    [Fact]
    public void OrdersApplyInArrivalOrderInPhaseZero()
    {
        var sim = TestContent.NewSim();
        int kestria = sim.World.Nations.Player;
        sim.Orders.Enqueue(new SetCorruptionOrder(kestria, 0, Fixed.FromInt(10)));
        sim.Orders.Enqueue(new SetCorruptionOrder(kestria, 0, Fixed.FromInt(20)));
        sim.StepDay();
        Assert.Equal(Fixed.FromInt(20), sim.World.Provinces.Corruption[0]);
        Assert.Equal([0L, 1L], sim.AppliedOrders.Select(a => a.Order.Seq));
        Assert.All(sim.AppliedOrders, a => Assert.Equal(0, a.Day));
    }

    [Fact]
    public void UntimedEventsApplyInTheDailyPass()
    {
        var log = new List<string>();
        var sim = TestContent.NewSim();
        sim.Events.Schedule(new MarkEvent(day: 3, hour: -1, province: -1, log));
        sim.RunThrough(3);
        Assert.Equal(["d3h-1:ev3/-1"], log);
        Assert.Equal(0, sim.Events.Count);
    }

    [Fact]
    public void OnlySpecHourlyPhasesMayRunHourly()
    {
        var o = new SimulationOptions();
        o.Phases[PhaseId.Production] = new StubHourlyPhaseForProduction();
        Assert.Throws<ArgumentException>(() => TestContent.NewSim(options: o));
    }

    [Fact]
    public void ScenarioEndsAfterDay90()
    {
        var sim = TestContent.NewSim();
        sim.RunThrough(90);
        Assert.True(sim.IsFinished);
        Assert.Throws<InvalidOperationException>(() => sim.StepHour());
    }

    private sealed class StubHourlyPhaseForProduction : IHourlyPhase
    {
        public PhaseId Id => PhaseId.Production;
        public void RunDaily(TickContext ctx) { }
        public void RunHourly(TickContext ctx) { }
    }
}
