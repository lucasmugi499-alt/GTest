using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Economy;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Conflict;

/// <summary>
/// Phase 6 (spec Army and mobilization; Combat and fronts), light: one front segment at Veyl.
/// Daily: mobilization moves toward its target level (calling up or releasing reservists), then, if the front is
/// active, both sides fight with the spec's detection, combat power, loss and advance formulas.
/// </summary>
public sealed class MilitaryPhase : IPhase
{
    public PhaseId Id => PhaseId.Military;

    public void RunDaily(TickContext ctx)
    {
        for (int n = 0; n < ctx.World.Nations.Count; n++) Military.AdvanceMobilization(ctx, n);
        Military.Fight(ctx);
    }
}

public static class Military
{
    // ---- Mobilization ----

    public static MobilizationDef? MobilizationOf(TickContext ctx, int nation) =>
        ctx.Content.Scenario.Conflict.Mobilization.FirstOrDefault(m => m.Nation == ctx.World.Nations.Keys[nation]);

    public static int MaxLevel(TickContext ctx, int nation) => (MobilizationOf(ctx, nation)?.ReservistsByLevel.Length ?? 1) - 1;

    public static void AdvanceMobilization(TickContext ctx, int n)
    {
        var w = ctx.World;
        var p = w.Politics;
        int level = w.Nations.MobilizationLevel[n];
        int target = p.MobilizationTarget[n];
        if (level == target || p.MobilizationChangeDay[n] < 0 || ctx.Day < p.MobilizationChangeDay[n]) return;

        // Up: straight to the target once its time has passed. Down: one level per 30 days (spec).
        int next = target > level ? target : level - 1;
        SetLevel(ctx, n, level, next);
        p.MobilizationChangeDay.Set(n, next == target ? -1 : ctx.Day + ctx.Balance.Mobilization.StepDownDays);
    }

    private static void SetLevel(TickContext ctx, int n, int from, int to)
    {
        var w = ctx.World;
        w.Nations.MobilizationLevel.Set(n, to);
        var mob = MobilizationOf(ctx, n);
        if (mob is not null)
        {
            bool calledBefore = mob.ReservistsByLevel[from] > 0;
            bool calledNow = mob.ReservistsByLevel[to] > 0;
            if (calledNow && !calledBefore) CallUp(ctx, n, mob, w.Politics.ExemptMask.Pending(n), call: true);
            if (!calledNow && calledBefore) CallUp(ctx, n, mob, w.Politics.ExemptMask.Pending(n), call: false);
            if (calledNow) SetReserveBrigade(ctx, n, mob, mob.ReservistsByLevel[to]);
            else SetReserveBrigade(ctx, n, mob, 0);
        }
        w.Log.Add(ctx.Day, -1, "mobilization", $"{w.Nations.Names[n]} reaches mobilization level {to}.", w.Nations.Keys[n], "", Fixed.FromInt(to));
    }

    /// <summary>Skilled reservists leave (or return to) their labour pools; exempted pools stay (spec Manpower pool).</summary>
    public static void CallUp(TickContext ctx, int n, MobilizationDef mob, int exemptMask, bool call)
    {
        var w = ctx.World;
        foreach (var (provKey, poolKey, people) in mob.SkilledReservists)
        {
            int pool = w.Labour.PoolId(poolKey);
            if ((exemptMask & (1 << pool)) != 0) continue;
            int at = w.Labour.Index(w.Provinces.IdOf(provKey), pool);
            var now = w.Labour.Available.Pending(at);
            w.Labour.Available.Set(at, call ? Fixed.Max(Fixed.Zero, now - people) : now + people);
        }
    }

    public static Fixed SkilledCalled(TickContext ctx, MobilizationDef mob, int exemptMask)
    {
        var total = Fixed.Zero;
        foreach (var (_, poolKey, people) in mob.SkilledReservists)
            if ((exemptMask & (1 << ctx.World.Labour.PoolId(poolKey))) == 0) total += people;
        return total;
    }

