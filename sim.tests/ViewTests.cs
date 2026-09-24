using Cascade.Sim.Narrative;

namespace Cascade.Sim.Tests;

/// <summary>What the Godot UI reads: snapshots, the Cascade graph and decision cards, every day of a campaign.</summary>
public class ViewTests
{
    [Fact]
    public void ViewsBuildEveryDayOfACampaign()
    {
        var sim = TestContent.NewScenario(5);
        var skip = new HashSet<long>();
        while (!sim.IsFinished)
        {
            var snap = SimSnapshot.Of(sim);
            var cascade = CascadeGraph.Build(sim);
            Assert.NotEmpty(cascade.Nodes);
            Assert.All(cascade.Edges, e =>
            {
                Assert.Contains(cascade.Nodes, n => n.Id == e.From);
                Assert.Contains(cascade.Nodes, n => n.Id == e.To);
            });
            Assert.All(snap.Decisions, d => Assert.NotEmpty(d.Choices));
            Autopilot.Answer(sim, AutoMode.Random, skip);
            sim.StepDay();
        }
    }

    [Fact]
    public void CascadeShowsTheBlackoutRollingDownstream()
    {
        var sim = TestContent.NewScenario();
        // fab_first: the industrial substation waits for a spare (about Day 18), so on Day 14 it is still dark.
        Story.Play(sim, 14, new() { ["lights_out"] = "fab_first" });
        var view = CascadeGraph.Build(sim);
        Health Of(string id) => view.Nodes.Single(n => n.Id == id).Health;
        Assert.Equal(Health.Failing, Of("sub:ossen_industrial"));
        Assert.Equal(Health.Failing, Of("fac:tessera_fab_3"));
        Assert.Equal(Health.Failing, Of("fac:ardent_controller_plant"));
        Assert.NotEqual(Health.Ok, Of("good:flight_controller"));
        Assert.Equal(Health.Ok, Of("fac:interior_legacy_fabs"));
        // Sources sit left of what they feed.
        var col = view.Nodes.ToDictionary(n => n.Id, n => n.Column);
        Assert.All(view.Edges, e => Assert.True(col[e.From] < col[e.To], $"{e.From} → {e.To}"));
    }
}
