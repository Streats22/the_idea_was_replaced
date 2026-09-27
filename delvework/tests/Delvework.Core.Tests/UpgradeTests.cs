using Delvework.Core.Content;
using Delvework.Core.Dungeon;
using Delvework.Core.Progress;
using Delvework.Core.Sim;
using Delvework.Core.Village;

namespace Delvework.Core.Tests;

public class FarmTests
{
    private static readonly ContentPack Content = Shared.Content;
    private static WorkshopDef FarmDef => Content.Workshops.First(w => w.Id == Farm.Id);

    private static WorkshopResult Run(string src) => Workshops.Run(FarmDef, src, new Dictionary<string, int>());

    private static List<string> Prints(WorkshopResult r) =>
        r.Log.Where(l => l.Contains("print: ", StringComparison.Ordinal)).Select(l => l[(l.IndexOf("print: ", StringComparison.Ordinal) + 7)..]).ToList();

    [Fact]
    public void TheStarterGrowsThreeWheat()
    {
        var r = Run(FarmDef.Starter);
        Assert.Null(r.Error);
        Assert.Equal(3, r.Made[Resources.Wheat]);
        Assert.Empty(r.Used);
    }

    [Fact]
    public void WheatRipensAfterEightTicksAndAnEarlyHarvestLosesIt()
    {
        var r = Run("plant(Wheat)\nwait(6)\nprint(can_harvest())\nprint(harvest(), growing())");
        Assert.Equal(["False", "False False"], Prints(r));
        Assert.False(r.Made.ContainsKey(Resources.Wheat));
        Assert.Equal(1, r.Found!["farm:too_early"]);
        var ok = Run("plant(Wheat)\nwait(7)\nprint(can_harvest())\nharvest()");
        Assert.Equal(["True"], Prints(ok));
        Assert.Equal(1, ok.Made[Resources.Wheat]);
    }

    [Fact]
    public void WateringDoublesGrowth()
    {
        Assert.Equal(["True"], Prints(Run("plant(Wheat)\nwater()\nwait(3)\nprint(can_harvest())")));
        Assert.Equal(["False"], Prints(Run("plant(Wheat)\nwait(4)\nprint(can_harvest())")));
    }

    [Fact]
    public void TheFieldWrapsAround()
    {
        var r = Run("move(West)\nmove(North)\nprint(x(), y(), width(), height())\nfor i in range(6):\n    move(East)\nprint(x())");
        Assert.Equal([$"{Farm.Width - 1} {Farm.Height - 1} {Farm.Width} {Farm.Height}", $"{Farm.Width - 1}"], Prints(r));
    }

    [Fact]
    public void PumpkinPatchesPayTheSquareUpToACap()
    {
        Assert.Equal([1, 4, 9, 12, 18], new[] { 1, 2, 3, 4, 6 }.Select(Farm.Patch));
        // Three pumpkins in a row; the field is the same every time, and the third one rots.
        var r = Run("for i in range(3):\n    plant(Pumpkin)\n    move(East)\nwait(20)\nmove(West)\nprint(rotten())\nmove(West)\nprint(rotten())\nharvest()");
        Assert.Equal(["True", "False"], Prints(r));
        Assert.Equal(1, r.Frames[^1].State["rotten"]);
        Assert.Equal(Farm.Patch(2), r.Made[Resources.Pumpkin]);
        Assert.Equal(1, r.Found!["farm:rotten"]);
    }

    [Fact]
    public void ItsTheSameFieldEveryTime()
    {
        var src = Content.Commissions.First(c => c.Id == "pumpkin_patch").Solution;
        var a = Run(src);
        var b = Run(src);
        Assert.Equal(a.Made, b.Made);
        Assert.Equal(a.Frames.Select(f => f.Tiles!.Sum()), b.Frames.Select(f => f.Tiles!.Sum()));
        Assert.True(a.Frames[^1].State["rotten"] > 0, "some pumpkins should rot, so replanting matters");
    }

