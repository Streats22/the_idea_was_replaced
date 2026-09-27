using Delvework.Core.Content;
using Delvework.Core.Dungeon;
using Delvework.Core.Glyph;

namespace Delvework.Core.Sim;

/// <summary>
/// Everything needed to reproduce a delve exactly (together with the content pack).
/// <see cref="Stratum"/> names a generated stratum or a hand-made floor.
/// </summary>
/// <param name="Tablet">Lesson id of a rune tablet to lay on the floor, if any.</param>
public sealed record DelveSetup(ulong Seed, string Stratum, IReadOnlyList<PartyMember> Party, int Tier = Tiers.Max, int? MaxTicks = null, Loadout? Loadout = null, string? Tablet = null);

public static class Delve
{
    /// <summary>Generate (or load) the floor for <paramref name="setup"/> and place the party on it.</summary>
    public static World Create(ContentPack content, DelveSetup setup)
    {
        World? w = null;
        if (content.Strata.TryGetValue(setup.Stratum, out var stratum))
        {
            w = World.Create(content, Generator.Generate(stratum, setup.Seed), setup.Party, setup.Seed, setup.MaxTicks ?? stratum.MaxTicks, setup.Tier, setup.Loadout);
        }
        else if (content.Floors.TryGetValue(setup.Stratum, out var floor))
        {
            w = World.Create(content, floor.ToLayout(), setup.Party, setup.Seed, setup.MaxTicks ?? floor.MaxTicks, setup.Tier, setup.Loadout);
        }
        if (w is not null)
        {
            if (setup.Tablet is { } t) w.PlaceTablet(t);
            return w;
        }
        var known = content.Strata.Keys.Concat(content.Floors.Keys).Order(StringComparer.Ordinal);
        throw new ContentException($"Unknown stratum or floor '{setup.Stratum}'. Known: {string.Join(", ", known)}");
    }

    public static (World World, Outcome Outcome) Run(ContentPack content, DelveSetup setup)
    {
        var w = Create(content, setup);
        w.RunToEnd();
        return (w, w.Outcome());
    }
}
