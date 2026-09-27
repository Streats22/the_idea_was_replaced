using Delvework.Core.Content;
using Delvework.Core.Progress;
using Delvework.Game.Audio;
using Delvework.Game.View3D;
using Godot;

namespace Delvework.Game.Screens;

public partial class TownScreen : Control
{
    public required App App { get; init; }

    private TownView3D _town = null!;

    public override void _Ready()
    {
        var p = App.Progress;
        _town = new TownView3D();
        _town.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_town);
        _town.Refresh(p.Buildings(), p.Party);
        App.Audio.SetTownLevel(p.Buildings().Values.Sum());
        _town.Picked += Pick;
        App.Learned += OnLearned;
        App.ToastFinds();

        var next = NextStep();
        var village = p.Content.Skills.Count(n => n.Tree == SkillTree.Village && p.Status(n) == NodeStatus.Available);
        var equipment = p.Content.Skills.Count(n => n.Tree == SkillTree.Equipment && p.Status(n) == NodeStatus.Available);
        var arcana = p.Content.Skills.Count(n => n.Tree == SkillTree.Arcana && p.Status(n) == NodeStatus.Available);
        static string Afford(int n) => n == 0 ? "" : n == 1 ? "1 thing to buy" : $"{n} things to buy";
        var signs = new List<TownSign>
        {
            new("delve", "The Mines", p.NextToLearn is null ? "Delve for gold and materials" : "Runes, gold and materials", true, next.Target == "delve"),
            new("library", "Library", $"Almanac {p.FoundCount}/{p.AlmanacEntries.Count}", true, next.Target == "library"),
            new("village", "Notice board: Village", Afford(village), true, next.Target == "village"),
            new("equipment", "Forge: Equipment", p.IsTreeOpen(SkillTree.Equipment) ? Afford(equipment) : "In ruins", p.IsTreeOpen(SkillTree.Equipment), next.Target == "equipment"),
            new("arcana", "Arcane Tower: Arcana", p.IsTreeOpen(SkillTree.Arcana) ? Afford(arcana) : "In ruins", p.IsTreeOpen(SkillTree.Arcana), next.Target == "arcana"),
        };
        foreach (var w in p.BuiltWorkshops)
        {
            var kind = Core.Village.Workshops.KindOf(w);
            var waiting = string.Join(", ", kind.Inputs.Where(id => id != Resources.Wood).Select(id => $"{App.Profile.Amount(id)} {Resources.Name(id)}"));
            signs.Add(new TownSign(w.Id, w.Name, waiting.Length > 0 ? $"Edit the script · {waiting}" : "Edit the script", true, next.Target == w.Id));
        }
        var orders = p.Content.Commissions.Count(p.IsOpen);
        if (orders > 0)
        {
            var todo = p.Content.Commissions.Count(c => p.IsOpen(c) && p.Best(c) is null);
            signs.Add(new TownSign("commissions", "Commission board", todo > 0 ? $"{todo} new order{(todo == 1 ? "" : "s")}" : "Chase gold medals", true, next.Target == "commissions"));
        }
        _town.SetSigns(signs);

        var menu = Ui.Button("Menu", "Back to the title screen");
        menu.Pressed += () => App.GoTitle();
        var party = Ui.Row(10);
        foreach (var id in p.Party) party.AddChild(Ui.Label("◆ " + p.Content.Chassis[id].Name, Palette.ForGolem(id), 13));
        var runes = Ui.Button($"✦ Grimoire  {p.LearnedCount}/{p.Lessons.Count}", "The runes your golems have found, with examples");
        runes.Pressed += () =>
        {
            App.Audio.Play(Sfx.Page);
            Help.Grimoire(App);
        };

        var name = Ui.Frame(Ui.Column(-2, Ui.Title("Hollowmere", 20), Ui.Label("VILLAGE OF THE DELVERS", Palette.Muted, 10)), FrameKind.Plate, opacity: 0.94f);
        var wallet = Ui.Frame(Goods.Bar(App.Profile, 14), FrameKind.Plate, opacity: 0.94f);
        var crew = Ui.Frame(Ui.Row(8, Ui.Label("PARTY", Palette.Muted, 11), party), FrameKind.Plate, opacity: 0.94f);
        var tools = Ui.Frame(Ui.Row(6, runes, Help.Menu(App), App.SettingsButton(), menu), FrameKind.Plate, opacity: 0.94f);
        foreach (var plate in new[] { name, wallet, crew, tools }) plate.CustomMinimumSize = new Vector2(0, 58);
        var top = Ui.Row(8, name, wallet, crew, Ui.Spacer(), tools);
        top.MouseFilter = MouseFilterEnum.Ignore;
        top.SetAnchorsPreset(LayoutPreset.TopWide);
        top.OffsetLeft = 12;
        top.OffsetRight = -12;
        top.OffsetTop = 10;
        AddChild(top);

