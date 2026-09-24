namespace Cascade.Sim.Content;

using Cascade.Sim.Core;

public sealed record DirectorPersonality(Fixed TargetTension, int MajorEveryDays, Fine BlackSwanPerMonth, Fixed Temperature);

public sealed record NarrativeBalance(
    Fixed MemoryGraveDays, Fixed MemoryMinorDays, Fixed MemoryGraveValence,
    Fixed CastingFitRole, Fixed CastingFitPortfolio, Fixed CastingFitProvince, Fixed CastingFitFaction,
    Fixed CastingHistory, Fixed CastingDrama, Fixed CastingRecent, Fixed CastingRecentPerAppearance, int CastingRecentWindowDays,
    Fixed HistoryPerMemory,
    Fixed TensionCrisis, Fixed TensionApproval, Fixed TensionRung, Fixed TensionArcs, Fixed ArcIntensityPerBeat, int ArcIntensityWindowDays,
    string Director, IReadOnlyDictionary<string, DirectorPersonality> Directors,
    Fixed UtilityTension, Fixed UtilityPacing, Fixed UtilityDebt, Fixed UtilityRepeat, Fixed UtilityBase, Fixed UtilityMin,
    int RepeatWindowDays, int TopChoices, int MajorsPerWeek, int MinorsPerWeek, int CrisisMajorHours,
    Fixed StoryletShareCap, Fixed StoryletEvenShareMultiple, int StoryletCapLibrarySize)
{
    public DirectorPersonality Personality => Directors.TryGetValue(Director, out var p) ? p
        : throw new ContentException($"Unknown director '{Director}' (balance.yaml narrative.directors).");

    public static NarrativeBalance Read(ContentNode n)
    {
        var d = n.Child("directors");
        var dirs = d.Keys().ToDictionary(k => k, k =>
        {
            var x = d.Child(k);
            return new DirectorPersonality(x.Fixed("target_tension"), x.Int("major_every_days"), x.Fine("black_swan_per_month"), x.Fixed("temperature"));
        }, StringComparer.Ordinal);
        var b = new NarrativeBalance(
            n.Fixed("memory_grave_days"), n.Fixed("memory_minor_days"), n.Fixed("memory_grave_valence"),
            n.Fixed("casting_fit_role"), n.Fixed("casting_fit_portfolio"), n.Fixed("casting_fit_province"), n.Fixed("casting_fit_faction"),
            n.Fixed("casting_history"), n.Fixed("casting_drama"), n.Fixed("casting_recent"), n.Fixed("casting_recent_per_appearance"),
            n.Int("casting_recent_window_days"), n.Fixed("history_per_memory"),
            n.Fixed("tension_crisis"), n.Fixed("tension_approval"), n.Fixed("tension_rung"), n.Fixed("tension_arcs"),
            n.Fixed("arc_intensity_per_beat"), n.Int("arc_intensity_window_days"),
            n.Str("director"), dirs,
            n.Fixed("utility_tension"), n.Fixed("utility_pacing"), n.Fixed("utility_debt"), n.Fixed("utility_repeat"),
            n.Fixed("utility_base"), n.Fixed("utility_min"),
            n.Int("repeat_window_days"), n.Int("top_choices"), n.Int("majors_per_week"), n.Int("minors_per_week"), n.Int("crisis_major_hours"),
            n.Fixed("storylet_share_cap"), n.Fixed("storylet_even_share_multiple"), n.Int("storylet_cap_library_size"));
        _ = b.Personality;
        return b;
    }
}
