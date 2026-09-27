using Delvework.Core.Content;
using Delvework.Core.Dungeon;
using Delvework.Core.Glyph;

namespace Delvework.Core.Sim;

public sealed record PartyMember(string Name, string Chassis, string Source);

public enum OutcomeKind
{
    Success,
    Costly,
    Retreat,
    Wiped,
    Stalled,
}

/// <summary>
/// How a delve ended. <see cref="Goods"/> are the materials that came home, by resource id;
/// <see cref="Found"/> counts what the party saw and did for the Almanac (<c>seen:slime</c>, <c>killed:slime</c>, ...).
/// </summary>
public sealed record Outcome(OutcomeKind Kind, int Ticks, int Loot, IReadOnlyList<string> Broken, string Summary, IReadOnlyDictionary<string, int>? Goods = null, IReadOnlyDictionary<string, int>? Found = null)
{
    /// <summary>Lesson ids of the rune tablets the party brought home.</summary>
    public IReadOnlyList<string> Tablets { get; init; } = [];
}

/// <summary>
/// The whole simulation state of one delve. <see cref="Tick"/> advances it by one fixed step
/// (10 per simulated second). Deterministic: same content, seed and programs give the same hash.
/// </summary>
public sealed class World
{
    public const int TicksPerSecond = 10;
    public const int MarkLifetime = 600;
    public const int MaxMarks = 32;
    public const int MaxLog = 5000;
    public const int BurnInterval = 5;

    private readonly List<Golem> _golems = [];
    private readonly List<Monster> _monsters = [];
    private readonly List<Chest> _chests = [];
    private readonly List<Vein> _veins = [];
    private readonly List<Trap> _traps = [];
    private readonly List<Drop> _drops = [];
    private readonly List<Mark> _marks = [];
    private readonly List<LogEntry> _log = [];
    private readonly List<Effect> _effects = [];
    private readonly List<Actor> _resolveOrder = [];
    private readonly SortedDictionary<string, int> _found = new(StringComparer.Ordinal);
    private readonly HashSet<int> _spotted = [];
    private Pathfinder? _pathfinder;

    private World(ContentPack content, Grid grid, RngStreams rng)
    {
        Content = content;
        Grid = grid;
        Rng = rng;
        Known = new bool[grid.Size];
        Visible = new bool[grid.Size];
    }

    public ContentPack Content { get; }
    public Grid Grid { get; }
    public RngStreams Rng { get; }
    public int Tick { get; private set; }
    public int MaxTicks { get; init; } = 6000;
    public int Tier { get; init; } = Tiers.Max;
    /// <summary>Equipment and arcana bonuses. Null means none, with every spell name usable (tests and the CLI).</summary>
    public Loadout? Loadout { get; init; }
    public Pos Start { get; private set; }
    public Pos Stairs { get; private set; }
    public bool[] Known { get; private set; }
    public bool[] Visible { get; private set; }
    public int NextId { get; private set; } = 1;
    public long LogCount { get; private set; }
    public IReadOnlyList<Golem> Golems => _golems;
    public IReadOnlyList<Monster> Monsters => _monsters;
    public IReadOnlyList<Chest> Chests => _chests;
    public IReadOnlyList<Vein> Veins => _veins;
    public IReadOnlyList<Trap> Traps => _traps;
    public IReadOnlyList<Drop> Drops => _drops;
    public IReadOnlyList<Mark> Marks => _marks;
    public IReadOnlyList<LogEntry> Log => _log;
    /// <summary>Almanac discoveries so far, counted.</summary>
    public IReadOnlyDictionary<string, int> Found => _found;

    private void Discover(string key) => _found[key] = _found.GetValueOrDefault(key) + 1;
    /// <summary>Effects produced by the last <see cref="Step"/>, for presentation only.</summary>
    public IReadOnlyList<Effect> Effects => _effects;
    public Pathfinder Paths => _pathfinder ??= new Pathfinder(Grid);

    /// <summary>Optional override of every golem's and monster's per-tick budget (benchmarks).</summary>
    public int? BudgetOverride { get; set; }

    public bool Finished => Tick >= MaxTicks || !_golems.Exists(g => g.State == GolemState.Active);

