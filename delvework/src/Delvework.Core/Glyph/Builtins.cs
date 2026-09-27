namespace Delvework.Core.Glyph;

/// <summary>The arguments of one builtin call. Only valid for the duration of the call.</summary>
public readonly ref struct BuiltinCall
{
    public BuiltinCall(Vm vm, ReadOnlySpan<Value> args, ReadOnlySpan<Value> kwValues, string[] kwNames, Value self, int line)
    {
        Vm = vm;
        Args = args;
        KwValues = kwValues;
        KwNames = kwNames;
        Self = self;
        Line = line;
    }

    public Vm Vm { get; }
    public ReadOnlySpan<Value> Args { get; }
    public ReadOnlySpan<Value> KwValues { get; }
    public string[] KwNames { get; }
    /// <summary>The receiver for methods (<c>xs</c> in <c>xs.append(1)</c>); None otherwise.</summary>
    public Value Self { get; }
    public int Line { get; }
    public int Count => Args.Length;

    public Value Arg(int i) => i < Args.Length ? Args[i] : Value.None;

    /// <summary>A keyword argument, falling back to the positional argument at <paramref name="position"/>.</summary>
    public Value Get(string name, int position, Value fallback)
    {
        for (var i = 0; i < KwNames.Length; i++)
        {
            if (KwNames[i] == name) return KwValues[i];
        }
        return position < Args.Length ? Args[position] : fallback;
    }

    public GlyphError Error(string message, GlyphErrorKind kind = GlyphErrorKind.Type) => new(kind, message, Line);

    public long Int(int i, string fn)
    {
        var v = Arg(i);
        if (v.Kind == ValueKind.Int) return v.AsInt;
        if (v.Kind == ValueKind.Float && v.AsFix.IsInteger) return v.AsFix.Floor();
        throw Error($"{fn}() expects a whole number, got {v.TypeName}");
    }

    public string Str(int i, string fn)
    {
        var v = Arg(i);
        return v.Kind == ValueKind.Str ? v.AsStr : throw Error($"{fn}() expects a string, got {v.TypeName}");
    }
}

public delegate Value BuiltinImpl(in BuiltinCall call);

/// <summary>
/// Metadata plus implementation for one builtin. The metadata also drives the capability gate,
/// instruction costs, autocomplete and generated docs.
/// </summary>
public sealed class BuiltinDef
{
    public required string Name { get; init; }
    public required BuiltinImpl Impl { get; init; }
    /// <summary>Lowest Glyph tier that may call this builtin.</summary>
    public int Tier { get; init; } = 1;
    /// <summary>Instruction cost of the call itself; implementations can <see cref="Vm.Charge"/> more.</summary>
    public int Cost { get; init; } = 1;
    public int MinArgs { get; init; }
    public int MaxArgs { get; init; }
    public string[] Keywords { get; init; } = [];
    /// <summary>Actions end the caller's turn; the host supplies the result with <see cref="Vm.ResolveAction"/>.</summary>
    public bool IsAction { get; init; }
    public string Signature { get; init; } = "";
    public string Doc { get; init; } = "";
}

/// <summary>
/// The Glyph feature ladder. Each tier is one Codex lesson: a player who has learned lesson N
/// may use every feature up to tier N. Used by the capability gate and in its error messages.
/// </summary>
public static class Tiers
{
    public const int Min = 1;
    public const int Max = 10;

    /// <summary>Calling functions such as <c>move(East)</c>, one statement after another.</summary>
    public const int Calls = 1;
    /// <summary><c>while</c>, <c>break</c>, <c>continue</c>.</summary>
    public const int Loops = 2;
    /// <summary><c>if</c> / <c>elif</c> / <c>else</c>, comparisons, <c>and</c> / <c>or</c> / <c>not</c>.</summary>
    public const int Conditions = 3;
    public const int Variables = 4;
    public const int Functions = 5;
    public const int Lists = 6;
    public const int Events = 7;
    public const int Dicts = 8;
    public const int Modules = 9;
    public const int Coroutines = 10;

    private static readonly string[] Names =
    [
        "", "Commands", "Loops", "Decisions", "Variables", "Functions", "Lists", "Events", "Dictionaries", "Modules", "Waiting",
    ];

    public static string Name(int tier) => tier >= 1 && tier < Names.Length ? Names[tier] : $"Tier {tier}";

    public static string Describe(int tier) => $"Codex {tier} ({Name(tier)})";

    public static string LockedMessage(string feature, int needed, int known) =>
        $"{feature} is not unlocked yet: it is taught in {Describe(needed)}. This golem's core knows up to {Describe(known)}.";
}

/// <summary>
/// The names a program can see before it defines anything: builtins, enums and constants.
/// Golems and monsters get different environments. Immutable once built and shared by all VMs.
/// </summary>
public sealed class GlyphEnvironment
{
    private readonly List<(string Name, Value Value, int Tier)> _entries = [];
    private readonly Dictionary<string, int> _index = [];
    private readonly HashSet<string> _events = [];
    private bool _frozen;

    public IReadOnlyList<(string Name, Value Value, int Tier)> Entries => _entries;

    /// <summary>Event names <c>on</c> handlers may use. Empty means any name is accepted.</summary>
    public IReadOnlySet<string> Events => _events;

    public GlyphEnvironment AddEvents(params string[] names)
    {
        if (_frozen) throw new InvalidOperationException("Environment is frozen");
        _events.UnionWith(names);
        return this;
    }

    public GlyphEnvironment Add(BuiltinDef def) => Add(def.Name, Value.Builtin(new BuiltinObj(def, Value.None)), def.Tier);

    public GlyphEnvironment Add(string name, Value value, int tier = Tiers.Calls)
    {
        if (_frozen) throw new InvalidOperationException("Environment is frozen");
        if (_index.TryGetValue(name, out var i))
        {
            _entries[i] = (name, value, tier);
        }
        else
        {
            _index[name] = _entries.Count;
            _entries.Add((name, value, tier));
        }
        return this;
    }

    public GlyphEnvironment AddEnum(EnumType type, bool exposeMembers = false)
    {
        Add(type.Name, Value.EnumType(type));
        if (exposeMembers)
        {
            foreach (var m in type.Members) Add(m.Name, m.AsValue());
        }
        return this;
    }

    public GlyphEnvironment Freeze()
    {
        _frozen = true;
        return this;
    }

    public bool TryGetIndex(string name, out int index) => _index.TryGetValue(name, out index);

    public BuiltinDef? Builtin(string name) =>
        _index.TryGetValue(name, out var i) && _entries[i].Value.Kind == ValueKind.Builtin ? _entries[i].Value.AsBuiltin.Def : null;

    public IEnumerable<BuiltinDef> Builtins => _entries.Where(e => e.Value.Kind == ValueKind.Builtin).Select(e => e.Value.AsBuiltin.Def);
}
