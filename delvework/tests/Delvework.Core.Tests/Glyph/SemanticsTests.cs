using static Delvework.Core.Tests.Glyph.Harness;

namespace Delvework.Core.Tests.Glyph;

public class SemanticsTests
{
    [Theory]
    [InlineData("print(1 + 2)", "3")]
    [InlineData("print(7 / 2)", "3.5")]
    [InlineData("print(1 / 3)", "0.3333")]
    [InlineData("print(2 ** -1)", "0.5")]
    [InlineData("print(7 % -3)", "-2")]
    [InlineData("print(-7 % -3)", "-1")]
    [InlineData("print(1.5 + 1)", "2.5")]
    [InlineData("print(3 * 1.5)", "4.5")]
    [InlineData("print(7.5 // 2)", "3.0")]
    [InlineData("print(7.5 % 2)", "1.5")]
    [InlineData("print(10 - 2 * 3)", "4")]
    [InlineData("print((10 - 2) * 3)", "24")]
    [InlineData("print(10 // 3 * 3 + 10 % 3)", "10")]
    [InlineData("print(2 ** 62)", "4611686018427387904")]
    [InlineData("print(1 == 1.0, 2 != 2.0)", "True False")]
    [InlineData("print(-0.5)", "-0.5")]
    [InlineData("print(1.0, 2.50)", "1.0 2.5")]
    [InlineData("print(int(3.9), int(-3.9), int(\"42\"), int(True))", "3 -3 42 1")]
    [InlineData("print(abs(-5), abs(-2.5))", "5 2.5")]
    [InlineData("print(min(3, 1, 2), max([4, 9, 2]))", "1 9")]
    [InlineData("print(min(1.5, 2), max(\"a\", \"b\"))", "1.5 b")]
    [InlineData("print(str(12) + \"a\")", "12a")]
    [InlineData("print(len(\"hello\"), len([1, 2]), len({1: 2}))", "5 2 1")]
    [InlineData("print(range(3), range(1, 7, 2), range(5, 0, -2))", "[0, 1, 2] [1, 3, 5] [5, 3, 1]")]
    [InlineData("print(range(0), range(3, 1))", "[] []")]
    [InlineData("print(\"ab\" * 3, [0] * 3, 2 * \"x\")", "ababab [0, 0, 0] xx")]
    [InlineData("print([1, 2] + [3])", "[1, 2, 3]")]
    [InlineData("print(\"a\" < \"b\", [1, 2] < [1, 3], [1] < [1, 0])", "True True True")]
    [InlineData("print(not 0, not [], not \"x\", not {})", "True True False True")]
    [InlineData("print(None == None, None != 0)", "True True")]
    [InlineData("print(\"lo\" in \"hello\", 3 in {3: 1}, 4 in {3: 1})", "True True False")]
    [InlineData("print([1, [2, 3]])", "[1, [2, 3]]")]
    [InlineData("print({\"a\": 1, 2: \"b\"})", "{'a': 1, 2: 'b'}")]
    [InlineData("print([\"x\"], \"x\")", "['x'] x")]
    [InlineData("print(\"abc\"[-1], \"abc\"[0])", "c a")]
    [InlineData("print(1 < 2 <= 2 < 3, 1 < 3 < 2)", "True False")]
    [InlineData("print(0 or \"\" or None, 1 and 2 and 3)", "None 3")]
    [InlineData("print(True and False or True)", "True")]
    [InlineData("x = 5\nx -= 2\nx *= 4\nx //= 5\nprint(x)", "2")]
    [InlineData("print(2 ** 10 == 1024)", "True")]
    public void EvaluatesExpressions(string source, string expected)
    {
        Assert.Equal(expected, Line(source));
    }

    [Theory]
    [InlineData("xs = [3, 1, 2]\nxs.sort()\nprint(xs)", "[1, 2, 3]")]
    [InlineData("xs = [3, 1, 2]\nxs.sort(reverse=True)\nprint(xs)", "[3, 2, 1]")]
    [InlineData("xs = [1, 2, 3]\nprint(xs.pop(), xs.pop(0), xs)", "3 1 [2]")]
    [InlineData("xs = [1, 3]\nxs.insert(1, 2)\nxs.insert(-100, 0)\nprint(xs)", "[0, 1, 2, 3]")]
    [InlineData("xs = [1, 2, 1]\nxs.remove(1)\nprint(xs, xs.index(1), xs.count(1))", "[2, 1] 1 1")]
    [InlineData("xs = [1, 2]\nys = xs.copy()\nys.append(3)\nxs.reverse()\nprint(xs, ys)", "[2, 1] [1, 2, 3]")]
    [InlineData("xs = [1]\nxs.extend([2, 3])\nprint(xs)\nxs.clear()\nprint(xs)", "[1, 2, 3]|[]")]
    [InlineData("xs = [\"b\", \"a\", \"c\"]\nxs.sort()\nprint(xs)", "['a', 'b', 'c']")]
    [InlineData("d = {\"a\": 1}\nd[\"b\"] = 2\nprint(d.get(\"c\", 0), d.get(\"a\"), d.keys(), d.values(), d.items())", "0 1 ['a', 'b'] [1, 2] [['a', 1], ['b', 2]]")]
    [InlineData("d = {\"a\": 1, \"b\": 2}\nprint(d.pop(\"a\"), d.pop(\"zz\", 5), d)", "1 5 {'b': 2}")]
    [InlineData("d = {\"z\": 1, \"a\": 2}\nfor k in d:\n    print(k)", "z|a")]
    [InlineData("d = {1: 1}\ne = d.copy()\ne[2] = 2\nd.clear()\nprint(d, e)", "{} {1: 1, 2: 2}")]
    [InlineData("d = {}\nd[1] = \"int\"\nd[1.0] = \"float\"\nprint(d)", "{1: 'float'}")]
    [InlineData("print(\"a,b\".split(\",\"), \" x  y \".split(), \"-\".join([\"a\", \"b\"]))", "['a', 'b'] ['x', 'y'] a-b")]
    [InlineData("print(\" x \".strip(), \"Hi\".upper(), \"Hi\".lower())", "x HI hi")]
    [InlineData("print(\"hello\".startswith(\"he\"), \"hello\".endswith(\"lo\"), \"hello\".find(\"l\"), \"hello\".find(\"z\"))", "True True 2 -1")]
    [InlineData("print(\"aXbX\".replace(\"X\", \"-\"))", "a-b-")]
    [InlineData("for ch in \"ab\":\n    print(ch)", "a|b")]
    public void SupportsMethods(string source, string expected)
    {
        Assert.Equal(expected, Line(source));
    }

