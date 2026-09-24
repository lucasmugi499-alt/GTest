using Cascade.Sim.Content;
using Cascade.Sim.Core;

namespace Cascade.Sim.Narrative;

public sealed record CharacterDef(string Id, string Name, string Role, string Portfolio, string Province, string Faction,
    IReadOnlyDictionary<string, Fixed> Competence, Fixed LoyaltyPlayer, Fixed LoyaltyFaction, Fixed LoyaltyNation, Fixed LoyaltySelf,
    string Ambition, IReadOnlyList<string> Secrets, Fixed Drama);

public enum CompareOp { Eq, Ne, Lt, Le, Gt, Ge, In }

/// <summary>"fact op value" or "fact in [a, b]" (spec: preconditions are queries on world state).</summary>
public sealed record Condition(string Fact, CompareOp Op, Fixed Value, Fixed Value2, string Source)
{
    public static Condition Parse(string text, string where)
    {
        var t = text.Trim();
        int inAt = t.IndexOf(" in ", StringComparison.Ordinal);
        if (inAt > 0)
        {
            var fact = t[..inAt].Trim();
            var range = t[(inAt + 4)..].Trim().TrimStart('[').TrimEnd(']').Split(',');
            if (range.Length != 2) throw new ContentException($"{where}: '{text}': expected 'fact in [a, b]'");
            return new Condition(fact, CompareOp.In, Num(range[0], where, text), Num(range[1], where, text), text);
        }
        foreach (var (sym, op) in new[] { (">=", CompareOp.Ge), ("<=", CompareOp.Le), ("==", CompareOp.Eq), ("!=", CompareOp.Ne), (">", CompareOp.Gt), ("<", CompareOp.Lt) })
        {
            int at = t.IndexOf($" {sym} ", StringComparison.Ordinal);
            if (at > 0) return new Condition(t[..at].Trim(), op, Num(t[(at + sym.Length + 2)..], where, text), Fixed.Zero, text);
        }
        throw new ContentException($"{where}: '{text}': expected 'fact op value' with op one of >= <= == != > < in");
    }

    private static Fixed Num(string s, string where, string text)
    {
        try { return Fixed.Parse(s.Trim()); }
        catch (FormatException e) { throw new ContentException($"{where}: '{text}': {e.Message}"); }
    }

    public bool Holds(Fixed v) => Op switch
    {
        CompareOp.Eq => v == Value,
        CompareOp.Ne => v != Value,
        CompareOp.Lt => v < Value,
        CompareOp.Le => v <= Value,
        CompareOp.Gt => v > Value,
        CompareOp.Ge => v >= Value,
        _ => v >= Value && v <= Value2,
    };
}

public sealed record RoleSpec(string Name, string Role, string? Portfolio, string? Province, string? Faction, Fixed? OpinionBelow);

/// <summary>One effect of a choice, e.g. ("escalation", -5) or ("order.repair", [ {substation, method} ]).</summary>
public sealed record EffectDef(string Key, CValue Value);

public sealed record ChoiceDef(string Id, string Text, string Hint, IReadOnlyList<Condition> Requires, IReadOnlyList<EffectDef> Effects,
    IReadOnlyList<(string Role, Fixed Valence)> Memories, IReadOnlyList<string> Seeds);

public enum Tier { Major = 0, Minor = 1 }

public sealed record StoryletDef(int Index, string Id, string? Arc, Tier Tier, Fixed Weight, Fine Intensity, int CooldownDays, int ExpiresDays,
    string? Default, string Title, string Text, IReadOnlyList<Condition> Preconditions, IReadOnlyList<RoleSpec> Roles,
    IReadOnlyList<ChoiceDef> Choices)
{
    /// <summary>The seed this storylet pays off, if one of its preconditions is "seed.X.ripe == 1".</summary>
    public string? PaysOffSeed => Preconditions.Select(c => c.Fact).FirstOrDefault(f => f.StartsWith("seed.") && f.EndsWith(".ripe"))?[5..^5];

    public int ChoiceIndex(string id) => Choices.Select((c, i) => (c, i)).FirstOrDefault(x => x.c.Id == id, (null!, -1)).Item2;
}

public sealed record SeedDef(int Index, string Id, Fixed Weight, int MinDelayDays, Fine MonthlyChance, IReadOnlyList<Condition> Defuse);

