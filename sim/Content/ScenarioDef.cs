using Cascade.Sim.Core;

namespace Cascade.Sim.Content;

public enum DetailLevel : byte
{
    /// <summary>Simulated facility by facility (Ossen East, Veyl).</summary>
    Full = 0,
    /// <summary>One aggregate block (Kestria Interior, Varan). D-005.</summary>
    Block = 1,
}

public sealed record NationDef(string Id, string Name);

public sealed record ProvinceDef(string Id, string Name, string Owner, DetailLevel Detail, Fixed Corruption);

/// <summary>A scenario's starting world. Entity IDs are assigned in file order (D-022).</summary>
public sealed record ScenarioDef(
    string Id,
    string Name,
    DateOnly StartDate,
    int LastDay,
    ulong DefaultSeed,
    string Player,
    IReadOnlyList<NationDef> Nations,
    IReadOnlyList<ProvinceDef> Provinces)
{
    public static ScenarioDef Read(ContentNode n)
    {
        var nations = n.List("nations").Select(x => new NationDef(x.Str("id"), x.Str("name"))).ToList();
        var provinces = n.List("provinces").Select(x => new ProvinceDef(
            x.Str("id"),
            x.Str("name"),
            x.Str("owner"),
            x.Str("detail") switch
            {
                "full" => DetailLevel.Full,
                "block" => DetailLevel.Block,
                var s => throw new ContentException($"{x.Path}.detail: expected 'full' or 'block', got '{s}'"),
            },
            x.Fixed("corruption"))).ToList();

        var def = new ScenarioDef(
            n.Str("id"),
            n.Str("name"),
            n.Date("start_date"),
            n.Int("last_day"),
            (ulong)n.Long("default_seed"),
            n.Str("player"),
            nations,
            provinces);
        def.Validate();
        return def;
    }

    private void Validate()
    {
        var nationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nd in Nations)
            if (!nationIds.Add(nd.Id)) throw new ContentException($"Scenario {Id}: duplicate nation '{nd.Id}'.");
        if (!nationIds.Contains(Player)) throw new ContentException($"Scenario {Id}: player '{Player}' is not a nation.");

        var provinceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in Provinces)
        {
            if (!provinceIds.Add(p.Id)) throw new ContentException($"Scenario {Id}: duplicate province '{p.Id}'.");
            if (!nationIds.Contains(p.Owner)) throw new ContentException($"Scenario {Id}: province '{p.Id}' has unknown owner '{p.Owner}'.");
        }
    }
}
