using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Tests;

internal static class TestContent
{
    private static readonly Lazy<ContentSet> Lazy = new(() => ContentSet.Load(ContentSet.FindContentDir()));
    public static ContentSet Repo => Lazy.Value;

    public static Simulation NewSim(ulong seed = 42, SimulationOptions? options = null) => new(Repo, seed, options);

    /// <summary>A sim whose Production slot runs <see cref="ProbePhase"/>, so state actually evolves with the RNG.</summary>
    public static Simulation NewProbeSim(ulong seed)
    {
        var o = new SimulationOptions();
        o.Phases[PhaseId.Production] = new ProbePhase();
        return new Simulation(Repo, seed, o);
    }
}

/// <summary>Random walk on province corruption: stands in for real systems until M2 so the hash has something to track.</summary>
internal sealed class ProbePhase : IPhase
{
    public PhaseId Id => PhaseId.Production;

    public void RunDaily(TickContext ctx)
    {
        var p = ctx.World.Provinces;
        for (int i = 0; i < p.Count; i++)
        {
            var rng = ctx.Rng(SystemId.TestProbe, EntityRef.Of(EntityKind.Province, i));
            var step = Fixed.FromInt(rng.NextInt(-1, 2)) + Fixed.FromRaw(rng.NextInt(-50, 51));
            p.Corruption.Set(i, Fixed.Clamp(p.Corruption[i] + step, Fixed.Zero, Fixed.Hundred));
        }
    }
}

/// <summary>Records every call, so tests can check the order phases and systems run in.</summary>
internal sealed class Trace
{
    public List<string> Calls { get; } = [];
}

internal sealed class TracePhase(PhaseId id, Trace trace) : IPhase
{
    public PhaseId Id { get; } = id;
    public void RunDaily(TickContext ctx) => trace.Calls.Add($"d{ctx.Day}:{(int)Id}");
}

internal sealed class TraceHourlyPhase(PhaseId id, Trace trace) : IHourlyPhase
{
    public PhaseId Id { get; } = id;
    public int HourlyRuns { get; private set; }
    public List<int> LastCrisisProvinces { get; } = [];
    public void RunDaily(TickContext ctx) => trace.Calls.Add($"d{ctx.Day}:{(int)Id}");
    public void RunHourly(TickContext ctx)
    {
        HourlyRuns++;
        LastCrisisProvinces.Clear();
        LastCrisisProvinces.AddRange(ctx.CrisisProvinces);
        trace.Calls.Add($"d{ctx.Day}h{ctx.Hour}:{(int)Id}");
    }
}

internal sealed class TraceSystem(SystemId id, Trace trace) : IPeriodicSystem
{
    public SystemId Id { get; } = id;
    public void Run(TickContext ctx) => trace.Calls.Add($"d{ctx.Day}:{Id}");
}

internal sealed class SetCorruptionOrder(int issuer, int province, Fixed value) : Order(issuer)
{
    public override string Kind => "test.set_corruption";
    public override void Apply(TickContext ctx) => ctx.World.Provinces.Corruption.Set(province, value);
    protected override void HashFields(StateHasher h) => h.Add(province).Add(value);
}

internal sealed class MarkEvent(int day, int hour, int province, List<string> log) : SimEvent(day, hour, province)
{
    public override string Kind => "test.mark";
    public override void Apply(TickContext ctx) => log.Add($"d{ctx.Day}h{ctx.Hour}:ev{Day}/{Hour}");
    protected override void HashFields(StateHasher h) { }
}

/// <summary>Unstable while the province's corruption is at or above 90 — a test stand-in for "grid below 70%".</summary>
internal sealed class HighCorruptionSignal : ICrisisSignal
{
    public string Name => "test.high_corruption";
    public bool IsUnstable(SimWorld world, int province) => world.Provinces.Corruption[province] >= Fixed.FromInt(90);
}
