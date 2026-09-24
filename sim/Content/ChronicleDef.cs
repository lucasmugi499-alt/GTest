using Cascade.Sim.Core;

namespace Cascade.Sim.Content;

public sealed record HistorianDef(string Id, string Name, string School, IReadOnlyDictionary<string, Fixed> Weights, string High, string Mid, string Low);

public sealed record ChronicleDef(string ChipsGood, string DronesGood, string MagnetsGood, string DroneDesign, IReadOnlyList<HistorianDef> Historians)
{
    public static readonly string[] Axes = ["survival", "prosperity", "liberty", "sovereignty", "humanity"];

    public static ChronicleDef Read(ContentNode n)
    {
        var hs = n.List("historians").Select(x =>
        {
            var w = x.Child("weights");
            var weights = Axes.ToDictionary(a => a, w.Fixed, StringComparer.Ordinal);
            if (weights.Values.Aggregate(Fixed.Zero, (a, b) => a + b) != Fixed.One)
                throw new ContentException($"{w.Path}: weights must sum to 1.");
            return new HistorianDef(x.Str("id"), x.Str("name"), x.Str("school"), weights, x.Str("high"), x.Str("mid"), x.Str("low"));
        }).ToList();
        return new ChronicleDef(n.Str("chips_good"), n.Str("drones_good"), n.Str("magnets_good"), n.Str("drone_design"), hs);
    }
}

public sealed record ChronicleBalance(Fixed SurvivalPerRung, Fixed SurvivalWarPenalty, int WarRung, Fixed LibertyPerBacksliding,
    Fixed LibertyPerCivilPrecedentUse, Fixed LibertyEmergencyInForce, Fixed SovereigntyCoverDays, Fixed HumanityBlackoutWeight,
    Fixed HumanityPerDeath, Fixed BandHigh, Fixed BandLow)
{
    public static ChronicleBalance Read(ContentNode n) => new(
        n.Fixed("survival_per_rung"), n.Fixed("survival_war_penalty"), n.Int("war_rung"), n.Fixed("liberty_per_backsliding"),
        n.Fixed("liberty_per_civil_precedent_use"), n.Fixed("liberty_emergency_in_force"), n.Fixed("sovereignty_cover_days"),
        n.Fixed("humanity_blackout_weight"), n.Fixed("humanity_per_death"), n.Fixed("band_high"), n.Fixed("band_low"));
}
