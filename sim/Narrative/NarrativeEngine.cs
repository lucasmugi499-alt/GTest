using Cascade.Sim.Conflict;
using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Economy;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Narrative;

/// <summary>
/// The narrative engine for one campaign: the blackboard and every storylet's choice effects, compiled and checked
/// against the world at startup so a bad fact, id or effect in content fails at load, not mid-campaign.
/// </summary>
public sealed class NarrativeEngine
{
    public Blackboard Blackboard { get; }
    public NarrativeDef Def { get; }
    private readonly Action<TickContext, StoryletInstance>[][][] _effects; // [storylet][choice][effect]

    public NarrativeEngine(SimWorld world, ContentSet content)
    {
        Def = content.Scenario.Narrative;
        Blackboard = new Blackboard(world, content);
        try
        {
            foreach (var s in Def.Storylets)
            {
                foreach (var c in s.Preconditions) Blackboard.Compile(c.Fact);
                foreach (var ch in s.Choices) foreach (var c in ch.Requires) Blackboard.Compile(c.Fact);
            }
            foreach (var seed in Def.Seeds) foreach (var c in seed.Defuse) Blackboard.Compile(c.Fact);
            _effects = Def.Storylets.Select(s => s.Choices.Select(ch =>
                ch.Effects.Select(e => Effects.Compile(e, world, content, s.Id)).ToArray()).ToArray()).ToArray();
        }
        catch (KeyNotFoundException e)
        {
            throw new ContentException($"storylets.yaml: {e.Message}");
        }
    }

    public FactContext Facts(TickContext ctx) => new(ctx.World, ctx.Balance, ctx.Content, ctx.Day, Math.Max(0, ctx.Hour));

    public bool Available(TickContext ctx, StoryletInstance inst, int choice) =>
        Blackboard.All(Def.Storylets[inst.Storylet].Choices[choice].Requires, Facts(ctx));

    /// <summary>Applies a choice: its effects, the cast's memories, and any seeds it plants.</summary>
    public void Resolve(TickContext ctx, StoryletInstance inst, int choice, bool byDefault)
    {
        var w = ctx.World;
        var s = Def.Storylets[inst.Storylet];
        var ch = s.Choices[choice];
        inst.Choice = choice;
        inst.ResolvedDay = ctx.Day;
        inst.ByDefault = byDefault;
        w.Log.Add(ctx.Day, ctx.Hour, "decision", $"{s.Title}: {ch.Text}{(byDefault ? " (no answer in time)" : "")}", "", $"{s.Id}.{ch.Id}");

        foreach (var effect in _effects[inst.Storylet][choice]) effect(ctx, inst);

        var n = ctx.Balance.Narrative;
        foreach (var (role, valence) in ch.Memories)
        {
            int r = s.Roles.Select((x, i) => (x, i)).First(x => x.x.Name == role).i;
            w.Characters.Remember(inst.Cast[r], ctx.Day, valence, n.MemoryGraveValence);
        }
        foreach (var seed in ch.Seeds)
        {
            int id = Def.Seed(seed);
            if (w.Seeds.State.Pending(id) != (int)SeedState.Dormant) continue;
            w.Seeds.State.Set(id, (int)SeedState.Live);
            w.Seeds.PlantedDay.Set(id, ctx.Day);
        }
    }

    public string Render(StoryletInstance inst, SimWorld w, string text)
    {
        var s = Def.Storylets[inst.Storylet];
        for (int r = 0; r < s.Roles.Count; r++) text = text.Replace("{" + s.Roles[r].Name + "}", w.Characters.Defs[inst.Cast[r]].Name);
        return text;
    }
}

