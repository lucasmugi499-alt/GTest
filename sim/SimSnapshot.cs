using Cascade.Sim.Content;
using Cascade.Sim.Conflict;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim;

/// <summary>
/// A read-only copy of what the UI may show. The game and tools read these and send orders;
/// they never touch sim state directly. Floats appear here only, for display. Fields grow with each milestone.
/// </summary>
public sealed record SimSnapshot(
    int Day,
    int Hour,
    string DateText,
    bool IsCrisisDay,
    string StateHash,
    IReadOnlyList<ProvinceView> Provinces,
    IReadOnlyList<GoodView> Goods,
    IReadOnlyList<FacilityView> Facilities,
    IReadOnlyList<SubstationView> Substations,
    IReadOnlyList<ServiceView> Services,
    IReadOnlyList<DesignView> Designs,
    PoliticsView Politics,
    IReadOnlyList<SegmentView> Segments,
    IReadOnlyList<FactionView> Factions,
    IReadOnlyList<NarrativeView> Narratives,
    IReadOnlyList<OperationView> Operations,
    FrontView Front,
    IReadOnlyList<LogEntry> Log,
    IReadOnlyList<DecisionView> Decisions,
    bool IsFinished,
    int Player,
    string PlayerKey)
{
    /// <summary>Snapshot from the point of view of the player nation.</summary>
    public static SimSnapshot Of(Simulation sim)
    {
        var w = sim.World;
        int player = w.Nations.Player;
        var p = w.Provinces;
        var loads = w.Loads;

        var provinces = new ProvinceView[p.Count];
        for (int i = 0; i < p.Count; i++)
        {
            double demand = 0, served = 0;
            long dark = 0;
            for (int l = 0; l < loads.Count; l++)
            {
                if (loads.Province[l] != i) continue;
                var d = loads.DemandMw[l].ToDoubleForUi();
                var s = loads.ServedLast[l].ToDoubleForUi();
                demand += d; served += d * s;
                dark += (long)Math.Round(loads.Population[l] * (1 - s));
            }
            var def = sim.Scenario.Provinces[i];
            int subsDown = Enumerable.Range(0, w.Substations.Count).Count(x => w.Substations.Province[x] == i && w.Substations.State[x] != (int)SubstationState.Online);
            int subsTotal = Enumerable.Range(0, w.Substations.Count).Count(x => w.Substations.Province[x] == i);
            provinces[i] = new ProvinceView(p.Keys[i], p.Names[i], w.Nations.Keys[p.Owner[i]], p.InCrisis[i],
                p.Corruption[i].ToDoubleForUi(), demand > 0 ? served / demand : 1, dark, p.Collapsed[i],
                def.Map[0], def.Map[1], def.Map[2], def.Map[3], subsDown, subsTotal, p.InternetShutdown[i],
                Enumerable.Range(0, w.Loads.Count).Where(l => w.Loads.Province[l] == i).Sum(l => w.Loads.Population[l]), i);
        }

        var produced = new double[w.Catalog.Goods.Count];
        var facilities = new List<FacilityView>();
        for (int f = 0; f < w.Facilities.Count; f++)
        {
            var recipe = w.Catalog.Recipes[w.Facilities.Recipe[f]];
            var output = w.Facilities.OutputToday[f].ToDoubleForUi();
            if (recipe.Outputs.Count > 0) produced[recipe.Outputs[0].Good] += output;
            int load = w.Facilities.Load[f];
            facilities.Add(new FacilityView(
                w.Facilities.Keys[f], w.Facilities.Names[f], p.Keys[w.Facilities.Province[f]], recipe.Key,
                w.Catalog.Goods[recipe.Outputs[0].Good].Key, output,
                (w.Facilities.Capacity[f] * w.Facilities.Efficiency[f]).ToDoubleForUi(),
                w.Facilities.Efficiency[f].ToDoubleForUi(),
                load >= 0 ? loads.PowerRatio[load].ToDoubleForUi() : 1,
                recipe.Fab != FabClass.None,
                w.Facilities.Yield[f].ToDoubleForUi(),
                w.Facilities.RampDone[f],
                w.Facilities.WipScrapped[f].ToDoubleForUi()));
        }

        var goods = new List<GoodView>();
        foreach (var g in w.Catalog.Goods)
        {
            double stock = 0;
            for (int i = 0; i < p.Count; i++)
                if (p.Owner[i] == player) stock += w.Stocks.Stock[w.Stocks.At(i, g.Id)].ToDoubleForUi();
            int at = w.Stocks.AtNation(player, g.Id);
            var doc = w.Stocks.DaysOfCover[at].ToDoubleForUi();
            goods.Add(new GoodView(g.Key, g.Name, g.Unit, stock, produced[g.Id], doc < 0 ? null : doc, w.Stocks.Shortage[at]));
        }

        var subs = new List<SubstationView>();
        for (int s = 0; s < w.Substations.Count; s++)
        {
            subs.Add(new SubstationView(w.Substations.Keys[s], p.Keys[w.Substations.Province[s]],
                w.Substations.State[s] == (int)SubstationState.Online,
                w.Substations.CapacityFactor(s, sim.Balance.Grid).ToDoubleForUi(),
                ((RepairKind)w.Substations.Repair[s]).ToString(),
                (w.Substations.RepairRequired[s] - w.Substations.RepairProgress[s]).ToDoubleForUi(),
                w.Substations.MobileAssigned[s], ((SubstationState)w.Substations.State[s]).ToString(),
                w.Nations.Keys[p.Owner[w.Substations.Province[s]]], s));
        }

        var services = new List<ServiceView>();
        for (int l = 0; l < loads.Count; l++)
        {
            if (!loads.HasBackup(l)) continue;
            services.Add(new ServiceView(loads.Keys[l], loads.Kind[l], p.Keys[loads.Province[l]],
                loads.ServedLast[l].ToDoubleForUi(), loads.FuelHours[l].ToDoubleForUi(), loads.TankHours[l].ToDoubleForUi(),
                loads.ServiceAvailability[l].ToDoubleForUi()));
        }

        var designs = new List<DesignView>();
        for (int d = 0; d < w.Designs.Count; d++)
            designs.Add(new DesignView(w.Designs.Keys[d], w.Designs.Effectiveness[d].ToDoubleForUi(), w.Designs.Cap[d].ToDoubleForUi(),
                w.Catalog.Designs[d].Name, d));

        var pol = w.Politics;
        int rival = Enumerable.Range(0, w.Nations.Count).First(n => n != player);
        var meter = Conflict.Escalation.Meter(w, player, rival);
        var politics = new PoliticsView(
            pol.PoliticalCapital[player].ToDoubleForUi(), pol.Approval[player].ToDoubleForUi(), pol.Trust[player].ToDoubleForUi(),
            pol.WarSupport[player].ToDoubleForUi(), pol.Rally[player].ToDoubleForUi(), pol.WarExhaustion[player].ToDoubleForUi(),
            pol.Legitimacy[player].ToDoubleForUi(), pol.Inflation[player].ToDoubleForUi(),
            pol.EmergencyActive[player], pol.Backsliding[player].ToDoubleForUi(),
            w.Nations.MobilizationLevel[player], pol.MobilizationTarget[player], pol.ManpowerPool[player].ToDoubleForUi(),
            meter.ToDoubleForUi(), sim.Balance.Escalation.RungOf(meter), pol.RedLineEstimate[rival].ToDoubleForUi(),
            pol.InsuranceMultiplier[player].ToDoubleForUi(), pol.LinesCalling[player], w.Shipping.Count,
            pol.KiaTotal[player].ToDoubleForUi(),
            Enumerable.Range(0, w.Precedents.Types).Where(t => w.Precedents.Uses[w.Precedents.At(player, t)] > 0)
                .Select(t => $"{w.Precedents.Defs[t].Id}×{w.Precedents.Uses[w.Precedents.At(player, t)]}").ToList(),
            w.Nations.SpareTransformers[player], w.Nations.MobileSubstations[player],
            sim.Scenario.Conflict.Mobilization.FirstOrDefault(m => m.Nation == w.Nations.Keys[player])?.ReservistsByLevel ?? []);

        var segments = Enumerable.Range(0, w.Segments.Count).Select(i => new SegmentView(
            w.Segments.Keys[i], w.Segments.Defs[i].Name, w.Segments.Population[i],
            Enumerable.Range(0, Needs.Count).Select(k => w.Segments.NeedSmoothed[w.Segments.At(i, (Need)k)].ToDoubleForUi()).ToArray(),
            w.Segments.Satisfaction[i].ToDoubleForUi(), w.Segments.Align[i].ToDoubleForUi(), w.Segments.Trust[i].ToDoubleForUi(),
            w.Segments.Unemployment[i].ToDoubleForUi())).ToList();

        var factions = Enumerable.Range(0, w.Factions.Count).Select(i => new FactionView(
            w.Factions.Keys[i], w.Factions.Defs[i].Name, w.Factions.Approval[i].ToDoubleForUi(), w.Factions.Leverage[i].ToDoubleForUi())).ToList();

        var narratives = Enumerable.Range(0, w.Narratives.Count).Where(n => w.Narratives.Active[n]).Select(n => new NarrativeView(
            w.Narratives.Keys[n], w.Narratives.Defs[n].Name,
            Enumerable.Range(0, w.Segments.Count).Select(s => w.Narratives.B[w.Narratives.At(n, s)].ToDoubleForUi()).ToArray(),
            Enumerable.Range(0, w.Segments.Count).Count(s => w.Narratives.Established[w.Narratives.At(n, s)]),
            w.Narratives.RumorHours[n].ToDoubleForUi(), w.Narratives.Takedown[n], w.Narratives.CounterUntil[n] >= sim.Day)).ToList();

        // Fog of war: the player sees its own operations, and a rival's only once it has been detected or used.
        var operations = Enumerable.Range(0, w.Operations.Count)
            .Where(i => w.Operations.Attacker[i] == player || w.Operations.State[i] is (int)OperationState.Detected or (int)OperationState.Used)
            .Select(i => new OperationView(
            w.Operations.Keys[i], w.Nations.Keys[w.Operations.Attacker[i]], ((OperationState)w.Operations.State[i]).ToString(),
            w.Operations.Access[i].ToDoubleForUi(), w.Operations.Attribution[i].ToDoubleForUi())).ToList();

        var fr = w.Front;
        var front = new FrontView(
            fr.Def.Id, fr.ActiveUntil[player] >= sim.Day - 1 || fr.ActiveUntil[rival] >= sim.Day - 1, fr.Locked[0],
            Enumerable.Range(0, w.Brigades.Count).Select(b => new BrigadeView(w.Brigades.Keys[b], w.Brigades.Defs[b].Name,
                w.Nations.Keys[w.Brigades.Nation[b]], w.Brigades.Strength[b].ToDoubleForUi(), w.Brigades.Killed[b].ToDoubleForUi(),
                ((Posture)w.Brigades.Posture[b]).ToString())).ToList(),
            fr.Detection[player].ToDoubleForUi(), fr.Detection[rival].ToDoubleForUi(),
            fr.DroneDensity[player].ToDoubleForUi(), fr.DroneDensity[rival].ToDoubleForUi(),
            fr.KillZone[player].ToDoubleForUi(), fr.AdvanceKm[player].ToDoubleForUi(), fr.AdvanceKm[rival].ToDoubleForUi(),
            p.Names[fr.Province]);

        return new SimSnapshot(
            sim.Day, sim.Hour, sim.Calendar.Describe(sim.Day), sim.IsCrisisDay,
            Core.StateHasher.Format(sim.StateHash()),
            provinces, goods, facilities, subs, services, designs,
            politics, segments, factions, narratives, operations, front, w.Log.Entries.ToList(),
            DecisionView.Pending(sim), sim.IsFinished, player, w.Nations.Keys[player]);
    }

    public GoodView Good(string key) => Goods.First(g => g.Id == key);
    public ProvinceView Province(string key) => Provinces.First(p => p.Id == key);
}

