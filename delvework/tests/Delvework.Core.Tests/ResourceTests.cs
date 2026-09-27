using Delvework.Core.Content;
using Delvework.Core.Dungeon;
using Delvework.Core.Glyph;
using Delvework.Core.Sim;
using Delvework.Core.Village;

namespace Delvework.Core.Tests;

public class VeinTests
{
    private static readonly ContentPack Content = Shared.Content;

    private static (World World, Outcome Outcome) Entrance(string program)
    {
        var setup = new DelveSetup(1, "entrance", [new PartyMember("Warden", "warden", program)]);
        return Delve.Run(Content, setup);
    }

    private static List<string> Output(World w) =>
        w.Log.Where(e => e.Kind == LogKind.Output).Select(e => e.Text["Warden: ".Length..]).ToList();

    [Fact]
    public void GeneratedFloorsHaveMineableVeins()
    {
        var stratum = Content.Strata["mines"];
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var layout = Generator.Generate(stratum, seed);
            Assert.InRange(layout.Veins.Count, 1, stratum.Veins.Count[1]);
            Assert.Empty(Generator.Validate(layout));
            Assert.All(layout.Veins, v => Assert.InRange(v.Amount, stratum.Veins.Amount[0], stratum.Veins.Amount[1]));
        }
    }

    [Fact]
    public void VeinsDoNotChangeTheRestOfTheFloor()
    {
        var stratum = Content.Strata["mines"];
        var plain = Generator.Generate(stratum with { Veins = new VeinTable() }, 7);
        var veined = Generator.Generate(stratum, 7);
        Assert.Empty(plain.Veins);
        Assert.Equal(plain.Monsters.Select(m => m.Pos), veined.Monsters.Select(m => m.Pos));
        Assert.Equal(plain.Chests.Select(c => c.Pos), veined.Chests.Select(c => c.Pos));
        Assert.Equal(plain.Start, veined.Start);
    }

    [Fact]
    public void MiningEmptiesAVeinAndOnlyCountsAtHome()
    {
        var (world, outcome) = Entrance("move(East)\nmove(East)\nprint(mine(North), mine(North), mine(North), mine(North))\nprint(carrying(Stone), carrying(Ore), carrying())\nmine(West)");
        Assert.Null(world.Golems[0].Error);
        Assert.Equal(["True True True False", "3 0 3"], Output(world));
        Assert.Equal(0, world.Veins.First(v => v.Resource == Resources.Stone).Left);
        Assert.NotEqual(OutcomeKind.Success, outcome.Kind);
        Assert.True(outcome.Goods is null || outcome.Goods.Count == 0);
    }

    [Fact]
    public void NearestVeinFindsSeenVeinsByKind()
    {
        var (world, _) = Entrance("v = nearest_vein(Ore)\nprint(v.x, v.y, v.kind, v.left)\nprint(nearest_vein().kind)");
        Assert.Null(world.Golems[0].Error);
        Assert.Equal(["4 2 Resource.Ore 3", "Resource.Stone"], Output(world));
    }

    [Fact]
    public void MiningAVeinRecordTarget()
    {
        var (world, _) = Entrance("v = nearest_vein(Ore)\nwhile not mine(v):\n    move(East)\nprint(carrying(Ore))");
        Assert.Null(world.Golems[0].Error);
        Assert.Equal(["1"], Output(world));
    }

    [Fact]
    public void MoveTowardAVeinStopsNextToIt()
    {
        var (world, _) = Entrance("while move_toward(nearest_vein(Ore)):\n    pass\nprint(distance(nearest_vein(Ore)), mine(nearest_vein(Ore)))");
        Assert.Null(world.Golems[0].Error);
        Assert.Equal(["1 True"], Output(world));
    }

    [Fact]
    public void TheCodexMiningLoopDigsEveryVeinAndFindsTheStairs()
    {
        var page = Content.Lessons.First(l => l.Id == "decisions").Pages.First(p => p.Title.Contains("materials", StringComparison.Ordinal));
        var code = string.Join('\n', page.Blocks.First(b => b.Code is not null).Code!);
        var (world, outcome) = Entrance(code);
        Assert.Null(world.Golems[0].Error);
        Assert.Equal(OutcomeKind.Success, outcome.Kind);
        Assert.Equal(9, outcome.Goods!.Values.Sum());
        var mines = Enumerable.Range(1, 8).Select(s => Delve.Run(Content, new DelveSetup((ulong)s, "mines", [new PartyMember("Warden", "warden", code)])).Outcome).ToList();
        Assert.DoesNotContain(mines, o => o.Kind == OutcomeKind.Stalled);
        Assert.True(mines.Sum(o => o.Goods?.Values.Sum() ?? 0) > 40);
    }

    [Fact]
    public void TimelineFramesTrackVeinsAndBags()
    {
        var setup = new DelveSetup(1, "entrance", [new PartyMember("Warden", "warden", "move(East)\nmove(East)\nmine(North)")]);
        var timeline = Replay.Timeline.Record(Content, setup);
        var last = timeline.Frames[^1];
        Assert.Equal(1, last.Golems[0].Bag![0]);
        Assert.Contains(2, last.VeinsLeft!);
        Assert.All(timeline.Frames[0].VeinsLeft!, n => Assert.Equal(3, n));
    }
}