    [Fact]
    public void FramesCarryTheField()
    {
        var r = Run("plant(Pumpkin)\nwater()\nmove(East)\nplant(Wheat)");
        var tiles = r.Frames[^1].Tiles!;
        Assert.Equal(Farm.Width * Farm.Height, tiles.Count);
        var pumpkin = Farm.Decode(tiles[0]);
        Assert.Equal(Farm.Crop.Pumpkin, pumpkin.Crop);
        Assert.True(pumpkin.Water > 0);
        Assert.Equal(Farm.Crop.Wheat, Farm.Decode(tiles[1]).Crop);
        Assert.Equal(Farm.Crop.None, Farm.Decode(tiles[2]).Crop);
        Assert.Equal(pumpkin, Farm.Decode(pumpkin.Encode()));
        Assert.Null(Workshops.Run(Content.Workshops.First(w => w.Id == Smelter.Id), "stoke()", new Dictionary<string, int>()).Frames[^1].Tiles);
    }

    [Fact]
    public void TheFarmWorksBeforeTheBakerySoItsWheatIsBakedTheSameDay()
    {
        var p = new Progression(Content, new Profile { Gold = 1000, Stock = new() { ["wood"] = 100, ["stone"] = 100 } });
        for (var i = 0; i < 3; i++) p.Profile.Learned.Add(Content.Lessons[i + 1].Id);
        foreach (var id in new[] { "v_lumber", "v_farm", "v_bakery" }) Assert.True(p.Buy(p.Node(id)!), id);
        p.Profile.Programs[Profile.WorkshopSlot("bakery")] = "light()\nknead()\nbake()\nwait(4)\ntake_out()";
        var results = p.RunWorkshops();
        Assert.Equal(["farm", "bakery"], results.Select(r => r.Def.Id));
        Assert.Equal(1, results[1].Made[Resources.Bread]);
    }
}

public class CommissionTests
{
    private static readonly ContentPack Content = Shared.Content;

    private static CommissionDef C(string id) => Content.Commissions.First(c => c.Id == id);

    /// <summary>First Pour in 9 ticks and 9 lines: stokes again halfway and wastes some heat.</summary>
    private const string Slow = "stoke()\nstoke()\nsmelt()\nsmelt()\nsmelt()\nstoke()\nsmelt()\nsmelt()\nsmelt()\n";

    [Fact]
    public void EveryReferenceSolutionEarnsBothGoldMedals()
    {
        Assert.Equal(6, Content.Commissions.Count);
        foreach (var c in Content.Commissions)
        {
            var s = Commissions.Score(Content, c, c.Solution);
            Assert.True(s.Passed, s.Summary);
            Assert.Equal((Medal.Gold, Medal.Gold), (s.TicksMedal, s.LinesMedal));
        }
    }

    [Fact]
    public void TheWorkshopStartersDoNotFinishACommission()
    {
        foreach (var c in Content.Commissions)
        {
            var starter = Content.Workshops.First(w => w.Id == c.Workshop).Starter;
            Assert.False(Commissions.Score(Content, c, starter).Passed, c.Id);
        }
    }

    [Fact]
    public void MedalsFollowThePar()
    {
        int[] par = [10, 20];
        Assert.Equal(Medal.Gold, Commissions.MedalFor(10, par));
        Assert.Equal(Medal.Silver, Commissions.MedalFor(11, par));
        Assert.Equal(Medal.Silver, Commissions.MedalFor(20, par));
        Assert.Equal(Medal.Bronze, Commissions.MedalFor(21, par));
    }

