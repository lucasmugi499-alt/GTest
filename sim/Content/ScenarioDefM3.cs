using Cascade.Sim.Core;

namespace Cascade.Sim.Content;

public sealed record GovernmentDef(string Nation, Fixed Information, Fixed Centralization, Fixed RuleOfLaw, Fixed Market,
    Fixed PoliticalCapital, Fixed Legitimacy, Fine IntelQuality, Fixed CyberSkill);

public sealed record SegmentDef(string Id, string Name, string Province, long Population, string Livelihood,
    IReadOnlyList<string> Tags, IReadOnlyList<string> Homes, Fixed Trust, Fixed Align, Fixed Mobilization, Fixed Identity, Fixed Unemployment);

public sealed record FactionDef(string Id, string Name, IReadOnlyList<(string Segment, Fine Share)> Members, Fixed Institutions,
    Fixed Approval, IReadOnlyDictionary<string, Fixed> Agenda, IReadOnlyList<string> Actions);

public sealed record CorporationDef(string Id, string Name, Fixed Loyalty, Fine RivalRevenueShare);
public sealed record PlatformDef(string Id, string Owner, Fine Share);

public sealed record NarrativeDef(string Id, string Name, string Origin, Fixed Virality, Fine Plausibility,
    IReadOnlyDictionary<string, Fixed> Resonance, Fixed AlignHit, Fixed TrustHit, IReadOnlyList<(string Faction, Fixed Shift)> Established);

public sealed record PrecedentDef(string Id, Fixed PoliticalCapital, Fixed ApprovalHit, bool CivilLiberties);

public sealed record EmergencyPowerDef(string Id, string Precedent, Fixed RefuelMultiplier, Fine ViralityMultiplier, Fixed SafetyBonus, Fixed DignityPenalty, bool PerProvince);

public sealed record SocietyDef(
    IReadOnlyList<GovernmentDef> Government,
    IReadOnlyList<SegmentDef> Segments,
    IReadOnlyList<FactionDef> Factions,
    IReadOnlyList<CorporationDef> Corporations,
    IReadOnlyList<PlatformDef> Platforms,
    Fine[,] Contact,
    IReadOnlyList<NarrativeDef> Narratives,
    IReadOnlyList<PrecedentDef> Precedents,
    IReadOnlyList<EmergencyPowerDef> EmergencyPowers)
{
    public static SocietyDef Read(ContentNode n)
    {
        var gov = n.Child("government");
        var government = gov.Keys().Select(k =>
        {
            var g = gov.Child(k);
            return new GovernmentDef(k, g.Fixed("information"), g.Fixed("centralization"), g.Fixed("rule_of_law"), g.Fixed("market"),
                g.Fixed("political_capital"), g.Fixed("legitimacy"), g.Fine("intel_quality"), g.Fixed("cyber_skill"));
        }).ToList();

        var segments = n.List("segments").Select(x => new SegmentDef(
            x.Str("id"), x.Str("name"), x.Str("province"), x.Long("population"), x.Str("livelihood"),
            x.StrList("tags"), x.StrList("homes"), x.Fixed("trust"), x.Fixed("align"), x.Fixed("mobilization"),
            x.Fixed("identity"), x.Fixed("unemployment"))).ToList();

        var factions = n.List("factions").Select(x =>
        {
            var m = x.Child("members");
            var a = x.Child("agenda");
            return new FactionDef(x.Str("id"), x.Str("name"),
                m.Keys().Select(k => (k, m.Fine(k))).ToList(), x.Fixed("institutions"), x.Fixed("approval"),
                a.Keys().ToDictionary(k => k, a.Fixed, StringComparer.Ordinal), x.StrList("actions"));
        }).ToList();

        var corps = n.List("corporations").Select(x => new CorporationDef(x.Str("id"), x.Str("name"), x.Fixed("loyalty"), x.Fine("rival_revenue_share"))).ToList();
        var platforms = n.List("platforms").Select(x => new PlatformDef(x.Str("id"), x.Str("owner"), x.Fine("share"))).ToList();

        var c = n.Child("contact");
        var contact = new Fine[segments.Count, segments.Count];
        for (int s = 0; s < segments.Count; s++)
        {
            var row = c.Child(segments[s].Id);
            for (int t = 0; t < segments.Count; t++) contact[s, t] = row.Fine(segments[t].Id);
        }

        var narratives = n.List("narratives").Select(x =>
        {
            var r = x.Child("resonance");
            var e = x.Child("established");
            return new NarrativeDef(x.Str("id"), x.Str("name"), x.Str("origin"), x.Fixed("virality"), x.Fine("plausibility"),
                r.Keys().ToDictionary(k => k, r.Fixed, StringComparer.Ordinal), x.Fixed("align_hit"), x.Fixed("trust_hit"),
                e.Keys().Select(k => (k, e.Fixed(k))).ToList());
        }).ToList();

        var pr = n.Child("precedents");
        var precedents = pr.Keys().Select(k =>
        {
            var x = pr.Child(k);
            return new PrecedentDef(k, x.Fixed("political_capital"), x.Fixed("approval_hit"), x.Bool("civil_liberties"));
        }).ToList();

        var ep = n.Child("emergency_powers");
        var powers = ep.Keys().Select(k =>
        {
            var x = ep.Child(k);
            return new EmergencyPowerDef(k, x.Str("precedent"),
                x.Has("refuel_multiplier") ? x.Fixed("refuel_multiplier") : Fixed.One,
                x.Has("virality_multiplier") ? x.Fine("virality_multiplier") : Fine.One,
                x.Has("safety_bonus") ? x.Fixed("safety_bonus") : Fixed.Zero,
                x.Has("dignity_penalty") ? x.Fixed("dignity_penalty") : Fixed.Zero,
                x.Has("per_province") && x.Bool("per_province"));
        }).ToList();

        return new SocietyDef(government, segments, factions, corps, platforms, contact, narratives, precedents, powers);
    }

    public int Segment(string id) => Index(Segments.Select(s => s.Id), id, "segment");
    public int Faction(string id) => Index(Factions.Select(s => s.Id), id, "faction");
    public int Narrative(string id) => Index(Narratives.Select(s => s.Id), id, "narrative");
    public int Precedent(string id) => Index(Precedents.Select(s => s.Id), id, "precedent");
    public int Corporation(string id) => Index(Corporations.Select(s => s.Id), id, "corporation");
    public int Power(string id) => Index(EmergencyPowers.Select(s => s.Id), id, "emergency power");

    internal static int Index(IEnumerable<string> ids, string id, string what)
    {
        int i = 0;
        foreach (var x in ids) { if (x == id) return i; i++; }
        throw new ContentException($"Unknown {what} '{id}'.");
    }
}