public class WorkshopTests
{
    private static readonly ContentPack Content = Shared.Content;
    private static WorkshopDef Smelter => Content.Workshops.First(w => w.Id == "smelter");
    private static WorkshopDef Bakery => Content.Workshops.First(w => w.Id == "bakery");

    private static Dictionary<string, int> Stock(int ore = 0, int wood = 0, int wheat = 0) => new()
    {
        ["ore"] = ore,
        ["wood"] = wood,
        ["wheat"] = wheat,
    };

    [Fact]
    public void EveryWorkshopLoadsWithAStarterThatCompilesAtTierOne()
    {
        Assert.Equal(["farm", "smelter", "bakery"], Content.Workshops.Select(w => w.Id));
        foreach (var w in Content.Workshops) Workshops.Compile(w, w.Starter, Tiers.Calls);
    }

    [Fact]
    public void TheSmelterStarterMakesFourIron()
    {
        var r = Workshops.Run(Smelter, Smelter.Starter, Stock(ore: 10, wood: 10));
        Assert.Null(r.Error);
        Assert.Equal(4, r.Made["iron"]);
        Assert.Equal(4, r.Used["ore"]);
        Assert.Equal(2, r.Used["wood"]);
        Assert.Contains("made 4 iron", r.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void SmeltingNeedsHeat()
    {
        var r = Workshops.Run(Smelter, "print(smelt())\nstoke()\nprint(heat(), smelt())", Stock(ore: 5, wood: 5));
        Assert.Contains(r.Log, l => l.EndsWith("print: False", StringComparison.Ordinal));
        Assert.Contains(r.Log, l => l.EndsWith("print: 5 True", StringComparison.Ordinal));
        Assert.Equal(1, r.Made["iron"]);
    }

    [Fact]
    public void OverstokingWastesWood()
    {
        var r = Workshops.Run(Smelter, "stoke()\nstoke()\nstoke()", Stock(wood: 5));
        Assert.Equal(3, r.Used["wood"]);
        Assert.Equal(5, r.Frames[^1].State["wasted"]);
        Assert.Contains(r.Log, l => l.Contains("up the chimney", StringComparison.Ordinal));
    }

    [Fact]
    public void AGoodLoopSmeltsAllTheOre()
    {
        var src = "while ore() > 0:\n    if heat() < 5:\n        stoke()\n    smelt()";
        var r = Workshops.Run(Smelter, src, Stock(ore: 20, wood: 20));
        Assert.Null(r.Error);
        Assert.Equal(20, r.Made["iron"]);
        Assert.True(r.Used["wood"] <= 12, $"used {r.Used["wood"]} wood");
        Assert.True(r.LastTick < Smelter.ShiftTicks);
    }

    [Fact]
    public void ShiftsStopAtTheShiftLength()
    {
        var r = Workshops.Run(Smelter, "while True:\n    stoke()\n    smelt()", Stock(ore: 1000, wood: 1000));
        Assert.Equal(Smelter.ShiftTicks, r.LastTick);
        Assert.Equal(Smelter.ShiftTicks / 2, r.Made["iron"]);
    }

    [Fact]
    public void TheBakeryStarterBakesTwoLoaves()
    {
        var r = Workshops.Run(Bakery, Bakery.Starter, Stock(wheat: 10, wood: 5));
        Assert.Null(r.Error);
        Assert.Equal(2, r.Made["bread"]);
        Assert.Equal(4, r.Used["wheat"]);
        Assert.Equal(1, r.Used["wood"]);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(3, 0, 0)]
    [InlineData(4, 1, 0)]
    [InlineData(7, 1, 0)]
    [InlineData(8, 0, 1)]
    public void BreadMustComeOutInTime(int wait, int bread, int burnt)
    {
        var r = Workshops.Run(Bakery, $"light()\nknead()\nbake()\nwait({wait - 1})\ntake_out()", Stock(wheat: 2, wood: 1));
        Assert.Equal(bread, r.Made.GetValueOrDefault("bread"));
        Assert.Equal(burnt, r.Frames[^1].State["burnt"]);
    }

    [Fact]
    public void BreadDoesNotBakeWithoutFire()
    {
        var r = Workshops.Run(Bakery, "knead()\nbake()\nwait(5)\nprint(baked())\ntake_out()", Stock(wheat: 2));
        Assert.Contains(r.Log, l => l.EndsWith("print: 0", StringComparison.Ordinal));
        Assert.Empty(r.Made);
    }

    [Fact]
    public void TheBakeryPageLoopBakesEverything()
    {
        var page = Bakery.Pages[1].Blocks.First(b => b.Code is not null).Code!;
        var r = Workshops.Run(Bakery, string.Join('\n', page), Stock(wheat: 16, wood: 5));
        Assert.Null(r.Error);
        Assert.Equal(8, r.Made["bread"]);
    }

    [Fact]
    public void BrokenScriptsChangeNothing()
    {
        var r = Workshops.Run(Smelter, "stoke(\n", Stock(ore: 3, wood: 3));
        Assert.NotNull(r.Error);
        Assert.Empty(r.Used);
        Assert.Empty(r.Made);
        Assert.Contains("error on line", r.Summary, StringComparison.Ordinal);
        var late = Workshops.Run(Smelter, "stoke()\nsmelt()\nprint(1 // 0)", Stock(ore: 3, wood: 3));
        Assert.NotNull(late.Error);
        Assert.Equal(1, late.Made["iron"]);
    }

    [Fact]
    public void WorkshopsHaveTheirOwnFunctions()
    {
        Assert.Throws<GlyphError>(() => Workshops.Compile(Smelter, "move(East)", Tiers.Max));
        Assert.Throws<GlyphError>(() => Workshops.Compile(Smelter, "knead()", Tiers.Max));
        Assert.Throws<GlyphError>(() => Workshops.Compile(Smelter, "while True:\n    smelt()", Tiers.Calls));
    }

    [Fact]
    public void ShiftsAreDeterministic()
    {
        var a = Workshops.Run(Bakery, Bakery.Starter, Stock(wheat: 9, wood: 3));
        var b = Workshops.Run(Bakery, Bakery.Starter, Stock(wheat: 9, wood: 3));
        Assert.Equal(a.Log, b.Log);
        Assert.Equal(a.Frames.Count, b.Frames.Count);
    }
}
