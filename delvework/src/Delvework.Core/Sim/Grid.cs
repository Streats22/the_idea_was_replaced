namespace Delvework.Core.Sim;

/// <summary>The static layout of a floor: which tiles are walls.</summary>
public sealed class Grid
{
    private readonly bool[] _walls;

    public Grid(int width, int height)
    {
        Width = width;
        Height = height;
        _walls = new bool[width * height];
        Array.Fill(_walls, true);
    }

    public int Width { get; }
    public int Height { get; }
    public int Size => Width * Height;

    public int Idx(Pos p) => p.Y * Width + p.X;

    public Pos At(int idx) => new(idx % Width, idx / Width);

    public bool InBounds(Pos p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

    public bool IsWall(Pos p) => !InBounds(p) || _walls[p.Y * Width + p.X];

    public bool IsWall(int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height || _walls[y * Width + x];

    public void SetWall(Pos p, bool wall)
    {
        if (InBounds(p)) _walls[Idx(p)] = wall;
    }

    /// <summary>Walls stay fixed during a run, so clones share nothing mutable except this copy.</summary>
    public Grid Clone()
    {
        var g = new Grid(Width, Height);
        Array.Copy(_walls, g._walls, _walls.Length);
        return g;
    }

    public static Grid FromRows(IReadOnlyList<string> rows)
    {
        var height = rows.Count;
        var width = rows.Max(r => r.Length);
        var g = new Grid(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var c = x < rows[y].Length ? rows[y][x] : '#';
                g.SetWall(new Pos(x, y), c == '#');
            }
        }
        return g;
    }

    public ulong StateHash(ulong h)
    {
        h = Glyph.Fnv.Mix(Glyph.Fnv.Mix(h, (long)Width), (long)Height);
        for (var i = 0; i < _walls.Length; i += 64)
        {
            ulong word = 0;
            for (var b = 0; b < 64 && i + b < _walls.Length; b++)
            {
                if (_walls[i + b]) word |= 1UL << b;
            }
            h = Glyph.Fnv.Mix(h, word);
        }
        return h;
    }
}
