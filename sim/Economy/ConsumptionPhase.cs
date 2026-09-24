using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Economy;

/// <summary>
/// Phase 5 (spec Consumption, Blackout clocks, Days of Cover).
/// Hourly in a crisis province: only the blackout clocks. Backed-up services burn tank hours while unpowered
/// and fail when the tank is empty; tanker trucks refuel them in fuel-priority order if there is road access.
/// Daily: final demand consumes its reserved share, burn averages update, and Days of Cover and shortage
/// flags are recomputed per nation.
/// </summary>
public sealed class ConsumptionPhase : IHourlyPhase
{
    private readonly Fine _alpha;

    public ConsumptionPhase(Balance balance) => _alpha = EconomyRules.EmaAlpha(balance.Economy.BurnHalfLifeDays);

    public PhaseId Id => PhaseId.Consumption;

    public void RunHourly(TickContext ctx)
    {
        var loads = ctx.World.Loads;
        foreach (int p in ctx.CrisisProvinces)
        {
            for (int l = 0; l < loads.Count; l++)
            {
                if (loads.Province[l] != p) continue;
                var up = loads.HasBackup(l) ? RunClock(loads, l, Fixed.One, loads.ServedLast[l]) : loads.ServedLast[l];
                var sofar = ctx.Hour == 0 ? Fine.Zero : loads.ServiceHours.Pending(l);
                loads.ServiceHours.Set(l, sofar + up);
            }
            Refuel(ctx, p, Fixed.One);
        }
    }

    public void RunDaily(TickContext ctx)
    {
        var w = ctx.World;
        var loads = w.Loads;
        var crisis = new bool[w.Provinces.Count];
        foreach (int p in ctx.CrisisProvinces) crisis[p] = true;

        // Clocks for provinces not in crisis run once, for the day's unpowered hours.
        for (int l = 0; l < loads.Count; l++)
        {
            if (crisis[loads.Province[l]])
            {
                loads.ServiceAvailability.Set(l, loads.ServiceHours[l] / 24);
                loads.ServiceHours.Set(l, Fine.Zero);
            }
            else if (loads.HasBackup(l))
                loads.ServiceAvailability.Set(l, RunClock(loads, l, Fixed.FromInt(24), loads.PowerRatio[l]) / 24);
            else
                loads.ServiceAvailability.Set(l, loads.PowerRatio[l]);
        }
        for (int p = 0; p < w.Provinces.Count; p++)
            if (!crisis[p]) Refuel(ctx, p, Fixed.FromInt(24));

        ConsumeFinalDemand(w);
        UpdateBurnAndCover(ctx.World, ctx.Balance, ctx.Day, _alpha);
    }

    /// <summary>
    /// Runs one backed-up load's clock over <paramref name="hours"/> hours. Returns the hours the service ran
    /// (on grid or on backup), as a Fine for hourly accumulation.
    /// </summary>
    private static Fine RunClock(LoadStore loads, int l, Fixed hours, Fine served)
    {
        var poweredHours = hours.Times(served);
        var unpowered = hours - poweredHours;
        var fuel = loads.FuelHours.Pending(l);
        var fromBackup = Fixed.Min(fuel, unpowered);
        loads.FuelHours.Set(l, fuel - fromBackup);
        return (poweredHours + fromBackup).ToFine();
    }

    /// <summary>Tanker trucks top up backup tanks, highest priority first (spec: the fuel priority list; needs road access).</summary>
    private static void Refuel(TickContext ctx, int p, Fixed hours)
    {
        var w = ctx.World;
        if (!w.Provinces.RoadAccess[p]) return;
        var loads = w.Loads;
        var stocks = w.Stocks;
        int diesel = w.Catalog.Good(ctx.Balance.Grid.BackupFuelGood);
        int at = stocks.At(p, diesel);
        var trucks = w.Provinces.RefuelTonnesPerHour[p] * hours * RefuelMultiplier(ctx, w.Provinces.Owner[p]);

        // Spec Shortage allocation applied to the fuel priority list: tiers in order, the short tier shared proportionally.
        var tanks = Enumerable.Range(0, loads.Count).Where(l => loads.Province[l] == p && loads.HasBackup(l)).ToArray();
        var need = tanks.Select(l => Fixed.Max(Fixed.Zero, loads.TankHours[l] - loads.FuelHours.Pending(l)) * loads.DieselPerHour[l]).ToArray();
        var tiers = tanks.Select(l => EconomyRules.LoadTier(w, ctx.Balance, l)).ToArray();
        var got = new Fixed[tanks.Length];
        Allocation.ByTier(Fixed.Min(trucks, stocks.Stock.Pending(at)), need, tiers, got);

        var used = Fixed.Zero;
        for (int k = 0; k < tanks.Length; k++)
        {
            if (got[k] <= Fixed.Zero) continue;
            int l = tanks[k];
            loads.FuelHours.Set(l, Fixed.Min(loads.TankHours[l], loads.FuelHours.Pending(l) + got[k] / loads.DieselPerHour[l]));
            used += got[k];
        }
        stocks.Stock.Set(at, stocks.Stock.Pending(at) - used);
        stocks.BurnToday.Set(at, stocks.BurnToday.Pending(at) + used);
    }

