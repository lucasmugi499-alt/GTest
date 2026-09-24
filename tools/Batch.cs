using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Cascade.Sim;
using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Narrative;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

/// <summary>
/// `batch`: plays many campaigns with random choices and reports the spec's automated-campaign checks (spec Balance
/// targets and test plan), the scenario's own targets, and tick timing.
///   --seeds N          campaigns to run (default 1000), seeds 1..N
///   --threads T        parallel workers for the metrics runs (default: all cores)
///   --timing-seeds K   campaigns re-run one at a time for clean tick timings (default 50)
///   --replay-every R   re-run every R-th campaign and compare the final hash (default 10; 1 = all)
///   --report FILE      also write the report as Markdown
///   --force S=C[,S=C]  always answer storylet S with choice C ("none": never answer it); for balance experiments
/// </summary>
static class Batch
{
    private sealed record Campaign(
        ulong Seed, ulong Hash, bool Replayed, bool ReplayMatches,
        Dictionary<string, int> Fires, List<int> MajorDays, int MaxRung, int CrisisDays,
        double? ChipCut, int? RecoveryDays, bool Attacked, string? Day10, bool DeepfakeEstablished,
        double OutputEndShare, double Approval, double PoliticalCapital, double Trust,
        bool VaranOffensive, string? DeepfakeChoice, double DeepfakePeak, int? DeepfakeTippedDay,
        IReadOnlyDictionary<string, double> Scores, List<double> TickMs);

    public static int Run(Dictionary<string, string> opts)
    {
        var content = ContentSet.Load(opts.TryGetValue("content", out var dir) ? dir : ContentSet.FindContentDir());
        int n = Int(opts, "seeds", 1000);
        int threads = Int(opts, "threads", Environment.ProcessorCount);
        int timingSeeds = Math.Min(n, Int(opts, "timing-seeds", 50));
        int replayEvery = Math.Max(1, Int(opts, "replay-every", 10));
        _force = opts.TryGetValue("force", out var f)
            ? f.Split(',').Select(x => x.Split('=')).ToDictionary(x => x[0], x => x[1]) : [];

        Console.WriteLine($"CASCADE batch · {content.Scenario.Name} · {n} campaigns with random choices · {threads} threads");
        var sw = Stopwatch.StartNew();
        var results = new ConcurrentBag<Campaign>();
        int done = 0;
        Parallel.For(1, n + 1, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
        {
            ulong seed = (ulong)i;
            var c = Play(content, seed, timeTicks: false);
            bool replay = i % replayEvery == 0;
            var again = replay ? Play(content, seed, timeTicks: false) : null;
            results.Add(c with { Replayed = replay, ReplayMatches = again is null || again.Hash == c.Hash });
            int d = Interlocked.Increment(ref done);
            if (d % 100 == 0) Console.WriteLine($"  {d}/{n} campaigns · {sw.Elapsed.TotalSeconds:0}s");
        });
        Console.WriteLine($"Metrics runs done in {sw.Elapsed.TotalSeconds:0.0}s. Timing {timingSeeds} campaigns one at a time…");

        var ticks = new List<double>();
        for (int i = 1; i <= timingSeeds; i++) ticks.AddRange(Play(content, (ulong)i, timeTicks: true).TickMs);

        var report = Report(content, results.OrderBy(r => r.Seed).ToList(), ticks, n);
        Console.WriteLine(report);
        if (opts.TryGetValue("report", out var path))
        {
            File.WriteAllText(path, report);
            Console.WriteLine($"Report written to {path}");
        }
        return 0;
    }

    private static Dictionary<string, string> _force = [];

    private static int Int(Dictionary<string, string> o, string key, int fallback) =>
        o.TryGetValue(key, out var v) ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;

