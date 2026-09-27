using Delvework.Core.Glyph;
using static Delvework.Core.Tests.Glyph.Harness;

namespace Delvework.Core.Tests.Glyph;

public class VmTests
{
    private static GlyphEnvironment EnvWith(params BuiltinDef[] defs)
    {
        var env = Stdlib.AddTo(new GlyphEnvironment());
        foreach (var d in defs) env.Add(d);
        return env.AddEvents("see", "signal").Freeze();
    }

    private static readonly BuiltinDef Act = new() { Name = "act", Impl = (in BuiltinCall c) => Value.Int(0), IsAction = true, MaxArgs = 0, Signature = "act()", Doc = "test" };
    private static readonly BuiltinDef Heavy = new() { Name = "heavy", Impl = (in BuiltinCall c) => Value.None, Cost = 25, MaxArgs = 0, Signature = "heavy()", Doc = "test" };

    [Fact]
    public void StepRunsExactlyTheBudget()
    {
        var vm = Start("x = 0\nwhile True:\n    x += 1");
        var r = vm.Step(10);
        Assert.Equal(VmStatus.Running, r.Status);
        Assert.Equal(10, r.Used);
        Assert.Equal(10, vm.TotalInstructions);
    }

    [Fact]
    public void ExpensiveBuiltinsCarryDebtIntoTheNextTick()
    {
        var vm = new Vm(Compile("heavy()\nheavy()", env: EnvWith(Heavy)));
        var first = vm.Step(10);
        Assert.True(first.Used > 10);
        var second = vm.Step(10);
        Assert.Equal(VmStatus.Running, second.Status);
        Assert.Equal(0, second.Used);
    }

    [Fact]
    public void ActionsYieldAndTakeTheResolvedValue()
    {
        var vm = new Vm(Compile("r = act()\nprint(r)", env: EnvWith(Act)));
        var r = vm.Step(100);
        Assert.Equal(VmStatus.Yielded, r.Status);
        Assert.Same(Act, r.Action);
        vm.ResolveAction(Value.Int(42));
        RunToEnd(vm);
        Assert.Equal(["42"], vm.Output);
    }

    [Fact]
    public void UnresolvedActionsKeepTheProvisionalValue()
    {
        var vm = new Vm(Compile("r = act()\nprint(r)", env: EnvWith(Act)));
        vm.Step(100);
        RunToEnd(vm);
        Assert.Equal(["0"], vm.Output);
    }

    [Fact]
    public void EachActionEndsTheTurn()
    {
        var vm = new Vm(Compile("act()\nact()\nact()", env: EnvWith(Act)));
        Assert.Equal(VmStatus.Yielded, vm.Step(1000).Status);
        Assert.Equal(VmStatus.Yielded, vm.Step(1000).Status);
        Assert.Equal(VmStatus.Yielded, vm.Step(1000).Status);
        Assert.Equal(VmStatus.Done, vm.Step(1000).Status);
    }

    [Fact]
    public void WaitBlocksForTheGivenTicks()
    {
        var vm = Start("wait(3)\nprint(\"done\")");
        Assert.Equal(VmStatus.Yielded, vm.Step(100).Status);
        Assert.Equal(VmStatus.Blocked, vm.Step(100).Status);
        Assert.Equal(VmStatus.Blocked, vm.Step(100).Status);
        Assert.Empty(vm.Output);
        Assert.Equal(VmStatus.Done, vm.Step(100).Status);
        Assert.Equal(["done"], vm.Output);
    }

    [Fact]
    public void HandlersRunDuringAWaitWithoutShorteningIt()
    {
        var vm = Start("on see(e):\n    print(\"saw\")\nwait(5)\nprint(\"done\")");
        vm.Step(100);
        vm.QueueEvent("see", null, Value.None);
        Assert.Equal(VmStatus.Blocked, vm.Step(100).Status);
        Assert.Equal(["saw"], vm.Output);
        for (var i = 0; i < 3; i++) Assert.Equal(VmStatus.Blocked, vm.Step(100).Status);
        Assert.Equal(VmStatus.Done, vm.Step(100).Status);
        Assert.Equal(["saw", "done"], vm.Output);
    }

    [Fact]
    public void HandlersStillRunAfterMainFinishes()
    {
        var vm = Start("on see(e):\n    print(e)\nprint(\"main\")");
        Assert.Equal(VmStatus.Done, vm.Step(100).Status);
        Assert.Equal(VmStatus.Done, vm.Step(100).Status);
        vm.QueueEvent("see", null, Value.Int(5));
        Assert.Equal(VmStatus.Done, vm.Step(100).Status);
        Assert.Equal(["main", "5"], vm.Output);
    }

    [Fact]
    public void HandlerArgumentsArePaddedOrTrimmed()
    {
        var vm = Start("on see(a, b):\n    print(a, b)\non signal(k):\n    print(k)\nwhile True:\n    pass");
        vm.QueueEvent("see", null, Value.Int(1));
        vm.QueueEvent("signal", "help", Value.Int(1), Value.Int(2));
        Steps(vm, 50, 10);
        Assert.Equal(["1 None", "help"], vm.Output);
    }

