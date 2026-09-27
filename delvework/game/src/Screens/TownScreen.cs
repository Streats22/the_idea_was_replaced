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
        var lesson = p.NextToLearn;
        var signs = new List<TownSign>
        {
            new("delve", "The Mines", "Delve for gold and materials", true, next.Target == "delve"),
            new("codex", "Library: the Codex", lesson is null ? "Everything learned" : $"Learn {lesson.Title}: {lesson.Cost} gold", true, next.Target == "codex"),
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

        var top = new PanelContainer();
        top.AddThemeStyleboxOverride("panel", Ui.Box(new Color(Palette.Panel, 0.9f), Palette.Border, 0, 12));
        top.SetAnchorsPreset(LayoutPreset.TopWide);
        AddChild(top);
        var menu = Ui.Button("Menu");
        menu.Pressed += () => App.GoTitle();
        var settings = Ui.Button("Settings");
        settings.Pressed += App.ShowSettings;
        var party = Ui.Row(6);
        foreach (var id in p.Party) party.AddChild(Ui.Label("● " + p.Content.Chassis[id].Name, Palette.ForGolem(id), 13));
        var almanac = Ui.Button($"Almanac {p.FoundCount}/{p.AlmanacEntries.Count}", "Everything you have found out: monsters, materials, hazards and more");
        almanac.Pressed += () =>
        {
            App.Audio.Play(Sfx.Click);
            App.GoAlmanac();
        };
        top.AddChild(Ui.Row(18,
            Ui.Heading("Hollowmere", 22, Palette.Accent),
            Goods.Bar(App.Profile, 15),
            Ui.Label($"Knows {p.LearnedCount} of {p.Lessons.Count} features", Palette.Text, 14),
            Ui.Label("Party:", Palette.Muted, 13), party,
            Ui.Spacer(), almanac, Help.Menu(App), settings, menu));

        var hint = Ui.Column(4, Ui.Label("NEXT STEP", Palette.Accent, 11), Ui.Para(next.Text, Palette.Text, 15));
        var card = new PanelContainer { CustomMinimumSize = new Vector2(420, 0) };
        card.AddThemeStyleboxOverride("panel", Ui.Box(new Color(Palette.Panel, 0.92f), Palette.Accent.Darkened(0.4f), 12, 14));
        card.AddChild(hint);
        card.SetAnchorsPreset(LayoutPreset.BottomLeft);
        card.GrowVertical = GrowDirection.Begin;
        card.Position = new Vector2(20, -20);
        AddChild(card);
        card.Resized += () => card.Position = new Vector2(20, Size.Y - card.Size.Y - 20);
    }

    public TownView3D Town => _town;

    public override void _ExitTree() => App.Learned -= OnLearned;

    private void OnLearned() => Callable.From(() => App.GoTown()).CallDeferred();

    private (string Text, string Target) NextStep()
    {
        var p = App.Progress;
        var lesson = p.NextToLearn;
        var entrance = p.OpenSites.FirstOrDefault()?.Name ?? "the mines";
        if (lesson is not null && p.LearnStatus(lesson) == NodeStatus.Available)
        {
            return ($"You have enough gold to learn {lesson.Title} ({lesson.Cost} gold). Buy it in the Library: your golems can use it right away.", "codex");
        }
        if (p.LearnedCount == 1 && App.Profile.Gold < (lesson?.Cost ?? 0))
        {
            return ($"Your golem already knows simple commands like move(East). Enter the cave and delve into {entrance}: bring home {lesson?.Cost} gold to learn {lesson?.Title}.", "delve");
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
        if (close is not null && (lesson is null || p.LearnStatus(lesson) != NodeStatus.TooExpensive || close.Cost < lesson.Cost))
        {
            var missing = p.Missing(close);
            return ($"Next build: {close.Name}. You still need {Resources.Format(missing)}. {SkillScreen.Where(missing)}", "delve");
        }
        if (lesson is not null) return ($"Delve for gold: {lesson.Title} costs {lesson.Cost} and you have {App.Profile.Gold}. Better code brings more home.", "delve");
        return ("Delve into the mines for gold and materials for the rest of your upgrades. Improve your code to bring more home.", "delve");
    }

    private void Pick(string id)
    {
        App.Audio.Play(Sfx.Click);
        switch (id)
        {
            case "codex":
                App.GoCodex();
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
            var lockText = open ? "" : $"Learn {p.Lessons[Math.Clamp(site.Lessons, 1, p.Lessons.Count) - 1].Title} in the Library to open.";
            var info = Ui.Column(2,
                Ui.Label(site.Name, open ? Palette.Text : Palette.Muted, 16),
                Ui.Para(site.Description + (lockText.Length > 0 ? " " + lockText : ""), Palette.Muted, 13));
            info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var card = Ui.Card(Ui.Row(12, info, go));
            card.AddThemeStyleboxOverride("panel", Ui.Box(Palette.Panel2, Palette.Border, 10, 12));
            list.AddChild(card);
        }
        var party = string.Join(", ", p.Party.Select(id => p.Content.Chassis[id].Name));
        var close = Ui.Button("Close");
        modal = App.ShowModal(Ui.Column(14,
            Ui.Heading("Into the mines", 24),
            Ui.Para($"Your party ({party}) runs the code you last wrote for free delves. Each delve explores a new part of the mines. Everything the golems carry home is yours.", Palette.Muted, 14),
            list,
            Ui.Row(8, Ui.Spacer(), close)), 620);
        close.Pressed += () => App.CloseModal(modal);
    }
}
