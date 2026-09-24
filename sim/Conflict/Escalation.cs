using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Conflict;

/// <summary>
/// Spec Escalation meter and Perception. Every action between two nations adds its weight, as the target perceives it
/// (×1.5 if civilians were harmed), to the pair's shared meter, and lifts the meter to the action's floor.
/// The victim's public rallies when attacked; an AI victim answers (spec reactive rule) and checks its red line.
/// </summary>
public static class Escalation
{
    public static Fixed Meter(SimWorld w, int a, int b) => w.Escalation.Meter[w.Escalation.At(a, b)];
    public static int Rung(SimWorld w, Balance bal, int a, int b) => bal.Escalation.RungOf(Meter(w, a, b));

    /// <summary>The highest rung the nation is on with anyone (the Director's and markets' "Rung").</summary>
    public static int HighestRung(SimWorld w, Balance bal, int nation)
    {
        int best = 1;
        for (int o = 0; o < w.Nations.Count; o++)
            if (o != nation) best = Math.Max(best, bal.Escalation.RungOf(w.Escalation.Meter.Pending(w.Escalation.At(nation, o))));
        return best;
    }

    /// <summary>Records an action by <paramref name="actor"/> against <paramref name="target"/>.</summary>
    public static void Record(TickContext ctx, int actor, int target, string action, string text,
        bool civilianHarm = false, bool react = true)
    {
        var w = ctx.World;
        var e = ctx.Balance.Escalation;
        var def = e.Action(action);
        var kappa = civilianHarm ? e.RivalPerceptionCivilianHarm : e.RivalPerception;
        var perceived = Fixed.FromInt(def.Weight) * kappa;
        AddToMeter(ctx, actor, target, perceived, def.Floor, def.Weight);

        var meter = w.Escalation.Meter.Pending(w.Escalation.At(actor, target));
        w.Log.Add(ctx.Day, ctx.Hour, "escalation", text, w.Nations.Keys[actor], action, meter);

        // Spec War exhaustion: "Rally starts at 20 when you are attacked"; answering an attack sets Legitimacy.
        if (def.Weight >= e.Action("disruptive_cyber").Weight)
        {
            var p = w.Politics;
            p.AttackedByRival.Set(target, true);
            p.Rally.Set(target, Fixed.Max(p.Rally.Pending(target), ctx.Balance.Society.RallyOnAttack));
            p.Legitimacy.Set(target, Fixed.Max(p.Legitimacy.Pending(target), ctx.Balance.Society.LegitimacyAnsweringAttack));
        }

        // An AI answers what is done to it: the reactive rule, then its red line (D-039: only the rival's actions
        // can carry the meter across it; the AI's own scripted moves don't provoke itself).
        if (react && target != w.Nations.Player) VaranAi.React(ctx, target, actor, perceived);
        if (target != w.Nations.Player) VaranAi.CheckRedLine(ctx, target, actor);
    }

    /// <summary>Adds perceived weight directly (storylet effects such as "escalation: +10").</summary>
    public static void AddToMeter(TickContext ctx, int a, int b, Fixed amount, int floor, int weight)
    {
        var s = ctx.World.Escalation;
        var value = Fixed.Clamp(s.Meter.Pending(s.At(a, b)) + amount, Fixed.Zero, Fixed.Hundred);
        value = Fixed.Max(value, Fixed.FromInt(floor));
        s.Meter.Set(s.At(a, b), value);
        s.Meter.Set(s.At(b, a), value);
        if (weight >= ctx.Balance.Escalation.DecayBlockedByWeight)
        {
            s.LastBigAction.Set(s.At(a, b), ctx.Day);
            s.LastBigAction.Set(s.At(b, a), ctx.Day);
        }
    }
}

/// <summary>Weekly: the meter decays by 1 when no action of weight 3+ happened between the pair that week.</summary>
public sealed class EscalationDecaySystem : IPeriodicSystem
{
    public SystemId Id => SystemId.WeeklyEscalationDecay;

