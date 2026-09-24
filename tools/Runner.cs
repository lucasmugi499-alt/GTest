using System.Globalization;
using Cascade.Sim;
using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Economy;
using Cascade.Sim.Scheduling;

/// <summary>`run` and `hash`: play the scenario with no decisions and print a daily readout (or just the final hash).</summary>
static class Runner
{
    public static int Run(Dictionary<string, string> opts, bool printDaily)
    {
        var content = ContentSet.Load(opts.TryGetValue("content", out var dir) ? dir : ContentSet.FindContentDir());
        ulong seed = opts.TryGetValue("seed", out var s) ? ulong.Parse(s, CultureInfo.InvariantCulture) : content.Scenario.DefaultSeed;
        int lastDay = opts.TryGetValue("days", out var d) ? int.Parse(d, CultureInfo.InvariantCulture) : content.Scenario.LastDay;
        bool verbose = opts.ContainsKey("verbose");
        var sim = new Simulation(content, seed);
        var w = sim.World;
        int kestria = w.Nations.Player;

        // Test shocks until Varan's schedule arrives in M3.
        var tripped = new List<int>();
        if (opts.TryGetValue("trip", out var trip))
        {
            var (day, hour) = ParseDayHour(trip);
            int ossen = w.Provinces.IdOf("kestria_east_ossen");
            tripped = new[] { "ossen_north", "ossen_south", "ossen_industrial" }.Select(w.Substations.IdOf).ToList();
            sim.Events.Schedule(new SubstationDamageEvent(day, hour, ossen, tripped));
        }
        if (opts.TryGetValue("export-controls", out var ec))
        {
            int day = int.Parse(ec, CultureInfo.InvariantCulture);
            var goods = new[] { "rare_earth_magnet", "gallium" }.Select(sim.Content.Catalog.Good).ToList();
            sim.Events.Schedule(new ExportControlEvent(day, w.Nations.IdOf("varan"), goods, active: true));
        }
        string repair = opts.TryGetValue("repair", out var r) ? r : "none";

        if (printDaily)
        {
            Console.WriteLine($"CASCADE · {content.Scenario.Name} · seed {seed} · {SimInfo.Milestone}");
            if (tripped.Count > 0) Console.WriteLine($"Shock: 3 Ossen East substations damaged at day {trip}. Repair policy: {repair}.");
            if (opts.ContainsKey("export-controls")) Console.WriteLine($"Shock: Varan export controls on magnets and gallium from day {ec}.");
            Console.WriteLine();
            Console.WriteLine("Day / date                 chips  drones  DoC mag  DoC ctrl  dark   subs  hosp  appr  PC   trust rung  deepfake  crisis");
        }

        double? chipBaseline = null;
        int logShown = 0;
        while (sim.Day <= lastDay)
        {
            int day = sim.Day;
            if (repair != "none" && tripped.Count > 0)
                IssueRepairs(sim, kestria, tripped, repair);
            sim.StepDay();
            if (!printDaily) continue;

            var snap = SimSnapshot.Of(sim);
            var chips = snap.Good("legacy_chip").ProducedToday;
            chipBaseline ??= chips;
            var crisis = snap.Provinces.Where(p => p.InCrisis).Select(p => p.Name).ToList();
            var dark = snap.Provinces.Where(p => p.Owner == "kestria").Sum(p => p.PeopleWithoutPower);
            var hospital = snap.Services.First(x => x.Id == "ossen_general");
            bool checkpoint = day % content.Balance.Sim.HashCheckIntervalDays == 0 || day == lastDay;

            var deepfake = snap.Narratives.FirstOrDefault(n => n.Id == "president_fled");
            var p = snap.Politics;
            Console.WriteLine(
                $"{sim.Calendar.Describe(day),-26} {chips / chipBaseline.Value,5:P0} {snap.Good("fpv_strike_drone").ProducedToday,6:0}" +
                $"  {Cover(snap.Good("rare_earth_magnet")),7}  {Cover(snap.Good("flight_controller")),8}" +
                $"  {dark / 1e6,4:0.0}M  {snap.Substations.Count(x => !x.Online),4}  {hospital.FuelHours,3:0}h" +
                $"  {p.Approval,4:0}  {p.PoliticalCapital,3:0}  {p.Trust,4:0}  {p.Rung,3}" +
                $"  {(deepfake is null ? "-" : $"{deepfake.Believing.Max(),6:P0}{(deepfake.EstablishedSegments > 0 ? "*" : " ")}"),8}" +
                $"  {(crisis.Count == 0 ? "-" : string.Join(", ", crisis))}" +
                (checkpoint ? $"   [hash {snap.StateHash}]" : ""));
            for (; logShown < snap.Log.Count; logShown++)
            {
                var e = snap.Log[logShown];
                if (e.Kind is "ai_intent" or "red_line" && !verbose) continue;
                Console.WriteLine($"      {(e.Hour >= 0 ? $"{e.Hour:00}:00 " : "")}{e.Text}");
            }

            if (verbose) PrintDetail(snap);
        }

        var hash = StateHasher.Format(sim.StateHash());
        Console.WriteLine(printDaily ? $"\nFinal state hash after day {lastDay}: {hash}" : hash);
        return 0;
    }

    public static string Cover(GoodView g) => g.DaysOfCover is null ? "-" : $"{g.DaysOfCover:0.0}d{(g.Shortage == 2 ? "!!" : g.Shortage == 1 ? "!" : "")}";

