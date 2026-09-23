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

    public string Kind { get; }
    public int Count { get; }
    public IReadOnlyList<string> Keys { get; }

    protected Store(string kind, IReadOnlyList<string> keys)
    {
        Kind = kind;
        Count = keys.Count;
        Keys = keys;
        for (int i = 0; i < keys.Count; i++) _byKey.Add(keys[i], i);
    }

    public int IdOf(string key) =>
        _byKey.TryGetValue(key, out var id) ? id : throw new KeyNotFoundException($"No {Kind} '{key}'.");

    public bool TryIdOf(string key, out int id) => _byKey.TryGetValue(key, out id);

    protected Column<T> Col<T>(string name) where T : unmanaged
    {
        var c = new Column<T>($"{Kind}.{name}", Count);
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
        h.Section(Kind).Add(Count);
        for (int i = 0; i < Count; i++) h.Add(Keys[i]);
        foreach (var c in _hashed) c.HashInto(h);
    }
}

public sealed class NationStore : Store
{
    public IReadOnlyList<string> Names { get; }
    public int Player { get; }

    public NationStore(ScenarioDef s) : base("nation", s.Nations.Select(n => n.Id).ToList())
    {
        Names = s.Nations.Select(n => n.Name).ToList();
        Player = IdOf(s.Player);
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

    public ProvinceStore(ScenarioDef s, NationStore nations) : base("province", s.Provinces.Select(p => p.Id).ToList())
    {
        Names = s.Provinces.Select(p => p.Name).ToList();
        Detail = s.Provinces.Select(p => p.Detail).ToList();
        Owner = Col<int>("owner");
        Controller = Col<int>("controller");
        Corruption = Col<Fixed>("corruption");
        InCrisis = Col<bool>("in_crisis");
        StableHours = Col<int>("stable_hours");

        for (int i = 0; i < Count; i++)
        {
            var def = s.Provinces[i];
            int owner = nations.IdOf(def.Owner);
            Owner.Init(i, owner);
            Controller.Init(i, owner);
            Corruption.Init(i, def.Corruption);
        }
    }
}
