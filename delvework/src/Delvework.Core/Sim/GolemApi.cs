using Delvework.Core.Content;
using Delvework.Core.Glyph;

namespace Delvework.Core.Sim;

/// <summary>The builtins a golem program can call. Queries read start-of-tick state; actions end the turn.</summary>
public static class GolemApi
{
    public const int SenseCost = 3;
    public const int PathBaseCost = 10;
    public const int StepPlanCost = 5;

    public static readonly EnumType Directions = new("Directions", Enum.GetNames<Dir>());
    public static readonly EnumType Intent = new("Intent", Enum.GetNames<IntentKind>());
    public static readonly EnumType DamageTypes = new("DamageType", Enum.GetNames<DamageType>());
    public static readonly EnumType ResourceKinds = new("Resource", [.. Resources.MinedEnumNames]);
    public const int MineTicks = 4;

    public static readonly RecordShape EnemyShape = new("Enemy", "id", "kind", "hp", "max_hp", "x", "y", "intent", "armor", "damage");
    public static readonly RecordShape GolemShape = new("Golem", "name", "chassis", "x", "y", "hp", "max_hp", "loot");
    public static readonly RecordShape TileShape = new("Tile", "x", "y", "wall", "trap", "enemy", "chest", "stairs");
    public static readonly RecordShape ChestShape = new("Chest", "x", "y", "gold");
    public static readonly RecordShape StairsShape = new("Stairs", "x", "y");
    public static readonly RecordShape MarkShape = new("Mark", "label", "x", "y", "age");
    public static readonly RecordShape VeinShape = new("Vein", "x", "y", "kind", "left");

    public static readonly string[] Events = ["see", "hurt", "low_hp", "signal", "status"];

    public static readonly GlyphEnvironment Environment = Build();

    private static GlyphEnvironment Build()
    {
        var env = Stdlib.AddTo(new GlyphEnvironment());
        env.AddEnum(Directions, exposeMembers: true);
        env.AddEnum(Intent);
        env.AddEnum(DamageTypes);
        env.AddEnum(ResourceKinds, exposeMembers: true);
        env.AddEvents(Events);
        foreach (var def in Definitions()) env.Add(def);
        return env.Freeze();
    }

    private static Golem G(in BuiltinCall c) => (Golem)c.Vm.Host!;

    public static Value EnemyRecord(Monster m) => Value.Record(new RecordObj(EnemyShape,
        Value.Int(m.Id), Value.Str(m.Def.Name), Value.Int(m.Hp), Value.Int(m.MaxHp), Value.Int(m.Pos.X), Value.Int(m.Pos.Y),
        Intent[m.Intent.ToString()].AsValue(), Value.Int(m.Armor), DamageTypes[m.Damage.ToString()].AsValue()));

    public static Value GolemRecord(Golem g) => Value.Record(new RecordObj(GolemShape,
        Value.Str(g.Name), Value.Str(g.Chassis.Name), Value.Int(g.Pos.X), Value.Int(g.Pos.Y), Value.Int(g.Hp), Value.Int(g.MaxHp), Value.Int(g.Loot)));

    public static Value PosList(Pos p) => Value.List([Value.Int(p.X), Value.Int(p.Y)]);

    /// <summary>Read a position from anything with x and y: an enemy, tile, chest, stairs, or [x, y].</summary>
    public static Pos PosOf(in BuiltinCall c, Value v, string fn)
    {
        if (v.Kind == ValueKind.Record)
        {
            var r = v.AsRecord;
            var x = r.Get("x");
            var y = r.Get("y");
            if (x.Kind == ValueKind.Int && y.Kind == ValueKind.Int) return new Pos((int)x.AsInt, (int)y.AsInt);
        }
        if (v.Kind == ValueKind.List && v.AsList.Items is [{ Kind: ValueKind.Int } lx, { Kind: ValueKind.Int } ly])
        {
            return new Pos((int)Math.Clamp(lx.AsInt, -1_000_000, 1_000_000), (int)Math.Clamp(ly.AsInt, -1_000_000, 1_000_000));
        }
        throw c.Error($"{fn}() needs something with a position (an enemy, a tile, stairs(), or [x, y]), got {v.Repr()}");
    }

    public static Dir DirOf(in BuiltinCall c, Value v, string fn)
    {
        if (v.Kind == ValueKind.Enum && v.AsEnum.Type == Directions) return (Dir)v.AsEnum.Ordinal;
        throw c.Error($"{fn}() expects North, East, South or West, got {v.Repr()}");
    }

