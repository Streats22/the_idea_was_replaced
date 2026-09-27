using Delvework.Core.Glyph;

namespace Delvework.Core.Sim;

/// <summary>The restricted API monster scripts run against. Same VM, smaller surface.</summary>
public static class MonsterApi
{
    public static readonly GlyphEnvironment Environment = Build();

    private static GlyphEnvironment Build()
    {
        var env = Stdlib.AddTo(new GlyphEnvironment());
        env.AddEnum(GolemApi.Directions, exposeMembers: true);
        env.AddEnum(GolemApi.Intent);
        env.AddEvents("hurt");
        foreach (var def in Definitions()) env.Add(def);
        return env.Freeze();
    }

    private static Monster M(in BuiltinCall c) => (Monster)c.Vm.Host!;

    private static Value Declare(Monster m, ActionKind kind, Dir dir, int target, int delay, int busy)
    {
        m.Pending = new PendingAction(kind, dir, target, m.World.Tick + delay);
        m.Busy = World.Cooldown(m, busy);
        return Value.False;
    }

    private static BuiltinDef Action(string name, BuiltinImpl impl, int min, int max, string sig, string doc, int cost = 1) => new()
    {
        Name = name, Impl = impl, IsAction = true, MinArgs = min, MaxArgs = max, Cost = cost, Signature = sig, Doc = doc,
    };

    private static BuiltinDef Query(string name, BuiltinImpl impl, int min, int max, string sig, string doc, int cost = 1) => new()
    {
        Name = name, Impl = impl, MinArgs = min, MaxArgs = max, Cost = cost, Signature = sig, Doc = doc,
    };

    private static IEnumerable<Golem> VisibleGolems(Monster m) =>
        m.World.Golems.Where(g => g.Present && Fov.CanSee(m.World.Grid, m.Pos, m.Sight, g.Pos));

