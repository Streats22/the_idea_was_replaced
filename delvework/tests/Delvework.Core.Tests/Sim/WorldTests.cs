using Delvework.Core.Sim;
using static Delvework.Core.Tests.Sim.TestContent;

namespace Delvework.Core.Tests.Sim;

public class WorldTests
{
    private const string Corridor = """
        ##########
        #S.......#
        ##########
        """;

    [Fact]
    public void GolemsWalkAndFinish()
    {
        var w = World(Corridor, ("tank", "move(East)\nmove(East)"));
        var g = w.Golems[0];
        w.Step();
        Assert.Equal(new Pos(2, 1), g.Pos);
        w.Step();
        Assert.Equal(new Pos(2, 1), g.Pos);
        Ticks(w, 10);
        Assert.Equal(new Pos(3, 1), g.Pos);
        Assert.Equal(GolemState.Idle, g.State);
        Assert.True(w.Finished);
    }

    [Fact]
    public void BlockedMovesReturnFalseAndCountAsStuck()
    {
        var w = World(Corridor, ("tank", "ok = move(North)\nprint(ok)\nok = move(East)\nprint(ok)"));
        Ticks(w, 20);
        Assert.Equal(["Tank: False", "Tank: True"], Output(w));
        Assert.Equal(0, w.Golems[0].Stuck);
    }

    [Fact]
    public void AttacksKillAndLeaveLoot()
    {
        var w = World("""
            ######
            #Sd..#
            ######
            """, ("tank", "while nearest_enemy():\n    attack()\nmove(East)\nprint(loot())"));
        Ticks(w, 40);
        Assert.False(w.Monsters[0].Alive);
        Assert.Equal(["Tank: 3"], Output(w));
        Assert.Contains(w.Log, e => e.Text.Contains("is destroyed", StringComparison.Ordinal));
    }

    [Fact]
    public void ArmorAndResistApply()
    {
        var w = World("""
            #####
            #Sa.#
            #####
            """, ("tank", "attack()"));
        Ticks(w, 3);
        Assert.Equal(18, w.Monsters[0].Hp);
    }

    [Fact]
    public void TrapsHurtRevealAndFireEvents()
    {
        var w = World("""
            ######
            #S^..#
            ######
            """, ("tank", "on hurt(n, source):\n    print(\"ouch\", n, source)\nmove(East)\nwait()"));
        Ticks(w, 10);
        var g = w.Golems[0];
        Assert.Equal(25, g.Hp);
        Assert.True(w.Traps[0].Revealed);
        Assert.Equal(["Tank: ouch 5 spike trap"], Output(w));
    }

    [Fact]
    public void SenseAheadRevealsTraps()
    {
        var w = World("""
            ######
            #S^..#
            ######
            """, ("tank", "t = sense_ahead(East)\nprint(t.trap, t.wall, t.x)"));
        Ticks(w, 5);
        Assert.True(w.Traps[0].Revealed);
        Assert.Equal(["Tank: True False 2"], Output(w));
    }

    [Fact]
    public void ChestsAndStairsEndTheDelve()
    {
        const string program = """
            while True:
                if nearest_chest():
                    move_toward(nearest_chest())
                elif stairs():
                    move_toward(stairs())
                else:
                    explore()
            """;
        var w = World("""
            #######
            #SC..>#
            #######
            """, ("tank", program));
        Ticks(w, 100);
        var g = w.Golems[0];
        Assert.Equal(GolemState.Descended, g.State);
        Assert.Equal(10, g.Loot);
        var outcome = w.Outcome();
        Assert.Equal(OutcomeKind.Success, outcome.Kind);
        Assert.Equal(10, outcome.Loot);
    }

    [Fact]
    public void RecallTakesLootHomeOnce()
    {
        var w = World(Corridor, ("tank", "print(recall())\nprint(\"after\")"));
        Ticks(w, 5);
        Assert.Equal(GolemState.Recalled, w.Golems[0].State);
        Assert.Equal(OutcomeKind.Retreat, w.Outcome().Kind);
        Assert.Empty(Output(w));
    }

    [Fact]
    public void RuntimeErrorsHaltTheGolem()
    {
        var w = World(Corridor, ("tank", "move(East)\nx = [1][5]"));
        Ticks(w, 10);
        var g = w.Golems[0];
        Assert.Equal(GolemState.Halted, g.State);
        Assert.Equal(2, g.Error!.Line);
        Assert.Contains(w.Log, e => e.Kind == LogKind.Error && e.Text.Contains("Line 2", StringComparison.Ordinal));
        Assert.Equal(OutcomeKind.Stalled, w.Outcome().Kind);
    }

