using Delvework.Core.Content;
using Delvework.Core.Glyph;
using Delvework.Core.Sim;
using Delvework.Core.Village;

namespace Delvework.Core.Progress;

public enum NodeStatus
{
    Owned,
    /// <summary>Requirements met and affordable.</summary>
    Available,
    /// <summary>Requirements met but not enough gold.</summary>
    TooExpensive,
    Locked,
}

/// <summary>
/// What one delve earns: gold that came home, the Market bonus, the flat village income, and
/// <see cref="Goods"/>: materials the golems mined plus what village buildings produced.
/// </summary>
public sealed record DelveReward(int Loot, int Bonus, int Income, int LessonReward, IReadOnlyDictionary<string, int>? Goods = null, IReadOnlyDictionary<string, int>? Found = null)
{
    public int Total => Loot + Bonus + Income + LessonReward;
}

/// <summary>
/// The rules of progression over a <see cref="Profile"/>: which lessons are open, what the
/// language tier is, what skill nodes cost and give, and how much gold a delve earns.
/// </summary>
public sealed class Progression(ContentPack content, Profile profile)
{
    /// <summary>Chassis join the party in this order as the Guild Hall grows.</summary>
    public static readonly IReadOnlyList<string> PartyOrder = ["warden", "seeker", "striker"];

    public ContentPack Content { get; } = content;
    public Profile Profile { get; } = profile;

    public IReadOnlyList<LessonDef> Lessons => Content.Lessons;

    // ----- Learning (features bought at the Library) -----

    /// <summary>A feature is learned when bought with gold. The first one (plain commands) is known from the start.</summary>
    public bool IsLearned(LessonDef lesson) => (Lessons.Count > 0 && lesson == Lessons[0]) || Profile.Learned.Contains(lesson.Id);

    /// <summary>Features are learned in ladder order, so this is also the language tier.</summary>
    public int LearnedCount => Lessons.TakeWhile(IsLearned).Count();

    /// <summary>The language tier for free delves: every learned feature.</summary>
    public int KnownTier => Math.Max(1, LearnedCount);

    /// <summary>The next feature to buy, or null when the whole language is known.</summary>
    public LessonDef? NextToLearn => Lessons.FirstOrDefault(l => !IsLearned(l));

    /// <summary>Owned when learned, Locked until the previous feature is learned, otherwise priced.</summary>
    public NodeStatus LearnStatus(LessonDef lesson)
    {
        if (IsLearned(lesson)) return NodeStatus.Owned;
        if (lesson != NextToLearn) return NodeStatus.Locked;
        return Profile.Gold >= lesson.Cost ? NodeStatus.Available : NodeStatus.TooExpensive;
    }

    public bool Learn(LessonDef lesson)
    {
        if (LearnStatus(lesson) != NodeStatus.Available) return false;
        Profile.Gold -= lesson.Cost;
        Profile.Learned.Add(lesson.Id);
        return true;
    }

    // ----- Challenges (optional practice floors) -----

    public bool IsCompleted(LessonDef lesson) => Profile.Completed.Contains(lesson.Id);

    /// <summary>A lesson's challenge can be played once its feature is learned.</summary>
    public bool IsOpen(LessonDef lesson) => IsLearned(lesson);

    public int CompletedCount => Lessons.Count(IsCompleted);

    /// <summary>The first learned lesson whose challenge isn't done yet, or null.</summary>
    public LessonDef? NextChallenge => Lessons.FirstOrDefault(l => IsLearned(l) && !IsCompleted(l));

    // ----- Skill trees -----

    public SkillNode? Node(string id) => Content.Skills.FirstOrDefault(n => n.Id == id);

    public bool Owns(string nodeId) => Profile.Owned.Contains(nodeId);

    public IEnumerable<SkillNode> OwnedNodes => Content.Skills.Where(n => Owns(n.Id));

    public bool IsTreeOpen(SkillTree tree)
    {
        var def = Content.Trees.FirstOrDefault(t => t.Id == tree);
        return def?.OpenedBy is not { } by || Owns(by);
    }

