using System.Globalization;
using Cascade.Sim;
using Cascade.Sim.Conflict;
using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Economy;
using Cascade.Sim.Narrative;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

/// <summary>
/// `play`: The Veyl Crossing as text. Major decisions stop the clock and ask for a number; minor cards wait in the
/// Brief. Crisis days step hour by hour. Everything you type can be saved with --record and replayed by piping the
/// file back in; if input runs out, the autopilot answers and the campaign plays to the end.
/// </summary>
static class Play
{
    private sealed class Input(StreamWriter? record)
    {
        public bool Ended { get; private set; }

        public string Read(string prompt)
        {
            if (Ended) return "";
            Console.Write(prompt);
            var line = Console.ReadLine();
            if (line is null)
            {
                Ended = true;
                Console.WriteLine();
                Console.WriteLine("(input ended: the autopilot answers from here and the campaign plays to the end)");
                return "";
            }
            if (Console.IsInputRedirected) Console.WriteLine(line);
            record?.WriteLine(line);
            record?.Flush();
            return line.Trim();
        }

        /// <summary>Reads a number. Enter returns <paramref name="fallback"/> unless <paramref name="required"/> (then it asks again).</summary>
        public int Number(string prompt, int min, int max, int fallback, bool required = false)
        {
            while (true)
            {
                var t = Read(prompt);
                if (Ended) return fallback;
                if (t.Length == 0 && !required) return fallback;
                if (int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= min && n <= max) return n;
                Console.WriteLine($"  Type a number from {min} to {max}.");
            }
        }
    }

    public static int Run(Dictionary<string, string> opts)
    {
        var content = ContentSet.Load(opts.TryGetValue("content", out var dir) ? dir : ContentSet.FindContentDir());
        ulong seed = opts.TryGetValue("seed", out var s) ? ulong.Parse(s, CultureInfo.InvariantCulture) : content.Scenario.DefaultSeed;
        var sim = new Simulation(content, seed);
        using var record = opts.TryGetValue("record", out var recPath) ? new StreamWriter(recPath) : null;
        var input = new Input(record);
        AutoMode? auto = opts.TryGetValue("auto", out var a) ? Enum.Parse<AutoMode>(a, ignoreCase: true) : null;
        var answered = new HashSet<long>();
        int logShown = 0;

        Intro(sim);
        while (!sim.IsFinished)
        {
            answered.RemoveWhere(seq => sim.World.Storylets.Find(seq) is { Pending: false });
            logShown = PrintNewLog(sim, logShown);

            if (auto is not null || input.Ended)
            {
                Autopilot.Answer(sim, auto ?? AutoMode.Default, answered);
                sim.StepDay();
                continue;
            }

            var major = sim.PendingDecisions.FirstOrDefault(i => !answered.Contains(i.Seq) && sim.Narrative.Def.Storylets[i.Storylet].Tier == Tier.Major);
            if (major is not null)
            {
                Decide(sim, input, major, answered);
                continue;
            }

            Status(sim);
            var cmd = input.Read(Prompt(sim, answered)).ToLowerInvariant();
            switch (cmd)
            {
                case "": sim.StepHour(); break;
                case "d": sim.StepDay(); break;
                case "w": Advance(sim, answered, 7); break;
                case "b": Brief(sim, input, answered); break;
                case "o": Orders(sim, input); break;
                case "s": Runner.PrintDetail(SimSnapshot.Of(sim)); break;
                case "l": foreach (var e in sim.World.Log.Entries.TakeLast(30)) Console.WriteLine($"  {sim.Calendar.Describe(e.Day)} {(e.Hour >= 0 ? $"{e.Hour:00}:00 " : "")}{e.Text}"); break;
                case "c": PrintChronicle(sim); break;
                case "q":
                    Console.WriteLine("Leaving the Office early.");
                    PrintChronicle(sim);
                    return 0;
                case "h": case "?": Help(); break;
                default: Console.WriteLine("  Unknown command. Type h for help."); break;
            }
        }
        PrintNewLog(sim, logShown);
        Console.WriteLine();
        Console.WriteLine("Day 90 is over.");
        PrintChronicle(sim);
        Console.WriteLine($"Final state hash: {StateHasher.Format(sim.StateHash())}");
        return 0;
    }

