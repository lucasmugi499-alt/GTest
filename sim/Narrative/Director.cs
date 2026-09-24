using Cascade.Sim.Conflict;
using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Narrative;

/// <summary>
/// Phase 10 (spec Narrative engine; Tension and Director scoring). Runs hourly in Crisis Time (for arc beats) and daily:
/// 1. Expired decisions take their default. Seeds are defused or ripen.
/// 2. Arc beats whose preconditions hold fire at once, ignoring caps (D-012). Then the dials update: Tension, Narrative Debt.
/// 3. Daily, the Director scores eligible generic storylets and, within its caps, samples one with a seeded softmax.
/// </summary>
public sealed class DirectorPhase : IHourlyPhase
{
    public PhaseId Id => PhaseId.Narrative;

    public void RunHourly(TickContext ctx) => FireArcBeats(ctx);

    public void RunDaily(TickContext ctx)
    {
        ExpireDecisions(ctx);
        UpdateSeeds(ctx);
        FireArcBeats(ctx);
        UpdateDials(ctx);
        DirectorPick(ctx);
    }

    private static void ExpireDecisions(TickContext ctx)
    {
        var eng = ctx.Narrative;
        foreach (var inst in ctx.World.Storylets.Instances.ToList())
        {
            if (!inst.Pending || inst.ExpiresDay < 0 || ctx.Day <= inst.ExpiresDay) continue;
            var s = eng.Def.Storylets[inst.Storylet];
            eng.Resolve(ctx, inst, s.ChoiceIndex(s.Default!), byDefault: true);
        }
    }

    /// <summary>
    /// Spec Seeds: after the minimum delay, a monthly payoff chance (applied as a daily hazard of p ÷ 30, D-040);
    /// defused when every defuse condition holds.
    /// </summary>
    private static void UpdateSeeds(TickContext ctx)
    {
        var w = ctx.World;
        var eng = ctx.Narrative;
        var facts = eng.Facts(ctx);
        for (int i = 0; i < w.Seeds.Count; i++)
        {
            if (w.Seeds.State[i] != (int)SeedState.Live) continue;
            var def = w.Seeds.Defs[i];
            if (def.Defuse.Count > 0 && eng.Blackboard.All(def.Defuse, facts))
            {
                w.Seeds.State.Set(i, (int)SeedState.Defused);
                w.Log.Add(ctx.Day, -1, "seed", $"Defused: {def.Id.Replace('_', ' ')}.", "", def.Id);
                continue;
            }
            if (ctx.Day < w.Seeds.PlantedDay[i] + def.MinDelayDays) continue;
            var rng = ctx.Rng(SystemId.Narrative, EntityRef.Of(EntityKind.Seed, i));
            if (rng.Chance(def.MonthlyChance / 30)) w.Seeds.State.Set(i, (int)SeedState.Ripe);
        }
    }

    /// <summary>T = 0.3 K + 0.2 (100 − Appr) + 2.8 Rung + 0.3 A; Narrative Debt = Σ weights of live seeds (D-040).</summary>
    private static void UpdateDials(TickContext ctx)
    {
        var w = ctx.World;
        var n = ctx.Balance.Narrative;
        int player = w.Nations.Player;
        int provinces = 0, crisis = 0;
        for (int p = 0; p < w.Provinces.Count; p++)
            if (w.Provinces.Owner[p] == player) { provinces++; if (w.Provinces.InCrisis[p]) crisis++; }
        var k = provinces == 0 ? Fixed.Zero : Fixed.Ratio(100L * crisis, provinces);
        int beats = w.Storylets.Instances.Count(i => ctx.Narrative.Def.Storylets[i.Storylet].Arc is not null && ctx.Day - i.Day < n.ArcIntensityWindowDays);
        var a = Fixed.Min(Fixed.Hundred, n.ArcIntensityPerBeat * beats);
        var rung = Escalation.HighestRung(w, ctx.Balance, player);
        var t = n.TensionCrisis * k + n.TensionApproval * (Fixed.Hundred - w.Politics.Approval[player]) + n.TensionRung * rung + n.TensionArcs * a;
        w.Director.Tension.Set(0, Fixed.Clamp(t, Fixed.Zero, Fixed.Hundred));
        w.Director.ArcIntensity.Set(0, a);

        var debt = Fixed.Zero;
        for (int i = 0; i < w.Seeds.Count; i++)
            if (w.Seeds.State[i] is (int)SeedState.Live or (int)SeedState.Ripe) debt += w.Seeds.Defs[i].Weight;
        w.Director.NarrativeDebt.Set(0, Fixed.Min(Fixed.Hundred, debt));
    }

