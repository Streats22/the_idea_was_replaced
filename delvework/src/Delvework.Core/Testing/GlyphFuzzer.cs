using System.Text;
using Delvework.Core.Content;
using Delvework.Core.Dungeon;
using Delvework.Core.Glyph;
using Delvework.Core.Sim;

namespace Delvework.Core.Testing;

public sealed record FuzzFailure(string Source, string Exception);

public sealed record FuzzReport(long Iterations, long CompileErrors, long RuntimeErrors, long Clean, IReadOnlyList<FuzzFailure> Failures);

/// <summary>
/// Throws random and mutated Glyph at the compiler, the VM and the golem API. Only
/// <see cref="GlyphError"/>s are acceptable; any other exception is a crash and is reported.
/// </summary>
public sealed class GlyphFuzzer
{
    private static readonly string[] Names = ["x", "y", "n", "xs", "d", "f", "g", "i", "s", "enemy", "t"];
    private static readonly string[] StdCalls = ["print", "range", "len", "min", "max", "abs", "str", "int", "wait"];
    private static readonly string[] GolemCalls =
    [
        .. StdCalls,
        "move", "attack", "explore", "nearest_enemy", "enemies", "sense_ahead", "path_to", "distance", "signal",
        "hp", "me", "ally", "stairs", "nearest_chest", "move_toward", "mark", "marks", "recall",
    ];
    private const string Prelude = "x = 0\ny = 1\nn = 3\ni = 0\nt = 2\ns = \"ab\"\nxs = [1, 2, 3]\nd = {\"a\": 1}\nenemy = None\n";
    private static readonly string[] BinOps = ["+", "-", "*", "/", "//", "%", "**", "==", "!=", "<", ">", "<=", ">=", "and", "or", "in", "not in"];
    private static readonly string[] Methods = ["append", "pop", "insert", "remove", "index", "sort", "get", "keys", "items", "split", "join", "upper", "copy", "extend", "count"];
    private static readonly string[] Junk = ["(", ")", "[", "]", "{", "}", ":", ",", ".", "=", "+=", "\n", "    ", "\t", "\"", "'", "#", "on", "def", "if", "else", "elif", "while", "for", "return", "global", "import", "not", "None", "True", "1e9", "0.5", "-", "\\", "@", "$", "9999999999999999999", "\u00e9"];

    private readonly Rng _rng;
    private readonly List<string> _corpus;
    private readonly ContentPack? _content;
    private readonly FloorLayout? _floor;
    private string[] _calls = GolemCalls;
    private string[] _atoms = [];

    public GlyphFuzzer(ulong seed, IEnumerable<string> corpus, ContentPack? content = null)
    {
        _rng = new Rng(seed);
        _corpus = corpus.ToList();
        _content = content;
        if (content is not null)
        {
            _floor = Generator.Generate(content.Strata.Values.OrderBy(s => s.Id, StringComparer.Ordinal).First(), seed);
        }
    }

    public int MaxFailures { get; init; } = 20;

    public FuzzReport Run(long iterations, TimeSpan? duration = null, Action<long>? progress = null)
    {
        var failures = new List<FuzzFailure>();
        long compileErrors = 0, runtimeErrors = 0, clean = 0, i = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (; i < iterations; i++)
        {
            if (duration is { } d && clock.Elapsed >= d) break;
            var inWorld = _content is not null && _rng.Next(4) == 0;
            var source = Next(inWorld);
            try
            {
                switch (RunOne(source, inWorld))
                {
                    case 0: clean++; break;
                    case 1: compileErrors++; break;
                    default: runtimeErrors++; break;
                }
            }
#pragma warning disable CA1031 // A fuzzer must catch everything to report it.
            catch (Exception e)
#pragma warning restore CA1031
            {
                if (failures.Count < MaxFailures) failures.Add(new FuzzFailure(source, e.ToString()));
            }
            if (progress is not null && i % 1000 == 0) progress(i);
        }
        return new FuzzReport(i, compileErrors, runtimeErrors, clean, failures);
    }

