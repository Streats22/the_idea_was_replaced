using Delvework.Core.Dungeon;
using Delvework.Core.Glyph;
using Delvework.Core.Sim;
using static Delvework.Core.Tests.Sim.TestContent;

namespace Delvework.Core.Tests.Sim;

public class SpellTests
{
    private static readonly Loadout AllSpells = new() { Mana = 20, Spells = ["heal", "bolt", "reveal", "shield"] };

    private static World Make(string map, string source, Loadout? loadout, int maxTicks = 300)
    {
        var rows = map.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim()).ToList();
        return Core.Sim.World.Create(Pack, FloorLayout.FromRows(rows, Keys), [new PartyMember("Tank", "tank", source)], 7, maxTicks, Tiers.Max, loadout);
    }

    [Fact]
    public void LoadoutBonusesApplyToEveryGolem()
    {
        var gear = new Loadout { Hp = 6, Armor = 1, Attack = 2, Sight = 1, Budget = 20, Speed = 1, RecallStones = 1, Mana = 8 };
        var g = Make("#####\n#S.>#\n#####", "wait()", gear).Golems[0];
        Assert.Equal(36, g.MaxHp);
        Assert.Equal(36, g.Hp);
        Assert.Equal(1, g.Armor);
        Assert.Equal(5, g.Attack);
        Assert.Equal(7, g.Sight);
        Assert.Equal(70, g.Budget);
        Assert.Equal(1, g.MoveTicks);
        Assert.Equal(2, g.RecallStones);
        Assert.Equal(8, g.Mana);
    }

    [Fact]
    public void StepsNeverGetFasterThanOneTick()
    {
        var g = Make("#####\n#S.>#\n#####", "wait()", new Loadout { Speed = 9 }).Golems[0];
        Assert.Equal(1, g.MoveTicks);
    }

    [Fact]
    public void UnlearnedSpellsAreCompileErrorsWithALoadout()
    {
        var w = Make("#####\n#S.>#\n#####", "heal()", new Loadout { Mana = 10, Spells = ["bolt"] });
        var g = w.Golems[0];
        Assert.Equal(GolemState.Halted, g.State);
        Assert.Contains("heal() is a spell your golems have not learned yet", g.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(GolemState.Active, Make("#####\n#S.>#\n#####", "bolt(nearest_enemy())", new Loadout { Spells = ["bolt"] }).Golems[0].State);
    }

    [Fact]
    public void WithoutALoadoutSpellsCompileButHaveNoMana()
    {
        var w = Make("#####\n#S.>#\n#####", "print(heal())", null);
        Ticks(w, 5);
        Assert.Equal(["Tank: False"], Output(w));
    }

    [Fact]
    public void HealRestoresHpAndSpendsMana()
    {
        var w = Make("#######\n#Sb..>#\n#######", "on hurt(n, src):\n    print(heal(), hp(), mana())\nwhile True:\n    wait()", AllSpells);
        Ticks(w, 60);
        var line = Output(w)[0].Split(' ');
        Assert.Equal("True", line[1]);
        Assert.True(int.Parse(line[2], System.Globalization.CultureInfo.InvariantCulture) > 25);
        Assert.Contains(w.Log, e => e.Text.Contains("casts heal", StringComparison.Ordinal));
    }

    [Fact]
    public void HealNeverExceedsMaxHp()
    {
        var w = Make("#####\n#S.>#\n#####", "heal()\nprint(hp(), mana())", AllSpells);
        Ticks(w, 10);
        Assert.Equal(["Tank: 30 16"], Output(w));
    }

    [Fact]
    public void BoltHitsAnEnemyInSightFromAfar()
    {
        var w = Make("##########\n#S.....d>#\n##########", "e = nearest_enemy()\nprint(bolt(e), mana())\nprint(enemy_alive(e))", AllSpells);
        Ticks(w, 12);
        Assert.Equal(["Tank: True 17", "Tank: True"], Output(w));
        Assert.Equal(1, w.Monsters[0].Hp);
        Assert.Contains(w.Log, e => e.Text.Contains("bolt hits the Dummy for 5", StringComparison.Ordinal));
    }

    [Fact]
    public void BoltNeedsAnEnemy()
    {
        var w = Make("#####\n#S.>#\n#####", "bolt(stairs())", AllSpells);
        Ticks(w, 3);
        Assert.Equal(GolemState.Halted, w.Golems[0].State);
        Assert.Contains("bolt() needs an enemy", w.Golems[0].Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RevealShowsNearbyTrapsAndLayout()
    {
        var w = Make("###########\n#S...^...>#\n###########", "reveal()\nwait()", new Loadout { Mana = 5, Spells = ["reveal"] });
        Assert.False(w.Traps[0].Revealed);
        Ticks(w, 6);
        Assert.True(w.Traps[0].Revealed);
        Assert.Equal(3, w.Golems[0].Mana);
    }

    [Fact]
    public void ShieldHalvesDamage()
    {
        var map = "#######\n#Sb..>#\n#######";
        var plain = Make(map, "while True:\n    wait()", AllSpells);
        var shielded = Make(map, "shield()\nwhile True:\n    wait()", AllSpells);
        Ticks(plain, 30);
        Ticks(shielded, 30);
        var lostPlain = 30 - plain.Golems[0].Hp;
        var lostShielded = 30 - shielded.Golems[0].Hp;
        Assert.True(lostPlain > 0);
        Assert.True(lostShielded < lostPlain, $"{lostShielded} vs {lostPlain}");
    }

    [Fact]
    public void ManaRefillsSlowly()
    {
        var w = Make("#####\n#S.>#\n#####", "heal()\nwhile True:\n    wait(10)", new Loadout { Mana = 8, Spells = ["heal"] });
        Ticks(w, 6);
        Assert.Equal(4, w.Golems[0].Mana);
        Ticks(w, Spells.ManaRegenInterval * 2);
        Assert.Equal(6, w.Golems[0].Mana);
    }

    [Fact]
    public void NotEnoughManaReturnsFalse()
    {
        var w = Make("#####\n#S.>#\n#####", "print(heal())", new Loadout { Mana = 2, Spells = ["heal"] });
        Ticks(w, 5);
        Assert.Equal(["Tank: False"], Output(w));
    }

    [Fact]
    public void EffectsDescribeTheLastTick()
    {
        var w = Make("#####\n#Sd>#\n#####", "while True:\n    attack()", null);
        var kinds = new List<EffectKind>();
        for (var i = 0; i < 20; i++)
        {
            w.Step();
            kinds.AddRange(w.Effects.Select(e => e.Kind));
        }
        Assert.Contains(EffectKind.Attack, kinds);
        Assert.Contains(EffectKind.Hit, kinds);
        Assert.Contains(EffectKind.Kill, kinds);
    }

    [Fact]
    public void LoadoutReplaysVerify()
    {
        var content = Shared.Content;
        var setup = new DelveSetup(3, "mines", [new PartyMember("Warden", "warden", "while True:\n    if nearest_enemy():\n        bolt(nearest_enemy())\n    explore()")],
            Loadout: new Loadout { Hp = 6, Mana = 12, Spells = ["bolt"] }, MaxTicks: 600);
        var file = Core.Replay.ReplayRecorder.Record(content, setup).File!;
        var back = Core.Replay.ReplayFile.FromJson(file.ToJson());
        Assert.Equal(12, back.Loadout!.Mana);
        Assert.True(Core.Replay.ReplayVerifier.Verify(content, back).Ok);
    }
}
