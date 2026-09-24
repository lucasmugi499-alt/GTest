using Cascade.Sim.Conflict;
using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Economy;
using Cascade.Sim.Narrative;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim;

/// <summary>0 healthy (green), 1 strained (amber), 2 failing (red), 3 not in play (grey).</summary>
public enum Health { Ok = 0, Strained = 1, Failing = 2, Inactive = 3 }

/// <summary>One node of the Cascade view: where it sits (column, row), how healthy it is, and why.</summary>
public sealed record CascadeNode(string Id, string Kind, string Label, int Column, int Row, Health Health, string Detail);

/// <summary>An arrow: power, an input, an output, an import, or drones to the front.</summary>
public sealed record CascadeEdge(string From, string To, string Kind, Health Health);

public sealed record CascadeView(IReadOnlyList<CascadeNode> Nodes, IReadOnlyList<CascadeEdge> Edges, int Columns, int Rows);

/// <summary>
/// The Cascade lens (concept): the dependency web drawn as a graph from power and imports on the left, through fabs,
/// components and the drone line, to the front. Each node is coloured by how it's doing, and its detail answers "why?".
/// </summary>
public static class CascadeGraph
{
    public static CascadeView Build(Simulation sim)
    {
        var w = sim.World;
        var b = sim.Balance;
        var cat = sim.Content.Catalog;
        int player = w.Nations.Player;
        var nodes = new List<CascadeNode>();
        var edges = new List<CascadeEdge>();
        var depth = new Dictionary<string, int>();

        // Goods that matter to the player's production chain.
        var goods = new HashSet<int>();
        for (int f = 0; f < w.Facilities.Count; f++)
        {
            if (w.Provinces.Owner[w.Facilities.Province[f]] != player) continue;
            var r = cat.Recipes[w.Facilities.Recipe[f]];
            foreach (var (g, _) in r.Inputs) goods.Add(g);
            foreach (var (g, _) in r.Outputs) goods.Add(g);
        }

        // Sources: substations feeding facilities, and import routes.
        var feeding = new HashSet<int>();
        for (int f = 0; f < w.Facilities.Count; f++)
            if (w.Facilities.Load[f] >= 0) feeding.Add(w.Loads.Substation[w.Facilities.Load[f]]);
        foreach (int s in feeding.OrderBy(x => x))
        {
            var state = (SubstationState)w.Substations.State[s];
            var factor = w.Substations.CapacityFactor(s, b.Grid);
            var health = state == SubstationState.Online ? Health.Ok : factor > Fine.Zero ? Health.Strained : Health.Failing;
            var detail = state switch
            {
                SubstationState.Online => "Online.",
                SubstationState.Tripped => $"Tripped by a cyber attack; back in {w.Substations.TripHoursLeft[s].ToString(0)} hours.",
                _ => $"Damaged. {(w.Substations.MobileAssigned[s] ? $"Mobile unit on site ({factor.ToFixed().ToString(2)} capacity). " : "")}" +
                     $"{((RepairKind)w.Substations.Repair[s] == RepairKind.None ? "No repair under way." : $"{(RepairKind)w.Substations.Repair[s]} repair: {(w.Substations.RepairRequired[s] - w.Substations.RepairProgress[s]).ToString(1)} days left.")}",
            };
            string id = $"sub:{w.Substations.Keys[s]}";
            nodes.Add(new CascadeNode(id, "substation", $"⚡ {w.Substations.Keys[s].Replace('_', ' ')}", 0, 0, health, detail));
            depth[id] = 0;
        }
        for (int r = 0; r < w.Imports.Count; r++)
        {
            if (w.Provinces.Owner[w.Imports.To[r]] != player || !goods.Contains(w.Imports.Good[r])) continue;
            var src = w.Imports.Source[r] < 0 ? "allied suppliers" : w.Nations.Names[w.Imports.Source[r]];
            var health = w.Imports.Closed[r] ? Health.Inactive : w.Imports.Blocked[r] ? Health.Failing
                : w.Imports.CapacityFactor[r] < Fine.One ? Health.Strained : Health.Ok;
            var detail = w.Imports.Closed[r] ? "Not in use." : w.Imports.Blocked[r] ? $"Blocked by {src}'s export controls; cargo already at sea still arrives."
                : $"Shipping {w.Imports.ShippedToday[r].ToString(0)} a day, {w.Imports.LeadDays[r]} days out.{(w.Imports.CapacityFactor[r] < Fine.One ? " Some shipping lines are skipping the port." : "")}";
            string id = $"import:{w.Imports.Keys[r]}";
            nodes.Add(new CascadeNode(id, "import", $"⛴ {cat.Goods[w.Imports.Good[r]].Name} from {src}", 0, 0, health, detail));
            depth[id] = 0;
            edges.Add(new CascadeEdge(id, $"good:{cat.Goods[w.Imports.Good[r]].Key}", "import", health));
        }

        // Goods and facilities, placed by longest path from a source.
        var facilityIds = Enumerable.Range(0, w.Facilities.Count).Where(f => w.Provinces.Owner[w.Facilities.Province[f]] == player).ToList();
        for (int pass = 0; pass < 12; pass++)
        {
            foreach (int g in goods)
            {
                string gid = $"good:{cat.Goods[g].Key}";
                int d = 0;
                foreach (var e in edges.Where(e => e.To == gid)) if (depth.TryGetValue(e.From, out var fd)) d = Math.Max(d, fd + 1);
                foreach (int f in facilityIds)
                    if (cat.Recipes[w.Facilities.Recipe[f]].Outputs.Any(o => o.Good == g) && depth.TryGetValue($"fac:{w.Facilities.Keys[f]}", out var fd2))
                        d = Math.Max(d, fd2 + 1);
                depth[gid] = d;
            }
            foreach (int f in facilityIds)
            {
                int d = 1;
                foreach (var (g, _) in cat.Recipes[w.Facilities.Recipe[f]].Inputs)
                    if (depth.TryGetValue($"good:{cat.Goods[g].Key}", out var gd)) d = Math.Max(d, gd + 1);
                depth[$"fac:{w.Facilities.Keys[f]}"] = d;
            }
        }

        foreach (int f in facilityIds)
        {
            var r = cat.Recipes[w.Facilities.Recipe[f]];
            string id = $"fac:{w.Facilities.Keys[f]}";
            var nominal = EconomyRules.EffectiveCapacity(w, b, f) * EconomyRules.EffectiveEfficiency(b, w.Facilities.Efficiency[f], w.Nations.Doctrine[player]);
            var share = nominal > Fixed.Zero ? w.Facilities.RunToday[f] / nominal : Fixed.Zero;
            int load = w.Facilities.Load[f];
            var power = load >= 0 ? w.Loads.PowerRatio[load] : Fine.One;
            var outputShare = share;
            if (r.Fab != FabClass.None) outputShare = share * Fixed.Ratio(w.Facilities.RampDone[f], b.Fab.RampDays);
            var health = outputShare >= Fixed.Parse("0.95") ? Health.Ok : outputShare >= Fixed.Parse("0.5") ? Health.Strained : Health.Failing;
            var why = new List<string> { $"Running at {(share * 100).ToString(0)}% of capacity." };
            if (power < Fine.One) why.Add($"Power {(power.ToFixed() * 100).ToString(0)}% of the day.");
            if (r.Fab != FabClass.None)
                why.Add(w.Facilities.RampDone[f] < b.Fab.RampDays
                    ? $"Restarting after a power loss: day {w.Facilities.RampDone[f]} of {b.Fab.RampDays}. Work in process scrapped: {w.Facilities.WipScrapped[f].ToString(0)} wafers."
                    : $"Yield {(w.Facilities.Yield[f] * 100).ToString(1)}%.");
            foreach (var (g, qty) in r.Inputs)
            {
                var doc = w.Stocks.DaysOfCover[w.Stocks.AtNation(player, g)];
                if (doc >= Fixed.Zero && doc < b.Economy.CriticalCoverDays) why.Add($"{cat.Goods[g].Name}: only {doc.ToString(1)} days of cover.");
            }
            nodes.Add(new CascadeNode(id, "facility", w.Facilities.Names[f], depth[id], 0, health, string.Join(" ", why)));
            if (load >= 0)
                edges.Add(new CascadeEdge($"sub:{w.Substations.Keys[w.Loads.Substation[load]]}", id, "power",
                    power >= Fine.One ? Health.Ok : power > Fine.Zero ? Health.Strained : Health.Failing));
            foreach (var (g, _) in r.Inputs) edges.Add(new CascadeEdge($"good:{cat.Goods[g].Key}", id, "input", health));
            foreach (var (g, _) in r.Outputs) edges.Add(new CascadeEdge(id, $"good:{cat.Goods[g].Key}", "output", health));
        }

        foreach (int g in goods.OrderBy(x => x))
        {
            string id = $"good:{cat.Goods[g].Key}";
            var doc = w.Stocks.DaysOfCover[w.Stocks.AtNation(player, g)];
            var health = doc < Fixed.Zero ? Health.Inactive : doc < b.Economy.CriticalCoverDays ? Health.Failing
                : w.Stocks.Shortage[w.Stocks.AtNation(player, g)] == 1 ? Health.Strained : Health.Ok;
            var stock = Fixed.Zero;
            for (int p = 0; p < w.Provinces.Count; p++) if (w.Provinces.Owner[p] == player) stock += w.Stocks.Stock[w.Stocks.At(p, g)];
            var detail = doc < Fixed.Zero ? $"Stock {stock.ToString(0)} {cat.Goods[g].Unit}s; nothing burns it."
                : $"{doc.ToString(1)} days of cover (stock plus 30 days of shipments ÷ average daily burn). Stock {stock.ToString(0)} {cat.Goods[g].Unit}s.";
            nodes.Add(new CascadeNode(id, "good", cat.Goods[g].Name, depth[id], 0, health, detail));
        }

        // The front, fed by drones.
        var front = w.Front;
        int rival = Military.Enemy(w, player);
        bool active = front.ActiveUntil[player] >= sim.Day - 1 || front.ActiveUntil[rival] >= sim.Day - 1;
        int droneGood = cat.Good(front.Def.DroneGood);
        string droneId = $"good:{cat.Goods[droneGood].Key}";
        int frontDepth = (depth.TryGetValue(droneId, out var dd) ? dd : 6) + 1;
        var frontHealth = !active ? Health.Ok : front.Locked[0] ? Health.Strained : front.AdvanceToday[rival] > Fixed.Zero ? Health.Failing : Health.Strained;
        nodes.Add(new CascadeNode("front", "front", $"⚔ {front.Def.Id.Replace('_', ' ')}", frontDepth, 0, frontHealth,
            active ? $"Fighting. Drones flown {front.DroneDensity[player].ToString(1)} per km per day; detection {front.Detection[player].ToFixed().ToString(2)} vs {front.Detection[rival].ToFixed().ToString(2)}."
                   : "Quiet."));
        edges.Add(new CascadeEdge(droneId, "front", "supply", nodes.First(n => n.Id == droneId).Health));

        // Rows within each column, in a stable order.
        var placed = new List<CascadeNode>();
        foreach (var col in nodes.GroupBy(n => n.Column).OrderBy(g => g.Key))
        {
            int row = 0;
            foreach (var n in col.OrderBy(n => n.Kind).ThenBy(n => n.Id, StringComparer.Ordinal)) placed.Add(n with { Row = row++ });
        }
        return new CascadeView(placed, edges, placed.Max(n => n.Column) + 1, placed.GroupBy(n => n.Column).Max(g => g.Count()));
    }
}

public sealed record ChoiceView(int Index, string Text, string Hint, bool Available);

/// <summary>A storylet waiting for the player: the rendered card and its choices.</summary>
public sealed record DecisionView(long Seq, string Id, string Title, string Text, bool Major, int Day, int Hour, int ExpiresDay,
    string? DefaultChoice, IReadOnlyList<ChoiceView> Choices)
{
    public static IReadOnlyList<DecisionView> Pending(Simulation sim) => sim.PendingDecisions.Select(inst =>
    {
        var s = sim.Narrative.Def.Storylets[inst.Storylet];
        return new DecisionView(inst.Seq, s.Id, s.Title, sim.Narrative.Render(inst, sim.World, s.Text), s.Tier == Tier.Major,
            inst.Day, inst.Hour, inst.ExpiresDay, s.Default is null ? null : s.Choices[s.ChoiceIndex(s.Default)].Text,
            s.Choices.Select((c, i) => new ChoiceView(i, c.Text, c.Hint, sim.ChoiceAvailable(inst, i))).ToList());
    }).ToList();
}