    private static void SetReserveBrigade(TickContext ctx, int n, MobilizationDef mob, int reservists)
    {
        var w = ctx.World;
        for (int b = 0; b < w.Brigades.Count; b++)
        {
            if (!w.Brigades.Reserve[b] || w.Brigades.Nation[b] != n) continue;
            var exempted = Fixed.Zero;
            foreach (var (_, poolKey, people) in mob.SkilledReservists)
                if ((w.Politics.ExemptMask.Pending(n) & (1 << w.Labour.PoolId(poolKey))) != 0) exempted += people;
            var strength = Fixed.Max(Fixed.Zero, Fixed.FromInt(reservists) - (reservists > 0 ? exempted : Fixed.Zero)).Times(mob.FrontShare);
            w.Brigades.Strength.Set(b, strength);
            if (reservists == 0) w.Brigades.Posture.Set(b, (int)Posture.Hold);
        }
    }

    // ---- Combat ----

    public static int FrontBrigade(TickContext ctx, int nation)
    {
        var br = ctx.World.Brigades;
        int best = -1;
        for (int b = 0; b < br.Count; b++)
            if (br.Nation[b] == nation && br.Strength.Pending(b) > Fixed.Zero && (best < 0 || br.Strength.Pending(b) > br.Strength.Pending(best))) best = b;
        return best;
    }

    /// <summary>Deaths from a clash: lost strength is deaths ÷ killed share (one killed to three wounded).</summary>
    public static void KillAtFront(TickContext ctx, int nation, Fixed deaths)
    {
        if (deaths <= Fixed.Zero) return;
        int b = FrontBrigade(ctx, nation);
        if (b < 0) return;
        Casualties(ctx, b, deaths / ctx.Balance.Military.KilledShare.ToFixed());
    }

    /// <summary>
    /// Spec Air and casualties: losses split one killed to three wounded; half the wounded return after 60 days if
    /// medical supply is at least 0.7. Deaths are booked to the brigade's home segment and the nation's weekly toll.
    /// </summary>
    public static void Casualties(TickContext ctx, int b, Fixed lost)
    {
        var w = ctx.World;
        var m = ctx.Balance.Military;
        var br = w.Brigades;
        lost = Fixed.Min(lost, br.Strength.Pending(b));
        if (lost <= Fixed.Zero) return;
        br.Strength.Set(b, br.Strength.Pending(b) - lost);
        var killed = lost.Times(m.KilledShare);
        var wounded = lost - killed;
        br.Killed.Set(b, br.Killed.Pending(b) + killed);

        int n = br.Nation[b];
        w.Politics.KiaWeek.Set(n, w.Politics.KiaWeek.Pending(n) + killed);
        w.Politics.KiaTotal.Set(n, w.Politics.KiaTotal.Pending(n) + killed);
        int home = br.Home[b];
        if (home >= 0)
        {
            var per100k = killed * 100_000 / w.Segments.Population[home];
            w.Segments.IncidentsMonth.Set(home, w.Segments.IncidentsMonth.Pending(home) + per100k);
        }
        if (MedicalSupply(ctx) >= m.MedicalMin)
        {
            var returning = wounded.Times(m.WoundedReturnShare);
            if (returning > Fixed.Zero) ctx.Events.Schedule(new WoundedReturnEvent(ctx.Day + m.WoundedReturnDays, b, returning));
        }
    }

    /// <summary>Medical supply for the front: how well the player's hospitals are running (D-037).</summary>
    private static Fine MedicalSupply(TickContext ctx)
    {
        var loads = ctx.World.Loads;
        var sum = Fine.Zero;
        int count = 0;
        for (int l = 0; l < loads.Count; l++)
            if (loads.Kind[l] == ctx.Balance.Military.MedicalKind) { sum += loads.ServiceAvailability[l]; count++; }
        return count == 0 ? Fine.One : sum / count;
    }

    public static Fixed Quality(Balance b, BrigadeStore br, int i)
    {
        var m = b.Military;
        return m.QualityTraining * br.Training[i] + m.QualityEquipment * br.Equipment[i] + m.QualityCohesion * br.Cohesion[i] + m.QualityExperience * br.Experience[i];
    }

