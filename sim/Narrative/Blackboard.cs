using Cascade.Sim.Conflict;
using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.World;

namespace Cascade.Sim.Narrative;

/// <summary>What a fact is read against.</summary>
public readonly record struct FactContext(SimWorld World, Balance Balance, ContentSet Content, int Day, int Hour);

/// <summary>
/// The blackboard of derived facts (spec Narrative tech: a queryable fact store over the simulation). Facts are
/// read straight from committed state; each key is compiled once, so an unknown fact or entity fails at load.
/// The fact list is in docs/storylets.md.
/// </summary>
public sealed class Blackboard
{
    private readonly Dictionary<string, Func<FactContext, Fixed>> _compiled = new(StringComparer.Ordinal);
    private readonly SimWorld _w;
    private readonly ContentSet _c;

    public Blackboard(SimWorld world, ContentSet content)
    {
        _w = world;
        _c = content;
    }

    public Fixed Get(string key, FactContext ctx) => Compile(key)(ctx);

    public bool Holds(Condition c, FactContext ctx) => c.Holds(Get(c.Fact, ctx));

    public bool All(IEnumerable<Condition> cs, FactContext ctx)
    {
        foreach (var c in cs) if (!Holds(c, ctx)) return false;
        return true;
    }

    public Func<FactContext, Fixed> Compile(string key)
    {
        if (_compiled.TryGetValue(key, out var f)) return f;
        try { f = Build(key); }
        catch (KeyNotFoundException e) { throw new ContentException($"Fact '{key}': {e.Message}"); }
        _compiled[key] = f;
        return f;
    }

    private static Fixed B(bool b) => b ? Fixed.One : Fixed.Zero;

