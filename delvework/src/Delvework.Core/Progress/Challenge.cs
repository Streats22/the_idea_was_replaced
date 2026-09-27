using Delvework.Core.Content;
using Delvework.Core.Glyph;
using Delvework.Core.Sim;

namespace Delvework.Core.Progress;

/// <summary>One requirement of a challenge and whether the delve met it.</summary>
public sealed record ChallengeCheck(string Text, bool Ok);

public sealed record ChallengeResult(bool Passed, IReadOnlyList<ChallengeCheck> Checks);

/// <summary>Builds the delve for a Codex lesson and judges the result.</summary>
public static class Challenge
{
    public static readonly IReadOnlySet<string> FeatureNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "call", "while", "if", "compare", "assign", "def", "return", "for", "list", "index", "on", "dict",
    };

    private static readonly Dictionary<string, string> FeatureWords = new(StringComparer.Ordinal)
    {
        ["call"] = "a function call",
        ["while"] = "a while loop",
        ["if"] = "an if statement",
        ["compare"] = "a comparison (==, <, >...)",
        ["assign"] = "a variable",
        ["def"] = "a function of your own (def)",
        ["return"] = "return",
        ["for"] = "a for loop",
        ["list"] = "a list",
        ["index"] = "indexing with [ ]",
        ["on"] = "an event handler (on ...:)",
        ["dict"] = "a dictionary",
    };

    public static string Describe(string feature) => FeatureWords.GetValueOrDefault(feature, feature);

    /// <summary>The delve a lesson runs: its floor, seed, party and tier, with standard (unequipped) golems.</summary>
    public static DelveSetup Setup(ContentPack content, LessonDef lesson, IReadOnlyDictionary<string, string> sources)
    {
        var c = lesson.Challenge;
        var party = c.Party.Select(id => new PartyMember(content.Chassis[id].Name, id, sources.GetValueOrDefault(id, ""))).ToList();
        return new DelveSetup(c.Seed, c.Floor, party, lesson.Tier, Loadout: Loadout.None);
    }

    /// <summary>Lines that count toward a size limit: not blank and not only a comment.</summary>
    public static int CountLines(string source) =>
        source.Split('\n').Count(l => l.Trim() is { Length: > 0 } t && !t.StartsWith('#'));

    /// <summary>Which features (see <see cref="FeatureNames"/>) a program uses. Unparseable code uses none.</summary>
    public static IReadOnlySet<string> FeaturesUsed(string source)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        List<Stmt> stmts;
        try
        {
            stmts = Parser.Parse(source);
        }
        catch (GlyphError)
        {
            return used;
        }
        foreach (var s in stmts) Visit(s, used);
        return used;
    }

    private static void Visit(Stmt s, HashSet<string> used)
    {
        switch (s)
        {
            case ExprStmt e:
                Visit(e.Expr, used);
                break;
            case AssignStmt a:
                used.Add("assign");
                Visit(a.Target, used);
                Visit(a.Value, used);
                break;
            case AugAssignStmt a:
                used.Add("assign");
                Visit(a.Target, used);
                Visit(a.Value, used);
                break;
            case IfStmt i:
                used.Add("if");
                Visit(i.Test, used);
                foreach (var b in i.Body.Concat(i.Else)) Visit(b, used);
                break;
            case WhileStmt w:
                used.Add("while");
                Visit(w.Test, used);
                foreach (var b in w.Body) Visit(b, used);
                break;
            case ForStmt f:
                used.Add("for");
                Visit(f.Iter, used);
                foreach (var b in f.Body) Visit(b, used);
                break;
            case DefStmt d:
                used.Add("def");
                foreach (var b in d.Body) Visit(b, used);
                break;
            case ReturnStmt r:
                used.Add("return");
                if (r.Value is not null) Visit(r.Value, used);
                break;
            case OnStmt o:
                used.Add("on");
                foreach (var b in o.Body) Visit(b, used);
                break;
        }
    }

    private static void Visit(Expr e, HashSet<string> used)
    {
        switch (e)
        {
            case ListExpr l:
                used.Add("list");
                foreach (var i in l.Items) Visit(i, used);
                break;
            case DictExpr d:
                used.Add("dict");
                foreach (var x in d.Keys.Concat(d.Values)) Visit(x, used);
                break;
            case UnaryExpr u:
                Visit(u.Operand, used);
                break;
            case BinaryExpr b:
                Visit(b.Left, used);
                Visit(b.Right, used);
                break;
            case CompareExpr c:
                used.Add("compare");
                foreach (var o in c.Operands) Visit(o, used);
                break;
            case LogicExpr l:
                Visit(l.Left, used);
                Visit(l.Right, used);
                break;
            case CallExpr c:
                used.Add("call");
                Visit(c.Callee, used);
                foreach (var a in c.Args) Visit(a, used);
                foreach (var k in c.Kwargs) Visit(k.Value, used);
                break;
            case IndexExpr ix:
                used.Add("index");
                Visit(ix.Target, used);
                Visit(ix.Index, used);
                break;
            case AttrExpr a:
                Visit(a.Target, used);
                break;
        }
    }

    /// <summary>The checks a lesson shows before a delve (all unmet), so the player sees the goal up front.</summary>
    public static IReadOnlyList<ChallengeCheck> Goals(LessonDef lesson) => Judge(lesson, new Dictionary<string, string>(), null).Checks;

    public static ChallengeResult Evaluate(LessonDef lesson, IReadOnlyDictionary<string, string> sources, World final) => Judge(lesson, sources, final);

    private static ChallengeResult Judge(LessonDef lesson, IReadOnlyDictionary<string, string> sources, World? w)
    {
        var c = lesson.Challenge;
        var checks = new List<ChallengeCheck>();
        var descended = w is not null && w.Golems.Any(g => g.State == GolemState.Descended);
        switch (c.Goal)
        {
            case ChallengeGoal.Stairs:
                checks.Add(new("Reach the stairs", descended));
                break;
            case ChallengeGoal.Chests:
                var opened = w?.Chests.Count(ch => ch.Opened) ?? 0;
                var total = w?.Chests.Count ?? 0;
                checks.Add(new(w is null ? "Open every chest" : $"Open every chest ({opened} of {total})", w is not null && opened == total));
                checks.Add(new("Then reach the stairs", descended));
                break;
            case ChallengeGoal.Gold:
                var loot = w?.Outcome().Loot ?? 0;
                checks.Add(new(w is null ? $"Bring {c.Gold} gold home" : $"Bring {c.Gold} gold home ({loot} came back)", loot >= c.Gold));
                break;
        }
        if (c.NoLosses) checks.Add(new("No golem breaks", w is not null && !w.Golems.Any(g => g.State == GolemState.Broken)));
        var programs = c.Party.Select(id => sources.GetValueOrDefault(id, "")).ToList();
        if (c.MaxLines > 0)
        {
            var longest = programs.Count == 0 ? 0 : programs.Max(CountLines);
            checks.Add(new(w is null ? $"Use at most {c.MaxLines} lines of code" : $"Use at most {c.MaxLines} lines of code ({longest} used)", w is not null && longest <= c.MaxLines));
        }
        foreach (var f in c.MustUse)
        {
            checks.Add(new($"Use {Describe(f)}", w is not null && programs.Any(p => FeaturesUsed(p).Contains(f))));
        }
        return new ChallengeResult(checks.TrueForAll(x => x.Ok), checks);
    }
}