    // ---- The spec's combat formulas, as pure functions ----

    /// <summary>CP = N × Q/50 × S × M</summary>
    public static Fixed CombatPower(Fixed n, Fixed quality, Fixed qualityScale, Fine supply, Fixed morale) =>
        (n * (quality / qualityScale) * morale).Times(supply);

    /// <summary>p_A = σ((Rec_A − Con_B) / 10)</summary>
    public static Fine Detection(Fixed recon, Fixed concealment) => FixedMath.Sigmoid((recon - concealment) / 10);

    /// <summary>L_B = k × CP_A p_A / CP_B × (1 − π_B), as a share of B's strength lost today (capped at 1).</summary>
    public static Fine LossShare(Fine k, Fixed cpA, Fine pA, Fixed cpB, Fine protectionB) =>
        cpB <= Fixed.Zero ? Fine.One : Fine.Min(Fine.One, k * (cpA.Times(pA) / cpB).ToFine() * (Fine.One - protectionB));

    /// <summary>ρ = CP_att p_att ÷ (CP_def p_def φ)</summary>
    public static Fixed ForceRatio(Fixed cpAtt, Fine pAtt, Fixed cpDef, Fine pDef, Fixed fortification) =>
        cpAtt.Times(pAtt) / (cpDef.Times(pDef) * fortification);

    /// <summary>v = v_max (1 − e^(−(ρ − 1.5))) λ for ρ above the threshold, else 0.</summary>
    public static Fixed Advance(Fixed vmax, Fixed rho, Fixed threshold, Fixed lambda) =>
        rho <= threshold ? Fixed.Zero : (vmax * lambda).Times(Fine.One - FixedMath.Exp(-(rho - threshold).ToFine()));

