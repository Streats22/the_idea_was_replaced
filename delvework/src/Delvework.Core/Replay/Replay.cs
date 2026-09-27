using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Delvework.Core.Content;
using Delvework.Core.Glyph;
using Delvework.Core.Sim;

namespace Delvework.Core.Replay;

public sealed record ReplayGolem(string Name, string Chassis, string SourceHash, string Source);

/// <summary>
/// A replay is inputs, not frames: seed, content version, the party's code (and its hashes),
/// plus state-hash checkpoints every <see cref="ReplayRecorder.KeyframeInterval"/> ticks that
/// let <see cref="ReplayVerifier"/> find the first tick where a re-simulation diverges.
/// </summary>
public sealed record ReplayFile
{
    public const int CurrentFormat = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public int Format { get; init; } = CurrentFormat;
    public ulong Seed { get; init; }
    public string Stratum { get; init; } = "";
    public string ContentVersion { get; init; } = "";
    public int Tier { get; init; } = Tiers.Max;
    public int? MaxTicks { get; init; }
    public Loadout? Loadout { get; init; }
    public string? Tablet { get; init; }
    public List<ReplayGolem> Party { get; init; } = [];
    public int FinalTick { get; init; }
    public string FinalHash { get; init; } = "";
    public OutcomeKind Outcome { get; init; }
    public int Loot { get; init; }
    /// <summary>Tick to state hash (hex), one entry per keyframe.</summary>
    public SortedDictionary<int, string> Checkpoints { get; init; } = [];

    public DelveSetup ToSetup() =>
        new(Seed, Stratum, Party.Select(p => new PartyMember(p.Name, p.Chassis, p.Source)).ToList(), Tier, MaxTicks, Loadout, Tablet);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static ReplayFile FromJson(string json) =>
        JsonSerializer.Deserialize<ReplayFile>(json, Json) ?? throw new InvalidDataException("Empty replay file");

    public void Save(string path) => File.WriteAllText(path, ToJson());

    public static ReplayFile Load(string path) => FromJson(File.ReadAllText(path));

    public static string Hex(ulong h) => h.ToString("x16", CultureInfo.InvariantCulture);

    public static string SourceHash(string source) => Hex(Fnv.Mix(Fnv.Offset, source.Replace("\r\n", "\n", StringComparison.Ordinal)));
}

/// <summary>Runs a delve while keeping world snapshots every 50 ticks, so any tick can be reached quickly.</summary>
public sealed class ReplayRecorder
{
    public const int KeyframeInterval = 50;

    private readonly SortedList<int, World> _keyframes = [];

    private ReplayRecorder(ContentPack content, DelveSetup setup)
    {
        Content = content;
        Setup = setup;
    }

    public ContentPack Content { get; }
    public DelveSetup Setup { get; }
    public IReadOnlyList<int> KeyframeTicks => _keyframes.Keys.ToList();
    public World? Final { get; private set; }
    public ReplayFile? File { get; private set; }

    /// <summary>Run the delve to the end. <paramref name="observe"/> sees the world at tick 0 and after every step.</summary>
    public static ReplayRecorder Record(ContentPack content, DelveSetup setup, Action<World>? observe = null)
    {
        var rec = new ReplayRecorder(content, setup);
        var world = Delve.Create(content, setup);
        var checkpoints = new SortedDictionary<int, string>();
        void Keyframe()
        {
            rec._keyframes[world.Tick] = world.Clone();
            checkpoints[world.Tick] = ReplayFile.Hex(world.StateHash());
        }
        Keyframe();
        observe?.Invoke(world);
        while (!world.Finished)
        {
            world.Step();
            if (world.Tick % KeyframeInterval == 0) Keyframe();
            observe?.Invoke(world);
        }
        if (!checkpoints.ContainsKey(world.Tick)) Keyframe();
        var outcome = world.Outcome();
        rec.Final = world;
        rec.File = new ReplayFile
        {
            Seed = setup.Seed,
            Stratum = setup.Stratum,
            ContentVersion = content.Version,
            Tier = setup.Tier,
            MaxTicks = setup.MaxTicks,
            Loadout = setup.Loadout,
            Tablet = setup.Tablet,
            Party = setup.Party.Select(p => new ReplayGolem(p.Name, p.Chassis, ReplayFile.SourceHash(p.Source), p.Source)).ToList(),
            FinalTick = world.Tick,
            FinalHash = ReplayFile.Hex(world.StateHash()),
            Outcome = outcome.Kind,
            Loot = outcome.Loot,
            Checkpoints = checkpoints,
        };
        return rec;
    }

    /// <summary>
    /// The world as it was at the end of <paramref name="tick"/>: clone the nearest keyframe at
    /// or before it and re-simulate forward. The returned world is independent of the recording.
    /// </summary>
    public World Seek(int tick)
    {
        if (_keyframes.Count == 0) throw new InvalidOperationException("Nothing recorded");
        tick = Math.Clamp(tick, 0, Final!.Tick);
        var keys = _keyframes.Keys;
        int lo = 0, hi = keys.Count - 1, best = 0;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (keys[mid] <= tick)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        var w = _keyframes.Values[best].Clone();
        while (w.Tick < tick && !w.Finished) w.Step();
        return w;
    }
}

public sealed record VerifyResult(bool Ok, string Message, int? DivergedAtTick = null, IReadOnlyList<string>? Warnings = null);

public static class ReplayVerifier
{
    /// <summary>Re-simulate a replay and compare every checkpoint hash.</summary>
    public static VerifyResult Verify(ContentPack content, ReplayFile file)
    {
        var warnings = new List<string>();
        if (file.Format != ReplayFile.CurrentFormat) return new VerifyResult(false, $"Unsupported replay format {file.Format}");
        if (file.ContentVersion != content.Version)
        {
            warnings.Add($"Content version differs: replay {file.ContentVersion}, installed {content.Version}");
        }
        foreach (var g in file.Party)
        {
            if (ReplayFile.SourceHash(g.Source) != g.SourceHash)
            {
                return new VerifyResult(false, $"{g.Name}'s program does not match its recorded hash", Warnings: warnings);
            }
        }
        World world;
        try
        {
            world = Delve.Create(content, file.ToSetup());
        }
        catch (ContentException e)
        {
            return new VerifyResult(false, e.Message, Warnings: warnings);
        }
        int? lastChecked = null;
        foreach (var (tick, expected) in file.Checkpoints)
        {
            while (world.Tick < tick && !world.Finished) world.Step();
            if (world.Tick != tick)
            {
                return new VerifyResult(false, $"Delve ended at tick {world.Tick}, before checkpoint {tick}", lastChecked ?? 0, warnings);
            }
            var actual = ReplayFile.Hex(world.StateHash());
            if (actual != expected)
            {
                return new VerifyResult(false, $"State diverged between tick {lastChecked ?? 0} and tick {tick}", tick, warnings);
            }
            lastChecked = tick;
        }
        world.RunToEnd();
        if (world.Tick != file.FinalTick || ReplayFile.Hex(world.StateHash()) != file.FinalHash)
        {
            return new VerifyResult(false, $"Final state differs (tick {world.Tick} vs {file.FinalTick})", world.Tick, warnings);
        }
        return new VerifyResult(true, $"Replay verified: {file.Checkpoints.Count} checkpoints, {file.FinalTick} ticks", Warnings: warnings);
    }
}
