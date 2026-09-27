namespace Delvework.Core.Sim;

public enum Dir
{
    North,
    East,
    South,
    West,
}

public readonly record struct Pos(int X, int Y)
{
    public Pos Step(Dir d) => d switch
    {
        Dir.North => new Pos(X, Y - 1),
        Dir.East => new Pos(X + 1, Y),
        Dir.South => new Pos(X, Y + 1),
        _ => new Pos(X - 1, Y),
    };

    public int Manhattan(Pos o) => Math.Abs(X - o.X) + Math.Abs(Y - o.Y);

    public int DistSq(Pos o) => (X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y);

    public override string ToString() => $"({X},{Y})";
}

public static class Dirs
{
    public static readonly Dir[] All = [Dir.North, Dir.East, Dir.South, Dir.West];

    public static Dir? Between(Pos from, Pos to)
    {
        foreach (var d in All)
        {
            if (from.Step(d) == to) return d;
        }
        return null;
    }
}
