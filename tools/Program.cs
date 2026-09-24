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
try { opts = Runner.ParseOptions(args.Skip(command == "help" && args.Length == 0 ? 0 : 1).ToArray()); }
catch (ArgumentException e) { Console.Error.WriteLine(e.Message); return 1; }

try
{
    return command switch
    {
        "run" => Runner.Run(opts, printDaily: true),
        "hash" => Runner.Run(opts, printDaily: false),
        "play" => Play.Run(opts),
        "batch" => Batch.Run(opts),
        _ => Runner.Help(),
    };
}
catch (ContentException e)
{
    Console.Error.WriteLine($"Content error:\n{e.Message}");
    return 2;
}