/// <summary>Turns a content effect ("order.repair", "faction.labour", …) into an action, checking every id up front.</summary>
public static class Effects
{
    public static Action<TickContext, StoryletInstance> Compile(EffectDef e, SimWorld w, ContentSet c, string storylet)
    {
        var v = e.Value;
        int player = w.Nations.Player;
        int rival = Enumerable.Range(0, w.Nations.Count).First(n => n != player);
        var parts = e.Key.Split('.', 2);

        switch (e.Key)
        {
            case "escalation":
            {
                var amount = v.Fixed();
                return (ctx, _) =>
                {
                    Escalation.AddToMeter(ctx, player, rival, amount, 0, (int)amount.Abs().RoundToInt());
                    VaranAi.CheckRedLine(ctx, rival, player);
                };
            }
            case "rally": { var x = v.Fixed(); return (ctx, _) => Add(ctx.World.Politics.Rally, player, x, Fixed.Hundred); }
            case "legitimacy": { var x = v.Fixed(); return (ctx, _) => Add(ctx.World.Politics.Legitimacy, player, x, Fixed.Hundred); }
            case "pc": { var x = v.Fixed(); return (ctx, _) => Add(ctx.World.Politics.PoliticalCapital, player, x, ctx.Balance.Society.PcCap); }
            case "trust":
            {
                var x = v.Fixed();
                return (ctx, _) => { for (int s = 0; s < ctx.World.Segments.Count; s++) Add(ctx.World.Segments.TrustBase, s, x, Fixed.Hundred); };
            }
            case "align":
            {
                var x = v.Fixed();
                return (ctx, _) =>
                {
                    var seg = ctx.World.Segments;
                    for (int s = 0; s < seg.Count; s++) seg.AlignShock.Set(s, seg.AlignShock.Pending(s) + x);
                };
            }
            case "headline":
            {
                var text = v.Text;
                return (ctx, _) => ctx.World.Log.Add(ctx.Day, ctx.Hour, "headline", text, "", storylet);
            }
            case "rival.must_respond":
            {
                var weight = v.Fixed();
                return (ctx, _) => VaranAi.React(ctx, rival, player, weight);
            }
            case "rival.lift_export_controls":
            {
                int days = v.Int();
                return (ctx, _) =>
                {
                    var im = ctx.World.Imports;
                    var goods = Enumerable.Range(0, im.Count).Where(r => im.Source[r] == rival && im.Blocked[r]).Select(r => im.Good[r]).Distinct().ToList();
                    if (goods.Count == 0) return;
                    ctx.Events.Schedule(new ExportControlEvent(ctx.Day + days, rival, goods, active: false));
                    ctx.World.Log.Add(ctx.Day, ctx.Hour, "diplomacy", $"{ctx.World.Nations.Names[rival]} agrees to end its export review in {days} days.", ctx.World.Nations.Keys[rival], "lift_export_controls");
                };
            }
        }

        switch (parts)
        {
            case ["faction", var id]:
            {
                int f = w.Factions.IdOf(id);
                var x = v.Fixed();
                return (ctx, _) => ctx.World.Factions.Standing.Set(f, ctx.World.Factions.Standing.Pending(f) + x);
            }
            case ["flag", var id]:
            {
                int f = w.Flags.IdOf(id);
                var x = v.Fixed();
                return (ctx, _) => ctx.World.Flags.Value.Set(f, x);
            }
            case ["design", var id]:
            {
                int d = w.Designs.IdOf(id);
                var x = v.Fixed();
                return (ctx, _) =>
                {
                    var ds = ctx.World.Designs;
                    ds.Cap.Set(d, Fixed.Min(Fixed.One, ds.Cap.Pending(d) + x));
                    ds.Effectiveness.Set(d, Fixed.Min(ds.Cap.Pending(d), ds.Effectiveness.Pending(d) + x));
                };
            }
            case ["narrative", var id]:
            {
                int n = c.Scenario.Society.Narrative(id);
                var seed = v.Map.Entries.Select(x => { w.Segments.IdOf(x.Key); return (x.Key, x.Value.Fine()); }).ToList();
                return (ctx, _) => Information.Seed(ctx, n, seed);
            }
            case ["order", var kind]:
                return CompileOrder(kind, v, w, c, player);
        }
        throw v.Error($"unknown effect '{e.Key}' in storylet '{storylet}'");
    }

