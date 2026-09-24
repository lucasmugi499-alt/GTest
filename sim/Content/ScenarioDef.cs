using Cascade.Sim.Core;

namespace Cascade.Sim.Content;

public enum DetailLevel : byte
{
    /// <summary>Simulated facility by facility (Ossen East, Veyl).</summary>
    Full = 0,
    /// <summary>One aggregate block (Kestria Interior, Varan). D-005.</summary>
    Block = 1,
}

public sealed record NationDef(string Id, string Name, Fine Doctrine, int SpareTransformers, int MobileSubstations);

public sealed record ProvinceDef(
    string Id, string Name, string Owner, DetailLevel Detail, Fixed Corruption,
    IReadOnlyList<(string Pool, Fixed People)> Labour, bool RoadAccess, Fixed RefuelTonnesPerHour);

public sealed record FacilityDef(
    string Id, string Name, string Province, string Owner, string Recipe, Fixed Capacity, Fixed Efficiency,
    bool DualUse, Fixed ExperienceDays, Fixed PowerMw, string? Substation, string Kind,
    IReadOnlyList<(string Pool, Fixed People)> Labour, Fixed Damage);

public sealed record PlantDef(string Id, string Province, Fixed CapacityMw, bool BlackStart);
public sealed record SubstationDef(string Id, string Province, Fixed CapacityMw);
public sealed record TieLineDef(string A, string B, Fixed CapacityMw);
public sealed record LoadDef(string Id, string Substation, string Kind, Fixed DemandMw, long Population, Fixed? TankHours, Fixed DieselPerHour);
public sealed record EdgeDef(string A, string B, int LeadDays, IReadOnlyList<Fixed> CapacityTonnesByClass);
public sealed record ImportDef(string Good, string From, string To, int LeadDays, Fixed CapacityPerDay, bool Sea);
public sealed record DemandDef(string Province, string Good, Fixed PerDay, string Kind);

