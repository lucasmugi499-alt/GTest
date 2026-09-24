using Cascade.Sim.Core;

namespace Cascade.Sim.Content;

/// <summary>The seven needs (spec Needs and Satisfaction), in a fixed order used by every per-need array.</summary>
public enum Need { Power = 0, Prices = 1, Jobs = 2, Safety = 3, Connectivity = 4, Services = 5, Dignity = 6 }

public static class Needs
{
    public const int Count = 7;
    public static readonly string[] Keys = ["power", "prices", "jobs", "safety", "connectivity", "services", "dignity"];
}

public sealed record SocietyBalance(
    IReadOnlyDictionary<string, Fixed[]> NeedWeights,
    int SmoothingDays, int FastSmoothingDays,
    Fixed ApprovalSatisfactionWeight, Fixed ApprovalAlignWeight,
    Fixed PricesBase, Fixed PricesPerPoint, Fixed WageGrowth, Fixed BaseInflation, Fixed ShortageWeight,
    Fixed ImportPriceWeight, Fixed InsurancePassThrough,
    Fixed JobsPerUnemploymentPoint, Fixed LabourForceShare,
    Fixed SafetyPerIncident, Fixed BaselineIncidentsPer100k,
    Fixed DignityBase, Fixed DignityAxisFactor, Fixed DignityNormalizedPenalty,
    Fixed ServicesTransit, string ConnectivityKind, string WaterKind, string HealthKind, string CivilLibertiesFaction, Fine TrustRecoveryPerDay,
    Fixed PcWeeklyBase, Fixed PcPerApprovalPoint, Fixed PcCap, Fixed PcPerRallyPoint,
    Fixed RallyOnAttack, Fixed RallyDecayPerWeek,
    Fine FactionSmoothing, Fine FactionStandingDecay,
    Fixed FactionActionApprovalBelow, Fixed FactionActionLeverageAbove, Fine FactionActionChance,
    Fine StrikeLabourCut, int StrikeDays, Fixed ProtestIncidents,
    Fixed AgendaRung, Fixed AgendaMobilization, Fixed AgendaEmergency, Fixed AgendaCivilPrecedent, Fixed AgendaJobs, Fixed AgendaPower,
    Fixed EmergencyEndCostPerMonth, Fixed EmergencyEndCostMax,
    Fixed BackslidingPerPowerMonth, Fixed BackslidingFirstPrecedent, Fixed BackslidingRecoveryPerMonth, Fixed DriftFactor,
    Fixed PrecedentRepeatFactor, int PrecedentNormalizedAt, Fixed PrecedentFactionHit, Fixed NationalizationLoyaltyHit,
    Fixed ExhaustionKia, Fixed ExhaustionBlackout, Fixed ExhaustionRationing, Fixed ExhaustionVictory,
    Fixed LegitimacyAnsweringAttack, Fixed ProtestBelowWarSupport, Fixed MobilizationBlockedBelowWarSupport)
{
    public static SocietyBalance Read(ContentNode n)
    {
        var w = n.Child("need_weights");
        var weights = new Dictionary<string, Fixed[]>(StringComparer.Ordinal);
        foreach (var livelihood in w.Keys())
        {
            var row = w.Child(livelihood);
            var arr = Needs.Keys.Select(row.Fixed).ToArray();
            var sum = arr.Aggregate(Fixed.Zero, (a, b) => a + b);
            if (sum != Fixed.One) throw new ContentException($"{row.Path}: need weights must sum to 1, got {sum}.");
            weights[livelihood] = arr;
        }
        var p = n.Child("prices");
        return new SocietyBalance(
            weights, n.Int("smoothing_days"), n.Int("fast_smoothing_days"),
            n.Fixed("approval_satisfaction_weight"), n.Fixed("approval_align_weight"),
            p.Fixed("base"), p.Fixed("per_point"), p.Fixed("wage_growth"), p.Fixed("base_inflation"), p.Fixed("shortage_weight"),
            p.Fixed("import_price_weight"), p.Fixed("insurance_pass_through"),
            n.Fixed("jobs_per_unemployment_point"), n.Fixed("labour_force_share"),
            n.Fixed("safety_per_incident"), n.Fixed("baseline_incidents_per_100k"),
            n.Fixed("dignity_base"), n.Fixed("dignity_axis_factor"), n.Fixed("dignity_normalized_penalty"),
            n.Fixed("services_transit"), n.Str("connectivity_kind"), n.Str("water_kind"), n.Str("health_kind"), n.Str("civil_liberties_faction"), n.Fine("trust_recovery_per_day"),
            n.Fixed("political_capital_weekly_base"), n.Fixed("political_capital_per_approval_point"), n.Fixed("political_capital_cap"),
            n.Fixed("political_capital_per_rally_point"),
            n.Fixed("rally_on_attack"), n.Fixed("rally_decay_per_week"),
            n.Fine("faction_smoothing"), n.Fine("faction_standing_decay"),
            n.Fixed("faction_action_approval_below"), n.Fixed("faction_action_leverage_above"), n.Fine("faction_action_chance"),
            n.Fine("strike_labour_cut"), n.Int("strike_days"), n.Fixed("protest_incidents"),
            n.Fixed("agenda_rung"), n.Fixed("agenda_mobilization"), n.Fixed("agenda_emergency"), n.Fixed("agenda_civil_precedent"),
            n.Fixed("agenda_jobs"), n.Fixed("agenda_power"),
            n.Fixed("emergency_end_cost_per_month"), n.Fixed("emergency_end_cost_max"),
            n.Fixed("backsliding_per_power_month"), n.Fixed("backsliding_first_precedent"), n.Fixed("backsliding_recovery_per_month"),
            n.Fixed("drift_factor"),
            n.Fixed("precedent_repeat_factor"), n.Int("precedent_normalized_at"), n.Fixed("precedent_faction_hit"), n.Fixed("nationalization_loyalty_hit"),
            n.Fixed("exhaustion_kia"), n.Fixed("exhaustion_blackout"), n.Fixed("exhaustion_rationing"), n.Fixed("exhaustion_victory"),
            n.Fixed("legitimacy_answering_attack"), n.Fixed("protest_below_war_support"), n.Fixed("mobilization_blocked_below_war_support"));
    }
}

