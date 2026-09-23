using Cascade.Sim.Scheduling;

namespace Cascade.Sim;

/// <summary>
/// A read-only copy of what the UI may show. The game and tools read these and send orders;
/// they never touch sim state directly. Fields grow with each milestone.
/// </summary>
public sealed record SimSnapshot(
    int Day,
    int Hour,
    string DateText,
    bool IsCrisisDay,
    string StateHash,
    IReadOnlyList<ProvinceView> Provinces)
{
    public static SimSnapshot Of(Simulation sim)
    {
        var p = sim.World.Provinces;
        var n = sim.World.Nations;
        var provinces = new ProvinceView[p.Count];
        for (int i = 0; i < p.Count; i++)
        {
            provinces[i] = new ProvinceView(
                p.Keys[i],
                p.Names[i],
                n.Keys[p.Owner[i]],
                p.InCrisis[i],
                p.Corruption[i].ToDoubleForUi());
        }
        return new SimSnapshot(
            sim.Day,
            sim.Hour,
            sim.Calendar.Describe(sim.Day),
            sim.IsCrisisDay,
            Core.StateHasher.Format(sim.StateHash()),
            provinces);
    }
}

public sealed record ProvinceView(string Id, string Name, string Owner, bool InCrisis, double Corruption);
