using Delvework.Core.Content;
using Delvework.Core.Glyph;
using Delvework.Core.Sim;

namespace Delvework.Core.Replay;

public sealed record GolemFrame(
    int Id, string Name, string Chassis, Pos Pos, int Hp, int MaxHp, GolemState State,
    int Line, int Ops, int Loot, int StunTicks, int BurnTicks, int SlowTicks,
    Dir Facing = Dir.East, int Mana = 0, int MaxMana = 0, int ShieldTicks = 0, IReadOnlyList<int>? Bag = null);

public sealed record MonsterFrame(int Id, string DefId, string Kind, Pos Pos, int Hp, int MaxHp, bool Alive, IntentKind Intent);

/// <summary>Fog per tile: 0 unknown, 1 remembered, 2 visible now. <see cref="Effects"/> happened during this tick.</summary>
public sealed record TickFrame(
    int Tick, IReadOnlyList<GolemFrame> Golems, IReadOnlyList<MonsterFrame> Monsters, byte[] Fog,
    bool[] ChestsOpened, bool[] TrapsRevealed, IReadOnlyList<Drop> Drops, long LogCount,
    IReadOnlyList<Effect>? Effects = null, int[]? VeinsLeft = null, IReadOnlyList<Mark>? Marks = null);

/// <summary>A value shown in the debugger: name, rendered value and type.</summary>
public sealed record Variable(string Name, string Value, string Type);

/// <summary>What a golem's program was doing at one tick.</summary>
public sealed record GolemInspection(
    string Name, GolemState State, int Line, IReadOnlyList<VmFrameInfo> CallStack,
    IReadOnlyList<Variable> Globals, IReadOnlyList<Variable> Locals, GlyphError? Error);

/// <summary>
/// A finished delve with one lightweight frame per tick for drawing and scrubbing, plus the
/// replay recorder's keyframes for inspecting program state at any tick.
/// </summary>
public sealed class Timeline
{
    private readonly List<TickFrame> _frames;

    private Timeline(ReplayRecorder recorder, List<TickFrame> frames)
    {
        Recorder = recorder;
        _frames = frames;
    }

    public ReplayRecorder Recorder { get; }
    /// <summary>The world at the end of the delve: static layout (walls, stairs, chest and trap order) and the full log.</summary>
    public World Final => Recorder.Final!;
    public Outcome Outcome => Final.Outcome();
    public IReadOnlyList<TickFrame> Frames => _frames;
    public int LastTick => _frames[^1].Tick;

    public static Timeline Record(ContentPack content, DelveSetup setup)
    {
        var frames = new List<TickFrame>();
        var recorder = ReplayRecorder.Record(content, setup, w => frames.Add(Capture(w)));
        return new Timeline(recorder, frames);
    }

    public TickFrame At(int tick) => _frames[Math.Clamp(tick, 0, _frames.Count - 1)];

    /// <summary>The log entries written up to and including <paramref name="tick"/> (the oldest may have been dropped past World.MaxLog).</summary>
    public IEnumerable<LogEntry> LogUpTo(int tick)
    {
        var dropped = Final.LogCount - Final.Log.Count;
        return Final.Log.Take((int)Math.Max(0, At(tick).LogCount - dropped));
    }

    /// <summary>Program state of a golem at the end of <paramref name="tick"/> (re-simulated from the nearest keyframe).</summary>
    public GolemInspection? Inspect(int tick, string golemName)
    {
        var world = Recorder.Seek(tick);
        var g = world.Golems.FirstOrDefault(x => x.Name == golemName);
        if (g is null) return null;
        var vm = g.Vm;
        static Variable V((string Name, Value Value) v) => new(v.Name, v.Value.Repr(), v.Value.TypeName);
        return new GolemInspection(
            g.Name, g.State, g.Line,
            vm?.CallStack() ?? [],
            vm?.GetGlobals().Select(V).ToList() ?? [],
            vm?.GetLocals().Select(V).ToList() ?? [],
            g.Error);
    }

    private static TickFrame Capture(World w)
    {
        var fog = new byte[w.Known.Length];
        for (var i = 0; i < fog.Length; i++) fog[i] = w.Visible[i] ? (byte)2 : w.Known[i] ? (byte)1 : (byte)0;
        return new TickFrame(
            w.Tick,
            w.Golems.Select(g => new GolemFrame(g.Id, g.Name, g.Chassis.Id, g.Pos, g.Hp, g.MaxHp, g.State,
                g.Line, g.Ops, g.Loot, g.StunTicks, g.BurnTicks, g.SlowTicks, g.Facing, g.Mana, g.MaxMana, g.ShieldTicks, (int[])g.Bag.Clone())).ToList(),
            w.Monsters.Select(m => new MonsterFrame(m.Id, m.Def.Id, m.Def.Name, m.Pos, m.Hp, m.MaxHp, m.Alive, m.Intent)).ToList(),
            fog,
            w.Chests.Select(c => c.Opened).ToArray(),
            w.Traps.Select(t => t.Revealed).ToArray(),
            w.Drops.ToList(),
            w.LogCount,
            w.Effects.Count == 0 ? [] : w.Effects.ToList(),
            w.Veins.Select(v => v.Left).ToArray(),
            w.Marks.Count == 0 ? [] : w.Marks.ToList());
    }
}
