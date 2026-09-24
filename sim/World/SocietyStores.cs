using Cascade.Sim.Content;
using Cascade.Sim.Core;

namespace Cascade.Sim.World;

/// <summary>Per-nation politics and war state (spec Politics; War, peace and conquest; Escalation).</summary>
public sealed class PoliticsStore : Store
{
    public Column<Fixed> PoliticalCapital { get; }
    public Column<Fixed> Approval { get; }
    /// <summary>Population-weighted effective Trust (the concept's "public trust" readout).</summary>
    public Column<Fixed> Trust { get; }
    public Column<Fixed> Legitimacy { get; }
    public Column<Fixed> Rally { get; }
    public Column<Fixed> WarSupport { get; }
    public Column<Fixed> WarExhaustion { get; }
    public Column<Fixed> Inflation { get; }
    public Column<bool> EmergencyActive { get; }
    public Column<int> EmergencySince { get; }
    /// <summary>Bit per emergency power (in content order) currently in force.</summary>
    public Column<int> PowersMask { get; }
    public Column<Fixed> Backsliding { get; }
    public Column<Fixed> AxisInformation { get; }
    public Column<Fixed> AxisCentralization { get; }
    public Column<Fixed> AxisRuleOfLaw { get; }
    public Column<Fixed> AxisMarket { get; }
    public Column<Fine> IntelQuality { get; }
    public Column<Fixed> CyberSkill { get; }
    /// <summary>Permanent Dignity loss from normalized civil-liberties precedents.</summary>
    public Column<Fixed> DignityPenalty { get; }
    public Column<Fixed> KiaWeek { get; }
    public Column<Fixed> KiaTotal { get; }
    public Column<int> MobilizationTarget { get; }
    /// <summary>Day the mobilization level next changes toward the target, or -1.</summary>
    public Column<int> MobilizationChangeDay { get; }
    /// <summary>Bit per labour pool exempted from call-up.</summary>
    public Column<int> ExemptMask { get; }
    public Column<Fixed> ManpowerPool { get; }
    public Column<Fixed> RedLine { get; }
    public Column<Fixed> RedLineEstimate { get; }
    /// <summary>True while the meter sits above this nation's red line after a crossing it has answered.</summary>
    public Column<bool> RedLineCrossed { get; }
    public Column<int> DroneOpsNearBorder { get; }
    public Column<bool> AttackedByRival { get; }
    public Column<Fixed> InsuranceMultiplier { get; }
    public Column<int> LinesCalling { get; }
    public Column<Fixed> ShortageSharePct { get; }
    /// <summary>Counts AI decisions, so each one draws its own random stream.</summary>
    public Column<int> AiDecisions { get; }

