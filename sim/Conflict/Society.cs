using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Economy;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Conflict;

/// <summary>
/// Phase 9 (spec Needs and Satisfaction; Approval; War support). Daily for the player's segments:
/// the seven needs from the world's state, smoothed (half-life 30 days, 7 for Power and Safety), weighted into
/// Satisfaction by livelihood; Align and Trust after narrative belief; then national Approval and war support.
/// </summary>
public sealed class SocietyPhase(Balance balance) : IPhase
{
    private readonly Fine _slow = EconomyRules.EmaAlpha(balance.Society.SmoothingDays);
    private readonly Fine _fast = EconomyRules.EmaAlpha(balance.Society.FastSmoothingDays);

    public PhaseId Id => PhaseId.Society;

    public void RunDaily(TickContext ctx) => Society.Update(ctx.World, ctx.Balance, ctx.Content, _slow, _fast, first: false);
}

public static class Society
{
    public static void Update(SimWorld w, Balance b, ContentSet c, Fine slow, Fine fast, bool first)
    {
        var so = b.Society;
        var seg = w.Segments;
        int player = w.Nations.Player;
        var pol = w.Politics;
        var nar = w.Narratives;

        var inflation = Inflation(w, b, player);
        pol.Inflation.Set(player, inflation);
        var pricesNeed = Clamp100(so.PricesBase + so.PricesPerPoint * (so.WageGrowth - inflation));
        bool curfew = PowerActive(w, c, player, "curfew", out var curfewDef);

        Fixed popSat = Fixed.Zero, popAlign = Fixed.Zero, popTrust = Fixed.Zero;
        long popTotal = 0;
        for (int s = 0; s < seg.Count; s++)
        {
            int p = seg.Province[s];
            var needs = new Fixed[Needs.Count];
            needs[(int)Need.Power] = WeightedPower(w, seg.Homes[s]) * 100;
            needs[(int)Need.Prices] = pricesNeed;

            var unemployment = seg.Defs[s].Unemployment + IdleShare(w, b, p) * 100;
            seg.Unemployment.Set(s, unemployment);
            needs[(int)Need.Jobs] = Clamp100(Fixed.Hundred - so.JobsPerUnemploymentPoint * unemployment);

            // Incidents decay back toward the peacetime baseline over about a month (D-034).
            var incidents = seg.IncidentsMonth.Pending(s);
            incidents += (so.BaselineIncidentsPer100k - incidents) / 30;
            seg.IncidentsMonth.Set(s, incidents);
            needs[(int)Need.Safety] = Clamp100(Fixed.Hundred - so.SafetyPerIncident * incidents + (curfew ? curfewDef!.SafetyBonus : Fixed.Zero));

            needs[(int)Need.Connectivity] = w.Provinces.InternetShutdown[p] ? Fixed.Zero : Availability(w, p, so.ConnectivityKind, Fine.One).ToFixed() * 100;
            var water = Availability(w, p, so.WaterKind, Fine.One);
            var health = Availability(w, p, so.HealthKind, water);
            var transit = so.ServicesTransit.Times(WeightedPower(w, seg.Homes[s]).ToFine());
            // Services = 100 × average availability of water, health care and transit.
            needs[(int)Need.Services] = (water.ToFixed() + health.ToFixed() + transit) * 100 / 3;
            needs[(int)Need.Dignity] = Clamp100(so.DignityBase + so.DignityAxisFactor * (pol.AxisInformation[player] - Fixed.FromInt(50))
                + seg.Defs[s].Identity - pol.DignityPenalty[player] - (curfew ? curfewDef!.DignityPenalty : Fixed.Zero));

            var sat = Fixed.Zero;
            for (int k = 0; k < Needs.Count; k++)
            {
                int at = seg.At(s, (Need)k);
                seg.Need.Set(at, needs[k]);
                var alpha = k is (int)Need.Power or (int)Need.Safety ? fast : slow;
                var smoothed = first ? needs[k] : seg.NeedSmoothed[at] + (needs[k] - seg.NeedSmoothed[at]).Times(alpha);
                seg.NeedSmoothed.Set(at, smoothed);
                sat += seg.Weights[s][k] * smoothed;
            }
            seg.Satisfaction.Set(s, sat);

            // Narrative belief pulls Align and Trust down; shocks and trust drift back over time.
            var alignLoss = Fixed.Zero;
            var trustLoss = Fixed.Zero;
            for (int n = 0; n < nar.Count; n++)
            {
                var belief = nar.B.Pending(nar.At(n, s));
                alignLoss += nar.Defs[n].AlignHit.Times(belief);
                trustLoss += nar.Defs[n].TrustHit.Times(belief);
            }
            var shock = seg.AlignShock.Pending(s);
            seg.AlignShock.Set(s, shock - shock.Times(so.TrustRecoveryPerDay));
            var align = Clamp100(seg.AlignBase[s] + shock - alignLoss);
            seg.Align.Set(s, align);
            var trustBase = seg.TrustBase.Pending(s);
            trustBase += (seg.Defs[s].Trust - trustBase).Times(so.TrustRecoveryPerDay);
            seg.TrustBase.Set(s, trustBase);
            var trust = Clamp100(trustBase - trustLoss);
            seg.Trust.Set(s, trust);

            popSat += sat * seg.Population[s];
            popAlign += align * seg.Population[s];
            popTrust += trust * seg.Population[s];
            popTotal += seg.Population[s];
        }

        // Appr = Σ pop (0.6 Sat + 0.4 Align) ÷ Σ pop
        var approval = (so.ApprovalSatisfactionWeight * popSat + so.ApprovalAlignWeight * popAlign) / popTotal;
        pol.Approval.Set(player, approval);
        pol.Trust.Set(player, popTrust / popTotal);

        // WS = clamp(0.5 L + 0.5 Appr + Rally − X, 0, 100)
        var ws = Clamp100((pol.Legitimacy.Pending(player) + approval) / 2 + pol.Rally.Pending(player) - pol.WarExhaustion.Pending(player));
        pol.WarSupport.Set(player, ws);

        // Manpower pool M = N_pop × ε_law × (0.5 + WS/200), less exempted reservists.
        var mob = c.Scenario.Conflict.Mobilization.FirstOrDefault(m => m.Nation == w.Nations.Keys[player]);
        if (mob is not null && b.Mobilization.RecruitmentLaw.TryGetValue(mob.RecruitmentLaw, out var eps))
        {
            var pool = Fixed.FromInt(popTotal).Times(eps) * (Fixed.Ratio(1, 2) + ws / 200);
            pol.ManpowerPool.Set(player, pool);
        }

        UpdateLeverage(w);
    }

