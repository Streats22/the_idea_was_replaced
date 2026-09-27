using Delvework.Core.Content;
using Delvework.Core.Dungeon;
using Delvework.Core.Replay;
using Delvework.Core.Sim;

namespace Delvework.Core.Tests;

/// <summary>The Phase 0 playtest floor and example parties (ported from the former TypeScript prototype).</summary>
public class PlaytestTests
{
    private static readonly ContentPack Content = Shared.Content;

    private static DelveSetup Setup(string example, ulong seed, bool seeker = true)
    {
        var ex = Content.Examples.First(e => e.Name == example);
        var party = new List<PartyMember> { new("Warden", "warden", ex.Programs["warden"]) };
        if (seeker) party.Add(new PartyMember("Seeker", "seeker", ex.Programs["seeker"]));
        return new DelveSetup(seed, "old_mines", party);
    }

    [Fact]
    public void OldMinesMatchesThePrototypeFloor()
    {
        var floor = Content.Floors["old_mines"];
        Assert.Equal(14, floor.Rows.Count);
        Assert.All(floor.Rows, r => Assert.Equal(20, r.Length));
        var layout = floor.ToLayout();
        Assert.Equal(2, layout.Chests.Count);
        Assert.Equal(2, layout.Traps.Count);
        Assert.Equal(["skeleton", "skeleton", "slime", "slime"], layout.Monsters.Select(m => m.Id).Order(StringComparer.Ordinal));
        Assert.Empty(Generator.Validate(layout));
    }

    [Fact]
    public void ExamplesLoadInOrderAndCompile()
    {
        Assert.Equal(["1. Just explore", "2. Fight back", "3. Read the Skeleton"], Content.Examples.Select(e => e.Name));
        foreach (var e in Content.Examples)
        {
            var world = Delve.Create(Content, Setup(e.Name, 1));
            Assert.All(world.Golems, g => Assert.Null(g.Error));
        }
    }

    [Fact]
    public void FloorsAreSelectableLikeStrata()
    {
        var world = Delve.Create(Content, Setup("1. Just explore", 3));
        Assert.Equal(20, world.Grid.Width);
        Assert.Equal(3000, world.MaxTicks);
        Assert.Throws<ContentException>(() => Delve.Create(Content, Setup("1. Just explore", 3) with { Stratum = "nowhere" }));
    }

    [Fact]
    public void TheNaiveExplorerUsuallyLosesAGolem()
    {
        var broken = Enumerable.Range(1, 10).Count(s => Delve.Run(Content, Setup("1. Just explore", (ulong)s)).Outcome.Broken.Count > 0);
        Assert.True(broken >= 6, $"only {broken}/10 runs lost a golem");
    }

    [Fact]
    public void ReadingTheSkeletonBringsThePartyHome()
    {
        var outcomes = Enumerable.Range(1, 10).Select(s => Delve.Run(Content, Setup("3. Read the Skeleton", (ulong)s)).Outcome).ToList();
        var wins = outcomes.Count(o => o.Kind == OutcomeKind.Success);
        Assert.True(wins >= 8, $"only {wins}/10 successes: {string.Join(", ", outcomes.Select(o => o.Kind))}");
        Assert.True(outcomes.Min(o => o.Loot) > 10, $"loot: {string.Join(", ", outcomes.Select(o => o.Loot))}");
    }

    [Fact]
    public void TimelineHasOneFramePerTick()
    {
        var t = Timeline.Record(Content, Setup("2. Fight back", 4));
        Assert.Equal(t.Final.Tick + 1, t.Frames.Count);
        for (var i = 0; i < t.Frames.Count; i++) Assert.Equal(i, t.Frames[i].Tick);
        Assert.Equal(t.Final.Log.Count, t.LogUpTo(t.LastTick).Count());
        Assert.Equal(2, t.Frames[0].Golems.Count);
        Assert.Contains(t.Frames[0].Fog, f => f == 2);
        Assert.Contains(t.Frames[0].Fog, f => f == 0);
    }

    [Fact]
    public void TimelineFramesMatchSeekingTheReplay()
    {
        var t = Timeline.Record(Content, Setup("3. Read the Skeleton", 5));
        foreach (var tick in new[] { 0, 7, 50, 123, t.LastTick })
        {
            var w = t.Recorder.Seek(tick);
            var frame = t.At(tick);
            Assert.Equal(w.Golems.Select(g => (g.Pos, g.Hp, g.State)), frame.Golems.Select(g => (g.Pos, g.Hp, g.State)));
            Assert.Equal(w.Monsters.Select(m => (m.Pos, m.Alive)), frame.Monsters.Select(m => (m.Pos, m.Alive)));
        }
    }

    [Fact]
    public void InspectShowsProgramStateAtATick()
    {
        var t = Timeline.Record(Content, Setup("3. Read the Skeleton", 6));
        var early = t.Inspect(3, "Warden")!;
        Assert.Contains(early.Globals, v => v.Name == "enemy");
        Assert.Contains(early.Globals, v => v.Name == "chest");
        Assert.True(early.Line > 0);
        Assert.Null(t.Inspect(3, "Nobody"));
    }

    [Fact]
    public void InspectReportsErrors()
    {
        var setup = new DelveSetup(1, "old_mines", [new PartyMember("Warden", "warden", "wait()\nboom()")]);
        var t = Timeline.Record(Content, setup);
        var i = t.Inspect(t.LastTick, "Warden")!;
        Assert.Equal(GolemState.Halted, i.State);
        Assert.Equal(2, i.Error!.Line);
    }
}