    /// <summary>Build a world from a generated or handmade layout and a party.</summary>
    public static World Create(ContentPack content, FloorLayout layout, IReadOnlyList<PartyMember> party, ulong seed, int maxTicks = 6000, int tier = Tiers.Max, Loadout? loadout = null)
    {
        var w = new World(content, layout.Grid, new RngStreams(seed)) { MaxTicks = maxTicks, Tier = tier, Loadout = loadout };
        w.Start = layout.Start;
        w.Stairs = layout.Stairs;
        foreach (var c in layout.Chests) w._chests.Add(new Chest { Pos = c.Pos, Gold = c.Gold });
        foreach (var v in layout.Veins) w._veins.Add(new Vein { Pos = v.Pos, Kind = v.Kind, Amount = v.Amount, Left = v.Amount });
        foreach (var t in layout.Traps)
        {
            if (!content.Traps.TryGetValue(t.Id, out var def)) throw new ContentException($"Unknown trap '{t.Id}'");
            w._traps.Add(new Trap { Pos = t.Pos, Def = def });
        }
        foreach (var m in layout.Monsters) w.AddMonster(m.Id, m.Pos);
        foreach (var p in party) w.AddGolem(p);
        w.UpdateVision();
        return w;
    }

    private void AddMonster(string id, Pos pos)
    {
        if (!Content.Monsters.TryGetValue(id, out var def)) throw new ContentException($"Unknown monster '{id}'");
        var m = new Monster
        {
            Id = NextId++,
            Def = def,
            World = this,
            Pos = pos,
            Hp = def.Hp,
            MaxHp = def.Hp,
            Armor = def.Armor,
            Attack = def.Attack,
            Damage = def.Damage,
            Initiative = def.Initiative,
            Resist = def.Resist,
        };
        m.Vm = new Vm(Content.MonsterScripts[id], m);
        _monsters.Add(m);
    }

    private void AddGolem(PartyMember p)
    {
        if (!Content.Chassis.TryGetValue(p.Chassis, out var chassis))
        {
            throw new ContentException($"Unknown chassis '{p.Chassis}'. Known: {string.Join(", ", Content.Chassis.Keys.Order(StringComparer.Ordinal))}");
        }
        var spot = FreeSpotNear(Start);
        var gear = Loadout ?? Loadout.None;
        var g = new Golem
        {
            Id = NextId++,
            Name = p.Name,
            Chassis = chassis,
            World = this,
            Pos = spot,
            Hp = chassis.Hp + gear.Hp,
            MaxHp = chassis.Hp + gear.Hp,
            Armor = chassis.Armor + gear.Armor,
            Attack = chassis.Attack + gear.Attack,
            Damage = chassis.Damage,
            Initiative = chassis.Initiative,
            Resist = chassis.Resist,
            Sight = chassis.Sight + gear.Sight,
            Budget = chassis.Budget + gear.Budget,
            MoveTicks = Math.Max(1, chassis.MoveTicks - gear.Speed),
            RecallStones = chassis.RecallStones + gear.RecallStones,
            Mana = gear.Mana,
            MaxMana = gear.Mana,
            Visible = new bool[Grid.Size],
            SourceHash = Fnv.Mix(Fnv.Offset, p.Source).ToString("x16", System.Globalization.CultureInfo.InvariantCulture),
        };
        _golems.Add(g);
        try
        {
            var program = GlyphCompiler.Compile(p.Source, GolemApi.Environment, new CompileOptions
            {
                Tier = Tier,
                ModuleResolver = name => Content.Programs.TryGetValue(name, out var src) ? src : null,
                Locked = Loadout is null ? null : Spells.LockedFor(Loadout),
            });
            g.Vm = new Vm(program, g);
        }
        catch (GlyphError e)
        {
            Halt(g, e);
        }
    }

    private Pos FreeSpotNear(Pos p)
    {
        var path = Paths.Nearest(p, q => q == p ? false : !Grid.IsWall(q) && Occupant(q) is null && q != Stairs, q => !Grid.IsWall(q));
        if (Occupant(p) is null && !Grid.IsWall(p)) return p;
        return path is { Count: > 0 } ? path[^1] : p;
    }

    public void Say(string text, LogKind kind = LogKind.Info)
    {
        LogCount++;
        if (_log.Count >= MaxLog) _log.RemoveAt(0);
        _log.Add(new LogEntry(Tick, text, kind));
    }

    public Actor? Occupant(Pos p)
    {
        foreach (var g in _golems)
        {
            if (g.Present && g.Pos == p) return g;
        }
        foreach (var m in _monsters)
        {
            if (m.Alive && m.Pos == p) return m;
        }
        return null;
    }

    public Monster? MonsterAt(Pos p) => _monsters.Find(m => m.Alive && m.Pos == p);

    public Monster? MonsterById(long id) => _monsters.Find(m => m.Id == id && m.Alive);

