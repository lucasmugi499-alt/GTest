using Cascade.Sim.World;

namespace Cascade.Sim.Scheduling;

/// <summary>One of the twelve daily phases. Reads committed state, writes next-state; the scheduler commits after.</summary>
public interface IPhase
{
    PhaseId Id { get; }
    void RunDaily(TickContext ctx);
}

/// <summary>
/// A phase that also runs once per hour in crisis provinces, before the daily pass (spec Crisis sub-ticks:
/// phases 2, 5 (blackout clocks only), 7 and 8; plus orders and timed events, D-019).
/// </summary>
public interface IHourlyPhase : IPhase
{
    void RunHourly(TickContext ctx);
}

/// <summary>A weekly (Mondays) or monthly (the 1st) system, run after phase 11.</summary>
public interface IPeriodicSystem
{
    SystemId Id { get; }
    void Run(TickContext ctx);
}

/// <summary>Tells the scheduler a province should be (or stay) in Crisis Time.</summary>
public interface ICrisisSignal
{
    string Name { get; }
    bool IsUnstable(SimWorld world, int province);
}

/// <summary>Keeps a phase's slot in the pipeline until its milestone fills it in.</summary>
public sealed class StubPhase(PhaseId id) : IPhase
{
    public PhaseId Id { get; } = id;
    public void RunDaily(TickContext ctx) { }
}

/// <summary>Stub for an hourly-capable phase: keeps both its daily and hourly slots.</summary>
public sealed class StubHourlyPhase(PhaseId id) : IHourlyPhase
{
    public PhaseId Id { get; } = id;
    public void RunDaily(TickContext ctx) { }
    public void RunHourly(TickContext ctx) { }
}

public sealed class StubSystem(SystemId id) : IPeriodicSystem
{
    public SystemId Id { get; } = id;
    public void Run(TickContext ctx) { }
}

/// <summary>Phase 0: applies queued orders in arrival order. In a crisis day it also runs every hour.</summary>
public sealed class OrdersPhase(OrderQueue queue) : IHourlyPhase
{
    public PhaseId Id => PhaseId.Orders;
    public List<AppliedOrder> Applied { get; } = [];

    public void RunDaily(TickContext ctx) => ApplyAll(ctx);
    public void RunHourly(TickContext ctx) => ApplyAll(ctx);

    private void ApplyAll(TickContext ctx)
    {
        foreach (var order in queue.Drain())
        {
            order.Apply(ctx);
            Applied.Add(new AppliedOrder(ctx.Day, ctx.Hour, order));
        }
    }
}

/// <summary>An order and when it took effect, for replays and the event log.</summary>
public sealed record AppliedOrder(int Day, int Hour, Order Order);

/// <summary>Phase 1: applies scheduled events that are due. Timed events apply at their hour in a crisis day.</summary>
public sealed class ScheduledEventsPhase : IHourlyPhase
{
    public PhaseId Id => PhaseId.ScheduledEvents;

    public void RunDaily(TickContext ctx)
    {
        foreach (var e in ctx.Events.TakeDue(ctx.Day, hour: null)) e.Apply(ctx);
    }

    public void RunHourly(TickContext ctx)
    {
        foreach (var e in ctx.Events.TakeDue(ctx.Day, ctx.Hour)) e.Apply(ctx);
    }
}
