using System.Globalization;
using Cascade.Sim;
using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Scheduling;

// Headless runner. M4 adds the text-playable mode (`play`), M6 the batch mode (`batch`).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

string command = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "help";
var opts = ParseOptions(args.Skip(command == "help" && args.Length == 0 ? 0 : 1).ToArray());

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
    var sim = new Simulation(content, seed);

    if (printDaily)
    {
        Console.WriteLine($"CASCADE · {content.Scenario.Name} · seed {seed} · {SimInfo.Milestone}");
        Console.WriteLine($"Provinces: {string.Join(", ", sim.World.Provinces.Names)}");
        Console.WriteLine();
    }

    while (sim.Day <= lastDay)
    {
        int day = sim.Day;
        sim.StepDay();
        if (!printDaily) continue;
        var snap = SimSnapshot.Of(sim);
        var crisis = snap.Provinces.Where(p => p.InCrisis).Select(p => p.Name).ToList();
        bool checkpoint = day % content.Balance.Sim.HashCheckIntervalDays == 0 || day == lastDay;
        Console.WriteLine(
            $"{sim.Calendar.Describe(day),-26} crisis: {(crisis.Count == 0 ? "none" : string.Join(", ", crisis)),-12}" +
            (checkpoint ? $" state hash {snap.StateHash}" : ""));
    }

    var hash = StateHasher.Format(sim.StateHash());
    Console.WriteLine(printDaily ? $"\nFinal state hash after day {lastDay}: {hash}" : hash);
    return 0;
}

static int Help()
{
    Console.WriteLine($"""
        CASCADE headless runner · {SimInfo.Slice} · {SimInfo.Milestone}

        Commands:
          run   [--seed N] [--days D]   Play days 0..D (default: the whole scenario) and print one line per day.
          hash  [--seed N] [--days D]   Same, but print only the final state hash.

        Options:
          --content DIR   Use a different content folder (default: the repo's content/).
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
