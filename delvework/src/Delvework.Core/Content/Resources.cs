namespace Delvework.Core.Content;

/// <summary>
/// The village's materials besides gold. The ids appear in content prices, production, the
/// profile's stock and workshop recipes.
/// </summary>
public static class Resources
{
    public const string Gold = "gold";
    public const string Wood = "wood";
    public const string Stone = "stone";
    public const string Ore = "ore";
    public const string Iron = "iron";
    public const string Wheat = "wheat";
    public const string Bread = "bread";
    public const string Pumpkin = "pumpkin";
    public const string Crystal = "crystal";
    public const string Essence = "essence";

    /// <summary>Every stockpiled material, in display order.</summary>
    public static readonly IReadOnlyList<string> All = [Wood, Stone, Ore, Iron, Wheat, Bread, Pumpkin, Crystal, Essence];

    /// <summary>What golems dig out of mine walls, in the order of the Glyph <c>Resource</c> enum.</summary>
    public static readonly IReadOnlyList<string> Mined = [Stone, Ore, Wood, Crystal];

    /// <summary>The enum member a golem program uses for a mined material (<c>Ore</c> for iron ore).</summary>
    public static readonly IReadOnlyList<string> MinedEnumNames = ["Stone", "Ore", "Wood", "Crystal"];

    public static bool IsKnown(string id) => id == Gold || All.Contains(id);

    public static string Name(string id) => id switch
    {
        Ore => "iron ore",
        Essence => "monster essence",
        _ => id,
    };

    /// <summary>"3 stone, 2 iron ore" in display order; zero amounts are skipped.</summary>
    public static string Format(IReadOnlyDictionary<string, int>? amounts)
    {
        if (amounts is null) return "";
        var parts = new List<string>();
        foreach (var id in new[] { Gold }.Concat(All))
        {
            if (amounts.TryGetValue(id, out var n) && n != 0) parts.Add($"{n} {Name(id)}");
        }
        return string.Join(", ", parts);
    }

    /// <summary>Add <paramref name="b"/> to a copy of <paramref name="a"/>.</summary>
    public static Dictionary<string, int> Sum(IReadOnlyDictionary<string, int>? a, IReadOnlyDictionary<string, int>? b)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var src in new[] { a, b })
        {
            if (src is null) continue;
            foreach (var (k, v) in src) d[k] = d.GetValueOrDefault(k) + v;
        }
        return d;
    }
}