    /// <summary>0 = ran cleanly, 1 = compile error, 2 = runtime error.</summary>
    public int RunOne(string source, bool inWorld)
    {
        if (inWorld && _content is null) throw new InvalidOperationException("World fuzzing needs a content pack");
        var env = inWorld ? GolemApi.Environment : FuzzEnvironment;
        var tier = _rng.Next(4) == 0 ? _rng.Range(1, Tiers.Max) : Tiers.Max;
        GlyphProgram program;
        try
        {
            program = GlyphCompiler.Compile(source, env, new CompileOptions { Tier = tier });
        }
        catch (GlyphError)
        {
            return 1;
        }
        if (inWorld)
        {
            var party = new[] { new PartyMember("Fuzz", "warden", source), new PartyMember("Pal", "seeker", _content!.Programs["seeker"]) };
            var world = World.Create(_content, _floor!, party, _rng.NextULong(), maxTicks: 150, tier: tier);
            world.RunToEnd();
            world.Clone().StateHash();
            return world.Golems[0].Error is null ? 0 : 2;
        }
        var vm = new Vm(program);
        for (var t = 0; t < 60; t++)
        {
            var r = vm.Step(_rng.Range(1, 400));
            if (_rng.Next(8) == 0) vm.QueueEvent(_rng.Next(2) == 0 ? "see" : "signal", _rng.Next(2) == 0 ? null : "help", Value.Int(t), Value.Str("x"));
            if (r.Status == VmStatus.Error) return 2;
            if (r.Status == VmStatus.Done) break;
            if (t == 30) vm = vm.Clone(null);
        }
        vm.StateHash();
        return 0;
    }

    private static readonly GlyphEnvironment FuzzEnvironment = Stdlib.AddTo(new GlyphEnvironment()).AddEvents("see", "signal").Freeze();

    /// <summary>The next program: mostly generated, sometimes a mutated corpus program or a mutated generated one.</summary>
    public string Next(bool golemApi = true)
    {
        _calls = golemApi ? GolemCalls : StdCalls;
        _atoms = golemApi ? ["North", "East", "South", "West", "Intent.Chase"] : [];
        return _rng.Next(8) switch
        {
            < 5 => Generate(),
            5 when golemApi && _corpus.Count > 0 => Mutate(_rng.Pick(_corpus)),
            _ => Mutate(Generate()),
        };
    }

    private string Mutate(string source)
    {
        var sb = new StringBuilder(source);
        var edits = _rng.Range(1, 6);
        for (var e = 0; e < edits && sb.Length > 0; e++)
        {
            var pos = _rng.Next(sb.Length + 1);
            switch (_rng.Next(5))
            {
                case 0:
                    sb.Insert(pos, _rng.Pick(Junk));
                    break;
                case 1:
                    sb.Remove(Math.Min(pos, sb.Length - 1), Math.Min(_rng.Range(1, 8), sb.Length - Math.Min(pos, sb.Length - 1)));
                    break;
                case 2:
                    if (pos < sb.Length) sb[pos] = (char)_rng.Range(9, 126);
                    break;
                case 3:
                {
                    var lines = sb.ToString().Split('\n');
                    var a = _rng.Next(lines.Length);
                    var copy = lines[_rng.Next(lines.Length)];
                    var list = lines.ToList();
                    list.Insert(a, copy);
                    sb.Clear().Append(string.Join('\n', list));
                    break;
                }
                default:
                    sb.Insert(pos, _rng.Pick(Names) + " = " + Expr(2) + "\n");
                    break;
            }
        }
        return sb.ToString();
    }

    public string Generate()
    {
        var sb = new StringBuilder();
        if (_rng.Next(4) != 0) sb.Append(Prelude);
        if (_rng.Next(3) == 0)
        {
            sb.Append("def f(a, b):\n");
            Block(sb, 1, 2, inLoop: false, inDef: true);
        }
        if (_rng.Next(3) == 0)
        {
            sb.Append(_rng.Next(2) == 0 ? "on see(e):\n" : "on signal \"help\"(data):\n");
            Block(sb, 1, 2, inLoop: false, inDef: false);
        }
        Block(sb, 0, 3, inLoop: false, inDef: false);
        return sb.ToString();
    }