        var hint = Ui.Column(6, Ui.Banner("Next step", 11), Ui.Para(next.Text, Palette.Text, 15));
        var card = Ui.Frame(hint, opacity: 0.94f);
        card.CustomMinimumSize = new Vector2(430, 0);
        card.SetAnchorsPreset(LayoutPreset.BottomLeft);
        card.GrowVertical = GrowDirection.Begin;
        card.Position = new Vector2(16, -16);
        AddChild(card);
        card.Resized += () => card.Position = new Vector2(16, Size.Y - card.Size.Y - 16);
    }

    public TownView3D Town => _town;

    public override void _ExitTree() => App.Learned -= OnLearned;

    private void OnLearned() => Callable.From(() => App.GoTown()).CallDeferred();

    private (string Text, string Target) NextStep()
    {
        var p = App.Progress;
        var runeSite = p.TabletSite;
        if (p.LearnedCount == 1 && runeSite is not null)
        {
            return ($"Your golem already knows simple commands like move(East). Enter the cave and delve into {runeSite.Name}: a rune tablet lies on the way to the stairs. Walk past it and carry it home to learn a new part of the language.", "delve");
        }
        if (runeSite is not null && App.Profile.Delves % 3 == 0)
        {
            return ($"Another rune tablet waits in {runeSite.Name}. Bring it home and the whole party learns it. The runes you have found are in the Grimoire above your code.", "delve");
        }
        var affordable = p.Content.Skills.Where(n => p.Status(n) == NodeStatus.Available).OrderBy(n => n.Cost).FirstOrDefault();
        if (affordable is not null)
        {
            var place = affordable.Tree switch { SkillTree.Village => "notice board", SkillTree.Equipment => "Forge", _ => "Arcane Tower" };
            return ($"You can afford {affordable.Name} ({Resources.Format(Progression.FullPrice(affordable))}). Buy it at the {place}.", affordable.Tree.ToString().ToLowerInvariant());
        }
        var smelter = p.BuiltWorkshops.FirstOrDefault(w => w.Id == Core.Village.Smelter.Id);
        if (smelter is not null && App.Profile.Amount(Resources.Ore) >= 4 && App.Profile.Shifts <= 3)
        {
            return ($"You have {App.Profile.Amount(Resources.Ore)} iron ore waiting. Open the Smelter and teach it to smelt everything: with a loop it can keep the fire hot until the ore runs out.", smelter.Id);
        }
        var order = p.Content.Commissions.FirstOrDefault(c => p.IsOpen(c) && p.Best(c) is null);
        if (order is not null && App.Profile.Commissions.Count == 0)
        {
            return ($"An order is pinned on the commission board: \"{order.Title}\" from {order.Client}. It brings its own stores and pays {Resources.Format(order.Reward)}.", "commissions");
        }
        var close = p.Content.Skills
            .Where(n => p.Status(n) == NodeStatus.TooExpensive && n.Tree == SkillTree.Village)
            .OrderBy(n => p.Missing(n).Values.Sum())
            .FirstOrDefault();
        if (runeSite is not null) return ($"A rune tablet waits in {runeSite.Name}. Bring it home to learn the next part of the language.", "delve");
        if (close is not null)
        {
            var missing = p.Missing(close);
            return ($"Next build: {close.Name}. You still need {Resources.Format(missing)}. {SkillScreen.Where(missing)}", "delve");
        }
        return ("Delve into the mines for gold and materials for the rest of your upgrades. Improve your code to bring more home.", "delve");
    }

    private void Pick(string id)
    {
        App.Audio.Play(Sfx.Click);
        switch (id)
        {
            case "library":
                App.GoAlmanac();
                break;
            case "village":
                App.GoSkills(SkillTree.Village);
                break;
            case "equipment":
                App.GoSkills(SkillTree.Equipment);
                break;
            case "arcana":
                App.GoSkills(SkillTree.Arcana);
                break;
            case "delve":
                PickSite();
                break;
            case "commissions":
                App.GoCommissions();
                break;
            default:
                if (App.Progress.Workshop(id) is { } w) App.GoWorkshop(w);
                break;
        }
    }

    private void PickSite()
    {
        var p = App.Progress;
        var list = Ui.Column(10);
        Control modal = null!;
        foreach (var site in p.Content.Sites)
        {
            var open = p.IsOpen(site);
            var go = Ui.Button(open ? "Delve" : "Locked", primary: open);
            go.Disabled = !open;
            var s = site;
            go.Pressed += () =>
            {
                App.Audio.Play(Sfx.Click);
                App.GoSite(s);
            };
            var lockText = open ? "" : $"Opens when your golems know {site.Lessons} runes.";
            var name = Ui.Heading(site.Name, 17, open ? Palette.BrassLight : Palette.Muted);
            var info = Ui.Column(2, name, Ui.Para(site.Description + (lockText.Length > 0 ? " " + lockText : ""), Palette.Muted, 13));
            if (open && p.TabletAt(site) is not null) info.AddChild(Ui.Label("✦ A rune tablet lies here", Palette.Rune, 13));
            info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var card = Ui.Frame(Ui.Row(12, info, go), FrameKind.Plate);
            list.AddChild(card);
        }
        var party = string.Join(", ", p.Party.Select(id => p.Content.Chassis[id].Name));
        var close = Ui.Button("Close");
        modal = App.ShowModal(Ui.Column(14,
            Ui.Banner("Into the mines", 16),
            Ui.Para($"Your party ({party}) runs the code you last wrote for free delves. Each delve explores a new part of the mines. Everything the golems carry home is yours.", Palette.Muted, 14),
            list,
            Ui.Row(8, Ui.Spacer(), close)), 620);
        close.Pressed += () => App.CloseModal(modal);
    }
}
