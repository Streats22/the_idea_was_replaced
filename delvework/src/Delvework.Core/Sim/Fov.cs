namespace Delvework.Core.Sim;

/// <summary>
/// Recursive shadowcasting over 8 octants with a circular radius. Walls are visible (so rooms
/// show their outline) but block sight. Integer-only slope math keeps it deterministic.
/// </summary>
public static class Fov
{
    private static readonly int[,] Octants =
    {
        { 1, 0, 0, 1 }, { 0, 1, 1, 0 }, { 0, -1, 1, 0 }, { -1, 0, 0, 1 },
        { -1, 0, 0, -1 }, { 0, -1, -1, 0 }, { 0, 1, -1, 0 }, { 1, 0, 0, -1 },
    };

    /// <summary>Mark every tile visible from <paramref name="origin"/> within <paramref name="radius"/>.</summary>
    public static void Compute(Grid grid, Pos origin, int radius, Action<int> markVisible)
    {
        if (!grid.InBounds(origin)) return;
        markVisible(grid.Idx(origin));
        for (var o = 0; o < 8; o++)
        {
            Cast(grid, origin, radius, 1, 1, 1, 0, 1, Octants[o, 0], Octants[o, 1], Octants[o, 2], Octants[o, 3], markVisible);
        }
    }

    /// <summary>
    /// Scan one octant row by row. Slopes are fractions (num/den) compared by cross-multiplying,
    /// start slope <paramref name="sn"/>/<paramref name="sd"/> down to end slope <paramref name="en"/>/<paramref name="ed"/>.
    /// </summary>
    private static void Cast(Grid grid, Pos origin, int radius, int row, int sn, int sd, int en, int ed,
        int xx, int xy, int yx, int yy, Action<int> mark)
    {
        if (sn * ed < en * sd) return;
        var r2 = radius * radius + radius;
        for (var j = row; j <= radius; j++)
        {
            var blocked = false;
            var newSn = 0;
            var newSd = 1;
            var dy = -j;
            for (var dx = -j; dx <= 0; dx++)
            {
                // Slopes of the cell's left and right edges, (dx -/+ 0.5) / (dy +/- 0.5), doubled
                // and normalised to positive denominators.
                var ln = 1 - 2 * dx;
                var ld = 2 * j - 1;
                var rn = -2 * dx - 1;
                var rd = 2 * j + 1;
                if (sn * rd < rn * sd) continue;
                if (en * ld > ln * ed) break;

                var x = origin.X + dx * xx + dy * xy;
                var y = origin.Y + dx * yx + dy * yy;
                var inside = x >= 0 && y >= 0 && x < grid.Width && y < grid.Height;
                if (inside && dx * dx + dy * dy <= r2) mark(y * grid.Width + x);

                var wall = grid.IsWall(x, y);
                if (blocked)
                {
                    if (wall)
                    {
                        newSn = rn;
                        newSd = rd;
                        continue;
                    }
                    blocked = false;
                    sn = newSn;
                    sd = newSd;
                }
                else if (wall && j < radius)
                {
                    blocked = true;
                    Cast(grid, origin, radius, j + 1, sn, sd, ln, ld, xx, xy, yx, yy, mark);
                    newSn = rn;
                    newSd = rd;
                }
            }
            if (blocked) break;
        }
    }

    /// <summary>Bresenham line of sight used for single checks (monster sight, ranged queries).</summary>
    public static bool LineOfSight(Grid grid, Pos a, Pos b)
    {
        int x = a.X, y = a.Y;
        var dx = Math.Abs(b.X - x);
        var dy = -Math.Abs(b.Y - y);
        var sx = x < b.X ? 1 : -1;
        var sy = y < b.Y ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            if (x == b.X && y == b.Y) return true;
            if (!(x == a.X && y == a.Y) && grid.IsWall(x, y)) return false;
            var e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x += sx;
            }
            if (e2 <= dx)
            {
                err += dx;
                y += sy;
            }
        }
    }

    public static bool CanSee(Grid grid, Pos from, int radius, Pos to) =>
        from.DistSq(to) <= radius * radius + radius && LineOfSight(grid, from, to);
}