    public PoliticsStore(ScenarioDef s, NationStore nations) : base("politics", nations.Keys)
    {
        PoliticalCapital = Col<Fixed>("political_capital");
        Approval = Col<Fixed>("approval");
        Trust = Col<Fixed>("trust");
        Legitimacy = Col<Fixed>("legitimacy");
        Rally = Col<Fixed>("rally");
        WarSupport = Col<Fixed>("war_support");
        WarExhaustion = Col<Fixed>("war_exhaustion");
        Inflation = Col<Fixed>("inflation");
        EmergencyActive = Col<bool>("emergency_active");
        EmergencySince = Col<int>("emergency_since");
        PowersMask = Col<int>("powers_mask");
        Backsliding = Col<Fixed>("backsliding");
        AxisInformation = Col<Fixed>("axis_information");
        AxisCentralization = Col<Fixed>("axis_centralization");
        AxisRuleOfLaw = Col<Fixed>("axis_rule_of_law");
        AxisMarket = Col<Fixed>("axis_market");
        IntelQuality = Col<Fine>("intel_quality");
        CyberSkill = Col<Fixed>("cyber_skill");
        DignityPenalty = Col<Fixed>("dignity_penalty");
        KiaWeek = Col<Fixed>("kia_week");
        KiaTotal = Col<Fixed>("kia_total");
        MobilizationTarget = Col<int>("mobilization_target");
        MobilizationChangeDay = Col<int>("mobilization_change_day");
        ExemptMask = Col<int>("exempt_mask");
        ManpowerPool = Col<Fixed>("manpower_pool");
        RedLine = Col<Fixed>("red_line");
        RedLineEstimate = Col<Fixed>("red_line_estimate");
        RedLineCrossed = Col<bool>("red_line_crossed");
        DroneOpsNearBorder = Col<int>("drone_ops_near_border");
        AttackedByRival = Col<bool>("attacked_by_rival");
        InsuranceMultiplier = Col<Fixed>("insurance_multiplier");
        LinesCalling = Col<int>("lines_calling");
        ShortageSharePct = Col<Fixed>("shortage_share_pct");
        AiDecisions = Col<int>("ai_decisions");

        for (int i = 0; i < Count; i++)
        {
            var g = s.Society.Government.First(x => x.Nation == nations.Keys[i]);
            PoliticalCapital.Init(i, g.PoliticalCapital);
            Legitimacy.Init(i, g.Legitimacy);
            AxisInformation.Init(i, g.Information);
            AxisCentralization.Init(i, g.Centralization);
            AxisRuleOfLaw.Init(i, g.RuleOfLaw);
            AxisMarket.Init(i, g.Market);
            IntelQuality.Init(i, g.IntelQuality);
            CyberSkill.Init(i, g.CyberSkill);
            EmergencySince.Init(i, -1);
            MobilizationChangeDay.Init(i, -1);
            InsuranceMultiplier.Init(i, Fixed.One);
            LinesCalling.Init(i, s.Conflict.ShippingLines.Count);
            Approval.Init(i, Fixed.FromInt(50));
            WarSupport.Init(i, Fixed.FromInt(50));
            Trust.Init(i, Fixed.FromInt(50));
        }
    }
}

/// <summary>The player's population segments (spec Data model: Segment).</summary>
public sealed class SegmentStore : Store
{
    public IReadOnlyList<SegmentDef> Defs { get; }
    public int[] Province { get; }
    public long[] Population { get; }
    public int[][] Homes { get; }
    public Fixed[][] Weights { get; }

    /// <summary>Raw need scores today, [segment × 7 + need].</summary>
    public Column<Fixed> Need { get; }
    /// <summary>Smoothed need scores, [segment × 7 + need].</summary>
    public Column<Fixed> NeedSmoothed { get; }
    public Column<Fixed> Satisfaction { get; }
    /// <summary>Institutional trust before narrative effects; drifts back to its start.</summary>
    public Column<Fixed> TrustBase { get; }
    /// <summary>Trust after narrative belief (what the contagion model and readouts use).</summary>
    public Column<Fixed> Trust { get; }
    public Column<Fixed> AlignBase { get; }
    /// <summary>Temporary Align change from precedents and choices; fades back to zero.</summary>
    public Column<Fixed> AlignShock { get; }
    public Column<Fixed> Align { get; }
    public Column<Fixed> Mobilization { get; }
    public Column<Fixed> Unemployment { get; }
    /// <summary>Deaths and violent incidents per 100,000 over roughly the last month (decaying sum, D-034).</summary>
    public Column<Fixed> IncidentsMonth { get; }