    public Golem? GolemById(long id) => _golems.Find(g => g.Id == id);

    public Trap? TrapAt(Pos p) => _traps.Find(t => t.Pos == p);

    public Chest? ChestAt(Pos p) => _chests.Find(c => c.Pos == p);

    public Vein? VeinAt(Pos p) => _veins.Find(v => v.Pos == p);

    public bool IsKnown(Pos p) => Grid.InBounds(p) && Known[Grid.Idx(p)];

    /// <summary>Cooldown after an action, doubled while slowed.</summary>
    public static int Cooldown(Actor a, int ticks) => a.SlowTicks > 0 ? ticks * 2 : ticks;

    // ----- Golem pathing -----

    /// <summary>
    /// Passable for golem pathing: known, not a wall, optionally free of revealed traps and
    /// monsters. Teammates never block a path, because stuck golems swap places.
    /// </summary>
    public bool GolemPassable(Pos p, bool avoidTraps, bool avoidMonsters)
    {
        if (Grid.IsWall(p) || !Known[Grid.Idx(p)]) return false;
        if (avoidTraps && TrapAt(p) is { Revealed: true }) return false;
        return !avoidMonsters || MonsterAt(p) is null;
    }

    /// <summary>
    /// Route around revealed traps and monsters if possible, over a trap if that's the only
    /// way, and as a last resort toward the monster blocking the only corridor.
    /// </summary>
    public List<Pos>? GolemPath(Pos from, Pos to) =>
        Paths.AStar(from, to, p => GolemPassable(p, true, true))
        ?? Paths.AStar(from, to, p => GolemPassable(p, false, true))
        ?? Paths.AStar(from, to, p => GolemPassable(p, false, false));

    public List<Pos>? GolemPathToNearest(Pos from, Func<Pos, bool> goal) =>
        Paths.Nearest(from, goal, p => GolemPassable(p, true, true))
        ?? Paths.Nearest(from, goal, p => GolemPassable(p, false, true))
        ?? Paths.Nearest(from, goal, p => GolemPassable(p, false, false));

    public bool IsFrontier(Pos p)
    {
        if (!GolemPassable(p, false, false)) return false;
        foreach (var d in Dirs.All)
        {
            var n = p.Step(d);
            if (Grid.InBounds(n) && !Known[Grid.Idx(n)]) return true;
        }
        return false;
    }

    // ----- The tick -----

    /// <summary>
    /// One fixed step: timers and statuses, golem programs in party order, monster scripts,
    /// resolution (by initiative, then id), then vision, events and mark decay.
    /// </summary>
    public void Step()
    {
        if (Finished) return;
        Tick++;
        _effects.Clear();
        UpdateTimers();

        foreach (var g in _golems)
        {
            g.Ops = 0;
            if (g.State != GolemState.Active || g.Busy > 0 || g.StunTicks > 0 || g.Vm is null) continue;
            RunGolem(g);
        }

        foreach (var m in _monsters)
        {
            if (!m.Alive || m.Pending is not null || m.Busy > 0 || m.StunTicks > 0 || m.Vm is null || m.Vm.IsFinished) continue;
            RunMonster(m);
        }

        Resolve();
        UpdateVision();
        _marks.RemoveAll(mk => Tick - mk.CreatedTick >= MarkLifetime);
    }

    public void RunToEnd()
    {
        while (!Finished) Step();
    }

    private void UpdateTimers()
    {
        foreach (var a in AllActors())
        {
            if (!a.Present) continue;
            if (a.Busy > 0) a.Busy--;
            if (a.StunTicks > 0) a.StunTicks--;
            if (a.SlowTicks > 0) a.SlowTicks--;
            if (a is Golem g)
            {
                if (g.ShieldTicks > 0) g.ShieldTicks--;
                if (g.MaxMana > 0 && g.Mana < g.MaxMana && Tick % Spells.ManaRegenInterval == 0) g.Mana++;
            }
            if (a.BurnTicks > 0)
            {
                a.BurnTicks--;
                if (Tick % BurnInterval == 0)
                {
                    var dmg = Math.Max(0, a.ResistPercent(DamageType.Fire) / 100);
                    if (dmg > 0) ApplyDamage(a, dmg, DamageType.Fire, "burning");
                }
            }
        }
    }

    private IEnumerable<Actor> AllActors()
    {
        foreach (var g in _golems) yield return g;
        foreach (var m in _monsters) yield return m;
    }

