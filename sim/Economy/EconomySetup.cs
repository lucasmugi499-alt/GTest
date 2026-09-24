using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.World;

namespace Cascade.Sim.Economy;

/// <summary>
/// Builds the Day 0 economy from the scenario (D-006): stockpiles sized to the scenario's starting Days of Cover,
/// imports already at sea, and burn averages seeded at the nominal rate, so the first days start in steady state.
/// </summary>
public static class EconomySetup
{
    public static void Initialize(SimWorld w, ContentSet content)
    {
        var b = content.Balance;
        var s = content.Scenario;
        var stocks = w.Stocks;
        int goods = stocks.Goods;
        var nominal = NominalBurn(w, b);

        for (int at = 0; at < nominal.Length; at++) stocks.BurnEma.Init(at, nominal[at]);

        foreach (var (goodKey, days) in s.InitialCoverDays)
        {
            int g = w.Catalog.Good(goodKey);
            for (int n = 0; n < w.Nations.Count; n++)
            {
                var provinces = Enumerable.Range(0, w.Provinces.Count).Where(p => w.Provinces.Owner[p] == n).ToList();
                var burn = provinces.Aggregate(Fixed.Zero, (sum, p) => sum + nominal[stocks.At(p, g)]);
                if (burn <= Fixed.Zero) continue;

                // Imports already on the way: one day's share of burn per day of lead time.
                var remaining = burn;
                var inTransitWindow = Fixed.Zero;
                for (int r = 0; r < w.Imports.Count; r++)
                {
                    if (w.Imports.Good[r] != g || w.Provinces.Owner[w.Imports.To[r]] != n || remaining <= Fixed.Zero || w.Imports.Closed[r]) continue;
                    var flow = Fixed.Min(remaining, w.Imports.CapacityPerDay[r]);
                    remaining -= flow;
                    for (int d = 0; d < w.Imports.LeadDays[r]; d++)
                    {
                        w.Shipments.Add(g, -1, w.Imports.To[r], flow, d);
                        if (d <= b.Economy.TransitWindowDays) inTransitWindow += flow;
                    }
                }

                var stock = burn * days - inTransitWindow;
                if (stock < Fixed.Zero)
                    throw new ContentException($"initial_cover_days.{goodKey}: {days} days is less than the imports already in transit.");

                // Spread in proportion to where the good is burned; rounding remainder to the biggest user.
                var given = Fixed.Zero;
                int biggest = provinces.OrderByDescending(p => nominal[stocks.At(p, g)].Raw).ThenBy(p => p).First();
                foreach (int p in provinces)
                {
                    var share = Fixed.FromRaw((long)((Int128)stock.Raw * nominal[stocks.At(p, g)].Raw / burn.Raw));
                    stocks.Stock.Init(stocks.At(p, g), share);
                    given += share;
                }
                int at = stocks.At(biggest, g);
                stocks.Stock.Init(at, stocks.Stock[at] + (stock - given));
            }
        }

        ConsumptionPhase.ComputeCover(w, b, 0, useCommittedEma: true);
        w.Commit();
    }

    /// <summary>
    /// Daily burn per province and good with everything running flat out: facilities at full power, labour and
    /// inputs, plus final demand. Used only to size the starting world.
    /// </summary>
    public static Fixed[] NominalBurn(SimWorld w, Balance b)
    {
        var stocks = w.Stocks;
        var burn = new Fixed[stocks.Provinces * stocks.Goods];
        var f = w.Facilities;
        for (int i = 0; i < f.Count; i++)
        {
            var recipe = w.Catalog.Recipes[f.Recipe[i]];
            int p = f.Province[i];
            var e = EconomyRules.EffectiveEfficiency(b, f.Efficiency[i], w.Nations.Doctrine[w.Provinces.Owner[p]]);
            var q = f.Capacity[i] * e * (Fixed.One - f.Damage[i]);
            foreach (var (good, qty) in recipe.Inputs) burn[stocks.At(p, good)] += qty * q;
        }
        for (int d = 0; d < w.Demand.Count; d++) burn[stocks.At(w.Demand.Province[d], w.Demand.Good[d])] += w.Demand.PerDay[d];
        return burn;
    }
}