    public SegmentStore(ScenarioDef s, LoadStore loads, ProvinceStore provinces, Balance b)
        : base("segment", s.Society.Segments.Select(x => x.Id).ToList())
    {
        Defs = s.Society.Segments;
        Province = Defs.Select(d => provinces.IdOf(d.Province)).ToArray();
        Population = Defs.Select(d => d.Population).ToArray();
        Homes = Defs.Select(d => d.Homes.Select(loads.IdOf).ToArray()).ToArray();
        Weights = Defs.Select(d => b.Society.NeedWeights.TryGetValue(d.Livelihood, out var w) ? w
            : throw new ContentException($"Segment '{d.Id}': no need weights for livelihood '{d.Livelihood}'.")).ToArray();

        Need = Col<Fixed>("need", Count * Needs.Count);
        NeedSmoothed = Col<Fixed>("need_smoothed", Count * Needs.Count);
        Satisfaction = Col<Fixed>("satisfaction");
        TrustBase = Col<Fixed>("trust_base");
        Trust = Col<Fixed>("trust");
        AlignBase = Col<Fixed>("align_base");
        AlignShock = Col<Fixed>("align_shock");
        Align = Col<Fixed>("align");
        Mobilization = Col<Fixed>("mobilization");
        Unemployment = Col<Fixed>("unemployment");
        IncidentsMonth = Col<Fixed>("incidents_month");
        for (int i = 0; i < Count; i++)
        {
            TrustBase.Init(i, Defs[i].Trust);
            Trust.Init(i, Defs[i].Trust);
            AlignBase.Init(i, Defs[i].Align);
            Align.Init(i, Defs[i].Align);
            Mobilization.Init(i, Defs[i].Mobilization);
            Unemployment.Init(i, Defs[i].Unemployment);
            IncidentsMonth.Init(i, b.Society.BaselineIncidentsPer100k);
        }
    }

    public int At(int segment, Need need) => segment * Needs.Count + (int)need;
}

public sealed class FactionStore : Store
{
    public IReadOnlyList<FactionDef> Defs { get; }
    public (int Segment, Fine Share)[][] Members { get; }
    public Column<Fixed> Approval { get; }
    /// <summary>Storylet-driven goodwill or anger, fading weekly (D-035).</summary>
    public Column<Fixed> Standing { get; }
    public Column<Fixed> Leverage { get; }
    public Column<int> Actions { get; }

    public FactionStore(ScenarioDef s, SegmentStore segments) : base("faction", s.Society.Factions.Select(f => f.Id).ToList())
    {
        Defs = s.Society.Factions;
        Members = Defs.Select(f => f.Members.Select(m => (segments.IdOf(m.Segment), m.Share)).ToArray()).ToArray();
        Approval = Col<Fixed>("approval");
        Standing = Col<Fixed>("standing");
        Leverage = Col<Fixed>("leverage");
        Actions = Col<int>("actions");
        for (int i = 0; i < Count; i++) Approval.Init(i, Defs[i].Approval);
    }
}

/// <summary>Precedents per nation (spec Precedents), [nation × types + type].</summary>
public sealed class PrecedentStore : IStateHashable, ICommittable
{
    public IReadOnlyList<PrecedentDef> Defs { get; }
    public int Types => Defs.Count;
    public Column<int> Uses { get; }
    public Column<int> LastUse { get; }

    public PrecedentStore(ScenarioDef s, int nations)
    {
        Defs = s.Society.Precedents;
        Uses = new("precedent.uses", nations * Types);
        LastUse = new("precedent.last_use", nations * Types);
        for (int i = 0; i < nations * Types; i++) LastUse.Init(i, -1);
    }

    public int At(int nation, int type) => nation * Types + type;
    public void Commit() { Uses.Commit(); LastUse.Commit(); }
    public void HashInto(StateHasher h) { h.Add(Uses); h.Add(LastUse); }
}

public sealed class CorporationStore : Store
{
    public IReadOnlyList<CorporationDef> Defs { get; }
    public Column<Fixed> Loyalty { get; }
    public Column<bool> Nationalized { get; }

    public CorporationStore(ScenarioDef s) : base("corporation", s.Society.Corporations.Select(c => c.Id).ToList())
    {
        Defs = s.Society.Corporations;
        Loyalty = Col<Fixed>("loyalty");
        Nationalized = Col<bool>("nationalized");
        for (int i = 0; i < Count; i++) Loyalty.Init(i, Defs[i].Loyalty);
    }
}

/// <summary>Narratives and their spread per segment (spec Narrative spread: S, E, B, R shares that sum to 1).</summary>
public sealed class NarrativeStore : Store
{
    public IReadOnlyList<NarrativeDef> Defs { get; }
    public int Segments { get; }
    /// <summary>Resonance R_s per [narrative × segments + segment].</summary>
    public Fine[] Resonance { get; }
    public int[] Origin { get; }

