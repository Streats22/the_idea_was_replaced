using Delvework.Core.Content;
using Delvework.Core.Glyph;
using Delvework.Core.Sim;

namespace Delvework.Core.Village;

/// <summary>
/// The Farm: a drone flies over a small field (it wraps around at the edges) planting, watering
/// and harvesting. Wheat ripens fast. Pumpkins ripen slowly and sometimes rot, but a connected
/// patch of n ripe pumpkins is harvested at once for n × min(n, 3) pumpkins.
/// </summary>
public static class Farm
{
    public const string Id = "farm";
    public const int Width = 6;
    public const int Height = 4;
    public const int WheatRipe = 8;
    public const int PumpkinRipe = 16;
    public const int WaterTicks = 15;
    public const int PatchCap = 3;
    /// <summary>One in this many pumpkins rots when it ripens.</summary>
    public const int RotOneIn = 5;

    public static readonly EnumType Crops = new("Crop", ["Wheat", "Pumpkin"]);

    public enum Crop
    {
        None,
        Wheat,
        Pumpkin,
        Rotten,
    }

    /// <summary>One tile of a frame: what grows there, how far it grew and how long it stays wet.</summary>
    public readonly record struct Plot(Crop Crop, int Growth, int Water)
    {
        public bool Ripe => Crop switch
        {
            Crop.Wheat => Growth >= WheatRipe,
            Crop.Pumpkin => Growth >= PumpkinRipe,
            _ => false,
        };

        public int Encode() => (int)Crop | Math.Min(Growth, 255) << 2 | Math.Min(Water, 63) << 10;
    }

    public static Plot Decode(int code) => new((Crop)(code & 3), (code >> 2) & 255, (code >> 10) & 63);

    public static int Patch(int n) => n * Math.Min(n, PatchCap);

    private sealed class State : WorkshopState
    {
        public readonly Plot[] Field = new Plot[Width * Height];
        public readonly int[] Planted = new int[Width * Height];

        public State(IReadOnlyDictionary<string, int> stock) : base(stock, [])
        {
            this["x"] = 0;
            this["y"] = 0;
            this[Resources.Wheat] = 0;
            this[Resources.Pumpkin] = 0;
            this["rotten"] = 0;
            this["lost"] = 0;
        }

        public int Here => this["y"] * Width + this["x"];

        public override void EndTick()
        {
            for (var i = 0; i < Field.Length; i++)
            {
                var p = Field[i];
                if (p.Crop is Crop.None or Crop.Rotten) continue;
                var wasRipe = p.Ripe;
                var growth = p.Ripe ? p.Growth : p.Growth + (p.Water > 0 ? 2 : 1);
                var next = p with { Growth = growth, Water = Math.Max(0, p.Water - 1) };
                if (!wasRipe && next.Ripe && p.Crop == Crop.Pumpkin && Rots(i))
                {
                    next = new Plot(Crop.Rotten, 0, next.Water);
                    this["rotten"]++;
                    Discover("farm:rotten");
                }
                Field[i] = next;
            }
        }

        private bool Rots(int i)
        {
            var h = Fnv.Mix(Fnv.Mix(Fnv.Offset, (long)i), (long)Planted[i]);
            return h % RotOneIn == 0;
        }

        public override int[] Tiles() => [.. Field.Select(p => p.Encode())];
    }

    private static State S(WorkshopState s) => (State)s;

    private static Dir DirArg(in BuiltinCall c, string fn)
    {
        var v = c.Arg(0);
        if (v.Kind == ValueKind.Enum && v.AsEnum.Type == GolemApi.Directions) return (Dir)v.AsEnum.Ordinal;
        throw c.Error($"{fn}() expects North, East, South or West, got {v.Repr()}");
    }

