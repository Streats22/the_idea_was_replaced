namespace Delvework.Core.Content;

public enum DamageType
{
    Physical,
    Fire,
    Frost,
    Arcane,
}

public enum StatusKind
{
    Stun,
    Burn,
    Slow,
}

public sealed record Manifest
{
    public string Name { get; init; } = "";
    public string Version { get; init; } = "0.0.0";
}

/// <summary>A status effect applied on hit. <see cref="Chance"/> is in permille (1000 = always).</summary>
public sealed record StatusOnHit
{
    public StatusKind Status { get; init; }
    public int Ticks { get; init; }
    public int Chance { get; init; } = 1000;
}

public sealed record ChassisDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Hp { get; init; }
    public int Armor { get; init; }
    public int Attack { get; init; }
    public DamageType Damage { get; init; }
    public int Sight { get; init; }
    public int Budget { get; init; }
    public int Initiative { get; init; }
    public int MoveTicks { get; init; } = 2;
    public int AttackTicks { get; init; } = 4;
    public int RecallStones { get; init; } = 1;
    /// <summary>Resistance multipliers in percent per damage type; missing types are 100.</summary>
    public Dictionary<DamageType, int> Resist { get; init; } = [];
}

public sealed record MonsterDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Hp { get; init; }
    public int Armor { get; init; }
    public int Attack { get; init; }
    public DamageType Damage { get; init; }
    public int Sight { get; init; }
    public int Budget { get; init; } = 60;
    public int Initiative { get; init; }
    public int MoveTicks { get; init; } = 2;
    public int AttackTicks { get; init; } = 6;
    /// <summary>Ticks between declaring an attack (visible as intent) and it landing.</summary>
    public int WindupTicks { get; init; } = 1;
    public int[] Gold { get; init; } = [0, 0];
    /// <summary>Monster essence left in the remains, min and max.</summary>
    public int[] Essence { get; init; } = [0, 0];
    public Dictionary<DamageType, int> Resist { get; init; } = [];
    public StatusOnHit? OnHit { get; init; }
    public string Script { get; init; } = "";
    /// <summary>What the Almanac says once the monster has been seen.</summary>
    public string Lore { get; init; } = "";
    /// <summary>The pattern a player can exploit, shown once the monster is studied.</summary>
    public string Weakness { get; init; } = "";
}

public sealed record TrapDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Damage { get; init; }
    public DamageType DamageType { get; init; }
    public StatusOnHit? Status { get; init; }
}

public sealed record WeightedId
{
    public string Id { get; init; } = "";
    public int Weight { get; init; } = 1;
}

public sealed record RoomTemplate
{
    /// <summary><c>rect</c>, <c>pillars</c> or <c>cross</c>.</summary>
    public string Shape { get; init; } = "rect";
    public int Weight { get; init; } = 1;
}

public sealed record SpawnTable
{
    public int[] Count { get; init; } = [0, 0];
    public List<WeightedId> Table { get; init; } = [];
}

public sealed record ChestTable
{
    public int[] Count { get; init; } = [0, 0];
    public int[] Gold { get; init; } = [0, 0];
}

/// <summary>Veins of stone, iron ore and old timber in the walls; <see cref="Table"/> ids are resource ids.</summary>
public sealed record VeinTable
{
    public int[] Count { get; init; } = [0, 0];
    public int[] Amount { get; init; } = [2, 4];
    public List<WeightedId> Table { get; init; } = [];
}

public sealed record StratumDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Width { get; init; } = 48;
    public int Height { get; init; } = 32;
    public int[] Rooms { get; init; } = [6, 9];
    public int[] RoomSize { get; init; } = [4, 8];
    public int ExtraCorridors { get; init; } = 2;
    public List<RoomTemplate> Templates { get; init; } = [];
    public SpawnTable Monsters { get; init; } = new();
    public SpawnTable Traps { get; init; } = new();
    public ChestTable Chests { get; init; } = new();
    public VeinTable Veins { get; init; } = new();
    public int MaxTicks { get; init; } = 6000;
}

