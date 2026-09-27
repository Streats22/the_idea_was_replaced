namespace Delvework.Core.Sim;

/// <summary>
/// A* and breadth-first search on the 4-connected grid. Ties are broken by (f, h, insertion
/// order) and neighbours are expanded North, East, South, West, so paths are deterministic.
/// Scratch buffers are reused between calls; one instance per world.
/// </summary>
public sealed class Pathfinder
{
    private readonly Grid _grid;
    private readonly int[] _stamp;
    private readonly int[] _g;
    private readonly int[] _parent;
    private readonly bool[] _closed;
    private readonly PriorityQueue<int, (int F, int H, int Seq)> _open = new();
    private readonly Queue<int> _queue = new();
    private int _gen;

    public Pathfinder(Grid grid)
    {
        _grid = grid;
        _stamp = new int[grid.Size];
        _g = new int[grid.Size];
        _parent = new int[grid.Size];
        _closed = new bool[grid.Size];
    }

    private void Reset()
    {
        _gen++;
        if (_gen == int.MaxValue)
        {
            Array.Clear(_stamp);
            _gen = 1;
        }
        _open.Clear();
        _queue.Clear();
    }

    private bool Seen(int i) => _stamp[i] == _gen;

    private void Visit(int i, int g, int parent)
    {
        _stamp[i] = _gen;
        _g[i] = g;
        _parent[i] = parent;
        _closed[i] = false;
    }

    /// <summary>
    /// Shortest path from <paramref name="from"/> to <paramref name="to"/> (excluding the start).
    /// The goal tile itself need not be passable, so paths toward an enemy end next to it.
    /// Returns null when unreachable.
    /// </summary>
    public List<Pos>? AStar(Pos from, Pos to, Func<Pos, bool> passable, int maxExpanded = 20_000)
    {
        if (!_grid.InBounds(from) || !_grid.InBounds(to)) return null;
        if (from == to) return [];
        Reset();
        var start = _grid.Idx(from);
        var goal = _grid.Idx(to);
        Visit(start, 0, -1);
        var seq = 0;
        _open.Enqueue(start, (from.Manhattan(to), from.Manhattan(to), seq++));
        var expanded = 0;
        while (_open.TryDequeue(out var cur, out _))
        {
            if (_closed[cur]) continue;
            _closed[cur] = true;
            if (cur == goal) return Build(goal);
            if (++expanded > maxExpanded) return null;
            var p = _grid.At(cur);
            foreach (var d in Dirs.All)
            {
                var n = p.Step(d);
                if (!_grid.InBounds(n)) continue;
                var ni = _grid.Idx(n);
                if (ni != goal && !passable(n)) continue;
                var g = _g[cur] + 1;
                if (Seen(ni) && (_closed[ni] || _g[ni] <= g)) continue;
                Visit(ni, g, cur);
                var h = n.Manhattan(to);
                _open.Enqueue(ni, (g + h, h, seq++));
            }
        }
        return null;
    }

    /// <summary>
    /// Breadth-first search to the nearest tile satisfying <paramref name="goal"/>. Goal tiles
    /// need not be passable. Returns the path (excluding the start) or null.
    /// </summary>
    public List<Pos>? Nearest(Pos from, Func<Pos, bool> goal, Func<Pos, bool> passable)
    {
        if (!_grid.InBounds(from)) return null;
        Reset();
        var start = _grid.Idx(from);
        Visit(start, 0, -1);
        _queue.Enqueue(start);
        while (_queue.TryDequeue(out var cur))
        {
            var p = _grid.At(cur);
            foreach (var d in Dirs.All)
            {
                var n = p.Step(d);
                if (!_grid.InBounds(n)) continue;
                var ni = _grid.Idx(n);
                if (Seen(ni)) continue;
                Visit(ni, _g[cur] + 1, cur);
                if (goal(n)) return Build(ni);
                if (!passable(n)) continue;
                _queue.Enqueue(ni);
            }
        }
        return null;
    }

    /// <summary>Distances from <paramref name="from"/> to every reachable tile (-1 if unreachable).</summary>
    public int[] DistanceMap(Pos from, Func<Pos, bool> passable)
    {
        var dist = new int[_grid.Size];
        Array.Fill(dist, -1);
        if (!_grid.InBounds(from)) return dist;
        var q = new Queue<int>();
        var s = _grid.Idx(from);
        dist[s] = 0;
        q.Enqueue(s);
        while (q.TryDequeue(out var cur))
        {
            var p = _grid.At(cur);
            foreach (var d in Dirs.All)
            {
                var n = p.Step(d);
                if (!_grid.InBounds(n) || !passable(n)) continue;
                var ni = _grid.Idx(n);
                if (dist[ni] >= 0) continue;
                dist[ni] = dist[cur] + 1;
                q.Enqueue(ni);
            }
        }
        return dist;
    }

    private List<Pos> Build(int end)
    {
        var path = new List<Pos>();
        for (var i = end; _parent[i] >= 0; i = _parent[i]) path.Add(_grid.At(i));
        path.Reverse();
        return path;
    }
}
