using Delvework.Core.Replay;

namespace Delvework.Game;

public static class GolemStatus
{
    public static void Append(GolemFrame g, List<string> into)
    {
        if (g.StunTicks > 0) into.Add("stunned");
        if (g.BurnTicks > 0) into.Add("burning");
        if (g.SlowTicks > 0) into.Add("slowed");
    }
}
