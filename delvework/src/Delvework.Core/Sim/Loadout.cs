using Delvework.Core.Glyph;

namespace Delvework.Core.Sim;

/// <summary>
/// Bonuses every golem in the party gets from the player's equipment and arcana. Part of a
/// delve's inputs, so it is recorded in replays.
/// </summary>
public sealed record Loadout
{
    public int Hp { get; init; }
    public int Armor { get; init; }
    public int Attack { get; init; }
    public int Sight { get; init; }
    /// <summary>Extra instructions per tick.</summary>
    public int Budget { get; init; }
    /// <summary>Ticks taken off every step; a step never takes less than 1 tick.</summary>
    public int Speed { get; init; }
    public int RecallStones { get; init; }
    public int Mana { get; init; }
    /// <summary>Spell ids the golems know (see <see cref="Spells"/>).</summary>
    public List<string> Spells { get; init; } = [];

    public static Loadout None { get; } = new();

    public ulong Hash(ulong h)
    {
        foreach (var v in new[] { Hp, Armor, Attack, Sight, Budget, Speed, RecallStones, Mana }) h = Fnv.Mix(h, (long)v);
        foreach (var s in Spells.Order(StringComparer.Ordinal)) h = Fnv.Mix(h, s);
        return h;
    }
}

public sealed record SpellDef(string Id, int Mana, int CastTicks, string Signature, string Doc);

/// <summary>Arcana spells. Each is a golem builtin that costs mana and ends the turn like any action.</summary>
public static class Spells
{
    public const int HealAmount = 8;
    public const int BoltDamage = 5;
    public const int RevealRadius = 6;
    public const int ShieldTicks = 50;
    /// <summary>Golems with a mana pool regain 1 mana every this many ticks.</summary>
    public const int ManaRegenInterval = 30;

    public static readonly SpellDef Heal = new("heal", 4, 4, "heal()", $"Spell: restore {HealAmount} HP. Costs 4 mana.");
    public static readonly SpellDef Bolt = new("bolt", 3, 4, "bolt(enemy)", $"Spell: hit a visible enemy anywhere in sight for {BoltDamage} arcane damage. Costs 3 mana.");
    public static readonly SpellDef Reveal = new("reveal", 2, 3, "reveal()", $"Spell: reveal every trap and the layout within {RevealRadius} tiles. Costs 2 mana.");
    public static readonly SpellDef Shield = new("shield", 3, 2, "shield()", $"Spell: halve all damage you take for {ShieldTicks / World.TicksPerSecond} seconds. Costs 3 mana.");

    public static readonly IReadOnlyList<SpellDef> All = [Heal, Bolt, Reveal, Shield];

    public static SpellDef? Find(string id) => All.FirstOrDefault(s => s.Id == id);

    /// <summary>Compile-time locks for every spell the loadout does not include.</summary>
    public static Dictionary<string, string> LockedFor(Loadout loadout) => All
        .Where(s => !loadout.Spells.Contains(s.Id))
        .ToDictionary(s => s.Id, s => $"{s.Id}() is a spell your golems have not learned yet. Learn it in the Arcana tree at the Arcane Tower.");
}

public enum EffectKind
{
    Attack,
    Hit,
    Heal,
    Bolt,
    Reveal,
    Shield,
    Chest,
    Trap,
    Break,
    Kill,
    Descend,
    Recall,
    /// <summary>A golem at <see cref="Effect.From"/> digs at the vein at <see cref="Effect.To"/>.</summary>
    Mine,
    /// <summary>A rune tablet at <see cref="Effect.From"/> was picked up.</summary>
    Tablet,
}

/// <summary>Something worth showing or playing a sound for, produced during one tick. Not part of the simulation state.</summary>
public sealed record Effect(EffectKind Kind, Pos From, Pos To);
