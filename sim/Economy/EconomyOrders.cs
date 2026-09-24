using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Economy;

public enum RepairChoice { Spare = 0, Mobile = 1, NewTransformer = 2 }

/// <summary>Send a spare transformer, a mobile emergency unit, or order a new transformer (spec Substations).</summary>
public sealed class RepairSubstationOrder(int issuer, int substation, RepairChoice choice) : Order(issuer)
{
    public override string Kind => "grid.repair_substation";

    public override OrderOutcome Apply(TickContext ctx)
    {
        var w = ctx.World;
        var g = ctx.Balance.Grid;
        var subs = w.Substations;
        var n = w.Nations;
        if (w.Provinces.Owner[subs.Province[substation]] != Issuer) return OrderOutcome.Refused("Not your substation.");
        if (subs.State[substation] == (int)SubstationState.Online) return OrderOutcome.Refused("Substation is working.");
        if (subs.State[substation] == (int)SubstationState.Tripped) return OrderOutcome.Refused("Tripped, not damaged: it comes back when the disruption ends.");

        switch (choice)
        {
            case RepairChoice.Mobile:
                if (subs.MobileAssigned[substation]) return OrderOutcome.Refused("A mobile unit is already there.");
                if (n.MobileSubstations.Pending(Issuer) <= 0) return OrderOutcome.Refused("No mobile units left.");
                n.MobileSubstations.Set(Issuer, n.MobileSubstations.Pending(Issuer) - 1);
                subs.MobileAssigned.Set(substation, true);
                subs.MobileProgress.Set(substation, Fixed.Zero);
                return OrderOutcome.Ok;

            case RepairChoice.Spare:
                if (subs.Repair[substation] != (int)RepairKind.None) return OrderOutcome.Refused("A repair is already under way.");
                if (n.SpareTransformers.Pending(Issuer) <= 0) return OrderOutcome.Refused("No spare transformers left.");
                n.SpareTransformers.Set(Issuer, n.SpareTransformers.Pending(Issuer) - 1);
                Start(subs, RepairKind.Spare, Fixed.FromInt(g.SpareRepairDays));
                return OrderOutcome.Ok;

            default:
                if (subs.Repair[substation] != (int)RepairKind.None) return OrderOutcome.Refused("A repair is already under way.");
                var rng = ctx.Rng(SystemId.GridDispatch, EntityRef.Of(EntityKind.Substation, substation));
                Start(subs, RepairKind.NewTransformer, Fixed.FromInt(rng.NextInt(g.NewTransformerDaysMin, g.NewTransformerDaysMax + 1)));
                return OrderOutcome.Ok;
        }
    }

    private void Start(SubstationStore subs, RepairKind kind, Fixed days)
    {
        subs.Repair.Set(substation, (int)kind);
        subs.RepairProgress.Set(substation, Fixed.Zero);
        subs.RepairRequired.Set(substation, days);
    }

    protected override void HashFields(StateHasher h) => h.Add(substation).Add((int)choice);
}

public enum PriorityTarget { Facility = 0, Load = 1, Demand = 2 }

/// <summary>Reorder the priority list: move one consumer to a tier, or back to its default (tier null).</summary>
public sealed class SetPriorityOrder(int issuer, PriorityTarget target, int id, PriorityTier? tier) : Order(issuer)
{
    public override string Kind => "economy.set_priority";

    public override OrderOutcome Apply(TickContext ctx)
    {
        var w = ctx.World;
        int province = target switch
        {
            PriorityTarget.Facility => w.Facilities.Province[id],
            PriorityTarget.Load => w.Loads.Province[id],
            _ => w.Demand.Province[id],
        };
        if (w.Provinces.Owner[province] != Issuer) return OrderOutcome.Refused("Not in your territory.");
        int value = tier is null ? -1 : (int)tier.Value;
        switch (target)
        {
            case PriorityTarget.Facility:
                w.Facilities.TierOverride.Set(id, value);
                // A facility's own grid load follows it.
                if (w.Facilities.Load[id] >= 0) w.Loads.TierOverride.Set(w.Facilities.Load[id], value);
                break;
            case PriorityTarget.Load: w.Loads.TierOverride.Set(id, value); break;
            default: w.Demand.TierOverride.Set(id, value); break;
        }
        return OrderOutcome.Ok;
    }

