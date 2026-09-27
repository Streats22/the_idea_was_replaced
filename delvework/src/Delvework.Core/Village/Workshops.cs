using Delvework.Core.Content;
using Delvework.Core.Glyph;

namespace Delvework.Core.Village;

/// <summary>
/// One tick of a workshop shift: the line that ran, every counter, and what happened.
/// <see cref="Tiles"/> is the field of a grid workshop (see <see cref="Farm.Decode"/>).
/// </summary>
public sealed record WorkshopFrame(int Tick, int Line, IReadOnlyDictionary<string, int> State, string? Event, IReadOnlyList<int>? Tiles = null);

/// <summary>
/// A finished shift. <see cref="Used"/> and <see cref="Made"/> are what to take from and add to
/// the village stock. The frames let the game replay the shift tick by tick.
/// </summary>
public sealed record WorkshopResult(
    WorkshopDef Def,
    IReadOnlyDictionary<string, int> Used,
    IReadOnlyDictionary<string, int> Made,
    IReadOnlyList<WorkshopFrame> Frames,
    IReadOnlyList<string> Log,
    GlyphError? Error,
    string Summary,
    IReadOnlyDictionary<string, int>? Found = null)
{
    public int LastTick => Frames[^1].Tick;

    public WorkshopFrame At(int tick) => Frames[Math.Clamp(tick, 0, Frames.Count - 1)];
}

/// <summary>The state a workshop script works on. Builtins cast <see cref="Vm.Host"/> to this.</summary>
public abstract class WorkshopState
{
    protected WorkshopState(IReadOnlyDictionary<string, int> stock, IReadOnlyList<string> inputs)
    {
        foreach (var id in inputs) Counters[id] = stock.GetValueOrDefault(id);
    }

    /// <summary>Everything the script can read, by name; includes the input materials.</summary>
    public SortedDictionary<string, int> Counters { get; } = new(StringComparer.Ordinal);
    public int Tick { get; set; }
    public string? Event { get; set; }
    public List<string> Log { get; } = [];

    public int this[string key]
    {
        get => Counters.GetValueOrDefault(key);
        set => Counters[key] = value;
    }

    /// <summary>What happens on its own at the end of every tick (the furnace cools, bread bakes).</summary>
    public abstract void EndTick();

    /// <summary>True when nothing is still in progress, so the shift can end once the script is done.</summary>
    public virtual bool Settled => true;

    /// <summary>A copy of the workshop's field for the frame, or null for workshops without one.</summary>
    public virtual int[]? Tiles() => null;

    /// <summary>Things that happened this shift for the Almanac, like <c>burnt</c>, counted.</summary>
    public SortedDictionary<string, int> Found { get; } = new(StringComparer.Ordinal);

    public void Discover(string key, int n = 1) => Found[key] = Found.GetValueOrDefault(key) + n;

    public void Note(string text)
    {
        Event = text;
        Log.Add($"{Tick / 10.0:0.0}s  {text}");
    }
}

/// <summary>A workshop's rules: its functions, which materials go in and come out, and its state.</summary>
public sealed class WorkshopKind
{
    public required string Id { get; init; }
    public required IReadOnlyList<string> Inputs { get; init; }
    public required IReadOnlyList<string> Outputs { get; init; }
    public required GlyphEnvironment Environment { get; init; }
    public required Func<IReadOnlyDictionary<string, int>, WorkshopState> Create { get; init; }
}

/// <summary>
/// Runs a workshop shift: the player's script is compiled against the workshop's functions and
/// stepped once per tick, like a golem. Deterministic, so a preview matches the real shift.
/// </summary>
public static class Workshops
{
    public const int Budget = 100;

    public static readonly IReadOnlyDictionary<string, WorkshopKind> Kinds = new Dictionary<string, WorkshopKind>(StringComparer.Ordinal)
    {
        [Smelter.Id] = Smelter.Kind,
        [Bakery.Id] = Bakery.Kind,
        [Farm.Id] = Farm.Kind,
    };

    public static WorkshopKind KindOf(WorkshopDef def) =>
        Kinds.TryGetValue(def.Id, out var k) ? k : throw new ContentException($"Workshop '{def.Id}' has no rules in code");

