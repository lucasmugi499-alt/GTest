using System.Globalization;
using Cascade.Sim;
using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Economy;
using Cascade.Sim.Scheduling;

// Headless runner. M4 adds the text-playable mode (`play`), M6 the batch mode (`batch`).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

string command = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "help";
Dictionary<string, string> opts;
try { opts = ParseOptions(args.Skip(command == "help" && args.Length == 0 ? 0 : 1).ToArray()); }
catch (ArgumentException e) { Console.Error.WriteLine(e.Message); return 1; }

try
{
    return command switch
    {
        "run" => Run(opts, printDaily: true),
        "hash" => Run(opts, printDaily: false),
        _ => Help(),
    };
}
catch (ContentException e)
{
    Console.Error.WriteLine($"Content error:\n{e.Message}");
    return 2;
}

static int Run(Dictionary<string, string> opts, bool printDaily)
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
        Console.WriteLine("Day / date                 chips   drones  DoC magnet  DoC ctrl   dark   subs down  hospital fuel  crisis");
    }

    double? chipBaseline = null;
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

        Console.WriteLine(
            $"{sim.Calendar.Describe(day),-26} {chips / chipBaseline.Value,6:P0}  {snap.Good("fpv_strike_drone").ProducedToday,6:0}" +
            $"  {Cover(snap.Good("rare_earth_magnet")),10}  {Cover(snap.Good("flight_controller")),8}" +
            $"  {dark / 1e6,5:0.0}M  {snap.Substations.Count(x => !x.Online),9}  {hospital.FuelHours,11:0}h" +
            $"  {(crisis.Count == 0 ? "-" : string.Join(", ", crisis))}" +
            (checkpoint ? $"   [hash {snap.StateHash}]" : ""));

        if (verbose) PrintDetail(snap);
    }

    var hash = StateHasher.Format(sim.StateHash());
    Console.WriteLine(printDaily ? $"\nFinal state hash after day {lastDay}: {hash}" : hash);
    return 0;
}

static string Cover(GoodView g) => g.DaysOfCover is null ? "-" : $"{g.DaysOfCover:0.0}d{(g.Shortage == 2 ? "!!" : g.Shortage == 1 ? "!" : "")}";

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

static void PrintDetail(SimSnapshot snap)
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
}

static (int Day, int Hour) ParseDayHour(string text)
{
    var parts = text.Split(':');
    return (int.Parse(parts[0], CultureInfo.InvariantCulture), parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : -1);
}

static int Help()
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

static Dictionary<string, string> ParseOptions(string[] a)
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
