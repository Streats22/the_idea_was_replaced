using Delvework.Core.Content;
using Delvework.Core.Progress;
using Delvework.Game.Audio;
using Delvework.Game.View3D;
using Godot;

namespace Delvework.Game.Screens;

/// <summary>
/// The Almanac: everything found out by playing. The Bestiary grows from a silhouette to stats
/// and lore (seen), to a weakness (studied) and finally the monster's own script (mastered).
/// </summary>
public partial class AlmanacScreen : Control
{
    public required App App { get; init; }
    public string? InitialCategory { get; init; }

    private readonly List<string> _categories = [];
    private readonly Dictionary<string, Button> _tabs = [];
    private VBoxContainer _list = null!;
    private string _category = Almanac.MonsterCategory;

    public override void _Ready()
    {
        var p = App.Progress;
        foreach (var e in p.AlmanacEntries)
        {
            if (!_categories.Contains(e.Category)) _categories.Add(e.Category);
        }
        _category = InitialCategory is { } c && _categories.Contains(c) ? c : _categories[0];

        var root = Ui.Column(12);
        var margin = Ui.Pad(root, 16);
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(margin);

        var back = Ui.Button("← Town");
        back.Pressed += () =>
        {
            App.Audio.Play(Sfx.Click);
            App.GoTown();
        };
        var gold = p.AlmanacEntries.Where(p.IsFound).Sum(e => e.Reward);
        root.AddChild(Ui.Row(14, back,
            Ui.Column(0, Ui.Label("LIBRARY", Palette.Accent, 11), Ui.Heading("The Almanac", 24)),
            Ui.Label($"Found {p.FoundCount} of {p.AlmanacEntries.Count} · {gold} gold from discoveries", Palette.Muted, 14),
            Ui.Spacer(), Goods.Bar(App.Profile, 14), Help.Menu(App)));

        var tabs = Ui.Column(6);
        tabs.CustomMinimumSize = new Vector2(220, 0);
        var group = new ButtonGroup();
        foreach (var cat in _categories)
        {
            var entries = p.AlmanacEntries.Where(e => e.Category == cat).ToList();
            var b = Ui.Button($"{cat}   {entries.Count(p.IsFound)}/{entries.Count}");
            b.ToggleMode = true;
            b.ButtonGroup = group;
            b.Alignment = HorizontalAlignment.Left;
            b.CustomMinimumSize = new Vector2(0, 38);
            var id = cat;
            b.Pressed += () =>
            {
                App.Audio.Play(Sfx.Page);
                Show(id);
            };
            tabs.AddChild(b);
            _tabs[cat] = b;
        }
        tabs.AddChild(Ui.Para("Entries are found by playing: meet monsters, mine new veins, set off traps, run your workshops and fill commissions. Each one pays gold the moment you find it.", Palette.Muted, 12));

        _list = Ui.Column(10);
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(Ui.Pad(_list, 4));
        var body = Ui.Row(16, Ui.Card(tabs), scroll);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(body);
        Show(_category);
    }

    public void Show(string category)
    {
        _category = category;
        _tabs[category].SetPressedNoSignal(true);
        foreach (var c in _list.GetChildren()) c.QueueFree();
        var p = App.Progress;
        if (category == Almanac.MonsterCategory)
        {
            foreach (var m in App.Content.Monsters.Values.OrderBy(m => m.Hp).ThenBy(m => m.Id, StringComparer.Ordinal)) _list.AddChild(MonsterCard(m));
            return;
        }
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        foreach (var e in p.AlmanacEntries.Where(e => e.Category == category)) grid.AddChild(EntryCard(e));
        _list.AddChild(grid);
    }

    private PanelContainer EntryCard(AlmanacEntryDef e)
    {
        var found = App.Progress.IsFound(e);
        var progress = Almanac.Progress(App.Profile, e);
        var col = Ui.Column(4,
            Ui.Row(8, Ui.Label(found ? e.Title : "? ? ?", found ? Palette.Text : Palette.Muted, 16), Ui.Spacer(),
                Ui.Label(found ? $"+{e.Reward} gold" : $"{e.Reward} gold", found ? Palette.Accent : Palette.Muted, 12)),
            Ui.Para(found ? e.Text : e.Hint, found ? Palette.Muted : Palette.Muted.Darkened(0.2f), 13));
        if (!found && e.Count > 1) col.AddChild(Bar(progress, e.Count, Palette.Accent));
        var card = Ui.Card(col);
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.CustomMinimumSize = new Vector2(380, 0);
        card.AddThemeStyleboxOverride("panel", Ui.Box(found ? Palette.Panel2 : Palette.Panel, found ? Palette.Accent.Darkened(0.5f) : Palette.Border, 10, 12));
        return card;
    }

