using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Conflict;

/// <summary>Declare an emergency (spec: 30 Political Capital; sets a precedent). Unlocks the emergency powers.</summary>
public sealed class DeclareEmergencyOrder(int issuer) : Order(issuer)
{
    public override string Kind => "politics.declare_emergency";

    public override OrderOutcome Apply(TickContext ctx)
    {
        var p = ctx.World.Politics;
        if (p.EmergencyActive.Pending(Issuer)) return OrderOutcome.Refused("An emergency is already in force.");
        var cost = Society.PrecedentCostPreview(ctx, Issuer, "emergency_declaration");
        if (!Society.TrySpend(ctx, Issuer, cost)) return OrderOutcome.Refused($"Needs {cost} Political Capital.");
        Society.UsePrecedent(ctx, Issuer, "emergency_declaration");
        p.EmergencyActive.Set(Issuer, true);
        p.EmergencySince.Set(Issuer, ctx.Day);
        ctx.World.Log.Add(ctx.Day, ctx.Hour, "emergency", $"{ctx.World.Nations.Names[Issuer]} declares a state of emergency.", ctx.World.Nations.Keys[Issuer]);
        return OrderOutcome.Ok;
    }

    protected override void HashFields(StateHasher h) { }
}

/// <summary>End the emergency: 10 Political Capital per month it lasted, up to 100. Every power lapses.</summary>
public sealed class EndEmergencyOrder(int issuer) : Order(issuer)
{
    public override string Kind => "politics.end_emergency";

    public override OrderOutcome Apply(TickContext ctx)
    {
        var w = ctx.World;
        var p = w.Politics;
        var so = ctx.Balance.Society;
        if (!p.EmergencyActive.Pending(Issuer)) return OrderOutcome.Refused("No emergency is in force.");
        int months = Math.Max(1, (ctx.Day - p.EmergencySince.Pending(Issuer) + 29) / 30);
        var cost = Fixed.Min(so.EmergencyEndCostMax, so.EmergencyEndCostPerMonth * months);
        if (!Society.TrySpend(ctx, Issuer, cost)) return OrderOutcome.Refused($"Needs {cost} Political Capital.");
        p.EmergencyActive.Set(Issuer, false);
        p.EmergencySince.Set(Issuer, -1);
        p.PowersMask.Set(Issuer, 0);
        for (int prov = 0; prov < w.Provinces.Count; prov++)
            if (w.Provinces.Owner[prov] == Issuer) w.Provinces.InternetShutdown.Set(prov, false);
        w.Log.Add(ctx.Day, ctx.Hour, "emergency", $"{w.Nations.Names[Issuer]} ends the state of emergency after {months} month{(months == 1 ? "" : "s")}.", w.Nations.Keys[Issuer]);
        return OrderOutcome.Ok;
    }

    protected override void HashFields(StateHasher h) { }
}

/// <summary>Use an emergency power (fuel requisition, press limits, curfew, or an internet shutdown in one province).</summary>
public sealed class UseEmergencyPowerOrder(int issuer, string power, int province = -1) : Order(issuer)
{
    public override string Kind => "politics.use_power";

    public override OrderOutcome Apply(TickContext ctx)
    {
        var w = ctx.World;
        var p = w.Politics;
        if (!p.EmergencyActive.Pending(Issuer)) return OrderOutcome.Refused("Needs a declared emergency.");
        int i = ctx.Content.Scenario.Society.Power(power);
        var def = ctx.Content.Scenario.Society.EmergencyPowers[i];
        bool shutdown = def.PerProvince;
        if (shutdown && (province < 0 || w.Provinces.Owner[province] != Issuer)) return OrderOutcome.Refused("Choose one of your provinces.");
        if (shutdown ? w.Provinces.InternetShutdown.Pending(province) : (p.PowersMask.Pending(Issuer) & (1 << i)) != 0)
            return OrderOutcome.Refused("Already in force.");
        var cost = Society.PrecedentCostPreview(ctx, Issuer, def.Precedent);
        if (!Society.TrySpend(ctx, Issuer, cost)) return OrderOutcome.Refused($"Needs {cost} Political Capital.");
        Society.UsePrecedent(ctx, Issuer, def.Precedent);
        p.PowersMask.Set(Issuer, p.PowersMask.Pending(Issuer) | (1 << i));
        if (shutdown) w.Provinces.InternetShutdown.Set(province, true);
        w.Log.Add(ctx.Day, ctx.Hour, "emergency", shutdown
            ? $"Internet shut down in {w.Provinces.Names[province]}."
            : $"Emergency power in force: {power.Replace('_', ' ')}.", w.Nations.Keys[Issuer], power);
        return OrderOutcome.Ok;
    }

