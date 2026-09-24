using Cascade.Sim.Conflict;
using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Narrative;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Tests;

internal static class Story
{
    /// <summary>
    /// Plays hour by hour (day by day outside Crisis Time), answering storylets from <paramref name="answers"/>
    /// (storylet id → choice id) and letting the autopilot answer everything else.
    /// </summary>
    public static void Play(Simulation sim, int throughDay, Dictionary<string, string>? answers = null, AutoMode fallback = AutoMode.Default)
    {
        var skip = new HashSet<long>();
        while (sim.Day <= throughDay && !sim.IsFinished)
        {
            foreach (var inst in sim.PendingDecisions.ToList())
            {
                if (skip.Contains(inst.Seq)) continue;
                var s = sim.Narrative.Def.Storylets[inst.Storylet];
                if (answers is not null && answers.TryGetValue(s.Id, out var choice))
                {
                    sim.Orders.Enqueue(new ChooseStoryletOrder(0, inst.Seq, s.ChoiceIndex(choice)));
                    skip.Add(inst.Seq);
                }
            }
            Autopilot.Answer(sim, fallback, skip);
            sim.StepHour();
        }
    }

    public static StoryletInstance? Fired(Simulation sim, string id) =>
        sim.World.Storylets.Instances.FirstOrDefault(i => sim.Narrative.Def.Storylets[i.Storylet].Id == id);

    public static string? Chosen(Simulation sim, string id)
    {
        var inst = Fired(sim, id);
        return inst is null || inst.Pending ? null : sim.Narrative.Def.Storylets[inst.Storylet].Choices[inst.Choice].Id;
    }
}

public class NarrativeTests
{
    // ---- Loading ----

    [Fact]
    public void ContentLoadsWithTheVeylBeatsAndGenericStorylets()
    {
        var n = TestContent.Repo.Scenario.Narrative;
        string[] beats = ["wreck_at_veyl", "chips_in_wreck", "survey_team_clash", "lights_out", "deepfake", "the_squeeze", "story_breaks", "story_breaks_coverup", "day10_decision", "odd_logins"];
        foreach (var b in beats) Assert.Equal("veyl", n.Storylets[n.Storylet(b)].Arc);
        Assert.True(n.Storylets.Count(s => s.Arc is null && s.Tier == Tier.Minor) >= 10);
        Assert.True(n.Characters.Count >= 12);
        foreach (var name in new[] { "Captain Idris Marr", "Defence Minister Rhea Castell", "Foreign Minister Selin Okafor", "Finance Minister Tomas Wren",
                     "Mara Voss", "Darius Kade", "Noor Haddad", "Deputy Trade Minister Aurel Brandt", "Jonah Pell" })
            Assert.Contains(n.Characters, c => c.Name == name);
    }

    [Fact]
    public void EveryFactAndEffectCompiles()
    {
        // NarrativeEngine compiles everything in its constructor and throws on anything unknown.
        var sim = TestContent.NewScenario();
        Assert.NotNull(sim.Narrative);
    }

    [Fact]
    public void ConditionsParse()
    {
        var c = Condition.Parse("rung in [1, 3]", "test");
        Assert.True(c.Holds(Fixed.FromInt(2)));
        Assert.False(c.Holds(Fixed.FromInt(4)));
        Assert.True(Condition.Parse("pc >= 15", "test").Holds(Fixed.FromInt(15)));
        Assert.False(Condition.Parse("flag.x == 1", "test").Holds(Fixed.Zero));
        Assert.Throws<ContentException>(() => Condition.Parse("pc => 15", "test"));
    }

    [Fact]
    public void UnknownFactIsALoadError()
    {
        var sim = TestContent.NewScenario();
        var e = Assert.Throws<ContentException>(() => sim.Narrative.Blackboard.Compile("province.atlantis.dark"));
        Assert.Contains("atlantis", e.Message);
        Assert.Throws<ContentException>(() => sim.Narrative.Blackboard.Compile("no_such_fact"));
    }

    // ---- The Veyl arc ----

    [Fact]
    public void WreckFiresOnDayZeroWithTheRightCast()
    {
        var sim = TestContent.NewScenario();
        sim.StepDay();
        var inst = Story.Fired(sim, "wreck_at_veyl");
        Assert.NotNull(inst);
        var chars = sim.World.Characters;
        Assert.Equal("idris_marr", chars.Keys[inst!.Cast[0]]);
        Assert.Equal("selin_okafor", chars.Keys[inst.Cast[1]]);
        Assert.True(inst.Pending);
        Assert.Contains("Captain Idris Marr secured it", sim.Narrative.Render(inst, sim.World, sim.Narrative.Def.Storylets[inst.Storylet].Text));
    }