    public Column<bool> Active { get; }
    public Column<int> SeededDay { get; }
    public Column<int> CounterUntil { get; }
    public Column<bool> Takedown { get; }
    /// <summary>Bit per segment targeted by prebunking.</summary>
    public Column<int> PrebunkMask { get; }
    /// <summary>Hours until some segment passes 25% belief (-1: none within the horizon).</summary>
    public Column<Fixed> RumorHours { get; }
    public Column<bool> FactionEffectsApplied { get; }

    public Column<Fine> S { get; }
    public Column<Fine> E { get; }
    public Column<Fine> B { get; }
    public Column<Fine> R { get; }
    public Column<bool> Established { get; }
    /// <summary>Counter-narrative Plausibility multiplier per [narrative × segments + segment], in force through PlausibilityUntil (D-045).</summary>
    public Column<Fine> PlausibilityFactor { get; }
    public Column<int> PlausibilityUntil { get; }

    public NarrativeStore(ScenarioDef s, SegmentStore segments, NationStore nations)
        : base("narrative", s.Society.Narratives.Select(n => n.Id).ToList())
    {
        Defs = s.Society.Narratives;
        Segments = segments.Count;
        Origin = Defs.Select(d => nations.IdOf(d.Origin)).ToArray();
        Resonance = new Fine[Count * Segments];
        for (int n = 0; n < Count; n++)
            for (int g = 0; g < Segments; g++)
            {
                var tags = segments.Defs[g].Tags;
                var sum = Fixed.Zero;
                foreach (var t in tags) sum += Defs[n].Resonance.TryGetValue(t, out var r) ? r : Fixed.One;
                Resonance[n * Segments + g] = (tags.Count == 0 ? Fixed.One : sum / tags.Count).ToFine();
            }

        Active = Col<bool>("active");
        SeededDay = Col<int>("seeded_day");
        CounterUntil = Col<int>("counter_until");
        Takedown = Col<bool>("takedown");
        PrebunkMask = Col<int>("prebunk_mask");
        RumorHours = Col<Fixed>("rumor_hours");
        FactionEffectsApplied = Col<bool>("faction_effects_applied");
        S = new Column<Fine>("narrative.s", Count * Segments);
        E = new Column<Fine>("narrative.e", Count * Segments);
        B = new Column<Fine>("narrative.b", Count * Segments);
        R = new Column<Fine>("narrative.r", Count * Segments);
        Established = new Column<bool>("narrative.established", Count * Segments);
        PlausibilityFactor = new Column<Fine>("narrative.plausibility_factor", Count * Segments);
        PlausibilityUntil = new Column<int>("narrative.plausibility_until", Count * Segments);
        for (int i = 0; i < Count; i++) { SeededDay.Init(i, -1); CounterUntil.Init(i, -1); RumorHours.Init(i, Fixed.FromInt(-1)); }
        for (int i = 0; i < Count * Segments; i++) { S.Init(i, Fine.One); PlausibilityFactor.Init(i, Fine.One); PlausibilityUntil.Init(i, -1); }
    }

    public int At(int narrative, int segment) => narrative * Segments + segment;

    public override void Commit()
    {
        base.Commit();
        S.Commit(); E.Commit(); B.Commit(); R.Commit(); Established.Commit(); PlausibilityFactor.Commit(); PlausibilityUntil.Commit();
    }

    public override void HashInto(StateHasher h)
    {
        base.HashInto(h);
        h.Add(S).Add(E).Add(B).Add(R).Add(Established).Add(PlausibilityFactor).Add(PlausibilityUntil);
    }
}

public enum OperationState { Ready = 0, Used = 1, Detected = 2, Patched = 3 }

/// <summary>Cyber operations (spec Data model: Operation).</summary>
public sealed class OperationStore : Store
{
    public IReadOnlyList<OperationDef> Defs { get; }
    public int[] Attacker { get; }
    public int[] Victim { get; }
    public int[][] Targets { get; }

