using Delvework.Core.Content;
using Delvework.Core.Dungeon;
using Delvework.Core.Sim;
using static Delvework.Core.Tests.Sim.TestContent;

namespace Delvework.Core.Tests.Sim;

public class MonsterTests
{
    private static readonly ContentPack Real = ContentPack.LoadDefault();

    private static World RealWorld(string[] rows, Dictionary<char, string> keys, params (string Chassis, string Source)[] party) =>
        Core.Sim.World.Create(Real, FloorLayout.FromRows(rows, keys),
            party.Select(p => new PartyMember(Real.Chassis[p.Chassis].Name, p.Chassis, p.Source)).ToList(), seed: 3);

    [Fact]
    public void ChasersCloseInAndAttack()
    {
        var w = World("""
            ##########
            #S.....c.#
            ##########
            """, ("tank", "while True:\n    wait()"));
        Ticks(w, 12);
        Assert.Equal(new Pos(2, 1), w.Monsters[0].Pos);
        Ticks(w, 10);
        Assert.True(w.Golems[0].Hp < 30);
    }

    [Fact]
    public void GolemsSeeChaseIntent()
    {
        var w = World("""
            ##########
            #S.....c.#
            ##########
            """, ("tank", "wait(2)\nprint(nearest_enemy().intent)"));
        Ticks(w, 4);
        Assert.Equal(["Tank: Intent.Chase"], Output(w));
    }

    [Fact]
    public void MonstersNeverShareATile()
    {
        var w = World("""
            ##########
            #S...cc..#
            #....c...#
            ##########
            """, 200, ("tank", "while True:\n    wait()"));
        for (var i = 0; i < 60 && !w.Finished; i++)
        {
            w.Step();
            var alive = w.Monsters.Where(m => m.Alive).Select(m => m.Pos).ToList();
            Assert.Equal(alive.Count, alive.Distinct().Count());
            Assert.DoesNotContain(w.Golems[0].Pos, alive);
        }
    }

    [Fact]
    public void KilledMonstersStopActing()
    {
        var w = World("""
            #######
            #Sc...#
            #######
            """, ("tank", "while enemy_alive(nearest_enemy()):\n    attack()\nprint(\"done\", hp())"));
        Ticks(w, 60);
        Assert.False(w.Monsters[0].Alive);
        var hp = w.Golems[0].Hp;
        Ticks(w, 20);
        Assert.Equal(hp, w.Golems[0].Hp);
    }

    [Fact]
    public void TrollsSleepUntilHurt()
    {
        string[] rows = ["##########", "#S.......#", "#......T.#", "##########"];
        var keys = new Dictionary<char, string> { ['T'] = "troll" };
        var w = RealWorld(rows, keys, ("warden", "while True:\n    wait()"));
        var troll = w.Monsters[0];
        var start = troll.Pos;
        for (var i = 0; i < 60; i++) w.Step();
        Assert.Equal(start, troll.Pos);

        w = RealWorld(rows, keys, ("warden", "for i in range(5):\n    move(East)\nwhile True:\n    wait()"));
        troll = w.Monsters[0];
        for (var i = 0; i < 60; i++) w.Step();
        Assert.NotEqual(start, troll.Pos);
    }

    [Fact]
    public void RealMonsterScriptsRunWithoutErrors()
    {
        string[] rows = ["##############", "#S...........#", "#..s.b.f.T.l.#", "#............#", "##############"];
        var keys = new Dictionary<char, string> { ['s'] = "skeleton", ['b'] = "bat", ['f'] = "fire_beetle", ['T'] = "troll", ['l'] = "slime" };
        var w = RealWorld(rows, keys, ("warden", Real.Programs["warden"]), ("seeker", Real.Programs["seeker"]));
        w.RunToEnd();
        Assert.DoesNotContain(w.Log, e => e.Kind == LogKind.Error);
    }
}