    /// <summary>π = m − g + 0.3 Δp_imp + 0.5 s_short (m − g fixed in the slice, D-034).</summary>
    public static Fixed Inflation(SimWorld w, Balance b, int nation)
    {
        var so = b.Society;
        int goods = 0, critical = 0;
        for (int g = 0; g < w.Stocks.Goods; g++)
        {
            int at = w.Stocks.AtNation(nation, g);
            if (w.Stocks.DaysOfCover[at] < Fixed.Zero) continue;
            goods++;
            if (w.Stocks.Shortage[at] == 2) critical++;
        }
        var share = goods == 0 ? Fixed.Zero : Fixed.Ratio(critical * 100, goods);
        w.Politics.ShortageSharePct.Set(nation, share);
        var importPrices = so.InsurancePassThrough * (w.Politics.InsuranceMultiplier[nation] - Fixed.One);
        return so.BaseInflation + so.ShortageWeight * share + so.ImportPriceWeight * importPrices;
    }

    private static Fixed Clamp100(Fixed x) => Fixed.Clamp(x, Fixed.Zero, Fixed.Hundred);

    private static Fixed WeightedPower(SimWorld w, int[] homes)
    {
        long pop = 0;
        var sum = Fixed.Zero;
        foreach (int l in homes)
        {
            pop += w.Loads.Population[l];
            sum += Fixed.FromInt(w.Loads.Population[l]).Times(w.Loads.PowerRatio[l]);
        }
        return pop == 0 ? Fixed.One : sum / pop;
    }