    private static Control Bar(int value, int max, Color color)
    {
        var bar = new ProgressBar { MinValue = 0, MaxValue = max, Value = value, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bar.AddThemeStyleboxOverride("background", Ui.Box(Palette.Void, Palette.Border, 4, 0));
        bar.AddThemeStyleboxOverride("fill", Ui.Box(color, null, 4, 0));
        return Ui.Row(8, bar, Ui.Label($"{value}/{max}", Palette.Muted, 11));
    }

    private PanelContainer MonsterCard(MonsterDef m)
    {
        var know = Almanac.Knowledge(App.Profile, m.Id);
        var kills = App.Profile.Found.GetValueOrDefault("killed:" + m.Id);
        var portrait = Portrait(m.Id, know != MonsterKnowledge.Unknown);
        var info = Ui.Column(6);
        info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var stage = know switch
        {
            MonsterKnowledge.Mastered => ("MASTERED", Palette.Accent),
            MonsterKnowledge.Studied => ("STUDIED", Palette.Seeker),
            MonsterKnowledge.Seen => ("SEEN", Palette.Ok),
            _ => ("UNKNOWN", Palette.Muted),
        };
        info.AddChild(Ui.Row(10, Ui.Label(know == MonsterKnowledge.Unknown ? "? ? ?" : m.Name, know == MonsterKnowledge.Unknown ? Palette.Muted : Palette.Text, 18),
            Ui.Label(stage.Item1, stage.Item2, 11), Ui.Spacer(), Ui.Label($"defeated {kills}", Palette.Muted, 12)));
        if (know == MonsterKnowledge.Unknown)
        {
            info.AddChild(Ui.Para("Something lurks in the mines. When one of your golems sees it, it goes in the book.", Palette.Muted, 13));
        }
        else
        {
            var stats = $"HP {m.Hp} · attack {m.Attack} {m.Damage.ToString().ToLowerInvariant()}{(m.Armor > 0 ? $" · armor {m.Armor}" : "")} · sees {m.Sight} tiles · moves every {m.MoveTicks} ticks";
            info.AddChild(Ui.Label(stats, Palette.Accent.Lerp(Palette.Text, 0.5f), 13));
            if (m.Essence[1] > 0) info.AddChild(Goods.Gains(new Dictionary<string, int> { [Resources.Gold] = m.Gold[1], [Resources.Essence] = m.Essence[1] }, 12, "up to "));
            info.AddChild(Ui.Para(m.Lore, Palette.Text, 14));
            if (know >= MonsterKnowledge.Studied) info.AddChild(Ui.Para("Weakness: " + m.Weakness, Palette.Seeker, 14));
            else info.AddChild(Ui.Row(8, Ui.Label($"Weakness: defeat {Almanac.StudyKills} to learn it", Palette.Muted, 12), Bar(kills, Almanac.StudyKills, Palette.Seeker)));
        }
        if (know == MonsterKnowledge.Mastered)
        {
            var read = Ui.Button("Read its script", "Every line this monster runs, in the same language as your golems");
            read.Pressed += () => ShowScript(m);
            info.AddChild(Ui.Row(8, read, Ui.Label($"monsters/{m.Script}", Palette.Muted, 12)));
        }
        else if (know == MonsterKnowledge.Studied)
        {
            info.AddChild(Ui.Row(8, Ui.Label($"Script: defeat {Almanac.MasterKills} to read it", Palette.Muted, 12), Bar(kills, Almanac.MasterKills, Palette.Accent)));
        }
        var card = Ui.Card(Ui.Row(14, portrait, info));
        card.AddThemeStyleboxOverride("panel", Ui.Box(know == MonsterKnowledge.Unknown ? Palette.Panel : Palette.Panel2, stage.Item2.Darkened(0.55f), 12, 12));
        return card;
    }

    /// <summary>A small turning 3D model, or a dark silhouette while the monster is unknown.</summary>
    private static Control Portrait(string id, bool known)
    {
        var (container, viewport) = Iso.PixelViewport();
        container.CustomMinimumSize = new Vector2(120, 120);
        container.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        container.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        viewport.AddChild(new WorldEnvironment { Environment = Iso.Environment(new Color("0b0a09"), new Color("8a7a6a"), known ? 0.7f : 0.02f, 0.6f) });
        var cam = Iso.Camera(1.9f);
        viewport.AddChild(cam);
        Iso.Aim(cam, new Vector3(0, 0.55f, 0), 10);
        viewport.AddChild(new DirectionalLight3D { LightEnergy = known ? 1.1f : 0f, RotationDegrees = new Vector3(-45, 30, 0) });
        var turn = new Turntable { Known = known };
        viewport.AddChild(turn);
        var fig = Figures.Monster(id);
        turn.AddChild(fig.Root);
        Iso.Cylinder(turn, 0.55f, 0.6f, 0.08f, new Vector3(0, 0.04f, 0), Iso.Flagstone(new Color("6b6259"), new Color("3a332d"), 0.55f, "dungeon-floor"), 16);
        if (!known)
        {
            if (fig.Light is not null) fig.Light.Visible = false;
            if (fig.Core is not null) fig.Core.EmissionEnergyMultiplier = 0.6f;
        }
        return container;
    }

    private void ShowScript(MonsterDef m)
    {
        App.Audio.Play(Sfx.Page);
        var editor = new GlyphEditor { Editable = false, CustomMinimumSize = new Vector2(620, 420), Environment = Core.Sim.MonsterApi.Environment };
        editor.ReplaceText(App.Content.MonsterSources.GetValueOrDefault(m.Id, ""));
        var close = Ui.Button("Close", primary: true);
        var modal = App.ShowModal(Ui.Column(12,
            Ui.Label("MASTERED", Palette.Accent, 11),
            Ui.Heading($"{m.Name}: monsters/{m.Script}", 22),
            Ui.Para("Monsters run code too. Now that you know every line, you can write your golems' programs around it.", Palette.Muted, 13),
            editor,
            Ui.Row(8, Ui.Spacer(), close)), 680);
        close.Pressed += () => App.CloseModal(modal);
    }
}

/// <summary>Slowly turns its children; bobs a little when <see cref="Known"/>.</summary>
public partial class Turntable : Node3D
{
    public bool Known { get; init; }
    private double _time;

    public override void _Process(double delta)
    {
        _time += delta;
        Rotation = new Vector3(0, (float)_time * (Known ? 0.7f : 0.2f), 0);
    }
}
