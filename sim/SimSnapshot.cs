using Cascade.Sim.Content;
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
    IReadOnlyList<DesignView> Designs)
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
            provinces[i] = new ProvinceView(p.Keys[i], p.Names[i], w.Nations.Keys[p.Owner[i]], p.InCrisis[i],
                p.Corruption[i].ToDoubleForUi(), demand > 0 ? served / demand : 1, dark, p.Collapsed[i]);
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
                w.Substations.MobileAssigned[s]));
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
            designs.Add(new DesignView(w.Designs.Keys[d], w.Designs.Effectiveness[d].ToDoubleForUi(), w.Designs.Cap[d].ToDoubleForUi()));

        return new SimSnapshot(
            sim.Day, sim.Hour, sim.Calendar.Describe(sim.Day), sim.IsCrisisDay,
            Core.StateHasher.Format(sim.StateHash()),
            provinces, goods, facilities, subs, services, designs);
    }

    public GoodView Good(string key) => Goods.First(g => g.Id == key);
    public ProvinceView Province(string key) => Provinces.First(p => p.Id == key);
}

public sealed record ProvinceView(string Id, string Name, string Owner, bool InCrisis, double Corruption,
    double PowerServed, long PeopleWithoutPower, bool GridCollapsed);

/// <summary>A good, nationally. DaysOfCover is null when nothing burns it. Shortage: 0 fine, 1 warning, 2 critical.</summary>
public sealed record GoodView(string Id, string Name, string Unit, double Stock, double ProducedToday, double? DaysOfCover, int Shortage);

public sealed record FacilityView(string Id, string Name, string Province, string Recipe, string Output,
    double OutputToday, double NominalRun, double Efficiency, double PowerRatio, bool IsFab, double Yield, int RampDays, double WipScrapped);

public sealed record SubstationView(string Id, string Province, bool Online, double CapacityFactor, string Repair, double RepairDaysLeft, bool MobileUnit);

public sealed record ServiceView(string Id, string Kind, string Province, double Powered, double FuelHours, double TankHours, double Availability);

public sealed record DesignView(string Id, double Effectiveness, double Cap);