    public static void Fight(TickContext ctx)
    {
        var w = ctx.World;
        var m = ctx.Balance.Military;
        var f = w.Front;
        var br = w.Brigades;
        int nations = w.Nations.Count;

        bool active = false;
        for (int n = 0; n < nations; n++) if (f.ActiveUntil[n] >= ctx.Day) active = true;

        // Stand down attack postures whose window has closed.
        for (int b = 0; b < br.Count; b++)
            if (br.Posture[b] == (int)Posture.Attack && f.ActiveUntil[br.Nation[b]] < ctx.Day) br.Posture.Set(b, (int)Posture.Hold);

        var N = new Fixed[nations]; var Q = new Fixed[nations]; var M = new Fixed[nations];
        var density = new Fixed[nations]; var rec = new Fixed[nations]; var p = new Fine[nations];
        var S = new Fine[nations]; var cp = new Fixed[nations]; var attacking = new bool[nations]; var mech = new bool[nations];

        for (int n = 0; n < nations; n++)
        {
            for (int b = 0; b < br.Count; b++)
            {
                if (br.Nation[b] != n || br.Strength[b] <= Fixed.Zero) continue;
                N[n] += br.Strength[b];
                Q[n] += br.Strength[b] * Quality(ctx.Balance, br, b);
                M[n] += br.Strength[b] * br.Morale[b];
                if (br.Posture[b] == (int)Posture.Attack) { attacking[n] = true; mech[n] |= br.Mechanized[b]; }
            }
            if (N[n] > Fixed.Zero) { Q[n] /= N[n]; M[n] /= N[n]; }
            if (active && N[n] > Fixed.Zero) density[n] = UseDrones(ctx, n) / f.Def.WidthKm;
        }

        for (int n = 0; n < nations; n++)
        {
            var side = f.Side[n];
            if (side is null || N[n] <= Fixed.Zero) continue;
            int enemy = Enemy(w, n);
            var enemySide = f.Side[enemy];
            if (enemySide is null) continue;

            // Rec = min(100, 40 drones + 30 sat + 20 sigint + 10 humint) × (1 − enemy jamming); p = σ((Rec − Con)/10)
            var eff = side.DroneDesign is not null ? w.Designs.Effectiveness[ctx.Content.Catalog.Design(side.DroneDesign)].ToFine() : side.DroneEffectiveness;
            var cover = Fine.Min(Fine.One, (density[n] / m.DroneCoverFullDensity).ToFine()) * eff;
            var score = m.ReconDrones.Times(cover) + m.ReconSatellite.Times(side.Satellite) + m.ReconSigint.Times(side.Sigint) + m.ReconHumint.Times(side.Humint);
            rec[n] = Fixed.Min(Fixed.Hundred, score).Times(Fine.One - enemySide.Jamming);
            p[n] = Detection(rec[n], enemySide.Concealment);

            // Supply fill: diesel at the front, less trucks lost in the enemy's kill zone.
            var truckLoss = Fine.Min(Fine.One, m.TruckLossPerDensity * density[enemy].ToFine());
            S[n] = (FrontSupply(ctx, n, N[n], consume: active) * (Fine.One - truckLoss));

            var warSupport = w.Politics.WarSupport[n];
            var morale = Fixed.Clamp(M[n] * (Fixed.One + m.MoralePerWarSupport * (warSupport - Fixed.FromInt(50))), m.MoraleMin, m.MoraleMax);
            // CP = N × Q/50 × S × M
            cp[n] = CombatPower(N[n], Q[n], m.QualityScale, S[n], morale);
        }

        for (int n = 0; n < nations; n++)
        {
            f.DroneDensity.Set(n, density[n]);
            f.Detection.Set(n, p[n]);
            f.CombatPower.Set(n, cp[n]);
            f.KillZone.Set(n, Fixed.Min(m.KillZoneMax, m.KillZoneBase + m.KillZonePerDensity * density[Enemy(w, n)]));
            f.AdvanceToday.Set(n, Fixed.Zero);
        }

        bool locked = true;
        for (int n = 0; n < nations; n++)
            if (f.Side[n] is not null && N[n] > Fixed.Zero && (density[n] < m.LockDroneDensity || p[n] < m.LockDetection)) locked = false;
        f.Locked.Set(0, active && locked);

        if (!active)
        {
            for (int b = 0; b < br.Count; b++)
                if (br.Strength[b] > Fixed.Zero) br.Cohesion.Set(b, Fixed.Min(br.Defs[b].Cohesion, br.Cohesion[b] + m.CohesionRecovery));
            return;
        }

        // Losses both ways: L_B = k × CP_A p_A / CP_B × (1 − π_B)
        var lossShare = new Fine[nations];
        for (int n = 0; n < nations; n++)
        {
            int enemy = Enemy(w, n);
            if (f.Side[n] is null || cp[n] <= Fixed.Zero || cp[enemy] <= Fixed.Zero) continue;
            lossShare[n] = LossShare(m.CombatK, cp[enemy], p[enemy], cp[n], f.Side[n]!.Protection);
        }

        // Advance for attackers: ρ = CP_att p_att / (CP_def p_def φ); v = vmax (1 − e^(−(ρ − 1.5))) λ for ρ > 1.5
        for (int n = 0; n < nations; n++)
        {
            int enemy = Enemy(w, n);
            if (!attacking[n] || f.Side[enemy] is null || cp[enemy] <= Fixed.Zero || p[enemy] <= Fine.Zero) continue;
            var rho = ForceRatio(cp[n], p[n], cp[enemy], p[enemy], f.Side[enemy]!.Fortification);
            f.ForceRatio.Set(n, rho);
            var lambda = f.Locked.Pending(0) ? m.LockedLambda : Fixed.One;
            var v = Advance(mech[n] ? m.VmaxMechanized : m.VmaxInfantry, rho, m.AdvanceThreshold, lambda);
            if (v <= Fixed.Zero) continue;
            f.AdvanceToday.Set(n, v);
            f.AdvanceKm.Set(n, f.AdvanceKm[n] + v);
        }

        for (int b = 0; b < br.Count; b++)
        {
            int n = br.Nation[b];
            if (br.Strength[b] <= Fixed.Zero || lossShare[n] <= Fine.Zero) continue;
            var lost = br.Strength[b].Times(lossShare[n]);
            Casualties(ctx, b, lost);
            br.Cohesion.Set(b, Fixed.Max(Fixed.Zero, br.Cohesion[b] - m.CohesionLossFactor.Times(lossShare[n])));
            br.Experience.Set(b, Fixed.Min(Fixed.Hundred, br.Experience[b] + m.ExperiencePerCombatDay));
        }
    }

