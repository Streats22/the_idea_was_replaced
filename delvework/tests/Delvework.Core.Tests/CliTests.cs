using Delvework.Cli;

namespace Delvework.Core.Tests;

public sealed class CliTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("delvework-cli-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static (int Code, string Output) Cli(params string[] args)
    {
        using var sw = new StringWriter();
        var code = Program.Run(args, sw);
        return (code, sw.ToString());
    }

    [Fact]
    public void RunPlaysAGeneratedFloor()
    {
        var (code, output) = Cli("run", "--seed", "42", "--party", "warden.glyph,seeker.glyph");
        Assert.Equal(0, code);
        Assert.Contains("Seed 42, stratum mines", output, StringComparison.Ordinal);
        Assert.Contains("Outcome:", output, StringComparison.Ordinal);
        Assert.Contains("State hash:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void RunIsDeterministic()
    {
        Assert.Equal(Cli("run", "--seed", "5").Output, Cli("run", "--seed", "5").Output);
    }

    [Fact]
    public void RunReadsProgramFiles()
    {
        var path = Path.Combine(_dir, "lazy.glyph");
        File.WriteAllText(path, "recall()\n");
        var (code, output) = Cli("run", "--seed", "1", "--party", path, "--chassis", "warden", "--map", "--log");
        Assert.Equal(0, code);
        Assert.Contains("Outcome: Retreat", output, StringComparison.Ordinal);
        Assert.Contains("#", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaysRoundTripThroughVerify()
    {
        var path = Path.Combine(_dir, "r.json");
        Assert.Equal(0, Cli("run", "--seed", "3", "--replay", path).Code);
        var (code, output) = Cli("verify", path);
        Assert.Equal(0, code);
        Assert.Contains("Replay verified", output, StringComparison.Ordinal);

        File.WriteAllText(path, File.ReadAllText(path).Replace("\"finalHash\": \"", "\"finalHash\": \"f", StringComparison.Ordinal));
        Assert.Equal(1, Cli("verify", path).Code);
    }

    [Fact]
    public void MapPrintsALayout()
    {
        var (code, output) = Cli("map", "--seed", "42");
        Assert.Equal(0, code);
        Assert.Contains("S", output, StringComparison.Ordinal);
        Assert.Contains(">", output, StringComparison.Ordinal);
        Assert.Contains("rooms", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckReportsErrorsWithLines()
    {
        var good = Path.Combine(_dir, "good.glyph");
        var bad = Path.Combine(_dir, "bad.glyph");
        File.WriteAllText(good, "move(East)\n");
        File.WriteAllText(bad, "move(East)\nattak()\n");
        Assert.Equal(0, Cli("check", good).Code);
        var (code, output) = Cli("check", bad);
        Assert.Equal(1, code);
        Assert.Contains(":2:", output, StringComparison.Ordinal);
        Assert.Contains("attack", output, StringComparison.Ordinal);
        var golemOnly = Path.Combine(_dir, "explore.glyph");
        File.WriteAllText(golemOnly, "explore()\n");
        Assert.Equal(0, Cli("check", golemOnly).Code);
        Assert.Equal(1, Cli("check", golemOnly, "--monster").Code);
    }

    [Fact]
    public void ValidateAcceptsTheBundledContent()
    {
        var (code, output) = Cli("validate");
        Assert.Equal(0, code);
        Assert.Contains("is valid", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SmallDeterminismAndFuzzRunsPass()
    {
        Assert.Equal(0, Cli("determinism", "--seeds", "3").Code);
        Assert.Equal(0, Cli("fuzz", "--iterations", "200", "--seed", "1").Code);
    }

    [Fact]
    public void UnknownCommandsAndMissingProgramsFail()
    {
        Assert.Throws<CliException>(() => Cli("dance"));
        Assert.Throws<CliException>(() => Cli("run", "--party", "no_such_program"));
        Assert.Equal(2, Cli().Code);
    }
}
