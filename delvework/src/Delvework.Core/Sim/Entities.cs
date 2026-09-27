using Delvework.Core.Content;
using Delvework.Core.Glyph;

namespace Delvework.Core.Sim;

public enum GolemState
{
    Active,
    /// <summary>The program finished normally.</summary>
    Idle,
    /// <summary>The program failed to compile or hit a runtime error.</summary>
    Halted,
    Broken,
    Recalled,
    Descended,
}

/// <summary>What a monster will do next; readable by golems through <c>enemy.intent</c>.</summary>
public enum IntentKind
{
    Idle,
    Move,
    Chase,
    Attack,
    Flee,
}

public enum ActionKind
{
    Move,
    Attack,
    Wait,
    Recall,
    Heal,
    Bolt,
    Reveal,
    Shield,
    Mine,
}

/// <summary>An action declared during the program phase and applied in the resolution phase.</summary>
public sealed record PendingAction(ActionKind Kind, Dir Dir, int TargetId, int DueTick)
{
    public ulong Hash(ulong h) => Fnv.Mix(Fnv.Mix(Fnv.Mix(Fnv.Mix(h, (long)Kind), (long)Dir), (long)TargetId), (long)DueTick);
}

public abstract class Actor
{
    public int Id { get; init; }
    public Pos Pos { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; init; }
    public int Armor { get; init; }
    public int Attack { get; init; }
    public DamageType Damage { get; init; }
    public int Initiative { get; init; }
    public IReadOnlyDictionary<DamageType, int> Resist { get; init; } = new Dictionary<DamageType, int>();
    /// <summary>Ticks until the actor may act again.</summary>
    public int Busy { get; set; }
    public int StunTicks { get; set; }
    public int BurnTicks { get; set; }
    public int SlowTicks { get; set; }
    public PendingAction? Pending { get; set; }
    public Vm? Vm { get; set; }
    public abstract string Label { get; }
    public abstract bool Present { get; }

    public int ResistPercent(DamageType t) => Resist.TryGetValue(t, out var pct) ? pct : 100;

    protected void CopyActorTo(Actor other)
    {
        other.Pos = Pos;
        other.Hp = Hp;
        other.Busy = Busy;
        other.StunTicks = StunTicks;
        other.BurnTicks = BurnTicks;
        other.SlowTicks = SlowTicks;
        other.Pending = Pending;
    }

    public virtual ulong Hash(ulong h)
    {
        h = Fnv.Mix(h, (long)Id);
        h = Fnv.Mix(Fnv.Mix(h, (long)Pos.X), (long)Pos.Y);
        h = Fnv.Mix(Fnv.Mix(h, (long)Hp), (long)Busy);
        h = Fnv.Mix(Fnv.Mix(Fnv.Mix(h, (long)StunTicks), (long)BurnTicks), (long)SlowTicks);
        h = Pending?.Hash(h) ?? Fnv.Mix(h, 0L);
        return Vm is null ? h : Fnv.Mix(h, Vm.StateHash());
    }
}

public sealed class Golem : Actor
{
    public required string Name { get; init; }
    public required ChassisDef Chassis { get; init; }
    public required World World { get; set; }
    public int Sight { get; init; }
    public int Budget { get; set; }
    public int MoveTicks { get; init; } = 2;
    public int Loot { get; set; }
    /// <summary>Mined materials carried, indexed like <see cref="Content.Resources.Mined"/>.</summary>
    public int[] Bag { get; private set; } = new int[Content.Resources.Mined.Count];
    /// <summary>Monster essence picked up from remains.</summary>
    public int Essence { get; set; }
    public int RecallStones { get; set; }
    public int Mana { get; set; }
    public int MaxMana { get; init; }
    public int ShieldTicks { get; set; }
    public GolemState State { get; set; }
    public Dir Facing { get; set; } = Dir.East;
    public int Stuck { get; set; }
    public bool LowHpFired { get; set; }
    public SortedSet<int> Seen { get; private set; } = [];
    public bool[] Visible { get; set; } = [];
    public int Line { get; set; }
    public int Ops { get; set; }
    public GlyphError? Error { get; set; }
    public string SourceHash { get; init; } = "";

