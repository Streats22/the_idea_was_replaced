using Delvework.Core.Content;
using Delvework.Core.Glyph;
using Delvework.Core.Progress;
using Delvework.Core.Sim;

namespace Delvework.Core.Tests;

public class LessonTests
{
    private static readonly ContentPack Content = Shared.Content;

    public static TheoryData<string> LessonIds()
    {
        var data = new TheoryData<string>();
        foreach (var l in Shared.Content.Lessons) data.Add(l.Id);
        return data;
    }

    private static LessonDef Lesson(string id) => Content.Lessons.First(l => l.Id == id);

    private static ChallengeResult Attempt(LessonDef lesson, string code, ulong seedOffset = 0)
    {
        var sources = lesson.Challenge.Party.ToDictionary(id => id, _ => code);
        var setup = Challenge.Setup(Content, lesson, sources) with { Seed = lesson.Challenge.Seed + seedOffset };
        var (world, _) = Delve.Run(Content, setup);
        return Challenge.Evaluate(lesson, sources, world);
    }

    [Fact]
    public void TheCodexTeachesEightLessonsInLadderOrder()
    {
        Assert.Equal(["commands", "loops", "decisions", "variables", "functions", "lists", "events", "dicts"], Content.Lessons.Select(l => l.Id));
        for (var i = 0; i < Content.Lessons.Count; i++) Assert.Equal(i + 1, Content.Lessons[i].Tier);
        Assert.All(Content.Lessons, l => Assert.InRange(l.Pages.Count, 2, 6));
        Assert.All(Content.Lessons, l => Assert.Contains(l.Pages.SelectMany(p => p.Blocks), b => b.Code is { Count: > 0 }));
    }

    [Theory]
    [MemberData(nameof(LessonIds))]
    public void SolutionPassesOnEverySeed(string id)
    {
        var lesson = Lesson(id);
        for (ulong s = 0; s < 8; s++)
        {
            var r = Attempt(lesson, lesson.Challenge.Solution, s);
            Assert.True(r.Passed, $"seed +{s}: " + string.Join("; ", r.Checks.Select(c => (c.Ok ? "ok " : "NO ") + c.Text)));
        }
    }

    [Theory]
    [MemberData(nameof(LessonIds))]
    public void StarterCompilesButDoesNotPass(string id)
    {
        var lesson = Lesson(id);
        GlyphCompiler.Compile(lesson.Challenge.Starter, GolemApi.Environment, new CompileOptions { Tier = lesson.Tier });
        Assert.False(Attempt(lesson, lesson.Challenge.Starter).Passed);
    }

    [Theory]
    [MemberData(nameof(LessonIds))]
    public void SolutionNeedsTheLessonsOwnFeature(string id)
    {
        var lesson = Lesson(id);
        if (lesson.Tier == 1) return;
        var e = Assert.Throws<GlyphError>(() =>
            GlyphCompiler.Compile(lesson.Challenge.Solution, GolemApi.Environment, new CompileOptions { Tier = lesson.Tier - 1 }));
        Assert.Equal(GlyphErrorKind.Capability, e.Kind);
    }

    [Theory]
    [MemberData(nameof(LessonIds))]
    public void EveryCodeSampleOnTheLessonPagesParses(string id)
    {
        foreach (var block in Lesson(id).Pages.SelectMany(p => p.Blocks).Where(b => b.Code is not null))
        {
            var src = string.Join('\n', block.Code!);
            var ex = Record.Exception(() => Parser.Parse(src));
            Assert.True(ex is null, $"{id}: {src}\n{ex?.Message}");
        }
    }

    [Fact]
    public void ChallengeGoalsAreListedBeforeADelve()
    {
        var goals = Challenge.Goals(Lesson("loops"));
        Assert.Equal(["Reach the stairs", "Use at most 5 lines of code", "Use a while loop"], goals.Select(g => g.Text));
        Assert.All(goals, g => Assert.False(g.Ok));
    }

