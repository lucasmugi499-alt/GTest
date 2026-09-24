using Cascade.Sim.Core;

namespace Cascade.Sim.Content;

/// <summary>
/// Every tunable number in the game, read from content/balance.yaml. Code never holds a tunable literal;
/// it reads it from here. Sections grow milestone by milestone.
/// </summary>
public sealed record Balance(
    SimBalance Sim,
    EconomyBalance Economy,
    FabBalance Fab,
    GridBalance Grid,
    CountermeasureBalance Countermeasures,
    SocietyBalance Society,
    InformationBalance Information,
    CyberBalance Cyber,
    MilitaryBalance Military,
    MobilizationBalance Mobilization,
    EscalationBalance Escalation,
    MarketsBalance Markets,
    NarrativeBalance Narrative,
    ChronicleBalance Chronicle)
{
    public static Balance Read(ContentNode root) => new(
        SimBalance.Read(root.Child("sim")),
        EconomyBalance.Read(root.Child("economy")),
        FabBalance.Read(root.Child("fab")),
        GridBalance.Read(root.Child("grid")),
        CountermeasureBalance.Read(root.Child("countermeasures")),
        SocietyBalance.Read(root.Child("society")),
        InformationBalance.Read(root.Child("information")),
        CyberBalance.Read(root.Child("cyber")),
        MilitaryBalance.Read(root.Child("military")),
        MobilizationBalance.Read(root.Child("mobilization")),
        EscalationBalance.Read(root.Child("escalation")),
        MarketsBalance.Read(root.Child("markets")),
        NarrativeBalance.Read(root.Child("narrative")),
        ChronicleBalance.Read(root.Child("chronicle")));
}

public sealed record SimBalance(CrisisBalance Crisis, int HashCheckIntervalDays)
{
    public static SimBalance Read(ContentNode n) => new(
        CrisisBalance.Read(n.Child("crisis")),
        n.Child("state_hash").Int("check_interval_days"));
}

/// <summary>Spec: Crisis sub-ticks.</summary>
public sealed record CrisisBalance(int ClearAfterStableHours)
{
    public static CrisisBalance Read(ContentNode n) => new(n.Int("clear_after_stable_hours"));
}

/// <summary>Spec Shortage allocation: four tiers, filled in this order.</summary>
public enum PriorityTier : byte
{
    Critical = 0,
    High = 1,
    Normal = 2,
    Low = 3,
}

public static class PriorityTiers
{
    public const int Count = 4;

    public static PriorityTier Parse(string s, string where) => s switch
    {
        "critical" => PriorityTier.Critical,
        "high" => PriorityTier.High,
        "normal" => PriorityTier.Normal,
        "low" => PriorityTier.Low,
        _ => throw new ContentException($"{where}: expected critical, high, normal or low, got '{s}'"),
    };
}

public sealed record EconomyBalance(
    IReadOnlyDictionary<string, PriorityTier> PriorityByKind,
    int MilitaryHighFromMobilization,
    string MilitaryKind,
    Fixed EfficiencyGrowthPerDay,
    Fixed EfficiencyMax,
    Fixed EfficiencyMaxDualUse,
    Fixed EfficiencyMin,
    Fixed RetoolSameLine,
    Fixed RetoolNewLine,
    Fixed DoctrineBaseDays,
    Fixed DoctrineSpanDays,
    Fixed JitEfficiencyBonus,
    int BurnHalfLifeDays,
    int TransitWindowDays,
    Fixed CriticalCoverDays,
    int ImportReplenishDays)
{
    public static EconomyBalance Read(ContentNode n)
    {
        var p = n.Child("priority");
        var tiers = p.Keys().ToDictionary(k => k, k => PriorityTiers.Parse(p.Str(k), $"{p.Path}.{k}"), StringComparer.Ordinal);
        var e = n.Child("efficiency");
        var d = n.Child("doctrine");
        var doc = n.Child("days_of_cover");
        return new EconomyBalance(
            tiers,
            n.Int("military_high_from_mobilization"),
            n.Str("military_kind"),
            e.Fixed("growth_per_day"), e.Fixed("max"), e.Fixed("max_dual_use"), e.Fixed("min"),
            e.Fixed("retool_same_line"), e.Fixed("retool_new_line"),
            d.Fixed("base_days"), d.Fixed("span_days"), d.Fixed("jit_efficiency_bonus"),
            doc.Int("burn_half_life_days"), doc.Int("transit_window_days"), doc.Fixed("critical_days"),
            n.Child("imports").Int("replenish_days"));
    }

    public PriorityTier TierOf(string kind) =>
        PriorityByKind.TryGetValue(kind, out var t) ? t : throw new ContentException($"No default priority for kind '{kind}' in balance.yaml economy.priority.");
}

public sealed record FabBalance(
    Fixed YieldStart,
    Fixed YieldMaxLegacy,
    Fixed YieldMaxLeading,
    int ExperienceScaleDays,
    Fixed EngineerLossFactor,
    int WipCycleDays,
    int RampDays,
    string EngineerPool)
{
    public static FabBalance Read(ContentNode n) => new(
        n.Fixed("yield_start"), n.Fixed("yield_max_legacy"), n.Fixed("yield_max_leading"),
        n.Int("experience_scale_days"), n.Fixed("engineer_loss_factor"), n.Int("wip_cycle_days"), n.Int("ramp_days"),
        n.Str("engineer_pool"));
}

public sealed record GridBalance(
    Fine CrisisBelowRatio,
    int SpareRepairDays,
    int MobileUnitDays,
    Fine MobileUnitCapacity,
    int NewTransformerDaysMin,
    int NewTransformerDaysMax,
    string RepairCrewPool,
    int LinemenPerRepair,
    Fine BlackStartRestorePerDay,
    Fine CollapseLoadShare,
    string BackupFuelGood,
    IReadOnlyDictionary<string, Fixed> BackupTankHours)
{
    public static GridBalance Read(ContentNode n)
    {
        var tanks = n.Child("backup_tank_hours");
        return new GridBalance(
            n.Fine("crisis_below_ratio"), n.Int("spare_repair_days"), n.Int("mobile_unit_days"), n.Fine("mobile_unit_capacity"),
            n.Int("new_transformer_days_min"), n.Int("new_transformer_days_max"), n.Str("repair_crew_pool"), n.Int("linemen_per_repair"),
            n.Fine("black_start_restore_per_day"), n.Fine("collapse_load_share"), n.Str("backup_fuel_good"),
            tanks.Keys().ToDictionary(k => k, tanks.Fixed, StringComparer.Ordinal));
    }
}

public sealed record CountermeasureBalance(
    Fixed DecayPerWeek,
    Fixed Adaptation,
    Fixed CapDecayShare,
    Fixed FirmwareGain,
    int FirmwareDays,
    Fixed RevisionCap,
    int RevisionDays)
{
    public static CountermeasureBalance Read(ContentNode n) => new(
        n.Fixed("decay_per_week"), n.Fixed("adaptation"), n.Fixed("cap_decay_share"),
        n.Fixed("firmware_gain"), n.Int("firmware_days"), n.Fixed("revision_cap"), n.Int("revision_days"));
}
