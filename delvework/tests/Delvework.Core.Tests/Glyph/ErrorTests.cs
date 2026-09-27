using Delvework.Core.Glyph;
using Delvework.Core.Sim;
using static Delvework.Core.Tests.Glyph.Harness;

namespace Delvework.Core.Tests.Glyph;

public class ErrorTests
{
    [Theory]
    [InlineData("print(1 // 0)", GlyphErrorKind.Value, "Division by zero")]
    [InlineData("print(1 / 0)", GlyphErrorKind.Value, "Division by zero")]
    [InlineData("print(1 % 0)", GlyphErrorKind.Value, "Modulo by zero")]
    [InlineData("print(1.5 / 0)", GlyphErrorKind.Value, "Division by zero")]
    [InlineData("x = [1]\nprint(x[5])", GlyphErrorKind.Value, "out of range")]
    [InlineData("x = [1]\nprint(x[1.5])", GlyphErrorKind.Type, "whole numbers")]
    [InlineData("d = {}\nprint(d[\"k\"])", GlyphErrorKind.Value, "not in the dict")]
    [InlineData("print(1 + \"a\")", GlyphErrorKind.Type, "convert with str()")]
    [InlineData("print([] - [])", GlyphErrorKind.Type, "Cannot use -")]
    [InlineData("print(len(5))", GlyphErrorKind.Type, "len() needs")]
    [InlineData("x = 5\nx()", GlyphErrorKind.Type, "not something you can call")]
    [InlineData("print(9223372036854775807 + 1)", GlyphErrorKind.Value, "Number too large")]
    [InlineData("print(2 ** 64)", GlyphErrorKind.Value, "Number too large")]
    [InlineData("print(-9223372036854775807 - 1 - 1)", GlyphErrorKind.Value, "Number too large")]
    [InlineData("print(int(\"abc\"))", GlyphErrorKind.Value, "cannot read")]
    [InlineData("print(range(0, 5, 0))", GlyphErrorKind.Value, "step cannot be zero")]
    [InlineData("print(range(20000))", GlyphErrorKind.Limit, "limited to 10000")]
    [InlineData("print(\"x\" * 20000)", GlyphErrorKind.Limit, "10000 characters")]
    [InlineData("print([0] * 20000)", GlyphErrorKind.Limit, "10000 items")]
    [InlineData("print(\"ab\" * 9223372036854775807)", GlyphErrorKind.Limit, "10000 characters")]
    [InlineData("print(9223372036854775807 * [1, 2])", GlyphErrorKind.Limit, "10000 items")]
    [InlineData("s = \"a\" * 5000\nprint(s.replace(\"a\", \"bbb\"))", GlyphErrorKind.Limit, "10000 characters")]
    [InlineData("print((\"x\" * 5000).join([\"a\", \"b\", \"c\"]))", GlyphErrorKind.Limit, "10000 characters")]
    [InlineData("d = {}\nd[[1]] = 2", GlyphErrorKind.Type, "cannot be a dict key")]
    [InlineData("print(2 ** 0.5)", GlyphErrorKind.Type, "whole-number exponents")]
    [InlineData("print(min([]))", GlyphErrorKind.Value, "empty list")]
    [InlineData("x = [1, 2]\nx.apend(3)", GlyphErrorKind.Name, "Did you mean 'append'")]
    [InlineData("\"abc\"[0] = \"z\"", GlyphErrorKind.Type, "cannot be changed")]
    [InlineData("def f(a):\n    return a\nf(1, 2)", GlyphErrorKind.Type, "takes 1 argument but got 2")]
    [InlineData("def f(a):\n    return a\nf(b=1)", GlyphErrorKind.Type, "no parameter named 'b'")]
    [InlineData("def f(a, b):\n    return a\nf(1, a=2)", GlyphErrorKind.Type, "got two values for 'a'")]
    [InlineData("def f():\n    return f()\nf()", GlyphErrorKind.Limit, "Recursion too deep")]
    [InlineData("xs = []\nwhile True:\n    xs.append(1)", GlyphErrorKind.Limit, "limited to 10000")]
    [InlineData("def f():\n    y = y + 1\nf()", GlyphErrorKind.Name, "used before it is assigned")]
    [InlineData("if False:\n    z = 1\nprint(z)", GlyphErrorKind.Name, "has no value yet")]
    [InlineData("f()\ndef f():\n    pass", GlyphErrorKind.Name, "has no value yet")]
    [InlineData("x = None\nprint(x.foo)", GlyphErrorKind.Name, "has no attribute")]
    [InlineData("print([1, 2] < 3)", GlyphErrorKind.Type, "Cannot compare")]
    [InlineData("print(abs(\"x\"))", GlyphErrorKind.Type, "abs() needs a number")]
    [InlineData("print(\"a\" in 5)", GlyphErrorKind.Type, "Cannot use 'in'")]
    [InlineData("print(1 in \"abc\")", GlyphErrorKind.Type, "needs a string on the left")]
    [InlineData("for x in 5:\n    pass", GlyphErrorKind.Type, "Cannot loop over")]
    [InlineData("print(len())", GlyphErrorKind.Type, "needs at least 1 argument")]
    [InlineData("print(abs(1, 2))", GlyphErrorKind.Type, "takes at most 1 argument")]
    [InlineData("print(len([], key=1))", GlyphErrorKind.Type, "no option named 'key'")]
    [InlineData("wait(0)", GlyphErrorKind.Value, "at least 1 tick")]
    [InlineData("print([].pop())", GlyphErrorKind.Value, "empty list")]
    [InlineData("print(\"-\".join([1]))", GlyphErrorKind.Type, "use str() first")]
    public void ReportsFriendlyRuntimeErrors(string source, GlyphErrorKind kind, string fragment)
    {
        var e = RuntimeError(source);
        Assert.Equal(kind, e.Kind);
        Assert.Contains(fragment, e.Message, StringComparison.Ordinal);
        Assert.True(e.Line > 0);
    }

