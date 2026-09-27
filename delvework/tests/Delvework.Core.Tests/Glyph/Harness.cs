using Delvework.Core.Glyph;

namespace Delvework.Core.Tests.Glyph;

/// <summary>Compiles and runs Glyph snippets against the plain standard library.</summary>
internal static class Harness
{
    public static readonly GlyphEnvironment Env = Stdlib.AddTo(new GlyphEnvironment()).Freeze();

    public static GlyphProgram Compile(string source, int tier = Tiers.Max, Func<string, string?>? modules = null, GlyphEnvironment? env = null) =>
        GlyphCompiler.Compile(source, env ?? Env, new CompileOptions { Tier = tier, ModuleResolver = modules });

    public static Vm Start(string source, int tier = Tiers.Max, GlyphEnvironment? env = null) => new(Compile(source, tier, env: env));

    /// <summary>Run until Done or Error; returns printed lines. Throws the runtime error, if any.</summary>
    public static List<string> Run(string source, int budget = 1000, int maxTicks = 10_000, int tier = Tiers.Max, Func<string, string?>? modules = null)
    {
        var vm = new Vm(Compile(source, tier, modules));
        RunToEnd(vm, budget, maxTicks);
        return [.. vm.Output];
    }

    public static void RunToEnd(Vm vm, int budget = 1000, int maxTicks = 10_000)
    {
        for (var t = 0; t < maxTicks; t++)
        {
            var r = vm.Step(budget);
            if (r.Status == VmStatus.Error) throw r.Error!;
            if (r.Status == VmStatus.Done) return;
        }
        throw new InvalidOperationException("Program did not finish");
    }

    public static string Line(string source) => string.Join("|", Run(source));

    /// <summary>Run expecting a runtime error and return it.</summary>
    public static GlyphError RuntimeError(string source, int budget = 1000, int maxTicks = 10_000)
    {
        var vm = new Vm(Compile(source));
        for (var t = 0; t < maxTicks; t++)
        {
            var r = vm.Step(budget);
            if (r.Status == VmStatus.Error) return r.Error!;
            if (r.Status == VmStatus.Done) break;
        }
        throw new InvalidOperationException("Expected a runtime error");
    }

    public static GlyphError CompileError(string source, int tier = Tiers.Max, GlyphEnvironment? env = null) =>
        Assert.Throws<GlyphError>(() => Compile(source, tier, env: env));

    public static void Steps(Vm vm, int ticks, int budget = 1)
    {
        for (var i = 0; i < ticks; i++)
        {
            var r = vm.Step(budget);
            if (r.Status == VmStatus.Error) throw r.Error!;
        }
    }
}
