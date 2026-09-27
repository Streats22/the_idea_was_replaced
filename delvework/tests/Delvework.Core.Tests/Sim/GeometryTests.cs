using Delvework.Core.Content;
using Delvework.Core.Sim;

namespace Delvework.Core.Tests.Sim;

public class RngTests
{
    [Fact]
    public void SameSeedSameSequence()
    {
        var a = new Rng(123);
        var b = new Rng(123);
        for (var i = 0; i < 100; i++) Assert.Equal(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void StreamsAreIndependent()
    {
        var s = new RngStreams(5);
        var combat = s.Combat.NextULong();
        var fresh = new RngStreams(5);
        for (var i = 0; i < 50; i++) fresh.Loot.NextULong();
        Assert.Equal(combat, fresh.Combat.NextULong());
        Assert.NotEqual(new RngStreams(5).Dungeon.NextULong(), new RngStreams(5).Monsters.NextULong());
    }

    [Fact]
    public void BoundsAreRespected()
    {
        var r = new Rng(9);
        var seen = new HashSet<int>();
        for (var i = 0; i < 2000; i++)
        {
            var n = r.Next(6);
            Assert.InRange(n, 0, 5);
            seen.Add(n);
            Assert.InRange(r.Range(3, 5), 3, 5);
        }
        Assert.Equal(6, seen.Count);
        Assert.Equal(0, r.Next(1));
        Assert.Equal(4, r.Range(4, 4));
    }

    [Fact]
    public void WeightedPickSkipsZeroWeights()
    {
        var r = new Rng(1);
        for (var i = 0; i < 500; i++) Assert.NotEqual(1, r.Weighted([3, 0, 1]));
    }

    [Fact]
    public void ChanceIsInPermille()
    {
        var r = new Rng(2);
        Assert.False(r.Chance(0));
        Assert.True(r.Chance(1000));
        var hits = Enumerable.Range(0, 10_000).Count(_ => r.Chance(250));
        Assert.InRange(hits, 2200, 2800);
    }

    [Fact]
    public void ClonesContinueTheSameSequence()
    {
        var r = new Rng(77);
        r.NextULong();
        var c = r.Clone();
        Assert.Equal(r.NextULong(), c.NextULong());
        Assert.Equal(r.StateHash(0), c.StateHash(0));
    }
}

public class FovTests
{
    private static Grid Open(int w, int h)
    {
        var g = new Grid(w, h);
        for (var y = 1; y < h - 1; y++)
        {
            for (var x = 1; x < w - 1; x++) g.SetWall(new Pos(x, y), false);
        }
        return g;
    }

    private static HashSet<Pos> Visible(Grid g, Pos o, int r)
    {
        var set = new HashSet<Pos>();
        Fov.Compute(g, o, r, i => set.Add(g.At(i)));
        return set;
    }

    [Fact]
    public void SeesEverythingNearbyInAnOpenRoom()
    {
        var g = Open(21, 21);
        var seen = Visible(g, new Pos(10, 10), 4);
        for (var y = 6; y <= 14; y++)
        {
            for (var x = 6; x <= 14; x++)
            {
                var p = new Pos(x, y);
                var inside = p.DistSq(new Pos(10, 10)) <= 16;
                if (inside) Assert.Contains(p, seen);
            }
        }
        Assert.DoesNotContain(new Pos(10, 15), seen);
        Assert.DoesNotContain(new Pos(14, 14), seen);
    }

    [Fact]
    public void WallsBlockSightButAreThemselvesVisible()
    {
        var g = Open(21, 11);
        for (var y = 1; y < 10; y++) g.SetWall(new Pos(12, y), true);
        var seen = Visible(g, new Pos(10, 5), 8);
        Assert.Contains(new Pos(12, 5), seen);
        Assert.DoesNotContain(new Pos(14, 5), seen);
    }

    [Fact]
    public void PillarsCastShadows()
    {
        var g = Open(21, 21);
        g.SetWall(new Pos(12, 10), true);
        var seen = Visible(g, new Pos(10, 10), 8);
        Assert.Contains(new Pos(12, 10), seen);
        Assert.DoesNotContain(new Pos(15, 10), seen);
        Assert.Contains(new Pos(15, 12), seen);
    }

    [Fact]
    public void CorridorsAreSeenEndToEnd()
    {
        var g = new Grid(20, 3);
        for (var x = 1; x < 19; x++) g.SetWall(new Pos(x, 1), false);
        var seen = Visible(g, new Pos(1, 1), 12);
        Assert.Contains(new Pos(13, 1), seen);
        Assert.DoesNotContain(new Pos(14, 1), seen);
    }

    [Fact]
    public void LineOfSightMatchesWalls()
    {
        var g = Open(10, 10);
        g.SetWall(new Pos(5, 5), true);
        Assert.False(Fov.LineOfSight(g, new Pos(3, 5), new Pos(7, 5)));
        Assert.True(Fov.LineOfSight(g, new Pos(3, 3), new Pos(7, 3)));
        Assert.True(Fov.CanSee(g, new Pos(3, 3), 4, new Pos(7, 3)));
        Assert.False(Fov.CanSee(g, new Pos(1, 1), 4, new Pos(8, 8)));
    }
}

public class PathfinderTests
{
    private static Grid Map(params string[] rows) => Grid.FromRows(rows);

    [Fact]
    public void FindsStraightPaths()
    {
        var g = Map("#######", "#.....#", "#######");
        var path = new Pathfinder(g).AStar(new Pos(1, 1), new Pos(5, 1), p => !g.IsWall(p));
        Assert.NotNull(path);
        Assert.Equal(4, path!.Count);
        Assert.Equal(new Pos(5, 1), path[^1]);
    }

    [Fact]
    public void RoutesAroundWalls()
    {
        var g = Map("#######", "#..#..#", "#..#..#", "#.....#", "#######");
        var path = new Pathfinder(g).AStar(new Pos(1, 1), new Pos(5, 1), p => !g.IsWall(p));
        Assert.Equal(8, path!.Count);
        Assert.DoesNotContain(path, p => g.IsWall(p));
    }

    [Fact]
    public void ReportsUnreachableGoals()
    {
        var g = Map("#####", "#.#.#", "#####");
        Assert.Null(new Pathfinder(g).AStar(new Pos(1, 1), new Pos(3, 1), p => !g.IsWall(p)));
    }

    [Fact]
    public void TheGoalMayBeOccupied()
    {
        var g = Map("######", "#....#", "######");
        var blocked = new Pos(4, 1);
        var path = new Pathfinder(g).AStar(new Pos(1, 1), blocked, p => !g.IsWall(p) && p != blocked);
        Assert.Equal(3, path!.Count);
    }

    [Fact]
    public void PathsAreDeterministic()
    {
        var g = Map("#######", "#.....#", "#.....#", "#.....#", "#######");
        var pf = new Pathfinder(g);
        var a = pf.AStar(new Pos(1, 1), new Pos(5, 3), p => !g.IsWall(p));
        var b = new Pathfinder(g).AStar(new Pos(1, 1), new Pos(5, 3), p => !g.IsWall(p));
        Assert.Equal(a, b);
        Assert.Equal(a, pf.AStar(new Pos(1, 1), new Pos(5, 3), p => !g.IsWall(p)));
    }

    [Fact]
    public void NearestFindsTheClosestMatch()
    {
        var g = Map("#########", "#.......#", "#########");
        var path = new Pathfinder(g).Nearest(new Pos(3, 1), p => p.X is 1 or 7, p => !g.IsWall(p));
        Assert.Equal(2, path!.Count);
        Assert.Equal(new Pos(1, 1), path[^1]);
    }

    [Fact]
    public void DistanceMapsCountSteps()
    {
        var g = Map("#####", "#...#", "#.#.#", "#...#", "#####");
        var d = new Pathfinder(g).DistanceMap(new Pos(1, 1), p => !g.IsWall(p));
        Assert.Equal(0, d[g.Idx(new Pos(1, 1))]);
        Assert.Equal(4, d[g.Idx(new Pos(3, 3))]);
        Assert.Equal(-1, d[g.Idx(new Pos(2, 2))]);
    }
}

public class CombatTests
{
    private static Monster Target(int armor, Dictionary<DamageType, int>? resist = null) => new()
    {
        Def = new MonsterDef { Id = "t", Name = "T", Hp = 10 },
        World = null!,
        Armor = armor,
        Resist = resist ?? [],
        Hp = 10,
        MaxHp = 10,
    };

    [Theory]
    [InlineData(4, 0, 4)]
    [InlineData(4, 1, 3)]
    [InlineData(4, 10, 1)]
    public void ArmorReducesDamageButNeverBelowOne(int attack, int armor, int expected)
    {
        Assert.Equal(expected, Combat.Damage(attack, DamageType.Physical, Target(armor)));
    }

    [Fact]
    public void ResistanceScalesDamage()
    {
        var t = Target(1, new() { [DamageType.Fire] = 150, [DamageType.Frost] = 50, [DamageType.Arcane] = 0 });
        Assert.Equal(5, Combat.Damage(4, DamageType.Fire, t));
        Assert.Equal(1, Combat.Damage(4, DamageType.Frost, t));
        Assert.Equal(0, Combat.Damage(4, DamageType.Arcane, t));
        Assert.Equal(3, Combat.Damage(4, DamageType.Physical, t));
    }

    [Fact]
    public void SlowDoublesCooldowns()
    {
        var t = Target(0);
        Assert.Equal(4, World.Cooldown(t, 4));
        t.SlowTicks = 3;
        Assert.Equal(8, World.Cooldown(t, 4));
    }
}
