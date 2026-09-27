using System.Globalization;
using Delvework.Core.Content;
using Delvework.Core.Dungeon;
using Delvework.Core.Glyph;
using Delvework.Core.Replay;
using Delvework.Core.Sim;
using Delvework.Core.Testing;

namespace Delvework.Cli;

public static class Program
{
    private const string Usage = """
        Delvework command-line tools

        Usage:
          run --seed N --party a.glyph,b.glyph [--chassis warden,seeker] [--stratum mines]
              [--tier 1-10] [--max-ticks N] [--replay out.json] [--log] [--map]
          verify <replay.json>
          map --seed N [--stratum mines]
          check <file.glyph> [--tier 1-10] [--monster]
          validate
          lessons [--seeds 1]      runs every Codex lesson's solution and starter code
          workshop <smelter|bakery|farm> [script.glyph] [--ore N] [--wood N] [--wheat N] [--log]
                                   runs one workshop shift (the starter script by default)
          commission [id] [script.glyph] [--log]
                                   scores a commission (every reference solution by default)
          determinism [--seeds 1000] [--start 1] [--max-ticks N] [--once]
          fuzz [--minutes 5 | --iterations N] [--seed N]
          bench [--ticks 2000] [--budget 200]

        Common options:
          --content <dir>   content folder (default: the nearest 'content' folder upward)

        Party files are paths, or names of programs in content/programs (e.g. warden).
        """;

