using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Economy;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Conflict;

/// <summary>Spec Cyber access and detection; Payloads, effects and attribution.</summary>
public static class Cyber
{
    public static Fixed MinAccess(Balance b, string effect) => effect switch
    {
        "disrupt" => b.Cyber.MinAccessDisrupt,
        "manipulate" => b.Cyber.MinAccessManipulate,
        "destroy" => b.Cyber.MinAccessDestroy,
        _ => throw new ContentException($"Unknown cyber effect '{effect}' (disrupt, manipulate or destroy)."),
    };

    public static bool CanUse(TickContext ctx, int op, string effect) =>
        ctx.World.Operations.State.Pending(op) == (int)OperationState.Ready &&
        ctx.World.Operations.Access.Pending(op) >= MinAccess(ctx.Balance, effect);

    /// <summary>Grid disruption lasts 12 × (A ÷ 50) × payload × (1 − analog reserve) hours.</summary>
    public static Fixed DisruptionHours(Balance b, Fixed access, Fixed payload, Fine analog) =>
        (b.Cyber.DisruptionBaseHours * (access / Fixed.FromInt(50)) * payload).Times(Fine.One - analog);

    /// <param name="escalate">False for D-014's fallback: the caught attempt is the escalation (+4); the fallback only reuses the effect.</param>
    public static void Use(TickContext ctx, int op, string effect, bool escalate = true)
    {
        var w = ctx.World;
        var ops = w.Operations;
        var def = ops.Defs[op];
        int attacker = ops.Attacker[op], victim = ops.Victim[op];
        int province = w.Provinces.IdOf(def.TargetProvince);
        var access = ops.Access.Pending(op);

        string what;
        if (ops.Targets[op].Length == 0)
            what = $"a strike on {w.Nations.Names[victim]}'s {def.Id.Replace('_', ' ')} systems";
        else if (effect == "destroy")
        {
            SubstationDamageEvent.Damage(ctx, province, ops.Targets[op]);
            what = $"{ops.Targets[op].Length} substations in {w.Provinces.Names[province]} damaged";
        }
        else
        {
            var hours = DisruptionHours(ctx.Balance, access, def.Payload, def.AnalogReserve);
            foreach (int s in ops.Targets[op])
            {
                if (w.Substations.State.Pending(s) != (int)SubstationState.Online) continue;
                w.Substations.State.Set(s, (int)SubstationState.Tripped);
                w.Substations.TripHoursLeft.Set(s, hours);
            }
            what = $"{ops.Targets[op].Length} substations in {w.Provinces.Names[province]} tripped for {hours.ToString(1)} hours";
        }

        ops.State.Set(op, (int)OperationState.Used);
        ops.UsedDay.Set(op, ctx.Day);
        ops.Attribution.Set(op, Fine.Zero);
        if (escalate) Escalation.Record(ctx, attacker, victim, "disruptive_cyber", $"Cyber attack: {what}.");
        else ctx.World.Log.Add(ctx.Day, ctx.Hour, "cyber", $"Cyber attack: {what}.", ctx.World.Nations.Keys[attacker], def.Id);

        // Spec Burn: every target on the same vendor systems patches with 50% chance within 30 days.
        var rng = ctx.Rng(SystemId.Operations, EntityRef.Of(EntityKind.Operation, op));
        for (int other = 0; other < ops.Count; other++)
        {
            if (other == op || ops.Defs[other].Vendor != def.Vendor || ops.State.Pending(other) != (int)OperationState.Ready) continue;
            if (rng.Chance(ctx.Balance.Cyber.BurnPatchChance))
                ctx.Events.Schedule(new PatchEvent(ctx.Day + rng.NextInt(1, ctx.Balance.Cyber.BurnPatchDays + 1), other));
        }
    }

    /// <summary>
    /// The victim's intelligence finds the access: A resets to 0, defence rises by 10, and it's an escalation.
    /// A forensic sweep passes escalate false: D-014 books the +4 when the attacker's attempt is caught on the day.
    /// </summary>
    public static void Detect(TickContext ctx, int op, string how, bool escalate = true)
    {
        var ops = ctx.World.Operations;
        ops.State.Set(op, (int)OperationState.Detected);
        ops.Access.Set(op, Fixed.Zero);
        ops.Defence.Set(op, ops.Defence.Pending(op) + ctx.Balance.Cyber.DefenceGainOnDetection);
        var text = $"{how}: {ctx.World.Nations.Adjectives[ops.Attacker[op]]} access in {ctx.World.Provinces.Names[ctx.World.Provinces.IdOf(ops.Defs[op].TargetProvince)]} systems found and closed.";
        if (escalate) Escalation.Record(ctx, ops.Attacker[op], ops.Victim[op], "cyber_intrusion_detected", text);
        else ctx.World.Log.Add(ctx.Day, ctx.Hour, "cyber", text, ctx.World.Nations.Keys[ops.Victim[op]], ops.Defs[op].Id);
    }

