using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Economy;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Conflict;

/// <summary>Day 0 politics: needs start settled, the AI draws its hidden red line, and the player gets a first estimate.</summary>
public static class SocietySetup
{
    public static void Initialize(SimWorld w, ContentSet c, ulong seed)
    {
        var b = c.Balance;
        var conflict = c.Scenario.Conflict;
        Society.Update(w, b, c, EconomyRules.EmaAlpha(b.Society.SmoothingDays), EconomyRules.EmaAlpha(b.Society.FastSmoothingDays), first: true);

        // Spec Red lines: RL ~ N(personality mean, 10), −10 when nationalists are strong, +10 when the economy is weak.
        var personality = b.Escalation.Personalities.TryGetValue(conflict.VaranPersonality, out var p) ? p
            : throw new ContentException($"Unknown personality '{conflict.VaranPersonality}' (balance.yaml escalation.personalities).");
        for (int n = 0; n < w.Nations.Count; n++)
        {
            if (n == w.Nations.Player) continue;
            var rng = new RngStream(seed, 0, (ulong)SystemId.Setup, EntityRef.Of(EntityKind.Nation, n));
            var rl = rng.NextNormal(personality.RedLine, b.Escalation.RedLineSd);
            if (conflict.VaranNationalistsStrong) rl -= b.Escalation.RedLineDomesticShift;
            if (conflict.VaranEconomyWeak) rl += b.Escalation.RedLineDomesticShift;
            w.Politics.RedLine.Init(n, rl);
            w.Politics.DroneOpsNearBorder.Init(n, conflict.DroneOpsNearBorder);
        }
        w.Commit();
        RedLineEstimateSystem.Estimate(w, b, seed, 0);
        MarketsSystemInit(w, b);
        w.Commit();
    }

    /// <summary>War-risk premiums at Day 0 follow the starting rung (no line skips yet).</summary>
    private static void MarketsSystemInit(SimWorld w, Balance b)
    {
        int n = w.Nations.Player;
        int rung = Escalation.HighestRung(w, b, n);
        w.Politics.InsuranceMultiplier.Set(n, rung >= b.Markets.InsuranceFromRung ? Fixed.FromInt(1 + rung) : Fixed.One);
    }
}