    [Fact]
    public void LineLimitIgnoresBlanksAndComments()
    {
        Assert.Equal(2, Challenge.CountLines("# hi\n\nwhile True:\n    # step\n    move(East)\n"));
        var tooLong = string.Join('\n', Enumerable.Repeat("move(East)", 30)) + "\nwhile True:\n    move(South)";
        var r = Attempt(Lesson("loops"), tooLong);
        Assert.Contains(r.Checks, c => c.Text.StartsWith("Use at most 5 lines", StringComparison.Ordinal) && !c.Ok);
    }

    [Theory]
    [InlineData("move(East)", "call")]
    [InlineData("while True:\n    pass", "while")]
    [InlineData("if True:\n    pass\nelse:\n    pass", "if")]
    [InlineData("x = 1 < 2", "compare")]
    [InlineData("x = 1\nx += 1", "assign")]
    [InlineData("def f():\n    return 1", "def")]
    [InlineData("def f():\n    return 1", "return")]
    [InlineData("for i in range(2):\n    pass", "for")]
    [InlineData("xs = [1]\nprint(xs[0])", "list")]
    [InlineData("xs = [1]\nprint(xs[0])", "index")]
    [InlineData("on see(e):\n    pass", "on")]
    [InlineData("d = {\"a\": 1}", "dict")]
    public void FeatureDetection(string source, string feature)
    {
        Assert.Contains(feature, Challenge.FeaturesUsed(source));
    }

    [Fact]
    public void FeaturesInCommentsDoNotCount()
    {
        Assert.DoesNotContain("while", Challenge.FeaturesUsed("# while True:\nmove(East)"));
        Assert.Empty(Challenge.FeaturesUsed("while (:"));
    }

    [Fact]
    public void LessonsRunWithStandardGolems()
    {
        var setup = Challenge.Setup(Content, Lesson("events"), new Dictionary<string, string> { ["warden"] = "heal()" });
        Assert.Equal(Tiers.Events, setup.Tier);
        var world = Delve.Create(Content, setup);
        Assert.Equal(GolemState.Halted, world.Golems[0].State);
        Assert.Equal(Content.Chassis["warden"].Hp, world.Golems[0].MaxHp);
    }
}

public class ProgressionTests
{
    private static readonly ContentPack Content = Shared.Content;

    /// <summary>A profile with gold, lessons learned and (for rich profiles) as much of every material as gold.</summary>
    private static Progression Fresh(int gold = 0, int lessons = 0, int? stock = null)
    {
        var p = new Progression(Content, new Profile { Gold = gold });
        foreach (var l in Content.Lessons.Take(lessons).Skip(1)) p.Profile.Learned.Add(l.Id);
        foreach (var id in Resources.All) p.Profile.Add(id, stock ?? (gold >= 1000 ? gold : 0));
        return p;
    }

    private static SkillNode Node(string id) => Content.Skills.First(n => n.Id == id);

    [Fact]
    public void CommandsAreKnownFromTheStartAndTheRestIsBoughtInOrder()
    {
        var p = Fresh(gold: 100);
        Assert.True(p.IsLearned(Content.Lessons[0]));
        Assert.Equal(1, p.KnownTier);
        Assert.Equal("loops", p.NextToLearn!.Id);
        Assert.Equal(NodeStatus.Available, p.LearnStatus(Content.Lessons[1]));
        Assert.Equal(NodeStatus.Locked, p.LearnStatus(Content.Lessons[2]));
        Assert.False(p.Learn(Content.Lessons[2]));
        Assert.True(p.Learn(Content.Lessons[1]));
        Assert.Equal(100 - Content.Lessons[1].Cost, p.Profile.Gold);
        Assert.Equal(2, p.KnownTier);
        Assert.Equal(NodeStatus.Owned, p.LearnStatus(Content.Lessons[1]));
        Assert.False(p.Learn(Content.Lessons[1]));
    }

