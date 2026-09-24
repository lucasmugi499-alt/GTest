using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Economy;

/// <summary>
/// Phase 3 (spec Production, Shortage allocation, Efficiency, Fab yield).
///
/// Per province and good, every claim on stock — facility inputs and final demand — is allocated by
/// priority tier. Facilities then produce Q = C · E · min(1, min_i a_i ÷ (r_i C)) · P · L · (1 − D),
/// consume r_i · Q of each input and add their outputs. Final demand's share is set aside for phase 5.
/// Outputs land in stock after the phase commits, so a downstream line uses them the next day.
/// </summary>
public sealed class ProductionPhase : IPhase
{
    public PhaseId Id => PhaseId.Production;

    public void RunDaily(TickContext ctx)
    {
        var w = ctx.World;
        var b = ctx.Balance;
        var f = w.Facilities;
        var stocks = w.Stocks;
        var recipes = w.Catalog.Recipes;
        int goods = stocks.Goods;

        // Allocated input per facility, indexed like the recipe's input list.
        var allocated = new Fixed[f.Count][];
        for (int i = 0; i < f.Count; i++) allocated[i] = new Fixed[recipes[f.Recipe[i]].Inputs.Count];

        for (int p = 0; p < w.Provinces.Count; p++)
        {
            for (int g = 0; g < goods; g++)
            {
                var claims = new List<(bool IsFacility, int Id, int Slot, Fixed Demand, PriorityTier Tier)>();
                for (int i = 0; i < f.Count; i++)
                {
                    if (f.Province[i] != p) continue;
                    var inputs = recipes[f.Recipe[i]].Inputs;
                    for (int k = 0; k < inputs.Count; k++)
                        if (inputs[k].Good == g)
                            claims.Add((true, i, k, inputs[k].Qty * f.Capacity[i], EconomyRules.FacilityTier(w, b, i)));
                }
                for (int d = 0; d < w.Demand.Count; d++)
                    if (w.Demand.Province[d] == p && w.Demand.Good[d] == g)
                        claims.Add((false, d, 0, w.Demand.PerDay[d], EconomyRules.DemandTier(w, b, d)));
                if (claims.Count == 0) continue;

                var got = new Fixed[claims.Count];
                Allocation.ByTier(stocks.Stock[stocks.At(p, g)], claims.Select(c => c.Demand).ToArray(), claims.Select(c => c.Tier).ToArray(), got);

                var reserved = Fixed.Zero;
                for (int c = 0; c < claims.Count; c++)
                {
                    if (claims[c].IsFacility) allocated[claims[c].Id][claims[c].Slot] = got[c];
                    else
                    {
                        w.Demand.ReservedToday.Set(claims[c].Id, got[c]);
                        reserved += got[c];
                    }
                }
                stocks.Reserved.Set(stocks.At(p, g), reserved);
            }
        }

        for (int i = 0; i < f.Count; i++) Produce(ctx, i, allocated[i]);
    }