    protected override void HashFields(StateHasher h) => h.Add(power).Add(province);
}

/// <summary>Nationalize a company (spec: 60 Political Capital; other companies' Loyalty −10; sets a precedent).</summary>
public sealed class NationalizeOrder(int issuer, string corporation) : Order(issuer)
{
    public override string Kind => "politics.nationalize";

    public override OrderOutcome Apply(TickContext ctx)
    {
        var w = ctx.World;
        int c = w.Corporations.IdOf(corporation);
        if (w.Corporations.Nationalized.Pending(c)) return OrderOutcome.Refused("Already a state asset.");
        var cost = Society.PrecedentCostPreview(ctx, Issuer, "nationalization");
        if (!Society.TrySpend(ctx, Issuer, cost)) return OrderOutcome.Refused($"Needs {cost} Political Capital.");
        Society.UsePrecedent(ctx, Issuer, "nationalization");
        w.Corporations.Nationalized.Set(c, true);
        for (int o = 0; o < w.Corporations.Count; o++)
            if (o != c) w.Corporations.Loyalty.Set(o, Fixed.Max(Fixed.Zero, w.Corporations.Loyalty.Pending(o) - ctx.Balance.Society.NationalizationLoyaltyHit));
        w.Log.Add(ctx.Day, ctx.Hour, "politics", $"{w.Corporations.Defs[c].Name} is nationalized.", w.Nations.Keys[Issuer], corporation);
        return OrderOutcome.Ok;
    }

    protected override void HashFields(StateHasher h) => h.Add(corporation);
}

/// <summary>
/// Ask the platform to take a narrative down (spec: contact weight to 30% if the company complies). It complies when
/// K + Loy/2 + P > 100 x_r + c (spec Corporations). The pressure P is Political Capital spent either way.
/// </summary>
public sealed class TakedownOrder(int issuer, int narrative, Fixed pressure, Fixed contract) : Order(issuer)
{
    public override string Kind => "info.takedown";

    public override OrderOutcome Apply(TickContext ctx)
    {
        var w = ctx.World;
        var so = ctx.Content.Scenario.Society;
        var platform = so.Platforms.FirstOrDefault(p => p.Owner != ScenarioDef.None);
        if (platform is null) return OrderOutcome.Refused("No platform company to ask.");
        int corp = w.Corporations.IdOf(platform.Owner);
        if (!Society.TrySpend(ctx, Issuer, pressure)) return OrderOutcome.Refused($"Needs {pressure} Political Capital.");
        bool nationalized = w.Corporations.Nationalized.Pending(corp);
        var lhs = contract + w.Corporations.Loyalty.Pending(corp) / 2 + pressure;
        var rhs = Fixed.Hundred.Times(so.Corporations[corp].RivalRevenueShare) + ctx.Balance.Information.TakedownComplianceCost;
        var name = so.Corporations[corp].Name;
        if (!nationalized && lhs <= rhs)
        {
            w.Log.Add(ctx.Day, ctx.Hour, "information", $"{name} declines to take down \"{so.Narratives[narrative].Name}\".", w.Nations.Keys[Issuer], platform.Owner);
            return OrderOutcome.Refused($"{name} declines ({lhs} vs {rhs}).");
        }
        w.Narratives.Takedown.Set(narrative, true);
        w.Log.Add(ctx.Day, ctx.Hour, "information", $"{name} takes down \"{so.Narratives[narrative].Name}\".", w.Nations.Keys[Issuer], platform.Owner);
        return OrderOutcome.Ok;
    }

    protected override void HashFields(StateHasher h) => h.Add(narrative).Add(pressure).Add(contract);
}