/// <summary>A scenario's starting world. Entity IDs are assigned in file order (D-022).</summary>
public sealed record ScenarioDef(
    string Id,
    string Name,
    DateOnly StartDate,
    int LastDay,
    ulong DefaultSeed,
    string Player,
    IReadOnlyList<NationDef> Nations,
    IReadOnlyList<ProvinceDef> Provinces,
    IReadOnlyList<(string Good, Fixed Days)> InitialCoverDays,
    IReadOnlyList<DemandDef> Demand,
    IReadOnlyList<FacilityDef> Facilities,
    IReadOnlyList<PlantDef> Plants,
    IReadOnlyList<SubstationDef> Substations,
    IReadOnlyList<TieLineDef> TieLines,
    IReadOnlyList<LoadDef> Loads,
    IReadOnlyList<EdgeDef> Edges,
    IReadOnlyList<ImportDef> Imports)
{
    public const string WorldSource = "world";

    public static ScenarioDef Read(ContentNode scenario, ContentNode facilities, ContentNode grid, ContentNode trade, Catalog catalog)
    {
        var nations = scenario.List("nations").Select(x => new NationDef(
            x.Str("id"), x.Str("name"), x.Fine("doctrine"), x.Int("spare_transformers"), x.Int("mobile_substations"))).ToList();

        var provinces = scenario.List("provinces").Select(x => new ProvinceDef(
            x.Str("id"),
            x.Str("name"),
            x.Str("owner"),
            x.Str("detail") switch
            {
                "full" => DetailLevel.Full,
                "block" => DetailLevel.Block,
                var s => throw new ContentException($"{x.Path}.detail: expected 'full' or 'block', got '{s}'"),
            },
            x.Fixed("corruption"),
            PoolMap(x.Child("labour")),
            x.Bool("road_access"),
            x.Fixed("refuel_t_per_hour"))).ToList();

        var cover = scenario.Child("initial_cover_days");
        var coverList = cover.Keys().Select(k => (k, cover.Fixed(k))).ToList();

        var demand = scenario.List("demand").Select(x => new DemandDef(
            x.Str("province"), x.Str("good"), x.Fixed("per_day"), x.Str("kind"))).ToList();

        var facilityList = facilities.List("facilities").Select(x => new FacilityDef(
            x.Str("id"), x.Str("name"), x.Str("province"), x.Str("owner"), x.Str("recipe"),
            x.Fixed("capacity"), x.Fixed("efficiency"), x.Bool("dual_use"),
            x.Has("experience_days") ? x.Fixed("experience_days") : Fixed.Zero,
            x.Fixed("power_mw"), x.OptStr("substation"), x.Str("kind"),
            PoolMap(x.Child("labour")), x.Fixed("damage"))).ToList();

        var plants = grid.List("plants").Select(x => new PlantDef(x.Str("id"), x.Str("province"), x.Fixed("capacity_mw"), x.Bool("black_start"))).ToList();
        var subs = grid.List("substations").Select(x => new SubstationDef(x.Str("id"), x.Str("province"), x.Fixed("capacity_mw"))).ToList();
        var ties = grid.List("tie_lines").Select(x => new TieLineDef(x.Str("a"), x.Str("b"), x.Fixed("capacity_mw"))).ToList();
        var loads = grid.List("loads").Select(x =>
        {
            Fixed? tank = null;
            Fixed burn = Fixed.Zero;
            if (x.Has("backup"))
            {
                var b = x.Child("backup");
                burn = b.Fixed("diesel_t_per_hour");
                if (b.Has("tank_hours")) tank = b.Fixed("tank_hours");
            }
            return new LoadDef(x.Str("id"), x.Str("substation"), x.Str("kind"), x.Fixed("demand_mw"), x.Long("population"), tank, burn);
        }).ToList();

        var edges = trade.List("edges").Select(x =>
        {
            var c = x.Child("capacity_t");
            return new EdgeDef(x.Str("a"), x.Str("b"), x.Int("lead_days"),
                [c.Fixed("bulk"), c.Fixed("container"), c.Fixed("fuel"), c.Fixed("high_value")]);
        }).ToList();
        var imports = trade.List("imports").Select(x => new ImportDef(
            x.Str("good"), x.Str("from"), x.Str("to"), x.Int("lead_days"), x.Fixed("capacity_per_day"), x.Bool("sea"))).ToList();

        var def = new ScenarioDef(
            scenario.Str("id"), scenario.Str("name"), scenario.Date("start_date"), scenario.Int("last_day"),
            (ulong)scenario.Long("default_seed"), scenario.Str("player"),
            nations, provinces, coverList, demand, facilityList, plants, subs, ties, loads, edges, imports);
        def.Validate(catalog);
        return def;
    }

    private static List<(string, Fixed)> PoolMap(ContentNode map) => map.Keys().Select(k => (k, map.Fixed(k))).ToList();

    private void Validate(Catalog catalog)
    {
        var nationIds = Unique(Nations.Select(n => n.Id), "nation");
        if (!nationIds.Contains(Player)) Fail($"player '{Player}' is not a nation");
        var provinceIds = Unique(Provinces.Select(p => p.Id), "province");
        foreach (var p in Provinces)
            if (!nationIds.Contains(p.Owner)) Fail($"province '{p.Id}' has unknown owner '{p.Owner}'");

        var subIds = Unique(Substations.Select(s => s.Id), "substation");
        Unique(Plants.Select(p => p.Id), "plant");
        Unique(Loads.Select(l => l.Id).Concat(Facilities.Select(f => f.Id)), "load or facility");
        foreach (var s in Substations) Province(s.Province, $"substation {s.Id}");
        foreach (var p in Plants) Province(p.Province, $"plant {p.Id}");
        foreach (var t in TieLines) { Province(t.A, "tie line"); Province(t.B, "tie line"); }
        foreach (var l in Loads) if (!subIds.Contains(l.Substation)) Fail($"load '{l.Id}' has unknown substation '{l.Substation}'");
        foreach (var e in Edges) { Province(e.A, "edge"); Province(e.B, "edge"); }

        foreach (var f in Facilities)
        {
            Province(f.Province, $"facility {f.Id}");
            catalog.Recipe(f.Recipe);
            if (f.PowerMw > Fixed.Zero && (f.Substation is null || !subIds.Contains(f.Substation)))
                Fail($"facility '{f.Id}' draws power but has no valid substation");
        }
        foreach (var d in Demand) { Province(d.Province, "demand"); catalog.Good(d.Good); }
        foreach (var (g, _) in InitialCoverDays) catalog.Good(g);
        foreach (var i in Imports)
        {
            catalog.Good(i.Good);
            Province(i.To, "import");
            if (i.From != WorldSource && !nationIds.Contains(i.From)) Fail($"import of {i.Good} from unknown source '{i.From}'");
        }

        void Province(string id, string what)
        {
            if (!provinceIds.Contains(id)) Fail($"{what} refers to unknown province '{id}'");
        }
    }

    private HashSet<string> Unique(IEnumerable<string> ids, string what)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids) if (!set.Add(id)) Fail($"duplicate {what} '{id}'");
        return set;
    }

    private void Fail(string message) => throw new ContentException($"Scenario {Id}: {message}.");
}