    /// <summary>c(t) = c_max (1 − e^(−t/14)); c_max 0.9 direct, 0.6 through a proxy.</summary>
    public static Fine Attribution(Balance b, bool proxy, int days)
    {
        var max = proxy ? b.Cyber.AttributionProxyMax : b.Cyber.AttributionDirectMax;
        return max * (Fine.One - FixedMath.Exp(-Fine.Ratio(days, b.Cyber.AttributionDays)));
    }
}

public sealed class PatchEvent(int day, int op) : SimEvent(day)
{
    public override string Kind => "cyber.patch";

    public override void Apply(TickContext ctx)
    {
        var ops = ctx.World.Operations;
        if (ops.State.Pending(op) != (int)OperationState.Ready) return;
        ops.State.Set(op, (int)OperationState.Patched);
        ops.Access.Set(op, Fixed.Zero);
        ctx.World.Log.Add(ctx.Day, -1, "cyber", $"Vendor patch closes the {ops.Defs[op].Id.Replace('_', ' ')} foothold.", "", ops.Defs[op].Id);
    }

    protected override void HashFields(StateHasher h) => h.Add(op);
}

/// <summary>
/// Phase 7 (spec Operations). Hourly in crisis provinces: tripped substations count down. Daily: the same for other
/// provinces (24 hours at once), and attribution confidence grows for operations already used.
/// </summary>
public sealed class OperationsPhase : IHourlyPhase
{
    public PhaseId Id => PhaseId.Operations;

    public void RunHourly(TickContext ctx) => CountDown(ctx, crisis: true, Fixed.One);

    public void RunDaily(TickContext ctx)
    {
        CountDown(ctx, crisis: false, Fixed.FromInt(24));
        var ops = ctx.World.Operations;
        for (int i = 0; i < ops.Count; i++)
            if (ops.State[i] == (int)OperationState.Used)
                ops.Attribution.Set(i, Cyber.Attribution(ctx.Balance, ops.Defs[i].Proxy, ctx.Day - ops.UsedDay[i]));
    }

    private static void CountDown(TickContext ctx, bool crisis, Fixed hours)
    {
        var subs = ctx.World.Substations;
        for (int s = 0; s < subs.Count; s++)
        {
            if (subs.State[s] != (int)SubstationState.Tripped) continue;
            if (ctx.CrisisProvinces.Contains(subs.Province[s]) != crisis) continue;
            var left = subs.TripHoursLeft[s] - hours;
            if (left <= Fixed.Zero)
            {
                subs.State.Set(s, (int)SubstationState.Online);
                subs.TripHoursLeft.Set(s, Fixed.Zero);
            }
            else subs.TripHoursLeft.Set(s, left);
        }
    }
}

/// <summary>Monthly (not in the spec's monthly list; the formulas are per month): access grows, detection is rolled.</summary>
public sealed class CyberMonthlySystem : IPeriodicSystem
{
    public SystemId Id => SystemId.MonthlyCyber;

    public void Run(TickContext ctx)
    {
        var w = ctx.World;
        var c = ctx.Balance.Cyber;
        var ops = w.Operations;
        for (int i = 0; i < ops.Count; i++)
        {
            if (ops.State[i] != (int)OperationState.Ready) continue;
            // ΔA = Sk/10 × v
            var skill = w.Politics.CyberSkill[ops.Attacker[i]];
            var access = Fixed.Min(Fixed.Hundred, ops.Access[i] + skill / c.AccessSkillDivisor * ops.Defs[i].Vulnerability);
            ops.Access.Set(i, access);
            // p_det = 0.03 × A/50 × Def/50
            var p = c.DetectionBase * (access / c.DetectionScale).ToFine() * (ops.Defence[i] / c.DetectionScale).ToFine();
            var rng = ctx.Rng(SystemId.MonthlyCyber, EntityRef.Of(EntityKind.Operation, i));
            if (rng.Chance(p)) Cyber.Detect(ctx, i, "Routine monitoring");
        }
    }
}