    private static void Intro(Simulation sim)
    {
        Console.WriteLine($"""
            ============================================================================================
             CASCADE · {sim.Scenario.Name} · seed {sim.Seed}
            ============================================================================================
             You are the Office: the head of government of the Republic of Kestria, 34 million people,
             a strong electronics sector, and most of its rare earth magnets bought from the larger
             Varan Union. The two share the Ossen River border at the Veyl Crossing.

             The clock advances a day at a time, or an hour at a time in Crisis Time. Big decisions stop
             the clock. Smaller ones wait in your Brief, and quietly decide themselves if you ignore them.
            """);
        Help();
    }

    private static void Help() => Console.WriteLine("""
         Commands:  Enter = next hour (or day)   d = to end of day   w = a week   b = the Brief (minor cards)
                    o = give orders   s = full status   l = recent log   c = the Chronicle so far   q = quit
        """);

    private static string Prompt(Simulation sim, HashSet<long> answered)
    {
        int brief = sim.PendingDecisions.Count(i => !answered.Contains(i.Seq) && sim.Narrative.Def.Storylets[i.Storylet].Tier == Tier.Minor);
        string when = sim.Hour >= 0 ? $"{sim.Calendar.Describe(sim.Day)} {sim.Hour:00}:00 · CRISIS" : sim.Calendar.Describe(sim.Day);
        return $"[{when}{(brief > 0 ? $" · Brief: {brief}" : "")}] > ";
    }

    private static void Status(Simulation sim)
    {
        var snap = SimSnapshot.Of(sim);
        var p = snap.Politics;
        var dark = snap.Provinces.Where(x => x.Owner == "kestria").Sum(x => x.PeopleWithoutPower);
        var deepfake = snap.Narratives.FirstOrDefault(n => n.Id == "president_fled");
        Console.WriteLine(
            $"  chips {snap.Good("legacy_chip").ProducedToday / 1e6:0.00}M/day  drones {snap.Good("fpv_strike_drone").ProducedToday:0}/day" +
            $"  magnets {Runner.Cover(snap.Good("rare_earth_magnet"))}  controllers {Runner.Cover(snap.Good("flight_controller"))}" +
            $"  dark {dark / 1e6:0.0}M  approval {p.Approval:0}  capital {p.PoliticalCapital:0}  trust {p.Trust:0}  rung {p.Rung}" +
            (deepfake is null ? "" : $"  deepfake {deepfake.Believing.Max():P0}{(deepfake.RumorHours > 0 ? $" (tips in ~{deepfake.RumorHours:0}h)" : "")}"));
    }

    private static int PrintNewLog(Simulation sim, int shown)
    {
        var log = sim.World.Log.Entries;
        for (; shown < log.Count; shown++)
        {
            var e = log[shown];
            if (e.Kind is "ai_intent" or "storylet" or "decision") continue;
            Console.WriteLine($"  · {sim.Calendar.Describe(e.Day)}{(e.Hour >= 0 ? $" {e.Hour:00}:00" : "")}  {(e.Kind == "headline" ? "HEADLINE: " : "")}{e.Text}");
        }
        return shown;
    }

    private static void Advance(Simulation sim, HashSet<long> answered, int days)
    {
        int end = Math.Min(sim.Scenario.LastDay + 1, sim.Day + days);
        while (sim.Day < end && !sim.IsFinished)
        {
            sim.StepDay();
            if (sim.PendingDecisions.Any(i => !answered.Contains(i.Seq) && sim.Narrative.Def.Storylets[i.Storylet].Tier == Tier.Major)) return;
        }
    }

    private static void Decide(Simulation sim, Input input, StoryletInstance inst, HashSet<long> answered)
    {
        Card(sim, inst);
        var s = sim.Narrative.Def.Storylets[inst.Storylet];
        var available = Enumerable.Range(0, s.Choices.Count).Where(c => sim.ChoiceAvailable(inst, c)).ToList();
        if (available.Count == 0)
        {
            Console.WriteLine("  (No choice is open to you now; it will decide itself.)");
            answered.Add(inst.Seq);
            return;
        }
        bool major = s.Tier == Tier.Major;
        int pick = input.Number($"  Your decision (1-{s.Choices.Count}{(major ? "" : ", Enter to decide later")}): ", 1, s.Choices.Count,
            major ? available[0] + 1 : 0, required: major) - 1;
        if (pick < 0) return;
        if (!available.Contains(pick))
        {
            Console.WriteLine("  That choice isn't open to you now.");
            return;
        }
        sim.Orders.Enqueue(new ChooseStoryletOrder(sim.World.Nations.Player, inst.Seq, pick));
        answered.Add(inst.Seq);
        Console.WriteLine($"  → {s.Choices[pick].Text}");
        Console.WriteLine();
    }