    private static Campaign Play(ContentSet content, ulong seed, bool timeTicks)
    {
        var sim = new Simulation(content, seed);
        var w = sim.World;
        var skip = new HashSet<long>();
        var tickMs = new List<double>();
        var chips = new List<double>();
        var ossenDark = new List<long>();
        int ossen = w.Provinces.IdOf("kestria_east_ossen");
        var sw = new Stopwatch();

        while (!sim.IsFinished)
        {
            sw.Restart();
            StepResult r;
            do
            {
                foreach (var inst in sim.PendingDecisions.ToList())
                {
                    if (skip.Contains(inst.Seq)) continue;
                    var def = sim.Narrative.Def.Storylets[inst.Storylet];
                    if (!_force.TryGetValue(def.Id, out var choice)) continue;
                    skip.Add(inst.Seq);
                    if (choice != "none") sim.Orders.Enqueue(new ChooseStoryletOrder(w.Nations.Player, inst.Seq, def.ChoiceIndex(choice)));
                }
                Autopilot.Answer(sim, AutoMode.Random, skip);
                r = sim.StepHour();
            } while (r == StepResult.HourAdvanced);
            sw.Stop();
            if (timeTicks) tickMs.Add(sw.Elapsed.TotalMilliseconds);
            chips.Add(w.Director.ChipsLast[0].ToDoubleForUi());
            ossenDark.Add(Blackboard.PeopleDark(w, ossen));
        }

        var fires = new Dictionary<string, int>();
        var majors = new List<int>();
        foreach (var inst in w.Storylets.Instances)
        {
            var s = sim.Narrative.Def.Storylets[inst.Storylet];
            fires[s.Id] = fires.GetValueOrDefault(s.Id) + 1;
            if (s.Tier == Tier.Major) majors.Add(inst.Day);
        }

        // The Day 4 attack: its day, and (spec regression) the chip cut over the next 7 days against the days before.
        var attack = w.Log.Entries.FirstOrDefault(e => e.Subject == "disruptive_cyber" && e.Actor != w.Nations.Keys[w.Nations.Player] && e.Text.Contains(w.Provinces.Names[ossen]));
        double? cut = null;
        int? recovery = null;
        if (attack is not null)
        {
            int a = attack.Day;
            var before = chips.Take(a).ToList();
            var after = chips.Skip(a).Take(7).ToList();
            if (before.Count > 0 && after.Count == 7) cut = 1 - after.Average() / before.Average();
            // Regional blackout recovery: days until at least 90% of the people cut off at the peak have power again.
            long peak = ossenDark.Skip(a).DefaultIfEmpty().Max();
            for (int d = a + 1; d < ossenDark.Count && peak > 0; d++) if (ossenDark[d] <= peak / 10) { recovery = d - a; break; }
        }

        var day10 = w.Storylets.Instances.FirstOrDefault(i => sim.Narrative.Def.Storylets[i.Storylet].Id == "day10_decision");
        var nar = w.Narratives;
        int deepfake = nar.IdOf("president_fled");
        var d0 = w.Director;
        double endShare = (d0.ChipsLast[0] + d0.DronesLast[0] * 1000).ToDoubleForUi() /
                          Math.Max(1, (d0.ChipsBaseline[0] + d0.DronesBaseline[0] * 1000).ToDoubleForUi());
        var chronicle = ChronicleView.Build(sim);
        int p = w.Nations.Player;
        var dfInst = w.Storylets.Instances.FirstOrDefault(i => sim.Narrative.Def.Storylets[i.Storylet].Id == "deepfake");
        string? dfChoice = dfInst is null || dfInst.Pending ? null : sim.Narrative.Def.Storylets[dfInst.Storylet].Choices[dfInst.Choice].Id;
        double dfPeak = Enumerable.Range(0, nar.Segments).Max(s => nar.B[nar.At(deepfake, s)].ToDoubleForUi());
        var tipped = w.Log.Entries.FirstOrDefault(e => e.Kind == "narrative" && e.Subject == "president_fled");
        bool offensive = w.Log.Entries.Any(e => e.Subject == "declare_war");
        return new Campaign(seed, sim.StateHash(), false, true, fires, majors, d0.MaxRung[0], d0.CrisisDays[0], cut, recovery,
            attack is not null, day10 is null || day10.Pending ? null : sim.Narrative.Def.Storylets[day10.Storylet].Choices[day10.Choice].Id,
            Enumerable.Range(0, nar.Segments).Any(s => nar.Established[nar.At(deepfake, s)]), endShare,
            w.Politics.Approval[p].ToDoubleForUi(), w.Politics.PoliticalCapital[p].ToDoubleForUi(), w.Politics.Trust[p].ToDoubleForUi(),
            offensive, dfChoice, dfPeak, tipped?.Day, chronicle.Scores, tickMs);
    }

