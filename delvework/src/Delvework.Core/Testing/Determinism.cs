using Delvework.Core.Content;
using Delvework.Core.Glyph;
using Delvework.Core.Sim;

namespace Delvework.Core.Testing;

public sealed record DeterminismReport(int Seeds, ulong CombinedHash, IReadOnlyList<ulong> Mismatches, IReadOnlyDictionary<OutcomeKind, int> Outcomes);

/// <summary>Runs many seeds twice each and folds the final state hashes into one number to compare across machines.</summary>
public static class Determinism
{
    public static IReadOnlyList<PartyMember> SampleParty(ContentPack content) =>
    [
        new("Warden", "warden", content.Programs["warden"]),
        new("Seeker", "seeker", content.Programs["seeker"]),
    ];

    public static ulong RunSeed(ContentPack content, ulong seed, IReadOnlyList<PartyMember> party, string stratum = "mines", int? maxTicks = null)
    {
        var (world, _) = Delve.Run(content, new DelveSetup(seed, stratum, party, MaxTicks: maxTicks));
        return world.StateHash();
    }

    public static DeterminismReport Sweep(ContentPack content, ulong firstSeed, int count, IReadOnlyList<PartyMember>? party = null,
        int? maxTicks = null, bool twice = true, Action<int>? progress = null)
    {
        party ??= SampleParty(content);
        var combined = Fnv.Offset;
        var mismatches = new List<ulong>();
        var outcomes = new SortedDictionary<OutcomeKind, int>();
        for (var i = 0; i < count; i++)
        {
            var seed = firstSeed + (ulong)i;
            var (world, outcome) = Delve.Run(content, new DelveSetup(seed, "mines", party, MaxTicks: maxTicks));
            var a = world.StateHash();
            if (twice && RunSeed(content, seed, party, maxTicks: maxTicks) != a) mismatches.Add(seed);
            combined = Fnv.Mix(combined, a);
            outcomes[outcome.Kind] = outcomes.GetValueOrDefault(outcome.Kind) + 1;
            progress?.Invoke(i + 1);
        }
        return new DeterminismReport(count, combined, mismatches, outcomes);
    }
}