    private static void Card(Simulation sim, StoryletInstance inst)
    {
        var eng = sim.Narrative;
        var s = eng.Def.Storylets[inst.Storylet];
        Console.WriteLine();
        Console.WriteLine($"  ┌─ {(s.Tier == Tier.Major ? "DECISION" : "BRIEF")} · {sim.Calendar.Describe(inst.Day)}{(inst.Hour >= 0 ? $" {inst.Hour:00}:00" : "")} ─────────────────");
        Console.WriteLine($"  │ {s.Title.ToUpperInvariant()}");
        foreach (var line in Wrap(eng.Render(inst, sim.World, s.Text), 86)) Console.WriteLine($"  │ {line}");
        Console.WriteLine("  │");
        for (int c = 0; c < s.Choices.Count; c++)
        {
            var ch = s.Choices[c];
            bool ok = sim.ChoiceAvailable(inst, c);
            Console.WriteLine($"  │ {c + 1}. {ch.Text}{(ok ? "" : "  [not available]")}");
            if (ch.Hint.Length > 0) Console.WriteLine($"  │      {ch.Hint}");
        }
        if (inst.ExpiresDay >= 0) Console.WriteLine($"  │ (If you don't answer by the end of {sim.Calendar.Describe(inst.ExpiresDay)}: \"{s.Choices[s.ChoiceIndex(s.Default!)].Text}\")");
        Console.WriteLine("  └──────────────────────────────────────────────────────────────");
    }

    private static void Brief(Simulation sim, Input input, HashSet<long> answered)
    {
        var cards = sim.PendingDecisions.Where(i => !answered.Contains(i.Seq)).ToList();
        if (cards.Count == 0) { Console.WriteLine("  The Brief is empty."); return; }
        for (int i = 0; i < cards.Count; i++)
            Console.WriteLine($"  {i + 1}. {sim.Narrative.Def.Storylets[cards[i].Storylet].Title}");
        int pick = input.Number($"  Open which card (1-{cards.Count}, Enter to close)? ", 1, cards.Count, 0);
        if (pick == 0) return;
        Decide(sim, input, cards[pick - 1], answered);
    }

    // ---- Orders ----

    private static void Orders(Simulation sim, Input input)
    {
        Console.WriteLine("""
              1 Repair a substation          7 Forensic sweep of a province's grid
              2 Change a priority            8 Design Bureau: firmware patch or hardware revision
              3 Mobilization level           9 Stockpile doctrine
              4 Exempt a labour pool         10 Attack at Veyl
              5 Emergency powers             11 Launch a cyber operation
              6 Information war              12 Nationalize a company
            """);
        int pick = input.Number("  Order (1-12, Enter to cancel)? ", 1, 12, 0);
        var w = sim.World;
        int me = w.Nations.Player;
        Order? order = pick switch
        {
            1 => Repair(sim, input),
            2 => Priority(sim, input),
            3 => new SetMobilizationOrder(me, input.Number($"  Level (0-{2}, now {w.Nations.MobilizationLevel[me]})? ", 0, 2, w.Nations.MobilizationLevel[me])),
            4 => Exempt(sim, input),
            5 => Emergency(sim, input),
            6 => InfoWar(sim, input),
            7 => Choose(input, "Sweep which province", ProvincesOf(w, me), p => new ForensicSweepOrder(me, p)),
            8 => Design(sim, input),
            9 => new SetDoctrineOrder(me, Fine.Ratio(input.Number("  Doctrine 0 (Just-in-Time) to 100 (Just-in-Case)? ", 0, 100, 46), 100)),
            10 => new FrontAttackOrder(me),
            11 => Choose(input, "Which operation", Enumerable.Range(0, w.Operations.Count).Where(o => w.Operations.Attacker[o] == me)
                .Select(o => (o, $"{w.Operations.Keys[o]} (access {w.Operations.Access[o].ToString(0)}, {(OperationState)w.Operations.State[o]})")).ToList(),
                o => new LaunchCyberOperationOrder(me, o)),
            12 => Choose(input, "Which company", Enumerable.Range(0, w.Corporations.Count).Select(c => (c, w.Corporations.Defs[c].Name)).ToList(),
                c => new NationalizeOrder(me, w.Corporations.Keys[c])),
            _ => null,
        };
        if (order is null) return;
        sim.Orders.Enqueue(order);
        Console.WriteLine("  Order given. It takes effect as the clock moves.");
    }