    private void RunGolem(Golem g)
    {
        var budget = BudgetOverride ?? g.Budget;
        var r = g.Vm!.Step(budget);
        g.Ops = r.Used;
        g.Line = r.Line;
        foreach (var line in g.Vm.DrainOutput()) Say($"{g.Name}: {line}", LogKind.Output);
        switch (r.Status)
        {
            case VmStatus.Error:
                Halt(g, r.Error!);
                break;
            case VmStatus.Done:
                if (g.State == GolemState.Active)
                {
                    g.State = GolemState.Idle;
                    Say($"{g.Name}'s program finished");
                }
                break;
        }
    }

    private void RunMonster(Monster m)
    {
        m.IntentDeclared = false;
        var r = m.Vm!.Step(BudgetOverride ?? m.Def.Budget);
        m.Vm.DrainOutput();
        if (r.Status == VmStatus.Error)
        {
            Say($"The {m.Def.Name}'s script failed on line {r.Line}: {r.Error!.Message}", LogKind.Error);
            m.Intent = IntentKind.Idle;
            return;
        }
        if (m.Pending is { } p && !m.IntentDeclared)
        {
            m.Intent = p.Kind switch
            {
                ActionKind.Attack => IntentKind.Attack,
                ActionKind.Move => IntentKind.Move,
                _ => IntentKind.Idle,
            };
        }
    }

    public void Halt(Golem g, GlyphError e)
    {
        g.State = GolemState.Halted;
        g.Error = e;
        g.Line = e.Line;
        g.Pending = null;
        Say($"{g.Name}'s core sputters. Line {e.Line}: {e.Message}", LogKind.Error);
    }

    private void Resolve()
    {
        _resolveOrder.Clear();
        foreach (var a in AllActors())
        {
            if (a.Pending is { } p && p.DueTick <= Tick) _resolveOrder.Add(a);
        }
        _resolveOrder.Sort((a, b) => a.Initiative != b.Initiative ? b.Initiative.CompareTo(a.Initiative) : a.Id.CompareTo(b.Id));
        foreach (var a in _resolveOrder)
        {
            var p = a.Pending!;
            a.Pending = null;
            if (!a.Present) continue;
            if (a.StunTicks > 0)
            {
                if (a is Monster sm) sm.Vm?.ResolveAction(Value.False);
                else a.Vm?.ResolveAction(Value.False);
                continue;
            }
            var ok = a switch
            {
                Golem g => ResolveGolem(g, p),
                Monster m => ResolveMonster(m, p),
                _ => false,
            };
            a.Vm?.ResolveAction(Value.Bool(ok));
        }
    }

    private bool ResolveGolem(Golem g, PendingAction p)
    {
        switch (p.Kind)
        {
            case ActionKind.Move:
            {
                var moved = MoveGolem(g, p.Dir);
                g.Stuck = moved ? 0 : g.Stuck + 1;
                return moved || p.TargetId == GolemApi.ExploreStep;
            }
            case ActionKind.Attack:
            {
                var target = MonsterById(p.TargetId);
                if (target is null || target.Pos.Manhattan(g.Pos) != 1)
                {
                    Say($"{g.Name} swings at empty air", LogKind.Combat);
                    return false;
                }
                var dmg = Combat.Damage(g.Attack, g.Damage, target);
                _effects.Add(new Effect(EffectKind.Attack, g.Pos, target.Pos));
                Say($"{g.Name} hits the {target.Def.Name} for {dmg}", LogKind.Combat);
                ApplyDamage(target, dmg, g.Damage, g.Name);
                return true;
            }
            case ActionKind.Wait:
                g.Stuck++;
                return true;
            case ActionKind.Recall:
                if (g.RecallStones <= 0)
                {
                    Say($"{g.Name} has no Recall Stone left");
                    return false;
                }
                g.RecallStones--;
                g.State = GolemState.Recalled;
                _effects.Add(new Effect(EffectKind.Recall, g.Pos, g.Pos));
                Say($"{g.Name} recalls home with {g.Loot} gold");
                return true;
            case ActionKind.Heal or ActionKind.Bolt or ActionKind.Reveal or ActionKind.Shield:
                return Cast(g, p);
            case ActionKind.Mine:
            {
                g.Facing = p.Dir;
                var vein = VeinAt(g.Pos.Step(p.Dir));
                if (vein is not { Left: > 0 })
                {
                    Say(vein is null ? $"{g.Name} digs at bare rock" : $"{g.Name} digs, but that vein is empty");
                    return false;
                }
                vein.Left--;
                g.Bag[vein.Kind]++;
                Discover("mined:" + vein.Resource);
                _effects.Add(new Effect(EffectKind.Mine, g.Pos, vein.Pos));
                Say($"{g.Name} mines 1 {Resources.Name(vein.Resource)}" + (vein.Left == 0 ? " (the vein is empty)" : ""));
                return true;
            }
            default:
                return false;
        }
    }