public sealed record OperationDef(string Id, string Attacker, string TargetProvince, IReadOnlyList<string> Targets, Fixed Access,
    Fixed Vulnerability, Fixed Defence, string Vendor, Fixed Payload, Fine AnalogReserve, bool Proxy);

public sealed record FrontSideDef(string Nation, Fixed Fortification, Fixed Concealment, Fine Protection, Fine Jamming,
    Fine Satellite, Fine Sigint, Fine Humint, string? DroneDesign, Fine DroneEffectiveness);

public sealed record FrontDef(string Id, string Province, Fixed WidthKm, string DroneGood, IReadOnlyList<FrontSideDef> Sides);

public sealed record BrigadeDef(string Id, string Name, string Nation, string Type, Fixed Strength, Fixed Training, Fixed Equipment,
    Fixed Cohesion, Fixed Experience, Fixed Morale, string Home, Fixed DronesPerDay);

public sealed record MobilizationDef(string Nation, int[] ReservistsByLevel, Fine FrontShare, BrigadeDef ReserveBrigade,
    IReadOnlyList<(string Province, string Pool, Fixed People)> SkilledReservists, string RecruitmentLaw);

public sealed record RepertoireDef(string Id, string Kind, string Action, string? Narrative, IReadOnlyList<(string Segment, Fine Share)> Seed,
    string? Operation, Fine Losses, int KestrianDeaths, int VaranDeaths, int Days);

public sealed record ScheduleDef(int Day, int Hour, string Kind, int KestrianDeaths, int VaranDeaths, string? Operation, string? Effect,
    string? FallbackOperation, string? FallbackEffect, string? Narrative, IReadOnlyList<(string Segment, Fine Share)> Seed,
    IReadOnlyList<string> Goods, string Note);

