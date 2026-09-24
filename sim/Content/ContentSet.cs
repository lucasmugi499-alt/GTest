namespace Cascade.Sim.Content;

/// <summary>Everything loaded from the /content folder for one campaign.</summary>
public sealed record ContentSet(string Root, Balance Balance, Catalog Catalog, ScenarioDef Scenario)
{
    public const string BalanceFile = "balance.yaml";
    public const string DefaultScenario = "veyl_crossing";

    public static ContentSet Load(string contentDir, string scenario = DefaultScenario)
    {
        var balance = ReadStrict(Path.Combine(contentDir, BalanceFile), Balance.Read);

        var goods = ContentNode.LoadFile(Path.Combine(contentDir, "goods.yaml"));
        var recipes = ContentNode.LoadFile(Path.Combine(contentDir, "recipes.yaml"));
        var designs = ContentNode.LoadFile(Path.Combine(contentDir, "designs.yaml"));
        var catalog = Catalog.Read(goods, recipes, designs);
        goods.EnsureAllUsed(); recipes.EnsureAllUsed(); designs.EnsureAllUsed();

        var dir = Path.Combine(contentDir, "scenarios", scenario);
        var sc = ContentNode.LoadFile(Path.Combine(dir, "scenario.yaml"));
        var fac = ContentNode.LoadFile(Path.Combine(dir, "facilities.yaml"), wrapListAs: "facilities");
        var grid = ContentNode.LoadFile(Path.Combine(dir, "grid.yaml"));
        var trade = ContentNode.LoadFile(Path.Combine(dir, "trade.yaml"));
        var society = ContentNode.LoadFile(Path.Combine(dir, "society.yaml"));
        var conflict = ContentNode.LoadFile(Path.Combine(dir, "conflict.yaml"));
        var characters = ContentNode.LoadFile(Path.Combine(dir, "characters.yaml"), wrapListAs: "characters");
        var storylets = ContentNode.LoadFile(Path.Combine(dir, "storylets.yaml"));
        var chronicle = ContentNode.LoadFile(Path.Combine(dir, "chronicle.yaml"));
        var scenarioDef = ScenarioDef.Read(sc, fac, grid, trade, society, conflict, characters, storylets, chronicle, catalog);
        foreach (var n in new[] { sc, fac, grid, trade, society, conflict, characters, storylets, chronicle }) n.EnsureAllUsed();

        return new ContentSet(contentDir, balance, catalog, scenarioDef);
    }

    private static T ReadStrict<T>(string file, Func<ContentNode, T> read)
    {
        var node = ContentNode.LoadFile(file);
        var value = read(node);
        node.EnsureAllUsed();
        return value;
    }

    /// <summary>
    /// Finds the repo's content folder by walking up from <paramref name="start"/> (default: the running
    /// program's folder) until a folder containing content/balance.yaml appears.
    /// </summary>
    public static string FindContentDir(string? start = null)
    {
        foreach (var origin in new[] { start, Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            if (origin is null) continue;
            for (var dir = new DirectoryInfo(origin); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "content");
                if (File.Exists(Path.Combine(candidate, BalanceFile))) return candidate;
            }
        }
        throw new ContentException("Could not find the content folder (content/balance.yaml). Run from inside the Cascade repo or pass --content.");
    }
}
