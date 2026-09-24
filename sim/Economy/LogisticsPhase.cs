using Cascade.Sim.Core;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Economy;

/// <summary>
/// Phase 4 (spec Transport, simplified by D-007): imports and domestic shipments with fixed lead times and capacities.
///
/// Imports: each open route orders EMA burn + (target − position) ÷ replenish days, up to its capacity, where
/// position is the nation's stock plus everything in transit and target is the doctrine's days of burn (D-029).
/// Domestic: along each edge, each way, stock moves to level the two provinces' local Days of Cover (after today's
/// reserved final demand), up to the edge's tonnes per day for that good's class (D-033).
/// </summary>
public sealed class LogisticsPhase : IPhase
{
    public PhaseId Id => PhaseId.Logistics;

    public void RunDaily(TickContext ctx)
    {
        var w = ctx.World;
        var b = ctx.Balance;
        var stocks = w.Stocks;
        var goods = w.Catalog.Goods;
        int provinces = w.Provinces.Count;

        var inbound = new Fixed[provinces * goods.Count];
        foreach (var s in w.Shipments.Items) inbound[stocks.At(s.To, s.Good)] += s.Qty;

        // Imports.
        var im = w.Imports;
        for (int r = 0; r < im.Count; r++)
        {
            int g = im.Good[r];
            int to = im.To[r];
            int nation = w.Provinces.Owner[to];
            var qty = Fixed.Zero;
            if (!im.Blocked[r])
            {
                var position = Fixed.Zero;
                var ema = Fixed.Zero;
                for (int p = 0; p < provinces; p++)
                {
                    if (w.Provinces.Owner[p] != nation) continue;
                    position += stocks.Stock[stocks.At(p, g)] + inbound[stocks.At(p, g)];
                    ema += stocks.BurnEma[stocks.At(p, g)];
                }
                var target = ema * EconomyRules.TargetDays(b, w.Nations.Doctrine[nation]);
                qty = ema + (target - position) / b.Economy.ImportReplenishDays;
                qty = Fixed.Clamp(qty, Fixed.Zero, im.CapacityPerDay[r]);
            }
            im.ShippedToday.Set(r, qty);
            if (qty > Fixed.Zero)
            {
                w.Shipments.Add(g, -1, to, qty, ctx.Day + im.LeadDays[r]);
                inbound[stocks.At(to, g)] += qty;
            }
        }

        // Domestic.
        var e = w.Edges;
        for (int k = 0; k < e.Count; k++)
        {
            Ship(ctx, k, e.A[k], e.B[k], inbound);
            Ship(ctx, k, e.B[k], e.A[k], inbound);
        }
    }

    private static void Ship(TickContext ctx, int edge, int from, int to, Fixed[] inbound)
    {
        var w = ctx.World;
        var stocks = w.Stocks;
        var goods = w.Catalog.Goods;
        int owner = w.Provinces.Owner[from];
        if (w.Provinces.Owner[to] != owner) return;

        // Kilograms, not tonnes: one chip is 0.000001 t, below the 4-decimal stored precision.
        var kgLeft = new Fixed[4];
        for (int c = 0; c < 4; c++) kgLeft[c] = w.Edges.CapacityTonnes[edge * 4 + c] * 1000;
        for (int g = 0; g < goods.Count; g++)
        {
            int src = stocks.At(from, g), dst = stocks.At(to, g);
            // Level local Days of Cover (D-033): move x so that (S_s − x) ÷ E_s = (S_d + I_d + x) ÷ E_d.
            // A province that doesn't use the good passes all of it on; one that doesn't need it receives none.
            var available = stocks.Stock.Pending(src) - stocks.Reserved[src];
            var emaSrc = stocks.BurnEma[src];
            var emaDst = stocks.BurnEma[dst];
            if (available <= Fixed.Zero || emaDst <= Fixed.Zero) continue;
            var position = stocks.Stock[dst] + inbound[dst];
            // In raw 128-bit integers: stock × burn products overflow 64 bits for goods counted in millions.
            var move = emaSrc <= Fixed.Zero
                ? available
                : Fixed.FromRaw((long)(((Int128)available.Raw * emaDst.Raw - (Int128)position.Raw * emaSrc.Raw) / (emaSrc.Raw + emaDst.Raw)));
            if (move <= Fixed.Zero) continue;

            var good = goods[g];
            int cls = (int)good.Class;
            var byCapacity = good.MassKg > Fixed.Zero ? kgLeft[cls] / good.MassKg : move;
            var qty = Fixed.Min(move, byCapacity);
            if (qty <= Fixed.Zero) continue;

            stocks.Stock.Set(src, stocks.Stock.Pending(src) - qty);
            kgLeft[cls] -= qty * good.MassKg;
            inbound[dst] += qty;
            w.Shipments.Add(g, from, to, qty, ctx.Day + w.Edges.LeadDays[edge]);
        }
    }

    /// <summary>Phase 1's share of logistics (spec: scheduled events include deliveries): shipments due today land in stock.</summary>
    public static void DeliverDue(TickContext ctx)
    {
        var stocks = ctx.World.Stocks;
        foreach (var s in ctx.World.Shipments.TakeDue(ctx.Day))
        {
            int at = stocks.At(s.To, s.Good);
            stocks.Stock.Set(at, stocks.Stock.Pending(at) + s.Qty);
        }
    }
}
