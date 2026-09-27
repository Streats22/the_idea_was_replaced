using System.Text;
using Delvework.Core.Sim;

namespace Delvework.Core.Dungeon;

public readonly record struct Room(int X, int Y, int W, int H, string Shape)
{
    public Pos Center => new(X + W / 2, Y + H / 2);

    public bool Contains(Pos p) => p.X >= X && p.Y >= Y && p.X < X + W && p.Y < Y + H;

    public bool Overlaps(Room o, int margin) =>
        X - margin < o.X + o.W && o.X - margin < X + W && Y - margin < o.Y + o.H && o.Y - margin < Y + H;
}

public sealed record Spawn(Pos Pos, string Id);

public sealed record ChestSpawn(Pos Pos, int Gold);

/// <summary>A vein in a wall tile; <see cref="Kind"/> indexes <see cref="Content.Resources.Mined"/>.</summary>
public sealed record VeinSpawn(Pos Pos, int Kind, int Amount);

/// <summary>A generated (or handmade) floor: walls plus where everything starts.</summary>
public sealed class FloorLayout
{
    public required Grid Grid { get; init; }
    public required Pos Start { get; init; }
    public required Pos Stairs { get; init; }
    public List<Room> Rooms { get; init; } = [];
    public List<Spawn> Monsters { get; init; } = [];
    public List<Spawn> Traps { get; init; } = [];
    public List<ChestSpawn> Chests { get; init; } = [];
    public List<VeinSpawn> Veins { get; init; } = [];

    /// <summary>Map characters for veins, in the order of <see cref="Content.Resources.Mined"/>: stone, ore, timber, crystal.</summary>
    public const string VeinChars = "%&|*";

    /// <summary>
    /// Parse an ASCII map: <c>#</c> wall, <c>S</c> start, <c>&gt;</c> stairs, <c>C</c> chest,
    /// <c>^</c> trap, <c>%</c> <c>&amp;</c> <c>|</c> <c>*</c> stone, ore, timber and crystal veins (walls), and letters
    /// from <paramref name="monsterKeys"/> for monsters.
    /// </summary>
    public static FloorLayout FromRows(IReadOnlyList<string> rows, IReadOnlyDictionary<char, string> monsterKeys, string trapId = "spikes", int chestGold = 10, int veinAmount = 3)
    {
        var grid = Grid.FromRows(rows);
        Pos? start = null, stairs = null;
        var monsters = new List<Spawn>();
        var traps = new List<Spawn>();
        var chests = new List<ChestSpawn>();
        var veins = new List<VeinSpawn>();
        for (var y = 0; y < rows.Count; y++)
        {
            for (var x = 0; x < rows[y].Length; x++)
            {
                var p = new Pos(x, y);
                var c = rows[y][x];
                switch (c)
                {
                    case 'S': start ??= p; break;
                    case '>': stairs = p; break;
                    case 'C': chests.Add(new ChestSpawn(p, chestGold)); break;
                    case '^': traps.Add(new Spawn(p, trapId)); break;
                    case var v when VeinChars.Contains(v, StringComparison.Ordinal):
                        grid.SetWall(p, true);
                        veins.Add(new VeinSpawn(p, VeinChars.IndexOf(c, StringComparison.Ordinal), veinAmount));
                        break;
                    default:
                        if (monsterKeys.TryGetValue(c, out var id)) monsters.Add(new Spawn(p, id));
                        break;
                }
            }
        }
        return new FloorLayout
        {
            Grid = grid,
            Start = start ?? new Pos(1, 1),
            Stairs = stairs ?? new Pos(grid.Width - 2, grid.Height - 2),
            Monsters = monsters,
            Traps = traps,
            Chests = chests,
            Veins = veins,
        };
    }

    /// <summary>Render as ASCII (the inverse of <see cref="FromRows"/>, monsters as their id's first letter).</summary>
    public string Render()
    {
        var chars = new char[Grid.Height][];
        for (var y = 0; y < Grid.Height; y++)
        {
            chars[y] = new char[Grid.Width];
            for (var x = 0; x < Grid.Width; x++) chars[y][x] = Grid.IsWall(x, y) ? '#' : '.';
        }
        void Put(Pos p, char c) => chars[p.Y][p.X] = c;
        foreach (var t in Traps) Put(t.Pos, '^');
        foreach (var c in Chests) Put(c.Pos, 'C');
        foreach (var v in Veins) Put(v.Pos, VeinChars[v.Kind]);
        foreach (var m in Monsters) Put(m.Pos, m.Id.Length > 0 ? m.Id[0] : 'm');
        Put(Stairs, '>');
        Put(Start, 'S');
        var sb = new StringBuilder();
        foreach (var row in chars) sb.Append(row).Append('\n');
        return sb.ToString();
    }
}
