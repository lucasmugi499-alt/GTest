namespace Cascade.Sim.Content;

/// <summary>Everything loaded from the /content folder for one campaign.</summary>
public sealed record ContentSet(string Root, Balance Balance, ScenarioDef Scenario)
{
    public const string BalanceFile = "balance.yaml";
    public const string DefaultScenario = "veyl_crossing";

    public static ContentSet Load(string contentDir, string scenario = DefaultScenario)
    {
        var balanceNode = ContentNode.LoadFile(Path.Combine(contentDir, BalanceFile));
        var balance = Balance.Read(balanceNode);
        balanceNode.EnsureAllUsed();

        var scenarioNode = ContentNode.LoadFile(Path.Combine(contentDir, "scenarios", scenario + ".yaml"));
        var scenarioDef = ScenarioDef.Read(scenarioNode);
        scenarioNode.EnsureAllUsed();

        return new ContentSet(contentDir, balance, scenarioDef);
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