    private static void Add(Column<Fixed> col, int i, Fixed x, Fixed max) =>
        col.Set(i, Fixed.Clamp(col.Pending(i) + x, Fixed.Zero, max));

    /// <summary>A choice that gives an order carries it out at once, through the same order the player could give.</summary>
    private static Action<TickContext, StoryletInstance> CompileOrder(string kind, CValue v, SimWorld w, ContentSet c, int p)
    {
        List<Func<Order>> orders = kind switch
        {
            "forensic_sweep" => [() => new ForensicSweepOrder(p, Id(w.Provinces, v))],
            "mobilize" => [() => new SetMobilizationOrder(p, v.Int())],
            "exempt" => v.Items.Select(x => { w.Labour.PoolId(x.Text); return (Func<Order>)(() => new ExemptPoolOrder(p, x.Text, true)); }).ToList(),
            "repair" => v.Items.Select(x =>
            {
                var m = x.Map.Only("substation", "method");
                int sub = Id(w.Substations, m["substation"]);
                var method = m["method"].Text switch
                {
                    "spare" => RepairChoice.Spare,
                    "mobile" => RepairChoice.Mobile,
                    "new" => RepairChoice.NewTransformer,
                    var t => throw m["method"].Error($"expected spare, mobile or new, got '{t}'"),
                };
                return (Func<Order>)(() => new RepairSubstationOrder(p, sub, method));
            }).ToList(),
            "priority" => v.Items.Select(x =>
            {
                var m = x.Map.Only("facility", "load", "tier");
                PriorityTier? tier = m["tier"].Text == "default" ? null : PriorityTiers.Parse(m["tier"].Text, m["tier"].Where);
                if (m.Has("facility")) { int f = Id(w.Facilities, m["facility"]); return (Func<Order>)(() => new SetPriorityOrder(p, PriorityTarget.Facility, f, tier)); }
                int l = Id(w.Loads, m["load"]);
                return (Func<Order>)(() => new SetPriorityOrder(p, PriorityTarget.Load, l, tier));
            }).ToList(),
            "priority_demand" => PriorityDemand(v, w, c, p),
            "declare_emergency" => [() => new DeclareEmergencyOrder(p)],
            "end_emergency" => [() => new EndEmergencyOrder(p)],
            "power" => v.Items.Select(x =>
            {
                if (x is CMap m)
                {
                    m.Only("power", "province");
                    c.Scenario.Society.Power(m["power"].Text);
                    int prov = Id(w.Provinces, m["province"]);
                    return (Func<Order>)(() => new UseEmergencyPowerOrder(p, m["power"].Text, prov));
                }
                c.Scenario.Society.Power(x.Text);
                return (Func<Order>)(() => new UseEmergencyPowerOrder(p, x.Text));
            }).ToList(),
            "nationalize" => [() => new NationalizeOrder(p, Key(w.Corporations, v))],
            "takedown" => Takedown(v, w, p),
            "counter" => [() => new CounterNarrativeOrder(p, Id(w.Narratives, v))],
            "launch_cyber" => [() => new LaunchCyberOperationOrder(p, Id(w.Operations, v))],
            "open_import" => OpenImport(v, w, c),
            "doctrine" => [() => new SetDoctrineOrder(p, v.Fine())],
            "firmware" => [() => new FirmwarePatchOrder(p, Id(w.Designs, v))],
            "revision" => [() => new HardwareRevisionOrder(p, Id(w.Designs, v))],
            "front_attack" => [() => new FrontAttackOrder(p)],
            _ => throw v.Error($"unknown order '{kind}'"),
        };
        return (ctx, _) =>
        {
            foreach (var make in orders)
            {
                var order = make();
                var outcome = order.Apply(ctx);
                if (!outcome.Accepted)
                    ctx.World.Log.Add(ctx.Day, ctx.Hour, "order_refused", $"Couldn't {order.Kind.Split('.')[^1].Replace('_', ' ')}: {outcome.Reason}", "", order.Kind);
            }
        };
    }