    private bool Cast(Golem g, PendingAction p)
    {
        var spell = p.Kind switch
        {
            ActionKind.Heal => Spells.Heal,
            ActionKind.Bolt => Spells.Bolt,
            ActionKind.Reveal => Spells.Reveal,
            _ => Spells.Shield,
        };
        if (g.Mana < spell.Mana)
        {
            Say($"{g.Name} has too little mana for {spell.Id}() ({g.Mana} of {spell.Mana})");
            return false;
        }
        switch (p.Kind)
        {
            case ActionKind.Heal:
            {
                var healed = Math.Min(Spells.HealAmount, g.MaxHp - g.Hp);
                g.Hp += healed;
                if (g.Hp * 10 > g.MaxHp * 3) g.LowHpFired = false;
                _effects.Add(new Effect(EffectKind.Heal, g.Pos, g.Pos));
                Say($"{g.Name} casts heal: +{healed} HP");
                break;
            }
            case ActionKind.Bolt:
            {
                var target = MonsterById(p.TargetId);
                if (target is null || !GolemSees(g, target.Pos))
                {
                    Say($"{g.Name}'s bolt fizzles: the target is gone", LogKind.Combat);
                    return false;
                }
                var dmg = Combat.Damage(Spells.BoltDamage, DamageType.Arcane, target);
                _effects.Add(new Effect(EffectKind.Bolt, g.Pos, target.Pos));
                Say($"{g.Name}'s bolt hits the {target.Def.Name} for {dmg}", LogKind.Combat);
                ApplyDamage(target, dmg, DamageType.Arcane, g.Name);
                break;
            }
            case ActionKind.Reveal:
            {
                var found = 0;
                foreach (var t in _traps)
                {
                    if (t.Pos.Manhattan(g.Pos) > Spells.RevealRadius || t.Revealed) continue;
                    t.Revealed = true;
                    found++;
                }
                for (var y = g.Pos.Y - Spells.RevealRadius; y <= g.Pos.Y + Spells.RevealRadius; y++)
                {
                    for (var x = g.Pos.X - Spells.RevealRadius; x <= g.Pos.X + Spells.RevealRadius; x++)
                    {
                        var q = new Pos(x, y);
                        if (Grid.InBounds(q) && q.Manhattan(g.Pos) <= Spells.RevealRadius) Known[Grid.Idx(q)] = true;
                    }
                }
                _effects.Add(new Effect(EffectKind.Reveal, g.Pos, g.Pos));
                Say($"{g.Name} casts reveal: {found} hidden trap{(found == 1 ? "" : "s")} found");
                break;
            }
            default:
                g.ShieldTicks = Spells.ShieldTicks;
                _effects.Add(new Effect(EffectKind.Shield, g.Pos, g.Pos));
                Say($"{g.Name} casts shield");
                break;
        }
        g.Mana -= spell.Mana;
        return true;
    }

    private bool MoveGolem(Golem g, Dir dir)
    {
        g.Facing = dir;
        var n = g.Pos.Step(dir);
        if (Grid.IsWall(n)) return false;
        var other = Occupant(n);
        if (other is Monster) return false;
        if (other is Golem mate)
        {
            // Squeeze past a teammate that is stuck or not running; swapping with one walking
            // the same way would just shove it backwards.
            if (mate.State == GolemState.Active && mate.Stuck == 0) return false;
            mate.Pos = g.Pos;
        }
        g.Pos = n;
        EnterTile(g, n);
        return true;
    }