    private static void FireArcBeats(TickContext ctx)
    {
        var eng = ctx.Narrative;
        foreach (var s in eng.Def.Storylets)
        {
            if (s.Arc is null) continue;
            var cast = Eligible(ctx, s);
            if (cast is not null) Fire(ctx, s, cast);
        }
    }

    private static void DirectorPick(TickContext ctx)
    {
        var w = ctx.World;
        var eng = ctx.Narrative;
        var n = ctx.Balance.Narrative;
        var pers = n.Personality;

        int majorsWeek = 0, minorsWeek = 0;
        foreach (var i in w.Storylets.Instances)
        {
            if (ctx.Day - i.Day >= 7) continue;
            if (eng.Def.Storylets[i.Storylet].Tier == Tier.Major) majorsWeek++; else minorsWeek++;
        }
        // Pending values: arc beats and the dials were updated earlier in this same pass and aren't committed yet.
        int lastMajor = w.Director.LastMajorDay.Pending(0);
        bool crisis = ctx.CrisisProvinces.Count > 0;
        int hoursSinceMajor = (ctx.Day - lastMajor) * 24;
        bool majorAllowed = majorsWeek < n.MajorsPerWeek && (!crisis || hoursSinceMajor >= n.CrisisMajorHours);
        bool minorAllowed = minorsWeek < n.MinorsPerWeek;
        if (!majorAllowed && !minorAllowed) return;

        var tension = w.Director.Tension.Pending(0);
        var debt = w.Director.NarrativeDebt.Pending(0);
        var sincePacing = Fixed.Ratio(ctx.Day - lastMajor, pers.MajorEveryDays);
        var scored = new List<(StoryletDef S, int[] Cast, Fixed U)>();
        foreach (var s in eng.Def.Storylets)
        {
            if (s.Arc is not null) continue;
            if (s.Tier == Tier.Major ? !majorAllowed : !minorAllowed) continue;
            var cast = Eligible(ctx, s);
            if (cast is null) continue;
            // U_k = w_T (T* − T) ι_k + w_P d/d* + w_D π_k ND − ρ n_k + b_k
            int recent = w.Storylets.Instances.Count(i => i.Storylet == s.Index && ctx.Day - i.Day < n.RepeatWindowDays);
            var u = (n.UtilityTension * (pers.TargetTension - tension)).Times(s.Intensity)
                    + n.UtilityPacing * sincePacing
                    + (s.PaysOffSeed is not null ? n.UtilityDebt * debt : Fixed.Zero)
                    - n.UtilityRepeat * recent
                    + n.UtilityBase * s.Weight;
            if (u >= n.UtilityMin) scored.Add((s, cast, u));
        }
        if (scored.Count == 0) return;

        // Seeded softmax over the top scores (spec); the Historian's low temperature keeps it close to the best.
        var top = scored.OrderByDescending(x => x.U).ThenBy(x => x.S.Index).Take(n.TopChoices).ToList();
        var best = top[0].U;
        var weights = top.Select(x => FixedMath.Exp(((x.U - best) / pers.Temperature).ToFine())).ToArray();
        var total = weights.Aggregate(Fine.Zero, (a, b) => a + b);
        var roll = ctx.Rng(SystemId.Narrative, EntityRef.None).NextFine() * total;
        int pick = 0;
        for (var acc = Fine.Zero; pick < top.Count - 1; pick++)
        {
            acc += weights[pick];
            if (roll < acc) break;
        }
        Fire(ctx, top[pick].S, top[pick].Cast);
    }

    /// <summary>
    /// Spec: eligible when every precondition holds, its cooldown has passed, it isn't already waiting for an answer,
    /// and every role can be cast. Returns the cast, or null.
    /// </summary>
    public static int[]? Eligible(TickContext ctx, StoryletDef s)
    {
        var w = ctx.World;
        if (ctx.Day - w.Storylets.LastFired[s.Index] < s.CooldownDays) return null;
        foreach (var i in w.Storylets.Instances) if (i.Storylet == s.Index && i.Pending) return null;
        if (!ctx.Narrative.Blackboard.All(s.Preconditions, ctx.Narrative.Facts(ctx))) return null;
        return Cast(ctx, s);
    }