    [Fact]
    public void ScoresCountTicksToTheGoalAndLinesWithoutComments()
    {
        var s = Commissions.Score(Content, C("first_pour"), "# warm up\nstoke()\nstoke()\n\nwhile smelt():\n    pass\n");
        Assert.True(s.Passed);
        Assert.Equal(8, s.Ticks);
        Assert.Equal(4, s.Lines);
        var slow = Commissions.Score(Content, C("first_pour"), Slow);
        Assert.True(slow.Passed);
        Assert.Equal(Medal.Silver, slow.TicksMedal);
        Assert.Equal(Medal.Bronze, slow.LinesMedal);
        var broken = Commissions.Score(Content, C("first_pour"), "stoke(");
        Assert.False(broken.Passed);
        Assert.Contains("error", broken.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void SubmittingPaysOnceAndKeepsTheBest()
    {
        var p = new Progression(Content, new Profile { Learned = [.. Content.Lessons.Select(l => l.Id)] });
        Assert.False(new Progression(Content, new Profile()).TryCommission(C("first_pour"), C("first_pour").Solution).Passed);
        var c = C("first_pour");
        var (s1, paid1) = p.SubmitCommission(c, Slow);
        Assert.True(s1.Passed);
        Assert.Equal(c.Reward, paid1);
        Assert.Equal(c.Reward["iron"], p.Profile.Amount("iron"));
        var (_, paid2) = p.SubmitCommission(c, Slow);
        Assert.Empty(paid2);
        var (_, paid3) = p.SubmitCommission(c, c.Solution);
        Assert.Equal(2 * c.GoldMedalBonus, paid3[Resources.Gold]);
        var best = p.Best(c)!;
        Assert.Equal((8, 4, Medal.Gold, Medal.Gold), (best.Ticks, best.Lines, best.TicksMedal, best.LinesMedal));
        Assert.Empty(p.SubmitCommission(c, c.Solution).Paid);
        Assert.Empty(p.SubmitCommission(c, "stoke()").Paid);
        var back = Profile.FromJson(p.Profile.ToJson());
        Assert.Equal(Medal.Gold, back.Commissions[c.Id].TicksMedal);
    }

    [Fact]
    public void CommissionsOpenWithTheirWorkshop()
    {
        var p = new Progression(Content, new Profile { Gold = 1000, Stock = new() { ["wood"] = 100, ["stone"] = 100 } });
        Assert.DoesNotContain(Content.Commissions, p.IsOpen);
        p.Profile.Learned.Add(Content.Lessons[1].Id);
        foreach (var id in new[] { "v_lumber", "v_quarry", "v_smelter" }) Assert.True(p.Buy(p.Node(id)!), id);
        Assert.Equal(["first_pour", "lean_fire"], Content.Commissions.Where(p.IsOpen).Select(c => c.Id));
    }
}

public class AlmanacTests
{
    private static readonly ContentPack Content = Shared.Content;

    [Fact]
    public void EveryMonsterHasThreeEntriesAndLore()
    {
        var entries = Almanac.Entries(Content);
        Assert.Equal(entries.Count, entries.Select(e => e.Id).Distinct().Count());
        Assert.Equal(3 * Content.Monsters.Count, entries.Count(e => e.Category == Almanac.MonsterCategory));
        Assert.All(Content.Monsters.Values, m => Assert.False(string.IsNullOrWhiteSpace(m.Lore) || string.IsNullOrWhiteSpace(m.Weakness), m.Id));
        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Text), e.Id));
    }

    [Fact]
    public void DiscoveringPaysOnceAndIsReportedOnce()
    {
        var p = new Progression(Content, new Profile());
        var fresh = p.Discover(new Dictionary<string, int> { ["trap:snare"] = 1, ["seen:slime"] = 2 });
        Assert.Equal(["monster:slime:seen", "trap_snare"], fresh.Select(e => e.Id).Order(StringComparer.Ordinal));
        Assert.Equal(fresh.Sum(e => e.Reward), p.Profile.Gold);
        Assert.Equal(2, p.TakeFresh().Count);
        Assert.Empty(p.TakeFresh());
        Assert.Empty(p.Discover(new Dictionary<string, int> { ["trap:snare"] = 1 }));
        Assert.Equal(2, p.Profile.Found["trap:snare"]);
        Assert.Equal(2, p.FoundCount);
    }

    [Fact]
    public void MonstersAreSeenThenStudiedThenMastered()
    {
        var profile = new Profile();
        var p = new Progression(Content, profile);
        Assert.Equal(MonsterKnowledge.Unknown, Almanac.Knowledge(profile, "troll"));
        p.Discover(new Dictionary<string, int> { ["seen:troll"] = 1 });
        Assert.Equal(MonsterKnowledge.Seen, Almanac.Knowledge(profile, "troll"));
        p.Discover(new Dictionary<string, int> { ["killed:troll"] = Almanac.StudyKills });
        Assert.Equal(MonsterKnowledge.Studied, Almanac.Knowledge(profile, "troll"));
        Assert.Contains("monster:troll:studied", profile.Almanac);
        Assert.DoesNotContain("monster:troll:mastered", profile.Almanac);
        p.Discover(new Dictionary<string, int> { ["killed:troll"] = Almanac.MasterKills });
        Assert.Equal(MonsterKnowledge.Mastered, Almanac.Knowledge(profile, "troll"));
        Assert.Contains("monster:troll:mastered", profile.Almanac);
    }

    [Fact]
    public void TheFirstDelveAndShiftFillTheAlmanac()
    {
        var p = new Progression(Content, new Profile { Gold = 100, Stock = new() { ["wood"] = 100, ["stone"] = 100, ["ore"] = 10 } });
        var site = Content.Sites[0];
        var (_, outcome) = Delve.Run(Content, p.SiteSetup(site, 1));
        p.Apply(p.SiteReward(outcome, site));
        Assert.Contains("mat_stone", p.Profile.Almanac);
        Assert.Contains("mat_ore", p.Profile.Almanac);
        Assert.Contains("mat_wood", p.Profile.Almanac);
        Assert.Contains("place_entrance", p.Profile.Almanac);
        p.Profile.Learned.Add(Content.Lessons[1].Id);
        foreach (var id in new[] { "v_lumber", "v_quarry", "v_smelter" }) Assert.True(p.Buy(p.Node(id)!), id);
        p.RunWorkshops();
        Assert.Contains("made_iron", p.Profile.Almanac);
        var back = Profile.FromJson(p.Profile.ToJson());
        Assert.Equal(p.Profile.Almanac, back.Almanac);
        Assert.Equal(p.Profile.Found, back.Found);
    }

    [Fact]
    public void WorkshopMistakesAreDiscoveries()
    {
        var p = new Progression(Content, new Profile());
        var bakery = Content.Workshops.First(w => w.Id == Bakery.Id);
        var burnt = Workshops.Run(bakery, "light()\nknead()\nbake()\nwait(9)\ntake_out()", new Dictionary<string, int> { ["wheat"] = 2, ["wood"] = 1 });
        p.Discover(burnt.Found);
        Assert.Contains("bakery_burnt", p.Profile.Almanac);
    }
}