    public Column<Fixed> Access { get; }
    public Column<Fixed> Defence { get; }
    public Column<int> State { get; }
    public Column<int> UsedDay { get; }
    /// <summary>The victim's confidence that the attacker did it: c(t) = c_max (1 − e^(−t/14)).</summary>
    public Column<Fine> Attribution { get; }

    public OperationStore(ScenarioDef s, NationStore nations, ProvinceStore provinces, SubstationStore subs)
        : base("operation", s.Conflict.Operations.Select(o => o.Id).ToList())
    {
        Defs = s.Conflict.Operations;
        Attacker = Defs.Select(d => nations.IdOf(d.Attacker)).ToArray();
        Victim = Defs.Select(d => provinces.Owner[provinces.IdOf(d.TargetProvince)]).ToArray();
        Targets = Defs.Select(d => d.Targets.Select(subs.IdOf).ToArray()).ToArray();
        Access = Col<Fixed>("access");
        Defence = Col<Fixed>("defence");
        State = Col<int>("state");
        UsedDay = Col<int>("used_day");
        Attribution = Col<Fine>("attribution");
        for (int i = 0; i < Count; i++) { Access.Init(i, Defs[i].Access); Defence.Init(i, Defs[i].Defence); UsedDay.Init(i, -1); }
    }
}

public enum Posture { Hold = 0, Attack = 1 }

/// <summary>Brigades (spec Data model: Brigade). Mobilization's reserve brigades exist from the start at strength 0.</summary>
public sealed class BrigadeStore : Store
{
    public IReadOnlyList<BrigadeDef> Defs { get; }
    public int[] Nation { get; }
    /// <summary>Home segment for casualties, or -1.</summary>
    public int[] Home { get; }
    public bool[] Mechanized { get; }
    /// <summary>True for a mobilization reserve brigade.</summary>
    public bool[] Reserve { get; }

    public Column<Fixed> Strength { get; }
    public Column<Fixed> Training { get; }
    public Column<Fixed> Equipment { get; }
    public Column<Fixed> Cohesion { get; }
    public Column<Fixed> Experience { get; }
    public Column<Fixed> Morale { get; }
    public Column<int> Posture { get; }
    public Column<Fixed> Killed { get; }

    public BrigadeStore(ScenarioDef s, NationStore nations, SegmentStore segments)
        : base("brigade", s.Conflict.Brigades.Concat(s.Conflict.Mobilization.Select(m => m.ReserveBrigade)).Select(b => b.Id).ToList())
    {
        Defs = s.Conflict.Brigades.Concat(s.Conflict.Mobilization.Select(m => m.ReserveBrigade)).ToList();
        Nation = Defs.Select(d => nations.IdOf(d.Nation)).ToArray();
        Home = Defs.Select(d => d.Home == ScenarioDef.None ? -1 : segments.IdOf(d.Home)).ToArray();
        Mechanized = Defs.Select(d => d.Type == "mechanized").ToArray();
        Reserve = Defs.Select((d, i) => i >= s.Conflict.Brigades.Count).ToArray();
        Strength = Col<Fixed>("strength");
        Training = Col<Fixed>("training");
        Equipment = Col<Fixed>("equipment");
        Cohesion = Col<Fixed>("cohesion");
        Experience = Col<Fixed>("experience");
        Morale = Col<Fixed>("morale");
        Posture = Col<int>("posture");
        Killed = Col<Fixed>("killed");
        for (int i = 0; i < Count; i++)
        {
            var d = Defs[i];
            Strength.Init(i, d.Strength);
            Training.Init(i, d.Training);
            Equipment.Init(i, d.Equipment);
            Cohesion.Init(i, d.Cohesion);
            Experience.Init(i, d.Experience);
            Morale.Init(i, d.Morale);
        }
    }
}