    private static Value Declare(Golem g, ActionKind kind, Dir dir, int target, int ticks)
    {
        g.Pending = new PendingAction(kind, dir, target, g.World.Tick);
        g.Busy = World.Cooldown(g, ticks);
        return Value.False;
    }

    /// <summary>Marks an explore() step, which reports True even if something blocks the tile this tick.</summary>
    public const int ExploreStep = 1;

    private static Value StepAlong(Golem g, List<Pos>? path, int marker = 0)
    {
        if (path is not { Count: > 0 } || Dirs.Between(g.Pos, path[0]) is not Dir d)
        {
            g.Stuck++;
            g.Busy = 1;
            return Value.False;
        }
        return Declare(g, ActionKind.Move, d, marker, g.MoveTicks);
    }

    private static BuiltinDef Action(string name, BuiltinImpl impl, int min, int max, string sig, string doc, int cost = 1, string[]? kw = null) => new()
    {
        Name = name, Impl = impl, IsAction = true, MinArgs = min, MaxArgs = max, Cost = cost,
        Signature = sig, Doc = doc, Keywords = kw ?? [],
    };

    private delegate int SpellTarget(in BuiltinCall call);

    /// <summary>A spell action. Returns False at once (costing a tick) without enough mana or a valid target.</summary>
    private static BuiltinDef Spell(SpellDef spell, int args, SpellTarget target)
    {
        var kind = spell.Id switch
        {
            "heal" => ActionKind.Heal,
            "bolt" => ActionKind.Bolt,
            "reveal" => ActionKind.Reveal,
            _ => ActionKind.Shield,
        };
        return Action(spell.Id, (in BuiltinCall c) =>
        {
            var g = G(c);
            var id = target(c);
            if (g.Mana < spell.Mana || id < 0)
            {
                g.Busy = 1;
                return Value.False;
            }
            return Declare(g, kind, g.Facing, id, spell.CastTicks);
        }, args, args, spell.Signature, spell.Doc);
    }

    private static BuiltinDef Query(string name, BuiltinImpl impl, int min, int max, string sig, string doc, int cost = 1, int tier = Tiers.Calls) => new()
    {
        Name = name, Impl = impl, MinArgs = min, MaxArgs = max, Cost = cost, Tier = tier, Signature = sig, Doc = doc,
    };