public sealed record ConflictDef(
    IReadOnlyList<OperationDef> Operations,
    FrontDef Front,
    IReadOnlyList<BrigadeDef> Brigades,
    IReadOnlyList<MobilizationDef> Mobilization,
    Fixed StartMeter,
    string VaranPersonality, bool VaranNationalistsStrong, bool VaranEconomyWeak, int DroneOpsNearBorder,
    IReadOnlyList<RepertoireDef> Repertoire,
    IReadOnlyList<ScheduleDef> Schedule,
    IReadOnlyList<string> ShippingLines)
{
    public static ConflictDef Read(ContentNode n)
    {
        var ops = n.List("operations").Select(x => new OperationDef(
            x.Str("id"), x.Str("attacker"), x.Str("target_province"), x.StrList("targets"), x.Fixed("access"),
            x.Fixed("vulnerability"), x.Fixed("defence"), x.Str("vendor"), x.Fixed("payload"), x.Fine("analog_reserve"), x.Bool("proxy"))).ToList();

        var f = n.Child("front");
        var sides = f.Child("sides");
        var front = new FrontDef(f.Str("id"), f.Str("province"), f.Fixed("width_km"), f.Str("drone_good"), sides.Keys().Select(k =>
        {
            var s = sides.Child(k);
            return new FrontSideDef(k, s.Fixed("fortification"), s.Fixed("concealment"), s.Fine("protection"), s.Fine("jamming"),
                s.Fine("satellite"), s.Fine("sigint"), s.Fine("humint"),
                s.OptStr("drone_effectiveness_design"),
                s.Has("drone_effectiveness") ? s.Fine("drone_effectiveness") : Fine.One);
        }).ToList());

        var brigades = n.List("brigades").Select(x => Brigade(x, x.Str("id"), x.Str("nation"))).ToList();

        var mob = n.Child("mobilization");
        var mobs = mob.Keys().Select(k =>
        {
            var m = mob.Child(k);
            var skilled = m.Child("skilled_reservists");
            var list = new List<(string, string, Fixed)>();
            foreach (var prov in skilled.Keys())
            {
                var pools = skilled.Child(prov);
                foreach (var pool in pools.Keys()) list.Add((prov, pool, pools.Fixed(pool)));
            }
            return new MobilizationDef(k, m.IntList("reservists_by_level"), m.Fine("front_share"),
                Brigade(m.Child("reserve_brigade"), $"{k}_reserve_bde", k), list, m.Str("recruitment_law"));
        }).ToList();

        var v = n.Child("varan");
        var repertoire = v.List("repertoire").Select(x => new RepertoireDef(
            x.Str("id"), x.Str("kind"), x.Str("action"), x.OptStr("narrative"), Seed(x),
            x.OptStr("operation"),
            x.Has("losses") ? x.Fine("losses") : Fine.Zero,
            x.Has("kestrian_deaths") ? x.Int("kestrian_deaths") : 0,
            x.Has("varan_deaths") ? x.Int("varan_deaths") : 0,
            x.Has("days") ? x.Int("days") : 0)).ToList();

        var schedule = n.List("schedule").Select(x => new ScheduleDef(
            x.Int("day"), x.Int("hour"), x.Str("kind"),
            x.Has("kestrian_deaths") ? x.Int("kestrian_deaths") : 0,
            x.Has("varan_deaths") ? x.Int("varan_deaths") : 0,
            x.OptStr("operation"), x.OptStr("effect"), x.OptStr("fallback_operation"), x.OptStr("fallback_effect"),
            x.OptStr("narrative"), Seed(x),
            x.Has("goods") ? x.StrList("goods") : [],
            x.OptStr("note") ?? "")).ToList();

        return new ConflictDef(ops, front, brigades, mobs, n.Child("escalation").Fixed("start_meter"),
            v.Str("personality"), v.Bool("nationalists_strong"), v.Bool("economy_weak"), v.Int("drone_ops_near_border"),
            repertoire, schedule, n.StrList("shipping_lines"));
    }

    private static BrigadeDef Brigade(ContentNode x, string id, string nation) => new(
        id, x.Str("name"), nation, x.Str("type"), x.Has("strength") ? x.Fixed("strength") : Fixed.Zero,
        x.Fixed("training"), x.Fixed("equipment"), x.Fixed("cohesion"), x.Fixed("experience"), x.Fixed("morale"),
        x.Str("home"), x.Fixed("drones_per_day"));

    private static List<(string, Fine)> Seed(ContentNode x)
    {
        if (!x.Has("seed")) return [];
        var s = x.Child("seed");
        return s.Keys().Select(k => (k, s.Fine(k))).ToList();
    }

    public int Operation(string id) => SocietyDef.Index(Operations.Select(o => o.Id), id, "operation");
}