    [Fact]
    public void CompileErrorsHaltBeforeTheFirstTick()
    {
        var w = World(Corridor, ("tank", "movee(East)"));
        Assert.Equal(GolemState.Halted, w.Golems[0].State);
        Assert.Contains("Did you mean 'move'", w.Golems[0].Error!.Message, StringComparison.Ordinal);
        Assert.True(w.Finished);
    }

    [Fact]
    public void InfiniteLoopsOnlyStallTheirOwnGolem()
    {
        var w = World(Corridor, 60, ("tank", "while True:\n    pass"), ("scout", "move(East)"));
        w.RunToEnd();
        Assert.Equal(60, w.Tick);
        Assert.Equal(GolemState.Active, w.Golems[0].State);
        Assert.Equal(GolemState.Idle, w.Golems[1].State);
        Assert.Equal(50, w.Golems[0].Ops);
        Assert.Equal(OutcomeKind.Stalled, w.Outcome().Kind);
    }

    [Fact]
    public void SeeFiresOncePerSighting()
    {
        var w = World("""
            ##########
            #S.....d.#
            ##########
            """, ("tank", "on see(e):\n    print(\"see\", e.kind)\nwhile True:\n    wait()"));
        Ticks(w, 30);
        Assert.Equal(["Tank: see Dummy"], Output(w));
    }

    [Fact]
    public void FogMemoryGrowsAndWallsHide()
    {
        var w = World("""
            ############
            #S...#.....#
            ############
            """, ("tank", "move(East)"));
        Assert.True(w.Known[w.Grid.Idx(new Pos(4, 1))]);
        Assert.True(w.Known[w.Grid.Idx(new Pos(5, 1))]);
        Assert.False(w.Known[w.Grid.Idx(new Pos(7, 1))]);
    }

    [Fact]
    public void ExploreMapsEverythingThenReturnsFalse()
    {
        var w = World("""
            ###############
            #S............#
            #######.#######
            #######.......#
            ###############
            """, 800, ("scout", "while explore():\n    pass\nprint(\"mapped\")"));
        w.RunToEnd();
        Assert.Equal(["Scout: mapped"], Output(w));
        Assert.True(w.Known[w.Grid.Idx(new Pos(13, 3))]);
    }

    [Fact]
    public void PathToListsTheSteps()
    {
        var w = World(Corridor, ("tank", "print(path_to([4, 1]))\nprint(path_to([0, 0]))"));
        Ticks(w, 3);
        Assert.Equal(["Tank: [[2, 1], [3, 1], [4, 1]]", "Tank: None"], Output(w));
    }

    [Fact]
    public void WindUpsCanBeDodged()
    {
        const string program = """
            move(East)
            while True:
                e = nearest_enemy()
                if e.intent == Intent.Attack:
                    move(West)
                    break
                wait()
            print("dodged", hp())
            """;
        var w = World("""
            ########
            #S.b...#
            ########
            """, ("tank", program));
        Ticks(w, 20);
        Assert.Equal(["Tank: dodged 30"], Output(w));
        Assert.Contains(w.Log, e => e.Text.Contains("strikes at empty air", StringComparison.Ordinal));
    }

    [Fact]
    public void StandingStillAgainstAWindUpHurts()
    {
        var w = World("""
            #######
            #Sb...#
            #######
            """, 30, ("tank", "while True:\n    wait()"));
        w.RunToEnd();
        Assert.True(w.Golems[0].Hp < 30);
    }

    [Fact]
    public void StunFreezesTheProgram()
    {
        var w = World("""
            #######
            #St...#
            #######
            """, ("tank", "n = 0\nwhile True:\n    n += 1\n    wait()"));
        Ticks(w, 2);
        var g = w.Golems[0];
        Assert.True(g.StunTicks > 0);
        var n = g.Vm!.GetGlobal("n").AsInt;
        Ticks(w, g.StunTicks - 1);
        Assert.Equal(n, g.Vm.GetGlobal("n").AsInt);
        Ticks(w, 4);
        Assert.True(g.Vm.GetGlobal("n").AsInt > n);
    }

    [Fact]
    public void BurnDealsDamageOverTime()
    {
        var w = World("""
            #######
            #Sf...#
            #######
            """, ("tank", "while True:\n    wait()"));
        Ticks(w, 2);
        var g = w.Golems[0];
        var afterHit = g.Hp;
        Assert.True(g.BurnTicks > 0);
        Ticks(w, 20);
        Assert.Equal(afterHit - 4, g.Hp);
        Assert.Equal(0, g.BurnTicks);
    }