    public static GlyphProgram Compile(WorkshopDef def, string source, int tier) =>
        GlyphCompiler.Compile(source, KindOf(def).Environment, new CompileOptions { Tier = tier });

    public static WorkshopResult Run(WorkshopDef def, string source, IReadOnlyDictionary<string, int> stock, int tier = Tiers.Max)
    {
        var kind = KindOf(def);
        var state = kind.Create(stock);
        var start = new Dictionary<string, int>(state.Counters, StringComparer.Ordinal);
        var frames = new List<WorkshopFrame> { new(0, 0, Snapshot(state), null, state.Tiles()) };
        GlyphError? error = null;
        Vm? vm = null;
        try
        {
            vm = new Vm(Compile(def, source, tier), state);
        }
        catch (GlyphError e)
        {
            error = e;
        }
        var line = 0;
        for (var tick = 1; vm is not null && tick <= def.ShiftTicks; tick++)
        {
            state.Tick = tick;
            state.Event = null;
            if (!vm.IsFinished)
            {
                var r = vm.Step(Budget);
                line = r.Line;
                foreach (var output in vm.DrainOutput()) state.Log.Add($"{tick / 10.0:0.0}s  print: {output}");
                if (r.Status == VmStatus.Error)
                {
                    error = r.Error;
                    state.Note($"The script stopped on line {r.Line}: {r.Error!.Message}");
                }
            }
            state.EndTick();
            frames.Add(new WorkshopFrame(tick, vm.IsFinished ? 0 : line, Snapshot(state), state.Event, state.Tiles()));
            if (vm.IsFinished && state.Settled) break;
        }
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        var made = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in kind.Inputs)
        {
            var n = start[id] - state[id];
            if (n > 0) used[id] = n;
        }
        foreach (var id in kind.Outputs)
        {
            if (state[id] > 0) made[id] = state[id];
        }
        var summary = error is not null && vm is null
            ? $"{def.Name}: the script has an error on line {error.Line}: {error.Message}"
            : made.Count == 0 ? $"{def.Name}: nothing made this shift."
            : used.Count == 0 ? $"{def.Name}: made {Resources.Format(made)}."
            : $"{def.Name}: made {Resources.Format(made)} from {Resources.Format(used)}.";
        return new WorkshopResult(def, used, made, frames, state.Log, error, summary, state.Found);
    }

    private static Dictionary<string, int> Snapshot(WorkshopState s) => new(s.Counters, StringComparer.Ordinal);

    internal static BuiltinDef Query(string name, Func<WorkshopState, int> read, string doc) => new()
    {
        Name = name,
        Impl = (in BuiltinCall c) => Value.Int(read((WorkshopState)c.Vm.Host!)),
        Signature = name + "()",
        Doc = doc,
    };

    internal static BuiltinDef Flag(string name, Func<WorkshopState, bool> read, string doc) => new()
    {
        Name = name,
        Impl = (in BuiltinCall c) => Value.Bool(read((WorkshopState)c.Vm.Host!)),
        Signature = name + "()",
        Doc = doc,
    };

    internal delegate bool WorkshopAction(WorkshopState state, in BuiltinCall call);

    /// <summary>An action takes one tick (plus <paramref name="extraTicks"/>) and returns True if it worked.</summary>
    internal static BuiltinDef Action(string name, WorkshopAction act, string doc, int extraTicks = 0, int args = 0, string? sig = null) => new()
    {
        Name = name,
        IsAction = true,
        MinArgs = args,
        MaxArgs = args,
        Impl = (in BuiltinCall c) =>
        {
            var ok = act((WorkshopState)c.Vm.Host!, c);
            if (extraTicks > 0) c.Vm.Block(1 + extraTicks);
            return Value.Bool(ok);
        },
        Signature = sig ?? name + "()",
        Doc = doc,
    };

    internal static GlyphEnvironment Environment(IEnumerable<BuiltinDef> defs, params EnumType[] enums)
    {
        var env = Stdlib.AddTo(new GlyphEnvironment());
        foreach (var e in enums) env.AddEnum(e, exposeMembers: true);
        foreach (var d in defs) env.Add(d);
        return env.Freeze();
    }

}