    [Fact]
    public void RepeatingEmptySequencesIsCheapAtAnyCount()
    {
        Assert.Equal("[] 0 []", Line("s = \"\" * 9223372036854775807\nprint([] * 9223372036854775807, len(s), [1] * -5)"));
    }

    [Fact]
    public void LocalAssignmentDoesNotTouchGlobalsWithoutDeclaration()
    {
        Assert.Equal("1", Line("x = 1\ndef f():\n    x = 2\nf()\nprint(x)"));
        Assert.Equal("2", Line("x = 1\ndef f():\n    global x\n    x = 2\nf()\nprint(x)"));
    }

    [Fact]
    public void FunctionsReadGlobalsAndTakeKeywordArguments()
    {
        Assert.Equal("4 3 10", Line("k = 10\ndef f(a, b):\n    return a - b\ndef g():\n    return k\nprint(f(b=1, a=5), f(5, b=2), g())"));
    }

    [Fact]
    public void ChainedComparisonEvaluatesTheMiddleOnce()
    {
        Assert.Equal(["f", "True"], Run("def f():\n    print(\"f\")\n    return 2\nprint(1 < f() < 3)"));
    }

    [Fact]
    public void LogicShortCircuits()
    {
        Assert.Equal(["False True"], Run("def boom():\n    print(\"boom\")\n    return 1\nprint(False and boom(), True or boom())"));
    }

    [Fact]
    public void AugmentedAssignmentWorksOnItems()
    {
        Assert.Equal("[1, 7] {'a': 3}", Line("xs = [1, 2]\nxs[1] += 5\nd = {\"a\": 1}\nd[\"a\"] *= 3\nprint(xs, d)"));
    }

    [Fact]
    public void BreakOnlyLeavesTheInnerLoop()
    {
        const string src = "for i in range(3):\n    for j in range(3):\n        if j == 1:\n            break\n        print(i, j)";
        Assert.Equal(["0 0", "1 0", "2 0"], Run(src));
    }

    [Fact]
    public void ContinueInWhileLoops()
    {
        Assert.Equal("[1, 3, 5]", Line("i = 0\nodd = []\nwhile i < 6:\n    i += 1\n    if i % 2 == 0:\n        continue\n    odd.append(i)\nprint(odd)"));
    }

    [Fact]
    public void ForLoopsIterateOverASnapshot()
    {
        Assert.Equal("[1, 2, 1, 2]", Line("xs = [1, 2]\nfor x in xs:\n    xs.append(x)\nprint(xs)"));
    }

    [Fact]
    public void ReturnFromInsideALoop()
    {
        Assert.Equal("5 None", Line("def first(xs):\n    for x in xs:\n        if x > 1:\n            return x\n    return None\nprint(first([1, 5, 7]), first([]))"));
    }

    [Fact]
    public void ModerateRecursionIsFine()
    {
        Assert.Equal("60", Line("def d(n):\n    if n == 0:\n        return 0\n    return 1 + d(n - 1)\nprint(d(60))"));
    }

    [Fact]
    public void WhileElseChainsAndNestedIfs()
    {
        const string src = """
            def grade(n):
                if n >= 90:
                    return "A"
                elif n >= 80:
                    return "B"
                elif n >= 70:
                    if n == 75:
                        return "C+"
                    return "C"
                else:
                    return "F"
            print(grade(95), grade(85), grade(75), grade(71), grade(3))
            """;
        Assert.Equal("A B C+ C F", Line(src));
    }

    [Fact]
    public void EnumsFromTheEnvironmentPrintAndCompare()
    {
        var env = new Delvework.Core.Glyph.GlyphEnvironment();
        Delvework.Core.Glyph.Stdlib.AddTo(env);
        env.AddEnum(new Delvework.Core.Glyph.EnumType("Color", ["Red", "Green"]), exposeMembers: true);
        env.Freeze();
        var vm = Start("print(Red, Color.Green, Red == Color.Red, Red == Color.Green, Red.name)", env: env);
        RunToEnd(vm);
        Assert.Equal(["Color.Red Color.Green True False Red"], vm.Output);
    }

    [Fact]
    public void WaitUntilPollsEveryTick()
    {
        var vm = Start("n = 0\non see(e):\n    global n\n    n = 1\nwait_until(n == 1)\nprint(\"go\")");
        for (var i = 0; i < 5; i++) Assert.NotEqual(Delvework.Core.Glyph.VmStatus.Done, vm.Step(100).Status);
        Assert.Empty(vm.Output);
        vm.QueueEvent("see", null);
        RunToEnd(vm);
        Assert.Equal(["go"], vm.Output);
    }
}