    private void EnterTile(Golem g, Pos n)
    {
        var trap = TrapAt(n);
        if (trap is not null)
        {
            trap.Revealed = true;
            Discover("trap:" + trap.Def.Id);
            _effects.Add(new Effect(EffectKind.Trap, n, n));
            Say($"{g.Name} triggers a {trap.Def.Name}!", LogKind.Combat);
            var dmg = Combat.Damage(trap.Def.Damage, trap.Def.DamageType, g);
            ApplyDamage(g, dmg, trap.Def.DamageType, trap.Def.Name);
            if (trap.Def.Status is { } st && g.Present) ApplyStatus(g, st);
            if (!g.Present) return;
        }
        var chest = ChestAt(n);
        if (chest is { Opened: false })
        {
            chest.Opened = true;
            g.Loot += chest.Gold;
            _effects.Add(new Effect(EffectKind.Chest, n, n));
            Say($"{g.Name} opens a chest: +{chest.Gold} gold");
        }
        for (var i = _drops.Count - 1; i >= 0; i--)
        {
            var d = _drops[i];
            var reach = d.Tablets is { Count: > 0 } ? 1 : 0;
            if (d.Pos.Manhattan(n) > reach) continue;
            g.Loot += d.Gold;
            g.Essence += d.Essence;
            if (d.Essence > 0) Discover("mined:" + Resources.Essence);
            var gains = new List<string>();
            if (d.Gold > 0) gains.Add($"+{d.Gold} gold");
            if (d.Essence > 0) gains.Add($"+{d.Essence} essence");
            foreach (var t in d.Tablets ?? [])
            {
                g.Tablets.Add(t);
                gains.Add($"the {TabletName(t)} rune");
                _effects.Add(new Effect(EffectKind.Tablet, d.Pos, d.Pos));
            }
            Say($"{g.Name} picks up {d.Label}" + (gains.Count > 0 ? $" ({string.Join(", ", gains)})" : ""));
            _drops.RemoveAt(i);
        }
        if (n == Stairs)
        {
            g.State = GolemState.Descended;
            _effects.Add(new Effect(EffectKind.Descend, n, n));
            Say($"{g.Name} descends the stairs with {g.Loot} gold");
        }
    }

    private bool ResolveMonster(Monster m, PendingAction p)
    {
        switch (p.Kind)
        {
            case ActionKind.Move:
            {
                var n = m.Pos.Step(p.Dir);
                if (Grid.IsWall(n) || Occupant(n) is not null || n == Stairs) return false;
                m.Pos = n;
                return true;
            }
            case ActionKind.Attack:
            {
                var target = GolemById(p.TargetId);
                if (target is null || !target.Present || target.Pos.Manhattan(m.Pos) != 1)
                {
                    Say($"The {m.Def.Name} strikes at empty air", LogKind.Combat);
                    return false;
                }
                var dmg = Combat.Damage(m.Attack, m.Damage, target);
                _effects.Add(new Effect(EffectKind.Attack, m.Pos, target.Pos));
                Say($"The {m.Def.Name} hits {target.Name} for {dmg}", LogKind.Combat);
                ApplyDamage(target, dmg, m.Damage, m.Def.Name);
                if (m.Def.OnHit is { } st && target.Present && Rng.Combat.Chance(st.Chance)) ApplyStatus(target, st);
                return true;
            }
            default:
                return true;
        }
    }

    public void ApplyStatus(Actor a, StatusOnHit st)
    {
        switch (st.Status)
        {
            case StatusKind.Stun:
                a.StunTicks = Math.Max(a.StunTicks, st.Ticks);
                break;
            case StatusKind.Burn:
                a.BurnTicks = Math.Max(a.BurnTicks, st.Ticks);
                break;
            case StatusKind.Slow:
                a.SlowTicks = Math.Max(a.SlowTicks, st.Ticks);
                break;
        }
        Say($"{(a is Monster ? "The " : "")}{a.Label} is {Combat.StatusWord(st.Status)}", LogKind.Combat);
        if (a is Golem g) g.Vm?.QueueEvent("status", null, Value.Str(st.Status.ToString()), Value.Int(st.Ticks));
    }

    public void ApplyDamage(Actor a, int amount, DamageType type, string source)
    {
        if (amount <= 0 || !a.Present) return;
        if (a is Golem { ShieldTicks: > 0 }) amount = (amount + 1) / 2;
        a.Hp -= amount;
        _effects.Add(new Effect(EffectKind.Hit, a.Pos, a.Pos));
        switch (a)
        {
            case Golem g:
                g.Vm?.QueueEvent("hurt", null, Value.Int(amount), Value.Str(source));
                if (!g.LowHpFired && g.Hp > 0 && g.Hp * 10 <= g.MaxHp * 3)
                {
                    g.LowHpFired = true;
                    g.Vm?.QueueEvent("low_hp", null);
                }
                if (g.Hp <= 0)
                {
                    g.Hp = 0;
                    g.State = GolemState.Broken;
                    g.Pending = null;
                    _drops.Add(new Drop(g.Pos, g.Loot, $"{g.Name}'s salvage", g.Essence, g.Tablets.Count > 0 ? [.. g.Tablets] : null));
                    _effects.Add(new Effect(EffectKind.Break, g.Pos, g.Pos));
                    Say($"{g.Name} breaks! Its salvage lies on the floor.", LogKind.Error);
                    g.Loot = 0;
                    g.Essence = 0;
                    g.Tablets.Clear();
                    Array.Clear(g.Bag);
                }
                break;
            case Monster m:
                m.Vm?.QueueEvent("hurt", null, Value.Int(amount), Value.Str(source));
                if (m.Hp <= 0)
                {
                    m.Hp = 0;
                    m.Alive = false;
                    m.Pending = null;
                    m.Intent = IntentKind.Idle;
                    var gold = Rng.Loot.Range(m.Def.Gold[0], m.Def.Gold[1]);
                    var essence = m.Def.Essence[1] > 0 ? Rng.Essence.Range(m.Def.Essence[0], m.Def.Essence[1]) : 0;
                    _drops.Add(new Drop(m.Pos, gold, $"{m.Def.Name} remains", essence));
                    Discover("killed:" + m.Def.Id);
                    _effects.Add(new Effect(EffectKind.Kill, m.Pos, m.Pos));
                    Say($"The {m.Def.Name} is destroyed", LogKind.Combat);
                }
                break;
        }
        _ = type;
    }