    protected override void HashFields(StateHasher h) => h.Add((int)target).Add(id).Add(tier is null ? -1 : (int)tier.Value);
}

/// <summary>Move the stockpile doctrine slider (spec Days of Cover and doctrine).</summary>
public sealed class SetDoctrineOrder(int issuer, Fine doctrine) : Order(issuer)
{
    public override string Kind => "economy.set_doctrine";

    public override OrderOutcome Apply(TickContext ctx)
    {
        ctx.World.Nations.Doctrine.Set(Issuer, Fine.Clamp01(doctrine));
        return OrderOutcome.Ok;
    }

    protected override void HashFields(StateHasher h) => h.Add(doctrine);
}

/// <summary>Switch a line to another recipe: E drops to max(0.1, E × similarity) (spec Efficiency and retooling).</summary>
public sealed class SwitchRecipeOrder(int issuer, int facility, int recipe) : Order(issuer)
{
    public override string Kind => "economy.switch_recipe";

    public override OrderOutcome Apply(TickContext ctx)
    {
        var w = ctx.World;
        var f = w.Facilities;
        if (w.Provinces.Owner[f.Province[facility]] != Issuer) return OrderOutcome.Refused("Not your facility.");
        var from = w.Catalog.Recipes[f.Recipe[facility]];
        var to = w.Catalog.Recipes[recipe];
        if (from.Id == to.Id) return OrderOutcome.Refused("Already on that recipe.");
        if ((from.Fab == FabClass.None) != (to.Fab == FabClass.None)) return OrderOutcome.Refused("A fab can't become an assembly line, or the reverse.");
        var e = ctx.Balance.Economy;
        var similarity = from.Line == to.Line ? e.RetoolSameLine : e.RetoolNewLine;
        f.Recipe.Set(facility, recipe);
        Retool(f, facility, similarity, e.EfficiencyMin);
        return OrderOutcome.Ok;
    }

    internal static void Retool(FacilityStore f, int facility, Fixed similarity, Fixed min) =>
        f.Efficiency.Set(facility, Fixed.Max(min, f.Efficiency.Pending(facility) * similarity));

    protected override void HashFields(StateHasher h) => h.Add(facility).Add(recipe);
}

/// <summary>Design Bureau firmware patch: +0.10 effectiveness up to the cap, after 5 days (spec Countermeasure decay).</summary>
public sealed class FirmwarePatchOrder(int issuer, int design) : Order(issuer)
{
    public override string Kind => "design.firmware_patch";

    public override OrderOutcome Apply(TickContext ctx)
    {
        var n = ctx.World.Nations;
        if (n.BureauBusyUntil.Pending(Issuer) > ctx.Day) return OrderOutcome.Refused("The Design Bureau is busy.");
        int done = ctx.Day + ctx.Balance.Countermeasures.FirmwareDays;
        n.BureauBusyUntil.Set(Issuer, done);
        ctx.Events.Schedule(new DesignUpdateEvent(done, design, hardware: false));
        return OrderOutcome.Ok;
    }

    protected override void HashFields(StateHasher h) => h.Add(design);
}

/// <summary>Hardware revision: cap and effectiveness back to 1.0, and every line building it retools at 0.8.</summary>
public sealed class HardwareRevisionOrder(int issuer, int design) : Order(issuer)
{
    public override string Kind => "design.hardware_revision";

    public override OrderOutcome Apply(TickContext ctx)
    {
        var n = ctx.World.Nations;
        if (n.BureauBusyUntil.Pending(Issuer) > ctx.Day) return OrderOutcome.Refused("The Design Bureau is busy.");
        int done = ctx.Day + ctx.Balance.Countermeasures.RevisionDays;
        n.BureauBusyUntil.Set(Issuer, done);
        ctx.Events.Schedule(new DesignUpdateEvent(done, design, hardware: true));
        return OrderOutcome.Ok;
    }

