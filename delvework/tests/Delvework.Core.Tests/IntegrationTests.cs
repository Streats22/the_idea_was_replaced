using Delvework.Core.Content;
using Delvework.Core.Dungeon;
using Delvework.Core.Glyph;
using Delvework.Core.Replay;
using Delvework.Core.Sim;
using Delvework.Core.Testing;

namespace Delvework.Core.Tests;

internal static class Shared
{
    public static readonly ContentPack Content = ContentPack.LoadDefault();

    public static DelveSetup Setup(ulong seed, int? maxTicks = null) =>
        new(seed, "mines", Determinism.SampleParty(Content), MaxTicks: maxTicks);
}

public class ContentTests
{
    [Fact]
    public void DefaultContentLoadsAndValidates()
    {
        var c = Shared.Content;
        Assert.Empty(c.Validate());
        Assert.Contains("warden", c.Chassis.Keys);
        Assert.Contains("troll", c.Monsters.Keys);
        Assert.Contains("mines", c.Strata.Keys);
        Assert.Equal(c.Monsters.Count, c.MonsterScripts.Count);
        Assert.StartsWith(c.Manifest.Version + "+", c.Version, StringComparison.Ordinal);
    }

    [Fact]
    public void BundledProgramsCompile()
    {
        foreach (var (name, src) in Shared.Content.Programs)
        {
            var ex = Record.Exception(() => GlyphCompiler.Compile(src, GolemApi.Environment));
            Assert.True(ex is null, $"{name}: {ex?.Message}");
        }
    }

    [Fact]
    public void HashIgnoresLineEndings()
    {
        var files = new Dictionary<string, string>
        {
            ["manifest.json"] = """{ "name": "t", "version": "1.0.0" }""",
            ["chassis.json"] = "[]",
            ["monsters.json"] = "[]",
            ["traps.json"] = "[]",
        };
        var plain = ContentPack.FromFiles(files).Hash;
        files["chassis.json"] = "[\r\n]";
        var crlf = ContentPack.FromFiles(files).Hash;
        files["chassis.json"] = "[\n]";
        var lf = ContentPack.FromFiles(files).Hash;
        Assert.Equal(lf, crlf);
        Assert.NotEqual(plain, lf);
    }

