using Cascade.Sim.Core;
using Cascade.Sim.Scheduling;

namespace Cascade.Sim.Narrative;

public enum AutoMode { First, Default, Random }

/// <summary>
/// Answers pending decisions without a player: headless runs, piped input that ends early, and the M6 batch.
/// Random picks are seeded by (campaign seed, storylet instance), so a batch run replays exactly.
/// </summary>
public static class Autopilot
{
    public const ulong System = 500;

    /// <summary>Queues an answer for every pending decision not in <paramref name="skip"/>. Returns how many.</summary>
    public static int Answer(Simulation sim, AutoMode mode, ISet<long>? skip = null)
    {
        int n = 0;
        foreach (var inst in sim.PendingDecisions.ToList())
        {
            if (skip is not null && skip.Contains(inst.Seq)) continue;
            int choice = Pick(sim, inst, mode);
            if (choice < 0) continue;
            sim.Orders.Enqueue(new ChooseStoryletOrder(sim.World.Nations.Player, inst.Seq, choice));
            skip?.Add(inst.Seq);
            n++;
        }
        return n;
    }

    public static int Pick(Simulation sim, StoryletInstance inst, AutoMode mode)
    {
        var s = sim.Narrative.Def.Storylets[inst.Storylet];
        var available = Enumerable.Range(0, s.Choices.Count).Where(c => sim.ChoiceAvailable(inst, c)).ToList();
        if (available.Count == 0) return -1;
        switch (mode)
        {
            case AutoMode.Default:
                int d = s.Default is null ? -1 : s.ChoiceIndex(s.Default);
                return available.Contains(d) ? d : available[0];
            case AutoMode.Random:
                var rng = new RngStream(sim.Seed, (ulong)inst.Seq, System, (ulong)inst.Storylet);
                return available[rng.NextInt(0, available.Count)];
            default:
                return available[0];
        }
    }
}
