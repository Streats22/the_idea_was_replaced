using Delvework.Core.Content;
using Delvework.Core.Glyph;
using Delvework.Core.Village;

namespace Delvework.Core.Progress;

public enum Medal
{
    None,
    Bronze,
    Silver,
    Gold,
}

/// <summary>
/// A scored commission run. <see cref="Ticks"/> is when the goal was first met (0 if never);
/// <see cref="Lines"/> counts lines that aren't blank or comments.
/// </summary>
public sealed record CommissionScore(CommissionDef Def, WorkshopResult Result, bool Passed, int Made, int Ticks, int Lines, Medal TicksMedal, Medal LinesMedal)
{
    public string Summary => Passed
        ? $"{Def.Title}: {Made} {Resources.Name(Def.Goal.Resource)} in {Ticks} ticks with {Lines} lines ({TicksMedal} for speed, {LinesMedal} for size)."
        : Result.Error is { } e && Result.Frames.Count <= 1
            ? $"{Def.Title}: the script has an error on line {e.Line}: {e.Message}"
            : $"{Def.Title}: made {Made} of {Def.Goal.Amount} {Resources.Name(Def.Goal.Resource)}.";
}

/// <summary>A player's best commission result, kept per metric.</summary>
public sealed class CommissionBest
{
    public int Ticks { get; set; }
    public int Lines { get; set; }
    public Medal TicksMedal { get; set; }
    public Medal LinesMedal { get; set; }
}

public static class Commissions
{
    public static Medal MedalFor(int value, int[] par) =>
        par.Length < 2 ? Medal.Bronze : value <= par[0] ? Medal.Gold : value <= par[1] ? Medal.Silver : Medal.Bronze;

    /// <summary>Run a commission's workshop on its own fixed stores and score the script.</summary>
    public static CommissionScore Score(ContentPack content, CommissionDef def, string source, int tier = Tiers.Max)
    {
        var workshop = content.Workshops.First(w => w.Id == def.Workshop);
        var result = Workshops.Run(workshop, source, def.Stock, tier);
        var made = result.Made.GetValueOrDefault(def.Goal.Resource);
        var reached = result.Frames.FirstOrDefault(f => f.State.GetValueOrDefault(def.Goal.Resource) >= def.Goal.Amount);
        var passed = reached is not null && result.Error is null;
        var ticks = reached?.Tick ?? 0;
        var lines = Challenge.CountLines(source);
        return passed
            ? new CommissionScore(def, result, true, made, ticks, lines, MedalFor(ticks, def.Par.Ticks), MedalFor(lines, def.Par.Lines))
            : new CommissionScore(def, result, false, made, 0, lines, Medal.None, Medal.None);
    }

    public static string Slot(CommissionDef def) => $"commission/{def.Id}";
}