/// <summary>Characters, storylets and seeds for a scenario, plus every flag they mention (flags get dense IDs, D-022).</summary>
public sealed record NarrativeDef(
    IReadOnlyList<CharacterDef> Characters,
    IReadOnlyList<StoryletDef> Storylets,
    IReadOnlyList<SeedDef> Seeds,
    IReadOnlyList<string> Flags)
{
    public static NarrativeDef Read(ContentNode characters, ContentNode storylets)
    {
        var chars = characters.List("characters").Select(x =>
        {
            var comp = x.Child("competence");
            var loy = x.Child("loyalties");
            return new CharacterDef(x.Str("id"), x.Str("name"), x.Str("role"), x.Str("portfolio"), x.Str("province"), x.Str("faction"),
                comp.Keys().ToDictionary(k => k, comp.Fixed, StringComparer.Ordinal),
                loy.Fixed("player"), loy.Fixed("faction"), loy.Fixed("nation"), loy.Fixed("self"),
                x.Str("ambition"), x.StrList("secrets"), x.Fixed("drama"));
        }).ToList();

        var list = new List<StoryletDef>();
        foreach (var x in storylets.List("storylets"))
        {
            var where = x.Path;
            var roles = x.Child("roles");
            var roleSpecs = roles.Keys().Select(r =>
            {
                var rs = roles.Child(r);
                return new RoleSpec(r, rs.Str("role"), rs.OptStr("portfolio"), rs.OptStr("province"), rs.OptStr("faction"),
                    rs.Has("opinion_below") ? rs.Fixed("opinion_below") : null);
            }).ToList();

            var choices = x.List("choices").Select(c =>
            {
                var cw = c.Path;
                var mem = c.Has("memories") ? c.Child("memories") : null;
                return new ChoiceDef(c.Str("id"), c.Str("text"), c.Str("hint"),
                    c.Has("requires") ? c.StrList("requires").Select(t => Condition.Parse(t, cw)).ToList() : [],
                    c.Child("effects").Entries().Select(e => new EffectDef(e.Key, e.Value)).ToList(),
                    mem?.Keys().Select(k => (k, mem.Fixed(k))).ToList() ?? [],
                    c.Has("seeds") ? c.StrList("seeds") : []);
            }).ToList();

            var tier = x.Str("tier") switch
            {
                "major" => Tier.Major,
                "minor" => Tier.Minor,
                var t => throw new ContentException($"{where}.tier: expected major or minor, got '{t}'"),
            };
            list.Add(new StoryletDef(list.Count, x.Str("id"), x.OptStr("arc"), tier, x.Fixed("weight"), x.Fine("intensity"),
                x.Int("cooldown_days"), x.Has("expires_days") ? x.Int("expires_days") : -1, x.OptStr("default"),
                x.Str("title"), x.Str("text").Trim(),
                x.StrList("preconditions").Select(t => Condition.Parse(t, where)).ToList(), roleSpecs, choices));
        }

        var seeds = storylets.List("seeds").Select((x, i) => new SeedDef(i, x.Str("id"), x.Fixed("weight"), x.Int("min_delay_days"),
            x.Fine("monthly_chance"), x.StrList("defuse").Select(t => Condition.Parse(t, x.Path)).ToList())).ToList();

        // Every flag named anywhere, in first-seen order.
        var flags = new List<string>();
        void Flag(string fact) { if (fact.StartsWith("flag.") && !flags.Contains(fact[5..])) flags.Add(fact[5..]); }
        foreach (var s in list)
        {
            foreach (var c in s.Preconditions) Flag(c.Fact);
            foreach (var ch in s.Choices)
            {
                foreach (var c in ch.Requires) Flag(c.Fact);
                foreach (var e in ch.Effects) Flag(e.Key);
            }
        }
        foreach (var s in seeds) foreach (var c in s.Defuse) Flag(c.Fact);

        var def = new NarrativeDef(chars, list, seeds, flags);
        def.Validate();
        return def;
    }

    private void Validate()
    {
        void Unique(IEnumerable<string> ids, string what)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ids) if (!seen.Add(id)) throw new ContentException($"Duplicate {what} '{id}'.");
        }
        Unique(Characters.Select(c => c.Id), "character");
        Unique(Storylets.Select(s => s.Id), "storylet");
        Unique(Seeds.Select(s => s.Id), "seed");
        foreach (var s in Storylets)
        {
            if (s.Choices.Count == 0) throw new ContentException($"Storylet '{s.Id}' has no choices.");
            Unique(s.Choices.Select(c => c.Id), $"choice in storylet '{s.Id}'");
            if (s.Default is not null && s.ChoiceIndex(s.Default) < 0) throw new ContentException($"Storylet '{s.Id}': default '{s.Default}' is not a choice.");
            if (s.ExpiresDays >= 0 && s.Default is null) throw new ContentException($"Storylet '{s.Id}' expires but has no default choice.");
            foreach (var r in s.Roles)
                if (!Characters.Any(c => c.Role == r.Role)) throw new ContentException($"Storylet '{s.Id}': nobody can play role '{r.Role}'.");
            foreach (var ch in s.Choices)
            {
                foreach (var (role, _) in ch.Memories)
                    if (!s.Roles.Any(r => r.Name == role)) throw new ContentException($"Storylet '{s.Id}', choice '{ch.Id}': memory for unknown role '{role}'.");
                foreach (var seed in ch.Seeds)
                    if (!Seeds.Any(x => x.Id == seed)) throw new ContentException($"Storylet '{s.Id}', choice '{ch.Id}': unknown seed '{seed}'.");
            }
        }
        foreach (var seed in Seeds)
            if (!Storylets.Any(s => s.PaysOffSeed == seed.Id)) throw new ContentException($"Seed '{seed.Id}' has no payoff storylet (precondition 'seed.{seed.Id}.ripe == 1').");
    }

    public int Storylet(string id) => SocietyDef.Index(Storylets.Select(s => s.Id), id, "storylet");
    public int Character(string id) => SocietyDef.Index(Characters.Select(s => s.Id), id, "character");
    public int Seed(string id) => SocietyDef.Index(Seeds.Select(s => s.Id), id, "seed");
    public int Flag(string id) => SocietyDef.Index(Flags, id, "flag");
}