    public static int Main(string[] args)
    {
        try
        {
            return Run(args, Console.Out);
        }
        catch (CliException e)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }
        catch (ContentException e)
        {
            Console.Error.WriteLine($"Content error: {e.Message}");
            return 3;
        }
    }

    public static int Run(string[] args, TextWriter output)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            output.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }
        var opts = Options.Parse(args.Skip(1));
        return args[0] switch
        {
            "run" => RunDelve(opts, output),
            "verify" => Verify(opts, output),
            "map" => Map(opts, output),
            "check" => Check(opts, output),
            "validate" => Validate(opts, output),
            "lessons" => Lessons(opts, output),
            "workshop" => Workshop(opts, output),
            "commission" => Commission(opts, output),
            "determinism" => DeterminismCmd(opts, output),
            "fuzz" => Fuzz(opts, output),
            "bench" => Bench(opts, output),
            _ => throw new CliException($"Unknown command '{args[0]}'.\n\n{Usage}"),
        };
    }

    /// <summary>Every lesson's solution must pass on every seed; its starter code must compile.</summary>
    private static int Lessons(Options o, TextWriter output)
    {
        var content = LoadContent(o);
        var seeds = o.Int("seeds", 1);
        var failures = 0;
        foreach (var lesson in content.Lessons)
        {
            foreach (var (label, code) in new[] { ("solution", lesson.Challenge.Solution), ("starter", lesson.Challenge.Starter) })
            {
                var sources = lesson.Challenge.Party.ToDictionary(id => id, _ => code);
                var passes = 0;
                string? detail = null;
                for (var s = 0; s < seeds; s++)
                {
                    var setup = Core.Progress.Challenge.Setup(content, lesson, sources) with { Seed = lesson.Challenge.Seed + (ulong)s };
                    var (world, outcome) = Delve.Run(content, setup);
                    var result = Core.Progress.Challenge.Evaluate(lesson, sources, world);
                    if (result.Passed) passes++;
                    else if (label == "solution") detail = null;
                    detail ??= $"seed {setup.Seed}: {outcome.Summary} [{string.Join("; ", result.Checks.Select(c => (c.Ok ? "ok " : "NO ") + c.Text))}]"
                        + (world.Golems.FirstOrDefault(g => g.Error is not null)?.Error is { } err ? $" error line {err.Line}: {err.Message}" : "");
                }
                output.WriteLine($"Codex {lesson.Tier} {lesson.Id,-10} {label,-8} {passes}/{seeds}  {detail}");
                if (label == "solution" && passes < seeds) failures++;
            }
        }
        return failures == 0 ? 0 : 1;
    }

    private static ContentPack LoadContent(Options o) =>
        o.Get("content") is { } dir ? ContentPack.Load(dir) : ContentPack.LoadDefault();

    public static List<PartyMember> ResolveParty(ContentPack content, string partyArg, string? chassisArg)
    {
        var files = partyArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var chassis = chassisArg?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        var fallback = new[] { "warden", "seeker", "striker" };
        var party = new List<PartyMember>();
        for (var i = 0; i < files.Length; i++)
        {
            var file = files[i];
            var stem = Path.GetFileNameWithoutExtension(file);
            string source;
            if (File.Exists(file)) source = File.ReadAllText(file);
            else if (content.Programs.TryGetValue(stem, out var bundled)) source = bundled;
            else throw new CliException($"Program not found: {file}");
            var ch = i < chassis.Length ? chassis[i]
                : content.Chassis.ContainsKey(stem.ToLowerInvariant()) ? stem.ToLowerInvariant()
                : fallback[i % fallback.Length];
            var name = content.Chassis.TryGetValue(ch, out var def) ? def.Name : stem;
            if (party.Exists(p => p.Name == name)) name = $"{name} {i + 1}";
            party.Add(new PartyMember(name, ch, source));
        }
        if (party.Count == 0) throw new CliException("--party needs at least one program");
        return party;
    }

    private static int RunDelve(Options o, TextWriter output)
    {
        var content = LoadContent(o);
        var seed = o.ULong("seed", 42);
        var party = ResolveParty(content, o.Get("party") ?? "warden,seeker", o.Get("chassis"));
        var setup = new DelveSetup(seed, o.Get("stratum") ?? "mines", party, o.Int("tier", Tiers.Max), o.Has("max-ticks") ? o.Int("max-ticks", 6000) : null);

        var recorder = ReplayRecorder.Record(content, setup);
        var world = recorder.Final!;
        var outcome = world.Outcome();

        if (o.Has("map")) output.Write(RenderWorld(world));
        if (o.Has("log"))
        {
            foreach (var e in world.Log) output.WriteLine($"[{e.Tick,5}] {e.Text}");
        }
        output.WriteLine($"Seed {seed}, stratum {setup.Stratum}, content {content.Version}");
        foreach (var g in world.Golems)
        {
            var err = g.Error is null ? "" : $" (line {g.Error.Line}: {g.Error.Message})";
            output.WriteLine($"  {g.Name,-10} {g.Chassis.Name,-8} {g.State,-9} hp {g.Hp,2}/{g.MaxHp,-2} loot {g.Loot,3}{err}");
        }
        output.WriteLine($"Outcome: {outcome.Kind} after {outcome.Ticks} ticks ({outcome.Ticks / (double)World.TicksPerSecond:0.0}s). {outcome.Summary}");
        output.WriteLine($"State hash: {ReplayFile.Hex(world.StateHash())}");
        if (o.Get("replay") is { } path)
        {
            recorder.File!.Save(path);
            output.WriteLine($"Replay saved to {path}");
        }
        return 0;
    }

    public static string RenderWorld(World w)
    {
        var sb = new System.Text.StringBuilder();
        for (var y = 0; y < w.Grid.Height; y++)
        {
            for (var x = 0; x < w.Grid.Width; x++)
            {
                var p = new Pos(x, y);
                var c = w.Grid.IsWall(p) ? '#' : '.';
                if (w.TrapAt(p) is { } t) c = t.Revealed ? '^' : '.';
                if (w.ChestAt(p) is { } ch) c = ch.Opened ? '_' : 'C';
                if (p == w.Stairs) c = '>';
                if (w.MonsterAt(p) is { } m) c = char.ToLowerInvariant(m.Def.Name[0]);
                foreach (var g in w.Golems)
                {
                    if (g.Present && g.Pos == p) c = char.ToUpperInvariant(g.Name[0]);
                }
                if (!w.Known[w.Grid.Idx(p)] && c is '.' or '#') c = ' ';
                sb.Append(c);
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static int Verify(Options o, TextWriter output)
    {
        var path = o.Positional.FirstOrDefault() ?? throw new CliException("verify needs a replay file");
        var content = LoadContent(o);
        var result = ReplayVerifier.Verify(content, ReplayFile.Load(path));
        foreach (var w in result.Warnings ?? []) output.WriteLine($"Warning: {w}");
        output.WriteLine(result.Message);
        return result.Ok ? 0 : 1;
    }

    private static int Map(Options o, TextWriter output)
    {
        var content = LoadContent(o);
        var id = o.Get("stratum") ?? "mines";
        if (!content.Strata.TryGetValue(id, out var stratum)) throw new CliException($"Unknown stratum '{id}'");
        var layout = Generator.Generate(stratum, o.ULong("seed", 42));
        output.Write(layout.Render());
        output.WriteLine($"{layout.Rooms.Count} rooms, {layout.Monsters.Count} monsters, {layout.Traps.Count} traps, {layout.Chests.Count} chests");
        return 0;
    }

    private static int Check(Options o, TextWriter output)
    {
        var path = o.Positional.FirstOrDefault() ?? throw new CliException("check needs a .glyph file");
        if (!File.Exists(path)) throw new CliException($"File not found: {path}");
        var env = o.Has("monster") ? MonsterApi.Environment : GolemApi.Environment;
        try
        {
            var program = GlyphCompiler.Compile(File.ReadAllText(path), env, new CompileOptions { Tier = o.Int("tier", Tiers.Max) });
            var handlers = program.Handlers.Count == 0 ? "no handlers" : string.Join(", ", program.Handlers.Select(h => h.Proto.Name));
            output.WriteLine($"OK: {path} ({program.Functions.Count} functions, {handlers}, {program.Main.Code.Length} instructions in main)");
            return 0;
        }
        catch (GlyphError e)
        {
            output.WriteLine($"{path}:{e.Line}: {e.Kind} error: {e.Message}");
            return 1;
        }
    }

    private static int Validate(Options o, TextWriter output)
    {
        var content = LoadContent(o);
        var problems = 0;
        foreach (var s in content.Strata.Values)
        {
            for (ulong seed = 1; seed <= 200; seed++)
            {
                var layout = Generator.Generate(s, seed);
                foreach (var p in Generator.Validate(layout))
                {
                    output.WriteLine($"{s.Id} seed {seed}: {p}");
                    problems++;
                }
            }
        }
        foreach (var f in content.Floors.Values)
        {
            foreach (var p in Generator.Validate(f.ToLayout()))
            {
                output.WriteLine($"floor {f.Id}: {p}");
                problems++;
            }
        }
        var sources = content.Programs.Select(p => ($"programs/{p.Key}.glyph", p.Value))
            .Concat(content.Examples.SelectMany(e => e.Programs.Select(p => ($"example '{e.Name}' ({p.Key})", p.Value))));
        foreach (var (name, src) in sources)
        {
            try
            {
                GlyphCompiler.Compile(src, GolemApi.Environment, new CompileOptions
                {
                    ModuleResolver = m => content.Programs.TryGetValue(m, out var s) ? s : null,
                });
            }
            catch (GlyphError e)
            {
                output.WriteLine($"{name}:{e.Line}: {e.Message}");
                problems++;
            }
        }
        output.WriteLine(problems == 0
            ? $"Content {content.Version} is valid: {content.Chassis.Count} chassis, {content.Monsters.Count} monsters, {content.Traps.Count} traps, {content.Strata.Count} strata, {content.Floors.Count} floors, {content.Examples.Count} example parties, {content.Programs.Count} programs, {content.Workshops.Count} workshops, {content.Commissions.Count} commissions, {Core.Progress.Almanac.Entries(content).Count} Almanac entries."
            : $"{problems} problem(s) found.");
        return problems == 0 ? 0 : 1;
    }

    private static int Workshop(Options o, TextWriter output)
    {
        var content = LoadContent(o);
        var id = o.Positional.ElementAtOrDefault(0) ?? throw new CliException("workshop needs an id: " + string.Join(", ", content.Workshops.Select(w => w.Id)));
        var def = content.Workshops.FirstOrDefault(w => w.Id == id) ?? throw new CliException($"Unknown workshop '{id}'");
        var file = o.Positional.ElementAtOrDefault(1);
        var src = file is null ? def.Starter : File.Exists(file) ? File.ReadAllText(file) : throw new CliException($"File not found: {file}");
        var stock = new Dictionary<string, int>
        {
            [Resources.Ore] = o.Int("ore", 10),
            [Resources.Wood] = o.Int("wood", 10),
            [Resources.Wheat] = o.Int("wheat", 12),
        };
        var r = Core.Village.Workshops.Run(def, src, stock);
        if (o.Has("log")) foreach (var line in r.Log) output.WriteLine(line);
        output.WriteLine($"{r.Summary} ({r.LastTick / 10.0:0.0}s of {def.ShiftTicks / 10.0:0.0}s)");
        return r.Error is null ? 0 : 1;
    }

    private static int Commission(Options o, TextWriter output)
    {
        var content = LoadContent(o);
        var ids = content.Commissions.Select(c => c.Id).ToList();
        var id = o.Positional.ElementAtOrDefault(0);
        var todo = id is null ? content.Commissions : [content.Commissions.FirstOrDefault(c => c.Id == id) ?? throw new CliException($"Unknown commission '{id}'. Known: {string.Join(", ", ids)}")];
        var file = o.Positional.ElementAtOrDefault(1);
        var failed = 0;
        foreach (var c in todo)
        {
            var src = file is null ? c.Solution : File.Exists(file) ? File.ReadAllText(file) : throw new CliException($"File not found: {file}");
            var score = Core.Progress.Commissions.Score(content, c, src);
            if (o.Has("log")) foreach (var line in score.Result.Log) output.WriteLine(line);
            output.WriteLine(score.Summary);
            if (!score.Passed) failed++;
        }
        return failed == 0 ? 0 : 1;
    }

    private static int DeterminismCmd(Options o, TextWriter output)
    {
        var content = LoadContent(o);
        var count = o.Int("seeds", 1000);
        var start = o.ULong("start", 1);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var report = Determinism.Sweep(content, start, count, maxTicks: o.Has("max-ticks") ? o.Int("max-ticks", 6000) : null, twice: !o.Has("once"));
        var outcomes = string.Join(", ", report.Outcomes.Select(kv => $"{kv.Key} {kv.Value}"));
        output.WriteLine($"Seeds {start}..{start + (ulong)count - 1}: {outcomes} ({sw.Elapsed.TotalSeconds:0.0}s)");
        output.WriteLine($"Content: {content.Version}");
        output.WriteLine($"Combined hash: {ReplayFile.Hex(report.CombinedHash)}");
        if (report.Mismatches.Count > 0)
        {
            output.WriteLine($"NONDETERMINISTIC seeds: {string.Join(", ", report.Mismatches)}");
            return 1;
        }
        return 0;
    }

    private static int Fuzz(Options o, TextWriter output)
    {
        var content = LoadContent(o);
        var corpus = content.Programs.Values.Concat(content.Monsters.Values.Select(m => File.Exists(Path.Combine(content.Directory, "monsters", m.Script))
            ? File.ReadAllText(Path.Combine(content.Directory, "monsters", m.Script)) : ""));
        var seed = o.ULong("seed", (ulong)DateTime.UtcNow.Ticks);
        var fuzzer = new GlyphFuzzer(seed, corpus, content);
        TimeSpan? duration = o.Has("minutes") ? TimeSpan.FromMinutes(double.Parse(o.Get("minutes")!, CultureInfo.InvariantCulture)) : null;
        var iterations = o.Has("iterations") ? long.Parse(o.Get("iterations")!, CultureInfo.InvariantCulture) : duration is null ? 10_000 : long.MaxValue;
        output.WriteLine($"Fuzzing with seed {seed}...");
        var report = fuzzer.Run(iterations, duration);
        output.WriteLine($"{report.Iterations} programs: {report.Clean} clean, {report.CompileErrors} compile errors, {report.RuntimeErrors} runtime errors, {report.Failures.Count} crashes");
        foreach (var f in report.Failures.Take(5))
        {
            output.WriteLine("---- crash ----");
            output.WriteLine(f.Source);
            output.WriteLine(f.Exception);
        }
        return report.Failures.Count == 0 ? 0 : 1;
    }

    private static int Bench(Options o, TextWriter output)
    {
        var content = LoadContent(o);
        var report = Benchmark.Run(content, o.Int("ticks", 2000), o.Int("budget", 200));
        output.WriteLine($"{report.Golems} golems + {report.Monsters} monsters, budget {report.Budget}/tick, {report.Ticks} ticks in {report.Seconds:0.000}s");
        output.WriteLine($"{report.TicksPerSecond:0} ticks/s, {report.InstructionsPerSecond / 1e6:0.0}M instructions/s");
        var target = o.Int("target", 1000);
        output.WriteLine(report.TicksPerSecond >= target ? $"PASS (target {target} ticks/s)" : $"BELOW TARGET ({target} ticks/s)");
        return report.TicksPerSecond >= target || o.Has("no-fail") ? 0 : 1;
    }
}

public sealed class CliException(string message) : Exception(message);

internal sealed class Options
{
    private readonly Dictionary<string, string?> _named = new(StringComparer.Ordinal);

    public List<string> Positional { get; } = [];

    public static Options Parse(IEnumerable<string> args)
    {
        var o = new Options();
        var list = args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var a = list[i];
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                var key = a[2..];
                string? value = null;
                var eq = key.IndexOf('=', StringComparison.Ordinal);
                if (eq >= 0)
                {
                    value = key[(eq + 1)..];
                    key = key[..eq];
                }
                else if (i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = list[++i];
                }
                o._named[key] = value;
            }
            else
            {
                o.Positional.Add(a);
            }
        }
        return o;
    }

    public bool Has(string key) => _named.ContainsKey(key);

    public string? Get(string key) => _named.TryGetValue(key, out var v) ? v : null;

    public int Int(string key, int fallback) =>
        Get(key) is { } s ? int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : throw new CliException($"--{key} expects a number") : fallback;

    public ulong ULong(string key, ulong fallback) =>
        Get(key) is { } s ? ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : throw new CliException($"--{key} expects a non-negative number") : fallback;
}