    /// <summary>The other side of the front (the slice has exactly two).</summary>
    public static int Enemy(SimWorld w, int n)
    {
        for (int o = 0; o < w.Nations.Count; o++) if (o != n && w.Front.Side[o] is not null) return o;
        return n;
    }

    /// <summary>Drones flown today: each brigade's daily use, limited for the player by drones in stock (D-037).</summary>
    private static Fixed UseDrones(TickContext ctx, int n)
    {
        var w = ctx.World;
        var planned = Fixed.Zero;
        for (int b = 0; b < w.Brigades.Count; b++)
            if (w.Brigades.Nation[b] == n && w.Brigades.Strength[b] > Fixed.Zero) planned += w.Brigades.Defs[b].DronesPerDay;
        if (n != w.Nations.Player) return planned;

        int drone = ctx.Content.Catalog.Good(w.Front.Def.DroneGood);
        var left = planned;
        var used = Fixed.Zero;
        var order = new[] { w.Front.Province }.Concat(Enumerable.Range(0, w.Provinces.Count).Where(x => x != w.Front.Province));
        foreach (int prov in order)
        {
            if (w.Provinces.Owner[prov] != n || left <= Fixed.Zero) continue;
            int at = w.Stocks.At(prov, drone);
            var take = Fixed.Min(left, w.Stocks.Stock.Pending(at));
            if (take <= Fixed.Zero) continue;
            w.Stocks.Stock.Set(at, w.Stocks.Stock.Pending(at) - take);
            w.Stocks.BurnToday.Set(at, w.Stocks.BurnToday.Pending(at) + take);
            left -= take; used += take;
        }
        return used;
    }

    /// <summary>Supply fill: diesel at the front ÷ 7 days of the brigades' burn, capped at 1 (D-037). The player burns it.</summary>
    private static Fine FrontSupply(TickContext ctx, int n, Fixed soldiers, bool consume)
    {
        var w = ctx.World;
        var m = ctx.Balance.Military;
        if (n != w.Nations.Player) return Fine.One;
        int diesel = ctx.Content.Catalog.Good(ctx.Balance.Grid.BackupFuelGood);
        int at = w.Stocks.At(w.Front.Province, diesel);
        var daily = m.DieselPer1000PerDay * soldiers / 1000;
        if (daily <= Fixed.Zero) return Fine.One;
        var stock = w.Stocks.Stock.Pending(at);
        var fill = Fine.Min(Fine.One, (stock / (daily * m.SupplyCoverDays)).ToFine());
        if (consume)
        {
            var use = Fixed.Min(stock, daily);
            w.Stocks.Stock.Set(at, stock - use);
            w.Stocks.BurnToday.Set(at, w.Stocks.BurnToday.Pending(at) + use);
        }
        return fill;
    }
}

public sealed class WoundedReturnEvent(int day, int brigade, Fixed people) : SimEvent(day)
{
    public override string Kind => "military.wounded_return";

    public override void Apply(TickContext ctx)
    {
        var br = ctx.World.Brigades;
        if (br.Reserve[brigade] && br.Strength.Pending(brigade) <= Fixed.Zero) return; // disbanded
        br.Strength.Set(brigade, br.Strength.Pending(brigade) + people);
    }

    protected override void HashFields(StateHasher h) => h.Add(brigade).Add(people);
}

/// <summary>Set the mobilization level (spec: going up takes 7 or 14 days, coming down 30 days a level).</summary>
public sealed class SetMobilizationOrder(int issuer, int level) : Order(issuer)
{
    public override string Kind => "military.set_mobilization";