public class DelveUpgradeTests
{
    private static readonly ContentPack Content = Shared.Content;

    private static World Floor(string[] rows, Dictionary<char, string> legend, params (string Chassis, string Source)[] party) =>
        World.Create(Content, FloorLayout.FromRows([.. rows], legend), party.Select(p => new PartyMember(Content.Chassis[p.Chassis].Name, p.Chassis, p.Source)).ToList(), seed: 3, maxTicks: 800);

    private static List<string> Output(World w) => w.Log.Where(e => e.Kind == LogKind.Output).Select(e => e.Text[(e.Text.IndexOf(": ", StringComparison.Ordinal) + 2)..]).ToList();

    [Fact]
    public void DefeatedMonstersLeaveEssenceThatComesHome()
    {
        var w = Floor(["#######", "#S.k.>#", "#######"], new() { ['k'] = "skeleton" },
            ("warden", "while nearest_enemy():\n    if distance(nearest_enemy()) == 1:\n        attack()\n    else:\n        wait()\nwhile move_toward(stairs()):\n    pass"));
        w.RunToEnd();
        var outcome = w.Outcome();
        Assert.True(outcome.Kind == OutcomeKind.Success, string.Join("\n", w.Log.TakeLast(12).Select(e => e.Text)));
        Assert.Equal(1, outcome.Goods![Resources.Essence]);
        Assert.Equal(1, outcome.Found!["killed:skeleton"]);
        Assert.Equal(1, outcome.Found["seen:skeleton"]);
        Assert.Equal(1, outcome.Found["mined:essence"]);
        Assert.Contains(w.Log, e => e.Text.Contains("+1 essence", StringComparison.Ordinal));
    }