/// <summary>A hand-made floor drawn in ASCII (see <see cref="Dungeon.FloorLayout.FromRows"/>).</summary>
public sealed record FloorDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public List<string> Rows { get; init; } = [];
    /// <summary>Map letter to monster id.</summary>
    public Dictionary<string, string> Legend { get; init; } = [];
    public string Trap { get; init; } = "spikes";
    public int ChestGold { get; init; } = 10;
    /// <summary>How many times each vein (<c>%</c> stone, <c>&amp;</c> ore, <c>|</c> timber, <c>*</c> crystal) can be mined.</summary>
    public int VeinAmount { get; init; } = 3;
    public int MaxTicks { get; init; } = 3000;

    public Dungeon.FloorLayout ToLayout() =>
        Dungeon.FloorLayout.FromRows(Rows, Legend.ToDictionary(kv => kv.Key[0], kv => kv.Value), Trap, ChestGold, VeinAmount);
}

/// <summary>
/// A village workshop that runs the player's script: the Smelter turns ore into iron, the Bakery
/// wheat into bread. The rules and functions live in code (<see cref="Village.Workshops"/>); this is
/// the data around them.
/// </summary>
public sealed record WorkshopDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>Village skill node that builds it.</summary>
    public string Building { get; init; } = "";
    public string Summary { get; init; } = "";
    /// <summary>Ticks in one shift; the script stops when they run out.</summary>
    public int ShiftTicks { get; init; } = 100;
    /// <summary>Workshops work their shifts in this order, so the Farm's wheat reaches the Bakery the same day.</summary>
    public int Order { get; init; }
    /// <summary>Explanation pages shown in the workshop and its help window.</summary>
    public List<LessonPage> Pages { get; init; } = [];
    /// <summary>Filled from workshops/&lt;id&gt;.starter.glyph.</summary>
    public string Starter { get; init; } = "";
}

/// <summary>One block on a Codex page: a paragraph, a code sample (one string per line) or a highlighted tip.</summary>
public sealed record LessonBlock
{
    public string? Text { get; init; }
    public List<string>? Code { get; init; }
    public string? Tip { get; init; }
}

public sealed record LessonPage
{
    public string Title { get; init; } = "";
    public List<LessonBlock> Blocks { get; init; } = [];
}

public enum ChallengeGoal
{
    /// <summary>At least one golem reaches the stairs.</summary>
    Stairs,
    /// <summary>Every chest is opened and a golem reaches the stairs.</summary>
    Chests,
    /// <summary>At least <see cref="ChallengeDef.Gold"/> gold comes home (stairs or recall).</summary>
    Gold,
}

public sealed record ChallengeDef
{
    public string Floor { get; init; } = "";
    public string Brief { get; init; } = "";
    public ChallengeGoal Goal { get; init; }
    public int Gold { get; init; }
    /// <summary>Fail if any golem breaks.</summary>
    public bool NoLosses { get; init; }
    /// <summary>Largest allowed program, counting lines that are not blank or comments. 0 = no limit.</summary>
    public int MaxLines { get; init; }
    /// <summary>Language features the program must use (see <see cref="Progress.Challenge.FeaturesUsed"/>).</summary>
    public List<string> MustUse { get; init; } = [];
    public ulong Seed { get; init; } = 1;
    /// <summary>Chassis ids of the party, in order.</summary>
    public List<string> Party { get; init; } = ["warden"];
    /// <summary>Filled from codex/&lt;id&gt;.starter.glyph and codex/&lt;id&gt;.solution.glyph.</summary>
    public string Starter { get; init; } = "";
    public string Solution { get; init; } = "";
}

/// <summary>A Codex lesson: explanation pages, then a challenge that unlocks the next language tier.</summary>
public sealed record LessonDef
{
    public string Id { get; init; } = "";
    /// <summary>The language tier this lesson teaches; its challenge is written at this tier.</summary>
    public int Tier { get; init; }
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";
    public List<LessonPage> Pages { get; init; } = [];
    public ChallengeDef Challenge { get; init; } = new();
    /// <summary>Gold for completing the challenge the first time.</summary>
    public int Reward { get; init; }
    /// <summary>Gold to learn the feature at the Library. The first lesson is free.</summary>
    public int Cost { get; init; }
}

public enum SkillTree
{
    Village,
    Equipment,
    Arcana,
}

