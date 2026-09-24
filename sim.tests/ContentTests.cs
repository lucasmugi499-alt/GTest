using Cascade.Sim.Content;
using Cascade.Sim.Core;

namespace Cascade.Sim.Tests;

public class ContentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cascade-content-" + Guid.NewGuid().ToString("N"));

    public ContentTests()
    {
        // Start from a copy of the real content so each test changes one thing.
        var repo = ContentSet.FindContentDir();
        foreach (var file in Directory.GetFiles(repo, "*.yaml", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_dir, Path.GetRelativePath(repo, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Edit(string file, string from, string to)
    {
        var path = Path.Combine(_dir, file);
        var text = File.ReadAllText(path);
        Assert.Contains(from, text);
        File.WriteAllText(path, text.Replace(from, to));
    }

    [Fact]
    public void RepoContentLoads()
    {
        var c = TestContent.Repo;
        Assert.Equal(48, c.Balance.Sim.Crisis.ClearAfterStableHours);
        Assert.Equal(30, c.Balance.Sim.HashCheckIntervalDays);
        Assert.Equal("veyl_crossing", c.Scenario.Id);
        Assert.Equal(new DateOnly(2031, 3, 3), c.Scenario.StartDate);
        Assert.Equal(90, c.Scenario.LastDay);
        Assert.Equal(["kestria", "varan"], c.Scenario.Nations.Select(n => n.Id));
        Assert.Equal(["kestria_east_ossen", "veyl", "kestria_interior", "varan_core"], c.Scenario.Provinces.Select(p => p.Id));
        Assert.Equal(Fixed.FromInt(26), c.Scenario.Provinces[1].Corruption);
    }

    [Fact]
    public void CopiedContentLoads() => ContentSet.Load(_dir);

    [Fact]
    public void MissingKeyIsAnError()
    {
        Edit("balance.yaml", "clear_after_stable_hours: 48", "clear_after_hours: 48");
        var e = Assert.Throws<ContentException>(() => ContentSet.Load(_dir));
        Assert.Contains("missing required key 'sim.crisis.clear_after_stable_hours'", e.Message);
    }

    [Fact]
    public void UnknownKeyIsAnError()
    {
        // A typo must not silently leave the real key at a default.
        Edit("balance.yaml", "clear_after_stable_hours: 48", "clear_after_stable_hours: 48\n    clear_afer_hours: 12");
        var e = Assert.Throws<ContentException>(() => ContentSet.Load(_dir));
        Assert.Contains("unknown or unused key 'sim.crisis.clear_afer_hours'", e.Message);
        Assert.Contains("balance.yaml:", e.Message);
    }

    [Fact]
    public void BadNumberNamesFileAndKey()
    {
        Edit("scenarios/veyl_crossing/scenario.yaml", "corruption: 26", "corruption: 26.12345");
        var e = Assert.Throws<ContentException>(() => ContentSet.Load(_dir));
        Assert.Contains("scenario.yaml:", e.Message);
        Assert.Contains("provinces[1].corruption", e.Message);
        Assert.Contains("more than 4 decimal places", e.Message);
    }

    [Fact]
    public void UnknownOwnerIsAnError()
    {
        Edit("scenarios/veyl_crossing/scenario.yaml", "owner: varan", "owner: atlantis");
        var e = Assert.Throws<ContentException>(() => ContentSet.Load(_dir));
        Assert.Contains("unknown owner 'atlantis'", e.Message);
    }
}