    [Fact]
    public void HandlerErrorsHaltTheProgram()
    {
        var vm = Start("on see(e):\n    print(e[3])\nwhile True:\n    pass");
        vm.Step(5);
        vm.QueueEvent("see", null, Value.List([]));
        StepResult r = default;
        for (var i = 0; i < 10 && r.Status != VmStatus.Error; i++) r = vm.Step(10);
        Assert.Equal(VmStatus.Error, r.Status);
        Assert.Equal(2, r.Error!.Line);
    }

    [Fact]
    public void CloneIsIndependent()
    {
        var vm = Start("xs = []\nwhile True:\n    xs.append(1)");
        Steps(vm, 5, 20);
        var copy = vm.Clone(null);
        var len = copy.GetGlobal("xs").AsList.Items.Count;
        Steps(vm, 5, 20);
        Assert.Equal(len, copy.GetGlobal("xs").AsList.Items.Count);
        Assert.True(vm.GetGlobal("xs").AsList.Items.Count > len);
    }

    [Fact]
    public void CloneContinuesExactlyLikeTheOriginal()
    {
        var vm = Start("d = {}\ni = 0\nfor k in range(100):\n    d[k % 7] = [k, str(k)]\n    i += k\nprint(i, d)");
        Steps(vm, 3, 25);
        var copy = vm.Clone(null);
        Assert.Equal(vm.StateHash(), copy.StateHash());
        RunToEnd(vm);
        RunToEnd(copy, 25);
        Assert.Equal(vm.Output, copy.Output);
        Assert.Equal(vm.StateHash(), copy.StateHash());
    }

    [Fact]
    public void StateHashTracksExecution()
    {
        var a = Start("x = 0\nwhile True:\n    x += 1");
        var b = Start("x = 0\nwhile True:\n    x += 1");
        Assert.Equal(a.StateHash(), b.StateHash());
        a.Step(7);
        Assert.NotEqual(a.StateHash(), b.StateHash());
        b.Step(7);
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void IntrospectionShowsTheCallStackAndLocals()
    {
        var vm = Start("def f(a):\n    b = a + 1\n    while True:\n        pass\nf(1)");
        Steps(vm, 3, 10);
        Assert.Equal(["f", "<main>"], vm.CallStack().Select(f => f.Function).ToList());
        var locals = vm.GetLocals().ToDictionary(l => l.Name, l => l.Value.ToString());
        Assert.Equal("1", locals["a"]);
        Assert.Equal("2", locals["b"]);
        Assert.Equal(3, vm.CurrentLine);
    }

    [Fact]
    public void GlobalsListOnlyShowsProgramVariables()
    {
        var vm = Start("hp_seen = 3\ndef f():\n    pass");
        RunToEnd(vm);
        var globals = vm.GetGlobals();
        Assert.Equal([("hp_seen", "3")], globals.Select(g => (g.Name, g.Value.ToString())).ToList());
    }

    [Fact]
    public void OutputKeepsTheMostRecentLines()
    {
        var vm = Start("for i in range(300):\n    print(i)");
        RunToEnd(vm, 10_000);
        Assert.Equal(Vm.MaxOutputLines, vm.Output.Count);
        Assert.Equal(300, vm.TotalOutputLines);
        Assert.Equal("299", vm.Output[^1]);
        Assert.Equal(Vm.MaxOutputLines, vm.DrainOutput().Count);
        Assert.Empty(vm.Output);
    }

    [Fact]
    public void AnInfiniteLoopNeverBlocksTheHost()
    {
        var vm = Start("while True:\n    x = [1, 2, 3]\n    y = len(x) * 2");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++) Assert.Equal(VmStatus.Running, vm.Step(200).Status);
        Assert.True(watch.Elapsed.TotalSeconds < 5);
        Assert.Equal(200_000, vm.TotalInstructions);
    }

    [Fact]
    public void DeepRecursionStopsAtTheFrameLimit()
    {
        var e = RuntimeError("def f(n):\n    return f(n + 1)\nf(0)");
        Assert.Equal(GlyphErrorKind.Limit, e.Kind);
    }

    [Fact]
    public void WideExpressionsGrowTheStack()
    {
        var items = string.Join(", ", Enumerable.Range(0, 180));
        Assert.Equal("180", Line($"xs = [{items}]\nprint(len(xs))"));
    }

    [Fact]
    public void EventQueueDropsTheOldest()
    {
        var vm = Start("on see(n):\n    print(n)\nwhile True:\n    pass");
        for (var i = 0; i < Vm.MaxEvents + 4; i++) vm.QueueEvent("see", null, Value.Int(i));
        Assert.Equal(Vm.MaxEvents, vm.PendingEventCount);
        Steps(vm, 100, 20);
        Assert.Equal("4", vm.Output[0]);
    }

    [Fact]
    public void HandlersNeverInterruptAStatementHalfway()
    {
        var vm = Start("x = 0\non see(e):\n    x = 100\nwhile True:\n    x = x + 1");
        vm.Step(2);
        vm.Step(2);
        vm.QueueEvent("see", null);
        vm.Step(10);
        Assert.True(vm.GetGlobal("x").AsInt >= 100);
    }

    [Fact]
    public void StatementStartsAreMarked()
    {
        var p = Compile("x = 1\ny = 2");
        Assert.True(p.Main.StatementStarts[0]);
        Assert.Equal(2, p.Main.StatementStarts.Take(p.Main.Code.Length).Count(s => s));
    }
}