    /// <summary>Average service availability of a kind of load in a province (fallback if the province has none).</summary>
    private static Fine Availability(SimWorld w, int province, string kind, Fine fallback)
    {
        var sum = Fine.Zero;
        int n = 0;
        for (int l = 0; l < w.Loads.Count; l++)
            if (w.Loads.Province[l] == province && w.Loads.Kind[l] == kind) { sum += w.Loads.ServiceAvailability[l]; n++; }
        return n == 0 ? fallback : sum / n;
    }

    /// <summary>Share of the province's labour force idled by facilities running below capacity (D-034).</summary>
    private static Fixed IdleShare(SimWorld w, Balance b, int province)
    {
        var idle = Fixed.Zero;
        var f = w.Facilities;
        for (int i = 0; i < f.Count; i++)
        {
            if (f.Province[i] != province) continue;
            var nominal = EconomyRules.EffectiveCapacity(w, b, i) * EconomyRules.EffectiveEfficiency(b, f.Efficiency[i], w.Nations.Doctrine[w.Provinces.Owner[province]]);
            if (nominal <= Fixed.Zero) continue;
            var use = Fixed.Min(Fixed.One, f.RunToday[i] / nominal);
            foreach (var (_, people) in f.LabourRequired[i]) idle += people * (Fixed.One - use);
        }
        long pop = 0;
        for (int s = 0; s < w.Segments.Count; s++) if (w.Segments.Province[s] == province) pop += w.Segments.Population[s];
        var force = Fixed.FromInt(pop) * b.Society.LabourForceShare;
        return force <= Fixed.Zero ? Fixed.Zero : idle / force;
    }

    /// <summary>Leverage = 100 × members' population share × Mobilization ÷ 100, plus institutions (spec Factions).</summary>
    public static void UpdateLeverage(SimWorld w)
    {
        long total = w.Segments.Population.Sum();
        for (int f = 0; f < w.Factions.Count; f++)
        {
            var members = Fixed.Zero;
            var mobilization = Fixed.Zero;
            foreach (var (s, share) in w.Factions.Members[f])
            {
                var people = Fixed.FromInt(w.Segments.Population[s]).Times(share);
                members += people;
                mobilization += people * w.Segments.Mobilization[s];
            }
            var popShare = members / total;
            var avgMob = members > Fixed.Zero ? mobilization / members : Fixed.Zero;
            w.Factions.Leverage.Set(f, popShare * avgMob + w.Factions.Defs[f].Institutions);
        }
    }

    public static bool PowerActive(SimWorld w, ContentSet c, int nation, string power, out EmergencyPowerDef? def)
    {
        var powers = c.Scenario.Society.EmergencyPowers;
        for (int i = 0; i < powers.Count; i++)
            if (powers[i].Id == power)
            {
                def = powers[i];
                return (w.Politics.PowersMask[nation] & (1 << i)) != 0;
            }
        def = null;
        return false;
    }