    [Fact]
    public void LearningCostsGold()
    {
        var p = Fresh(gold: 5);
        Assert.Equal(NodeStatus.TooExpensive, p.LearnStatus(Content.Lessons[1]));
        Assert.False(p.Learn(Content.Lessons[1]));
        Assert.Equal(5, p.Profile.Gold);
        Assert.All(Content.Lessons.Skip(1), l => Assert.True(l.Cost > 0, l.Id));
        Assert.Equal(0, Content.Lessons[0].Cost);
    }

    [Fact]
    public void ChallengesAreOptionalPracticeForLearnedFeatures()
    {
        var p = Fresh(lessons: 2);
        Assert.True(p.IsOpen(Content.Lessons[1]));
        Assert.False(p.IsOpen(Content.Lessons[2]));
        Assert.Equal("commands", p.NextChallenge!.Id);
        p.Complete(Content.Lessons[0]);
        Assert.Equal("loops", p.NextChallenge!.Id);
        Assert.Equal(2, p.KnownTier);
    }
    [Fact]
    public void BuyingSpendsGoldAndNeedsRequirements()
    {
        var p = Fresh(gold: 1000, lessons: 3);
        Assert.Equal(NodeStatus.Locked, p.Status(Node("v_market")));
        Assert.Equal("Needs Water Mill", p.LockReason(Node("v_market")));
        Assert.True(p.Buy(Node("v_mill")));
        Assert.Equal(975, p.Profile.Gold);
        Assert.Equal(NodeStatus.Owned, p.Status(Node("v_mill")));
        Assert.False(p.Buy(Node("v_mill")));
        Assert.Equal(NodeStatus.Available, p.Status(Node("v_market")));
    }

    [Fact]
    public void NodesCanBeTooExpensive()
    {
        var p = Fresh(gold: 10, lessons: 3);
        Assert.Equal(NodeStatus.TooExpensive, p.Status(Node("v_mill")));
        Assert.False(p.Buy(Node("v_mill")));
        Assert.Equal(10, p.Profile.Gold);
    }

    [Fact]
    public void TreesOpenWithTheirBuilding()
    {
        var p = Fresh(gold: 1000, lessons: 4);
        Assert.False(p.IsTreeOpen(SkillTree.Equipment));
        Assert.Equal("Build Rebuild the Forge in the Village first", p.LockReason(Node("e_lantern")));
        p.Buy(Node("v_forge"));
        Assert.True(p.IsTreeOpen(SkillTree.Equipment));
        Assert.Equal(NodeStatus.Available, p.Status(Node("e_lantern")));
        Assert.False(p.IsTreeOpen(SkillTree.Arcana));
        p.Buy(Node("v_tower"));
        Assert.True(p.IsTreeOpen(SkillTree.Arcana));
    }

    [Fact]
    public void SomeNodesWaitForLessons()
    {
        var p = Fresh(gold: 1000, lessons: 2);
        Assert.Equal("Learn Decisions at the Library first", p.LockReason(Node("v_forge")));
        p.Profile.Learned.Add(Content.Lessons[2].Id);
        Assert.Null(p.LockReason(Node("v_forge")));
    }

    [Fact]
    public void LoadoutAddsUpOwnedNodes()
    {
        var p = Fresh(gold: 10_000, lessons: 8);
        foreach (var id in new[] { "v_forge", "e_plating", "e_lantern", "e_core", "v_tower", "a_well", "a_heal", "a_bolt" }) Assert.True(p.Buy(Node(id)), id);
        var l = p.Loadout();
        Assert.Equal(6, l.Hp);
        Assert.Equal(1, l.Sight);
        Assert.Equal(20, l.Budget);
        Assert.Equal(8, l.Mana);
        Assert.Equal(["heal", "bolt"], l.Spells);
    }

