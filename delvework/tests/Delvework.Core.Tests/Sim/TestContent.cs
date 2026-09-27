using Delvework.Core.Content;
using Delvework.Core.Dungeon;
using Delvework.Core.Sim;

namespace Delvework.Core.Tests.Sim;

/// <summary>A tiny, balance-independent content pack plus helpers for handmade test floors.</summary>
internal static class TestContent
{
    private const string Idle = "while True:\n    idle(10)\n";

    private const string Brute = """
        while True:
            t = adjacent_golem()
            if t:
                attack(t)
            else:
                idle(1)
        """;

    private const string Chaser = """
        while True:
            t = adjacent_golem()
            if t:
                attack(t)
                continue
            g = nearest_golem()
            if g:
                intend(Intent.Chase)
                step_toward(g)
            else:
                idle(1)
        """;

    public static readonly ContentPack Pack = ContentPack.FromFiles(new Dictionary<string, string>
    {
        ["manifest.json"] = """{ "name": "test", "version": "9.9.9" }""",
        ["chassis.json"] = """
            [
              { "id": "tank", "name": "Tank", "hp": 30, "armor": 0, "attack": 3, "sight": 6, "budget": 50, "initiative": 5, "moveTicks": 2, "attackTicks": 4 },
              { "id": "scout", "name": "Scout", "hp": 10, "armor": 0, "attack": 2, "sight": 8, "budget": 50, "initiative": 9, "moveTicks": 2, "attackTicks": 4 }
            ]
            """,
        ["monsters.json"] = """
            [
              { "id": "dummy", "name": "Dummy", "hp": 6, "attack": 0, "sight": 4, "gold": [3, 3], "script": "idle.glyph" },
              { "id": "armored", "name": "Armored", "hp": 20, "armor": 1, "attack": 0, "sight": 4, "resist": { "Fire": 50 }, "gold": [0, 0], "script": "idle.glyph" },
              { "id": "brute", "name": "Brute", "hp": 50, "attack": 5, "sight": 4, "windupTicks": 2, "attackTicks": 4, "gold": [0, 0], "script": "brute.glyph" },
              { "id": "stunner", "name": "Stunner", "hp": 50, "attack": 1, "sight": 4, "windupTicks": 0, "attackTicks": 30, "onHit": { "status": "Stun", "ticks": 8 }, "gold": [0, 0], "script": "brute.glyph" },
              { "id": "burner", "name": "Burner", "hp": 50, "attack": 1, "damage": "Fire", "sight": 4, "windupTicks": 0, "attackTicks": 100, "onHit": { "status": "Burn", "ticks": 20 }, "gold": [0, 0], "script": "brute.glyph" },
              { "id": "chaser", "name": "Chaser", "hp": 8, "attack": 2, "sight": 6, "moveTicks": 2, "windupTicks": 1, "gold": [1, 1], "script": "chaser.glyph" }
            ]
            """,
        ["traps.json"] = """
            [
              { "id": "spikes", "name": "spike trap", "damage": 5 },
              { "id": "snare", "name": "snare", "damage": 1, "status": { "status": "Slow", "ticks": 30 } }
            ]
            """,
        ["strata/test.json"] = """
            {
              "id": "test", "name": "Test", "width": 30, "height": 20, "rooms": [3, 5], "roomSize": [3, 6],
              "templates": [ { "shape": "rect" } ],
              "monsters": { "count": [2, 3], "table": [ { "id": "dummy" }, { "id": "chaser" } ] },
              "traps": { "count": [1, 2], "table": [ { "id": "spikes" } ] },
              "chests": { "count": [1, 2], "gold": [5, 5] },
              "maxTicks": 2000
            }
            """,
        ["monsters/idle.glyph"] = Idle,
        ["monsters/brute.glyph"] = Brute,
        ["monsters/chaser.glyph"] = Chaser,
        ["programs/walker.glyph"] = "while True:\n    if stairs():\n        move_toward(stairs())\n    else:\n        explore()\n",
        ["programs/helpers.glyph"] = "STEP = 1\n\ndef ahead_x():\n    return sense_ahead(East).x + STEP - 1\n",
    });

    public static readonly Dictionary<char, string> Keys = new()
    {
        ['d'] = "dummy",
        ['a'] = "armored",
        ['b'] = "brute",
        ['t'] = "stunner",
        ['f'] = "burner",
        ['c'] = "chaser",
    };

    public static World World(string map, params (string Chassis, string Source)[] party) => World(map, 500, party);

    public static World World(string map, int maxTicks, params (string Chassis, string Source)[] party)
    {
        var rows = map.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim()).ToList();
        var layout = FloorLayout.FromRows(rows, Keys);
        var members = party.Select((p, i) => new PartyMember(Pack.Chassis[p.Chassis].Name + (i > 0 && party.Take(i).Any(q => q.Chassis == p.Chassis) ? $"{i}" : ""), p.Chassis, p.Source)).ToList();
        return Core.Sim.World.Create(Pack, layout, members, seed: 7, maxTicks: maxTicks);
    }

    public static void Ticks(World w, int n)
    {
        for (var i = 0; i < n && !w.Finished; i++) w.Step();
    }

    public static List<string> Output(World w) => w.Log.Where(e => e.Kind == LogKind.Output).Select(e => e.Text).ToList();
}