    /// <summary>
    /// Spec Precedents: the n-th use costs base × 0.6^(n−1). Applies the approval hit and faction reaction, Backsliding
    /// +5 on first use, and normalization at the 4th use (civil-liberties precedents then cost Dignity 3, permanently).
    /// Returns the Political Capital the act costs.
    /// </summary>
    public static Fixed UsePrecedent(TickContext ctx, int nation, string precedent)
    {
        var w = ctx.World;
        var so = ctx.Balance.Society;
        int type = ctx.Content.Scenario.Society.Precedent(precedent);
        var def = w.Precedents.Defs[type];
        int at = w.Precedents.At(nation, type);
        int uses = w.Precedents.Uses.Pending(at) + 1;
        w.Precedents.Uses.Set(at, uses);
        w.Precedents.LastUse.Set(at, ctx.Day);

        var factor = Fixed.One;
        for (int i = 1; i < uses; i++) factor *= so.PrecedentRepeatFactor;

        if (nation == w.Nations.Player)
        {
            for (int s = 0; s < w.Segments.Count; s++)
                w.Segments.AlignShock.Set(s, w.Segments.AlignShock.Pending(s) - def.ApprovalHit * factor);
            if (def.CivilLiberties && w.Factions.TryIdOf(so.CivilLibertiesFaction, out int cl))
                w.Factions.Standing.Set(cl, w.Factions.Standing.Pending(cl) - so.PrecedentFactionHit * factor);
        }
        if (uses == 1)
            w.Politics.Backsliding.Set(nation, w.Politics.Backsliding.Pending(nation) + so.BackslidingFirstPrecedent);
        if (uses == so.PrecedentNormalizedAt && def.CivilLiberties)
            w.Politics.DignityPenalty.Set(nation, w.Politics.DignityPenalty.Pending(nation) + so.DignityNormalizedPenalty);

        w.Log.Add(ctx.Day, ctx.Hour, "precedent",
            uses == 1 ? $"First time: {precedent.Replace('_', ' ')}." : uses >= so.PrecedentNormalizedAt
                ? $"{char.ToUpperInvariant(precedent[0])}{precedent[1..].Replace('_', ' ')} again: it no longer surprises anyone."
                : $"{char.ToUpperInvariant(precedent[0])}{precedent[1..].Replace('_', ' ')} again (use {uses}).",
            w.Nations.Keys[nation], precedent, Fixed.FromInt(uses));
        return def.PoliticalCapital * factor;
    }

    public static bool TrySpend(TickContext ctx, int nation, Fixed amount)
    {
        var pc = ctx.World.Politics.PoliticalCapital;
        if (pc.Pending(nation) < amount) return false;
        pc.Set(nation, pc.Pending(nation) - amount);
        return true;
    }

    public static Fixed PrecedentCostPreview(TickContext ctx, int nation, string precedent)
    {
        int type = ctx.Content.Scenario.Society.Precedent(precedent);
        int uses = ctx.World.Precedents.Uses.Pending(ctx.World.Precedents.At(nation, type));
        var factor = Fixed.One;
        for (int i = 0; i < uses; i++) factor *= ctx.Balance.Society.PrecedentRepeatFactor;
        return ctx.World.Precedents.Defs[type].PoliticalCapital * factor;
    }
}

/// <summary>Weekly (spec): PC grows by 2 + 0.1 × (Approval − 50) plus the rally effect, up to 300. Rally decays 1 a week.</summary>
public sealed class PoliticalCapitalSystem : IPeriodicSystem
{
    public SystemId Id => SystemId.WeeklyPoliticalCapital;

    public void Run(TickContext ctx)
    {
        var so = ctx.Balance.Society;
        var p = ctx.World.Politics;
        int n = ctx.World.Nations.Player;
        var gain = so.PcWeeklyBase + so.PcPerApprovalPoint * (p.Approval[n] - Fixed.FromInt(50)) + so.PcPerRallyPoint * p.Rally[n];
        p.PoliticalCapital.Set(n, Fixed.Clamp(p.PoliticalCapital.Pending(n) + gain, Fixed.Zero, so.PcCap));
        for (int i = 0; i < ctx.World.Nations.Count; i++)
            p.Rally.Set(i, Fixed.Max(Fixed.Zero, p.Rally.Pending(i) - so.RallyDecayPerWeek));
    }
}

/// <summary>
/// Weekly (spec Factions): approval moves toward agenda alignment plus standing (D-035). Angry, strong factions act:
/// probability 0.1 × Leverage ÷ 50 when approval is under 30 and Leverage over 20.
/// </summary>
public sealed class FactionSystem : IPeriodicSystem
{
    public SystemId Id => SystemId.WeeklyFactions;