    /// <summary>Spec Casting: score(c, r) = fit + 0.5 H + 0.3 Dr − 0.5 U; one role per character per storylet.</summary>
    public static int[]? Cast(TickContext ctx, StoryletDef s)
    {
        var w = ctx.World;
        var n = ctx.Balance.Narrative;
        var chars = w.Characters;
        var facts = ctx.Narrative.Facts(ctx);
        var cast = new int[s.Roles.Count];
        var used = new HashSet<int>();
        for (int r = 0; r < s.Roles.Count; r++)
        {
            var spec = s.Roles[r];
            int best = -1;
            var bestScore = Fixed.Zero;
            for (int c = 0; c < chars.Count; c++)
            {
                var d = chars.Defs[c];
                if (d.Role != spec.Role || used.Contains(c)) continue;
                if (spec.OpinionBelow is { } below && Blackboard.Opinion(facts, c) >= below) continue;
                var fit = n.CastingFitRole
                          + (spec.Portfolio is not null && d.Portfolio == spec.Portfolio ? n.CastingFitPortfolio : Fixed.Zero)
                          + (spec.Province is not null && d.Province == spec.Province ? n.CastingFitProvince : Fixed.Zero)
                          + (spec.Faction is not null && d.Faction == spec.Faction ? n.CastingFitFaction : Fixed.Zero);
                fit = Fixed.Min(Fixed.Hundred, fit);
                var history = Fixed.Min(Fixed.Hundred, n.HistoryPerMemory * chars.MemoryCount(c));
                var recent = n.CastingRecentPerAppearance * chars.RecentAppearances(c, ctx.Day, n.CastingRecentWindowDays);
                var score = fit + n.CastingHistory * history + n.CastingDrama * d.Drama - n.CastingRecent * recent;
                if (best < 0 || score > bestScore) { best = c; bestScore = score; }
            }
            if (best < 0) return null;
            cast[r] = best;
            used.Add(best);
        }
        return cast;
    }

    public static StoryletInstance Fire(TickContext ctx, StoryletDef s, int[] cast)
    {
        var w = ctx.World;
        int expires = s.ExpiresDays >= 0 ? ctx.Day + s.ExpiresDays : -1;
        var inst = w.Storylets.Add(s.Index, ctx.Day, ctx.Hour, cast, expires);
        w.Storylets.LastFired.Set(s.Index, ctx.Day);
        w.Storylets.Fired.Set(s.Index, w.Storylets.Fired.Pending(s.Index) + 1);
        foreach (int c in cast) w.Characters.Appear(c, ctx.Day);
        if (s.Tier == Tier.Major)
        {
            w.Director.LastMajorDay.Set(0, ctx.Day);
            w.Director.LastMajorHour.Set(0, Math.Max(0, ctx.Hour));
        }
        if (s.PaysOffSeed is { } seed)
        {
            int id = w.Seeds.IdOf(seed);
            w.Seeds.State.Set(id, (int)SeedState.PaidOff);
        }
        w.Log.Add(ctx.Day, ctx.Hour, "storylet", s.Title, "", s.Id);
        return inst;
    }
}

/// <summary>
/// Phase 11 (spec Record): the event log is written as things happen; here the running totals the Chronicle needs.
/// </summary>
public sealed class RecordPhase : IPhase
{
    public PhaseId Id => PhaseId.Record;

    public void RunDaily(TickContext ctx)
    {
        var w = ctx.World;
        var d = w.Director;
        var dark = Blackboard.PeopleDark(w, -1);
        d.PeopleDarkDays.Set(0, d.PeopleDarkDays[0] + Fixed.FromInt(dark));
        d.MaxRung.Set(0, Math.Max(d.MaxRung[0], Escalation.HighestRung(w, ctx.Balance, w.Nations.Player)));
        if (ctx.CrisisProvinces.Any(p => w.Provinces.Owner[p] == w.Nations.Player)) d.CrisisDays.Set(0, d.CrisisDays[0] + 1);

        var chips = Produced(ctx, ctx.Content.Scenario.Chronicle.ChipsGood);
        var drones = Produced(ctx, ctx.Content.Scenario.Chronicle.DronesGood);
        if (ctx.Day == 0) { d.ChipsBaseline.Set(0, chips); d.DronesBaseline.Set(0, drones); }
        d.ChipsLast.Set(0, chips);
        d.DronesLast.Set(0, drones);
        d.ChipsTotal.Set(0, d.ChipsTotal[0] + chips);
        d.DronesTotal.Set(0, d.DronesTotal[0] + drones);
    }

    private static Fixed Produced(TickContext ctx, string good)
    {
        int g = ctx.Content.Catalog.Good(good);
        var sum = Fixed.Zero;
        var f = ctx.World.Facilities;
        for (int i = 0; i < f.Count; i++)
        {
            var r = ctx.Content.Catalog.Recipes[f.Recipe[i]];
            if (r.Outputs.Count > 0 && r.Outputs[0].Good == g) sum += f.OutputToday[i];
        }
        return sum;
    }
}
