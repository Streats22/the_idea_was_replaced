using Delvework.Core.Content;

namespace Delvework.Core.Sim;

public static class Combat
{
    /// <summary>Damage = attack × type multiplier − armor, floored at 1. Integer math only.</summary>
    public static int Damage(int attack, DamageType type, Actor target)
    {
        var scaled = attack * target.ResistPercent(type) / 100;
        if (target.ResistPercent(type) == 0) return 0;
        return Math.Max(1, scaled - target.Armor);
    }

    public static string StatusWord(StatusKind s) => s switch
    {
        StatusKind.Stun => "stunned",
        StatusKind.Burn => "burning",
        _ => "slowed",
    };
}