    [Fact]
    public void BrokenMonsterScriptsAreReported()
    {
        var files = new Dictionary<string, string>
        {
            ["manifest.json"] = """{ "name": "t", "version": "1.0.0" }""",
            ["chassis.json"] = "[]",
            ["monsters.json"] = """[ { "id": "x", "name": "X", "hp": 1, "script": "x.glyph" } ]""",
            ["traps.json"] = "[]",
            ["monsters/x.glyph"] = "atack()",
        };
        var ex = Assert.Throws<ContentException>(() => ContentPack.FromFiles(files));
        Assert.Contains("x.glyph", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingFilesAreReported()
    {
        Assert.Throws<ContentException>(() => ContentPack.FromFiles(new Dictionary<string, string>()));
    }
}

public class DungeonTests
{
    [Fact]
    public void GeneratedFloorsAreValid()
    {
        var stratum = Shared.Content.Strata["mines"];
        for (ulong seed = 1; seed <= 150; seed++)
        {
            var layout = Generator.Generate(stratum, seed);
            Assert.Empty(Generator.Validate(layout));
            Assert.InRange(layout.Rooms.Count, stratum.Rooms[0], stratum.Rooms[1]);
            Assert.InRange(layout.Monsters.Count, stratum.Monsters.Count[0], stratum.Monsters.Count[1]);
        }
    }

    [Fact]
    public void GenerationIsDeterministic()
    {
        var stratum = Shared.Content.Strata["mines"];
        Assert.Equal(Generator.Generate(stratum, 42).Render(), Generator.Generate(stratum, 42).Render());
        Assert.NotEqual(Generator.Generate(stratum, 42).Render(), Generator.Generate(stratum, 43).Render());
    }

    [Fact]
    public void SmallTestStratumAlsoWorks()
    {
        var stratum = Sim.TestContent.Pack.Strata["test"];
        for (ulong seed = 1; seed <= 50; seed++) Assert.Empty(Generator.Validate(Generator.Generate(stratum, seed)));
    }

    [Fact]
    public void ValidateCatchesUnreachableStairs()
    {
        var layout = FloorLayout.FromRows(["#######", "#S.#.>#", "#######"], new Dictionary<char, string>());
        Assert.NotEmpty(Generator.Validate(layout));
    }

    [Fact]
    public void RenderRoundTripsHandmadeFloors()
    {
        string[] rows = ["#######", "#S.C.>#", "#######"];
        var layout = FloorLayout.FromRows(rows, new Dictionary<char, string>());
        Assert.Equal(string.Join('\n', rows) + "\n", layout.Render().Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}

public class DelveTests
{
    [Fact]
    public void BundledPartyPlaysAGeneratedFloor()
    {
        var (world, outcome) = Delve.Run(Shared.Content, Shared.Setup(42));
        Assert.True(world.Finished);
        Assert.NotEqual(OutcomeKind.Stalled, outcome.Kind);
        Assert.DoesNotContain(world.Golems, g => g.Error is not null);
    }

    [Fact]
    public void UnknownStratumOrChassisIsAContentError()
    {
        Assert.Throws<ContentException>(() => Delve.Create(Shared.Content, Shared.Setup(1) with { Stratum = "nowhere" }));
        var bad = new DelveSetup(1, "mines", [new PartyMember("X", "nope", "wait()")]);
        Assert.Throws<ContentException>(() => Delve.Create(Shared.Content, bad));
    }

    [Fact]
    public void TierLimitsApplyToPrograms()
    {
        var setup = new DelveSetup(1, "mines", [new PartyMember("Warden", "warden", "xs = [1, 2]")], Tier: 2);
        var world = Delve.Create(Shared.Content, setup);
        Assert.Equal(GolemState.Halted, world.Golems[0].State);
    }

    [Fact]
    public void ImportsResolveAgainstBundledPrograms()
    {
        var world = Delve.Create(Shared.Content, new DelveSetup(1, "mines", [new PartyMember("Seeker", "seeker", "import explorer\n")], MaxTicks: 50));
        Assert.Equal(GolemState.Halted, world.Golems[0].State);
        Assert.Contains("may only contain functions", world.Golems[0].Error!.Message, StringComparison.Ordinal);

        world = Delve.Create(Shared.Content, new DelveSetup(1, "mines", [new PartyMember("Seeker", "seeker", "import nowhere\n")], MaxTicks: 50));
        Assert.Equal(GolemState.Halted, world.Golems[0].State);
    }
}

public class ReplayTests
{
    [Fact]
    public void SeekMatchesDirectStepping()
    {
        var rec = ReplayRecorder.Record(Shared.Content, Shared.Setup(7, 400));
        foreach (var tick in new[] { 0, 1, 49, 50, 51, 137, 250, 399 })
        {
            if (tick > rec.Final!.Tick) continue;
            var direct = Delve.Create(Shared.Content, rec.Setup);
            while (direct.Tick < tick) direct.Step();
            Assert.Equal(direct.StateHash(), rec.Seek(tick).StateHash());
        }
    }

    [Fact]
    public void SeekDoesNotDisturbKeyframes()
    {
        var rec = ReplayRecorder.Record(Shared.Content, Shared.Setup(8, 300));
        var first = rec.Seek(120).StateHash();
        rec.Seek(120).RunToEnd();
        Assert.Equal(first, rec.Seek(120).StateHash());
    }

    [Fact]
    public void RecordedReplaysVerify()
    {
        var rec = ReplayRecorder.Record(Shared.Content, Shared.Setup(11));
        var result = ReplayVerifier.Verify(Shared.Content, rec.File!);
        Assert.True(result.Ok, result.Message);
        Assert.Empty(result.Warnings!);
        Assert.Equal(ReplayFile.Hex(rec.Final!.StateHash()), rec.File!.FinalHash);
    }

    [Fact]
    public void JsonRoundTripsAndStillVerifies()
    {
        var file = ReplayRecorder.Record(Shared.Content, Shared.Setup(12, 500)).File!;
        var json = file.ToJson();
        Assert.Contains("\"contentVersion\"", json, StringComparison.Ordinal);
        var back = ReplayFile.FromJson(json);
        Assert.Equal(file.FinalHash, back.FinalHash);
        Assert.Equal(file.Checkpoints, back.Checkpoints);
        Assert.True(ReplayVerifier.Verify(Shared.Content, back).Ok);
    }

    [Fact]
    public void TamperedProgramsAreDetected()
    {
        var file = ReplayRecorder.Record(Shared.Content, Shared.Setup(13, 300)).File!;
        var party = file.Party.ToList();
        party[0] = party[0] with { Source = party[0].Source + "\n# edited" };
        var result = ReplayVerifier.Verify(Shared.Content, file with { Party = party });
        Assert.False(result.Ok);
        Assert.Contains("does not match", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DivergenceIsLocated()
    {
        var file = ReplayRecorder.Record(Shared.Content, Shared.Setup(14, 300)).File!;
        var cps = new SortedDictionary<int, string>(file.Checkpoints) { [100] = new string('0', 16) };
        var result = ReplayVerifier.Verify(Shared.Content, file with { Checkpoints = cps });
        Assert.False(result.Ok);
        Assert.Equal(100, result.DivergedAtTick);
    }

    [Fact]
    public void ContentVersionMismatchWarns()
    {
        var file = ReplayRecorder.Record(Shared.Content, Shared.Setup(15, 200)).File!;
        var result = ReplayVerifier.Verify(Shared.Content, file with { ContentVersion = "0.0.1+0" });
        Assert.True(result.Ok);
        Assert.Single(result.Warnings!);
    }

    [Fact]
    public void UnknownFormatsAreRejected()
    {
        var file = ReplayRecorder.Record(Shared.Content, Shared.Setup(16, 100)).File!;
        Assert.False(ReplayVerifier.Verify(Shared.Content, file with { Format = 99 }).Ok);
    }
}

public class DeterminismTests
{
    [Fact]
    public void SeedsReplayIdentically()
    {
        var count = int.TryParse(Environment.GetEnvironmentVariable("DELVEWORK_DETERMINISM_SEEDS"), out var n) ? n : 60;
        var report = Determinism.Sweep(Shared.Content, 1, count);
        Assert.Empty(report.Mismatches);
        Assert.Equal(count, report.Outcomes.Values.Sum());
    }

    [Fact]
    public void CombinedHashIsStable()
    {
        var a = Determinism.Sweep(Shared.Content, 100, 10, twice: false).CombinedHash;
        var b = Determinism.Sweep(Shared.Content, 100, 10, twice: false).CombinedHash;
        Assert.Equal(a, b);
        Assert.NotEqual(a, Determinism.Sweep(Shared.Content, 101, 10, twice: false).CombinedHash);
    }

    [Fact]
    public void ClonedWorldsStayInLockstep()
    {
        var w = Delve.Create(Shared.Content, Shared.Setup(21));
        for (var i = 0; i < 80; i++) w.Step();
        var c = w.Clone();
        while (!w.Finished)
        {
            w.Step();
            c.Step();
            Assert.Equal(w.StateHash(), c.StateHash());
        }
        Assert.True(c.Finished);
    }
}

public class FuzzTests
{
    [Fact]
    public void FuzzedProgramsNeverCrashTheHost()
    {
        var corpus = Shared.Content.Programs.Values;
        var report = new GlyphFuzzer(12345, corpus, Shared.Content).Run(3000);
        Assert.True(report.Failures.Count == 0, report.Failures.Count == 0 ? "" : $"{report.Failures[0].Source}\n---\n{report.Failures[0].Exception}");
        Assert.Equal(3000, report.Iterations);
        Assert.True(report.Clean > 0 && report.CompileErrors > 0 && report.RuntimeErrors > 0,
            $"clean {report.Clean}, compile {report.CompileErrors}, runtime {report.RuntimeErrors}");
    }

    [Fact]
    public void FuzzerIsSeeded()
    {
        var a = new GlyphFuzzer(5, Shared.Content.Programs.Values);
        var b = new GlyphFuzzer(5, Shared.Content.Programs.Values);
        for (var i = 0; i < 20; i++) Assert.Equal(a.Next(), b.Next());
    }
}

public class BenchmarkTests
{
    [Fact]
    public void ArenaHasTheRoadmapShape()
    {
        var w = Benchmark.CreateWorld(Shared.Content);
        Assert.Equal(4, w.Golems.Count);
        Assert.Equal(60, w.Monsters.Count);
        Assert.Equal(200, w.BudgetOverride);
    }

    [Fact]
    public void ShortRunReportsThroughput()
    {
        var r = Benchmark.Run(Shared.Content, ticks: 50, warmup: 5);
        Assert.Equal(50, r.Ticks);
        Assert.True(r.Instructions > 50 * 60 * 100, $"only {r.Instructions} instructions");
        Assert.True(r.TicksPerSecond > 0);
    }
}