    public void Run(TickContext ctx)
    {
        var w = ctx.World;
        var so = ctx.Balance.Society;
        var fac = w.Factions;
        int player = w.Nations.Player;
        int rung = Escalation.HighestRung(w, ctx.Balance, player);
        int civilPrecedents = 0;
        for (int t = 0; t < w.Precedents.Types; t++)
            if (w.Precedents.Defs[t].CivilLiberties && w.Precedents.Uses[w.Precedents.At(player, t)] > 0) civilPrecedents++;

        for (int f = 0; f < fac.Count; f++)
        {
            var agenda = fac.Defs[f].Agenda;
            var jobs = MemberNeed(w, f, Need.Jobs);
            var power = MemberNeed(w, f, Need.Power);
            var terms = new Dictionary<string, Fixed>
            {
                ["rung"] = so.AgendaRung * (rung - 2),
                ["mobilization"] = so.AgendaMobilization * w.Nations.MobilizationLevel[player],
                ["emergency"] = w.Politics.EmergencyActive[player] ? so.AgendaEmergency : Fixed.Zero,
                ["civil_precedents"] = so.AgendaCivilPrecedent * civilPrecedents,
                ["jobs"] = so.AgendaJobs * (jobs - Fixed.FromInt(50)),
                ["power"] = so.AgendaPower * (power - Fixed.FromInt(80)),
            };
            var alignment = Fixed.FromInt(50);
            foreach (var (term, weight) in agenda)
                alignment += weight * (terms.TryGetValue(term, out var v) ? v : throw new ContentException($"Faction '{fac.Defs[f].Id}': unknown agenda term '{term}'."));
            var standing = fac.Standing.Pending(f);
            var target = Fixed.Clamp(alignment + standing, Fixed.Zero, Fixed.Hundred);
            var approval = fac.Approval[f] + (target - fac.Approval[f]).Times(so.FactionSmoothing);
            fac.Approval.Set(f, approval);
            fac.Standing.Set(f, standing - standing.Times(so.FactionStandingDecay));

            var lev = fac.Leverage[f];
            if (approval >= so.FactionActionApprovalBelow || lev <= so.FactionActionLeverageAbove) continue;
            var rng = ctx.Rng(SystemId.WeeklyFactions, EntityRef.Of(EntityKind.Faction, f));
            if (!rng.Chance(so.FactionActionChance * (lev / Fixed.FromInt(50)).ToFine())) continue;
            var actions = fac.Defs[f].Actions;
            var action = actions[rng.NextInt(0, actions.Count)];
            Act(ctx, f, action);
        }
    }

    private static Fixed MemberNeed(SimWorld w, int f, Need need)
    {
        var sum = Fixed.Zero; var weight = Fixed.Zero;
        foreach (var (s, share) in w.Factions.Members[f])
        {
            var people = Fixed.FromInt(w.Segments.Population[s]).Times(share);
            sum += people * w.Segments.NeedSmoothed[w.Segments.At(s, need)];
            weight += people;
        }
        return weight > Fixed.Zero ? sum / weight : Fixed.FromInt(50);
    }