    private Func<FactContext, Fixed> Build(string key)
    {
        var w = _w;
        var parts = key.Split('.');
        int player = w.Nations.Player;
        int rival = Enumerable.Range(0, w.Nations.Count).First(n => n != player);

        switch (parts)
        {
            case ["day"]: return c => Fixed.FromInt(c.Day);
            case ["hour"]: return c => Fixed.FromInt(c.Hour);
            case ["meter"]: return c => Escalation.Meter(c.World, player, rival);
            case ["rung"]: return c => Fixed.FromInt(c.Balance.Escalation.RungOf(Escalation.Meter(c.World, player, rival)));
            case ["approval"]: return c => c.World.Politics.Approval[player];
            case ["trust"]: return c => c.World.Politics.Trust[player];
            case ["pc"]: return c => c.World.Politics.PoliticalCapital.Pending(player); // Pending: spends earlier in this phase count
            case ["war_support"]: return c => c.World.Politics.WarSupport[player];
            case ["rally"]: return c => c.World.Politics.Rally[player];
            case ["legitimacy"]: return c => c.World.Politics.Legitimacy[player];
            case ["inflation"]: return c => c.World.Politics.Inflation[player];
            case ["backsliding"]: return c => c.World.Politics.Backsliding[player];
            case ["mobilization"]: return c => Fixed.FromInt(c.World.Nations.MobilizationLevel[player]);
            case ["emergency"]: return c => B(c.World.Politics.EmergencyActive[player]);
            case ["kia_total"]: return c => c.World.Politics.KiaTotal[player];
            case ["lines_calling"]: return c => Fixed.FromInt(c.World.Politics.LinesCalling[player]);
            case ["tension"]: return c => c.World.Director.Tension[0];
            case ["narrative_debt"]: return c => c.World.Director.NarrativeDebt[0];
            case ["people_dark"]: return c => Fixed.FromInt(PeopleDark(c.World, -1));
            case ["rival", "drone_ops_near_border"]: return c => Fixed.FromInt(c.World.Politics.DroneOpsNearBorder[rival]);
            case ["rival", "red_line_estimate"]: return c => c.World.Politics.RedLineEstimate[rival];
            case ["front", "active"]: return c => B(c.World.Front.ActiveUntil[player] >= c.Day || c.World.Front.ActiveUntil[rival] >= c.Day);
            case ["front", "locked"]: return c => B(c.World.Front.Locked[0]);

            case ["province", var id, var field]:
            {
                int p = w.Provinces.IdOf(id);
                return field switch
                {
                    "dark" => c => Fixed.FromInt(PeopleDark(c.World, p)),
                    "dark_share" => c => { long pop = Population(c.World, p); return pop == 0 ? Fixed.Zero : Fixed.Ratio(PeopleDark(c.World, p), pop); },
                    "crisis" => c => B(c.World.Provinces.InCrisis[p]),
                    "subs_down" => c => Fixed.FromInt(Enumerable.Range(0, c.World.Substations.Count)
                        .Count(s => c.World.Substations.Province[s] == p && c.World.Substations.State[s] != (int)SubstationState.Online)),
                    _ => throw Unknown(key),
                };
            }
            case ["doc", var good]:
            {
                int g = _c.Catalog.Good(good);
                return c => c.World.Stocks.DaysOfCover[c.World.Stocks.AtNation(player, g)];
            }
            case ["produced", var good]:
            {
                int g = _c.Catalog.Good(good);
                return c =>
                {
                    var sum = Fixed.Zero;
                    for (int f = 0; f < c.World.Facilities.Count; f++)
                    {
                        var r = c.Content.Catalog.Recipes[c.World.Facilities.Recipe[f]];
                        if (r.Outputs.Count > 0 && r.Outputs[0].Good == g) sum += c.World.Facilities.OutputToday[f];
                    }
                    return sum;
                };
            }
            case ["facility", var id, var field]:
            {
                int f = w.Facilities.IdOf(id);
                return field switch
                {
                    "power" => c => c.World.Facilities.Load[f] < 0 ? Fixed.One : c.World.Loads.PowerRatio[c.World.Facilities.Load[f]].ToFixed(),
                    "ramp" => c => Fixed.FromInt(c.World.Facilities.RampDone[f]),
                    "interruptions" => c => Fixed.FromInt(c.World.Facilities.Interruptions[f]),
                    "run" => c => c.World.Facilities.RunToday[f],
                    _ => throw Unknown(key),
                };
            }
            case ["service", var id, var field]:
            {
                int l = w.Loads.IdOf(id);
                return field switch
                {
                    "fuel_hours" => c => c.World.Loads.FuelHours[l],
                    "availability" => c => c.World.Loads.ServiceAvailability[l].ToFixed(),
                    _ => throw Unknown(key),
                };
            }
            case ["narrative", var id, var field]:
            {
                int n = w.Narratives.IdOf(id);
                return field switch
                {
                    "active" => c => B(c.World.Narratives.Active[n]),
                    "belief" => c =>
                    {
                        var nar = c.World.Narratives;
                        var max = Fine.Zero;
                        for (int s = 0; s < nar.Segments; s++) max = Fine.Max(max, nar.B[nar.At(n, s)]);
                        return max.ToFixed();
                    },
                    "established" => c => Fixed.FromInt(Enumerable.Range(0, c.World.Narratives.Segments).Count(s => c.World.Narratives.Established[c.World.Narratives.At(n, s)])),
                    "rumor_hours" => c => c.World.Narratives.RumorHours[n],
                    _ => throw Unknown(key),
                };
            }
            case ["op", var id, var field]:
            {
                int o = w.Operations.IdOf(id);
                return field switch
                {
                    "state" => c => Fixed.FromInt(c.World.Operations.State[o]),
                    "attribution" => c => c.World.Operations.Attribution[o].ToFixed(),
                    "access" => c => c.World.Operations.Access[o],
                    _ => throw Unknown(key),
                };
            }
            case ["import", var good, var field]:
            {
                int g = _c.Catalog.Good(good);
                return field switch
                {
                    "controlled" => c => B(Enumerable.Range(0, c.World.Imports.Count).Any(r => c.World.Imports.Good[r] == g && c.World.Imports.Blocked[r])),
                    "closed" => c => B(Enumerable.Range(0, c.World.Imports.Count).All(r => c.World.Imports.Good[r] != g || c.World.Imports.Closed[r] || c.World.Imports.Blocked[r])),
                    _ => throw Unknown(key),
                };
            }
            case ["faction", var id, var field]:
            {
                int f = w.Factions.IdOf(id);
                return field switch
                {
                    "approval" => c => c.World.Factions.Approval[f],
                    "leverage" => c => c.World.Factions.Leverage[f],
                    _ => throw Unknown(key),
                };
            }
            case ["segment", var id, var field]:
            {
                int s = w.Segments.IdOf(id);
                return field switch
                {
                    "satisfaction" => c => c.World.Segments.Satisfaction[s],
                    "trust" => c => c.World.Segments.Trust[s],
                    "power" => c => c.World.Segments.Need[c.World.Segments.At(s, Need.Power)],
                    _ => throw Unknown(key),
                };
            }
            case ["design", var id, var field]:
            {
                int d = w.Designs.IdOf(id);
                return field switch
                {
                    "effectiveness" => c => c.World.Designs.Effectiveness[d],
                    "cap" => c => c.World.Designs.Cap[d],
                    _ => throw Unknown(key),
                };
            }
            case ["character", var id, "opinion"]:
            {
                int ch = w.Characters.IdOf(id);
                return c => Opinion(c, ch);
            }
            case ["cast_opinion_min", var role]:
            {
                if (!_c.Scenario.Narrative.Characters.Any(x => x.Role == role)) throw Unknown(key);
                return c =>
                {
                    var min = Fixed.FromInt(1000);
                    for (int ch = 0; ch < c.World.Characters.Count; ch++)
                        if (c.World.Characters.Defs[ch].Role == role) min = Fixed.Min(min, Opinion(c, ch));
                    return min;
                };
            }
            case ["flag", var name]:
            {
                int f = w.Flags.IdOf(name);
                return c => c.World.Flags.Value[f];
            }
            case ["seed", var id, var field]:
            {
                int s = w.Seeds.IdOf(id);
                return field switch
                {
                    "ripe" => c => B(c.World.Seeds.State[s] == (int)SeedState.Ripe),
                    "live" => c => B(c.World.Seeds.State[s] is (int)SeedState.Live or (int)SeedState.Ripe),
                    _ => throw Unknown(key),
                };
            }
            case ["event", var subject]: return c => Fixed.FromInt(c.World.Log.Entries.Count(e => e.Subject == subject));
            case ["event_day", var subject]:
                return c =>
                {
                    var last = c.World.Log.Entries.LastOrDefault(e => e.Subject == subject);
                    return Fixed.FromInt(last?.Day ?? -1);
                };
        }
        throw Unknown(key);
    }

    public static Fixed Opinion(FactContext c, int ch)
    {
        var n = c.Balance.Narrative;
        return c.World.Characters.Opinion(ch, c.Day, n.MemoryGraveDays, n.MemoryMinorDays);
    }

    public static long PeopleDark(SimWorld w, int province)
    {
        long dark = 0;
        for (int l = 0; l < w.Loads.Count; l++)
        {
            if (province >= 0 && w.Loads.Province[l] != province) continue;
            if (province < 0 && w.Provinces.Owner[w.Loads.Province[l]] != w.Nations.Player) continue;
            dark += Fixed.FromInt(w.Loads.Population[l]).Times(Fine.One - w.Loads.ServedLast[l]).RoundToInt();
        }
        return dark;
    }

    private static long Population(SimWorld w, int province)
    {
        long pop = 0;
        for (int l = 0; l < w.Loads.Count; l++) if (w.Loads.Province[l] == province) pop += w.Loads.Population[l];
        return pop;
    }

    private static ContentException Unknown(string key) => new($"Unknown fact '{key}' (see docs/storylets.md for the fact list).");
}