    private static List<Func<Order>> PriorityDemand(CValue v, SimWorld w, ContentSet c, int p)
    {
        var m = v.Map.Only("good", "tier");
        int g = c.Catalog.Good(m["good"].Text);
        PriorityTier? tier = m["tier"].Text == "default" ? null : PriorityTiers.Parse(m["tier"].Text, m["tier"].Where);
        return Enumerable.Range(0, w.Demand.Count).Where(d => w.Demand.Good[d] == g)
            .Select(d => (Func<Order>)(() => new SetPriorityOrder(p, PriorityTarget.Demand, d, tier))).ToList();
    }

    private static List<Func<Order>> Takedown(CValue v, SimWorld w, int p)
    {
        var m = v.Map.Only("narrative", "pressure", "contract");
        int n = Id(w.Narratives, m["narrative"]);
        var pressure = m["pressure"].Fixed();
        var contract = m.Has("contract") ? m["contract"].Fixed() : Fixed.Zero;
        return [() => new TakedownOrder(p, n, pressure, contract)];
    }

    private static List<Func<Order>> OpenImport(CValue v, SimWorld w, ContentSet c)
    {
        var m = v.Map.Only("good", "from");
        int route = w.Imports.IdOf($"{m["good"].Text}<{m["from"].Text}");
        return [() => new OpenImportOrder(w.Nations.Player, route)];
    }

    private static int Id(Store s, CValue v) => s.TryIdOf(v.Text, out int id) ? id : throw v.Error($"no {s.TypeName} '{v.Text}'");
    private static string Key(Store s, CValue v) => s.TryIdOf(v.Text, out _) ? v.Text : throw v.Error($"no {s.TypeName} '{v.Text}'");
}

/// <summary>Open a closed import route (e.g. allied magnet suppliers).</summary>
public sealed class OpenImportOrder(int issuer, int route) : Order(issuer)
{
    public override string Kind => "trade.open_import";

    public override OrderOutcome Apply(TickContext ctx)
    {
        var im = ctx.World.Imports;
        if (ctx.World.Provinces.Owner[im.To[route]] != Issuer) return OrderOutcome.Refused("Not your port.");
        if (!im.Closed.Pending(route)) return OrderOutcome.Refused("Already open.");
        im.Closed.Set(route, false);
        ctx.World.Log.Add(ctx.Day, ctx.Hour, "trade", $"A new supply route opens: {ctx.Content.Catalog.Goods[im.Good[route]].Name.ToLowerInvariant()}, first delivery in {im.LeadDays[route]} days.", ctx.World.Nations.Keys[Issuer], im.Keys[route]);
        return OrderOutcome.Ok;
    }

    protected override void HashFields(StateHasher h) => h.Add(route);
}

/// <summary>The player's answer to a storylet. Applied in phase 0 like every order, so replays reproduce it.</summary>
public sealed class ChooseStoryletOrder(int issuer, long instance, int choice) : Order(issuer)
{
    public override string Kind => "narrative.choose";
    public long Instance => instance;

    public override OrderOutcome Apply(TickContext ctx)
    {
        var inst = ctx.World.Storylets.Find(instance);
        if (inst is null || !inst.Pending) return OrderOutcome.Refused("That decision is no longer open.");
        var s = ctx.Narrative.Def.Storylets[inst.Storylet];
        if (choice < 0 || choice >= s.Choices.Count) return OrderOutcome.Refused("No such choice.");
        if (!ctx.Narrative.Available(ctx, inst, choice)) return OrderOutcome.Refused("That choice isn't available now.");
        ctx.Narrative.Resolve(ctx, inst, choice, byDefault: false);
        return OrderOutcome.Ok;
    }

    protected override void HashFields(StateHasher h) => h.Add(instance).Add(choice);
}