    [Fact]
    public void GuildHallGrowsTheParty()
    {
        var p = Fresh(gold: 10_000, lessons: 8);
        Assert.Equal(["warden"], p.Party);
        p.Buy(Node("v_mill"));
        p.Buy(Node("v_guild"));
        Assert.Equal(["warden", "seeker"], p.Party);
        p.Buy(Node("v_guild2"));
        Assert.Equal(["warden", "seeker", "striker"], p.Party);
    }

    [Fact]
    public void VillageBonusesChangeDelveRewards()
    {
        var p = Fresh(gold: 10_000, lessons: 8);
        var outcome = new Outcome(OutcomeKind.Success, 100, 40, [], "");
        Assert.Equal(40, p.SiteReward(outcome).Total);
        foreach (var id in new[] { "v_mill", "v_market", "v_market2" }) p.Buy(Node(id));
        var r = p.SiteReward(outcome);
        Assert.Equal(40, r.Loot);
        Assert.Equal(10, r.Bonus);
        Assert.Equal(3, r.Income);
        Assert.Equal(53, r.Total);
    }

    [Fact]
    public void LessonRewardsPayOnlyOnce()
    {
        var p = Fresh();
        var lesson = Content.Lessons[0];
        var pass = new ChallengeResult(true, []);
        var outcome = new Outcome(OutcomeKind.Success, 30, 0, [], "");
        Assert.Equal(lesson.Reward, p.LessonReward(lesson, pass, outcome).Total);
        Assert.Equal(0, p.LessonReward(lesson, new ChallengeResult(false, []), outcome).Total);
        p.Complete(lesson);
        Assert.Equal(0, p.LessonReward(lesson, pass, outcome).Total);
    }

    [Fact]
    public void ApplyingARewardTracksStats()
    {
        var p = Fresh();
        p.Apply(new DelveReward(10, 1, 3, 15));
        Assert.Equal(29, p.Profile.Gold);
        Assert.Equal(29, p.Profile.GoldEarned);
        Assert.Equal(1, p.Profile.Delves);
    }

    [Fact]
    public void SitesOpenWithLessons()
    {
        Assert.Equal(["entrance"], Fresh().OpenSites.Select(s => s.Id));
        Assert.Equal(["entrance"], Fresh(lessons: 2).OpenSites.Select(s => s.Id));
        Assert.Equal(["entrance", "old_mines", "mines"], Fresh(lessons: 3).OpenSites.Select(s => s.Id));
        Assert.Contains("deep_mines", Fresh(lessons: 6).OpenSites.Select(s => s.Id));
    }

    [Fact]
    public void FreeDelvesUseKnownTierLoadoutAndParty()
    {
        var p = Fresh(gold: 1000, lessons: 4);
        p.Buy(Node("v_forge"));
        p.Buy(Node("e_plating"));
        p.Profile.Programs[Profile.DelveSlot("warden")] = Content.Lessons[3].Challenge.Solution;
        var setup = p.SiteSetup(Content.Sites.First(s => s.Id == "mines"), 5);
        Assert.Equal(4, setup.Tier);
        Assert.Equal(6, setup.Loadout!.Hp);
        Assert.Single(setup.Party);
        var (world, outcome) = Delve.Run(Content, setup);
        Assert.Null(world.Golems[0].Error);
        Assert.NotEqual(OutcomeKind.Stalled, outcome.Kind);
    }

    [Fact]
    public void DelveCodeFallsBackToTheLatestLessonSolution()
    {
        var p = Fresh(lessons: 3);
        Assert.Equal(Content.Programs[Progression.FirstProgram], p.DelveProgram("warden"));
        p.Complete(Content.Lessons[0]);
        p.Complete(Content.Lessons[1]);
        Assert.Equal(Content.Programs[Progression.FirstProgram], p.DelveProgram("warden"));
        p.Complete(Content.Lessons[2]);
        Assert.Equal(Content.Lessons[2].Challenge.Solution, p.DelveProgram("warden"));
        p.Profile.Programs[Profile.LessonSlot("decisions", "warden")] = "explore()";
        Assert.Equal("explore()", p.DelveProgram("warden"));
        p.Profile.Programs[Profile.DelveSlot("warden")] = "wait()";
        Assert.Equal("wait()", p.DelveProgram("warden"));
        Assert.Equal(Content.Programs[Progression.FirstProgram], Fresh().DelveProgram("warden"));
    }

