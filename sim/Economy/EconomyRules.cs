using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.World;

namespace Cascade.Sim.Economy;

/// <summary>Small shared rules the economy and grid phases both use.</summary>
public static class EconomyRules
{
    /// <summary>
    /// The tier a consumer is served at: the player's override if any, else the default for its kind,
    /// with military raised to High from the mobilization level in balance (spec Shortage allocation).
    /// </summary>
    public static PriorityTier Tier(Balance b, string kind, int overrideTier, int mobilizationLevel)
    {
        if (overrideTier >= 0) return (PriorityTier)overrideTier;
        var t = b.Economy.TierOf(kind);
        if (kind == "military" && mobilizationLevel >= b.Economy.MilitaryHighFromMobilization && t > PriorityTier.High)
            t = PriorityTier.High;
        return t;
    }

    public static PriorityTier FacilityTier(SimWorld w, Balance b, int f) =>
        Tier(b, w.Facilities.Kind[f], w.Facilities.TierOverride[f], w.Nations.MobilizationLevel[OwnerOf(w, w.Facilities.Province[f])]);

    public static PriorityTier LoadTier(SimWorld w, Balance b, int l) =>
        Tier(b, w.Loads.Kind[l], w.Loads.TierOverride[l], w.Nations.MobilizationLevel[OwnerOf(w, w.Loads.Province[l])]);

    public static PriorityTier DemandTier(SimWorld w, Balance b, int d) =>
        Tier(b, w.Demand.Kind[d], w.Demand.TierOverride[d], w.Nations.MobilizationLevel[OwnerOf(w, w.Demand.Province[d])]);

    public static int OwnerOf(SimWorld w, int province) => w.Provinces.Owner[province];

    /// <summary>Target stock in days of burn: base + span × j (spec Days of Cover and doctrine).</summary>
    public static Fixed TargetDays(Balance b, Fine doctrine) =>
        b.Economy.DoctrineBaseDays + b.Economy.DoctrineSpanDays.Times(doctrine);

    /// <summary>
    /// Efficiency with the Just-in-Time bonus: E × (1 + 0.03 × (1 − j)), capped at 1 so a line never
    /// runs above capacity (D-031).
    /// </summary>
    public static Fixed EffectiveEfficiency(Balance b, Fixed e, Fine doctrine)
    {
        var bonus = Fixed.One + b.Economy.JitEfficiencyBonus.Times(Fine.One - doctrine);
        return Fixed.Min(Fixed.One, e * bonus);
    }

    /// <summary>Lowest available ÷ required over a facility's labour pools (spec Production: L).</summary>
    public static Fine LabourRatio(SimWorld w, int f)
    {
        var ratio = Fine.One;
        int p = w.Facilities.Province[f];
        foreach (var (pool, _) in w.Facilities.LabourRequired[f])
            ratio = Fine.Min(ratio, w.Labour.Ratio(p, pool));
        return ratio;
    }

    /// <summary>EMA weight per day for a half-life of h days: α = 1 − 2^(−1/h).</summary>
    public static Fine EmaAlpha(int halfLifeDays) =>
        Fine.One - FixedMath.Exp(-(Fine.Parse("0.69314718") / halfLifeDays));
}