    /// <summary>Why a node can't be bought yet, or null if only gold (or nothing) stands in the way.</summary>
    public string? LockReason(SkillNode node)
    {
        if (!IsTreeOpen(node.Tree))
        {
            var by = Content.Trees.First(t => t.Id == node.Tree).OpenedBy!;
            return $"Build {Node(by)?.Name ?? by} in the Village first";
        }
        var missing = node.Requires.Where(r => !Owns(r)).Select(r => Node(r)?.Name ?? r).ToList();
        if (missing.Count > 0) return "Needs " + string.Join(" and ", missing);
        if (LearnedCount < node.Lessons)
        {
            var lesson = Lessons[node.Lessons - 1];
            return $"Learn {lesson.Title} at the Library first";
        }
        return null;
    }

    public NodeStatus Status(SkillNode node)
    {
        if (Owns(node.Id)) return NodeStatus.Owned;
        if (LockReason(node) is not null) return NodeStatus.Locked;
        return Missing(node).Count == 0 ? NodeStatus.Available : NodeStatus.TooExpensive;
    }

    /// <summary>Everything a node costs, gold included, by resource id.</summary>
    public static Dictionary<string, int> FullPrice(SkillNode node)
    {
        var d = new Dictionary<string, int>(node.Price, StringComparer.Ordinal);
        if (node.Cost > 0) d[Resources.Gold] = node.Cost;
        return d;
    }

    /// <summary>How much of each resource is still missing to buy a node (empty when affordable).</summary>
    public Dictionary<string, int> Missing(SkillNode node) => FullPrice(node)
        .Where(kv => Profile.Amount(kv.Key) < kv.Value)
        .ToDictionary(kv => kv.Key, kv => kv.Value - Profile.Amount(kv.Key), StringComparer.Ordinal);

    public bool Buy(SkillNode node)
    {
        if (Status(node) != NodeStatus.Available) return false;
        foreach (var (id, n) in FullPrice(node)) Profile.Add(id, -n);
        Profile.Owned.Add(node.Id);
        return true;
    }