    protected override void HashFields(StateHasher h) => h.Add(design);
}

/// <summary>A firmware patch or hardware revision lands.</summary>
public sealed class DesignUpdateEvent(int day, int design, bool hardware) : SimEvent(day)
{
    public override string Kind => hardware ? "design.revision_done" : "design.firmware_done";

    public override void Apply(TickContext ctx)
    {
        var w = ctx.World;
        var c = ctx.Balance.Countermeasures;
        var d = w.Designs;
        if (!hardware)
        {
            d.Effectiveness.Set(design, Fixed.Min(d.Cap.Pending(design), d.Effectiveness.Pending(design) + c.FirmwareGain));
            return;
        }
        d.Cap.Set(design, c.RevisionCap);
        d.Effectiveness.Set(design, c.RevisionCap);
        string key = w.Catalog.Designs[design].Key;
        for (int f = 0; f < w.Facilities.Count; f++)
            if (w.Catalog.Recipes[w.Facilities.Recipe[f]].Design == key)
                SwitchRecipeOrder.Retool(w.Facilities, f, ctx.Balance.Economy.RetoolSameLine, ctx.Balance.Economy.EfficiencyMin);
    }

    protected override void HashFields(StateHasher h) => h.Add(design).Add(hardware);
}

/// <summary>
/// Physical damage to substations (from sabotage or a destructive cyber payload): zero capacity until repaired.
/// If the damaged substations carried at least the balance's share of the region's load, the region collapses
/// and must black-start (D-028).
/// </summary>
public sealed class SubstationDamageEvent(int day, int hour, int province, IReadOnlyList<int> substations) : SimEvent(day, hour, province)
{
    public override string Kind => "grid.substation_damage";
    public IReadOnlyList<int> Substations => substations;

    public override void Apply(TickContext ctx)
    {
        Damage(ctx, Province, substations);
        ctx.World.Log.Add(ctx.Day, ctx.Hour, "grid", $"{substations.Count} substations damaged in {ctx.World.Provinces.Names[Province]}.",
            "", ctx.World.Provinces.Keys[Province]);
    }

    /// <summary>Marks substations damaged and collapses the region if they carried enough of its load.</summary>
    public static void Damage(TickContext ctx, int province, IReadOnlyList<int> substations)
    {
        var w = ctx.World;
        foreach (int s in substations)
        {
            w.Substations.State.Set(s, (int)SubstationState.Damaged);
            w.Substations.TripHoursLeft.Set(s, Fixed.Zero);
        }

        var total = Fixed.Zero;
        var lost = Fixed.Zero;
        for (int l = 0; l < w.Loads.Count; l++)
        {
            if (w.Loads.Province[l] != province) continue;
            total += w.Loads.DemandMw[l];
            if (w.Substations.State.Pending(w.Loads.Substation[l]) != (int)SubstationState.Online) lost += w.Loads.DemandMw[l];
        }
        if (total > Fixed.Zero && (lost / total).ToFine() >= ctx.Balance.Grid.CollapseLoadShare)
        {
            w.Provinces.Collapsed.Set(province, true);
            w.Provinces.RestoreLevel.Set(province, Fine.Zero);
        }
    }

    protected override void HashFields(StateHasher h)
    {
        h.Add(substations.Count);
        foreach (int s in substations) h.Add(s);
    }
}

/// <summary>A nation starts or lifts export controls on some goods (spec Supply shock). Shipments already at sea still arrive.</summary>
public sealed class ExportControlEvent(int day, int source, IReadOnlyList<int> goods, bool active) : SimEvent(day)
{
    public override string Kind => "trade.export_control";

    public override void Apply(TickContext ctx)
    {
        var im = ctx.World.Imports;
        for (int r = 0; r < im.Count; r++)
            if (im.Source[r] == source && goods.Contains(im.Good[r])) im.Blocked.Set(r, active);
    }

    protected override void HashFields(StateHasher h)
    {
        h.Add(source).Add(active).Add(goods.Count);
        foreach (int g in goods) h.Add(g);
    }
}
