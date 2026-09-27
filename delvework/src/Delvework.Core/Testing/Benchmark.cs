using System.Diagnostics;
using Delvework.Core.Content;
using Delvework.Core.Dungeon;
using Delvework.Core.Glyph;
using Delvework.Core.Sim;

namespace Delvework.Core.Testing;

public sealed record BenchmarkReport(int Golems, int Monsters, int Budget, int Ticks, double Seconds, long Instructions)
{
    public double TicksPerSecond => Ticks / Math.Max(Seconds, 1e-9);
    public double InstructionsPerSecond => Instructions / Math.Max(Seconds, 1e-9);
}

/// <summary>
/// The Phase 1 performance target: 4 golems and 60 monsters, each spending its full
/// 200-instruction budget every tick (with world queries mixed in), at 1,000+ ticks per second.
/// </summary>
public static class Benchmark
{
    public const string GolemScript = """
        x = 0
        while True:
            e = nearest_enemy()
            if e:
                x = (x + distance(e)) % 1000
            else:
                x = (x + hp()) % 1000
        """;

    public const string MonsterScript = """
        x = 0
        while True:
            g = nearest_golem()
            if g:
                x = (x + g.hp) % 1000
            x = (x * 3 + 1) % 997
        """;

    public static readonly StratumDef Arena = new()
    {
        Id = "bench",
        Name = "Benchmark arena",
        Width = 80,
        Height = 50,
        Rooms = [14, 16],
        RoomSize = [6, 10],
        ExtraCorridors = 4,
        Templates = [new RoomTemplate { Shape = "rect" }],
        Monsters = new SpawnTable { Count = [60, 60], Table = [new WeightedId { Id = "slime" }] },
        Traps = new SpawnTable(),
        Chests = new ChestTable(),
        MaxTicks = 1_000_000,
    };

    public static World CreateWorld(ContentPack content, int budget = 200, ulong seed = 1)
    {
        var monsterProgram = GlyphCompiler.Compile(MonsterScript, MonsterApi.Environment);
        var benchContent = new ContentPack
        {
            Manifest = content.Manifest,
            Chassis = content.Chassis,
            Monsters = content.Monsters,
            Traps = content.Traps,
            Strata = content.Strata,
            MonsterScripts = content.Monsters.Keys.ToDictionary(k => k, _ => monsterProgram),
            Programs = content.Programs,
            Hash = content.Hash,
        };
        var layout = Generator.Generate(Arena, seed);
        var party = Enumerable.Range(1, 4).Select(i => new PartyMember($"Bench{i}", i % 2 == 0 ? "seeker" : "warden", GolemScript)).ToList();
        var world = World.Create(benchContent, layout, party, seed, maxTicks: 1_000_000);
        world.BudgetOverride = budget;
        return world;
    }

    public static BenchmarkReport Run(ContentPack content, int ticks = 2000, int budget = 200, int warmup = 100)
    {
        var world = CreateWorld(content, budget);
        for (var i = 0; i < warmup; i++) world.Step();
        var before = TotalInstructions(world);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < ticks; i++) world.Step();
        sw.Stop();
        return new BenchmarkReport(world.Golems.Count, world.Monsters.Count, budget, ticks, sw.Elapsed.TotalSeconds, TotalInstructions(world) - before);
    }

    private static long TotalInstructions(World w) =>
        w.Golems.Sum(g => g.Vm?.TotalInstructions ?? 0) + w.Monsters.Sum(m => m.Vm?.TotalInstructions ?? 0);
}
