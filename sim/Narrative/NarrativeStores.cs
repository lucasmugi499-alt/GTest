using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.World;

namespace Cascade.Sim.Narrative;

/// <summary>Named facts that storylets set and test (flag.X). Values default to 0.</summary>
public sealed class FlagStore : Store
{
    public Column<Fixed> Value { get; }
    public FlagStore(NarrativeDef n) : base("flag", n.Flags) => Value = Col<Fixed>("value");
}

/// <summary>A character's memory of something the player did (spec Character memory).</summary>
public readonly record struct Memory(int Character, int Day, Fixed Valence, bool Grave);

public sealed class CharacterStore : Store
{
    public IReadOnlyList<CharacterDef> Defs { get; }
    private readonly List<Memory> _memories = [];
    private readonly List<(int Character, int Day)> _appearances = [];

    public IReadOnlyList<Memory> Memories => _memories;
    public IReadOnlyList<(int Character, int Day)> Appearances => _appearances;

    public CharacterStore(NarrativeDef n) : base("character", n.Characters.Select(c => c.Id).ToList()) => Defs = n.Characters;

    public void Remember(int character, int day, Fixed valence, Fixed graveAt) =>
        _memories.Add(new Memory(character, day, valence, valence.Abs() >= graveAt));

    public void Appear(int character, int day) => _appearances.Add((character, day));

    /// <summary>Op_c = Σ v_m s_m e^(−t_m/τ_m), τ 730 days for grave acts, 90 for minor (salience 1 in the slice).</summary>
    public Fixed Opinion(int character, int today, Fixed graveTau, Fixed minorTau)
    {
        var sum = Fixed.Zero;
        foreach (var m in _memories)
        {
            if (m.Character != character) continue;
            var tau = m.Grave ? graveTau : minorTau;
            sum += m.Valence.Times(FixedMath.Exp(-(Fixed.FromInt(today - m.Day) / tau).ToFine()));
        }
        return sum;
    }

    public int MemoryCount(int character) => _memories.Count(m => m.Character == character);
    public int RecentAppearances(int character, int today, int window) => _appearances.Count(a => a.Character == character && today - a.Day < window);

    public override void HashInto(StateHasher h)
    {
        base.HashInto(h);
        h.Add(_memories.Count);
        foreach (var m in _memories) h.Add(m.Character).Add(m.Day).Add(m.Valence).Add(m.Grave);
        h.Add(_appearances.Count);
        foreach (var (c, d) in _appearances) h.Add(c).Add(d);
    }
}

/// <summary>A storylet that has fired: who was cast, when it expires, and the choice once made.</summary>
public sealed class StoryletInstance(long seq, int storylet, int day, int hour, int[] cast, int expiresDay)
{
    public long Seq { get; } = seq;
    public int Storylet { get; } = storylet;
    public int Day { get; } = day;
    public int Hour { get; } = hour;
    /// <summary>Character per role, in the storylet's role order.</summary>
    public int[] Cast { get; } = cast;
    /// <summary>Last day the player can answer before the default applies, or -1 if it waits.</summary>
    public int ExpiresDay { get; } = expiresDay;
    public int Choice { get; set; } = -1;
    public int ResolvedDay { get; set; } = -1;
    public bool ByDefault { get; set; }
    public bool Pending => Choice < 0;
}

public sealed class StoryletState : Store
{
    public Column<int> LastFired { get; }
    public Column<int> Fired { get; }
    private readonly List<StoryletInstance> _instances = [];
    private long _nextSeq;

    public IReadOnlyList<StoryletInstance> Instances => _instances;

    public StoryletState(NarrativeDef n) : base("storylet", n.Storylets.Select(s => s.Id).ToList())
    {
        LastFired = Col<int>("last_fired");
        Fired = Col<int>("fired");
        for (int i = 0; i < Count; i++) LastFired.Init(i, int.MinValue / 2);
    }

    public StoryletInstance Add(int storylet, int day, int hour, int[] cast, int expiresDay)
    {
        var inst = new StoryletInstance(_nextSeq++, storylet, day, hour, cast, expiresDay);
        _instances.Add(inst);
        return inst;
    }

    public StoryletInstance? Find(long seq) => _instances.FirstOrDefault(i => i.Seq == seq);

    public override void HashInto(StateHasher h)
    {
        base.HashInto(h);
        h.Add(_nextSeq).Add(_instances.Count);
        foreach (var i in _instances)
        {
            h.Add(i.Seq).Add(i.Storylet).Add(i.Day).Add(i.Hour).Add(i.ExpiresDay).Add(i.Choice).Add(i.ResolvedDay).Add(i.ByDefault);
            foreach (int c in i.Cast) h.Add(c);
        }
    }
}

public enum SeedState { Dormant = 0, Live = 1, Ripe = 2, PaidOff = 3, Defused = 4 }

/// <summary>Seeds planted by choices (spec Seeds and Narrative Debt).</summary>
public sealed class SeedStore : Store
{
    public IReadOnlyList<SeedDef> Defs { get; }
    public Column<int> State { get; }
    public Column<int> PlantedDay { get; }

    public SeedStore(NarrativeDef n) : base("seed", n.Seeds.Select(s => s.Id).ToList())
    {
        Defs = n.Seeds;
        State = Col<int>("state");
        PlantedDay = Col<int>("planted_day");
        for (int i = 0; i < Count; i++) PlantedDay.Init(i, -1);
    }
}

/// <summary>The Director's dials (spec Tension and Director scoring) and running totals for the Chronicle.</summary>
public sealed class DirectorStore : Store
{
    public Column<int> LastMajorDay { get; }
    public Column<int> LastMajorHour { get; }
    public Column<Fixed> Tension { get; }
    public Column<Fixed> NarrativeDebt { get; }
    public Column<Fixed> ArcIntensity { get; }
    // Chronicle totals, kept by the Record phase.
    public Column<Fixed> PeopleDarkDays { get; }
    public Column<int> MaxRung { get; }
    public Column<Fixed> ChipsBaseline { get; }
    public Column<Fixed> ChipsLast { get; }
    public Column<Fixed> DronesBaseline { get; }
    public Column<Fixed> DronesLast { get; }
    public Column<Fixed> ChipsTotal { get; }
    public Column<Fixed> DronesTotal { get; }
    public Column<int> CrisisDays { get; }

    public DirectorStore() : base("director", ["director"])
    {
        LastMajorDay = Col<int>("last_major_day");
        LastMajorHour = Col<int>("last_major_hour");
        Tension = Col<Fixed>("tension");
        NarrativeDebt = Col<Fixed>("narrative_debt");
        ArcIntensity = Col<Fixed>("arc_intensity");
        PeopleDarkDays = Col<Fixed>("people_dark_days");
        MaxRung = Col<int>("max_rung");
        ChipsBaseline = Col<Fixed>("chips_baseline");
        ChipsLast = Col<Fixed>("chips_last");
        DronesBaseline = Col<Fixed>("drones_baseline");
        DronesLast = Col<Fixed>("drones_last");
        ChipsTotal = Col<Fixed>("chips_total");
        DronesTotal = Col<Fixed>("drones_total");
        CrisisDays = Col<int>("crisis_days");
        LastMajorDay.Init(0, -1000);
    }
}