    [Fact]
    public void StudyingTheWreckFindsTessraChipsOnDay2()
    {
        var sim = TestContent.NewScenario();
        var d = sim.World.Designs;
        int design = d.IdOf("fpv_strike_mk2");
        Story.Play(sim, 2, new() { ["wreck_at_veyl"] = "study_it" });
        Assert.Equal("study_it", Story.Chosen(sim, "wreck_at_veyl"));
        Assert.Equal(Fixed.One, sim.World.Flags.Value[sim.World.Flags.IdOf("wreck_studied")]);
        Assert.NotNull(Story.Fired(sim, "chips_in_wreck"));
        Assert.Equal(2, Story.Fired(sim, "chips_in_wreck")!.Day);
        // +0.10 effectiveness from studying (then the Monday decay had already happened on day 0).
        Assert.Equal(Fixed.Parse("0.9245"), d.Effectiveness[design]);
    }

    [Fact]
    public void ReturningTheWreckMeansNoChipsBeat()
    {
        var sim = TestContent.NewScenario();
        Story.Play(sim, 5, new() { ["wreck_at_veyl"] = "return_quietly" });
        Assert.Null(Story.Fired(sim, "chips_in_wreck"));
        Assert.True(sim.World.Factions.Standing[sim.World.Factions.IdOf("nationalists")] < Fixed.Zero);
    }

    [Fact]
    public void BuryingTheFindingTurnsDay9IntoACoverUp()
    {
        var buried = TestContent.NewScenario();
        Story.Play(buried, 9, new() { ["wreck_at_veyl"] = "study_it", ["chips_in_wreck"] = "bury" });
        Assert.NotNull(Story.Fired(buried, "story_breaks_coverup"));
        Assert.Null(Story.Fired(buried, "story_breaks"));

        var honest = TestContent.NewScenario();
        Story.Play(honest, 9, new() { ["wreck_at_veyl"] = "study_it", ["chips_in_wreck"] = "investigate" });
        Assert.NotNull(Story.Fired(honest, "story_breaks"));
        Assert.Null(Story.Fired(honest, "story_breaks_coverup"));
    }

    [Fact]
    public void ArcBeatsLandInTheHourTheyHappen()
    {
        var sim = TestContent.NewScenario();
        Story.Play(sim, 4);
        Assert.Equal((3, 11), (Story.Fired(sim, "survey_team_clash")!.Day, Story.Fired(sim, "survey_team_clash")!.Hour));
        Assert.Equal((4, 2), (Story.Fired(sim, "lights_out")!.Day, Story.Fired(sim, "lights_out")!.Hour));
        Assert.Equal((4, 6), (Story.Fired(sim, "deepfake")!.Day, Story.Fired(sim, "deepfake")!.Hour));
    }

    [Fact]
    public void Day10DecisionOffersFivePaths()
    {
        var sim = TestContent.NewScenario();
        Story.Play(sim, 10);
        var inst = Story.Fired(sim, "day10_decision");
        Assert.NotNull(inst);
        Assert.Equal(5, sim.Narrative.Def.Storylets[inst!.Storylet].Choices.Count);
        Assert.Contains(sim.World.Log.Entries, e => e.Kind == "headline");
    }

    [Theory]
    [InlineData("lights_first", "Kestria Keeps the Lights On, Loses the Sky")]
    [InlineData("fab_first", "The Weapons-or-Warmth Winter")]
    [InlineData("emergency", "The Emergency That Never Ended?")]
    [InlineData("strike_back", "Kestria Answers in Kind")]
    [InlineData("back_channel", "Peace, at a Price")]
    public void EveryDay10PathPlaysOutToTheEnd(string choice, string headline)
    {
        var sim = TestContent.NewScenario();
        sim.World.Politics.PoliticalCapital.Init(0, Fixed.FromInt(300)); // every path affordable
        Story.Play(sim, 90, new() { ["day10_decision"] = choice });
        Assert.True(sim.IsFinished);
        Assert.Equal(choice, Story.Chosen(sim, "day10_decision"));
        Assert.Contains(sim.World.Log.Entries, e => e.Kind == "headline" && e.Text == headline);
        var chronicle = ChronicleView.Build(sim);
        Assert.Contains(chronicle.Headlines, h => h.Text == headline);
        Assert.Equal(3, chronicle.Verdicts.Count);
    }