    [Fact]
    public void SnaresSlowMovement()
    {
        var layout = Dungeon.FloorLayout.FromRows(["#######", "#S^...#", "#######"], Keys, "snare");
        var world = Core.Sim.World.Create(Pack, layout, [new PartyMember("Tank", "tank", "move(East)\nmove(East)")], 1);
        world.Step();
        var g = world.Golems[0];
        Assert.Equal(new Pos(2, 1), g.Pos);
        Assert.True(g.SlowTicks > 0);
        world.Step();
        world.Step();
        Assert.Equal(new Pos(3, 1), g.Pos);
        Assert.Equal(4, g.Busy);
    }

    [Fact]
    public void HigherInitiativeResolvesFirst()
    {
        const string map = """
            #######
            ###.###
            ##S.###
            #######
            """;
        var w = World(map, ("tank", "print(move(East))"), ("scout", "move(North)"));
        Ticks(w, 3);
        Assert.Equal(["Tank: True"], Output(w));

        var w2 = World(map, ("scout", "print(move(East))"), ("tank", "move(North)"));
        Ticks(w2, 3);
        Assert.Equal(["Scout: False"], Output(w2));
    }

    [Fact]
    public void GolemsSwapWithAWaitingTeammate()
    {
        var w = World(Corridor, ("tank", "wait()\nmove(East)\nprint(me().x)"), ("scout", "while True:\n    wait()"));
        Ticks(w, 6);
        Assert.Equal(["Tank: 2"], Output(w));
        Assert.Equal(new Pos(1, 1), w.Golems[1].Pos);
    }

    [Fact]
    public void SignalsCarryACopyOfTheData()
    {
        var w = World(Corridor,
            ("tank", "xs = [1]\nsignal(\"go\", xs)\nxs.append(2)"),
            ("scout", "on signal \"go\"(data):\n    print(data)\nwhile True:\n    wait()"));
        Ticks(w, 10);
        Assert.Equal(["Scout: [1]"], Output(w));
    }

    [Fact]
    public void LowHpFiresOnceAndBrokenGolemsDropSalvage()
    {
        const string map = """
            #######
            #Sb...#
            #######
            """;
        var w = World(map, 400, ("tank", "on low_hp():\n    print(\"low\")\nwhile True:\n    wait()"));
        w.RunToEnd();
        var g = w.Golems[0];
        Assert.Equal(GolemState.Broken, g.State);
        Assert.Equal(["Tank: low"], Output(w));
        Assert.Contains(w.Drops, d => d.Label.Contains("salvage", StringComparison.Ordinal));
        Assert.Equal(OutcomeKind.Wiped, w.Outcome().Kind);
    }

    [Fact]
    public void MarksAreSharedAndFade()
    {
        var w = World(Corridor, 1000, ("tank", "mark(\"here\")\nmove(East)\nprint(marks())\nwhile True:\n    wait(100)"));
        Ticks(w, 5);
        Assert.Equal(["Tank: [Mark(label='here', x=1, y=1, age=2)]"], Output(w));
        Ticks(w, Core.Sim.World.MarkLifetime);
        Assert.Empty(w.Marks);
    }

    [Fact]
    public void BudgetIsSpentPerTick()
    {
        var w = World(Corridor, ("tank", "x = 0\nwhile True:\n    x += 1"));
        w.Step();
        Assert.Equal(50, w.Golems[0].Ops);
        w.BudgetOverride = 7;
        w.Step();
        Assert.Equal(7, w.Golems[0].Ops);
    }

    [Fact]
    public void ClonesReplayTheSameFuture()
    {
        var w = World("""
            ##########
            #S..c....#
            #...d....#
            ##########
            """, ("tank", Pack.Programs["walker"]), ("scout", "while True:\n    e = nearest_enemy()\n    if e and distance(e) == 1:\n        attack(e)\n    else:\n        explore()"));
        Ticks(w, 7);
        var copy = w.Clone();
        Assert.Equal(w.StateHash(), copy.StateHash());
        Ticks(w, 40);
        Ticks(copy, 40);
        Assert.Equal(w.StateHash(), copy.StateHash());
        Assert.Equal(w.Golems[0].Pos, copy.Golems[0].Pos);
    }

    [Fact]
    public void ProgramsImportContentModules()
    {
        var w = World(Corridor, ("tank", "import helpers\nprint(ahead_x())"));
        Ticks(w, 2);
        Assert.Equal(["Tank: 2"], Output(w));
    }

    [Fact]
    public void QueriesSeeStartOfTickState()
    {
        var w = World(Corridor, ("scout", "move(East)"), ("tank", "print(ally().x)"));
        Ticks(w, 1);
        Assert.Equal(["Tank: 1"], Output(w));
    }
}