    [Fact]
    public void RuntimeErrorsCarryTheRightLine()
    {
        var e = RuntimeError("x = 1\ny = 2\n\nz = [x, y][7]\nprint(z)");
        Assert.Equal(4, e.Line);
    }

    [Fact]
    public void ErrorsInsideFunctionsPointAtTheFunctionLine()
    {
        var e = RuntimeError("def f(d):\n    return d[\"missing\"]\n\nf({})");
        Assert.Equal(2, e.Line);
    }

    [Theory]
    [InlineData("x = 1 +", GlyphErrorKind.Syntax, "Expected")]
    [InlineData("print(undefined_name)", GlyphErrorKind.Name, "not defined")]
    [InlineData("prnt(1)", GlyphErrorKind.Name, "Did you mean 'print'")]
    [InlineData("total = 1\nprint(totl)", GlyphErrorKind.Name, "Did you mean 'total'")]
    [InlineData("break", GlyphErrorKind.Syntax, "'break' outside a loop")]
    [InlineData("continue", GlyphErrorKind.Syntax, "'continue' outside a loop")]
    [InlineData("return 1", GlyphErrorKind.Syntax, "'return' outside a function")]
    [InlineData("def f():\n    def g():\n        pass", GlyphErrorKind.Syntax, "top level")]
    [InlineData("while True:\n    on see(e):\n        pass", GlyphErrorKind.Syntax, "top level")]
    [InlineData("on see(e):\n    def g():\n        pass", GlyphErrorKind.Syntax, "top level")]
    [InlineData("def f():\n    import x", GlyphErrorKind.Syntax, "top level")]
    [InlineData("def f(a, a):\n    pass", GlyphErrorKind.Syntax, "Duplicate parameter")]
    [InlineData("print(a=1, a=2)", GlyphErrorKind.Syntax, "given twice")]
    [InlineData("print(a=1, 2)", GlyphErrorKind.Syntax, "Positional arguments must come before")]
    [InlineData("import nothing", GlyphErrorKind.Name, "No module named")]
    [InlineData("x = 99999999999999999999", GlyphErrorKind.Limit, "too large")]
    [InlineData("x = \"abc", GlyphErrorKind.Syntax, "Unterminated string")]
    [InlineData("x = 1 $ 2", GlyphErrorKind.Syntax, "Unexpected character")]
    [InlineData("if True:\n        x = 1\n    y = 2", GlyphErrorKind.Syntax, "Indentation does not match")]
    [InlineData("  x = 1", GlyphErrorKind.Syntax, "indentation")]
    [InlineData("wait_until(1, 2)", GlyphErrorKind.Type, "exactly one condition")]
    [InlineData("1 = x", GlyphErrorKind.Syntax, "Can only assign")]
    [InlineData("for 1 in x:\n    pass", GlyphErrorKind.Syntax, "Expected")]
    [InlineData("while True\n    pass", GlyphErrorKind.Syntax, "Expected ':'")]
    public void ReportsFriendlyCompileErrors(string source, GlyphErrorKind kind, string fragment)
    {
        var e = CompileError(source);
        Assert.Equal(kind, e.Kind);
        Assert.Contains(fragment, e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownEventsSuggestTheClosestName()
    {
        var e = CompileError("on sea(e):\n    pass", env: GolemApi.Environment);
        Assert.Contains("Did you mean 'see'", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeeplyNestedCodeIsRejectedCleanly()
    {
        var src = string.Concat(Enumerable.Repeat("(", 150)) + "1" + string.Concat(Enumerable.Repeat(")", 150));
        Assert.Equal(GlyphErrorKind.Limit, CompileError("x = " + src).Kind);
    }

    [Fact]
    public void VeryLongExpressionsAreRejectedCleanly()
    {
        var src = "x = " + string.Join(" + ", Enumerable.Repeat("1", 500));
        Assert.Equal(GlyphErrorKind.Limit, CompileError(src).Kind);
    }

    [Fact]
    public void AVmStaysInErrorAfterFailing()
    {
        var vm = Start("print(1 // 0)\nprint(\"never\")");
        var first = vm.Step(100);
        var second = vm.Step(100);
        Assert.Equal(VmStatus.Error, first.Status);
        Assert.Equal(VmStatus.Error, second.Status);
        Assert.Same(first.Error, second.Error);
        Assert.Empty(vm.Output);
    }
}