    [Fact]
    public void CrystalVeinsCanBeMined()
    {
        var w = Floor(["#*###", "#S.>#", "#####"], [], ("warden", "print(nearest_vein(Crystal).kind)\nmine(North)\nprint(carrying(Crystal), essence())\nmove(East)\nmove(East)"));
        w.RunToEnd();
        Assert.True(w.Golems[0].Error is null, w.Golems[0].Error?.Message);
        Assert.Equal(["Resource.Crystal", "1 0"], Output(w));
        Assert.Equal(1, w.Outcome().Goods![Resources.Crystal]);
    }

    [Fact]
    public void SmellPointsAlongTheWayToAMark()
    {
        var w = Floor(["#######", "#S....#", "####.##", "####.>#", "#######"], [],
            ("warden", "mark(\"here\")\nmove(East)\nmove(East)\nmove(East)\nprint(smell(\"here\"), smell(\"nothing\"))\nmove(South)\nprint(smell(\"here\"))\nmove(North)\nwhile smell(\"here\"):\n    move(smell(\"here\"))\nprint(unmark(), unmark(), smell(\"here\"))"));
        w.RunToEnd();
        Assert.Equal(["Directions.West None", "Directions.North", "True False None"], Output(w));
    }

    [Fact]
    public void SmellOnlyReachesSoFar()
    {
        var row = "#S" + new string('.', GolemApi.SmellRange + 1) + ">#";
        var w = Floor([new string('#', row.Length), row, new string('#', row.Length)], [],
            ("warden", $"mark(\"x\")\nfor i in range({GolemApi.SmellRange}):\n    move(East)\nprint(smell(\"x\"))\nmove(East)\nprint(smell(\"x\"))"));
        w.RunToEnd();
        Assert.Equal(["Directions.West", "None"], Output(w));
    }

    [Fact]
    public void PartyListsEveryGolemStillInside()
    {
        var w = Floor(["#######", "#S...>#", "#######"], [], ("warden", "print(len(party()), party()[0].name)\nwait(20)\nprint(len(party()))"), ("seeker", "move(East)\nmove(East)\nmove(East)\nmove(East)"));
        w.RunToEnd();
        Assert.Equal(["2 Warden", "1"], Output(w));
    }

    [Fact]
    public void MimicsNeverMoveAndWispsRunAway()
    {
        var w = Floor(["##########", "#S.m....>#", "#......w.#", "##########"], new() { ['m'] = "mimic", ['w'] = "wisp" }, ("warden", "wait(60)"));
        var mimic = w.Monsters.First(m => m.Def.Id == "mimic").Pos;
        var wisp = w.Monsters.First(m => m.Def.Id == "wisp");
        var golem = w.Golems[0].Pos;
        for (var i = 0; i < 40; i++) w.Step();
        Assert.Equal(mimic, w.Monsters.First(m => m.Def.Id == "mimic").Pos);
        Assert.Equal(w.Golems[0].Hp, w.Golems[0].MaxHp);
        Assert.True(wisp.Pos.Manhattan(golem) >= 4, $"the wisp should keep its distance, it's at {wisp.Pos}");
    }

    [Fact]
    public void SpidersBackOffAfterABite()
    {
        var w = Floor(["#########", "#S....s>#", "#########"], new() { ['s'] = "spider" }, ("warden", "while True:\n    wait()"));
        var spider = w.Monsters[0];
        var fled = false;
        for (var i = 0; i < 200 && !fled; i++)
        {
            w.Step();
            fled = w.Golems[0].Hp < w.Golems[0].MaxHp && spider.Intent == IntentKind.Flee;
        }
        Assert.True(fled);
    }
}