    /// <summary>Emergency fuel requisition puts more tankers on the road (content: refuel_multiplier).</summary>
    private static Fixed RefuelMultiplier(TickContext ctx, int nation)
    {
        var m = Fixed.One;
        var powers = ctx.Content.Scenario.Society.EmergencyPowers;
        for (int i = 0; i < powers.Count; i++)
            if ((ctx.World.Politics.PowersMask[nation] & (1 << i)) != 0) m *= powers[i].RefuelMultiplier;
        return m;
    }

    private static void ConsumeFinalDemand(SimWorld w)
    {
        var stocks = w.Stocks;
        var d = w.Demand;
        for (int i = 0; i < d.Count; i++)
        {
            var use = d.ReservedToday[i];
            int at = stocks.At(d.Province[i], d.Good[i]);
            stocks.Stock.Set(at, stocks.Stock.Pending(at) - use);
            stocks.BurnToday.Set(at, stocks.BurnToday.Pending(at) + use);
            d.ServedToday.Set(i, use);
            d.ReservedToday.Set(i, Fixed.Zero);
        }
        for (int at = 0; at < stocks.Provinces * stocks.Goods; at++) stocks.Reserved.Set(at, Fixed.Zero);
    }

    /// <summary>
    /// Burn EMA per province and good, then per nation: DoC = (S + T30) ÷ EMA(b, 7) (spec Days of Cover).
    /// Warning when DoC is below the good's replacement lead time, critical under 14 days.
    /// </summary>
    internal static void UpdateBurnAndCover(SimWorld w, Balance b, int day, Fine alpha)
    {
        var stocks = w.Stocks;
        int goods = stocks.Goods;
        for (int at = 0; at < stocks.Provinces * goods; at++)
        {
            var ema = stocks.BurnEma[at];
            stocks.BurnEma.Set(at, ema + (stocks.BurnToday.Pending(at) - ema).Times(alpha));
            stocks.BurnToday.Set(at, Fixed.Zero);
        }
        ComputeCover(w, b, day, useCommittedEma: false);
    }

    internal static void ComputeCover(SimWorld w, Balance b, int day, bool useCommittedEma)
    {
        var stocks = w.Stocks;
        int goods = stocks.Goods;
        int window = b.Economy.TransitWindowDays;

        var transit = new Fixed[w.Nations.Count * goods];
        foreach (var s in w.Shipments.Items)
            if (s.ArriveDay <= day + window)
                transit[stocks.AtNation(w.Provinces.Owner[s.To], s.Good)] += s.Qty;

        for (int n = 0; n < w.Nations.Count; n++)
        {
            for (int g = 0; g < goods; g++)
            {
                var stock = Fixed.Zero;
                var ema = Fixed.Zero;
                for (int p = 0; p < w.Provinces.Count; p++)
                {
                    if (w.Provinces.Owner[p] != n) continue;
                    stock += stocks.Stock.Pending(stocks.At(p, g));
                    ema += useCommittedEma ? stocks.BurnEma[stocks.At(p, g)] : stocks.BurnEma.Pending(stocks.At(p, g));
                }
                int at = stocks.AtNation(n, g);
                if (ema <= Fixed.Zero)
                {
                    stocks.DaysOfCover.Set(at, Fixed.FromInt(-1));
                    stocks.Shortage.Set(at, 0);
                    continue;
                }
                var doc = (stock + transit[at]) / ema;
                stocks.DaysOfCover.Set(at, doc);

                int lead = 0;
                for (int r = 0; r < w.Imports.Count; r++)
                    if (w.Imports.Good[r] == g && w.Provinces.Owner[w.Imports.To[r]] == n && !w.Imports.Closed[r])
                        lead = lead == 0 ? w.Imports.LeadDays[r] : Math.Min(lead, w.Imports.LeadDays[r]);
                int flag = doc < b.Economy.CriticalCoverDays ? 2 : doc < Fixed.FromInt(lead) ? 1 : 0;
                stocks.Shortage.Set(at, flag);
            }
        }
    }
}
