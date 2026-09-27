using Delvework.Core.Glyph;
using static Delvework.Core.Tests.Glyph.Harness;

namespace Delvework.Core.Tests.Glyph;

/// <summary>The interpreter tests of the retired TypeScript prototype (tests/interpreter.test.ts), ported.</summary>
public class PortedInterpreterTests
{
    [Fact]
    public void ArithmeticHasPythonSemantics()
    {
        Assert.Equal("3 -4 2 1024 0.25", Line("print(7 // 2, -7 // 2, -7 % 3, 2 ** 10, 1 / 4)"));
    }

    [Fact]
    public void RunsIfElifElseAndWhileLoops()
    {
        const string src = """
            i = 0
            while i < 5:
                if i == 0:
                    print("zero")
                elif i % 2 == 0:
                    print("even", i)
                else:
                    pass
                i += 1
            """;
        Assert.Equal(["zero", "even 2", "even 4"], Run(src));
    }

    [Fact]
    public void SupportsFunctionsRecursionAndGlobals()
    {
        const string src = """
            count = 0
            def fib(n):
                global count
                count += 1
                if n < 2:
                    return n
                return fib(n - 1) + fib(n - 2)
            print(fib(10), count)
            """;
        Assert.Equal("55 177", Line(src));
    }

    [Fact]
    public void SupportsListsIndexingMethodsAndForLoopsWithBreakContinue()
    {
        const string src = """
            xs = [3, 1, 4]
            xs.append(1)
            xs[0] = 9
            total = 0
            for x in xs:
                if x == 4:
                    continue
                total += x
            for i in range(100):
                if i == 3:
                    break
            print(xs, total, i, xs[-1], len(xs), 4 in xs, 7 not in xs)
            """;
        Assert.Equal("[9, 1, 4, 1] 11 3 1 4 True True", Line(src));
    }

    [Fact]
    public void SupportsChainedComparisonsAndShortCircuitLogic()
    {
        // The prototype used an undefined name on the right of 'and'; Glyph rejects unknown names
        // at compile time, so a runtime error stands in for it.
        Assert.Equal("True False 5 0", Line("print(1 < 2 < 3, 3 > 2 > 2, None or 5, 0 and 1 // 0)"));
    }

    [Fact]
    public void ReportsErrorsWithLineNumbers()
    {
        var e = CompileError("x = 1\ny = x + undefined_thing");
        Assert.Equal(2, e.Line);
        Assert.Contains("undefined_thing", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsSyntaxErrors()
    {
        var e = Assert.Throws<GlyphError>(() => Parser.Parse("if True\n    pass"));
        Assert.Contains("Expected ':'", e.Message, StringComparison.Ordinal);
        Assert.Throws<GlyphError>(() => Parser.Parse("x = (1 +\n"));
    }

    [Fact]
    public void InfiniteLoopsKeepRunningAcrossTicks()
    {
        var vm = Start("while True:\n    pass");
        for (var i = 0; i < 1000; i++) Assert.Equal(VmStatus.Running, vm.Step(10).Status);
    }

    [Fact]
    public void AllowsOneLineBlocksAndMultiLineBrackets()
    {
        const string src = """
            xs = [1,
                  2,
                  3]
            if len(xs) == 3: print("ok")
            """;
        Assert.Equal(["ok"], Run(src));
    }

    [Fact]
    public void ParsesOnHandlersWithAndWithoutKeysAndParams()
    {
        var ast = Parser.Parse("on see(enemy):\n    pass\non signal \"help\"(data):\n    pass\non low_hp():\n    pass\non tick:\n    pass");
        var ons = ast.Cast<OnStmt>().Select(o => (o.Event, o.Key, string.Join(",", o.Params))).ToList();
        Assert.Equal([("see", null, "enemy"), ("signal", "help", "data"), ("low_hp", null, ""), ("tick", null, "")], ons);
    }

    [Fact]
    public void RunsAHandlerThenResumesTheMainLoop()
    {
        var vm = Start("""
            on see(enemy):
                print("saw", enemy)
            i = 0
            while True:
                i += 1
                if i == 3:
                    print("three")
            """);
        Steps(vm, 5);
        vm.QueueEvent("see", null, Value.Str("Slime"));
        Steps(vm, 200);
        Assert.Equal("saw Slime", vm.Output[0]);
        Assert.Contains("three", vm.Output);
    }

    [Fact]
    public void HandlerAssignmentsGoToGlobalsButParametersStayLocal()
    {
        var vm = Start("""
            alert = False
            enemy = "none"
            on see(enemy):
                alert = True
            while not alert:
                pass
            print(alert, enemy)
            """);
        Steps(vm, 10);
        vm.QueueEvent("see", null, Value.Str("Skeleton"));
        Steps(vm, 100);
        Assert.Equal(["True none"], vm.Output);
    }

    [Fact]
    public void RoutesKeyedSignalsToMatchingHandlersOnly()
    {
        var vm = Start("""
            on signal "help"(data):
                print("help", data)
            on signal "avoid"(data):
                print("avoid", data)
            on signal(name, data):
                print("any", name, data)
            while True:
                pass
            """);
        Steps(vm, 3);
        vm.QueueEvent("signal", "help", Value.Int(7));
        Steps(vm, 50);
        Assert.Equal(["help 7", "any help 7"], vm.Output);
    }

    [Fact]
    public void IgnoresUnheardEventsAndCapsTheQueue()
    {
        var vm = Start("on hurt(n):\n    print(n)\nwhile True:\n    pass");
        Assert.False(vm.QueueEvent("see", null, Value.Str("x")));
        for (var i = 0; i < 40; i++) vm.QueueEvent("hurt", null, Value.Int(i));
        Steps(vm, 500);
        Assert.Equal(16, vm.Output.Count);
        Assert.Equal("39", vm.Output[^1]);
        Assert.True(vm.Program.Listens("hurt"));
        Assert.False(vm.Program.Listens("see"));
    }

    [Fact]
    public void RejectsHandlersThatAreNotAtTheTopLevel()
    {
        var e = CompileError("def f():\n    on see(e):\n        pass\nf()");
        Assert.Contains("top level", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsesEveryBundledExample()
    {
        var content = Core.Content.ContentPack.LoadDefault();
        Assert.NotEmpty(content.Programs);
        foreach (var (name, src) in content.Programs)
        {
            var ex = Record.Exception(() => GlyphCompiler.Compile(src, Core.Sim.GolemApi.Environment));
            Assert.True(ex is null, $"programs/{name}.glyph: {ex?.Message}");
        }
        foreach (var m in content.Monsters.Values)
        {
            var src = File.ReadAllText(Path.Combine(content.Directory, "monsters", m.Script));
            var ex = Record.Exception(() => GlyphCompiler.Compile(src, Core.Sim.MonsterApi.Environment));
            Assert.True(ex is null, $"monsters/{m.Script}: {ex?.Message}");
        }
    }
}