    private static List<(int, string)> ProvincesOf(SimWorld w, int n) =>
        Enumerable.Range(0, w.Provinces.Count).Where(p => w.Provinces.Owner[p] == n).Select(p => (p, w.Provinces.Names[p])).ToList();

    private static Order? Choose(Input input, string question, List<(int Id, string Label)> options, Func<int, Order> make)
    {
        if (options.Count == 0) { Console.WriteLine("  Nothing to choose from."); return null; }
        for (int i = 0; i < options.Count; i++) Console.WriteLine($"    {i + 1}. {options[i].Label}");
        int pick = input.Number($"  {question} (1-{options.Count})? ", 1, options.Count, 0);
        return pick == 0 ? null : make(options[pick - 1].Id);
    }

    private static Order? Repair(Simulation sim, Input input)
    {
        var w = sim.World;
        int me = w.Nations.Player;
        var subs = Enumerable.Range(0, w.Substations.Count).Where(x => w.Substations.State[x] == (int)SubstationState.Damaged && w.Provinces.Owner[w.Substations.Province[x]] == me)
            .Select(x => (x, $"{w.Substations.Keys[x]} ({(w.Substations.MobileAssigned[x] ? "mobile unit on site, " : "")}{(RepairKind)w.Substations.Repair[x]} repair)")).ToList();
        Console.WriteLine($"  In reserve: {w.Nations.SpareTransformers[me]} spare transformer(s), {w.Nations.MobileSubstations[me]} mobile unit(s).");
        int sub = -1;
        var chosen = Choose(input, "Which substation", subs, x => { sub = x; return null!; });
        if (sub < 0) return null;
        int method = input.Number("  1 spare transformer (14 days)  2 mobile unit (30% in 7 days)  3 new transformer (2-4 years)? ", 1, 3, 0);
        return method == 0 ? null : new RepairSubstationOrder(me, sub, (RepairChoice)(method - 1));
    }

    private static Order? Priority(Simulation sim, Input input)
    {
        var w = sim.World;
        int me = w.Nations.Player;
        var items = new List<(int Id, string Label)>();
        var targets = new List<(PriorityTarget, int)>();
        for (int f = 0; f < w.Facilities.Count; f++)
            if (w.Provinces.Owner[w.Facilities.Province[f]] == me) { items.Add((items.Count, $"{w.Facilities.Names[f]} ({EconomyRules.FacilityTier(w, sim.Balance, f)})")); targets.Add((PriorityTarget.Facility, f)); }
        for (int l = 0; l < w.Loads.Count; l++)
            if (w.Loads.Facility[l] < 0 && w.Provinces.Owner[w.Loads.Province[l]] == me && w.Loads.Province[l] != w.Provinces.IdOf("kestria_interior"))
            { items.Add((items.Count, $"{w.Loads.Keys[l]} ({EconomyRules.LoadTier(w, sim.Balance, l)})")); targets.Add((PriorityTarget.Load, l)); }
        int pickIndex = -1;
        Choose(input, "Which consumer", items, i => { pickIndex = i; return null!; });
        if (pickIndex < 0) return null;
        int tier = input.Number("  1 Critical  2 High  3 Normal  4 Low  5 back to default? ", 1, 5, 0);
        if (tier == 0) return null;
        var (target, id) = targets[pickIndex];
        return new SetPriorityOrder(me, target, id, tier == 5 ? null : (PriorityTier)(tier - 1));
    }

    private static Order? Exempt(Simulation sim, Input input)
    {
        var w = sim.World;
        int me = w.Nations.Player;
        var pools = Enumerable.Range(0, w.Labour.PoolCount)
            .Select(p => (p, $"{w.Labour.Pools[p]}{((w.Politics.ExemptMask[me] & (1 << p)) != 0 ? " (exempt)" : "")}")).ToList();
        return Choose(input, "Toggle exemption for which pool", pools,
            p => new ExemptPoolOrder(me, w.Labour.Pools[p], (w.Politics.ExemptMask[me] & (1 << p)) == 0));
    }