/// <summary>The front segment at Veyl (spec Data model: Front segment). Per-side values are [nation].</summary>
public sealed class FrontStore : Store
{
    public FrontDef Def { get; }
    public int Province { get; }
    /// <summary>Front side definition per nation id (null if the nation has no side).</summary>
    public FrontSideDef?[] Side { get; }

    public Column<int> ActiveUntil { get; }
    public Column<bool> Locked { get; }
    public Column<Fixed> AdvanceKm { get; }
    public Column<Fixed> AdvanceToday { get; }
    public Column<Fixed> DroneDensity { get; }
    public Column<Fine> Detection { get; }
    public Column<Fixed> CombatPower { get; }
    public Column<Fixed> KillZone { get; }
    public Column<Fixed> ForceRatio { get; }

    public FrontStore(ScenarioDef s, NationStore nations, ProvinceStore provinces) : base("front", nations.Keys)
    {
        Def = s.Conflict.Front;
        Province = provinces.IdOf(Def.Province);
        Side = nations.Keys.Select(k => Def.Sides.FirstOrDefault(x => x.Nation == k)).ToArray();
        ActiveUntil = Col<int>("active_until");
        Locked = Col<bool>("locked");
        AdvanceKm = Col<Fixed>("advance_km");
        AdvanceToday = Col<Fixed>("advance_today");
        DroneDensity = Col<Fixed>("drone_density");
        Detection = Col<Fine>("detection");
        CombatPower = Col<Fixed>("combat_power");
        KillZone = Col<Fixed>("kill_zone");
        ForceRatio = Col<Fixed>("force_ratio");
        for (int i = 0; i < Count; i++) ActiveUntil.Init(i, -1);
    }
}

/// <summary>The escalation meter for each pair of nations (spec Escalation meter), [a × nations + b], kept symmetric.</summary>
public sealed class EscalationStore : IStateHashable, ICommittable
{
    public int Nations { get; }
    public Column<Fixed> Meter { get; }
    /// <summary>Last day an action of weight 3+ happened between the pair (blocks weekly decay).</summary>
    public Column<int> LastBigAction { get; }

    public EscalationStore(int nations, Fixed start)
    {
        Nations = nations;
        Meter = new("escalation.meter", nations * nations);
        LastBigAction = new("escalation.last_big_action", nations * nations);
        for (int a = 0; a < nations; a++)
            for (int b = 0; b < nations; b++)
            {
                Meter.Init(a * nations + b, a == b ? Fixed.Zero : start);
                LastBigAction.Init(a * nations + b, -100);
            }
    }

    public int At(int a, int b) => a * Nations + b;
    public void Commit() { Meter.Commit(); LastBigAction.Commit(); }
    public void HashInto(StateHasher h) { h.Add(Meter); h.Add(LastBigAction); }
}

public sealed class ShippingStore : Store
{
    public Column<bool> Calling { get; }
    public ShippingStore(ScenarioDef s) : base("shipping_line", s.Conflict.ShippingLines)
    {
        Calling = Col<bool>("calling");
        for (int i = 0; i < Count; i++) Calling.Init(i, true);
    }
}

/// <summary>
/// The history of the campaign (spec Record phase: the event log; concept: the Chronicle is a rendered view of it).
/// Append-only; phases add entries, nothing edits them.
/// </summary>
public sealed class EventLog : IStateHashable
{
    private readonly List<LogEntry> _entries = [];
    public IReadOnlyList<LogEntry> Entries => _entries;

    public void Add(int day, int hour, string kind, string text, string actor = "", string subject = "", Fixed value = default)
        => _entries.Add(new LogEntry(_entries.Count, day, hour, kind, actor, subject, value, text));

    public void HashInto(StateHasher h)
    {
        h.Section("event_log").Add(_entries.Count);
        foreach (var e in _entries) h.Add(e.Day).Add(e.Hour).Add(e.Kind).Add(e.Actor).Add(e.Subject).Add(e.Value).Add(e.Text);
    }
}

public sealed record LogEntry(int Seq, int Day, int Hour, string Kind, string Actor, string Subject, Fixed Value, string Text);