/// <summary>What owning a skill node gives. Everything adds up across owned nodes.</summary>
public sealed record SkillEffect
{
    public int Hp { get; init; }
    public int Armor { get; init; }
    public int Attack { get; init; }
    public int Sight { get; init; }
    public int Budget { get; init; }
    public int Speed { get; init; }
    public int RecallStones { get; init; }
    public int Mana { get; init; }
    public string? Spell { get; init; }
    /// <summary>Extra percent on gold brought home from delves.</summary>
    public int GoldPercent { get; init; }
    /// <summary>Flat gold after every delve.</summary>
    public int Income { get; init; }
    public int PartySize { get; init; }
    /// <summary>Materials the building delivers after every delve, by resource id.</summary>
    public Dictionary<string, int> Produce { get; init; } = [];
}

public sealed record SkillNode
{
    public string Id { get; init; } = "";
    public SkillTree Tree { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public int Cost { get; init; }
    /// <summary>Materials it costs on top of <see cref="Cost"/> gold, by resource id.</summary>
    public Dictionary<string, int> Price { get; init; } = [];
    public List<string> Requires { get; init; } = [];
    /// <summary>Language features that must be learned first (by count). 0 = none.</summary>
    public int Lessons { get; init; }
    public SkillEffect Effect { get; init; } = new();
    /// <summary>Town building this node restores or upgrades (Village tree), shown in the hub.</summary>
    public string? Building { get; init; }
    /// <summary>Position in the tree view: column (depth) and row.</summary>
    public int Col { get; init; }
    public int Row { get; init; }
}

public sealed record SkillTreeDef
{
    public SkillTree Id { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>Node that opens this tree (for example the Forge opens Equipment); null = always open.</summary>
    public string? OpenedBy { get; init; }
}

internal sealed record SkillsFile
{
    public List<SkillTreeDef> Trees { get; init; } = [];
    public List<SkillNode> Nodes { get; init; } = [];
}

/// <summary>A place the player can delve freely for gold once enough lessons are done.</summary>
public sealed record DelveSite
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>A stratum id (random floor per seed) or a floor id.</summary>
    public string Stratum { get; init; } = "";
    public int Lessons { get; init; }
    /// <summary>The highest feature tier whose rune tablet can lie on this site's floors.</summary>
    public int Tablets { get; init; }
}

/// <summary>
/// One Almanac entry: found once <see cref="Key"/> (a discovery like <c>trap:snare</c> or
/// <c>bakery:burnt</c>) has been counted <see cref="Count"/> times. Monster entries are made
/// from monsters.json instead (see <see cref="Progress.Almanac"/>).
/// </summary>
public sealed record AlmanacEntryDef
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>Shown while it's still unknown: a nudge toward how to find it.</summary>
    public string Hint { get; init; } = "";
    public string Text { get; init; } = "";
    public string Key { get; init; } = "";
    public int Count { get; init; } = 1;
    public int Reward { get; init; } = 5;
}

public sealed record CommissionGoal
{
    public string Resource { get; init; } = "";
    public int Amount { get; init; }
}

/// <summary>Medal thresholds, best first: gold, silver. Passing at all earns bronze.</summary>
public sealed record CommissionPar
{
    public int[] Ticks { get; init; } = [];
    public int[] Lines { get; init; } = [];
}

/// <summary>
/// A scored workshop puzzle: fixed stores, a goal, and medals for how fast (ticks) and how short
/// (lines) the script is. Commissions never touch the village stores; they pay a one-time reward.
/// </summary>
public sealed record CommissionDef
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Client { get; init; } = "";
    public string Brief { get; init; } = "";
    public string Workshop { get; init; } = "";
    public Dictionary<string, int> Stock { get; init; } = [];
    public CommissionGoal Goal { get; init; } = new();
    public CommissionPar Par { get; init; } = new();
    /// <summary>Paid the first time the goal is met, by resource id (gold included).</summary>
    public Dictionary<string, int> Reward { get; init; } = [];
    /// <summary>Extra gold for each gold medal, paid once per metric.</summary>
    public int GoldMedalBonus { get; init; } = 10;
    /// <summary>Filled from commissions/&lt;id&gt;.solution.glyph: proves the gold medals can be reached.</summary>
    public string Solution { get; init; } = "";
}

/// <summary>A named starting party: chassis id to program source.</summary>
public sealed record ExampleParty(string Name, IReadOnlyDictionary<string, string> Programs);

internal sealed record ExampleFile
{
    public string Name { get; init; } = "";
    public Dictionary<string, string> Programs { get; init; } = [];
}
