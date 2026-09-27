using Delvework.Core.Content;

namespace Delvework.Core.Progress;

public enum MonsterKnowledge
{
    Unknown,
    /// <summary>A golem has seen one: its stats and lore.</summary>
    Seen,
    /// <summary>Defeated a few times: its weakness.</summary>
    Studied,
    /// <summary>Defeated many times: its whole script.</summary>
    Mastered,
}

/// <summary>
/// The Almanac: everything the player has found out by playing, the in-game Bestiary included.
/// Entries unlock when a discovery key (counted by delves and workshop shifts) reaches its count.
/// </summary>
public static class Almanac
{
    public const int StudyKills = 3;
    public const int MasterKills = 10;
    public const string MonsterCategory = "Bestiary";

    /// <summary>Every entry: three per monster (seen, studied, mastered), then almanac.json.</summary>
    public static IReadOnlyList<AlmanacEntryDef> Entries(ContentPack content)
    {
        var list = new List<AlmanacEntryDef>();
        foreach (var m in content.Monsters.Values.OrderBy(m => m.Hp).ThenBy(m => m.Id, StringComparer.Ordinal))
        {
            list.Add(new AlmanacEntryDef
            {
                Id = $"monster:{m.Id}:seen", Category = MonsterCategory, Title = m.Name, Key = "seen:" + m.Id, Count = 1, Reward = 3,
                Hint = "Something lurks in the mines. Send a golem to find it.", Text = m.Lore,
            });
            list.Add(new AlmanacEntryDef
            {
                Id = $"monster:{m.Id}:studied", Category = MonsterCategory, Title = $"{m.Name}: studied", Key = "killed:" + m.Id, Count = StudyKills, Reward = 10,
                Hint = $"Defeat {StudyKills} to learn its weakness.", Text = m.Weakness,
            });
            list.Add(new AlmanacEntryDef
            {
                Id = $"monster:{m.Id}:mastered", Category = MonsterCategory, Title = $"{m.Name}: mastered", Key = "killed:" + m.Id, Count = MasterKills, Reward = 25,
                Hint = $"Defeat {MasterKills} to read its whole script.", Text = $"You can read the {m.Name}'s script now: every line it runs.",
            });
        }
        list.AddRange(content.AlmanacEntries);
        return list;
    }

    public static MonsterKnowledge Knowledge(Profile profile, string monsterId)
    {
        var kills = profile.Found.GetValueOrDefault("killed:" + monsterId);
        if (kills >= MasterKills) return MonsterKnowledge.Mastered;
        if (kills >= StudyKills) return MonsterKnowledge.Studied;
        return profile.Found.GetValueOrDefault("seen:" + monsterId) > 0 || kills > 0 ? MonsterKnowledge.Seen : MonsterKnowledge.Unknown;
    }

    public static bool IsFound(Profile profile, AlmanacEntryDef entry) => profile.Found.GetValueOrDefault(entry.Key) >= entry.Count;

    /// <summary>How far along an entry is, 0 to its count.</summary>
    public static int Progress(Profile profile, AlmanacEntryDef entry) => Math.Min(entry.Count, profile.Found.GetValueOrDefault(entry.Key));
}
