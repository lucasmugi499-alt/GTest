using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Narrative;

public sealed record ChronicleEntry(int Day, int Hour, string Date, string Kind, string Text);
public sealed record HistorianVerdict(string Name, string School, double Score, string Verdict);

/// <summary>
/// The Chronicle (concept: event sourcing; the Chronicle is a rendered view of the event log) and History's Verdict:
/// headlines, the timeline, five scores, three historians, and a Declassified epilogue.
/// </summary>
public sealed record ChronicleView(
    string Title,
    IReadOnlyList<ChronicleEntry> Headlines,
    IReadOnlyList<ChronicleEntry> Timeline,
    IReadOnlyDictionary<string, double> Scores,
    IReadOnlyList<HistorianVerdict> Verdicts,
    IReadOnlyList<string> Declassified)
{
    private static readonly string[] TimelineKinds =
        ["escalation", "grid", "narrative", "precedent", "mobilization", "emergency", "faction_action", "markets", "decision",
         "cyber", "trade", "politics", "information", "diplomacy", "red_line", "seed"];

    public static ChronicleView Build(Simulation sim)
    {
        var w = sim.World;
        var b = sim.Balance.Chronicle;
        var cd = sim.Scenario.Chronicle;
        int player = w.Nations.Player;
        int rival = Enumerable.Range(0, w.Nations.Count).First(n => n != player);

        ChronicleEntry E(LogEntry e) => new(e.Day, e.Hour, sim.Calendar.Describe(e.Day), e.Kind, e.Text);
        var headlines = w.Log.Entries.Where(e => e.Kind == "headline").Select(E).ToList();
        var timeline = w.Log.Entries.Where(e => TimelineKinds.Contains(e.Kind)).Select(E).ToList();

        int days = Math.Max(1, sim.Day);
        var d = w.Director;
        long pop = w.Segments.Population.Sum();

        // Survival: rungs climbed and whether it came to war.
        int startRung = sim.Balance.Escalation.RungOf(sim.Scenario.Conflict.StartMeter);
        var survival = Fixed.Hundred - b.SurvivalPerRung * Math.Max(0, d.MaxRung[0] - startRung)
                       - (d.MaxRung[0] >= b.WarRung ? b.SurvivalWarPenalty : Fixed.Zero);
        // Prosperity: chip and drone output over the campaign against Day 0.
        var chipsShare = d.ChipsBaseline[0] > Fixed.Zero ? d.ChipsTotal[0] / (d.ChipsBaseline[0] * days) : Fixed.One;
        var dronesShare = d.DronesBaseline[0] > Fixed.Zero ? d.DronesTotal[0] / (d.DronesBaseline[0] * days) : Fixed.One;
        var prosperity = Fixed.FromInt(50) * (chipsShare + dronesShare);
        // Liberty: backsliding, civil-liberties precedents, an emergency still in force.
        int civilUses = 0;
        for (int t = 0; t < w.Precedents.Types; t++)
            if (w.Precedents.Defs[t].CivilLiberties) civilUses += w.Precedents.Uses[w.Precedents.At(player, t)];
        var liberty = Fixed.Hundred - b.LibertyPerBacksliding * w.Politics.Backsliding[player] - b.LibertyPerCivilPrecedentUse * civilUses
                      - (w.Politics.EmergencyActive[player] ? b.LibertyEmergencyInForce : Fixed.Zero);
        // Sovereignty: magnets still on hand, and how well our drones still work against Varan's jamming.
        var magnetCover = w.Stocks.DaysOfCover[w.Stocks.AtNation(player, sim.Content.Catalog.Good(cd.MagnetsGood))];
        var sovereignty = Fixed.FromInt(50) * Fixed.Clamp(magnetCover / b.SovereigntyCoverDays, Fixed.Zero, Fixed.One)
                          + Fixed.FromInt(50) * w.Designs.Effectiveness[sim.Content.Catalog.Design(cd.DroneDesign)];
        // Humanity: people-days in the dark, and the dead.
        var darkShare = d.PeopleDarkDays[0] / (Fixed.FromInt(pop) * days);
        var humanity = Fixed.Hundred - b.HumanityBlackoutWeight * darkShare - b.HumanityPerDeath * w.Politics.KiaTotal[player];

        var scores = new Dictionary<string, Fixed>
        {
            ["survival"] = Clamp(survival), ["prosperity"] = Clamp(prosperity), ["liberty"] = Clamp(liberty),
            ["sovereignty"] = Clamp(sovereignty), ["humanity"] = Clamp(humanity),
        };

        var verdicts = cd.Historians.Select(h =>
        {
            var score = ChronicleDef.Axes.Aggregate(Fixed.Zero, (acc, a) => acc + h.Weights[a] * scores[a]);
            var text = score >= b.BandHigh ? h.High : score >= b.BandLow ? h.Mid : h.Low;
            return new HistorianVerdict(h.Name, h.School, score.ToDoubleForUi(), text);
        }).ToList();

        return new ChronicleView(
            $"{sim.Scenario.Name}: {sim.Calendar.Describe(0)} to {sim.Calendar.Describe(Math.Max(0, sim.Day - 1))}",
            headlines, timeline,
            scores.ToDictionary(kv => kv.Key, kv => kv.Value.ToDoubleForUi()),
            verdicts, Declassify(sim, player, rival));
    }

    private static Fixed Clamp(Fixed x) => Fixed.Clamp(x, Fixed.Zero, Fixed.Hundred);

    /// <summary>Concept: a final Declassified epilogue reveals what really happened.</summary>
    private static List<string> Declassify(Simulation sim, int player, int rival)
    {
        var w = sim.World;
        var lines = new List<string>();
        var rl = w.Politics.RedLine[rival];
        lines.Add($"{w.Nations.Names[rival]}'s hidden red line sat at {rl.ToString(0)} on the escalation meter; " +
                  $"your intelligence last estimated {w.Politics.RedLineEstimate[rival].ToString(0)}. " +
                  (w.Politics.RedLineCrossed[rival] ? "Your actions carried the meter across it, and it escalated in answer."
                                                    : "Nothing you did carried the meter across it."));
        for (int o = 0; o < w.Operations.Count; o++)
        {
            var def = w.Operations.Defs[o];
            var state = (OperationState)w.Operations.State[o];
            if (w.Operations.Attacker[o] == rival && state == OperationState.Used)
                lines.Add($"The attack on {def.TargetProvince.Replace('_', ' ')} was {w.Nations.Adjectives[rival]} state work{(def.Proxy ? ", run through a hacker collective as cover" : "")}. " +
                          $"By the end your analysts were {w.Operations.Attribution[o].ToFixed().ToString(2)} sure.");
            if (w.Operations.Attacker[o] == rival && state == OperationState.Detected && def.Targets.Count > 0)
                lines.Add($"{w.Nations.Adjectives[rival]} access in {def.TargetProvince.Replace('_', ' ')} grid control was found and closed before it could be used.");
            if (w.Operations.Attacker[o] == player && state == OperationState.Used)
                lines.Add($"{w.Nations.Names[player]}'s strike on {def.Id.Replace('_', ' ')} was traced back to it with {w.Operations.Attribution[o].ToFixed().ToString(2)} confidence abroad.");
        }
        if (w.Log.Entries.Any(e => e.Subject == "odd_logins.ignore"))
            lines.Add($"The odd logins at the eastern substation were {w.Nations.Names[rival]}'s foothold. A forensic sweep ({sim.Balance.Cyber.ForensicSweepCost.ToString(0)} Political Capital) in the first three days would have found it.");
        foreach (var c in w.Characters.Defs)
            foreach (var secret in c.Secrets)
                lines.Add($"{c.Name}: {secret}");
        return lines;
    }
}
