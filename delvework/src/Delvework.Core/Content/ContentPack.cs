using System.Text.Json;
using System.Text.Json.Serialization;
using Delvework.Core.Glyph;
using Delvework.Core.Sim;

namespace Delvework.Core.Content;

public sealed class ContentException(string message) : Exception(message);

/// <summary>
/// All data-driven game content: chassis, monsters (with compiled scripts), traps, strata and
/// sample programs. <see cref="Version"/> combines the manifest version with a hash of every file,
/// so replays can detect content drift.
/// </summary>
public sealed class ContentPack
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public required Manifest Manifest { get; init; }
    public required IReadOnlyDictionary<string, ChassisDef> Chassis { get; init; }
    public required IReadOnlyDictionary<string, MonsterDef> Monsters { get; init; }
    public required IReadOnlyDictionary<string, TrapDef> Traps { get; init; }
    public required IReadOnlyDictionary<string, StratumDef> Strata { get; init; }
    public required IReadOnlyDictionary<string, GlyphProgram> MonsterScripts { get; init; }
    /// <summary>Monster script source by monster id, for the Almanac.</summary>
    public IReadOnlyDictionary<string, string> MonsterSources { get; init; } = new Dictionary<string, string>();
    /// <summary>Sample golem programs by file name (without extension).</summary>
    public required IReadOnlyDictionary<string, string> Programs { get; init; }
    public IReadOnlyDictionary<string, FloorDef> Floors { get; init; } = new Dictionary<string, FloorDef>();
    /// <summary>Starting parties from examples/examples.json, in file order.</summary>
    public IReadOnlyList<ExampleParty> Examples { get; init; } = [];
    /// <summary>Codex lessons ordered by tier.</summary>
    public IReadOnlyList<LessonDef> Lessons { get; init; } = [];
    public IReadOnlyList<SkillTreeDef> Trees { get; init; } = [];
    public IReadOnlyList<SkillNode> Skills { get; init; } = [];
    public IReadOnlyList<DelveSite> Sites { get; init; } = [];
    /// <summary>Village workshops in file order (by id).</summary>
    public IReadOnlyList<WorkshopDef> Workshops { get; init; } = [];
    public IReadOnlyList<AlmanacEntryDef> AlmanacEntries { get; init; } = [];
    public IReadOnlyList<CommissionDef> Commissions { get; init; } = [];
    public required ulong Hash { get; init; }
    public string Directory { get; init; } = "";

    public string Version => $"{Manifest.Version}+{Hash:x16}";

    public static ContentPack Load(string directory)
    {
        if (!System.IO.Directory.Exists(directory)) throw new ContentException($"Content folder not found: {directory}");
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in System.IO.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(path);
            if (ext is not (".json" or ".glyph")) continue;
            var rel = Path.GetRelativePath(directory, path).Replace('\\', '/');
            files[rel] = File.ReadAllText(path);
        }
        return FromFiles(files, directory);
    }

    /// <summary>Build a pack from relative paths and contents (used by tests and embedded content).</summary>
    public static ContentPack FromFiles(IReadOnlyDictionary<string, string> files, string directory = "")
    {
        files = files.ToDictionary(f => f.Key, f => f.Value.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparer.Ordinal);
        var hash = Fnv.Offset;
        foreach (var (name, text) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            hash = Fnv.Mix(Fnv.Mix(hash, name), text);
        }

        string Need(string name) => files.TryGetValue(name, out var t) ? t : throw new ContentException($"Missing content file: {name}");
        T Parse<T>(string name, string text)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(text, Json) ?? throw new ContentException($"{name} is empty");
            }
            catch (JsonException e)
            {
                throw new ContentException($"{name}: {e.Message}");
            }
        }

        var manifest = Parse<Manifest>("manifest.json", Need("manifest.json"));
        var chassis = ById(Parse<List<ChassisDef>>("chassis.json", Need("chassis.json")), c => c.Id, "chassis");
        var monsters = ById(Parse<List<MonsterDef>>("monsters.json", Need("monsters.json")), m => m.Id, "monster");
        var traps = ById(Parse<List<TrapDef>>("traps.json", Need("traps.json")), t => t.Id, "trap");
        var strata = new Dictionary<string, StratumDef>();
        foreach (var (name, text) in files.Where(f => f.Key.StartsWith("strata/", StringComparison.Ordinal) && f.Key.EndsWith(".json", StringComparison.Ordinal)))
        {
            var s = Parse<StratumDef>(name, text);
            if (!strata.TryAdd(s.Id, s)) throw new ContentException($"Duplicate stratum id '{s.Id}'");
        }

        var scripts = new Dictionary<string, GlyphProgram>();
        var monsterSources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in monsters.Values)
        {
            var path = "monsters/" + m.Script;
            var src = Need(path);
            monsterSources[m.Id] = src;
            try
            {
                scripts[m.Id] = GlyphCompiler.Compile(src, MonsterApi.Environment);
            }
            catch (GlyphError e)
            {
                throw new ContentException($"{path} line {e.Line}: {e.Message}");
            }
        }

        var programs = files
            .Where(f => f.Key.StartsWith("programs/", StringComparison.Ordinal) && f.Key.EndsWith(".glyph", StringComparison.Ordinal))
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f.Key), f => f.Value);

        var floors = new Dictionary<string, FloorDef>();
        foreach (var (name, text) in files.Where(f => f.Key.StartsWith("floors/", StringComparison.Ordinal) && f.Key.EndsWith(".json", StringComparison.Ordinal)))
        {
            var fl = Parse<FloorDef>(name, text);
            if (!floors.TryAdd(fl.Id, fl)) throw new ContentException($"Duplicate floor id '{fl.Id}'");
        }

        var examples = new List<ExampleParty>();
        if (files.TryGetValue("examples/examples.json", out var examplesJson))
        {
            foreach (var ex in Parse<List<ExampleFile>>("examples/examples.json", examplesJson))
            {
                var sources = new Dictionary<string, string>();
                foreach (var (chassisId, file) in ex.Programs)
                {
                    sources[chassisId] = files.TryGetValue("examples/" + file, out var src)
                        ? src
                        : throw new ContentException($"Example '{ex.Name}' is missing examples/{file}");
                }
                examples.Add(new ExampleParty(ex.Name, sources));
            }
        }

        var lessons = new List<LessonDef>();
        foreach (var (name, text) in files.Where(f => f.Key.StartsWith("codex/", StringComparison.Ordinal) && f.Key.EndsWith(".json", StringComparison.Ordinal)))
        {
            var l = Parse<LessonDef>(name, text);
            string Glyph(string kind) => files.TryGetValue($"codex/{l.Id}.{kind}.glyph", out var src)
                ? src
                : throw new ContentException($"Lesson '{l.Id}' is missing codex/{l.Id}.{kind}.glyph");
            lessons.Add(l with { Challenge = l.Challenge with { Starter = Glyph("starter"), Solution = Glyph("solution") } });
        }
        lessons.Sort((a, b) => a.Tier.CompareTo(b.Tier));

        var skills = files.TryGetValue("skills.json", out var skillsJson) ? Parse<SkillsFile>("skills.json", skillsJson) : new SkillsFile();
        var sites = files.TryGetValue("delves.json", out var sitesJson) ? Parse<List<DelveSite>>("delves.json", sitesJson) : [];

        var workshops = new List<WorkshopDef>();
        foreach (var (name, text) in files.Where(f => f.Key.StartsWith("workshops/", StringComparison.Ordinal) && f.Key.EndsWith(".json", StringComparison.Ordinal)))
        {
            var w = Parse<WorkshopDef>(name, text);
            var starter = files.TryGetValue($"workshops/{w.Id}.starter.glyph", out var src)
                ? src
                : throw new ContentException($"Workshop '{w.Id}' is missing workshops/{w.Id}.starter.glyph");
            workshops.Add(w with { Starter = starter });
        }
        workshops = [.. workshops.OrderBy(w => w.Order).ThenBy(w => w.Id, StringComparer.Ordinal)];

        var almanac = files.TryGetValue("almanac.json", out var almanacJson) ? Parse<List<AlmanacEntryDef>>("almanac.json", almanacJson) : [];
        var commissions = new List<CommissionDef>();
        if (files.TryGetValue("commissions/commissions.json", out var commissionsJson))
        {
            foreach (var c in Parse<List<CommissionDef>>("commissions/commissions.json", commissionsJson))
            {
                var solution = files.TryGetValue($"commissions/{c.Id}.solution.glyph", out var src)
                    ? src
                    : throw new ContentException($"Commission '{c.Id}' is missing commissions/{c.Id}.solution.glyph");
                commissions.Add(c with { Solution = solution });
            }
        }

        var pack = new ContentPack
        {
            Manifest = manifest,
            Chassis = chassis,
            Monsters = monsters,
            Traps = traps,
            Strata = strata,
            MonsterScripts = scripts,
            MonsterSources = monsterSources,
            Programs = programs,
            Floors = floors,
            Examples = examples,
            Lessons = lessons,
            Trees = skills.Trees,
            Skills = skills.Nodes,
            Sites = sites,
            Workshops = workshops,
            AlmanacEntries = almanac,
            Commissions = commissions,
            Hash = hash,
            Directory = directory,
        };
        var problems = pack.Validate();
        if (problems.Count > 0) throw new ContentException(string.Join(Environment.NewLine, problems));
        return pack;
    }

    private static Dictionary<string, T> ById<T>(List<T> items, Func<T, string> id, string what)
    {
        var d = new Dictionary<string, T>();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(id(item))) throw new ContentException($"A {what} is missing its id");
            if (!d.TryAdd(id(item), item)) throw new ContentException($"Duplicate {what} id '{id(item)}'");
        }
        return d;
    }

    /// <summary>Cross-reference and range checks. Returns human-readable problems.</summary>
    public List<string> Validate()
    {
        var problems = new List<string>();
        static bool Range(int[] r) => r.Length == 2 && r[0] >= 0 && r[0] <= r[1];
        foreach (var c in Chassis.Values)
        {
            if (c.Hp <= 0 || c.Budget <= 0 || c.Sight <= 0) problems.Add($"Chassis '{c.Id}' needs positive hp, budget and sight");
            if (c.MoveTicks <= 0 || c.AttackTicks <= 0) problems.Add($"Chassis '{c.Id}' needs positive move and attack ticks");
        }
        foreach (var m in Monsters.Values)
        {
            if (m.Hp <= 0 || m.Budget <= 0) problems.Add($"Monster '{m.Id}' needs positive hp and budget");
            if (m.MoveTicks <= 0 || m.AttackTicks <= 0 || m.WindupTicks < 0) problems.Add($"Monster '{m.Id}' has invalid timings");
            if (!Range(m.Gold)) problems.Add($"Monster '{m.Id}' gold must be [min, max]");
            if (!Range(m.Essence)) problems.Add($"Monster '{m.Id}' essence must be [min, max]");        }
        foreach (var s in Strata.Values)
        {
            if (s.Width < 16 || s.Height < 12 || s.Width > 200 || s.Height > 200) problems.Add($"Stratum '{s.Id}' size must be between 16x12 and 200x200");
            if (!Range(s.Rooms) || s.Rooms[0] < 2) problems.Add($"Stratum '{s.Id}' needs at least 2 rooms");
            if (!Range(s.RoomSize) || s.RoomSize[0] < 3) problems.Add($"Stratum '{s.Id}' room size must be [min>=3, max]");
            if (!Range(s.Monsters.Count) || !Range(s.Traps.Count) || !Range(s.Chests.Count) || !Range(s.Chests.Gold))
            {
                problems.Add($"Stratum '{s.Id}' has an invalid count range");
            }
            foreach (var e in s.Monsters.Table)
            {
                if (!Monsters.ContainsKey(e.Id)) problems.Add($"Stratum '{s.Id}' references unknown monster '{e.Id}'");
            }
            foreach (var e in s.Traps.Table)
            {
                if (!Traps.ContainsKey(e.Id)) problems.Add($"Stratum '{s.Id}' references unknown trap '{e.Id}'");
            }
            foreach (var t in s.Templates)
            {
                if (t.Shape is not ("rect" or "pillars" or "cross")) problems.Add($"Stratum '{s.Id}' has unknown room shape '{t.Shape}'");
            }
            if (!Range(s.Veins.Count) || !Range(s.Veins.Amount) || s.Veins.Amount[0] < 1) problems.Add($"Stratum '{s.Id}' has an invalid vein range");
            foreach (var e in s.Veins.Table)
            {
                if (!Resources.Mined.Contains(e.Id)) problems.Add($"Stratum '{s.Id}' veins can't hold '{e.Id}' (only {string.Join(", ", Resources.Mined)})");
            }
        }
        foreach (var f in Floors.Values)
        {
            if (Strata.ContainsKey(f.Id)) problems.Add($"Floor '{f.Id}' has the same id as a stratum");
            if (f.Rows.Count < 3 || f.Rows.Exists(r => r.Length != f.Rows[0].Length)) problems.Add($"Floor '{f.Id}' needs at least 3 rows of equal length");
            if (!Traps.ContainsKey(f.Trap)) problems.Add($"Floor '{f.Id}' references unknown trap '{f.Trap}'");
            foreach (var (key, id) in f.Legend)
            {
                if (key.Length != 1 || ("#.S>C^" + Dungeon.FloorLayout.VeinChars).Contains(key, StringComparison.Ordinal)) problems.Add($"Floor '{f.Id}' legend key '{key}' must be one free character");
                if (!Monsters.ContainsKey(id)) problems.Add($"Floor '{f.Id}' references unknown monster '{id}'");
            }
            if (!f.Rows.Exists(r => r.Contains('S', StringComparison.Ordinal)) || !f.Rows.Exists(r => r.Contains('>', StringComparison.Ordinal)))
            {
                problems.Add($"Floor '{f.Id}' needs a start (S) and stairs (>)");
            }
        }
        foreach (var e in Examples)
        {
            foreach (var chassisId in e.Programs.Keys)
            {
                if (!Chassis.ContainsKey(chassisId)) problems.Add($"Example '{e.Name}' references unknown chassis '{chassisId}'");
            }
        }
        ValidateProgression(problems);
        return problems;
    }

    private void ValidateProgression(List<string> problems)
    {
        for (var i = 0; i < Lessons.Count; i++)
        {
            var l = Lessons[i];
            if (l.Tier != i + 1) problems.Add($"Lesson '{l.Id}' has tier {l.Tier}; lessons must cover tiers 1, 2, 3... without gaps");
            if (l.Tier > Tiers.Max) problems.Add($"Lesson '{l.Id}' tier is above the language maximum");
            if (l.Pages.Count == 0) problems.Add($"Lesson '{l.Id}' has no pages");
            if (i == 0 && l.Cost != 0) problems.Add($"Lesson '{l.Id}' is the first lesson and must be free");
            if (i > 0 && l.Cost <= 0) problems.Add($"Lesson '{l.Id}' needs a cost in gold");
            var c = l.Challenge;
            if (!Floors.ContainsKey(c.Floor) && !Strata.ContainsKey(c.Floor)) problems.Add($"Lesson '{l.Id}' references unknown floor '{c.Floor}'");
            if (c.Party.Count == 0) problems.Add($"Lesson '{l.Id}' has an empty party");
            foreach (var id in c.Party)
            {
                if (!Chassis.ContainsKey(id)) problems.Add($"Lesson '{l.Id}' references unknown chassis '{id}'");
            }
            foreach (var f in c.MustUse)
            {
                if (!Progress.Challenge.FeatureNames.Contains(f)) problems.Add($"Lesson '{l.Id}' requires unknown feature '{f}'");
            }
            if (c.Goal == ChallengeGoal.Gold && c.Gold <= 0) problems.Add($"Lesson '{l.Id}' has a gold goal without an amount");
        }
        if (Lessons.Select(l => l.Id).Distinct(StringComparer.Ordinal).Count() != Lessons.Count) problems.Add("Two lessons share an id");

        var nodes = new Dictionary<string, SkillNode>(StringComparer.Ordinal);
        foreach (var n in Skills)
        {
            if (!nodes.TryAdd(n.Id, n)) problems.Add($"Duplicate skill id '{n.Id}'");
        }
        foreach (var n in Skills)
        {
            if (n.Cost < 0) problems.Add($"Skill '{n.Id}' has a negative cost");
            if (n.Lessons < 0 || n.Lessons > Lessons.Count) problems.Add($"Skill '{n.Id}' needs {n.Lessons} lessons, but there are {Lessons.Count}");
            foreach (var r in n.Requires)
            {
                if (!nodes.ContainsKey(r)) problems.Add($"Skill '{n.Id}' requires unknown skill '{r}'");
            }
            if (n.Effect.Spell is { } spell && Sim.Spells.Find(spell) is null) problems.Add($"Skill '{n.Id}' teaches unknown spell '{spell}'");
            foreach (var (id, amount) in n.Price)
            {
                if (!Resources.IsKnown(id) || id == Resources.Gold) problems.Add($"Skill '{n.Id}' costs unknown resource '{id}' (gold goes in \"cost\")");
                if (amount <= 0) problems.Add($"Skill '{n.Id}' costs a non-positive amount of {id}");
            }
            foreach (var (id, amount) in n.Effect.Produce)
            {
                if (!Resources.IsKnown(id) || id == Resources.Gold) problems.Add($"Skill '{n.Id}' produces unknown resource '{id}'");
                if (amount <= 0) problems.Add($"Skill '{n.Id}' produces a non-positive amount of {id}");
            }
        }
        foreach (var w in Workshops)
        {
            if (!Village.Workshops.Kinds.ContainsKey(w.Id)) problems.Add($"Workshop '{w.Id}' has no rules in code (known: {string.Join(", ", Village.Workshops.Kinds.Keys)})");
            else
            {
                try
                {
                    Village.Workshops.Compile(w, w.Starter, Tiers.Calls);
                }
                catch (GlyphError e)
                {
                    problems.Add($"workshops/{w.Id}.starter.glyph line {e.Line}: {e.Message}");
                }
            }
            if (!nodes.ContainsKey(w.Building)) problems.Add($"Workshop '{w.Id}' is housed in unknown skill '{w.Building}'");
            if (w.ShiftTicks is < 10 or > 1000) problems.Add($"Workshop '{w.Id}' shift must be 10 to 1000 ticks");
            if (w.Pages.Count == 0) problems.Add($"Workshop '{w.Id}' has no pages");
        }
        if (Workshops.Select(w => w.Id).Distinct(StringComparer.Ordinal).Count() != Workshops.Count) problems.Add("Two workshops share an id");
        foreach (var t in Trees)
        {
            if (t.OpenedBy is { } by && !nodes.ContainsKey(by)) problems.Add($"Tree {t.Id} is opened by unknown skill '{by}'");
        }
        foreach (var n in Skills)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>(n.Requires);
            while (stack.Count > 0)
            {
                var r = stack.Pop();
                if (r == n.Id)
                {
                    problems.Add($"Skill '{n.Id}' requires itself (a cycle)");
                    break;
                }
                if (seen.Add(r) && nodes.TryGetValue(r, out var rn)) foreach (var x in rn.Requires) stack.Push(x);
            }
        }
        foreach (var s in Sites)
        {
            if (!Strata.ContainsKey(s.Stratum) && !Floors.ContainsKey(s.Stratum)) problems.Add($"Delve site '{s.Id}' references unknown stratum '{s.Stratum}'");
        }
        ValidateAlmanac(problems);
        ValidateCommissions(problems);
    }

    private void ValidateAlmanac(List<string> problems)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in AlmanacEntries)
        {
            if (!ids.Add(e.Id)) problems.Add($"Almanac entry id '{e.Id}' is used twice");
            if (e.Id.StartsWith("monster:", StringComparison.Ordinal)) problems.Add($"Almanac entry '{e.Id}': monster entries come from monsters.json");
            if (e.Title.Length == 0 || e.Text.Length == 0 || e.Hint.Length == 0 || e.Category.Length == 0) problems.Add($"Almanac entry '{e.Id}' needs a category, title, hint and text");
            if (e.Count < 1 || e.Reward < 0) problems.Add($"Almanac entry '{e.Id}' needs a count of 1 or more and a reward of 0 or more");
            var colon = e.Key.IndexOf(':', StringComparison.Ordinal);
            var kind = colon < 0 ? e.Key : e.Key[..colon];
            var rest = colon < 0 ? "" : e.Key[(colon + 1)..];
            var known = kind switch
            {
                "mined" or "made" => Resources.All.Contains(rest),
                "trap" => Traps.ContainsKey(rest),
                "cleared" => Sites.Any(s => s.Id == rest),
                "commission" => Commissions.Any(c => c.Id == rest),
                "smelter" or "bakery" or "farm" => Workshops.Any(w => w.Id == kind),
                _ => false,
            };
            if (!known) problems.Add($"Almanac entry '{e.Id}' has a key nothing ever counts: '{e.Key}'");
        }
    }

    private void ValidateCommissions(List<string> problems)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in Commissions)
        {
            var before = problems.Count;
            if (!ids.Add(c.Id)) problems.Add($"Commission id '{c.Id}' is used twice");
            var w = Workshops.FirstOrDefault(x => x.Id == c.Workshop);
            if (w is null)
            {
                problems.Add($"Commission '{c.Id}' uses unknown workshop '{c.Workshop}'");
                continue;
            }
            if (!Village.Workshops.KindOf(w).Outputs.Contains(c.Goal.Resource) || c.Goal.Amount <= 0) problems.Add($"Commission '{c.Id}' goal must be a positive amount of something the {w.Name} makes");
            if (c.Par.Ticks.Length != 2 || c.Par.Lines.Length != 2 || c.Par.Ticks[0] > c.Par.Ticks[1] || c.Par.Lines[0] > c.Par.Lines[1]) problems.Add($"Commission '{c.Id}' par needs [gold, silver] for ticks and lines, gold first");
            foreach (var id in c.Stock.Keys.Concat(c.Reward.Keys))
            {
                if (!Resources.IsKnown(id)) problems.Add($"Commission '{c.Id}' mentions unknown resource '{id}'");
            }
            if (problems.Count > before) continue;
            var score = Progress.Commissions.Score(this, c, c.Solution);
            if (!score.Passed) problems.Add($"commissions/{c.Id}.solution.glyph doesn't meet the goal: {score.Summary}");
            else if (score.TicksMedal != Progress.Medal.Gold || score.LinesMedal != Progress.Medal.Gold) problems.Add($"commissions/{c.Id}.solution.glyph should earn both gold medals, got {score.Ticks} ticks and {score.Lines} lines");
        }
    }

    /// <summary>Find the repository's content folder by walking up from the working and app directories.</summary>
    public static string? FindDirectory()
    {
        foreach (var start in new[] { System.IO.Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "content");
                if (File.Exists(Path.Combine(candidate, "manifest.json"))) return candidate;
            }
        }
        return null;
    }

    public static ContentPack LoadDefault() =>
        Load(FindDirectory() ?? throw new ContentException("Could not find a 'content' folder with manifest.json"));
}