public sealed record ProvinceView(string Id, string Name, string Owner, bool InCrisis, double Corruption,
    double PowerServed, long PeopleWithoutPower, bool GridCollapsed,
    int MapX, int MapY, int MapW, int MapH, int SubstationsDown, int Substations, bool InternetShutdown, long Population, int Index);

/// <summary>A good, nationally. DaysOfCover is null when nothing burns it. Shortage: 0 fine, 1 warning, 2 critical.</summary>
public sealed record GoodView(string Id, string Name, string Unit, double Stock, double ProducedToday, double? DaysOfCover, int Shortage);

public sealed record FacilityView(string Id, string Name, string Province, string Recipe, string Output,
    double OutputToday, double NominalRun, double Efficiency, double PowerRatio, bool IsFab, double Yield, int RampDays, double WipScrapped);

/// <summary>State is Online, Tripped or Damaged. Owner is the owning nation's key. Index is the id orders take.</summary>
public sealed record SubstationView(string Id, string Province, bool Online, double CapacityFactor, string Repair, double RepairDaysLeft, bool MobileUnit,
    string State, string Owner, int Index);

public sealed record ServiceView(string Id, string Kind, string Province, double Powered, double FuelHours, double TankHours, double Availability);

public sealed record DesignView(string Id, double Effectiveness, double Cap, string Name, int Index);