    [Fact]
    public void LessonSourcesStartFromTheStarter()
    {
        var p = Fresh();
        var lesson = Content.Lessons[0];
        Assert.Equal(lesson.Challenge.Starter, p.LessonSources(lesson)["warden"]);
        p.Profile.Programs[Profile.LessonSlot(lesson.Id, "warden")] = "move(East)";
        Assert.Equal("move(East)", p.LessonSources(lesson)["warden"]);
    }

    [Fact]
    public void BuildingsLevelUpWithTheirNodes()
    {
        var p = Fresh(gold: 10_000, lessons: 8);
        foreach (var id in new[] { "v_forge", "v_forge2", "v_mill" }) p.Buy(Node(id));
        var b = p.Buildings();
        Assert.Equal(2, b["forge"]);
        Assert.Equal(1, b["mill"]);
        Assert.False(b.ContainsKey("tower"));
    }

    [Fact]
    public void ProfileRoundTripsThroughJson()
    {
        var profile = new Profile { Gold = 42, Seed = 9, Site = "mines" };
        profile.Completed.Add("commands");
        profile.Owned.Add("v_mill");
        profile.Learned.Add("loops");
        profile.Programs["delve/warden"] = "explore()";
        var back = Profile.FromJson(profile.ToJson());
        Assert.Equal(42, back.Gold);
        Assert.Equal(["commands"], back.Completed);
        Assert.Equal(["v_mill"], back.Owned);
        Assert.Equal(["loops"], back.Learned);
        Assert.Equal("explore()", back.Programs["delve/warden"]);
        Assert.Equal(9UL, back.Seed);
    }

    [Fact]
    public void EveryNodeCanEventuallyBeBought()
    {
        var p = Fresh(gold: 1_000_000, lessons: Content.Lessons.Count);
        for (var round = 0; round < Content.Skills.Count; round++)
        {
            foreach (var n in Content.Skills) p.Buy(n);
        }
        Assert.All(Content.Skills, n => Assert.True(p.Owns(n.Id), n.Id));
    }

    [Fact]
    public void TheFirstProgramEarnsGoldAtTheEntranceWithPlainCommands()
    {
        var p = Fresh();
        var setup = p.SiteSetup(Content.Sites[0], 1);
        Assert.Equal(1, setup.Tier);
        var (world, outcome) = Delve.Run(Content, setup);
        Assert.Null(world.Golems[0].Error);
        Assert.True(outcome.Loot >= 5, outcome.Summary);
        Assert.True(outcome.Loot * 2 >= Content.Lessons[1].Cost, "two first delves should buy Loops");
    }

    [Fact]
    public void OldProfilesKeepTheFeaturesTheyUnlockedByLessons()
    {
        var back = Profile.FromJson("{\"version\": 1, \"completed\": [\"commands\", \"loops\"]}");
        Assert.Equal(["commands", "loops"], back.Learned);
        Assert.Equal(Profile.CurrentVersion, back.Version);
        Assert.Equal(2, new Progression(Content, back).KnownTier);
    }

    [Fact]
    public void BuildingsCostMaterialsToo()
    {
        var p = Fresh(gold: 1000, lessons: 3, stock: 0);
        var forge = Node("v_forge");
        Assert.Equal(NodeStatus.TooExpensive, p.Status(forge));
        Assert.Equal(new Dictionary<string, int> { ["stone"] = 15, ["wood"] = 10 }, p.Missing(forge));
        p.Profile.Add("stone", 20);
        p.Profile.Add("wood", 10);
        Assert.Empty(p.Missing(forge));
        Assert.True(p.Buy(forge));
        Assert.Equal(1000 - forge.Cost, p.Profile.Gold);
        Assert.Equal(5, p.Profile.Amount("stone"));
        Assert.Equal(0, p.Profile.Amount("wood"));
        Assert.False(p.Profile.Stock.ContainsKey("wood"));
    }

