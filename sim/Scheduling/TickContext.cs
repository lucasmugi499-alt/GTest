using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.World;

namespace Cascade.Sim.Scheduling;

/// <summary>Everything a phase or system may use during one pass: state, rules, time and its random streams.</summary>
public sealed class TickContext
{
    public required ContentSet Content { get; init; }
    public required SimWorld World { get; init; }
    public required Balance Balance { get; init; }
    public required Calendar Calendar { get; init; }
    public required ulong Seed { get; init; }
    public required int Day { get; init; }
    /// <summary>0–23 during a crisis hour, -1 in the daily, weekly and monthly passes.</summary>
    public required int Hour { get; init; }
    public required TickSlot Slot { get; init; }
    /// <summary>Provinces in Crisis Time today, in ID order. Hourly phases only work on these.</summary>
    public required IReadOnlyList<int> CrisisProvinces { get; init; }
    public required EventQueue Events { get; init; }
    /// <summary>The narrative engine (blackboard, compiled effects), for phases and orders that need it.</summary>
    public required Narrative.NarrativeEngine Narrative { get; init; }

    public bool IsHourly => Hour >= 0;

    /// <summary>The random stream for one system acting on one entity in this pass (spec: counter-based randomness).</summary>
    public RngStream Rng(SystemId system, ulong entity) =>
        new(Seed, Core.Rng.TickKey(Day, Slot), (ulong)system, entity);
}