public sealed record PoliticsView(double PoliticalCapital, double Approval, double Trust, double WarSupport, double Rally,
    double WarExhaustion, double Legitimacy, double Inflation, bool Emergency, double Backsliding,
    int Mobilization, int MobilizationTarget, double ManpowerPool,
    double EscalationMeter, int Rung, double RivalRedLineEstimate, double InsuranceMultiplier, int ShippingLinesCalling, int ShippingLines,
    double KilledInAction, IReadOnlyList<string> Precedents,
    int SpareTransformers, int MobileUnits, IReadOnlyList<int> ReservistsByLevel);

/// <summary>Needs in the order of <see cref="Need"/>, smoothed.</summary>
public sealed record SegmentView(string Id, string Name, long Population, double[] Needs, double Satisfaction, double Align, double Trust, double Unemployment);

public sealed record FactionView(string Id, string Name, double Approval, double Leverage);

/// <summary>Believing share per segment (in segment order); RumorHours −1 if no segment is about to tip.</summary>
public sealed record NarrativeView(string Id, string Name, double[] Believing, int EstablishedSegments, double RumorHours, bool TakenDown, bool Countered);

public sealed record OperationView(string Id, string Attacker, string State, double Access, double Attribution);

public sealed record BrigadeView(string Id, string Name, string Nation, double Strength, double Killed, string Posture);

public sealed record FrontView(string Id, bool Active, bool Locked, IReadOnlyList<BrigadeView> Brigades, double PlayerDetection, double RivalDetection,
    double PlayerDroneDensity, double RivalDroneDensity, double KillZoneKm, double PlayerAdvanceKm, double RivalAdvanceKm, string ProvinceName);
