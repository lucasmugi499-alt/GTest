using Cascade.Sim.Content;
using Cascade.Sim.Core;

namespace Cascade.Sim.World;

/// <summary>
/// Base for one entity type's storage. IDs are dense 32-bit indexes 0..Count-1 in content order.
/// Static identity (keys, names) lives in plain arrays; everything that changes lives in <see cref="Column{T}"/>s.
/// </summary>
public abstract class Store : ICommittable, IStateHashable
{
    private readonly List<ICommittable> _columns = [];
    private readonly List<IStateHashable> _hashed = [];
    private readonly Dictionary<string, int> _byKey = new(StringComparer.Ordinal);

    /// <summary>Entity type name, e.g. "facility". Prefixes column names in the hash.</summary>
    public string TypeName { get; }
    public int Count { get; }
    public IReadOnlyList<string> Keys { get; }

    protected Store(string typeName, IReadOnlyList<string> keys)
    {
        TypeName = typeName;
        Count = keys.Count;
        Keys = keys;
        for (int i = 0; i < keys.Count; i++) _byKey.Add(keys[i], i);
    }

    public int IdOf(string key) =>
        _byKey.TryGetValue(key, out var id) ? id : throw new KeyNotFoundException($"No {TypeName} '{key}'.");

    public bool TryIdOf(string key, out int id) => _byKey.TryGetValue(key, out id);

    protected Column<T> Col<T>(string name) where T : unmanaged
    {
        var c = new Column<T>($"{TypeName}.{name}", Count);
        _columns.Add(c);
        _hashed.Add(c);
        return c;
    }

    public void Commit()
    {
        foreach (var c in _columns) c.Commit();
    }

    public void HashInto(StateHasher h)
    {
        h.Section(TypeName).Add(Count);
        for (int i = 0; i < Count; i++) h.Add(Keys[i]);
        foreach (var c in _hashed) c.HashInto(h);
    }
}

public sealed class NationStore : Store
{
    public IReadOnlyList<string> Names { get; }
    public int Player { get; }

    /// <summary>Stockpile doctrine j: 0 Just-in-Time to 1 Just-in-Case (spec Days of Cover and doctrine).</summary>
    public Column<Fine> Doctrine { get; }
    public Column<int> SpareTransformers { get; }
    public Column<int> MobileSubstations { get; }
    /// <summary>0 to 4 (spec Mobilization levels). Set from M3; read by priority tiers now.</summary>
    public Column<int> MobilizationLevel { get; }
    /// <summary>Day the Design Bureau is free again (spec Countermeasure decay: patches take bureau time).</summary>
    public Column<int> BureauBusyUntil { get; }

    public NationStore(ScenarioDef s) : base("nation", s.Nations.Select(n => n.Id).ToList())
    {
        Names = s.Nations.Select(n => n.Name).ToList();
        Player = IdOf(s.Player);
        Doctrine = Col<Fine>("doctrine");
        SpareTransformers = Col<int>("spare_transformers");
        MobileSubstations = Col<int>("mobile_substations");
        MobilizationLevel = Col<int>("mobilization_level");
        BureauBusyUntil = Col<int>("bureau_busy_until");
        for (int i = 0; i < Count; i++)
        {
            var n = s.Nations[i];
            Doctrine.Init(i, n.Doctrine);
            SpareTransformers.Init(i, n.SpareTransformers);
            MobileSubstations.Init(i, n.MobileSubstations);
            BureauBusyUntil.Init(i, -1);
        }
    }
}

public sealed class ProvinceStore : Store
{
    public IReadOnlyList<string> Names { get; }
    public IReadOnlyList<DetailLevel> Detail { get; }

    // Spec Data model: Province.
    public Column<int> Owner { get; }
    public Column<int> Controller { get; }
    public Column<Fixed> Corruption { get; }

    // Spec Crisis sub-ticks: the crisis flag and the count of stable hours toward clearing it.
    public Column<bool> InCrisis { get; }
    public Column<int> StableHours { get; }

    // Spec Blackout clocks: refuelling backup generators needs road access; trucks limit how fast.
    public Column<bool> RoadAccess { get; }
    public Column<Fixed> RefuelTonnesPerHour { get; }

    // Spec Black start: a collapsed region restores a share of its load each day.
    public Column<bool> Collapsed { get; }
    public Column<Fine> RestoreLevel { get; }

    public ProvinceStore(ScenarioDef s, NationStore nations) : base("province", s.Provinces.Select(p => p.Id).ToList())
    {
        Names = s.Provinces.Select(p => p.Name).ToList();
        Detail = s.Provinces.Select(p => p.Detail).ToList();
        Owner = Col<int>("owner");
        Controller = Col<int>("controller");
        Corruption = Col<Fixed>("corruption");
        InCrisis = Col<bool>("in_crisis");
        StableHours = Col<int>("stable_hours");
        RoadAccess = Col<bool>("road_access");
        RefuelTonnesPerHour = Col<Fixed>("refuel_tph");
        Collapsed = Col<bool>("collapsed");
        RestoreLevel = Col<Fine>("restore_level");

        for (int i = 0; i < Count; i++)
        {
            var def = s.Provinces[i];
            int owner = nations.IdOf(def.Owner);
            Owner.Init(i, owner);
            Controller.Init(i, owner);
            Corruption.Init(i, def.Corruption);
            RoadAccess.Init(i, def.RoadAccess);
            RefuelTonnesPerHour.Init(i, def.RefuelTonnesPerHour);
            RestoreLevel.Init(i, Fine.One);
        }
    }
}