    public static readonly WorkshopKind Kind = new()
    {
        Id = Id,
        Inputs = [],
        Outputs = [Resources.Wheat, Resources.Pumpkin],
        Create = stock => new State(stock),
        Environment = Workshops.Environment(
        [
            Workshops.Action("move", (WorkshopState s, in BuiltinCall c) =>
            {
                var d = DirArg(c, "move");
                var (dx, dy) = d switch { Dir.North => (0, -1), Dir.South => (0, 1), Dir.East => (1, 0), _ => (-1, 0) };
                s["x"] = (s["x"] + dx + Width) % Width;
                s["y"] = (s["y"] + dy + Height) % Height;
                return true;
            }, $"Fly one tile. The field is {Width} wide and {Height} tall and wraps around: fly off the east edge and you come back on the west.", args: 1, sig: "move(North | East | South | West)"),
            Workshops.Action("plant", (WorkshopState s, in BuiltinCall c) =>
            {
                var st = S(s);
                var v = c.Arg(0);
                if (v.Kind != ValueKind.Enum || v.AsEnum.Type != Crops) throw c.Error($"plant() expects Wheat or Pumpkin, got {v.Repr()}");
                if (st.Field[st.Here].Crop != Crop.None)
                {
                    s.Note("Something already grows here: harvest() it first");
                    return false;
                }
                var crop = v.AsEnum.Ordinal == 0 ? Crop.Wheat : Crop.Pumpkin;
                st.Field[st.Here] = new Plot(crop, 0, st.Field[st.Here].Water);
                st.Planted[st.Here] = s.Tick;
                s.Note($"Planted {crop.ToString().ToLowerInvariant()}");
                return true;
            }, $"Sow Wheat (ripe after {WheatRipe} ticks) or Pumpkin ({PumpkinRipe} ticks) on this tile. Returns False if something grows here already.", args: 1, sig: "plant(Wheat | Pumpkin)"),
            Workshops.Action("water", (WorkshopState s, in BuiltinCall _) =>
            {
                var st = S(s);
                st.Field[st.Here] = st.Field[st.Here] with { Water = WaterTicks };
                s.Note("Watered");
                return true;
            }, $"Water this tile: whatever grows here grows twice as fast for {WaterTicks} ticks."),
            Workshops.Action("harvest", (WorkshopState s, in BuiltinCall _) =>
            {
                var st = S(s);
                var p = st.Field[st.Here];
                switch (p.Crop)
                {
                    case Crop.None:
                        s.Note("Nothing to harvest here");
                        return false;
                    case Crop.Rotten:
                        st.Field[st.Here] = new Plot(Crop.None, 0, p.Water);
                        s.Note("Cleared a rotten pumpkin");
                        return false;
                }
                if (!p.Ripe)
                {
                    st.Field[st.Here] = new Plot(Crop.None, 0, p.Water);
                    s["lost"]++;
                    s.Discover("farm:too_early");
                    s.Note($"Harvested too early: the {p.Crop.ToString().ToLowerInvariant()} was lost");
                    return false;
                }
                if (p.Crop == Crop.Wheat)
                {
                    st.Field[st.Here] = new Plot(Crop.None, 0, p.Water);
                    s[Resources.Wheat]++;
                    s.Note("Harvested 1 wheat");
                    return true;
                }
                var patch = PatchAt(st.Field, st.Here);
                foreach (var i in patch) st.Field[i] = new Plot(Crop.None, 0, st.Field[i].Water);
                var yield = Patch(patch.Count);
                s[Resources.Pumpkin] += yield;
                if (patch.Count >= 4) s.Discover("farm:big_patch");
                s.Note(patch.Count == 1 ? "Harvested 1 pumpkin" : $"Harvested a patch of {patch.Count} pumpkins: {yield} pumpkins");
                return true;
            }, $"Harvest this tile. A ripe pumpkin brings in its whole connected patch of ripe pumpkins: n pumpkins give n × min(n, {PatchCap}). Unripe crops are lost; rotten pumpkins are just cleared."),
            Workshops.Query("x", s => s["x"], $"The drone's column, 0 to {Width - 1}."),
            Workshops.Query("y", s => s["y"], $"The drone's row, 0 to {Height - 1}."),
            Workshops.Query("width", _ => Width, "How many columns the field has."),
            Workshops.Query("height", _ => Height, "How many rows the field has."),
            Workshops.Flag("can_harvest", s => S(s).Field[S(s).Here].Ripe, "True if the crop under the drone is ripe."),
            Workshops.Flag("growing", s => S(s).Field[S(s).Here].Crop is not Crop.None, "True if something (even a rotten pumpkin) is on this tile."),
            Workshops.Flag("rotten", s => S(s).Field[S(s).Here].Crop == Crop.Rotten, "True if the pumpkin under the drone has rotted. harvest() clears it."),
            Workshops.Query("wet", s => S(s).Field[S(s).Here].Water, "How many more ticks this tile stays watered."),
            Workshops.Query("wheat", s => s[Resources.Wheat], "Wheat harvested this shift."),
            Workshops.Query("pumpkins", s => s[Resources.Pumpkin], "Pumpkins harvested this shift."),
        ], GolemApi.Directions, Crops),
    };

    /// <summary>Every ripe pumpkin connected to tile <paramref name="start"/> (no wrapping), in index order.</summary>
    private static List<int> PatchAt(Plot[] field, int start)
    {
        var seen = new HashSet<int> { start };
        var queue = new Queue<int>([start]);
        while (queue.TryDequeue(out var i))
        {
            var (x, y) = (i % Width, i / Width);
            foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
                var n = ny * Width + nx;
                if (field[n] is { Crop: Crop.Pumpkin, Ripe: true } && seen.Add(n)) queue.Enqueue(n);
            }
        }
        return [.. seen.Order()];
    }
}