    /// <summary>Materials village buildings deliver after every delve.</summary>
    public Dictionary<string, int> Production()
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var n in OwnedNodes)
        {
            foreach (var (id, amount) in n.Effect.Produce) d[id] = d.GetValueOrDefault(id) + amount;
        }
        return d;
    }

    private int Sum(Func<SkillEffect, int> pick) => OwnedNodes.Sum(n => pick(n.Effect));

    public Loadout Loadout() => new()
    {
        Hp = Sum(e => e.Hp),
        Armor = Sum(e => e.Armor),
        Attack = Sum(e => e.Attack),
        Sight = Sum(e => e.Sight),
        Budget = Sum(e => e.Budget),
        Speed = Sum(e => e.Speed),
        RecallStones = Sum(e => e.RecallStones),
        Mana = Sum(e => e.Mana),
        Spells = OwnedNodes.Select(n => n.Effect.Spell).OfType<string>().Distinct(StringComparer.Ordinal).ToList(),
    };

    public int PartySize => Math.Clamp(1 + Sum(e => e.PartySize), 1, PartyOrder.Count);

    public IReadOnlyList<string> Party => PartyOrder.Take(PartySize).ToList();

    public int GoldPercent => Sum(e => e.GoldPercent);

    public int Income => Sum(e => e.Income);

    /// <summary>Town buildings and their level (number of owned nodes that upgrade them).</summary>
    public IReadOnlyDictionary<string, int> Buildings() => OwnedNodes
        .Where(n => n.Building is not null)
        .GroupBy(n => n.Building!, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    // ----- Delving -----

    public bool IsOpen(DelveSite site) => LearnedCount >= site.Lessons;

    public IEnumerable<DelveSite> OpenSites => Content.Sites.Where(IsOpen);

    public DelveSetup SiteSetup(DelveSite site, ulong seed)
    {
        var party = Party.Select(id => new PartyMember(Content.Chassis[id].Name, id, DelveProgram(id))).ToList();
        return new DelveSetup(seed, site.Stratum, party, KnownTier, Loadout: Loadout());
    }

    /// <summary>A free delve: loot plus the Market bonus and village income, and the mined materials plus village production.</summary>
    public DelveReward SiteReward(Outcome outcome, DelveSite? site = null)
    {
        var found = new Dictionary<string, int>(outcome.Found ?? new Dictionary<string, int>(), StringComparer.Ordinal);
        if (site is not null && outcome.Kind is OutcomeKind.Success or OutcomeKind.Costly) found["cleared:" + site.Id] = 1;
        return new(outcome.Loot, outcome.Loot * GoldPercent / 100, Income, 0, Resources.Sum(outcome.Goods, Production()), found);
    }

    /// <summary>
    /// Gold for a lesson attempt: the first pass pays the lesson's reward plus the loot. Repeats
    /// and failed attempts pay nothing, so lessons can be retried freely.
    /// </summary>
    public DelveReward LessonReward(LessonDef lesson, ChallengeResult result, Outcome outcome) =>
        result.Passed && !IsCompleted(lesson) ? new(outcome.Loot, 0, 0, lesson.Reward, Found: outcome.Found) : new(0, 0, 0, 0, Found: outcome.Found);

    public void Apply(DelveReward reward)
    {
        Profile.Gold += reward.Total;
        Profile.GoldEarned += reward.Total;
        Profile.Add(reward.Goods);
        Profile.Delves++;
        Discover(reward.Found);
    }

    // ----- Almanac -----

    private readonly List<AlmanacEntryDef> _fresh = [];

    public IReadOnlyList<AlmanacEntryDef> AlmanacEntries => _entries ??= Almanac.Entries(Content);
    private IReadOnlyList<AlmanacEntryDef>? _entries;

    public bool IsFound(AlmanacEntryDef entry) => Profile.Almanac.Contains(entry.Id);

    public int FoundCount => AlmanacEntries.Count(IsFound);

    /// <summary>
    /// Count discoveries, then claim every entry that is now found: its gold is paid at once.
    /// Returns the new entries (also kept for <see cref="TakeFresh"/>).
    /// </summary>
    public List<AlmanacEntryDef> Discover(IReadOnlyDictionary<string, int>? found)
    {
        if (found is not null)
        {
            foreach (var (k, n) in found) Profile.Found[k] = Profile.Found.GetValueOrDefault(k) + n;
        }
        var fresh = AlmanacEntries.Where(e => !IsFound(e) && Almanac.IsFound(Profile, e)).ToList();
        foreach (var e in fresh)
        {
            Profile.Almanac.Add(e.Id);
            Profile.Gold += e.Reward;
            Profile.GoldEarned += e.Reward;
        }
        _fresh.AddRange(fresh);
        return fresh;
    }

    /// <summary>Entries found since the last call, for "new in the Almanac" notices.</summary>
    public List<AlmanacEntryDef> TakeFresh()
    {
        var list = _fresh.ToList();
        _fresh.Clear();
        return list;
    }

    // ----- Commissions -----

    public bool IsOpen(CommissionDef commission) => Workshop(commission.Workshop) is { } w && IsBuilt(w);

    public CommissionBest? Best(CommissionDef commission) => Profile.Commissions.GetValueOrDefault(commission.Id);

    public string CommissionProgram(CommissionDef commission) =>
        Profile.Programs.TryGetValue(Commissions.Slot(commission), out var src) ? src : Workshop(commission.Workshop)?.Starter ?? "";

    /// <summary>Score without keeping anything (the live preview).</summary>
    public CommissionScore TryCommission(CommissionDef commission, string source) => Commissions.Score(Content, commission, source, KnownTier);

    /// <summary>
    /// Score a commission and keep the best result per metric. The reward is paid the first time
    /// it passes, and the gold-medal bonus once per metric. Returns what was paid.
    /// </summary>
    public (CommissionScore Score, Dictionary<string, int> Paid) SubmitCommission(CommissionDef commission, string source)
    {
        var score = TryCommission(commission, source);
        var paid = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!score.Passed) return (score, paid);
        var best = Best(commission);
        if (best is null)
        {
            best = new CommissionBest { Ticks = score.Ticks, Lines = score.Lines };
            Profile.Commissions[commission.Id] = best;
            foreach (var (id, n) in commission.Reward) paid[id] = paid.GetValueOrDefault(id) + n;
        }
        if (score.TicksMedal == Medal.Gold && best.TicksMedal != Medal.Gold) paid[Resources.Gold] = paid.GetValueOrDefault(Resources.Gold) + commission.GoldMedalBonus;
        if (score.LinesMedal == Medal.Gold && best.LinesMedal != Medal.Gold) paid[Resources.Gold] = paid.GetValueOrDefault(Resources.Gold) + commission.GoldMedalBonus;
        best.Ticks = Math.Min(best.Ticks, score.Ticks);
        best.Lines = Math.Min(best.Lines, score.Lines);
        if (score.TicksMedal > best.TicksMedal) best.TicksMedal = score.TicksMedal;
        if (score.LinesMedal > best.LinesMedal) best.LinesMedal = score.LinesMedal;
        Profile.Add(paid);
        if (paid.TryGetValue(Resources.Gold, out var g)) Profile.GoldEarned += g;
        Discover(new Dictionary<string, int>(StringComparer.Ordinal) { ["commission:" + commission.Id] = 1 });
        return (score, paid);
    }

    public void Complete(LessonDef lesson)
    {
        if (!IsCompleted(lesson)) Profile.Completed.Add(lesson.Id);
    }

    // ----- Programs -----

    public string Program(string slot) => Profile.Programs.GetValueOrDefault(slot, "");

    /// <summary>The lesson's saved code per chassis, falling back to its starter code.</summary>
    public Dictionary<string, string> LessonSources(LessonDef lesson) => lesson.Challenge.Party.ToDictionary(
        id => id,
        id => Profile.Programs.TryGetValue(Profile.LessonSlot(lesson.Id, id), out var src) ? src : lesson.Challenge.Starter);

    /// <summary>Name of the bundled program every new golem starts with (content/programs).</summary>
    public const string FirstProgram = "first_delve";

    /// <summary>
    /// Code for free delves. Until the player writes their own, each golem starts with the
    /// player's code from the latest completed challenge, or the bundled first program. The
    /// earliest challenges are fixed paths through their own floor, so they don't count.
    /// </summary>
    public string DelveProgram(string chassis)
    {
        if (Profile.Programs.TryGetValue(Profile.DelveSlot(chassis), out var src)) return src;
        var last = Lessons.LastOrDefault(l => l.Tier >= Tiers.Conditions && IsCompleted(l));
        if (last is not null) return Profile.Programs.GetValueOrDefault(Profile.LessonSlot(last.Id, last.Challenge.Party[0]), last.Challenge.Solution);
        return Content.Programs.GetValueOrDefault(FirstProgram, "");
    }

    // ----- Workshops -----

    public WorkshopDef? Workshop(string id) => Content.Workshops.FirstOrDefault(w => w.Id == id);

    public bool IsBuilt(WorkshopDef workshop) => Owns(workshop.Building);

    public IEnumerable<WorkshopDef> BuiltWorkshops => Content.Workshops.Where(IsBuilt);

    /// <summary>The player's script for a workshop, or its starter.</summary>
    public string WorkshopProgram(WorkshopDef workshop) =>
        Profile.Programs.TryGetValue(Profile.WorkshopSlot(workshop.Id), out var src) ? src : workshop.Starter;

    /// <summary>Run one shift on the current stock without changing it (for the workshop preview).</summary>
    public WorkshopResult TryShift(WorkshopDef workshop, string? source = null) =>
        Workshops.Run(workshop, source ?? WorkshopProgram(workshop), Profile.Stock, KnownTier);

    /// <summary>Run one shift and move its materials into and out of the stock.</summary>
    public WorkshopResult Shift(WorkshopDef workshop, string? source = null)
    {
        var result = TryShift(workshop, source);
        foreach (var (id, n) in result.Used) Profile.Add(id, -n);
        Profile.Add(result.Made);
        Profile.Shifts++;
        var found = new Dictionary<string, int>(result.Found ?? new Dictionary<string, int>(), StringComparer.Ordinal);
        foreach (var (id, n) in result.Made) found["made:" + id] = found.GetValueOrDefault("made:" + id) + n;
        Discover(found);
        return result;
    }

    /// <summary>Every built workshop works one shift, in content order (they share the wood).</summary>
    public List<WorkshopResult> RunWorkshops() => BuiltWorkshops.Select(w => Shift(w)).ToList();
}