    private static void Act(TickContext ctx, int f, string action)
    {
        var w = ctx.World;
        var so = ctx.Balance.Society;
        var name = w.Factions.Defs[f].Name;
        w.Factions.Actions.Set(f, w.Factions.Actions.Pending(f) + 1);
        switch (action)
        {
            case "strike":
                foreach (var (s, _) in w.Factions.Members[f])
                    ctx.Events.Schedule(new StrikeEvent(ctx.Day, w.Segments.Province[s], so.StrikeLabourCut, start: true));
                foreach (var p in w.Factions.Members[f].Select(m => w.Segments.Province[m.Segment]).Distinct())
                    ctx.Events.Schedule(new StrikeEvent(ctx.Day + so.StrikeDays, p, so.StrikeLabourCut, start: false));
                break;
            case "protest":
                foreach (var (s, _) in w.Factions.Members[f])
                    w.Segments.IncidentsMonth.Set(s, w.Segments.IncidentsMonth.Pending(s) + so.ProtestIncidents);
                break;
        }
        w.Log.Add(ctx.Day, -1, "faction_action", action switch
        {
            "strike" => $"{name} call a strike.",
            "protest" => $"{name} take to the streets.",
            "leak" => $"{name} leak to the press.",
            "no_confidence" => $"{name} table a no-confidence motion.",
            _ => $"{name}: {action}.",
        }, w.Factions.Keys[f], action);
    }
}

/// <summary>A strike idles a share of the province's workers for a week (D-035). Idempotent per province and day.</summary>
public sealed class StrikeEvent(int day, int province, Fine share, bool start) : SimEvent(day)
{
    public override string Kind => "society.strike";

    public override void Apply(TickContext ctx)
    {
        var lab = ctx.World.Labour;
        for (int pool = 0; pool < lab.PoolCount; pool++)
        {
            int at = lab.Index(province, pool);
            var cut = lab.Base(province, pool).Times(share);
            var now = lab.Available.Pending(at);
            lab.Available.Set(at, start ? Fixed.Max(Fixed.Zero, now - cut) : Fixed.Min(lab.Base(province, pool), now + cut));
        }
    }

    protected override void HashFields(StateHasher h) => h.Add(province).Add(share).Add(start);
}

/// <summary>Weekly (spec): ΔX = 3 KIA ÷ (pop/100k) + 5 h_black + 2 r + e_mob − 3 V.</summary>
public sealed class WarExhaustionSystem : IPeriodicSystem
{
    public SystemId Id => SystemId.WeeklyWarExhaustion;

    public void Run(TickContext ctx)
    {
        var w = ctx.World;
        var so = ctx.Balance.Society;
        int n = w.Nations.Player;
        long pop = w.Segments.Population.Sum();
        long dark = 0;
        for (int l = 0; l < w.Loads.Count; l++)
            if (w.Provinces.Owner[w.Loads.Province[l]] == n)
                dark += (Fixed.FromInt(w.Loads.Population[l]).Times(Fine.One - w.Loads.PowerRatio[l])).RoundToInt();
        var kia = w.Politics.KiaWeek[n];
        var delta = so.ExhaustionKia * kia / (pop / 100_000)
                    + so.ExhaustionBlackout * Fixed.Ratio(dark, pop)
                    + so.ExhaustionRationing * Fixed.Zero      // rationing isn't modelled in the slice
                    + ctx.Balance.Mobilization.ExhaustionPerWeek[w.Nations.MobilizationLevel[n]]
                    - so.ExhaustionVictory * Fixed.Zero;       // no decisive victories in the slice
        w.Politics.WarExhaustion.Set(n, Fixed.Clamp(w.Politics.WarExhaustion[n] + delta, Fixed.Zero, Fixed.Hundred));
        for (int i = 0; i < w.Nations.Count; i++) w.Politics.KiaWeek.Set(i, Fixed.Zero);
    }
}

/// <summary>Weekly markets (D-009): war-risk insurance only. At rung 2+, premiums × (1 + Rung); from ×4 each line may skip.</summary>
public sealed class MarketsSystem : IPeriodicSystem
{
    public SystemId Id => SystemId.WeeklyMarkets;

