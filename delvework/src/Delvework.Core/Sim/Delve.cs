using Delvework.Core.Content;
using Delvework.Core.Dungeon;
using Delvework.Core.Glyph;

namespace Delvework.Core.Sim;

/// <summary>
/// Everything needed to reproduce a delve exactly (together with the content pack).
/// <see cref="Stratum"/> names a generated stratum or a hand-made floor.
/// </summary>
public sealed record DelveSetup(ulong Seed, string Stratum, IReadOnlyList<PartyMember> Party, int Tier = Tiers.Max, int? MaxTicks = null, Loadout? Loadout = null);

public static class Delve
{
    /// <summary>Generate (or load) the floor for <paramref name="setup"/> and place the party on it.</summary>
    public static World Create(ContentPack content, DelveSetup setup)
    {
        if (content.Strata.TryGetValue(setup.Stratum, out var stratum))
        {
            var layout = Generator.Generate(stratum, setup.Seed);
            return World.Create(content, layout, setup.Party, setup.Seed, setup.MaxTicks ?? stratum.MaxTicks, setup.Tier, setup.Loadout);
        }
        if (content.Floors.TryGetValue(setup.Stratum, out var floor))
        {
            return World.Create(content, floor.ToLayout(), setup.Party, setup.Seed, setup.MaxTicks ?? floor.MaxTicks, setup.Tier, setup.Loadout);
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