    [Fact]
    public void BackChannelLiftsTheExportControls()
    {
        var sim = TestContent.NewScenario();
        Story.Play(sim, 25, new() { ["day10_decision"] = "back_channel" });
        var w = sim.World;
        int magnets = Enumerable.Range(0, w.Imports.Count).Single(r => w.Imports.Good[r] == Veyl.Good(sim, "rare_earth_magnet") && w.Imports.Source[r] >= 0);
        Assert.False(w.Imports.Blocked[magnets]);
        Assert.Equal((int)SeedState.Live, w.Seeds.State[w.Seeds.IdOf("no_confidence")]);
    }

    [Fact]
    public void EmergencyPathSetsItsPrecedents()
    {
        var sim = TestContent.NewScenario();
        sim.World.Politics.PoliticalCapital.Init(0, Fixed.FromInt(300));
        Story.Play(sim, 12, new() { ["day10_decision"] = "emergency", ["lights_out"] = "homes_first", ["deepfake"] = "go_live" });
        var w = sim.World;
        foreach (var p in new[] { "emergency_declaration", "fuel_requisition", "internet_shutdown", "nationalization" })
            Assert.True(w.Precedents.Uses[w.Precedents.At(0, sim.Scenario.Society.Precedent(p))] >= 1, p);
        Assert.True(w.Corporations.Nationalized[w.Corporations.IdOf("corp_tessera")]);
    }

    [Fact]
    public void ForensicSweepOnTheBriefCardDefusesTheSeed()
    {
        var sim = TestContent.NewScenario();
        Story.Play(sim, 6, new() { ["odd_logins"] = "sweep" });
        Assert.Equal("sweep", Story.Chosen(sim, "odd_logins"));
        Assert.Equal((int)OperationState.Detected, sim.World.Operations.State[sim.World.Operations.IdOf("varan_eastern_grid")]);
        Assert.Null(Story.Fired(sim, "lights_out")); // no eastern blackout
        Assert.Equal(0, sim.World.Facilities.Interruptions[Veyl.Facility(sim, "tessera_fab_3")]);
    }

    [Fact]
    public void IgnoredBriefCardsTakeTheirDefault()
    {
        var sim = TestContent.NewScenario();
        sim.RunThrough(3); // nobody answers
        var inst = Story.Fired(sim, "odd_logins")!;
        Assert.False(inst.Pending);
        Assert.True(inst.ByDefault);
        Assert.Equal("ignore", Story.Chosen(sim, "odd_logins"));
        Assert.Equal(3, inst.ResolvedDay);
    }

    [Fact]
    public void UnavailableChoicesAreRefused()
    {
        var sim = TestContent.NewScenario();
        sim.World.Politics.PoliticalCapital.Init(0, Fixed.FromInt(10));
        sim.StepDay();
        var card = Story.Fired(sim, "odd_logins")!;
        Assert.False(sim.ChoiceAvailable(card, 0)); // the sweep needs 15
        sim.Orders.Enqueue(new ChooseStoryletOrder(0, card.Seq, 0));
        sim.StepDay();
        Assert.False(sim.AppliedOrders[^1].Outcome.Accepted);
        Assert.True(card.Pending);
    }

    // ---- The Director ----

    [Fact]
    public void DirectorRespectsWeeklyCaps()
    {
        var sim = TestContent.NewScenario();
        Story.Play(sim, 90);
        var instances = sim.World.Storylets.Instances;
        var generic = instances.Where(i => sim.Narrative.Def.Storylets[i.Storylet].Arc is null).ToList();
        for (int day = 0; day <= 90; day++)
        {
            var window = instances.Where(i => i.Day > day - 7 && i.Day <= day).ToList();
            // Generic picks happen only while the caps (counting every storylet in the window) allow them.
            foreach (var g in generic.Where(i => i.Day == day))
            {
                var before = window.Where(i => i != g && (i.Day < g.Day || i.Seq < g.Seq)).ToList();
                var tier = sim.Narrative.Def.Storylets[g.Storylet].Tier;
                Assert.True(before.Count(i => sim.Narrative.Def.Storylets[i.Storylet].Tier == tier) < (tier == Tier.Major ? 1 : 3),
                    $"day {day}: {sim.Narrative.Def.Storylets[g.Storylet].Id}");
            }
        }
        Assert.True(generic.Count > 0);
    }