    public void Run(TickContext ctx)
    {
        var w = ctx.World;
        var m = ctx.Balance.Markets;
        int n = w.Nations.Player;
        int rung = Escalation.HighestRung(w, ctx.Balance, n);
        var multiplier = rung >= m.InsuranceFromRung ? Fixed.FromInt(1 + rung) : Fixed.One;
        w.Politics.InsuranceMultiplier.Set(n, multiplier);

        // A state war-risk guarantee (a storylet decision) keeps every line calling.
        bool guaranteed = w.Flags.TryIdOf(m.GuaranteeFlag, out int flag) && w.Flags.Value[flag] > Fixed.Zero;
        int calling = 0;
        for (int line = 0; line < w.Shipping.Count; line++)
        {
            bool call = true;
            if (multiplier >= m.InsuranceSkipFromMultiplier && !guaranteed)
                call = !ctx.Rng(SystemId.WeeklyMarkets, EntityRef.Of(EntityKind.ShippingLine, line)).Chance(m.InsuranceSkipChance);
            w.Shipping.Calling.Set(line, call);
            if (call) calling++;
        }
        int lost = w.Politics.LinesCalling[n] - calling;
        w.Politics.LinesCalling.Set(n, calling);
        var share = Fine.Ratio(calling, Math.Max(1, w.Shipping.Count));
        for (int r = 0; r < w.Imports.Count; r++)
            if (w.Imports.Sea[r] && w.Provinces.Owner[w.Imports.To[r]] == n) w.Imports.CapacityFactor.Set(r, share);
        if (lost > 0)
            w.Log.Add(ctx.Day, -1, "markets", $"War-risk premiums at {multiplier.RoundToInt()}× peacetime: {lost} shipping line{(lost == 1 ? " stops" : "s stop")} calling at {w.Nations.Adjectives[n]} ports.");
    }
}

/// <summary>Monthly (spec Emergency Powers and drift): Backsliding and government axes drift.</summary>
public sealed class DriftSystem : IPeriodicSystem
{
    public SystemId Id => SystemId.MonthlyGovernmentDrift;

    public void Run(TickContext ctx)
    {
        var w = ctx.World;
        var so = ctx.Balance.Society;
        var p = w.Politics;
        for (int n = 0; n < w.Nations.Count; n++)
        {
            int active = System.Numerics.BitOperations.PopCount((uint)p.PowersMask[n]);
            var back = p.Backsliding.Pending(n);
            back = active > 0 ? back + so.BackslidingPerPowerMonth * active : Fixed.Max(Fixed.Zero, back - so.BackslidingRecoveryPerMonth);
            back = Fixed.Min(Fixed.Hundred, back);
            p.Backsliding.Set(n, back);
            var drift = so.DriftFactor * back;
            p.AxisInformation.Set(n, Fixed.Clamp(p.AxisInformation[n] - drift, Fixed.Zero, Fixed.Hundred));
            p.AxisCentralization.Set(n, Fixed.Clamp(p.AxisCentralization[n] + drift, Fixed.Zero, Fixed.Hundred));
            p.AxisRuleOfLaw.Set(n, Fixed.Clamp(p.AxisRuleOfLaw[n] - drift, Fixed.Zero, Fixed.Hundred));
        }
    }
}

/// <summary>Monthly (spec Red lines): the player's estimate of the rival's red line is RL + noise, σ = 20 × (1 − intel).</summary>
public sealed class RedLineEstimateSystem : IPeriodicSystem
{
    public SystemId Id => SystemId.MonthlyRedLineEstimate;

    public void Run(TickContext ctx) => Estimate(ctx.World, ctx.Balance, ctx.Seed, Rng.TickKey(ctx.Day, TickSlot.Monthly));

    public static void Estimate(SimWorld w, Balance b, ulong seed, ulong tick)
    {
        int player = w.Nations.Player;
        var sd = b.Escalation.RedLineEstimateSd.Times(Fine.One - w.Politics.IntelQuality[player]);
        for (int n = 0; n < w.Nations.Count; n++)
        {
            if (n == player) continue;
            var rng = new RngStream(seed, tick, (ulong)SystemId.MonthlyRedLineEstimate, EntityRef.Of(EntityKind.Nation, n));
            w.Politics.RedLineEstimate.Set(n, rng.NextNormal(w.Politics.RedLine[n], sd));
        }
    }
}