    private static void Produce(TickContext ctx, int i, Fixed[] allocated)
    {
        var w = ctx.World;
        var b = ctx.Balance;
        var f = w.Facilities;
        var stocks = w.Stocks;
        var recipe = w.Catalog.Recipes[f.Recipe[i]];
        int p = f.Province[i];
        int nation = w.Provinces.Owner[p];
        var capacity = f.Capacity[i];

        // min(1, min_i a_i ÷ (r_i C))
        var inputFactor = Fine.One;
        for (int k = 0; k < recipe.Inputs.Count; k++)
        {
            var need = recipe.Inputs[k].Qty * capacity;
            if (need > Fixed.Zero) inputFactor = Fine.Min(inputFactor, (allocated[k] / need).ToFine());
        }

        int load = f.Load[i];
        var power = load >= 0 ? w.Loads.PowerRatio[load] : Fine.One;
        var labour = EconomyRules.LabourRatio(w, i);
        var e = EconomyRules.EffectiveEfficiency(b, f.Efficiency[i], w.Nations.Doctrine[nation]);

        var q = (capacity * e * (Fixed.One - f.Damage[i])).Times(inputFactor).Times(power).Times(labour);

        var outputMultiplier = Fine.One;
        if (recipe.Fab != FabClass.None)
            outputMultiplier = FabStep(ctx, i, recipe, q, load >= 0 && w.Loads.Interrupted[load]);

        for (int k = 0; k < recipe.Inputs.Count; k++)
        {
            var use = Fixed.Min(allocated[k], recipe.Inputs[k].Qty * q);
            int at = stocks.At(p, recipe.Inputs[k].Good);
            stocks.Stock.Set(at, stocks.Stock.Pending(at) - use);
            stocks.BurnToday.Set(at, stocks.BurnToday.Pending(at) + use);
        }
        for (int k = 0; k < recipe.Outputs.Count; k++)
        {
            var made = (recipe.Outputs[k].Qty * q).Times(outputMultiplier);
            int at = stocks.At(p, recipe.Outputs[k].Good);
            stocks.Stock.Set(at, stocks.Stock.Pending(at) + made);
            if (k == 0) f.OutputToday.Set(i, made);
        }
        f.RunToday.Set(i, q);

        // Spec Efficiency: E_{t+1} = E_t + g (E_max − E_t).
        var eMax = f.DualUse[i] ? b.Economy.EfficiencyMaxDualUse : b.Economy.EfficiencyMax;
        var eNow = f.Efficiency[i];
        f.Efficiency.Set(i, eNow + b.Economy.EfficiencyGrowthPerDay * (eMax - eNow));
    }

    /// <summary>
    /// Spec Fab yield. Returns the share of each wafer start that comes out as good output today: Y(X) × ramp.
    /// Any power interruption scraps all work in process (capacity × 42 days of starts) and output then ramps
    /// linearly back over 21 days of full power.
    /// </summary>
    private static Fine FabStep(TickContext ctx, int i, RecipeDef recipe, Fixed starts, bool interrupted)
    {
        var w = ctx.World;
        var fab = ctx.Balance.Fab;
        var f = w.Facilities;
        var capacity = f.Capacity[i];

        if (interrupted)
        {
            // The line holds capacity × 42 days of starts when running steadily; while it refills after an
            // earlier interruption it holds that times the ramp so far (D-032). An empty line has nothing to scrap.
            if (f.RampDone[i] > 0)
            {
                var wip = (capacity * fab.WipCycleDays).Times(Fine.Ratio(f.RampDone[i], fab.RampDays));
                f.WipScrapped.Set(i, f.WipScrapped[i] + wip);
                f.Interruptions.Set(i, f.Interruptions[i] + 1);
            }
            f.RampDone.Set(i, 0);
        }
        else if (f.RampDone[i] < fab.RampDays)
        {
            f.RampDone.Set(i, f.RampDone[i] + 1);
        }

        // Losing engineers multiplies X by (1 − 0.5 × share lost).
        var x = f.Experience[i];
        int pool = w.Labour.PoolId(fab.EngineerPool);
        var ratio = w.Labour.Ratio(f.Province[i], pool);
        var prev = f.EngineerRatioPrev[i];
        if (ratio < prev && prev > Fine.Zero)
            x = x.Times(Fine.One - (fab.EngineerLossFactor.ToFine() * ((prev - ratio) / prev)));
        f.EngineerRatioPrev.Set(i, ratio);

        // X counts cumulative starts ÷ daily capacity.
        if (capacity > Fixed.Zero) x += starts / capacity;
        f.Experience.Set(i, x);

        var yMax = recipe.Fab == FabClass.Leading ? fab.YieldMaxLeading : fab.YieldMaxLegacy;
        var decay = FixedMath.Exp(-(x / Fixed.FromInt(fab.ExperienceScaleDays)).ToFine());
        var y = yMax - (yMax - fab.YieldStart).Times(decay);
        f.Yield.Set(i, y);

        var ramp = Fine.Ratio(f.RampDone.Pending(i), fab.RampDays);
        return y.ToFine() * ramp;
    }
}
