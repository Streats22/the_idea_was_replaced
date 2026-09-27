namespace Delvework.Core.Sim;

/// <summary>xoshiro256** seeded with SplitMix64. Pure integer math, identical on every platform.</summary>
public sealed class Rng
{
    private ulong _s0, _s1, _s2, _s3;

    public Rng(ulong seed)
    {
        var sm = seed;
        _s0 = SplitMix(ref sm);
        _s1 = SplitMix(ref sm);
        _s2 = SplitMix(ref sm);
        _s3 = SplitMix(ref sm);
    }

    private Rng(Rng other)
    {
        _s0 = other._s0;
        _s1 = other._s1;
        _s2 = other._s2;
        _s3 = other._s3;
    }

    public static ulong SplitMix(ref ulong state)
    {
        var z = state += 0x9E3779B97F4A7C15;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
        return z ^ (z >> 31);
    }

    /// <summary>Derive an independent seed for a named stream, so adding draws to one stream never shifts another.</summary>
    public static ulong Derive(ulong seed, string stream)
    {
        var h = Glyph.Fnv.Mix(seed ^ 0xD1B54A32D192ED03, stream);
        return SplitMix(ref h);
    }

    public ulong NextULong()
    {
        var result = RotL(_s1 * 5, 7) * 9;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotL(_s3, 45);
        return result;
    }

    private static ulong RotL(ulong x, int k) => (x << k) | (x >> (64 - k));

    /// <summary>Uniform integer in [0, n) without modulo bias.</summary>
    public int Next(int n)
    {
        if (n <= 1) return 0;
        var bound = (ulong)n;
        var threshold = (0 - bound) % bound;
        while (true)
        {
            var r = NextULong();
            if (r >= threshold) return (int)(r % bound);
        }
    }

    /// <summary>Uniform integer in [min, max] inclusive.</summary>
    public int Range(int min, int max) => max <= min ? min : min + Next(max - min + 1);

    /// <summary>True with probability permille / 1000.</summary>
    public bool Chance(int permille) => permille > 0 && (permille >= 1000 || Next(1000) < permille);

    public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];

    public int Weighted(IReadOnlyList<int> weights)
    {
        var total = 0;
        foreach (var w in weights) total += Math.Max(0, w);
        if (total == 0) return 0;
        var roll = Next(total);
        for (var i = 0; i < weights.Count; i++)
        {
            roll -= Math.Max(0, weights[i]);
            if (roll < 0) return i;
        }
        return weights.Count - 1;
    }

    public Rng Clone() => new(this);

    public ulong StateHash(ulong h) => Glyph.Fnv.Mix(Glyph.Fnv.Mix(Glyph.Fnv.Mix(Glyph.Fnv.Mix(h, _s0), _s1), _s2), _s3);
}

/// <summary>The independent random streams of one run, all derived from the run seed.</summary>
public sealed class RngStreams
{
    public RngStreams(ulong seed)
    {
        Seed = seed;
        Dungeon = new Rng(Rng.Derive(seed, "dungeon"));
        Combat = new Rng(Rng.Derive(seed, "combat"));
        Loot = new Rng(Rng.Derive(seed, "loot"));
        Monsters = new Rng(Rng.Derive(seed, "monsters"));
        Essence = new Rng(Rng.Derive(seed, "essence"));
    }

    private RngStreams(RngStreams other)
    {
        Seed = other.Seed;
        Dungeon = other.Dungeon.Clone();
        Combat = other.Combat.Clone();
        Loot = other.Loot.Clone();
        Monsters = other.Monsters.Clone();
        Essence = other.Essence.Clone();
    }

    public ulong Seed { get; }
    public Rng Dungeon { get; }
    public Rng Combat { get; }
    public Rng Loot { get; }
    public Rng Monsters { get; }
    /// <summary>Monster essence drops. Separate so adding essence didn't change any gold roll.</summary>
    public Rng Essence { get; }

    public RngStreams Clone() => new(this);

    public ulong StateHash(ulong h) => Essence.StateHash(Monsters.StateHash(Loot.StateHash(Combat.StateHash(Dungeon.StateHash(h)))));
}