/// <summary>
/// The Smelter: keep the furnace hot enough (5 or more) and smelt ore into iron. Every stoke
/// burns one wood for +5 heat, up to 10; heat above that is wasted. The furnace cools by 1 each
/// tick it isn't stoked.
/// </summary>
public static class Smelter
{
    public const string Id = "smelter";
    public const int MeltHeat = 5;
    public const int MaxHeat = 10;
    public const int StokeHeat = 5;

    private sealed class State(IReadOnlyDictionary<string, int> stock) : WorkshopState(stock, [Resources.Ore, Resources.Wood])
    {
        public bool Stoked { get; set; }

        public override void EndTick()
        {
            if (this["heat"] > 0 && !Stoked) this["heat"]--;
            Stoked = false;
        }
    }

    public static readonly WorkshopKind Kind = new()
    {
        Id = Id,
        Inputs = [Resources.Ore, Resources.Wood],
        Outputs = [Resources.Iron],
        Create = stock =>
        {
            var s = new State(stock);
            s["heat"] = 0;
            s[Resources.Iron] = 0;
            s["wasted"] = 0;
            return s;
        },
        Environment = Workshops.Environment(
        [
            Workshops.Query("ore", s => s[Resources.Ore], "Iron ore waiting to be smelted."),
            Workshops.Query("wood", s => s[Resources.Wood], "Wood left for the fire."),
            Workshops.Query("iron", s => s[Resources.Iron], "Iron bars made this shift."),
            Workshops.Query("heat", s => s["heat"], $"How hot the furnace is, 0 to {MaxHeat}. Ore melts at {MeltHeat} or more. It cools by 1 every tick you don't stoke."),
            Workshops.Action("stoke", (WorkshopState s, in BuiltinCall _) =>
            {
                if (s[Resources.Wood] <= 0)
                {
                    s.Note("No wood left to stoke the fire");
                    return false;
                }
                s[Resources.Wood]--;
                var heat = s["heat"] + StokeHeat;
                if (heat > MaxHeat)
                {
                    s["wasted"] += heat - MaxHeat;
                    s.Discover("smelter:wasted");
                    s.Note($"Stoked: heat {MaxHeat}, but {heat - MaxHeat} heat went up the chimney");
                    heat = MaxHeat;
                }
                else
                {
                    s.Note($"Stoked: heat {heat}");
                }
                s["heat"] = heat;
                ((State)s).Stoked = true;
                return true;
            }, $"Burn 1 wood: +{StokeHeat} heat (up to {MaxHeat}; anything above is wasted). Returns False without wood."),
            Workshops.Action("smelt", (WorkshopState s, in BuiltinCall _) =>
            {
                if (s[Resources.Ore] <= 0)
                {
                    s.Note("No ore left to smelt");
                    return false;
                }
                if (s["heat"] < MeltHeat)
                {
                    s.Note($"Too cold to smelt (heat {s["heat"]}, needs {MeltHeat})");
                    s.Discover("smelter:cold");
                    return false;
                }
                s[Resources.Ore]--;
                s[Resources.Iron]++;
                s.Note("Smelted 1 iron");
                return true;
            }, $"Melt 1 ore into 1 iron. Needs heat {MeltHeat} or more; returns False if too cold or out of ore."),
        ]),
    };
}

/// <summary>
/// The Bakery: knead 2 wheat into dough, light the oven with wood, bake up to 4 dough at once
/// and take the bread out after 4 to 7 ticks in a burning oven. Too early and it goes back in
/// as dough; too late and it burns.
/// </summary>
public static class Bakery
{
    public const string Id = "bakery";
    public const int WheatPerDough = 2;
    public const int FireTicks = 12;
    public const int MaxFire = 30;
    public const int Tray = 4;
    public const int DoneAt = 4;
    public const int BurntAfter = 7;

    private sealed class State(IReadOnlyDictionary<string, int> stock) : WorkshopState(stock, [Resources.Wheat, Resources.Wood])
    {
        public override void EndTick()
        {
            if (this["tray"] > 0 && this["fire"] > 0) this["baked"]++;
            if (this["fire"] > 0) this["fire"]--;
        }