    [Fact]
    public void ProductionBuildingsDeliverAfterEveryDelve()
    {
        var p = Fresh(gold: 1000);
        Assert.Empty(p.Production());
        Assert.True(p.Buy(Node("v_lumber")));
        Assert.True(p.Buy(Node("v_farm")));
        Assert.Equal(new Dictionary<string, int> { ["wood"] = 4, ["wheat"] = 3 }, p.Production());
        var before = p.Profile.Amount("wood");
        var outcome = new Outcome(OutcomeKind.Success, 100, 5, [], "", new Dictionary<string, int> { ["wood"] = 1, ["ore"] = 2 });
        var reward = p.SiteReward(outcome);
        Assert.Equal(5, reward.Goods!["wood"]);
        Assert.Equal(2, reward.Goods["ore"]);
        p.Apply(reward);
        Assert.Equal(before + 5, p.Profile.Amount("wood"));
    }

    [Fact]
    public void TheFirstProgramMinesEveryKindOfMaterial()
    {
        var setup = Fresh().SiteSetup(Content.Sites[0], 1);
        var (world, outcome) = Delve.Run(Content, setup);
        Assert.Null(world.Golems[0].Error);
        Assert.Equal(OutcomeKind.Success, outcome.Kind);
        Assert.NotNull(outcome.Goods);
        Assert.Equal(2, outcome.Goods!["stone"]);
        Assert.Equal(1, outcome.Goods["ore"]);
        Assert.Equal(1, outcome.Goods["wood"]);
        Assert.Contains("Brought home: 2 stone, 1 iron ore, 1 wood", outcome.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void StockRoundTripsAndOldProfilesStartEmpty()
    {
        var profile = new Profile();
        profile.Add("iron", 7);
        profile.Add(new Dictionary<string, int> { ["bread"] = 2, ["iron"] = 1 });
        var back = Profile.FromJson(profile.ToJson());
        Assert.Equal(8, back.Amount("iron"));
        Assert.Equal(2, back.Amount("bread"));
        Assert.Empty(Profile.FromJson("{\"version\": 2, \"gold\": 5}").Stock);
    }

    [Fact]
    public void WorkshopsRunAfterBuildingThem()
    {
        var p = Fresh(gold: 1000, lessons: 3);
        Assert.Empty(p.RunWorkshops());
        p.Buy(Node("v_lumber"));
        p.Buy(Node("v_quarry"));
        p.Buy(Node("v_smelter"));
        var ore = p.Profile.Amount("ore");
        var iron = p.Profile.Amount("iron");
        var results = p.RunWorkshops();
        var smelt = Assert.Single(results);
        Assert.Null(smelt.Error);
        Assert.Equal(4, smelt.Made["iron"]);
        Assert.Equal(ore - 4, p.Profile.Amount("ore"));
        Assert.Equal(iron + 4, p.Profile.Amount("iron"));
        Assert.Equal(1, p.Profile.Shifts);
        p.Profile.Programs[Profile.WorkshopSlot("smelter")] = "while ore() > 0:\n    if heat() < 5:\n        stoke()\n    smelt()\n";
        Assert.True(p.TryShift(p.Workshop("smelter")!).Made["iron"] > 4);
    }

    [Fact]
    public void TreePositionsDoNotOverlap()
    {
        foreach (var tree in Content.Skills.GroupBy(n => n.Tree))
        {
            var spots = tree.Select(n => (n.Col, n.Row)).ToList();
            Assert.Equal(spots.Count, spots.Distinct().Count());
        }
    }
}