    private void Block(StringBuilder sb, int indent, int depth, bool inLoop, bool inDef)
    {
        var count = _rng.Range(1, 4);
        for (var i = 0; i < count; i++) Stmt(sb, indent, depth, inLoop, inDef);
    }

    private string Jump(bool inLoop, bool inDef)
    {
        var options = new List<string> { "pass" };
        if (inLoop) options.AddRange(["break", "continue"]);
        if (inDef) options.AddRange(["return " + Expr(1), "global x"]);
        return _rng.Pick(options);
    }

    private void Stmt(StringBuilder sb, int indent, int depth, bool inLoop, bool inDef)
    {
        var pad = new string(' ', indent * 4);
        switch (depth <= 0 ? _rng.Next(4) : _rng.Next(10))
        {
            case 0:
                sb.Append(pad).Append(_rng.Pick(Names)).Append(" = ").Append(Expr(3)).Append('\n');
                break;
            case 1:
                sb.Append(pad).Append(_rng.Pick(Names)).Append(_rng.Pick(new[] { " += ", " -= ", " *= " })).Append(Expr(2)).Append('\n');
                break;
            case 2:
                sb.Append(pad).Append(Call(2)).Append('\n');
                break;
            case 3:
                sb.Append(pad).Append(Jump(inLoop, inDef)).Append('\n');
                break;
            case 4:
                sb.Append(pad).Append("if ").Append(Expr(2)).Append(":\n");
                Block(sb, indent + 1, depth - 1, inLoop, inDef);
                if (_rng.Next(2) == 0)
                {
                    sb.Append(pad).Append("else:\n");
                    Block(sb, indent + 1, depth - 1, inLoop, inDef);
                }
                break;
            case 5:
                sb.Append(pad).Append("while ").Append(Expr(2)).Append(":\n");
                Block(sb, indent + 1, depth - 1, true, inDef);
                break;
            case 6:
                sb.Append(pad).Append("for ").Append(_rng.Pick(Names)).Append(" in ").Append(Expr(2)).Append(":\n");
                Block(sb, indent + 1, depth - 1, true, inDef);
                break;
            case 7:
                sb.Append(pad).Append(_rng.Pick(Names)).Append('[').Append(Expr(1)).Append("] = ").Append(Expr(2)).Append('\n');
                break;
            case 8:
                sb.Append(pad).Append(_rng.Pick(Names)).Append('.').Append(_rng.Pick(Methods)).Append('(').Append(Expr(1)).Append(")\n");
                break;
            default:
                sb.Append(pad).Append("print(").Append(Expr(3)).Append(")\n");
                break;
        }
    }

    private string Call(int depth)
    {
        var n = _rng.Next(3);
        var args = Enumerable.Range(0, n).Select(_ => Expr(depth - 1));
        return $"{_rng.Pick(_calls)}({string.Join(", ", args)})";
    }

    private string Expr(int depth)
    {
        if (depth <= 0) return Atom();
        return _rng.Next(9) switch
        {
            0 => $"{Expr(depth - 1)} {_rng.Pick(BinOps)} {Expr(depth - 1)}",
            1 => $"({Expr(depth - 1)})",
            2 => $"[{string.Join(", ", Enumerable.Range(0, _rng.Next(4)).Select(_ => Expr(depth - 1)))}]",
            3 => $"{{{Expr(depth - 1)}: {Expr(depth - 1)}}}",
            4 => Call(depth),
            5 => $"{Expr(depth - 1)}[{Expr(depth - 1)}]",
            6 => $"not {Expr(depth - 1)}",
            7 => $"-{Expr(depth - 1)}",
            _ => Atom(),
        };
    }

    private string Atom() => _rng.Next(10) switch
    {
        0 => _rng.Range(-3, 100).ToString(System.Globalization.CultureInfo.InvariantCulture),
        1 => "\"ab\"",
        2 => "None",
        3 => "True",
        4 => "1.5",
        5 when _atoms.Length > 0 => _rng.Pick(_atoms),
        5 => "9223372036854775807",
        6 => "range(" + _rng.Range(0, 20).ToString(System.Globalization.CultureInfo.InvariantCulture) + ")",
        _ => _rng.Pick(Names),
    };
}