    public void Run(TickContext ctx)
    {
        var s = ctx.World.Escalation;
        var e = ctx.Balance.Escalation;
        for (int a = 0; a < s.Nations; a++)
            for (int b = a + 1; b < s.Nations; b++)
            {
                if (ctx.Day - s.LastBigAction[s.At(a, b)] < 7) continue;
                var v = Fixed.Max(Fixed.Zero, s.Meter[s.At(a, b)] - e.DecayPerWeek);
                s.Meter.Set(s.At(a, b), v);
                s.Meter.Set(s.At(b, a), v);
                var p = ctx.World.Politics;
                if (v < p.RedLine[a]) p.RedLineCrossed.Set(a, false);
                if (v < p.RedLine[b]) p.RedLineCrossed.Set(b, false);
            }
    }
}

/// <summary>
/// Varan's behaviour in the slice: a scripted schedule (D-013) plus the spec's reactive rule and red line.
/// The full goal planner is out of scope.
/// </summary>
public static class VaranAi
{
    /// <summary>
    /// Spec: below its red line, the AI answers each action within 1 to 14 days with one of its own of weight e × r,
    /// limited by what it can do. It picks the heaviest repertoire action that fits (never war on the reactive path).
    /// </summary>
    public static void React(TickContext ctx, int ai, int rival, Fixed perceivedWeight)
    {
        var bal = ctx.Balance.Escalation;
        var conflict = ctx.Content.Scenario.Conflict;
        var r = bal.Personalities[conflict.VaranPersonality].Retaliation;
        var budget = perceivedWeight * r;

        int pick = -1;
        int pickWeight = 0;
        for (int i = 0; i < conflict.Repertoire.Count; i++)
        {
            var rep = conflict.Repertoire[i];
            if (rep.Kind == "offensive" || !CanDo(ctx, ai, rep)) continue;
            int wgt = bal.Action(rep.Action).Weight;
            if (Fixed.FromInt(wgt) <= budget && wgt > pickWeight) { pick = i; pickWeight = wgt; }
        }
        if (pick < 0) return;

        int decision = ctx.World.Politics.AiDecisions.Pending(ai);
        ctx.World.Politics.AiDecisions.Set(ai, decision + 1);
        var rng = ctx.Rng(SystemId.AiReaction, EntityRef.Of(EntityKind.Nation, ai) | ((ulong)decision << 40));
        int delay = rng.NextInt(bal.RetaliationDelayMin, bal.RetaliationDelayMax + 1);
        ctx.Events.Schedule(new AiActionEvent(ctx.Day + delay, ai, rival, pick));
        ctx.World.Log.Add(ctx.Day, ctx.Hour, "ai_intent", $"{ctx.World.Nations.Names[ai]} prepares a response.",
            ctx.World.Nations.Keys[ai], conflict.Repertoire[pick].Id);
    }

    /// <summary>
    /// Spec Red lines: when E crosses RL, the AI escalates by one rung. It answers each crossing once; the meter has
    /// to fall back below RL (weekly decay) before another crossing counts.
    /// </summary>
    public static void CheckRedLine(TickContext ctx, int ai, int rival)
    {
        var w = ctx.World;
        var bal = ctx.Balance.Escalation;
        var meter = w.Escalation.Meter.Pending(w.Escalation.At(ai, rival));
        if (meter < w.Politics.RedLine[ai] || w.Politics.RedLineCrossed.Pending(ai)) return;
        w.Politics.RedLineCrossed.Set(ai, true);
        int rung = bal.RungOf(meter);

        // The action that lifts the meter into the next rung, else the heaviest available.
        var conflict = ctx.Content.Scenario.Conflict;
        int nextFloor = rung < bal.Rungs.Length ? bal.Rungs[rung] : 100;
        int pick = -1;
        var ordered = Enumerable.Range(0, conflict.Repertoire.Count)
            .Where(i => CanDo(ctx, ai, conflict.Repertoire[i]))
            .OrderBy(i => bal.Action(conflict.Repertoire[i].Action).Weight).ToList();
        foreach (int i in ordered)
            if (bal.Action(conflict.Repertoire[i].Action).Floor >= nextFloor) { pick = i; break; }
        if (pick < 0 && ordered.Count > 0) pick = ordered[^1];
        if (pick < 0) return;

        ctx.Events.Schedule(new AiActionEvent(ctx.Day + 1, ai, rival, pick));
        w.Log.Add(ctx.Day, ctx.Hour, "red_line", $"{w.Nations.Names[ai]} judges its red line crossed.", w.Nations.Keys[ai], conflict.Repertoire[pick].Id, meter);
    }

