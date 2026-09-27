using Delvework.Core.Glyph;
using Delvework.Core.Sim;
using static Delvework.Core.Tests.Glyph.Harness;

namespace Delvework.Core.Tests.Glyph;

public class CompilerTests
{
    [Theory]
    [InlineData("while True:\n    pass", 1, "'while' loops is not unlocked yet: it is taught in Codex 2")]
    [InlineData("if True:\n    pass", 2, "'if' is not unlocked yet: it is taught in Codex 3")]
    [InlineData("wait(1 < 2)", 2, "Comparisons is not unlocked yet: it is taught in Codex 3")]
    [InlineData("wait(True and False)", 2, "'and' is not unlocked yet: it is taught in Codex 3")]
    [InlineData("wait(not True)", 2, "'not' is not unlocked yet: it is taught in Codex 3")]
    [InlineData("x = 1", 3, "Variables is not unlocked yet: it is taught in Codex 4")]
    [InlineData("print(1)", 3, "print() is not unlocked yet: it is taught in Codex 4")]
    [InlineData("wait(1 + 2)", 3, "Arithmetic is not unlocked yet: it is taught in Codex 4")]
    [InlineData("def f():\n    pass", 4, "Functions ('def') is not unlocked yet: it is taught in Codex 5")]
    [InlineData("xs = [1]", 5, "Lists is not unlocked yet: it is taught in Codex 6")]
    [InlineData("for i in range(3):\n    pass", 5, "'for' loops is not unlocked yet: it is taught in Codex 6")]
    [InlineData("print(len)", 5, "len() is not unlocked yet: it is taught in Codex 6")]
    [InlineData("on see(e):\n    pass", 6, "Event handlers ('on') is not unlocked yet: it is taught in Codex 7")]
    [InlineData("d = {}", 7, "Dicts is not unlocked yet: it is taught in Codex 8")]
    [InlineData("print(1 in [1])", 7, "'in' is not unlocked yet: it is taught in Codex 8")]
    [InlineData("import tactics", 8, "'import' is not unlocked yet: it is taught in Codex 9")]
    [InlineData("wait_until(True)", 9, "wait_until() is not unlocked yet: it is taught in Codex 10")]
    [InlineData("x = 1\nx += 1", 3, "Variables is not unlocked yet: it is taught in Codex 4")]
    [InlineData("x = 1\nglobal x", 4, "'global' is not unlocked yet: it is taught in Codex 5")]
    public void LockedFeaturesAreCompileErrors(string source, int tier, string fragment)
    {
        var e = CompileError(source, tier);
        Assert.Equal(GlyphErrorKind.Capability, e.Kind);
        Assert.Contains(fragment, e.Message, StringComparison.Ordinal);
        Assert.Contains($"up to {Tiers.Describe(tier)}", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CapabilityErrorsNameTheLesson()
    {
        Assert.Contains("Codex 4 (Variables)", CompileError("x = 1", 1).Message, StringComparison.Ordinal);
        Assert.Contains("up to Codex 1 (Commands)", CompileError("x = 1", 1).Message, StringComparison.Ordinal);
        Assert.Contains("Codex 9 (Modules)", CompileError("import t", 8).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GolemBuiltinsHaveTiersToo()
    {
        var e = CompileError("signal(\"go\")", 6, GolemApi.Environment);
        Assert.Contains("signal() is not unlocked yet: it is taught in Codex 7", e.Message, StringComparison.Ordinal);
        Assert.Contains("path_to() is not unlocked yet: it is taught in Codex 6", CompileError("path_to([1, 1])", 5, GolemApi.Environment).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TierOneIsAPlainSequenceOfCalls()
    {
        var program = Compile("# walk to the stairs\nmove(East)\nmove(South)\nexplore()\nattack(nearest_enemy())", 1, env: GolemApi.Environment);
        Assert.Equal(1, program.Tier);
    }

    [Fact]
    public void LoopsTierAllowsWhileTrue()
    {
        Compile("while True:\n    move(East)", Tiers.Loops, env: GolemApi.Environment);
        Compile("while explore():\n    pass\nmove(East)", Tiers.Loops, env: GolemApi.Environment);
    }

    [Fact]
    public void DecisionsTierProgramsCanExploreAndFight()
    {
        const string src = "while True:\n    if nearest_enemy() and distance(nearest_enemy()) == 1:\n        attack()\n    elif stairs():\n        move_toward(stairs())\n    else:\n        explore()";
        var program = Compile(src, Tiers.Conditions, env: GolemApi.Environment);
        Assert.Equal(Tiers.Conditions, program.Tier);
    }

    [Fact]
    public void LockedNamesUseTheirOwnMessage()
    {
        var locked = new Dictionary<string, string> { ["explore"] = "explore() is locked in this test" };
        var e = Assert.Throws<GlyphError>(() => GlyphCompiler.Compile("explore()", GolemApi.Environment, new CompileOptions { Locked = locked }));
        Assert.Equal(GlyphErrorKind.Capability, e.Kind);
        Assert.Equal("explore() is locked in this test", e.Message);
        GlyphCompiler.Compile("move(East)", GolemApi.Environment, new CompileOptions { Locked = locked });
    }

    [Fact]
    public void NegativeLiteralsAreAllowedBeforeArithmeticUnlocks()
    {
        Compile("wait(-1)", 1);
    }

    [Fact]
    public void MethodTiersAreCheckedAtRuntime()
    {
        var vm = new Vm(Compile("s = \"a\"\nprint(s.upper())", Tiers.Lists));
        RunToEndExpectingError(vm, ".upper() is not unlocked yet: it is taught in Codex 8");
    }

    private static void RunToEndExpectingError(Vm vm, string fragment)
    {
        for (var i = 0; i < 100; i++)
        {
            var r = vm.Step(100);
            if (r.Status == VmStatus.Error)
            {
                Assert.Equal(GlyphErrorKind.Capability, r.Error!.Kind);
                Assert.Contains(fragment, r.Error.Message, StringComparison.Ordinal);
                return;
            }
        }
        Assert.Fail("Expected a capability error");
    }

    [Fact]
    public void ConstantExpressionsAreFolded()
    {
        var p = Compile("x = 2 * 3 + 1 - (10 // 4)");
        Assert.DoesNotContain(p.Main.Code, i => i.Op is OpCode.Mul or OpCode.Add or OpCode.Sub or OpCode.FloorDiv);
        Assert.Contains(p.Main.Constants, v => v.Kind == ValueKind.Int && v.AsInt == 5);
    }

    [Fact]
    public void OverflowingConstantsAreNotFolded()
    {
        var p = Compile("x = 9223372036854775807 + 1");
        Assert.Contains(p.Main.Code, i => i.Op == OpCode.Add);
    }

    [Fact]
    public void DivisionByConstantZeroIsLeftForRuntime()
    {
        var p = Compile("x = 1 // 0");
        Assert.Contains(p.Main.Code, i => i.Op == OpCode.FloorDiv);
    }

    [Fact]
    public void WhileTrueHasNoConditionCheck()
    {
        var p = Compile("while True:\n    pass");
        Assert.DoesNotContain(p.Main.Code, i => i.Op is OpCode.JumpIfFalse or OpCode.PushTrue);
    }

    [Fact]
    public void LineTableMapsEveryInstruction()
    {
        var p = Compile("x = 1\n\ny = x + 2\nprint(y)");
        Assert.Equal(p.Main.Code.Length, p.Main.Lines.Length);
        Assert.Contains(3, p.Main.Lines);
        Assert.Contains(4, p.Main.Lines);
        Assert.DoesNotContain(2, p.Main.Lines);
    }

    [Fact]
    public void HandlersKeepOnlyParametersLocal()
    {
        var p = Compile("on see(e):\n    seen = e");
        var h = Assert.Single(p.Handlers);
        Assert.Equal(["e"], h.Proto.LocalNames);
        Assert.Contains("seen", p.GlobalNames);
    }

    [Fact]
    public void FunctionLocalsExcludeGlobalDeclarations()
    {
        var p = Compile("count = 0\ndef f(a):\n    global count\n    count = a\n    tmp = 1\n    for i in range(2):\n        pass");
        var f = Assert.Single(p.Functions);
        Assert.Equal(["a", "tmp", "i"], f.LocalNames);
        Assert.Equal(1, f.Arity);
    }

    [Fact]
    public void KeyedAndKeylessHandlersAreRecorded()
    {
        var p = Compile("on signal \"a\"(d):\n    pass\non signal(n, d):\n    pass");
        Assert.Equal([("signal", "a"), ("signal", (string?)null)], p.Handlers.Select(h => (h.Event, h.Key)).ToList());
        Assert.True(p.Listens("signal"));
    }

    [Fact]
    public void SameSourceGivesTheSameHash()
    {
        Assert.Equal(Compile("x = 1").SourceHash, Compile("x = 1").SourceHash);
        Assert.NotEqual(Compile("x = 1").SourceHash, Compile("x = 2").SourceHash);
    }

    [Fact]
    public void ImportsInlineModuleFunctions()
    {
        var modules = new Dictionary<string, string> { ["tactics"] = "LIMIT = 3\ndef twice(n):\n    return n * 2\n" };
        Assert.Equal(["8 3"], Run("import tactics\nprint(twice(4), LIMIT)", modules: m => modules.GetValueOrDefault(m)));
    }

    [Fact]
    public void NestedImportsWorkAndCyclesAreRejected()
    {
        var modules = new Dictionary<string, string>
        {
            ["a"] = "import b\ndef fa():\n    return fb() + 1\n",
            ["b"] = "def fb():\n    return 10\n",
            ["x"] = "import y\n",
            ["y"] = "import x\n",
        };
        Assert.Equal(["11"], Run("import a\nprint(fa())", modules: m => modules.GetValueOrDefault(m)));
        var e = Assert.Throws<GlyphError>(() => Compile("import x", modules: m => modules.GetValueOrDefault(m)));
        Assert.Contains("Circular import", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ModulesMayOnlyDefineThings()
    {
        var e = Assert.Throws<GlyphError>(() => Compile("import noisy", modules: _ => "print(1)\n"));
        Assert.Contains("may only contain", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ModuleSyntaxErrorsNameTheModule()
    {
        var e = Assert.Throws<GlyphError>(() => Compile("\nimport broken", modules: _ => "def f(:\n"));
        Assert.Contains("In module 'broken'", e.Message, StringComparison.Ordinal);
        Assert.Equal(2, e.Line);
    }

    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("print", "print", 0)]
    [InlineData("", "abc", 3)]
    [InlineData("nearest_enemy", "nearest_enmy", 1)]
    public void EditDistanceIsLevenshtein(string a, string b, int expected)
    {
        Assert.Equal(expected, GlyphCompiler.EditDistance(a, b));
    }

    [Fact]
    public void SuggestionsNeedToBeClose()
    {
        Assert.Equal("explore", GlyphCompiler.DidYouMean("explor", ["explore", "attack"]));
        Assert.Null(GlyphCompiler.DidYouMean("zzzzzz", ["explore", "attack"]));
        Assert.Null(GlyphCompiler.DidYouMean("ab", ["xyz"]));
    }

    [Fact]
    public void UnknownGolemBuiltinsSuggestTheRealOne()
    {
        var e = CompileError("nearest_enmy()", env: GolemApi.Environment);
        Assert.Contains("Did you mean 'nearest_enemy'", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryBuiltinDocumentsItself()
    {
        foreach (var def in GolemApi.Environment.Builtins.Concat(MonsterApi.Environment.Builtins))
        {
            Assert.False(string.IsNullOrWhiteSpace(def.Signature), def.Name);
            Assert.False(string.IsNullOrWhiteSpace(def.Doc), def.Name);
            Assert.InRange(def.Tier, Tiers.Min, Tiers.Max);
        }
    }
}