    private static IEnumerable<BuiltinDef> Definitions()
    {
        yield return Action("move", (in BuiltinCall c) =>
        {
            var m = M(c);
            return Declare(m, ActionKind.Move, GolemApi.DirOf(c, c.Arg(0), "move"), 0, 0, m.Def.MoveTicks);
        }, 1, 1, "move(dir)", "Step one tile.");

        yield return Action("step_toward", (in BuiltinCall c) =>
        {
            var m = M(c);
            var w = m.World;
            var target = GolemApi.PosOf(c, c.Arg(0), "step_toward");
            var path = w.Paths.AStar(m.Pos, target, p => !w.Grid.IsWall(p) && w.Occupant(p) is null && p != w.Stairs, 2_000);
            if (path is not { Count: > 0 } || Dirs.Between(m.Pos, path[0]) is not Dir d)
            {
                m.Busy = m.Def.MoveTicks;
                return Value.False;
            }
            return Declare(m, ActionKind.Move, d, 0, 0, m.Def.MoveTicks);
        }, 1, 1, "step_toward(target)", "Step along a path toward a position.", 5);

        yield return Action("step_away", (in BuiltinCall c) =>
        {
            var m = M(c);
            var w = m.World;
            var from = GolemApi.PosOf(c, c.Arg(0), "step_away");
            var best = Dirs.All
                .Select(d => (d, p: m.Pos.Step(d)))
                .Where(x => !w.Grid.IsWall(x.p) && w.Occupant(x.p) is null && x.p != w.Stairs && x.p.Manhattan(from) > m.Pos.Manhattan(from))
                .OrderByDescending(x => x.p.Manhattan(from)).ThenBy(x => (int)x.d).Select(x => (Dir?)x.d).FirstOrDefault();
            if (best is not Dir d)
            {
                m.Busy = m.Def.MoveTicks;
                return Value.False;
            }
            return Declare(m, ActionKind.Move, d, 0, 0, m.Def.MoveTicks);
        }, 1, 1, "step_away(target)", "Step to a free tile further from a position. Returns False if cornered.");

        yield return Action("wander", (in BuiltinCall c) =>
        {
            var m = M(c);
            var d = Dirs.All[m.World.Rng.Monsters.Next(4)];
            return Declare(m, ActionKind.Move, d, 0, 0, m.Def.MoveTicks);
        }, 0, 0, "wander()", "Step in a random direction.");

        yield return Action("attack", (in BuiltinCall c) =>
        {
            var m = M(c);
            Golem? target = null;
            var arg = c.Arg(0);
            if (arg.IsNone)
            {
                target = m.World.Golems.Where(g => g.Present && g.Pos.Manhattan(m.Pos) == 1).OrderBy(g => g.Hp).ThenBy(g => g.Id).FirstOrDefault();
            }
            else
            {
                var p = GolemApi.PosOf(c, arg, "attack");
                target = m.World.Golems.FirstOrDefault(g => g.Present && g.Pos == p);
            }
            if (target is null || target.Pos.Manhattan(m.Pos) != 1)
            {
                m.Busy = 1;
                return Value.False;
            }
            m.Intent = IntentKind.Attack;
            m.IntentDeclared = true;
            return Declare(m, ActionKind.Attack, Dir.North, target.Id, m.Def.WindupTicks, m.Def.WindupTicks + m.Def.AttackTicks);
        }, 0, 1, "attack(golem=None)", "Wind up and strike an adjacent golem.");

        yield return Action("idle", (in BuiltinCall c) =>
        {
            var m = M(c);
            var ticks = c.Count == 0 ? 2 : c.Int(0, "idle");
            if (!m.IntentDeclared) m.Intent = IntentKind.Idle;
            m.Busy = (int)Math.Clamp(ticks, 1, 1000);
            return Value.None;
        }, 0, 1, "idle(ticks=2)", "Do nothing for a while.");

        yield return Query("intend", (in BuiltinCall c) =>
        {
            var v = c.Arg(0);
            if (v.Kind != ValueKind.Enum || v.AsEnum.Type != GolemApi.Intent) throw c.Error($"intend() expects an Intent, got {v.Repr()}");
            var m = M(c);
            m.Intent = (IntentKind)v.AsEnum.Ordinal;
            m.IntentDeclared = true;
            return Value.None;
        }, 1, 1, "intend(Intent.X)", "Show what you are about to do.");

        yield return Query("nearest_golem", (in BuiltinCall c) =>
        {
            var m = M(c);
            var g = VisibleGolems(m).OrderBy(x => x.Pos.Manhattan(m.Pos)).ThenBy(x => x.Id).FirstOrDefault();
            return g is null ? Value.None : GolemApi.GolemRecord(g);
        }, 0, 0, "nearest_golem()", "The closest golem this monster can see, or None.", 2);

        yield return Query("adjacent_golem", (in BuiltinCall c) =>
        {
            var m = M(c);
            var g = m.World.Golems.Where(x => x.Present && x.Pos.Manhattan(m.Pos) == 1).OrderBy(x => x.Hp).ThenBy(x => x.Id).FirstOrDefault();
            return g is null ? Value.None : GolemApi.GolemRecord(g);
        }, 0, 0, "adjacent_golem()", "A golem next to this monster, or None.");

        yield return Query("golems", (in BuiltinCall c) => Value.List(VisibleGolems(M(c)).Select(GolemApi.GolemRecord)),
            0, 0, "golems()", "Every golem this monster can see.", 2);
        yield return Query("me", (in BuiltinCall c) => GolemApi.EnemyRecord(M(c)), 0, 0, "me()", "This monster's own record.");
        yield return Query("hp", (in BuiltinCall c) => Value.Int(M(c).Hp), 0, 0, "hp()", "Hit points.");
        yield return Query("distance", (in BuiltinCall c) => Value.Int(M(c).Pos.Manhattan(GolemApi.PosOf(c, c.Arg(0), "distance"))),
            1, 1, "distance(target)", "Steps to a target.");
        yield return Query("tick", (in BuiltinCall c) => Value.Int(M(c).World.Tick), 0, 0, "tick()", "The current tick.");
        yield return Query("rand", (in BuiltinCall c) =>
        {
            var n = c.Int(0, "rand");
            if (n < 1 || n > 1_000_000) throw c.Error("rand(n) needs 1 <= n <= 1000000", GlyphErrorKind.Value);
            return Value.Int(M(c).World.Rng.Monsters.Next((int)n));
        }, 1, 1, "rand(n)", "A seeded random number in [0, n).");
    }
}
