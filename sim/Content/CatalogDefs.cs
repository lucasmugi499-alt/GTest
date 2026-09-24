using Cascade.Sim.Core;

namespace Cascade.Sim.Content;

public enum GoodTier : byte { Raw, Refined, Component, System }

/// <summary>Spec Transport: goods move in four classes, each with its own edge capacity.</summary>
public enum TransportClass : byte { Bulk, Container, Fuel, HighValue }

public sealed record GoodDef(
    int Id, string Key, string Name, string Unit, GoodTier Tier, TransportClass Class,
    Fixed MassKg, Fixed PriceK, bool Storable, Fixed DecayPerDay);

public enum FabClass : byte { None, Legacy, Leading }

public sealed record RecipeDef(
    int Id, string Key, string Line, FabClass Fab, string? Design,
    IReadOnlyList<(int Good, Fixed Qty)> Inputs,
    IReadOnlyList<(int Good, Fixed Qty)> Outputs);

public sealed record DesignDef(int Id, string Key, string Name, Fixed Effectiveness, Fixed Cap);

/// <summary>Scenario-independent libraries: goods, recipes, designs. IDs follow file order (D-022).</summary>
public sealed class Catalog
{
    public IReadOnlyList<GoodDef> Goods { get; }
    public IReadOnlyList<RecipeDef> Recipes { get; }
    public IReadOnlyList<DesignDef> Designs { get; }

    private readonly Dictionary<string, int> _goods, _recipes, _designs;

    private Catalog(List<GoodDef> goods, List<RecipeDef> recipes, List<DesignDef> designs)
    {
        Goods = goods; Recipes = recipes; Designs = designs;
        _goods = goods.ToDictionary(g => g.Key, g => g.Id, StringComparer.Ordinal);
        _recipes = recipes.ToDictionary(r => r.Key, r => r.Id, StringComparer.Ordinal);
        _designs = designs.ToDictionary(d => d.Key, d => d.Id, StringComparer.Ordinal);
    }

    public int Good(string key) => _goods.TryGetValue(key, out var id) ? id : throw new ContentException($"Unknown good '{key}'.");
    public int Recipe(string key) => _recipes.TryGetValue(key, out var id) ? id : throw new ContentException($"Unknown recipe '{key}'.");
    public int Design(string key) => _designs.TryGetValue(key, out var id) ? id : throw new ContentException($"Unknown design '{key}'.");

    public static Catalog Read(ContentNode goods, ContentNode recipes, ContentNode designs)
    {
        var goodList = new List<GoodDef>();
        foreach (var key in goods.Keys())
        {
            var g = goods.Child(key);
            goodList.Add(new GoodDef(goodList.Count, key, g.Str("name"), g.Str("unit"),
                Enum<GoodTier>(g, "tier", ("raw", GoodTier.Raw), ("refined", GoodTier.Refined), ("component", GoodTier.Component), ("system", GoodTier.System)),
                Enum<TransportClass>(g, "class", ("bulk", TransportClass.Bulk), ("container", TransportClass.Container), ("fuel", TransportClass.Fuel), ("high_value", TransportClass.HighValue)),
                g.Fixed("mass_kg"), g.Fixed("price_k"), g.Bool("storable"), g.Fixed("decay_per_day")));
        }
        var goodIds = goodList.ToDictionary(g => g.Key, g => g.Id, StringComparer.Ordinal);

        var designList = new List<DesignDef>();
        foreach (var key in designs.Keys())
        {
            var d = designs.Child(key);
            designList.Add(new DesignDef(designList.Count, key, d.Str("name"), d.Fixed("effectiveness"), d.Fixed("cap")));
        }

        var recipeList = new List<RecipeDef>();
        foreach (var key in recipes.Keys())
        {
            var r = recipes.Child(key);
            FabClass fab = r.OptStr("fab") switch
            {
                null => FabClass.None,
                "legacy" => FabClass.Legacy,
                "leading" => FabClass.Leading,
                var s => throw new ContentException($"{r.Path}.fab: expected legacy or leading, got '{s}'"),
            };
            var design = r.OptStr("design");
            if (design is not null && !designList.Any(d => d.Key == design))
                throw new ContentException($"{r.Path}.design: unknown design '{design}'");
            recipeList.Add(new RecipeDef(recipeList.Count, key, r.Str("line"), fab, design,
                GoodQtyMap(r.Child("inputs"), goodIds), GoodQtyMap(r.Child("outputs"), goodIds)));
        }

        return new Catalog(goodList, recipeList, designList);
    }

    internal static List<(int, Fixed)> GoodQtyMap(ContentNode map, IReadOnlyDictionary<string, int> goods)
    {
        var list = new List<(int, Fixed)>();
        foreach (var k in map.Keys())
        {
            if (!goods.TryGetValue(k, out var id)) throw new ContentException($"{map.Path}: unknown good '{k}'");
            list.Add((id, map.Fixed(k)));
        }
        return list;
    }

    private static T Enum<T>(ContentNode n, string key, params (string Name, T Value)[] options)
    {
        var s = n.Str(key);
        foreach (var (name, value) in options) if (name == s) return value;
        throw new ContentException($"{n.Path}.{key}: expected one of {string.Join(", ", options.Select(o => o.Name))}, got '{s}'");
    }
}
