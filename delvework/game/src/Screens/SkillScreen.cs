using Delvework.Core.Content;
using Delvework.Core.Progress;
using Delvework.Game.Audio;
using Godot;

namespace Delvework.Game.Screens;

/// <summary>The three skill trees (Village, Equipment, Arcana): pick a node, read what it does, buy it with gold.</summary>
public partial class SkillScreen : Control
{
    private const float CardW = 210, CardH = 84, GapX = 48, GapY = 26;

    public required App App { get; init; }
    public required SkillTree Tree { get; set; }

    private HBoxContainer _gold = null!;
    private HBoxContainer _tabs = null!;
    private TreeCanvas _canvas = null!;
    private VBoxContainer _details = null!;
    private Label _treeText = null!;
    private SkillNode? _selected;

    public override void _Ready()
    {
        var root = Ui.Column(12);
        var margin = Ui.Pad(root, 18);
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(margin);

        var back = Ui.Button("← Town");
        back.Pressed += () =>
        {
            App.Audio.Play(Sfx.Click);
            App.GoTown();
        };
        _gold = Ui.Row(0);
        _tabs = Ui.Row(6);
        root.AddChild(Ui.Row(14, back, Ui.Heading("Skills", 28, Palette.Accent), _tabs, Ui.Spacer(), _gold));
        _treeText = Ui.Para("", Palette.Muted, 14);
        root.AddChild(_treeText);

        var split = Ui.Row(16);
        split.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(split);

        _canvas = new TreeCanvas { Screen = this, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        scroll.AddChild(_canvas);
        var canvasCard = Ui.Card(scroll);
        canvasCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        canvasCard.SizeFlagsVertical = SizeFlags.ExpandFill;
        split.AddChild(canvasCard);

        _details = Ui.Column(12);
        var detailCard = Ui.Card(_details);
        detailCard.CustomMinimumSize = new Vector2(380, 0);
        detailCard.SizeFlagsVertical = SizeFlags.ExpandFill;
        split.AddChild(detailCard);
        Refresh();
    }

    public void Refresh()
    {
        var p = App.Progress;
        foreach (var c in _gold.GetChildren()) c.QueueFree();
        _gold.AddChild(Goods.Bar(App.Profile, 16));
        foreach (var c in _tabs.GetChildren()) c.QueueFree();
        foreach (var tree in p.Content.Trees)
        {
            var open = p.IsTreeOpen(tree.Id);
            var b = Ui.Button(open ? tree.Name : "🔒 " + tree.Name, open ? tree.Description : $"Build {p.Node(tree.OpenedBy!)?.Name} in the Village first");
            b.ToggleMode = true;
            b.ButtonPressed = tree.Id == Tree;
            var id = tree.Id;
            b.Pressed += () =>
            {
                App.Audio.Play(Sfx.Click);
                Tree = id;
                _selected = null;
                Refresh();
            };
            _tabs.AddChild(b);
        }
        var def = p.Content.Trees.First(t => t.Id == Tree);
        _treeText.Text = p.IsTreeOpen(Tree) ? def.Description : $"{def.Description} Build {p.Node(def.OpenedBy!)?.Name} in the Village tree to open it.";
        _selected ??= Nodes.FirstOrDefault(n => p.Status(n) == NodeStatus.Available) ?? Nodes.FirstOrDefault();
        _canvas.Rebuild();
        ShowDetails();
    }

    internal IEnumerable<SkillNode> Nodes => App.Progress.Content.Skills.Where(n => n.Tree == Tree);

    internal static Vector2 CardPos(SkillNode n) => new(20 + n.Col * (CardW + GapX), 20 + n.Row * (CardH + GapY));

    internal Button Card(SkillNode node)
    {
        var p = App.Progress;
        var status = p.Status(node);
        var price = Resources.Format(Progression.FullPrice(node));
        if (price.Length == 0) price = "free";
        var (border, text, tag) = status switch
        {
            NodeStatus.Owned => (Palette.Ok, Palette.Ok, "✓ owned"),
            NodeStatus.Available => (Palette.Accent, Palette.Text, price),
            NodeStatus.TooExpensive => (Palette.Border.Lightened(0.3f), Palette.Text, price),
            _ => (Palette.Border, Palette.Muted, "🔒 " + price),
        };
        var b = new Button
        {
            Text = $"{node.Name}\n{Wrap(tag, 28)}",
            CustomMinimumSize = new Vector2(CardW, CardH),
            Size = new Vector2(CardW, CardH),
            Position = CardPos(node),
            FocusMode = FocusModeEnum.None,
            ClipText = true,
            TooltipText = node.Description,
        };
        var bg = status == NodeStatus.Owned ? Palette.Panel2.Lerp(Palette.Ok, 0.1f) : status == NodeStatus.Locked ? Palette.Panel : Palette.Panel2;
        b.AddThemeStyleboxOverride("normal", Ui.Box(bg, border, 10, 8));
        b.AddThemeStyleboxOverride("hover", Ui.Box(bg.Lightened(0.06f), Palette.Accent, 10, 8));
        b.AddThemeStyleboxOverride("pressed", Ui.Box(bg, Palette.Accent, 10, 8));
        if (node == _selected)
        {
            var sel = Ui.Box(bg.Lightened(0.08f), Palette.Accent, 10, 8);
            sel.SetBorderWidthAll(3);
            b.AddThemeStyleboxOverride("normal", sel);
        }
        b.AddThemeColorOverride("font_color", text);
        b.AddThemeFontSizeOverride("font_size", 13);
        b.Pressed += () =>
        {
            App.Audio.Play(Sfx.Click);
            _selected = node;
            _canvas.Rebuild();
            ShowDetails();
        };
        return b;
    }

    private static string Wrap(string text, int width)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var part in text.Split(", "))
        {
            var next = line.Length == 0 ? part : $"{line}, {part}";
            if (next.Length > width && line.Length > 0)
            {
                lines.Add(line + ",");
                line = part;
            }
            else line = next;
        }
        lines.Add(line);
        return string.Join('\n', lines);
    }

    private void ShowDetails()
    {
        foreach (var c in _details.GetChildren()) c.QueueFree();
        if (_selected is not { } node) return;
        var p = App.Progress;
        var status = p.Status(node);
        _details.AddChild(Ui.Label(node.Tree.ToString().ToUpperInvariant(), Palette.Accent, 12));
        _details.AddChild(Ui.Heading(node.Name, 26));
        _details.AddChild(Ui.Para(node.Description, Palette.Text, 16));
        if (node.Effect.Produce.Count > 0)
        {
            _details.AddChild(Ui.Label("AFTER EVERY DELVE", Palette.Muted, 11));
            _details.AddChild(Goods.Gains(node.Effect.Produce));
        }
        if (p.Content.Workshops.FirstOrDefault(w => w.Building == node.Id) is { } workshop)
        {
            _details.AddChild(Ui.Para($"A workshop that runs your script: {workshop.Summary}", Palette.Muted, 13));
            var how = Ui.Button("How it works");
            how.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            how.Pressed += () => Help.Workshop(App, workshop);
            _details.AddChild(how);
        }
        if (node.Effect.Spell is { } spell)
        {
            var def = Core.Sim.Spells.Find(spell);
            if (def is not null)
            {
                _details.AddChild(Ui.Label("New function for your code:", Palette.Muted, 13));
                _details.AddChild(GlyphEditor.Snippet(SpellExample(spell)));
                _details.AddChild(Ui.Para(def.Doc, Palette.Muted, 13));
            }
        }
        if (node.Requires.Count > 0)
        {
            _details.AddChild(Ui.Para("Needs: " + string.Join(", ", node.Requires.Select(r => (p.Owns(r) ? "✓ " : "✗ ") + (p.Node(r)?.Name ?? r))), Palette.Muted, 13));
        }
        if (node.Lessons > 0)
        {
            var lesson = p.Lessons[node.Lessons - 1];
            _details.AddChild(Ui.Para($"{(p.LearnedCount >= node.Lessons ? "✓" : "✗")} Learned {lesson.Title}", Palette.Muted, 13));
        }
        _details.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        switch (status)
        {
            case NodeStatus.Owned:
                _details.AddChild(Ui.Label("✓ You own this.", Palette.Ok, 16));
                break;
            case NodeStatus.Locked:
                _details.AddChild(Ui.Para("🔒 " + p.LockReason(node), Palette.Danger, 15));
                break;
            default:
                _details.AddChild(Ui.Label("COSTS", Palette.Muted, 11));
                _details.AddChild(Goods.Price(Progression.FullPrice(node), App.Profile, 15));
                var buy = Ui.Button("Buy", primary: status == NodeStatus.Available);
                buy.CustomMinimumSize = new Vector2(0, 46);
                buy.Disabled = status != NodeStatus.Available;
                if (status == NodeStatus.TooExpensive) _details.AddChild(Ui.Para($"You still need {Resources.Format(p.Missing(node))}. {Where(p.Missing(node))}", Palette.Muted, 13));
                buy.Pressed += () => Buy(node);
                _details.AddChild(buy);
                break;
        }
    }

    /// <summary>Where the missing materials come from.</summary>
    internal static string Where(IReadOnlyDictionary<string, int> missing)
    {
        var tips = new List<string>();
        if (missing.ContainsKey(Resources.Gold)) tips.Add("Gold comes from chests in the mines.");
        if (missing.ContainsKey(Resources.Stone) || missing.ContainsKey(Resources.Ore)) tips.Add("Golems mine() stone and iron ore from veins in the mine walls; the Quarry cuts stone too.");
        if (missing.ContainsKey(Resources.Wood)) tips.Add("The Woodcutter's Hut brings wood after every delve, and old timber can be mined.");
        if (missing.ContainsKey(Resources.Iron)) tips.Add("The Smelter makes iron from ore.");
        if (missing.ContainsKey(Resources.Wheat)) tips.Add("The Wheat Fields grow wheat.");
        if (missing.ContainsKey(Resources.Bread)) tips.Add("The Bakery bakes bread from wheat.");
        return string.Join(" ", tips);
    }

    private static string SpellExample(string spell) => spell switch
    {
        "heal" => "if hp() < 10 and mana() >= 4:\n    heal()",
        "bolt" => "enemy = nearest_enemy()\nif enemy and mana() >= 3:\n    bolt(enemy)",
        "reveal" => "if mana() >= 2:\n    reveal()",
        "shield" => "on see(enemy):\n    shield()",
        _ => spell + "()",
    };

    private void Buy(SkillNode node)
    {
        var p = App.Progress;
        var openedBefore = p.Content.Trees.Where(t => p.IsTreeOpen(t.Id)).Select(t => t.Id).ToList();
        if (!p.Buy(node)) return;
        App.Audio.Play(Sfx.Unlock);
        App.Save();
        var opened = p.Content.Trees.Where(t => p.IsTreeOpen(t.Id) && !openedBefore.Contains(t.Id)).ToList();
        Refresh();
        var lines = new List<string> { node.Description };
        foreach (var t in opened) lines.Add($"The {t.Name} tree is open: {t.Description}");
        if (node.Effect.PartySize > 0) lines.Add($"Your party is now: {string.Join(", ", p.Party.Select(id => p.Content.Chassis[id].Name))}. Each golem has its own program tab in the delve screen.");
        if (node.Effect.Produce.Count > 0) lines.Add($"From now on it delivers {Resources.Format(node.Effect.Produce)} after every delve.");
        var workshop = p.Content.Workshops.FirstOrDefault(w => w.Building == node.Id);
        if (workshop is not null) lines.Add($"It works one shift after every delve, running the script you write for it. Open it from the village to change the script and watch it work.");
        var ok = Ui.Button(workshop is null ? "Nice" : "Later", primary: workshop is null);
        var buttons = Ui.Row(8, Ui.Spacer(), ok);
        var modal = App.ShowModal(Ui.Column(12,
            Ui.Label("UNLOCKED", Palette.Ok, 12),
            Ui.Heading(node.Name, 26, Palette.Accent),
            Ui.Para(string.Join("\n\n", lines)),
            buttons), 500);
        ok.Pressed += () => App.CloseModal(modal);
        if (workshop is not null)
        {
            var open = Ui.Button($"Open the {workshop.Name}", primary: true);
            open.Pressed += () => App.GoWorkshop(workshop);
            buttons.AddChild(open);
        }
    }

    /// <summary>Draws the requirement lines and hosts the node cards.</summary>
    internal sealed partial class TreeCanvas : Control
    {
        public SkillScreen Screen { get; init; } = null!;

        public void Rebuild()
        {
            foreach (var c in GetChildren()) c.QueueFree();
            var nodes = Screen.Nodes.ToList();
            if (nodes.Count == 0) return;
            foreach (var n in nodes) AddChild(Screen.Card(n));
            CustomMinimumSize = new Vector2(40 + (nodes.Max(n => n.Col) + 1) * (CardW + GapX), 40 + (nodes.Max(n => n.Row) + 1) * (CardH + GapY));
            QueueRedraw();
        }

        public override void _Draw()
        {
            var p = Screen.App.Progress;
            var nodes = Screen.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
            foreach (var n in nodes.Values)
            {
                foreach (var r in n.Requires)
                {
                    if (!nodes.TryGetValue(r, out var from)) continue;
                    var a = CardPos(from) + new Vector2(CardW, CardH / 2);
                    var b = CardPos(n) + new Vector2(0, CardH / 2);
                    var color = p.Owns(r) ? (p.Owns(n.Id) ? Palette.Ok : Palette.Accent) : Palette.Border.Lightened(0.15f);
                    var near = a.X + GapX / 2;
                    if (n.Col - from.Col <= 1)
                    {
                        DrawPolyline([a, new Vector2(near, a.Y), new Vector2(near, b.Y), b], color, 2.5f, true);
                        continue;
                    }
                    // Longer links run along the gap between rows so they never cross a card.
                    var far = b.X - GapX / 2;
                    var lane = (n.Row >= from.Row ? CardPos(n).Y : CardPos(from).Y) - GapY / 2;
                    DrawPolyline([a, new Vector2(near, a.Y), new Vector2(near, lane), new Vector2(far, lane), new Vector2(far, b.Y), b], color, 2.5f, true);
                }
            }
        }
    }
}
