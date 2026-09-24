using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Economy;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Grid;

/// <summary>
/// Phase 2 (spec Grid and blackouts). Each province is a dispatch region:
/// 1. Each substation's capacity is shared among the loads behind it by priority tier.
/// 2. Regions trade surplus over tie-lines (in tie-line ID order).
/// 3. Each region's supply is shared among its loads by priority tier; the rest is shed.
/// A load's power ratio P is the share of the day it was powered: hourly in a crisis province, else once a day.
/// Daily, before dispatching, collapsed regions black-start. Repairs advance at the start of each province's day in
/// phase 1 (<see cref="AdvanceRepairs"/>), so a substation finished today carries load from today's first dispatch.
/// </summary>
public sealed class GridDispatchPhase : IHourlyPhase
{
    public PhaseId Id => PhaseId.GridDispatch;

    public void RunHourly(TickContext ctx)
    {
        var w = ctx.World;
        var loads = w.Loads;
        var crisis = CrisisMask(ctx);
        Restore(ctx, crisis, hoursFraction: 24);
        var served = Dispatch(ctx);

        for (int l = 0; l < loads.Count; l++)
        {
            if (!crisis[loads.Province[l]]) continue;
            var s = served[l];
            bool firstHour = ctx.Hour == 0;
            loads.ServedLast.Set(l, s);
            loads.PoweredHours.Set(l, (firstHour ? Fine.Zero : loads.PoweredHours.Pending(l)) + s);
            loads.Interrupted.Set(l, (!firstHour && loads.Interrupted.Pending(l)) || s < Fine.One);
        }
    }

    public void RunDaily(TickContext ctx)
    {
        var w = ctx.World;
        var loads = w.Loads;
        var crisis = CrisisMask(ctx);
        Restore(ctx, crisis, hoursFraction: 1);
        var served = Dispatch(ctx);

        for (int l = 0; l < loads.Count; l++)
        {
            if (crisis[loads.Province[l]])
            {
                // The day's hourly totals (spec Crisis sub-ticks: daily phases read the hourly results).
                loads.PowerRatio.Set(l, loads.PoweredHours[l] / 24);
                loads.PoweredHours.Set(l, Fine.Zero);
            }
            else
            {
                var s = served[l];
                loads.ServedLast.Set(l, s);
                loads.PowerRatio.Set(l, s);
                loads.Interrupted.Set(l, s < Fine.One);
            }
        }
    }

    private static bool[] CrisisMask(TickContext ctx)
    {
        var mask = new bool[ctx.World.Provinces.Count];
        foreach (int p in ctx.CrisisProvinces) mask[p] = true;
        return mask;
    }

    /// <summary>Returns each load's served share (0 to 1) for this dispatch.</summary>
    internal static Fine[] Dispatch(TickContext ctx)
    {
        var w = ctx.World;
        var b = ctx.Balance;
        var loads = w.Loads;
        var subs = w.Substations;
        int provinces = w.Provinces.Count;

        // 1. Substation capacity shared by tier among the loads behind it.
        var connected = new Fixed[loads.Count];
        for (int s = 0; s < subs.Count; s++)
        {
            var members = Enumerable.Range(0, loads.Count).Where(l => loads.Substation[l] == s).ToArray();
            var demand = members.Select(l => loads.DemandMw[l]).ToArray();
            var tiers = members.Select(l => EconomyRules.LoadTier(w, b, l)).ToArray();
            var got = new Fixed[members.Length];
            Allocation.ByTier(subs.CapacityMw[s].Times(subs.CapacityFactor(s, b.Grid)), demand, tiers, got);
            for (int k = 0; k < members.Length; k++) connected[members[k]] = got[k];
        }

        // 2. Regional balance and tie-line trade.
        var load = new Fixed[provinces];
        var generation = new Fixed[provinces];
        for (int l = 0; l < loads.Count; l++) load[loads.Province[l]] += connected[l];
        for (int g = 0; g < w.Plants.Count; g++)
            generation[w.Plants.Province[g]] += w.Plants.CapacityMw[g].Times(w.Plants.Available[g]);

        var supply = new Fixed[provinces];
        var surplus = new Fixed[provinces];
        for (int p = 0; p < provinces; p++)
        {
            supply[p] = generation[p];
            // A collapsed region can serve only its restored share of load, and exports nothing.
            surplus[p] = w.Provinces.Collapsed[p] ? Fixed.Min(Fixed.Zero, generation[p] - load[p]) : generation[p] - load[p];
        }
        for (int t = 0; t < w.TieLines.Count; t++)
        {
            int a = w.TieLines.A[t], bb = w.TieLines.B[t];
            var cap = w.TieLines.CapacityMw[t];
            var flow = Fixed.Zero;
            if (surplus[a] > Fixed.Zero && surplus[bb] < Fixed.Zero) flow = Fixed.Min(cap, Fixed.Min(surplus[a], -surplus[bb]));
            else if (surplus[bb] > Fixed.Zero && surplus[a] < Fixed.Zero) flow = -Fixed.Min(cap, Fixed.Min(surplus[bb], -surplus[a]));
            surplus[a] -= flow; surplus[bb] += flow;
            supply[a] -= flow; supply[bb] += flow;
            w.TieLines.Flow.Set(t, flow);
        }

        // 3. Regional supply shared by tier.
        var served = new Fine[loads.Count];
        for (int p = 0; p < provinces; p++)
        {
            var avail = supply[p];
            if (w.Provinces.Collapsed[p]) avail = Fixed.Min(avail, load[p].Times(w.Provinces.RestoreLevel[p]));
            var members = Enumerable.Range(0, loads.Count).Where(l => loads.Province[l] == p).ToArray();
            var demand = members.Select(l => connected[l]).ToArray();
            var tiers = members.Select(l => EconomyRules.LoadTier(w, b, l)).ToArray();
            var got = new Fixed[members.Length];
            Allocation.ByTier(avail, demand, tiers, got);
            for (int k = 0; k < members.Length; k++)
            {
                int l = members[k];
                var d = loads.DemandMw[l];
                served[l] = d <= Fixed.Zero ? Fine.One : Fine.Min(Fine.One, (got[k] / d).ToFine());
            }
        }
        return served;
    }