    private static string Report(ContentSet content, List<Campaign> runs, List<double> ticks, int n)
    {
        var b = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        string P(double x) => x.ToString("P1", inv);
        string F(double x, string f = "0.0") => x.ToString(f, inv);
        void Row(string check, string target, string result, string verdict) => b.AppendLine($"| {check} | {target} | {result} | {verdict} |");
        string Band(double v, double lo, double hi) => v >= lo && v <= hi ? "PASS" : "FAIL";

        var library = content.Scenario.Narrative.Storylets;
        var allFires = runs.SelectMany(r => r.Fires).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));
        int totalFires = allFires.Values.Sum();
        var never = library.Where(s => !allFires.ContainsKey(s.Id)).Select(s => s.Id).ToList();
        // D-047: arc beats fire every game by design and are left out; generics are capped at twice the even share
        // until the library passes the balance's size, then at the spec's 2%.
        var generic = library.Where(s => s.Arc is null).Select(s => s.Id).ToHashSet();
        var genericFires = allFires.Where(kv => generic.Contains(kv.Key)).ToList();
        int totalGeneric = genericFires.Sum(kv => kv.Value);
        var top = genericFires.OrderByDescending(kv => kv.Value).First();
        var check = content.Balance.Narrative;
        double cap = library.Count > check.StoryletCapLibrarySize ? check.StoryletShareCap.ToDoubleForUi()
            : check.StoryletEvenShareMultiple.ToDoubleForUi() / generic.Count;

        ticks.Sort();
        double Pct(double q) => ticks.Count == 0 ? 0 : ticks[Math.Min(ticks.Count - 1, (int)Math.Ceiling(q * ticks.Count) - 1)];

        b.AppendLine($"# CASCADE balance report — {content.Scenario.Name}");
        b.AppendLine();
        b.AppendLine($"{n} campaigns (seeds 1–{n}), Day 0 to Day {content.Scenario.LastDay}, every decision answered at random by the autopilot. " +
                     $"Tick timings from {ticks.Count} days ({ticks.Count / (content.Scenario.LastDay + 1)} campaigns run one at a time), Release build.");
        b.AppendLine();
        b.AppendLine("## The spec's automated campaign checks");
        b.AppendLine();
        b.AppendLine("| Check | Pass band | Result | Verdict |");
        b.AppendLine("| --- | --- | --- | --- |");
        Row("Storylets that never fire", "under 5% of the library", $"{never.Count} of {library.Count} ({P((double)never.Count / library.Count)}){(never.Count > 0 ? ": " + string.Join(", ", never) : "")}",
            (double)never.Count / library.Count < 0.05 ? "PASS" : "FAIL");
        double topShare = (double)top.Value / totalGeneric;
        Row("Any one generic storylet's share of generic fires",
            library.Count > check.StoryletCapLibrarySize ? $"under {P(cap)}" : $"under {P(cap)} ({F(check.StoryletEvenShareMultiple.ToDoubleForUi(), "0")}× the even share of {generic.Count}; 2% once the library passes {check.StoryletCapLibrarySize})",
            $"{top.Key}: {P(topShare)}", topShare < cap ? "PASS — see note 1" : "FAIL — see note 1");
        Row("Campaigns where some nation doubles its territory", "5–15%", "no conquest in the slice", "N/A");
        var collapse = runs.Count(r => r.OutputEndShare < 0.5);
        Row("Campaigns where output falls more than 50% (GDP proxy)", "under 10%", $"{collapse} ({P((double)collapse / n)})",
            (double)collapse / n < 0.10 ? "PASS" : "FAIL — see note 2");
        var threshold = runs.Count(r => r.MaxRung >= 7);
        Row("Campaigns that cross the strategic threshold", "under 1%", $"{threshold} ({P((double)threshold / n)})", (double)threshold / n < 0.01 ? "PASS" : "FAIL");
        var replayed = runs.Where(r => r.Replayed).ToList();
        Row("Identical state hash on replay", "100%", $"{replayed.Count(r => r.ReplayMatches)} of {replayed.Count} replayed",
            replayed.All(r => r.ReplayMatches) ? "PASS" : "FAIL");
        Row("Daily tick, 95th percentile", "under 50 ms", $"{F(Pct(0.95), "0.00")} ms (median {F(Pct(0.5), "0.00")}, 99th {F(Pct(0.99), "0.00")}, max {F(ticks.LastOrDefault(), "0.00")})",
            Pct(0.95) < 50 ? "PASS" : "FAIL");
        b.AppendLine();

        b.AppendLine("## The scenario's own targets");
        b.AppendLine();
        b.AppendLine("| Check | Target | Result | Verdict |");
        b.AppendLine("| --- | --- | --- | --- |");
        var cuts = runs.Where(r => r.ChipCut is not null).Select(r => r.ChipCut!.Value).ToList();
        int inBand = cuts.Count(c => c >= 0.35 && c <= 0.45);
        Row("Eastern blackout cuts legacy chip output (spec regression)", "35–45%",
            $"{inBand} of {cuts.Count} attacked campaigns in band; mean {P(cuts.DefaultIfEmpty().Average())}, range {P(cuts.DefaultIfEmpty().Min())}–{P(cuts.DefaultIfEmpty().Max())}",
            cuts.Count > 0 && inBand == cuts.Count ? "PASS" : "FAIL");
        int attacked = runs.Count(r => r.Attacked);
        Row("Day 4 attack lands (else the seed was defused, D-014)", "most campaigns", $"{attacked} ({P((double)attacked / n)})", "INFO");
        var rec = runs.Where(r => r.Attacked).Select(r => r.RecoveryDays).ToList();
        var recovered = rec.Where(x => x is not null).Select(x => x!.Value).ToList();
        Row("Regional blackout recovery (90% of the dark back on)", "3–14 days", recovered.Count == 0 ? "never recovered within 90 days" :
            $"{recovered.Count} of {rec.Count} recovered; median {Median(recovered)} days; {recovered.Count(x => x >= 3 && x <= 14)} within 3–14",
            "INFO — see note 3");
        double crisisShare = runs.Average(r => r.CrisisDays / (double)(content.Scenario.LastDay + 1));
        Row("Time in Crisis Time", "5–10% of days (over a 20-year campaign)", P(crisisShare), "INFO — see note 4");
        var gaps = runs.SelectMany(r => r.MajorDays.Distinct().OrderBy(d => d).Zip(r.MajorDays.Distinct().OrderBy(d => d).Skip(1), (a, c) => c - a)).ToList();
        Row("Major story beat", "every 45–90 days (by Director)", $"{F(runs.Average(r => r.MajorDays.Count))} majors per campaign; median gap {Median(gaps)} days", "INFO — see note 5");
        Row("Critical inputs at start", "20–60 Days of Cover", string.Join(", ", content.Scenario.InitialCoverDays.Select(x => $"{x.Good} {x.Days}")),
            content.Scenario.InitialCoverDays.All(x => x.Days >= Fixed.FromInt(20) && x.Days <= Fixed.FromInt(60)) ? "PASS" : "FAIL");
        int war = runs.Count(r => r.MaxRung >= 5);
        Row("Campaigns where the meter reaches rung 5+", "rare in a grey-zone crisis", $"{war} ({P((double)war / n)})", "INFO");
        int offensives = runs.Count(r => r.VaranOffensive);
        Row("Campaigns where Varan launches an offensive", "only after the player crosses its red line", $"{offensives} ({P((double)offensives / n)})", "INFO");
        int established = runs.Count(r => r.DeepfakeEstablished);
        var tipDays = runs.Where(r => r.DeepfakeTippedDay is not null).Select(r => r.DeepfakeTippedDay!.Value).ToList();
        Row("Deepfake takes hold somewhere", "counter-measures should matter", $"{established} ({P((double)established / n)}); median day {Median(tipDays)}", "INFO");
        Row("  Deepfake peak believing share at day 90, all campaigns", "", P(runs.Average(r => r.DeepfakePeak)), "INFO");
        foreach (var g in runs.Where(r => r.DeepfakeChoice is not null).GroupBy(r => r.DeepfakeChoice!).OrderBy(g => g.Key))
            Row($"  Deepfake answered with '{g.Key}'", "lower peak belief than doing nothing", $"peak believing share {P(g.Average(r => r.DeepfakePeak))} at day 90 ({g.Count()} campaigns)", "INFO");
        b.AppendLine();

        b.AppendLine("## Outcomes");
        b.AppendLine();
        b.AppendLine($"- Day 10 decision, chosen at random among what was affordable: {string.Join(", ", runs.GroupBy(r => r.Day10 ?? "(not reached)").OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"))}");
        b.AppendLine($"- End of campaign, mean: Approval {F(runs.Average(r => r.Approval))}, Political Capital {F(runs.Average(r => r.PoliticalCapital))}, Trust {F(runs.Average(r => r.Trust))}");
        foreach (var axis in ChronicleDef.Axes)
            b.AppendLine($"- Chronicle {axis}: mean {F(runs.Average(r => r.Scores[axis]))}, 10th–90th percentile {F(Quantile(runs.Select(r => r.Scores[axis]), 0.1), "0")}–{F(Quantile(runs.Select(r => r.Scores[axis]), 0.9), "0")}");
        b.AppendLine($"- Storylet fires per campaign: {F((double)totalFires / n)}. Most frequent: {string.Join(", ", allFires.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{kv.Key} {P((double)kv.Value / totalFires)}"))}");
        b.AppendLine();
        b.AppendLine("## Notes");
        b.AppendLine();
        b.AppendLine($"1. Generic storylets only (D-047): the {library.Count - generic.Count} scenario-arc beats fire in nearly every campaign by design. The spec's 2% needs a library of 60+; until then the cap is twice the even share.");
        b.AppendLine("2. There is no GDP in the slice. The proxy is legacy chips plus drones (weighted ×1,000) on the last day against Day 0.");
        b.AppendLine("3. Three transformers are wrecked and Kestria holds two spares; a new one takes 730+ days. Only the lights_out choice that sends both spares to the homes (homes_first) relights 90% of the people, in 14 days. The other choices leave part of Ossen East on a 30% mobile unit for the rest of the slice, by design.");
        b.AppendLine("4. Counts days on which any Kestrian province is in Crisis Time. The spec's 5–10% band is for a 20-year campaign. Under D-046 a province is in crisis while any load gets under 70% of its demand. Loads can't be rerouted in the slice's grid, so a wrecked substation on a 30% mobile unit (a new transformer takes 730+ days) is a live blackout until Day 90.");
        b.AppendLine("5. The slice's ten arc beats land in the first 10 days by design (D-012). The Historian's 90-day rhythm governs everything after.");
        return b.ToString();
    }

    private static double Median(List<int> xs) => xs.Count == 0 ? 0 : Quantile(xs.Select(x => (double)x), 0.5);

    private static double Quantile(IEnumerable<double> xs, double q)
    {
        var s = xs.OrderBy(x => x).ToList();
        if (s.Count == 0) return 0;
        return s[Math.Min(s.Count - 1, (int)Math.Floor(q * (s.Count - 1)))];
    }
}