        public override bool Settled => this["tray"] == 0;
    }

    public static readonly WorkshopKind Kind = new()
    {
        Id = Id,
        Inputs = [Resources.Wheat, Resources.Wood],
        Outputs = [Resources.Bread],
        Create = stock =>
        {
            var s = new State(stock);
            s["dough"] = 0;
            s["fire"] = 0;
            s["tray"] = 0;
            s["baked"] = 0;
            s["burnt"] = 0;
            s[Resources.Bread] = 0;
            return s;
        },
        Environment = Workshops.Environment(
        [
            Workshops.Query("wheat", s => s[Resources.Wheat], "Wheat in the flour bin."),
            Workshops.Query("wood", s => s[Resources.Wood], "Wood left for the oven."),
            Workshops.Query("dough", s => s["dough"], "Dough ready to bake."),
            Workshops.Query("bread", s => s[Resources.Bread], "Loaves baked this shift."),
            Workshops.Query("fire", s => s["fire"], "Ticks the oven fire keeps burning. Bread only bakes while it burns."),
            Workshops.Query("tray", s => s["tray"], $"Loaves in the oven (up to {Tray})."),
            Workshops.Query("baked", s => s["tray"] > 0 ? s["baked"] : -1, $"Ticks the loaves in the oven have baked, or -1 if it's empty. They're done from {DoneAt} to {BurntAfter}."),
            Workshops.Action("knead", (WorkshopState s, in BuiltinCall _) =>
            {
                if (s[Resources.Wheat] < WheatPerDough)
                {
                    s.Note($"Not enough wheat to knead (needs {WheatPerDough})");
                    return false;
                }
                s[Resources.Wheat] -= WheatPerDough;
                s["dough"]++;
                s.Note("Kneaded 1 dough");
                return true;
            }, $"Turn {WheatPerDough} wheat into 1 dough. Returns False without enough wheat."),
            Workshops.Action("light", (WorkshopState s, in BuiltinCall _) =>
            {
                if (s[Resources.Wood] <= 0)
                {
                    s.Note("No wood to light the oven");
                    return false;
                }
                s[Resources.Wood]--;
                s["fire"] = Math.Min(MaxFire, s["fire"] + FireTicks);
                s.Note($"Fire lit: it burns for {s["fire"]} ticks");
                return true;
            }, $"Burn 1 wood: the oven fire burns {FireTicks} ticks longer (up to {MaxFire})."),
            Workshops.Action("bake", (WorkshopState s, in BuiltinCall _) =>
            {
                if (s["tray"] > 0 || s["dough"] <= 0)
                {
                    s.Note(s["tray"] > 0 ? "The oven is already full: take_out() first" : "No dough to bake");
                    return false;
                }
                var n = Math.Min(Tray, s["dough"]);
                s["dough"] -= n;
                s["tray"] = n;
                s["baked"] = 0;
                s.Note($"{n} loaves into the oven" + (s["fire"] > 0 ? "" : " (but the fire is out!)"));
                return true;
            }, $"Put up to {Tray} dough in the oven. Returns False if it's full or there's no dough."),
            Workshops.Action("take_out", (WorkshopState s, in BuiltinCall _) =>
            {
                var n = s["tray"];
                if (n == 0)
                {
                    s.Note("The oven is empty");
                    return false;
                }
                var baked = s["baked"];
                s["tray"] = 0;
                s["baked"] = 0;
                if (baked < DoneAt)
                {
                    s["dough"] += n;
                    s.Discover("bakery:raw");
                    s.Note($"Still raw after {baked} ticks: back to the dough pile");
                    return false;
                }
                if (baked > BurntAfter)
                {
                    s["burnt"] += n;
                    s.Discover("bakery:burnt");
                    s.Note($"Burnt! {n} loaves baked {baked} ticks (done from {DoneAt} to {BurntAfter})");
                    return false;
                }
                s[Resources.Bread] += n;
                if (n == Tray) s.Discover("bakery:full_tray");
                s.Note($"{n} golden loaves");
                return true;
            }, $"Take the loaves out: bread if they baked {DoneAt} to {BurntAfter} ticks, burnt if longer, back to dough if shorter."),
        ]),
    };
}