    public override OrderOutcome Apply(TickContext ctx)
    {
        var w = ctx.World;
        var p = w.Politics;
        int current = w.Nations.MobilizationLevel[Issuer];
        int max = Military.MaxLevel(ctx, Issuer);
        if (level < 0 || level > max) return OrderOutcome.Refused($"Mobilization runs 0 to {max} in this scenario.");
        if (level > current && p.WarSupport[Issuer] < ctx.Balance.Society.MobilizationBlockedBelowWarSupport)
            return OrderOutcome.Refused("War support is too low to raise mobilization.");
        p.MobilizationTarget.Set(Issuer, level);
        if (level == current) { p.MobilizationChangeDay.Set(Issuer, -1); return OrderOutcome.Ok; }
        var days = ctx.Balance.Mobilization.DaysToReach;
        int wait = level > current ? Math.Max(1, days[level] - days[current]) : ctx.Balance.Mobilization.StepDownDays;
        p.MobilizationChangeDay.Set(Issuer, ctx.Day + wait);
        w.Log.Add(ctx.Day, ctx.Hour, "mobilization", $"{w.Nations.Names[Issuer]} orders mobilization level {level}.", w.Nations.Keys[Issuer], "", Fixed.FromInt(level));
        return OrderOutcome.Ok;
    }

    protected override void HashFields(StateHasher h) => h.Add(level);
}

/// <summary>Protect a labour pool from call-up, such as grid linemen (spec: an exemption list protects chosen pools).</summary>
public sealed class ExemptPoolOrder(int issuer, string pool, bool exempt) : Order(issuer)
{
    public override string Kind => "military.exempt_pool";

    public override OrderOutcome Apply(TickContext ctx)
    {
        var w = ctx.World;
        int id = w.Labour.PoolId(pool);
        var mask = w.Politics.ExemptMask.Pending(Issuer);
        int newMask = exempt ? mask | (1 << id) : mask & ~(1 << id);
        if (newMask == mask) return OrderOutcome.Ok;
        var mob = Military.MobilizationOf(ctx, Issuer);
        // If reservists are already out, the pool's people come home (or go).
        if (mob is not null && mob.ReservistsByLevel[w.Nations.MobilizationLevel[Issuer]] > 0)
        {
            var single = new MobilizationDef(mob.Nation, mob.ReservistsByLevel, mob.FrontShare, mob.ReserveBrigade,
                mob.SkilledReservists.Where(r => r.Pool == pool).ToList(), mob.RecruitmentLaw);
            Military.CallUp(ctx, Issuer, single, 0, call: !exempt);
        }
        w.Politics.ExemptMask.Set(Issuer, newMask);
        return OrderOutcome.Ok;
    }

    protected override void HashFields(StateHasher h) => h.Add(pool).Add(exempt);
}

/// <summary>Order the player's brigades at the front to attack for a few days. Fighting with deaths is an escalation.</summary>
public sealed class FrontAttackOrder(int issuer) : Order(issuer)
{
    public override string Kind => "military.front_attack";

    public override OrderOutcome Apply(TickContext ctx)
    {
        var w = ctx.World;
        bool any = false;
        for (int b = 0; b < w.Brigades.Count; b++)
            if (w.Brigades.Nation[b] == Issuer && w.Brigades.Strength[b] > Fixed.Zero) { w.Brigades.Posture.Set(b, (int)Posture.Attack); any = true; }
        if (!any) return OrderOutcome.Refused("No brigades at the front.");
        w.Front.ActiveUntil.Set(Issuer, ctx.Day + ctx.Balance.Military.ActiveDays);
        int enemy = Military.Enemy(w, Issuer);
        Escalation.Record(ctx, Issuer, enemy, "border_clash_deaths", $"{w.Nations.Adjectives[Issuer]} forces attack at Veyl.");
        return OrderOutcome.Ok;
    }

    protected override void HashFields(StateHasher h) { }
}