/// <summary>Counter-narrative: γ triples for 7 days. Going live only works while national Trust is above 50 (concept).</summary>
public sealed class CounterNarrativeOrder(int issuer, int narrative) : Order(issuer)
{
    public override string Kind => "info.counter";

    public override OrderOutcome Apply(TickContext ctx)
    {
        var w = ctx.World;
        if (w.Politics.Trust[Issuer] <= ctx.Balance.Information.CounterMinTrust)
            return OrderOutcome.Refused($"Trust is too low ({w.Politics.Trust[Issuer].ToString(1)}) for people to believe it.");
        w.Narratives.CounterUntil.Set(narrative, ctx.Day + ctx.Balance.Information.CounterDays - 1);
        w.Log.Add(ctx.Day, ctx.Hour, "information", $"The government answers \"{w.Narratives.Defs[narrative].Name}\" live.", w.Nations.Keys[Issuer]);
        return OrderOutcome.Ok;
    }

    protected override void HashFields(StateHasher h) => h.Add(narrative);
}

/// <summary>Prebunking: 1% of the targeted segments' susceptible people move to rejecting each day.</summary>
public sealed class PrebunkOrder(int issuer, int narrative, IReadOnlyList<int> segments) : Order(issuer)
{
    public override string Kind => "info.prebunk";

    public override OrderOutcome Apply(TickContext ctx)
    {
        if (!Society.TrySpend(ctx, Issuer, ctx.Balance.Information.PrebunkingCost))
            return OrderOutcome.Refused($"Needs {ctx.Balance.Information.PrebunkingCost} Political Capital.");
        var nar = ctx.World.Narratives;
        int mask = nar.PrebunkMask.Pending(narrative);
        foreach (int s in segments) mask |= 1 << s;
        nar.PrebunkMask.Set(narrative, mask);
        return OrderOutcome.Ok;
    }

    protected override void HashFields(StateHasher h)
    {
        h.Add(narrative).Add(segments.Count);
        foreach (int s in segments) h.Add(s);
    }
}

/// <summary>Forensic sweep of a province's grid control systems (D-014): finds and closes any foreign access there.</summary>
public sealed class ForensicSweepOrder(int issuer, int province) : Order(issuer)
{
    public override string Kind => "cyber.forensic_sweep";

    public override OrderOutcome Apply(TickContext ctx)
    {
        var w = ctx.World;
        if (!Society.TrySpend(ctx, Issuer, ctx.Balance.Cyber.ForensicSweepCost))
            return OrderOutcome.Refused($"Needs {ctx.Balance.Cyber.ForensicSweepCost} Political Capital.");
        var ops = w.Operations;
        bool found = false;
        for (int i = 0; i < ops.Count; i++)
        {
            if (ops.Victim[i] != Issuer || ops.State.Pending(i) != (int)OperationState.Ready) continue;
            if (w.Provinces.IdOf(ops.Defs[i].TargetProvince) != province) continue;
            Cyber.Detect(ctx, i, "Forensic sweep");
            found = true;
        }
        if (!found) w.Log.Add(ctx.Day, ctx.Hour, "cyber", $"Forensic sweep of {w.Provinces.Names[province]} finds nothing.", w.Nations.Keys[Issuer]);
        return OrderOutcome.Ok;
    }

    protected override void HashFields(StateHasher h) => h.Add(province);
}

/// <summary>Use one of the player's prepared cyber operations (Day 10 option D).</summary>
public sealed class LaunchCyberOperationOrder(int issuer, int operation) : Order(issuer)
{
    public override string Kind => "cyber.launch";

    public override OrderOutcome Apply(TickContext ctx)
    {
        var ops = ctx.World.Operations;
        if (ops.Attacker[operation] != Issuer) return OrderOutcome.Refused("Not your operation.");
        if (!Cyber.CanUse(ctx, operation, "disrupt")) return OrderOutcome.Refused("The access isn't ready.");
        Cyber.Use(ctx, operation, "disrupt");
        return OrderOutcome.Ok;
    }

    protected override void HashFields(StateHasher h) => h.Add(operation);
}