public sealed record InformationBalance(
    Fine Beta, Fine Eta, Fine Gamma, Fine GammaEstablished, Fine EstablishedShare, Fixed TrustDivisor,
    Fixed CounterGammaMultiplier, int CounterDays, Fixed CounterMinTrust, Fine PrebunkingPerDay, Fixed PrebunkingCost,
    Fine TakedownWeight, Fixed TakedownComplianceCost, int RumorCrisisHours, int RumorHorizonHours)
{
    public static InformationBalance Read(ContentNode n) => new(
        n.Fine("beta"), n.Fine("eta"), n.Fine("gamma"), n.Fine("gamma_established"), n.Fine("established_share"), n.Fixed("trust_divisor"),
        n.Fixed("counter_gamma_multiplier"), n.Int("counter_days"), n.Fixed("counter_min_trust"), n.Fine("prebunking_per_day"), n.Fixed("prebunking_cost"),
        n.Fine("takedown_weight"), n.Fixed("takedown_compliance_cost"), n.Int("rumor_crisis_hours"), n.Int("rumor_horizon_hours"));
}

public sealed record CyberBalance(
    Fixed AccessSkillDivisor, Fine DetectionBase, Fixed DetectionScale, Fixed DefenceGainOnDetection,
    Fixed MinAccessDisrupt, Fixed MinAccessManipulate, Fixed MinAccessDestroy, Fixed DisruptionBaseHours,
    Fine AttributionDirectMax, Fine AttributionProxyMax, int AttributionDays, Fine BurnPatchChance, int BurnPatchDays, Fixed ForensicSweepCost)
{
    public static CyberBalance Read(ContentNode n) => new(
        n.Fixed("access_skill_divisor"), n.Fine("detection_base"), n.Fixed("detection_scale"), n.Fixed("defence_gain_on_detection"),
        n.Fixed("min_access_disrupt"), n.Fixed("min_access_manipulate"), n.Fixed("min_access_destroy"), n.Fixed("disruption_base_hours"),
        n.Fine("attribution_direct_max"), n.Fine("attribution_proxy_max"), n.Int("attribution_days"),
        n.Fine("burn_patch_chance"), n.Int("burn_patch_days"), n.Fixed("forensic_sweep_cost"));
}