    private static IEnumerable<BuiltinDef> Definitions()
    {
        yield return Action("move", (in BuiltinCall c) =>
        {
            var g = G(c);
            return Declare(g, ActionKind.Move, DirOf(c, c.Arg(0), "move"), 0, g.MoveTicks);
        }, 1, 1, "move(North | East | South | West)", "Step one tile. Returns False if blocked.");

        yield return Action("move_toward", (in BuiltinCall c) =>
        {
            var g = G(c);
            var target = PosOf(c, c.Arg(0), "move_toward");
            if (target == g.Pos) return StepAlong(g, null);
            var w = g.World;
            if (w.Grid.InBounds(target) && w.Grid.IsWall(target))
            {
                if (g.Pos.Manhattan(target) == 1) return StepAlong(g, null);
                return StepAlong(g, w.GolemPathToNearest(g.Pos, p => p.Manhattan(target) == 1 && !w.Grid.IsWall(p)));
            }
            return StepAlong(g, w.GolemPath(g.Pos, target));
        }, 1, 1, "move_toward(target)", "Step along the shortest known path toward an enemy, tile, vein or stairs(). Toward a wall (like a vein) it stops next to it and returns False.", StepPlanCost);

        yield return Action("explore", (in BuiltinCall c) =>
        {
            var g = G(c);
            var w = g.World;
            return StepAlong(g, w.GolemPathToNearest(g.Pos, w.IsFrontier), ExploreStep);
        }, 0, 0, "explore()", "Step toward the nearest unexplored area. Returns False only when everything is mapped.", StepPlanCost);

        yield return Action("attack", (in BuiltinCall c) =>
        {
            var g = G(c);
            var w = g.World;
            Monster? target;
            var arg = c.Arg(0);
            if (arg.IsNone)
            {
                target = w.Monsters.Where(m => m.Alive && m.Pos.Manhattan(g.Pos) == 1)
                    .OrderBy(m => m.Hp).ThenBy(m => m.Id).FirstOrDefault();
            }
            else
            {
                target = w.MonsterAt(PosOf(c, arg, "attack"));
            }
            if (target is null || target.Pos.Manhattan(g.Pos) != 1)
            {
                g.Busy = Math.Max(1, g.Chassis.AttackTicks / 2);
                return Value.False;
            }
            if (Dirs.Between(g.Pos, target.Pos) is Dir d) g.Facing = d;
            return Declare(g, ActionKind.Attack, g.Facing, target.Id, g.Chassis.AttackTicks);
        }, 0, 1, "attack(enemy=None)", "Hit an adjacent enemy (the weakest if none is given).");

        yield return Action("wait", (in BuiltinCall c) =>
        {
            var g = G(c);
            var ticks = c.Count == 0 ? 1 : c.Int(0, "wait");
            if (ticks < 1) throw c.Error("wait() needs at least 1 tick", GlyphErrorKind.Value);
            return Declare(g, ActionKind.Wait, g.Facing, 0, (int)Math.Min(ticks, 10_000));
        }, 0, 1, "wait(ticks=1)", "Do nothing for a number of ticks.");

        yield return Action("recall", (in BuiltinCall c) => Declare(G(c), ActionKind.Recall, G(c).Facing, 0, 1),
            0, 0, "recall()", "Use the Recall Stone: teleport home with your loot. One per delve.");

        yield return Spell(Spells.Heal, 0, (in BuiltinCall c) => 0);
        yield return Spell(Spells.Bolt, 1, (in BuiltinCall c) =>
        {
            var g = G(c);
            var e = c.Arg(0);
            if (e.Kind != ValueKind.Record || e.AsRecord.Shape != EnemyShape) throw c.Error($"bolt() needs an enemy, like bolt(nearest_enemy()), got {e.Repr()}");
            var m = g.World.MonsterById(e.AsRecord.Get("id").AsInt);
            return m is not null && g.World.GolemSees(g, m.Pos) ? m.Id : -1;
        });
        yield return Spell(Spells.Reveal, 0, (in BuiltinCall c) => 0);
        yield return Spell(Spells.Shield, 0, (in BuiltinCall c) => 0);

        yield return Action("mine", (in BuiltinCall c) =>
        {
            var g = G(c);
            var arg = c.Arg(0);
            Dir dir;
            if (arg.IsNone) dir = g.Facing;
            else if (arg.Kind == ValueKind.Enum && arg.AsEnum.Type == Directions) dir = (Dir)arg.AsEnum.Ordinal;
            else if (Dirs.Between(g.Pos, PosOf(c, arg, "mine")) is Dir d) dir = d;
            else
            {
                g.Busy = 1;
                return Value.False;
            }
            return Declare(g, ActionKind.Mine, dir, 0, MineTicks);
        }, 0, 1, "mine(target=facing)", "Dig one unit out of the vein next to you: a direction or a vein from nearest_vein(). Returns False if there's nothing to dig.");

        yield return Query("nearest_vein", (in BuiltinCall c) =>
        {
            var g = G(c);
            var w = g.World;
            var arg = c.Arg(0);
            int? kind = null;
            if (!arg.IsNone)
            {
                if (arg.Kind != ValueKind.Enum || arg.AsEnum.Type != ResourceKinds) throw c.Error($"nearest_vein() expects Stone, Ore, Wood or Crystal, got {arg.Repr()}");
                kind = arg.AsEnum.Ordinal;
            }
            var vein = w.Veins.Where(v => v.Left > 0 && (kind is null || v.Kind == kind) && w.IsKnown(v.Pos))
                .OrderBy(v => v.Pos.Manhattan(g.Pos)).ThenBy(v => v.Pos.Y).ThenBy(v => v.Pos.X).FirstOrDefault();
            return vein is null ? Value.None : Value.Record(new RecordObj(VeinShape,
                Value.Int(vein.Pos.X), Value.Int(vein.Pos.Y), ResourceKinds.Members[vein.Kind].AsValue(), Value.Int(vein.Left)));
        }, 0, 1, "nearest_vein(kind=None)", "The closest vein the party has seen that still has something in it (Stone, Ore, Wood or Crystal), or None. .x .y .kind .left", 2);

        yield return Query("carrying", (in BuiltinCall c) =>
        {
            var g = G(c);
            var arg = c.Arg(0);
            if (arg.IsNone) return Value.Int(g.Bag.Sum());
            if (arg.Kind != ValueKind.Enum || arg.AsEnum.Type != ResourceKinds) throw c.Error($"carrying() expects Stone, Ore, Wood or Crystal, got {arg.Repr()}");
            return Value.Int(g.Bag[arg.AsEnum.Ordinal]);
        }, 0, 1, "carrying(kind=None)", "How much Stone, Ore, Wood or Crystal you carry (everything if no kind is given). It only counts once you're home.");

        yield return Query("mana", (in BuiltinCall c) => Value.Int(G(c).Mana), 0, 0, "mana()", "Your mana. Spells cost mana; it refills slowly during a delve.");

        yield return Query("sense_ahead", (in BuiltinCall c) =>
        {
            var g = G(c);
            var w = g.World;
            var dir = c.Count == 0 ? g.Facing : DirOf(c, c.Arg(0), "sense_ahead");
            var p = g.Pos.Step(dir);
            var trap = w.TrapAt(p);
            if (trap is not null) trap.Revealed = true;
            return Value.Record(new RecordObj(TileShape,
                Value.Int(p.X), Value.Int(p.Y), Value.Bool(w.Grid.IsWall(p)), Value.Bool(trap is not null),
                Value.Bool(w.MonsterAt(p) is not null), Value.Bool(w.ChestAt(p) is { Opened: false }), Value.Bool(p == w.Stairs)));
        }, 0, 1, "sense_ahead(dir=facing)", "Inspect the next tile: .wall .trap .enemy .chest .stairs .x .y. Reveals traps.", SenseCost);

        yield return Query("path_to", (in BuiltinCall c) =>
        {
            var g = G(c);
            var target = PosOf(c, c.Arg(0), "path_to");
            var path = g.World.GolemPath(g.Pos, target);
            if (path is null) return Value.None;
            c.Vm.Charge(path.Count / 4);
            return Value.List(path.Select(PosList));
        }, 1, 1, "path_to(target)", "The known path to target as a list of [x, y], or None. Costs 10 + length / 4.", PathBaseCost, Tiers.Lists);

        yield return Query("signal", (in BuiltinCall c) =>
        {
            var g = G(c);
            var name = c.Str(0, "signal");
            var data = DeepCopy(c.Arg(1), 0);
            foreach (var other in g.World.Golems)
            {
                if (other != g && other.State == GolemState.Active) other.Vm?.QueueEvent("signal", name, data);
            }
            return Value.None;
        }, 1, 2, "signal(name, data=None)", "Wake teammates' on signal \"name\"(data): handlers.", 2, Tiers.Events);

        yield return Query("nearest_enemy", (in BuiltinCall c) =>
        {
            var g = G(c);
            var m = g.World.VisibleMonsters(g).OrderBy(x => x.Pos.Manhattan(g.Pos)).ThenBy(x => x.Id).FirstOrDefault();
            return m is null ? Value.None : EnemyRecord(m);
        }, 0, 0, "nearest_enemy()", "The closest visible enemy, or None.", 2);

        yield return Query("enemies", (in BuiltinCall c) =>
        {
            var g = G(c);
            return Value.List(g.World.VisibleMonsters(g).OrderBy(x => x.Pos.Manhattan(g.Pos)).ThenBy(x => x.Id).Select(EnemyRecord));
        }, 0, 0, "enemies()", "All visible enemies, nearest first.", 2, Tiers.Lists);

        yield return Query("enemy_alive", (in BuiltinCall c) =>
        {
            var e = c.Arg(0);
            if (e.Kind != ValueKind.Record || e.AsRecord.Shape != EnemyShape) throw c.Error("enemy_alive() needs an enemy");
            return Value.Bool(G(c).World.MonsterById(e.AsRecord.Get("id").AsInt) is not null);
        }, 1, 1, "enemy_alive(enemy)", "True while that enemy lives.");

        yield return Query("distance", (in BuiltinCall c) => Value.Int(G(c).Pos.Manhattan(PosOf(c, c.Arg(0), "distance"))),
            1, 1, "distance(target)", "Steps between you and a target (ignoring walls).");
        yield return Query("hp", (in BuiltinCall c) => Value.Int(G(c).Hp), 0, 0, "hp()", "Your hit points.");
        yield return Query("max_hp", (in BuiltinCall c) => Value.Int(G(c).MaxHp), 0, 0, "max_hp()", "Your maximum hit points.");
        yield return Query("loot", (in BuiltinCall c) => Value.Int(G(c).Loot), 0, 0, "loot()", "Gold you carry.");
        yield return Query("me", (in BuiltinCall c) => GolemRecord(G(c)), 0, 0, "me()", "Your own record: .x .y .hp .loot.");
        yield return Query("tick", (in BuiltinCall c) => Value.Int(G(c).World.Tick), 0, 0, "tick()", "The current tick (10 per second).");

        yield return Query("ally", (in BuiltinCall c) =>
        {
            var g = G(c);
            var other = g.World.Golems.Where(x => x != g && x.Present)
                .OrderBy(x => x.Pos.Manhattan(g.Pos)).ThenBy(x => x.Id).FirstOrDefault();
            return other is null ? Value.None : GolemRecord(other);
        }, 0, 0, "ally()", "The nearest other golem, or None.", 2);

        yield return Query("nearest_chest", (in BuiltinCall c) =>
        {
            var g = G(c);
            var w = g.World;
            var chest = w.Chests.Where(x => !x.Opened && w.IsKnown(x.Pos))
                .OrderBy(x => x.Pos.Manhattan(g.Pos)).ThenBy(x => x.Pos.Y).ThenBy(x => x.Pos.X).FirstOrDefault();
            return chest is null ? Value.None : Value.Record(new RecordObj(ChestShape, Value.Int(chest.Pos.X), Value.Int(chest.Pos.Y), Value.Int(chest.Gold)));
        }, 0, 0, "nearest_chest()", "The closest unopened chest the party has seen, or None.", 2);

        yield return Query("stairs", (in BuiltinCall c) =>
        {
            var w = G(c).World;
            return w.IsKnown(w.Stairs) ? Value.Record(new RecordObj(StairsShape, Value.Int(w.Stairs.X), Value.Int(w.Stairs.Y))) : Value.None;
        }, 0, 0, "stairs()", "The stairs once the party has seen them, else None.");

        yield return Query("mark", (in BuiltinCall c) =>
        {
            var g = G(c);
            var label = c.Str(0, "mark");
            if (label.Length > 40) throw c.Error("mark() labels are limited to 40 characters", GlyphErrorKind.Value);
            g.World.AddMark(g, label);
            return Value.None;
        }, 1, 1, "mark(label)", "Leave a mark on your tile for the party. Marks fade after 60 seconds.", 2, Tiers.Variables);

        yield return Query("marks", (in BuiltinCall c) =>
        {
            var w = G(c).World;
            return Value.List(w.Marks.Select(m => Value.Record(new RecordObj(MarkShape,
                Value.Str(m.Label), Value.Int(m.Pos.X), Value.Int(m.Pos.Y), Value.Int(w.Tick - m.CreatedTick)))));
        }, 0, 0, "marks()", "Every mark the party has left.", 2, Tiers.Lists);

        yield return Query("smell", (in BuiltinCall c) =>
        {
            var g = G(c);
            var w = g.World;
            var label = c.Str(0, "smell");
            var near = w.Marks.Where(m => m.Label == label && m.Pos != g.Pos && m.Pos.Manhattan(g.Pos) <= SmellRange).Select(m => m.Pos).ToHashSet();
            if (near.Count == 0) return Value.None;
            var path = w.GolemPathToNearest(g.Pos, near.Contains);
            return path is { Count: > 0 } && Dirs.Between(g.Pos, path[0]) is Dir d ? Directions.Members[(int)d].AsValue() : Value.None;
        }, 1, 1, "smell(label)", $"The direction (North, East, South or West) of the first step toward the nearest mark with this label within {SmellRange} tiles, or None. Follow it with move(smell(\"ore\")) to walk a trail a teammate left.", 3, Tiers.Variables);

        yield return Query("unmark", (in BuiltinCall c) =>
        {
            var g = G(c);
            return Value.Bool(g.World.RemoveMarks(g.Pos));
        }, 0, 0, "unmark()", "Wipe every mark on your tile. Returns True if there was one.", 1, Tiers.Variables);

        yield return Query("party", (in BuiltinCall c) =>
        {
            var g = G(c);
            return Value.List(g.World.Golems.Where(x => x.Present).Select(GolemRecord));
        }, 0, 0, "party()", "Every golem still in the dungeon, you included: .name .chassis .x .y .hp .max_hp .loot", 2, Tiers.Lists);

        yield return Query("essence", (in BuiltinCall c) => Value.Int(G(c).Essence), 0, 0, "essence()", "Monster essence you carry. Defeated monsters leave it in their remains.");
    }

    public const int SmellRange = 8;

    /// <summary>Copy signal data so two golems never share a mutable list.</summary>
    public static Value DeepCopy(Value v, int depth)
    {
        if (depth > 16) return Value.None;
        switch (v.Kind)
        {
            case ValueKind.List:
                return Value.List(v.AsList.Items.Select(x => DeepCopy(x, depth + 1)));
            case ValueKind.Dict:
            {
                var d = new DictObj();
                var src = v.AsDict;
                for (var i = 0; i < src.Count; i++) d.Set(src.KeyAt(i), DeepCopy(src.ValueAt(i), depth + 1));
                return Value.Dict(d);
            }
            case ValueKind.Iterator:
            case ValueKind.Builtin when v.AsBuiltin.Self.Kind != ValueKind.None:
                return Value.None;
            default:
                return v;
        }
    }
}
