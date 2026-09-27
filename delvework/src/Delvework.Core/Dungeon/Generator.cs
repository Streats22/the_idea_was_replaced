using Delvework.Core.Content;
using Delvework.Core.Sim;

namespace Delvework.Core.Dungeon;

/// <summary>
/// Room-and-corridor floors from a stratum table and a seed. Every floor passes validation:
/// the stairs are reachable, and at least one route from the start avoids every trap.
/// </summary>
public static class Generator
{
    public const int MaxAttempts = 50;

    public static FloorLayout Generate(StratumDef stratum, ulong seed)
    {
        var rng = new Rng(Rng.Derive(seed, "dungeon:" + stratum.Id));
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var layout = TryGenerate(stratum, rng);
            if (layout is not null && Validate(layout).Count == 0) return AddVeins(layout, stratum, seed);
        }
        throw new InvalidOperationException($"Could not generate a valid floor for stratum '{stratum.Id}' with seed {seed}");
    }

    /// <summary>
    /// Put veins into room walls. They use their own random stream, so adding veins to a stratum
    /// never changes its rooms, monsters, chests or traps.
    /// </summary>
    private static FloorLayout AddVeins(FloorLayout layout, StratumDef s, ulong seed)
    {
        if (s.Veins.Table.Count == 0 || s.Veins.Count[1] <= 0) return layout;
        var rng = new Rng(Rng.Derive(seed, "veins:" + s.Id));
        var g = layout.Grid;
        var candidates = new List<Pos>();
        for (var y = 1; y < g.Height - 1; y++)
        {
            for (var x = 1; x < g.Width - 1; x++)
            {
                var p = new Pos(x, y);
                if (!g.IsWall(p)) continue;
                if (Dirs.All.Any(d => !g.IsWall(p.Step(d)) && layout.Rooms.Exists(r => r.Contains(p.Step(d))))) candidates.Add(p);
            }
        }
        var weights = s.Veins.Table.Select(e => e.Weight).ToList();
        var veins = new List<VeinSpawn>();
        var count = rng.Range(s.Veins.Count[0], s.Veins.Count[1]);
        for (var i = 0; i < count && candidates.Count > 0; i++)
        {
            var at = rng.Next(candidates.Count);
            var p = candidates[at];
            candidates.RemoveAt(at);
            candidates.RemoveAll(q => q.Manhattan(p) <= 1);
            var kind = Content.Resources.Mined.ToList().IndexOf(s.Veins.Table[rng.Weighted(weights)].Id);
            veins.Add(new VeinSpawn(p, kind, rng.Range(s.Veins.Amount[0], s.Veins.Amount[1])));
        }
        return new FloorLayout
        {
            Grid = g,
            Start = layout.Start,
            Stairs = layout.Stairs,
            Rooms = layout.Rooms,
            Monsters = layout.Monsters,
            Traps = layout.Traps,
            Chests = layout.Chests,
            Veins = veins,
        };
    }

    private static FloorLayout? TryGenerate(StratumDef s, Rng rng)
    {
        var grid = new Grid(s.Width, s.Height);
        var rooms = new List<Room>();
        var target = rng.Range(s.Rooms[0], s.Rooms[1]);
        var weights = s.Templates.Count > 0 ? s.Templates.Select(t => t.Weight).ToList() : [1];
        for (var tries = 0; tries < target * 30 && rooms.Count < target; tries++)
        {
            var w = rng.Range(s.RoomSize[0], s.RoomSize[1]);
            var h = rng.Range(s.RoomSize[0], Math.Max(s.RoomSize[0], s.RoomSize[1] - 1));
            if (w + 2 >= s.Width || h + 2 >= s.Height) continue;
            var x = rng.Range(1, s.Width - w - 1);
            var y = rng.Range(1, s.Height - h - 1);
            var shape = s.Templates.Count > 0 ? s.Templates[rng.Weighted(weights)].Shape : "rect";
            var room = new Room(x, y, w, h, shape);
            if (rooms.Exists(r => r.Overlaps(room, 2))) continue;
            rooms.Add(room);
        }
        if (rooms.Count < 2) return null;

        foreach (var r in rooms) Carve(grid, r);

        // Connect in left-to-right order so corridors rarely cross, then add a few loops.
        var ordered = rooms.OrderBy(r => r.Center.X).ThenBy(r => r.Center.Y).ToList();
        for (var i = 1; i < ordered.Count; i++) Corridor(grid, ordered[i - 1].Center, ordered[i].Center, rng);
        for (var i = 0; i < s.ExtraCorridors; i++)
        {
            var a = rng.Pick(ordered);
            var b = rng.Pick(ordered);
            if (a != b) Corridor(grid, a.Center, b.Center, rng);
        }

        var startRoom = ordered[0];
        var start = FirstFloor(grid, startRoom);
        var paths = new Pathfinder(grid);
        var dist = paths.DistanceMap(start, p => !grid.IsWall(p));
        var stairsRoom = rooms.Where(r => r != startRoom).OrderByDescending(r => dist[grid.Idx(FirstFloor(grid, r))]).First();
        var stairs = FirstFloor(grid, stairsRoom, fromCenter: true);

        var occupied = new HashSet<Pos> { start, stairs };
        foreach (var d in Dirs.All) occupied.Add(start.Step(d));
        var otherRooms = rooms.Where(r => r != startRoom).ToList();

        var chests = new List<ChestSpawn>();
        var chestCount = rng.Range(s.Chests.Count[0], s.Chests.Count[1]);
        for (var i = 0; i < chestCount; i++)
        {
            if (RandomFloorIn(grid, rng.Pick(otherRooms), rng, occupied) is Pos p)
            {
                chests.Add(new ChestSpawn(p, rng.Range(s.Chests.Gold[0], s.Chests.Gold[1])));
                occupied.Add(p);
            }
        }

        var monsters = new List<Spawn>();
        var monsterCount = rng.Range(s.Monsters.Count[0], s.Monsters.Count[1]);
        var monsterWeights = s.Monsters.Table.Select(e => e.Weight).ToList();
        for (var i = 0; i < monsterCount && s.Monsters.Table.Count > 0; i++)
        {
            var id = s.Monsters.Table[rng.Weighted(monsterWeights)].Id;
            if (RandomFloorIn(grid, rng.Pick(otherRooms), rng, occupied) is Pos p)
            {
                monsters.Add(new Spawn(p, id));
                occupied.Add(p);
            }
        }

        var traps = new List<Spawn>();
        var trapCount = rng.Range(s.Traps.Count[0], s.Traps.Count[1]);
        var trapWeights = s.Traps.Table.Select(e => e.Weight).ToList();
        for (var i = 0; i < trapCount && s.Traps.Table.Count > 0; i++)
        {
            var id = s.Traps.Table[rng.Weighted(trapWeights)].Id;
            if (RandomFloorIn(grid, rng.Pick(otherRooms), rng, occupied) is not Pos p) continue;
            // Never allow a trap to cut off the only trap-free route to the stairs.
            if (!SafeRouteExists(grid, start, stairs, traps.Select(t => t.Pos).Append(p).ToHashSet())) continue;
            traps.Add(new Spawn(p, id));
            occupied.Add(p);
        }

        return new FloorLayout
        {
            Grid = grid,
            Start = start,
            Stairs = stairs,
            Rooms = rooms,
            Monsters = monsters,
            Traps = traps,
            Chests = chests,
        };
    }

    private static void Carve(Grid grid, Room r)
    {
        for (var y = r.Y; y < r.Y + r.H; y++)
        {
            for (var x = r.X; x < r.X + r.W; x++) grid.SetWall(new Pos(x, y), false);
        }
        switch (r.Shape)
        {
            case "pillars" when r.W >= 5 && r.H >= 5:
                for (var y = r.Y + 1; y < r.Y + r.H - 1; y += 2)
                {
                    for (var x = r.X + 1; x < r.X + r.W - 1; x += 2)
                    {
                        if (new Pos(x, y) != r.Center) grid.SetWall(new Pos(x, y), true);
                    }
                }
                break;
            case "cross" when r.W >= 5 && r.H >= 5:
                var cw = Math.Max(1, r.W / 3);
                var ch = Math.Max(1, r.H / 3);
                foreach (var (cx, cy) in new[] { (r.X, r.Y), (r.X + r.W - cw, r.Y), (r.X, r.Y + r.H - ch), (r.X + r.W - cw, r.Y + r.H - ch) })
                {
                    for (var y = cy; y < cy + ch; y++)
                    {
                        for (var x = cx; x < cx + cw; x++) grid.SetWall(new Pos(x, y), true);
                    }
                }
                break;
        }
    }

    private static void Corridor(Grid grid, Pos a, Pos b, Rng rng)
    {
        var horizontalFirst = rng.Next(2) == 0;
        var corner = horizontalFirst ? new Pos(b.X, a.Y) : new Pos(a.X, b.Y);
        Line(grid, a, corner);
        Line(grid, corner, b);
    }

    private static void Line(Grid grid, Pos a, Pos b)
    {
        var p = a;
        grid.SetWall(p, false);
        while (p != b)
        {
            p = new Pos(p.X + Math.Sign(b.X - p.X), p.Y + Math.Sign(b.Y - p.Y));
            grid.SetWall(p, false);
        }
    }

    private static Pos FirstFloor(Grid grid, Room r, bool fromCenter = true)
    {
        if (fromCenter && !grid.IsWall(r.Center)) return r.Center;
        for (var y = r.Y; y < r.Y + r.H; y++)
        {
            for (var x = r.X; x < r.X + r.W; x++)
            {
                if (!grid.IsWall(x, y)) return new Pos(x, y);
            }
        }
        return r.Center;
    }

    private static Pos? RandomFloorIn(Grid grid, Room r, Rng rng, HashSet<Pos> occupied)
    {
        for (var i = 0; i < 20; i++)
        {
            var p = new Pos(rng.Range(r.X, r.X + r.W - 1), rng.Range(r.Y, r.Y + r.H - 1));
            if (!grid.IsWall(p) && !occupied.Contains(p)) return p;
        }
        return null;
    }

    private static bool SafeRouteExists(Grid grid, Pos start, Pos stairs, HashSet<Pos> traps)
    {
        var dist = new Pathfinder(grid).DistanceMap(start, p => !grid.IsWall(p) && !traps.Contains(p));
        return dist[grid.Idx(stairs)] >= 0;
    }

    /// <summary>Checks every generated floor must pass. Returns human-readable problems.</summary>
    public static List<string> Validate(FloorLayout layout)
    {
        var problems = new List<string>();
        var g = layout.Grid;
        if (g.IsWall(layout.Start)) problems.Add("Start is inside a wall");
        if (g.IsWall(layout.Stairs)) problems.Add("Stairs are inside a wall");
        if (layout.Start == layout.Stairs) problems.Add("Start and stairs coincide");
        var dist = new Pathfinder(g).DistanceMap(layout.Start, p => !g.IsWall(p));
        if (!g.IsWall(layout.Stairs) && dist[g.Idx(layout.Stairs)] < 0) problems.Add("Stairs are unreachable");
        var traps = layout.Traps.Select(t => t.Pos).ToHashSet();
        if (!SafeRouteExists(g, layout.Start, layout.Stairs, traps)) problems.Add("No trap-free route to the stairs");
        var spots = new HashSet<Pos>();
        foreach (var v in layout.Veins)
        {
            if (!g.IsWall(v.Pos)) problems.Add($"Vein at {v.Pos} is not in a wall");
            else if (!Dirs.All.Any(d => !g.IsWall(v.Pos.Step(d)) && dist[g.Idx(v.Pos.Step(d))] >= 0)) problems.Add($"Vein at {v.Pos} can't be reached from any floor");
        }
        foreach (var p in layout.Monsters.Select(m => m.Pos).Concat(layout.Chests.Select(c => c.Pos)).Concat(traps))
        {
            if (g.IsWall(p)) problems.Add($"Something spawns inside a wall at {p}");
            else if (dist[g.Idx(p)] < 0) problems.Add($"Unreachable spawn at {p}");
            if (!spots.Add(p)) problems.Add($"Two things spawn at {p}");
        }
        for (var x = 0; x < g.Width; x++)
        {
            if (!g.IsWall(x, 0) || !g.IsWall(x, g.Height - 1)) problems.Add("Floor touches the map edge");
        }
        for (var y = 0; y < g.Height; y++)
        {
            if (!g.IsWall(0, y) || !g.IsWall(g.Width - 1, y)) problems.Add("Floor touches the map edge");
        }
        return problems.Distinct().ToList();
    }
}