    public override string Label => Name;
    public override bool Present => State is GolemState.Active or GolemState.Idle or GolemState.Halted;

    public Golem CloneInto(World world)
    {
        var g = new Golem
        {
            Id = Id,
            Name = Name,
            Chassis = Chassis,
            World = world,
            MaxHp = MaxHp,
            Armor = Armor,
            Attack = Attack,
            Damage = Damage,
            Initiative = Initiative,
            Resist = Resist,
            Sight = Sight,
            Budget = Budget,
            MoveTicks = MoveTicks,
            Loot = Loot,
            Bag = (int[])Bag.Clone(),
            Essence = Essence,
            RecallStones = RecallStones,
            Mana = Mana,
            MaxMana = MaxMana,
            ShieldTicks = ShieldTicks,
            State = State,
            Facing = Facing,
            Stuck = Stuck,
            LowHpFired = LowHpFired,
            Seen = new SortedSet<int>(Seen),
            Visible = (bool[])Visible.Clone(),
            Line = Line,
            Ops = Ops,
            Error = Error,
            SourceHash = SourceHash,
        };
        CopyActorTo(g);
        g.Vm = Vm?.Clone(g);
        return g;
    }

    public override ulong Hash(ulong h)
    {
        h = base.Hash(h);
        h = Fnv.Mix(Fnv.Mix(Fnv.Mix(h, (long)Loot), (long)RecallStones), (long)State);
        h = Fnv.Mix(Fnv.Mix(Fnv.Mix(h, (long)Facing), (long)Stuck), LowHpFired ? 1L : 0L);
        h = Fnv.Mix(Fnv.Mix(h, (long)Mana), (long)ShieldTicks);
        foreach (var s in Seen) h = Fnv.Mix(h, (long)s);
        foreach (var b in Bag) h = Fnv.Mix(h, (long)b);
        return Fnv.Mix(h, (long)Essence);
    }
}

public sealed class Monster : Actor
{
    public required MonsterDef Def { get; init; }
    public required World World { get; set; }
    public bool Alive { get; set; } = true;
    public IntentKind Intent { get; set; }
    /// <summary>Set when the script calls <c>intend()</c> during its current step.</summary>
    public bool IntentDeclared { get; set; }
    public int Sight => Def.Sight;

    public override string Label => Def.Name;
    public override bool Present => Alive;

    public Monster CloneInto(World world)
    {
        var m = new Monster
        {
            Id = Id,
            Def = Def,
            World = world,
            MaxHp = MaxHp,
            Armor = Armor,
            Attack = Attack,
            Damage = Damage,
            Initiative = Initiative,
            Resist = Resist,
            Alive = Alive,
            Intent = Intent,
        };
        CopyActorTo(m);
        m.Vm = Vm?.Clone(m);
        return m;
    }

    public override ulong Hash(ulong h) => Fnv.Mix(Fnv.Mix(base.Hash(h), Alive ? 1L : 0L), (long)Intent);
}

public sealed class Chest
{
    public Pos Pos { get; init; }
    public int Gold { get; init; }
    public bool Opened { get; set; }
}

/// <summary>
/// A wall tile with something worth digging out: stone, iron ore or old timber. Golems next to it
/// call <c>mine()</c>; each dig takes one unit until <see cref="Left"/> runs out.
/// </summary>
public sealed class Vein
{
    public Pos Pos { get; init; }
    /// <summary>Index into <see cref="Content.Resources.Mined"/>.</summary>
    public int Kind { get; init; }
    public int Amount { get; init; }
    public int Left { get; set; }

    public string Resource => Content.Resources.Mined[Kind];
}

public sealed class Trap
{
    public Pos Pos { get; init; }
    public required TrapDef Def { get; init; }
    public bool Revealed { get; set; }
}

public sealed record Drop(Pos Pos, int Gold, string Label, int Essence = 0);

public sealed record Mark(Pos Pos, string Label, int OwnerId, int CreatedTick);

public enum LogKind
{
    Info,
    Combat,
    Error,
    Output,
}

public sealed record LogEntry(int Tick, string Text, LogKind Kind);