    private static bool CanDo(TickContext ctx, int ai, RepertoireDef rep) => rep.Kind switch
    {
        "cyber" => rep.Operation is not null && Cyber.CanUse(ctx, ctx.Content.Scenario.Conflict.Operation(rep.Operation), "disrupt"),
        "drone_strike" or "border_clash" or "offensive" => Enumerable.Range(0, ctx.World.Brigades.Count)
            .Any(b => ctx.World.Brigades.Nation[b] == ai && ctx.World.Brigades.Strength[b] > Fixed.Zero),
        _ => true,
    };

    /// <summary>Schedules the scenario's scripted actions (D-013) at campaign start.</summary>
    public static void ScheduleScript(Simulation sim)
    {
        var w = sim.World;
        int player = w.Nations.Player;
        int varan = Enumerable.Range(0, w.Nations.Count).First(n => n != player);
        var conflict = sim.Scenario.Conflict;
        for (int i = 0; i < conflict.Schedule.Count; i++)
        {
            var x = conflict.Schedule[i];
            int province = x.Kind switch
            {
                "border_clash" => w.Front.Province,
                "cyber_attack" => w.Provinces.IdOf(conflict.Operations[conflict.Operation(x.Operation!)].TargetProvince),
                _ => -1,
            };
            sim.Events.Schedule(new ScriptedActionEvent(x.Day, x.Hour, province, varan, player, i));
        }
    }
}

/// <summary>One of the AI's repertoire actions, due now.</summary>
public sealed class AiActionEvent(int day, int ai, int rival, int repertoire) : SimEvent(day)
{
    public override string Kind => "ai.action";

    public override void Apply(TickContext ctx)
    {
        var rep = ctx.Content.Scenario.Conflict.Repertoire[repertoire];
        ConflictActions.Execute(ctx, ai, rival, rep.Kind, new ActionArgs(
            rep.Action, rep.Narrative, rep.Seed, rep.Operation, "disrupt", null, null, rep.Losses,
            rep.KestrianDeaths, rep.VaranDeaths, rep.Days, [], rep.Id));
    }

    protected override void HashFields(StateHasher h) => h.Add(ai).Add(rival).Add(repertoire);
}

/// <summary>A scripted scenario action (D-013).</summary>
public sealed class ScriptedActionEvent(int day, int hour, int province, int ai, int rival, int index) : SimEvent(day, hour, province)
{
    public override string Kind => "script.action";

    public override void Apply(TickContext ctx)
    {
        var x = ctx.Content.Scenario.Conflict.Schedule[index];
        string action = x.Kind switch
        {
            "border_clash" => "border_clash_deaths",
            "cyber_attack" => "disruptive_cyber",
            "narrative" => "influence_detected",
            "export_controls" => "export_controls",
            _ => throw new ContentException($"schedule day {x.Day}: unknown kind '{x.Kind}'"),
        };
        ConflictActions.Execute(ctx, ai, rival, x.Kind, new ActionArgs(
            action, x.Narrative, x.Seed, x.Operation, x.Effect, x.FallbackOperation, x.FallbackEffect, Fine.Zero,
            x.KestrianDeaths, x.VaranDeaths, 0, x.Goods, x.Note));
    }

    protected override void HashFields(StateHasher h) => h.Add(ai).Add(rival).Add(index);
}

public sealed record ActionArgs(
    string Action, string? Narrative, IReadOnlyList<(string Segment, Fine Share)> Seed, string? Operation, string? Effect,
    string? FallbackOperation, string? FallbackEffect, Fine Losses, int KestrianDeaths, int VaranDeaths, int Days,
    IReadOnlyList<string> Goods, string Note);