    // ----- Vision -----

    /// <summary>Recompute each golem's field of view, the party's fog memory, and queue <c>see</c> events.</summary>
    public void UpdateVision()
    {
        Array.Clear(Visible);
        foreach (var g in _golems)
        {
            if (!g.Present) continue;
            Array.Clear(g.Visible);
            var gv = g.Visible;
            var known = Known;
            var visible = Visible;
            Fov.Compute(Grid, g.Pos, g.Sight, i =>
            {
                gv[i] = true;
                visible[i] = true;
                known[i] = true;
            });
            if (g.State != GolemState.Active) continue;
            foreach (var m in _monsters)
            {
                var inView = m.Alive && gv[Grid.Idx(m.Pos)];
                if (inView && _spotted.Add(m.Id)) Discover("seen:" + m.Def.Id);
                if (inView && g.Seen.Add(m.Id))
                {
                    g.Vm?.QueueEvent("see", null, GolemApi.EnemyRecord(m));
                }
                else if (!inView)
                {
                    g.Seen.Remove(m.Id);
                }
            }
        }
    }

    public bool GolemSees(Golem g, Pos p) => Grid.InBounds(p) && g.Visible.Length > 0 && g.Visible[Grid.Idx(p)];

    public IEnumerable<Monster> VisibleMonsters(Golem g) => _monsters.Where(m => m.Alive && GolemSees(g, m.Pos));

    public void AddMark(Golem g, string label)
    {
        _marks.RemoveAll(m => m.Pos == g.Pos && m.OwnerId == g.Id);
        if (_marks.Count >= MaxMarks) _marks.RemoveAt(0);
        _marks.Add(new Mark(g.Pos, label, g.Id, Tick));
    }

    public bool RemoveMarks(Pos p) => _marks.RemoveAll(m => m.Pos == p) > 0;

    public void AddDrop(Drop d) => _drops.Add(d);

    private string TabletName(string lessonId) => Content.Lessons.FirstOrDefault(l => l.Id == lessonId)?.Title ?? lessonId;

    /// <summary>
    /// Lay a rune tablet on the way from the start to the stairs (a little past halfway), so a
    /// party heading for the stairs walks right by it. Returns where it lies, or null if there's no way.
    /// </summary>
    public Pos? PlaceTablet(string lessonId)
    {
        var path = Paths.AStar(Start, Stairs, p => !Grid.IsWall(p));
        if (path is null || path.Count < 3) return null;
        bool Free(Pos p) => p != Start && p != Stairs && !_chests.Exists(c => c.Pos == p) && !_traps.Exists(t => t.Pos == p)
            && !_monsters.Exists(m => m.Pos == p) && !_golems.Exists(g => g.Pos == p);
        var from = path.Count * 11 / 20;
        for (var k = 0; k < path.Count; k++)
        {
            var p = path[(from + k) % path.Count];
            if (!Free(p)) continue;
            _drops.Add(new Drop(p, 0, "a rune tablet", 0, [lessonId]));
            return p;
        }
        return null;
    }

    // ----- Outcome, hashing, cloning -----