    private static Order? Emergency(Simulation sim, Input input)
    {
        var w = sim.World;
        int me = w.Nations.Player;
        if (!w.Politics.EmergencyActive[me])
            return input.Number("  1 Declare a state of emergency? ", 1, 1, 0) == 1 ? new DeclareEmergencyOrder(me) : null;
        var powers = sim.Scenario.Society.EmergencyPowers;
        var items = powers.Select((p, i) => (i, p.Id.Replace('_', ' '))).Append((powers.Count, "End the emergency")).ToList();
        return Choose(input, "Which", items, i =>
        {
            if (i == powers.Count) return new EndEmergencyOrder(me);
            if (!powers[i].PerProvince) return new UseEmergencyPowerOrder(me, powers[i].Id);
            var provs = ProvincesOf(w, me);
            for (int k = 0; k < provs.Count; k++) Console.WriteLine($"    {k + 1}. {provs[k].Item2}");
            int p = input.Number($"  Where (1-{provs.Count})? ", 1, provs.Count, 1);
            return new UseEmergencyPowerOrder(me, powers[i].Id, provs[p - 1].Item1);
        });
    }

    private static Order? InfoWar(Simulation sim, Input input)
    {
        var w = sim.World;
        int me = w.Nations.Player;
        var active = Enumerable.Range(0, w.Narratives.Count).Where(n => w.Narratives.Active[n]).Select(n => (n, w.Narratives.Defs[n].Name)).ToList();
        int nar = -1;
        Choose(input, "Which narrative", active, n => { nar = n; return null!; });
        if (nar < 0) return null;
        int kind = input.Number("  1 Ask the platform for a takedown  2 Counter-narrative (go live)  3 Prebunk every segment? ", 1, 3, 0);
        return kind switch
        {
            1 => new TakedownOrder(me, nar, Fixed.FromInt(input.Number("  Political Capital to spend as pressure? ", 0, 300, 0)), Fixed.Zero),
            2 => new CounterNarrativeOrder(me, nar),
            3 => new PrebunkOrder(me, nar, Enumerable.Range(0, w.Segments.Count).ToList()),
            _ => null,
        };
    }

    private static Order? Design(Simulation sim, Input input)
    {
        var w = sim.World;
        int me = w.Nations.Player;
        var designs = Enumerable.Range(0, w.Designs.Count).Select(d => (d, $"{w.Designs.Keys[d]} (effectiveness {w.Designs.Effectiveness[d].ToString(2)}, cap {w.Designs.Cap[d].ToString(2)})")).ToList();
        int design = -1;
        Choose(input, "Which design", designs, d => { design = d; return null!; });
        if (design < 0) return null;
        int kind = input.Number("  1 Firmware patch (+0.10, 5 days)  2 Hardware revision (back to 1.0, 30 days, retool)? ", 1, 2, 0);
        return kind switch { 1 => new FirmwarePatchOrder(me, design), 2 => new HardwareRevisionOrder(me, design), _ => null };
    }

    // ---- The Chronicle ----

    public static void PrintChronicle(Simulation sim)
    {
        var c = ChronicleView.Build(sim);
        Console.WriteLine();
        Console.WriteLine("============================================================================================");
        Console.WriteLine($" THE CHRONICLE · {c.Title}");
        Console.WriteLine("============================================================================================");
        Console.WriteLine(" Headlines");
        foreach (var h in c.Headlines) Console.WriteLine($"   {h.Date,-24} {h.Text}");
        if (c.Headlines.Count == 0) Console.WriteLine("   (none)");
        Console.WriteLine();
        Console.WriteLine(" What happened");
        foreach (var e in c.Timeline) Console.WriteLine($"   {e.Date,-24}{(e.Hour >= 0 ? $" {e.Hour:00}:00" : "      ")}  {e.Text}");
        Console.WriteLine();
        Console.WriteLine(" History's verdict");
        Console.WriteLine($"   {string.Join("   ", c.Scores.Select(kv => $"{kv.Key} {kv.Value:0}"))}");
        foreach (var v in c.Verdicts)
        {
            Console.WriteLine();
            Console.WriteLine($"   {v.Name}, {v.School} ({v.Score:0}/100):");
            foreach (var line in Wrap($"\"{v.Verdict}\"", 84)) Console.WriteLine($"     {line}");
        }
        Console.WriteLine();
        Console.WriteLine(" DECLASSIFIED");
        foreach (var d in c.Declassified) foreach (var line in Wrap($"• {d}", 86)) Console.WriteLine($"   {line}");
        Console.WriteLine();
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new System.Text.StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length + word.Length + 1 > width && line.Length > 0) { yield return line.ToString(); line.Clear(); }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) yield return line.ToString();
    }
}
