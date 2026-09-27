using System.Text.Json;

namespace Delvework.Core.Progress;

/// <summary>Everything the player has earned and written. Saved as JSON by the front end.</summary>
public sealed class Profile
{
    public const int CurrentVersion = 4;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public int Version { get; set; } = CurrentVersion;
    public int Gold { get; set; }
    /// <summary>Materials in the village storehouse by resource id (see <see cref="Content.Resources"/>).</summary>
    public Dictionary<string, int> Stock { get; set; } = [];
    /// <summary>Lesson ids whose challenge has been passed.</summary>
    public List<string> Completed { get; set; } = [];
    /// <summary>Lesson ids whose feature was bought at the Library.</summary>
    public List<string> Learned { get; set; } = [];
    /// <summary>Owned skill node ids.</summary>
    public List<string> Owned { get; set; } = [];
    /// <summary>Program source per slot: <c>lesson/&lt;id&gt;/&lt;chassis&gt;</c> or <c>delve/&lt;chassis&gt;</c>.</summary>
    public Dictionary<string, string> Programs { get; set; } = [];
    /// <summary>Codex pages the player has opened, so new ones can be highlighted.</summary>
    public List<string> Read { get; set; } = [];
    public string Site { get; set; } = "";
    public ulong Seed { get; set; } = 1;
    public int Delves { get; set; }
    public int GoldEarned { get; set; }
    /// <summary>Workshop shifts run so far.</summary>
    public int Shifts { get; set; }
    /// <summary>Everything counted for the Almanac so far, like <c>killed:slime</c> or <c>bakery:burnt</c>.</summary>
    public Dictionary<string, int> Found { get; set; } = [];
    /// <summary>Almanac entries found (and paid for), in the order they were found.</summary>
    public List<string> Almanac { get; set; } = [];
    /// <summary>Best commission results by commission id.</summary>
    public Dictionary<string, CommissionBest> Commissions { get; set; } = [];
    public double MusicVolume { get; set; } = 0.6;
    public double SfxVolume { get; set; } = 0.8;

    public static string LessonSlot(string lessonId, string chassis) => $"lesson/{lessonId}/{chassis}";

    public static string DelveSlot(string chassis) => $"delve/{chassis}";

    public static string WorkshopSlot(string workshop) => $"workshop/{workshop}";

    /// <summary>How much of a resource the player has; <c>gold</c> reads <see cref="Gold"/>.</summary>
    public int Amount(string id) => id == Content.Resources.Gold ? Gold : Stock.GetValueOrDefault(id);

    public void Add(string id, int amount)
    {
        if (id == Content.Resources.Gold)
        {
            Gold += amount;
            return;
        }
        var n = Stock.GetValueOrDefault(id) + amount;
        if (n == 0) Stock.Remove(id);
        else Stock[id] = n;
    }

    public void Add(IReadOnlyDictionary<string, int>? amounts)
    {
        if (amounts is null) return;
        foreach (var (id, n) in amounts) Add(id, n);
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static Profile FromJson(string json)
    {
        var p = JsonSerializer.Deserialize<Profile>(json, Json) ?? new Profile();
        if (p.Version < 2)
        {
            // Version 1 unlocked features by passing lessons; keep what those players had.
            foreach (var id in p.Completed.Where(id => !p.Learned.Contains(id))) p.Learned.Add(id);
        }
        p.Version = CurrentVersion;
        return p;
    }
}