public sealed record MilitaryBalance(
    Fine CombatK, Fixed QualityTraining, Fixed QualityEquipment, Fixed QualityCohesion, Fixed QualityExperience, Fixed QualityScale,
    Fixed ReconDrones, Fixed ReconSatellite, Fixed ReconSigint, Fixed ReconHumint, Fixed DroneCoverFullDensity,
    Fixed LockDroneDensity, Fine LockDetection, Fixed LockedLambda, Fixed AdvanceThreshold, Fixed VmaxMechanized, Fixed VmaxInfantry,
    Fixed KillZoneBase, Fixed KillZonePerDensity, Fixed KillZoneMax, Fine TruckLossPerDensity,
    Fixed CohesionLossFactor, Fixed CohesionRecovery, Fixed ExperiencePerCombatDay,
    Fine KilledShare, Fine WoundedReturnShare, int WoundedReturnDays, Fine MedicalMin, string MedicalKind,
    Fixed MoraleMin, Fixed MoraleMax, Fixed MoralePerWarSupport, Fixed SupplyCoverDays, Fixed DieselPer1000PerDay, int ActiveDays)
{
    public static MilitaryBalance Read(ContentNode n) => new(
        n.Fine("combat_k"), n.Fixed("quality_training"), n.Fixed("quality_equipment"), n.Fixed("quality_cohesion"), n.Fixed("quality_experience"),
        n.Fixed("quality_scale"),
        n.Fixed("recon_drones"), n.Fixed("recon_satellite"), n.Fixed("recon_sigint"), n.Fixed("recon_humint"), n.Fixed("drone_cover_full_density"),
        n.Fixed("lock_drone_density"), n.Fine("lock_detection"), n.Fixed("locked_lambda"), n.Fixed("advance_threshold"),
        n.Fixed("vmax_mechanized"), n.Fixed("vmax_infantry"),
        n.Fixed("kill_zone_base"), n.Fixed("kill_zone_per_density"), n.Fixed("kill_zone_max"), n.Fine("truck_loss_per_density"),
        n.Fixed("cohesion_loss_factor"), n.Fixed("cohesion_recovery"), n.Fixed("experience_per_combat_day"),
        n.Fine("killed_share"), n.Fine("wounded_return_share"), n.Int("wounded_return_days"), n.Fine("medical_min"), n.Str("medical_kind"),
        n.Fixed("morale_min"), n.Fixed("morale_max"), n.Fixed("morale_per_war_support"), n.Fixed("supply_cover_days"),
        n.Fixed("diesel_per_1000_per_day"), n.Int("active_days"));
}

public sealed record MobilizationBalance(
    int[] DaysToReach, Fixed[] MilitaryOutput, Fixed[] ExhaustionPerWeek, int StepDownDays,
    IReadOnlyDictionary<string, Fine> RecruitmentLaw)
{
    public static MobilizationBalance Read(ContentNode n)
    {
        var law = n.Child("recruitment_law");
        return new MobilizationBalance(
            n.IntList("days_to_reach"), n.FixedList("military_output"), n.FixedList("exhaustion_per_week"), n.Int("step_down_days"),
            law.Keys().ToDictionary(k => k, law.Fine, StringComparer.Ordinal));
    }
}

public sealed record EscalationAction(int Weight, int Floor);
public sealed record Personality(Fixed RedLine, Fixed Retaliation);

public sealed record EscalationBalance(
    int[] Rungs, Fixed DecayPerWeek, int DecayBlockedByWeight,
    IReadOnlyDictionary<string, EscalationAction> Actions,
    Fixed RivalPerception, Fixed RivalPerceptionCivilianHarm, Fixed PublicPerceptionVisible, Fixed PublicPerceptionHidden,
    Fixed WorldPerception, Fixed RedLineSd, Fixed RedLineDomesticShift, Fixed RedLineEstimateSd,
    int RetaliationDelayMin, int RetaliationDelayMax, IReadOnlyDictionary<string, Personality> Personalities)
{
    public static EscalationBalance Read(ContentNode n)
    {
        var a = n.Child("actions");
        var actions = a.Keys().ToDictionary(k => k, k => { var x = a.Child(k); return new EscalationAction(x.Int("weight"), x.Int("floor")); }, StringComparer.Ordinal);
        var p = n.Child("personalities");
        var pers = p.Keys().ToDictionary(k => k, k => { var x = p.Child(k); return new Personality(x.Fixed("red_line"), x.Fixed("retaliation")); }, StringComparer.Ordinal);
        return new EscalationBalance(
            n.IntList("rungs"), n.Fixed("decay_per_week"), n.Int("decay_blocked_by_weight"), actions,
            n.Fixed("rival_perception"), n.Fixed("rival_perception_civilian_harm"), n.Fixed("public_perception_visible"),
            n.Fixed("public_perception_hidden"), n.Fixed("world_perception"),
            n.Fixed("red_line_sd"), n.Fixed("red_line_domestic_shift"), n.Fixed("red_line_estimate_sd"),
            n.Int("retaliation_delay_min"), n.Int("retaliation_delay_max"), pers);
    }

    public EscalationAction Action(string id) =>
        Actions.TryGetValue(id, out var a) ? a : throw new ContentException($"Unknown escalation action '{id}' (balance.yaml escalation.actions).");

    /// <summary>Rung 1–7 for a meter value (D-003).</summary>
    public int RungOf(Fixed meter)
    {
        int rung = 1;
        for (int i = 1; i < Rungs.Length; i++) if (meter >= Fixed.FromInt(Rungs[i])) rung = i + 1;
        return rung;
    }
}

public sealed record MarketsBalance(int InsuranceFromRung, Fixed InsuranceSkipFromMultiplier, Fine InsuranceSkipChance, string GuaranteeFlag)
{
    public static MarketsBalance Read(ContentNode n) => new(n.Int("insurance_from_rung"), n.Fixed("insurance_skip_from_multiplier"), n.Fine("insurance_skip_chance"), n.Str("guarantee_flag"));
}