    public Outcome Outcome()
    {
        var home = _golems.Where(g => g.State is GolemState.Descended or GolemState.Recalled).ToList();
        var loot = home.Sum(g => g.Loot);
        var broken = _golems.Where(g => g.State == GolemState.Broken).Select(g => g.Name).ToList();
        OutcomeKind kind;
        if (_golems.Exists(g => g.State == GolemState.Descended)) kind = broken.Count > 0 ? OutcomeKind.Costly : OutcomeKind.Success;
        else if (home.Count > 0) kind = OutcomeKind.Retreat;
        else if (broken.Count == _golems.Count && _golems.Count > 0) kind = OutcomeKind.Wiped;
        else kind = OutcomeKind.Stalled;
        var headline = kind switch
        {
            OutcomeKind.Success => "Floor cleared: the party found the stairs.",
            OutcomeKind.Costly => "Floor cleared, at a cost.",
            OutcomeKind.Retreat => "Retreat: the party recalled home.",
            OutcomeKind.Wiped => "Wiped: every golem broke.",
            _ => "Stalled: nobody reached the stairs.",
        };
        var goods = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var k = 0; k < Resources.Mined.Count; k++)
        {
            var n = home.Sum(g => g.Bag[k]);
            if (n > 0) goods[Resources.Mined[k]] = n;
        }
        var essence = home.Sum(g => g.Essence);
        if (essence > 0) goods[Resources.Essence] = essence;
        var tablets = home.SelectMany(g => g.Tablets).Distinct().ToList();
        var summary = $"{headline} {loot} gold came back.";
        if (tablets.Count > 0) summary += $" Rune found: {string.Join(", ", tablets.Select(TabletName))}.";
        if (goods.Count > 0) summary += $" Brought home: {Resources.Format(goods)}.";
        if (broken.Count > 0) summary += $" Salvaged: {string.Join(", ", broken)}.";
        var found = new SortedDictionary<string, int>(_found, StringComparer.Ordinal);
        foreach (var t in tablets) found["tablet:" + t] = 1;
        return new Outcome(kind, Tick, loot, broken, summary, goods, found) { Tablets = tablets };
    }

    public ulong StateHash()
    {
        var h = Fnv.Mix(Fnv.Offset, (long)Tick);
        h = Rng.StateHash(h);
        foreach (var g in _golems) h = g.Hash(h);
        foreach (var m in _monsters) h = m.Hash(h);
        foreach (var c in _chests) h = Fnv.Mix(h, c.Opened ? 1L : 0L);
        foreach (var v in _veins) h = Fnv.Mix(h, (long)v.Left);
        foreach (var t in _traps) h = Fnv.Mix(h, t.Revealed ? 1L : 0L);
        foreach (var d in _drops)
        {
            h = Fnv.Mix(Fnv.Mix(Fnv.Mix(h, (long)d.Pos.X), (long)d.Pos.Y), (long)d.Gold);
            foreach (var t in d.Tablets ?? []) h = Fnv.Mix(h, t);
        }
        foreach (var mk in _marks) h = Fnv.Mix(Fnv.Mix(h, mk.Label), (long)mk.CreatedTick);
        for (var i = 0; i < Known.Length; i += 64)
        {
            ulong word = 0;
            for (var b = 0; b < 64 && i + b < Known.Length; b++)
            {
                if (Known[i + b]) word |= 1UL << b;
            }
            h = Fnv.Mix(h, word);
        }
        return Fnv.Mix(h, LogCount);
    }

    /// <summary>Deep copy, including every VM. Used for replay keyframes.</summary>
    public World Clone()
    {
        var w = new World(Content, Grid, Rng.Clone())
        {
            MaxTicks = MaxTicks,
            Tier = Tier,
            Loadout = Loadout,
            Tick = Tick,
            Start = Start,
            Stairs = Stairs,
            Known = (bool[])Known.Clone(),
            Visible = (bool[])Visible.Clone(),
            NextId = NextId,
            LogCount = LogCount,
            BudgetOverride = BudgetOverride,
        };
        foreach (var g in _golems) w._golems.Add(g.CloneInto(w));
        foreach (var m in _monsters) w._monsters.Add(m.CloneInto(w));
        foreach (var c in _chests) w._chests.Add(new Chest { Pos = c.Pos, Gold = c.Gold, Opened = c.Opened });
        foreach (var v in _veins) w._veins.Add(new Vein { Pos = v.Pos, Kind = v.Kind, Amount = v.Amount, Left = v.Left });
        foreach (var t in _traps) w._traps.Add(new Trap { Pos = t.Pos, Def = t.Def, Revealed = t.Revealed });
        w._drops.AddRange(_drops);
        w._marks.AddRange(_marks);
        w._log.AddRange(_log);
        foreach (var (k, v) in _found) w._found[k] = v;
        w._spotted.UnionWith(_spotted);
        return w;
    }
}