    /// <summary>
    /// Spec Black start: a collapsed region restores 25% of its load per day if it has a black-start plant,
    /// otherwise only while a tie-line neighbour is energized (D-028).
    /// </summary>
    private static void Restore(TickContext ctx, bool[] crisis, int hoursFraction)
    {
        var w = ctx.World;
        var prov = w.Provinces;
        for (int p = 0; p < prov.Count; p++)
        {
            if (!prov.Collapsed[p]) continue;
            // Hourly passes restore crisis provinces; the daily pass restores the rest.
            bool hourly = hoursFraction == 24;
            if (hourly != crisis[p]) continue;

            bool blackStart = Enumerable.Range(0, w.Plants.Count).Any(g => w.Plants.Province[g] == p && w.Plants.BlackStart[g]);
            bool neighbourLive = Enumerable.Range(0, w.TieLines.Count).Any(t =>
                (w.TieLines.A[t] == p && !prov.Collapsed[w.TieLines.B[t]]) ||
                (w.TieLines.B[t] == p && !prov.Collapsed[w.TieLines.A[t]]));
            if (!blackStart && !neighbourLive) continue;

            var level = prov.RestoreLevel[p] + ctx.Balance.Grid.BlackStartRestorePerDay / hoursFraction;
            if (level >= Fine.One)
            {
                prov.Collapsed.Set(p, false);
                prov.RestoreLevel.Set(p, Fine.One);
            }
            else prov.RestoreLevel.Set(p, level);
        }
    }

    /// <summary>
    /// Spec Substations: spare 14 days, mobile unit 30% after 7 days, new transformer 730–1,460 days.
    /// Crews: a spare or mobile job goes at full speed with the balance's linemen per job; fewer linemen slow
    /// every job in the province proportionally (D-028). A new transformer is manufacturing time.
    /// </summary>
    /// <param name="crisisProvinces">True: work on today's crisis provinces (called in hour 0); false: on the others (daily pass).</param>
    public static void AdvanceRepairs(TickContext ctx, bool crisisProvinces)
    {
        var inCrisis = CrisisMask(ctx);
        var w = ctx.World;
        var g = ctx.Balance.Grid;
        var subs = w.Substations;
        int crewPool = w.Labour.PoolId(g.RepairCrewPool);
        var mobileDays = Fixed.FromInt(g.MobileUnitDays);

        for (int p = 0; p < w.Provinces.Count; p++)
        {
            if (inCrisis[p] != crisisProvinces) continue;
            int jobs = 0;
            for (int s = 0; s < subs.Count; s++)
            {
                if (subs.Province[s] != p) continue;
                if (subs.Repair[s] == (int)RepairKind.Spare) jobs++;
                if (subs.MobileAssigned[s] && subs.MobileProgress[s] < mobileDays) jobs++;
            }
            var rate = Fixed.One;
            if (jobs > 0)
            {
                var crew = w.Labour.Available[w.Labour.Index(p, crewPool)];
                rate = Fixed.Min(Fixed.One, crew / Fixed.FromInt((long)jobs * g.LinemenPerRepair));
            }

            for (int s = 0; s < subs.Count; s++)
            {
                if (subs.Province[s] != p) continue;
                if (subs.MobileAssigned[s] && subs.MobileProgress[s] < mobileDays)
                    subs.MobileProgress.Set(s, Fixed.Min(mobileDays, subs.MobileProgress[s] + rate));

                var kind = (RepairKind)subs.Repair[s];
                if (kind == RepairKind.None) continue;
                var progress = subs.RepairProgress[s] + (kind == RepairKind.Spare ? rate : Fixed.One);
                if (progress >= subs.RepairRequired[s])
                {
                    subs.State.Set(s, (int)SubstationState.Online);
                    subs.Repair.Set(s, (int)RepairKind.None);
                    subs.RepairProgress.Set(s, Fixed.Zero);
                    subs.RepairRequired.Set(s, Fixed.Zero);
                    if (subs.MobileAssigned[s])
                    {
                        // The mobile unit goes back to the national pool.
                        subs.MobileAssigned.Set(s, false);
                        subs.MobileProgress.Set(s, Fixed.Zero);
                        int owner = w.Provinces.Owner[p];
                        w.Nations.MobileSubstations.Set(owner, w.Nations.MobileSubstations.Pending(owner) + 1);
                    }
                }
                else subs.RepairProgress.Set(s, progress);
            }
        }
    }
}

/// <summary>
/// Spec Crisis sub-ticks, read as actual blackout (D-046): a province is in crisis while any load (grid node) is served
/// less than 70% of its demand. A damaged substation whose loads are still served doesn't count on its own; that
/// chronic damage shows in the Cascade view instead.
/// </summary>
public sealed class GridCrisisSignal(Balance balance) : ICrisisSignal
{
    public string Name => "grid";

    public bool IsUnstable(SimWorld w, int province)
    {
        var threshold = balance.Grid.CrisisBelowRatio;
        for (int l = 0; l < w.Loads.Count; l++)
            if (w.Loads.Province[l] == province && w.Loads.ServedLast[l] < threshold) return true;
        return false;
    }
}