/// <summary>What each kind of AI action does in the world.</summary>
public static class ConflictActions
{
    public static void Execute(TickContext ctx, int actor, int target, string kind, ActionArgs a)
    {
        var w = ctx.World;
        var names = w.Nations.Names;
        switch (kind)
        {
            case "border_clash":
            {
                Military.KillAtFront(ctx, target, Fixed.FromInt(a.KestrianDeaths));
                Military.KillAtFront(ctx, actor, Fixed.FromInt(a.VaranDeaths));
                Escalation.Record(ctx, actor, target, a.Action,
                    $"Border clash at Veyl: {a.KestrianDeaths} {Demonym(w, target)} and {a.VaranDeaths} {Demonym(w, actor)} dead.{Note(a)}");
                break;
            }
            case "drone_strike":
            {
                int b = Military.FrontBrigade(ctx, target);
                if (b >= 0) Military.Casualties(ctx, b, w.Brigades.Strength[b].Times(a.Losses));
                Escalation.Record(ctx, actor, target, a.Action, $"{names[actor]} drone strike on {Demonym(w, target)} positions at Veyl.");
                break;
            }
            case "offensive":
            {
                for (int b = 0; b < w.Brigades.Count; b++)
                    if (w.Brigades.Nation[b] == actor && w.Brigades.Strength[b] > Fixed.Zero) w.Brigades.Posture.Set(b, (int)Posture.Attack);
                w.Front.ActiveUntil.Set(actor, ctx.Day + a.Days);
                Escalation.Record(ctx, actor, target, a.Action, $"{names[actor]} launches an offensive at Veyl.");
                break;
            }
            case "export_controls":
            {
                var goods = a.Goods.Select(ctx.Content.Catalog.Good).ToList();
                var im = w.Imports;
                for (int r = 0; r < im.Count; r++)
                    if (im.Source[r] == actor && goods.Contains(im.Good[r])) im.Blocked.Set(r, true);
                Escalation.Record(ctx, actor, target, a.Action,
                    $"{names[actor]} announces an export licensing review on {string.Join(" and ", a.Goods.Select(g => ctx.Content.Catalog.Goods[ctx.Content.Catalog.Good(g)].Name.ToLowerInvariant()))}.");
                break;
            }
            case "narrative":
            {
                int n = ctx.Content.Scenario.Society.Narrative(a.Narrative!);
                Information.Seed(ctx, n, a.Seed);
                Escalation.Record(ctx, actor, target, a.Action, $"{ctx.Content.Scenario.Society.Narratives[n].Name} starts spreading.");
                break;
            }
            case "cyber_attack":
            case "cyber":
            {
                int op = ctx.Content.Scenario.Conflict.Operation(a.Operation!);
                if (Cyber.CanUse(ctx, op, a.Effect!))
                {
                    Cyber.Use(ctx, op, a.Effect!);
                    break;
                }
                // D-014: the planned attack was defused; the attempt shows up as a detected intrusion, and the
                // attacker falls back to a weaker foothold if it has one.
                Escalation.Record(ctx, actor, target, "cyber_intrusion_detected",
                    $"An attempted intrusion into {ctx.Content.Scenario.Conflict.Operations[op].TargetProvince.Replace('_', ' ')} grid control is caught; {names[actor]} is blamed.");
                if (a.FallbackOperation is not null)
                {
                    int fb = ctx.Content.Scenario.Conflict.Operation(a.FallbackOperation);
                    if (Cyber.CanUse(ctx, fb, a.FallbackEffect ?? "disrupt")) Cyber.Use(ctx, fb, a.FallbackEffect ?? "disrupt");
                }
                break;
            }
            default:
                throw new ContentException($"Unknown action kind '{kind}'.");
        }
    }

    private static string Demonym(SimWorld w, int nation) => w.Nations.Adjectives[nation];

    private static string Note(ActionArgs a) => a.Note.Length > 0 ? $" ({a.Note}.)" : "";
}