    [Fact]
    public void TensionFollowsTheFormula()
    {
        var sim = TestContent.NewScenario();
        sim.StepDay();
        var w = sim.World;
        var n = sim.Balance.Narrative;
        // Day 0: no crisis (K = 0), rung 2, two arc beats fired (odd logins, wreck) → A = 40.
        var expected = n.TensionApproval * (Fixed.Hundred - w.Politics.Approval[0]) + n.TensionRung * 2 + n.TensionArcs * Fixed.FromInt(40);
        Assert.Equal(expected, w.Director.Tension[0]);
    }

    [Fact]
    public void CastingAvoidsOverusedCharactersAndHonoursOpinionFilters()
    {
        var sim = TestContent.NewScenario();
        var ctx = sim.ReadContext();
        var s = sim.Narrative.Def.Storylets[sim.Narrative.Def.Storylet("minister_resigns")];
        Assert.Null(DirectorPhase.Cast(ctx, s)); // nobody's opinion is below −40
        int brandt = sim.World.Characters.IdOf("aurel_brandt");
        sim.World.Characters.Remember(brandt, 0, Fixed.FromInt(-70), Fixed.FromInt(50));
        var cast = DirectorPhase.Cast(sim.ReadContext(), s);
        Assert.NotNull(cast);
        Assert.Equal(brandt, cast![0]);
    }

    [Fact]
    public void OpinionsFadeWithTheSpecTimeConstants()
    {
        var sim = TestContent.NewScenario();
        var ch = sim.World.Characters;
        int mara = ch.IdOf("mara_voss");
        ch.Remember(mara, 0, Fixed.FromInt(-20), Fixed.FromInt(50)); // minor: τ = 90
        ch.Remember(mara, 0, Fixed.FromInt(60), Fixed.FromInt(50));  // grave: τ = 730
        var op = ch.Opinion(mara, 90, Fixed.FromInt(730), Fixed.FromInt(90));
        var expected = -20 * Math.Exp(-1) + 60 * Math.Exp(-90.0 / 730);
        Assert.InRange(op.ToDoubleForUi(), expected - 0.01, expected + 0.01);
    }

    [Fact]
    public void SeedsRipenAndPayOff()
    {
        // Fab first plants mara_protest: at least 30 days, then ~30% a month. Over several seeds some pay off before day 90.
        int paid = 0;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var sim = TestContent.NewScenario(seed);
            Story.Play(sim, 90, new() { ["day10_decision"] = "fab_first", ["lights_out"] = "fab_first" });
            var w = sim.World;
            var state = (SeedState)w.Seeds.State[w.Seeds.IdOf("mara_protest")];
            Assert.NotEqual(SeedState.Dormant, state);
            if (state == SeedState.PaidOff)
            {
                paid++;
                Assert.True(Story.Fired(sim, "mara_leads_protest")!.Day >= Story.Fired(sim, "day10_decision")!.Day + 30);
            }
        }
        Assert.True(paid >= 1, $"{paid} of 6 campaigns paid off the seed");
    }

    // ---- Chronicle and determinism ----

    [Fact]
    public void ChronicleHasVerdictsAndDeclassifiedTruths()
    {
        var sim = TestContent.NewScenario();
        Story.Play(sim, 90);
        var c = ChronicleView.Build(sim);
        Assert.Equal(5, c.Scores.Count);
        Assert.All(c.Scores.Values, v => Assert.InRange(v, 0, 100));
        Assert.Equal(3, c.Verdicts.Count);
        Assert.Contains(c.Declassified, d => d.Contains("red line"));
        Assert.Contains(c.Declassified, d => d.Contains("hacker collective"));
        Assert.Contains(c.Declassified, d => d.Contains("Aurel Brandt"));
        Assert.NotEmpty(c.Timeline);
    }

    [Fact]
    public void SameSeedAndSameChoicesGiveTheSameResult()
    {
        static ulong Run()
        {
            var sim = TestContent.NewScenario(99);
            Story.Play(sim, 90, new() { ["wreck_at_veyl"] = "televise", ["day10_decision"] = "strike_back" }, AutoMode.Random);
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void RandomCampaignsRunCleanly()
    {
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var sim = TestContent.NewScenario(seed);
            Story.Play(sim, 90, fallback: AutoMode.Random);
            Assert.True(sim.IsFinished);
            Assert.NotNull(Story.Chosen(sim, "day10_decision"));
        }
    }
}