    // A stand-in for the player until M4: send what's in reserve to the damaged substations, in ID order.
    static void IssueRepairs(Simulation sim, int nation, List<int> subs, string policy)
    {
        var w = sim.World;
        foreach (int s in subs)
        {
            if (w.Substations.State[s] == (int)Cascade.Sim.World.SubstationState.Online) continue;
            if ((policy == "spare" || policy == "both") && w.Substations.Repair[s] == 0 && w.Nations.SpareTransformers[nation] > 0)
                sim.Orders.Enqueue(new RepairSubstationOrder(nation, s, RepairChoice.Spare));
            if ((policy == "mobile" || policy == "both") && !w.Substations.MobileAssigned[s] && w.Nations.MobileSubstations[nation] > 0)
                sim.Orders.Enqueue(new RepairSubstationOrder(nation, s, RepairChoice.Mobile));
        }
    }

    public static void PrintDetail(SimSnapshot snap)
    {
        foreach (var p in snap.Provinces.Where(p => p.Owner == "kestria"))
            Console.WriteLine($"    grid {p.Name,-17} served {p.PowerServed,5:P0}  dark {p.PeopleWithoutPower,9:N0}{(p.GridCollapsed ? "  COLLAPSED" : "")}");
        foreach (var f in snap.Facilities.Where(f => f.IsFab))
            Console.WriteLine($"    fab  {f.Name,-22} out {f.OutputToday,10:N0}  power {f.PowerRatio,5:P0}  yield {f.Yield:0.000}  ramp {f.RampDays,2}d  scrapped {f.WipScrapped:N0} wafers");
        foreach (var s in snap.Substations.Where(s => !s.Online))
            Console.WriteLine($"    sub  {s.Id,-18} capacity {s.CapacityFactor,4:P0}  repair {s.Repair} ({s.RepairDaysLeft:0.0} days left){(s.MobileUnit ? "  mobile unit" : "")}");
        foreach (var s in snap.Services.Where(s => s.Province == "kestria_east_ossen"))
            Console.WriteLine($"    svc  {s.Id,-18} powered {s.Powered,4:P0}  fuel {s.FuelHours,5:0.0}/{s.TankHours:0}h  ran {s.Availability,4:P0} of the day");
        var low = snap.Goods.Where(g => g.DaysOfCover is not null).OrderBy(g => g.DaysOfCover).Take(4);
        Console.WriteLine($"    lowest cover: {string.Join(", ", low.Select(g => $"{g.Id} {Cover(g)}"))}");
        foreach (var d in snap.Designs) Console.WriteLine($"    design {d.Id}: effectiveness {d.Effectiveness:0.000} (cap {d.Cap:0.000})");
        var pv = snap.Politics;
        Console.WriteLine($"    politics: approval {pv.Approval:0.0}  PC {pv.PoliticalCapital:0}  trust {pv.Trust:0.0}  war support {pv.WarSupport:0.0}  rally {pv.Rally:0}  exhaustion {pv.WarExhaustion:0.0}  inflation {pv.Inflation:0.0}%");
        Console.WriteLine($"    escalation: meter {pv.EscalationMeter:0.0} (rung {pv.Rung})  red line estimate {pv.RivalRedLineEstimate:0}  insurance ×{pv.InsuranceMultiplier:0}  lines calling {pv.ShippingLinesCalling}/{pv.ShippingLines}  mobilization {pv.Mobilization}");
        foreach (var s in snap.Segments)
            Console.WriteLine($"    seg  {s.Name,-24} sat {s.Satisfaction,5:0.0}  align {s.Align,5:0.0}  trust {s.Trust,5:0.0}  needs {string.Join(" ", s.Needs.Select(n => $"{n,3:0}"))}");
        foreach (var f in snap.Factions) Console.WriteLine($"    fac  {f.Name,-24} approval {f.Approval,5:0.0}  leverage {f.Leverage,5:0.0}");
        foreach (var n in snap.Narratives)
            Console.WriteLine($"    nar  {n.Id,-22} believing {string.Join(" ", n.Believing.Select(b => $"{b,5:P1}"))}  rumor {(n.RumorHours < 0 ? "-" : $"{n.RumorHours:0}h")}");
        Console.WriteLine($"    front: {(snap.Front.Active ? "ACTIVE" : "quiet")}  {string.Join("  ", snap.Front.Brigades.Where(b => b.Strength > 0).Select(b => $"{b.Name} {b.Strength:N0} ({b.Killed:N0} killed)"))}");
    }

    static (int Day, int Hour) ParseDayHour(string text)
    {
        var parts = text.Split(':');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture), parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : -1);
    }

    public static int Help()
    {
        Console.WriteLine($"""
            CASCADE headless runner · {SimInfo.Slice} · {SimInfo.Milestone}

            Commands:
              run   [options]   Play days 0..D and print one line per day.
              hash  [options]   Same, but print only the final state hash.

            Options:
              --seed N                 Campaign seed (default: the scenario's).
              --days D                 Last day to play (default: the whole scenario).
              --trip DAY[:HOUR]        Damage the three Ossen East substations (north, south, industrial).
              --repair none|spare|mobile|both
                                       What to send to the damaged substations (default none).
              --export-controls DAY    Varan stops exporting magnets and gallium.
              --verbose                Print grid, fab, service and stockpile detail every day.
              --content DIR            Use a different content folder (default: the repo's content/).

            Example: the Day 4 blackout with repairs, and the Day 6 squeeze:
              dotnet run --project tools -- run --trip 4:2 --repair both --export-controls 6
            """);
        return 0;
    }

    public static Dictionary<string, string> ParseOptions(string[] a)
    {
        var o = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].StartsWith("--")) throw new ArgumentException($"Unexpected argument '{a[i]}'.");
            string key = a[i][2..];
            string value = i + 1 < a.Length && !a[i + 1].StartsWith("--") ? a[++i] : "true";
            o[key] = value;
        }
        return o;
    }
}
