using Cascade.Sim.Content;
using Cascade.Sim.Core;

namespace Cascade.Sim.World;

/// <summary>All live sim state. Only phases (via the scheduler) write to it; the UI reads snapshots.</summary>
public sealed class SimWorld : ICommittable, IStateHashable
{
    public NationStore Nations { get; }
    public ProvinceStore Provinces { get; }

    private readonly Store[] _stores;

    public SimWorld(ScenarioDef scenario)
    {
        Nations = new NationStore(scenario);
        Provinces = new ProvinceStore(scenario, Nations);
        // Fixed order: this is the order stores commit and hash in.
        _stores = [Nations, Provinces];
    }

    public void Commit()
    {
        foreach (var s in _stores) s.Commit();
    }

    public void HashInto(StateHasher h)
    {
        foreach (var s in _stores) s.HashInto(h);
    }
}
