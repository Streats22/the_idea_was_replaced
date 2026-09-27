using Delvework.Core.Content;
using Delvework.Core.Glyph;
using Delvework.Core.Progress;
using Delvework.Core.Sim;
using Delvework.Game.Audio;
using Delvework.Game.Screens;
using Godot;

namespace Delvework.Game;

/// <summary>
/// A small window that floats over any screen: drag it by its title bar, resize it from the
/// corner, close it with ✕. The game keeps playing underneath.
/// </summary>
public partial class HelpWindow : PanelContainer
{
    public required string Title { get; init; }
    public required Vector2 StartSize { get; init; }

    public event Action? Closed;

    private MarginContainer _body = null!;
    private ScrollContainer _scroll = null!;
    private Label _title = null!;
    private bool _dragging, _resizing;

    public override void _Ready()
    {
        Size = StartSize;
        var frame = Ui.Box(Palette.Panel, Palette.Accent.Darkened(0.35f), 12, 0);
        frame.ShadowColor = new Color(0, 0, 0, 0.55f);
        frame.ShadowSize = 18;
        frame.ShadowOffset = new Vector2(0, 6);
        AddThemeStyleboxOverride("panel", frame);
        var col = Ui.Column(0);
        AddChild(col);

        var bar = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.Move };
        var barStyle = Ui.Box(Palette.Panel2, null, 12, 8);
        barStyle.CornerRadiusBottomLeft = barStyle.CornerRadiusBottomRight = 0;
        barStyle.ContentMarginLeft = 14;
        bar.AddThemeStyleboxOverride("panel", barStyle);
        var close = Ui.Button("✕", "Close (Esc)");
        close.Flat = true;
        close.Pressed += Close;
        _title = Ui.Label(Title, Palette.Accent, 13);
        bar.AddChild(Ui.Row(8, _title, Ui.Spacer(), close));
        bar.GuiInput += e => Drag(e, ref _dragging, d => Position = Clamp(Position + d));
        col.AddChild(bar);

        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        _body = Ui.Pad(new Control(), 16);
        _body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(_body);
        col.AddChild(_scroll);

        var grip = Ui.Label("◢", Palette.Muted.Darkened(0.3f), 12);
        grip.MouseFilter = MouseFilterEnum.Stop;
        grip.MouseDefaultCursorShape = CursorShape.Fdiagsize;
        grip.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        grip.GuiInput += e => Drag(e, ref _resizing, d => Size = new Vector2(Math.Max(300, Size.X + d.X), Math.Max(180, Size.Y + d.Y)));
        col.AddChild(grip);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true }) MoveToFront();
    }

    private void Drag(InputEvent e, ref bool active, Action<Vector2> apply)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            active = mb.Pressed;
            MoveToFront();
            AcceptEvent();
        }
        else if (e is InputEventMouseMotion motion && active)
        {
            apply(motion.Relative);
            AcceptEvent();
        }
    }

    private Vector2 Clamp(Vector2 pos)
    {
        var area = GetParentAreaSize();
        return new Vector2(Math.Clamp(pos.X, 40 - Size.X, area.X - 40), Math.Clamp(pos.Y, 0, area.Y - 36));
    }

    public void SetBody(string title, Control content)
    {
        _title.Text = title;
        foreach (var c in _body.GetChildren()) c.QueueFree();
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _body.AddChild(content);
        _scroll.ScrollVertical = 0;
        _ = Fit(content);
    }

    /// <summary>Shrink to the content (up to <see cref="StartSize"/>) once it has been laid out, and stay on screen.</summary>
    private async Task Fit(Control content)
    {
        for (var i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInstanceValid(content) || content.IsQueuedForDeletion()) return;
        var chrome = Size.Y - _scroll.Size.Y;
        var want = chrome + content.GetCombinedMinimumSize().Y + 32;
        Size = new Vector2(StartSize.X, Math.Clamp(want, 160, StartSize.Y));
        var area = GetParentAreaSize();
        Position = new Vector2(Math.Clamp(Position.X, 10, Math.Max(10, area.X - Size.X - 10)), Math.Clamp(Position.Y, 10, Math.Max(10, area.Y - Size.Y - 10)));
    }

    public void Close()
    {
        Closed?.Invoke();
        QueueFree();
    }
}

/// <summary>What goes into help windows: language features, functions and the game itself.</summary>
public static class Help
{
    public static LessonDef? LessonFor(App app, int tier) => app.Content.Lessons.FirstOrDefault(l => l.Tier == tier);

    /// <summary>A documented function in <paramref name="env"/> (the golems' functions by default).</summary>
    public static BuiltinDef? Builtin(string name, GlyphEnvironment? env = null) =>
        (env ?? GolemApi.Environment).Builtins.FirstOrDefault(b => b.Name == name && b.Signature.Length > 0);

    private static bool IsSpell(string name) => name == "mana" || Spells.All.Any(s => s.Id == name);

    /// <summary>Open the right window for a word from code: a function or a keyword's feature.</summary>
    public static bool Word(App app, string word, GlyphEnvironment? env = null)
    {
        if (Builtin(word, env) is { } b)
        {
            Function(app, b, env);
            return true;
        }
        if (GlyphEditor.KeywordTier(word) is { } tier && LessonFor(app, tier) is { } lesson)
        {
            Topic(app, lesson);
            return true;
        }
        return false;
    }

    /// <summary>One line about a word for tooltips and the editor's info strip, or null.</summary>
    public static string? Brief(App app, string word, int tier, GlyphEnvironment? env = null)
    {
        if (Builtin(word, env) is { } b)
        {
            var locked = b.Tier > tier ? $"  (not learned yet: {Where(app, b.Tier)})" : "";
            return $"{b.Signature}{(b.IsAction ? "  · action" : "")}\n{b.Doc}{locked}";
        }
        if (GlyphEditor.KeywordTier(word) is { } t)
        {
            var locked = t > tier ? "  (not learned yet)" : "";
            return $"{word}  · keyword\nPart of {Where(app, t)}{locked}. Click More for the explanation.";
        }
        return null;
    }

    private static string Where(App app, int tier) => LessonFor(app, tier) is { } l ? $"Codex {l.Tier}: {l.Title}" : Tiers.Describe(tier);

    private static Control Tip(string text)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", Ui.Box(new Color(Palette.Accent, 0.08f), Palette.Accent.Darkened(0.45f), 10, 10));
        card.AddChild(Ui.Row(10, Ui.Label("TIP", Palette.Accent, 11), Ui.Para(text, Palette.Text, 13)));
        return card;
    }

    /// <summary>A language feature: its short explanation once learned, or what it costs to learn.</summary>
    public static HelpWindow Topic(App app, LessonDef lesson, bool justLearned = false)
    {
        var p = app.Progress;
        var col = Ui.Column(10);
        var learned = p.IsLearned(lesson);
        var tag = justLearned ? "NEW FEATURE LEARNED" : learned ? $"CODEX {lesson.Tier} · LEARNED" : $"CODEX {lesson.Tier} · {lesson.Cost} GOLD";
        col.AddChild(Ui.Label(tag, justLearned || learned ? Palette.Ok : Palette.Accent, 11));
        col.AddChild(Ui.Heading(lesson.Title, 26));
        col.AddChild(Ui.Para(lesson.Summary, Palette.Muted, 14));

        if (!learned)
        {
            col.AddChild(Ui.Para("Teaches your golems " + CodexScreen.Unlocks(lesson) + ".", Palette.Text, 14));
            col.AddChild(LearnButton(app, lesson));
        }
        else
        {
            if (justLearned) col.AddChild(Ui.Para($"Your golems now understand {CodexScreen.Unlocks(lesson)}. Here's how it works:", Palette.Ok, 14));
            foreach (var page in lesson.Pages)
            {
                col.AddChild(Ui.Heading(page.Title, 18, Palette.Accent));
                foreach (var block in page.Blocks)
                {
                    if (block.Text is { } text) col.AddChild(Ui.Para(text, Palette.Text, 14));
                    if (block.Code is { Count: > 0 } code) col.AddChild(GlyphEditor.Snippet(string.Join("\n", code)));
                    if (block.Tip is { } tip) col.AddChild(Tip(tip));
                }
            }
            col.AddChild(new HSeparator());
            var done = p.IsCompleted(lesson);
            col.AddChild(Ui.Label("OPTIONAL CHALLENGE", Palette.Muted, 11));
            col.AddChild(Ui.Para(lesson.Challenge.Brief, Palette.Text, 13));
            var go = Ui.Button(done ? "Replay the challenge  ✓" : $"Try it  ·  +{lesson.Reward} gold bonus", primary: !done);
            go.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            col.AddChild(go);
            go.Pressed += () =>
            {
                app.Audio.Play(Sfx.Click);
                app.CloseWindow(Key(lesson));
                app.GoLesson(lesson);
            };
        }
        return app.OpenWindow(Key(lesson), lesson.Title, col, new Vector2(460, 520));
    }

    private static string Key(LessonDef lesson) => "topic/" + lesson.Id;

    /// <summary>The Learn button, or why it can't be pressed yet.</summary>
    public static Control LearnButton(App app, LessonDef lesson)
    {
        var p = app.Progress;
        var status = p.LearnStatus(lesson);
        var b = Ui.Button($"Learn {lesson.Title}  ·  {lesson.Cost} gold", primary: status == NodeStatus.Available);
        b.CustomMinimumSize = new Vector2(240, 42);
        b.Disabled = status != NodeStatus.Available;
        b.Pressed += () => app.Learn(lesson);
        var why = status switch
        {
            NodeStatus.TooExpensive => $"You have {app.Profile.Gold} of {lesson.Cost} gold. Delve to bring home more.",
            NodeStatus.Locked => $"Learn {p.NextToLearn?.Title} first: features build on each other.",
            _ => "",
        };
        var col = Ui.Column(4, b);
        b.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        if (why.Length > 0) col.AddChild(Ui.Para(why, Palette.Muted, 13));
        return col;
    }

    /// <summary>A built-in function: what it does, whether you have it, and an example from the Codex.</summary>
    public static HelpWindow Function(App app, BuiltinDef b, GlyphEnvironment? env = null)
    {
        var p = app.Progress;
        var col = Ui.Column(10);
        var sig = Ui.Para(b.Signature, Palette.Accent, 17);
        sig.AddThemeFontOverride("font", Ui.Mono);
        col.AddChild(sig);
        var who = env is null || env == GolemApi.Environment ? "golem's" : "workshop's";
        col.AddChild(Ui.Label(b.IsAction ? $"ACTION · ends the {who} turn" : "INSTANT · no time passes", b.IsAction ? Palette.Danger : Palette.Muted, 11));
        col.AddChild(Ui.Para(b.Doc, Palette.Text, 14));

        var lesson = LessonFor(app, b.Tier);
        if (IsSpell(b.Name))
        {
            var owned = !Spells.LockedFor(p.Loadout()).ContainsKey(b.Name);
            col.AddChild(Ui.Para(owned ? "A spell you know. It costs mana: check mana() first." : "A spell: learn it in the Arcana tree at the Arcane Tower.", owned ? Palette.Ok : Palette.Muted, 13));
        }
        else if (b.Tier <= p.KnownTier)
        {
            col.AddChild(Ui.Label("✓ Your golems know this.", Palette.Ok, 13));
        }
        else if (lesson is not null)
        {
            col.AddChild(Ui.Para($"Comes with Codex {lesson.Tier}: {lesson.Title}.", Palette.Muted, 13));
        }

        var pages = env is null || env == GolemApi.Environment
            ? app.Content.Lessons.SelectMany(l => l.Pages)
            : app.Content.Workshops.Where(w => Core.Village.Workshops.KindOf(w).Environment == env).SelectMany(w => w.Pages);
        var example = pages
            .SelectMany(pg => pg.Blocks)
            .Select(bl => bl.Code)
            .FirstOrDefault(code => code is { Count: > 0 } && code.Any(line => line.Contains(b.Name + "(", StringComparison.Ordinal)));
        if (example is not null)
        {
            col.AddChild(Ui.Label("EXAMPLE", Palette.Muted, 11));
            col.AddChild(GlyphEditor.Snippet(string.Join("\n", example), env));
        }
        if (lesson is not null)
        {
            var about = Ui.Button($"About {lesson.Title} →");
            about.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            about.Pressed += () => Topic(app, lesson);
            col.AddChild(about);
        }
        return app.OpenWindow("fn/" + b.Name, b.Name + "()", col, new Vector2(420, 360));
    }

    /// <summary>How a workshop works: its pages and every function it has.</summary>
    public static HelpWindow Workshop(App app, WorkshopDef w)
    {
        var env = Core.Village.Workshops.KindOf(w).Environment;
        var col = Ui.Column(10, Ui.Label("WORKSHOP", Palette.Accent, 11), Ui.Heading(w.Name, 26), Ui.Para(w.Summary, Palette.Muted, 14));
        foreach (var page in w.Pages)
        {
            col.AddChild(Ui.Heading(page.Title, 18, Palette.Accent));
            foreach (var block in page.Blocks)
            {
                if (block.Text is { } text) col.AddChild(Ui.Para(text, Palette.Text, 14));
                if (block.Code is { Count: > 0 } code) col.AddChild(GlyphEditor.Snippet(string.Join("\n", code), env));
                if (block.Tip is { } tip) col.AddChild(Tip(tip));
            }
        }
        col.AddChild(Ui.Heading("Functions", 18, Palette.Accent));
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 6);
        flow.AddThemeConstantOverride("v_separation", 6);
        foreach (var b in env.Builtins.Where(b => b.Signature.Length > 0 && (b.Tier <= Tiers.Calls || Stdlib.Functions.All(s => s.Name != b.Name))))
        {
            var chip = Ui.Button(b.Name + "()", b.Doc);
            chip.AddThemeFontOverride("font", Ui.Mono);
            chip.AddThemeColorOverride("font_color", Palette.Accent);
            var def = b;
            chip.Pressed += () => Function(app, def, env);
            flow.AddChild(chip);
        }
        col.AddChild(flow);
        return app.OpenWindow("workshop/" + w.Id, w.Name, col, new Vector2(460, 540));
    }

    /// <summary>Every function as a clickable chip, greyed out until it is learned.</summary>
    public static HelpWindow Functions(App app)
    {
        var p = app.Progress;
        var locked = Spells.LockedFor(p.Loadout());
        var col = Ui.Column(10, Ui.Para("Click a name to see what it does and an example.", Palette.Muted, 13));
        foreach (var group in GolemApi.Environment.Builtins.Where(b => b.Signature.Length > 0).GroupBy(b => IsSpell(b.Name) ? -1 : b.Tier).OrderBy(g => g.Key < 0 ? int.MaxValue : g.Key))
        {
            var title = group.Key < 0 ? "Spells" : LessonFor(app, group.Key)?.Title ?? Tiers.Name(group.Key);
            col.AddChild(Ui.Heading(title, 16, Palette.Accent));
            var flow = new HFlowContainer();
            flow.AddThemeConstantOverride("h_separation", 6);
            flow.AddThemeConstantOverride("v_separation", 6);
            foreach (var b in group)
            {
                var known = group.Key < 0 ? !locked.ContainsKey(b.Name) : b.Tier <= p.KnownTier;
                var chip = Ui.Button(b.Name + "()", b.Doc);
                chip.AddThemeFontOverride("font", Ui.Mono);
                chip.AddThemeColorOverride("font_color", known ? Palette.Accent : Palette.Muted.Darkened(0.25f));
                var def = b;
                chip.Pressed += () => Function(app, def);
                flow.AddChild(chip);
            }
            col.AddChild(flow);
        }
        return app.OpenWindow("functions", "All functions", col, new Vector2(460, 520));
    }

    /// <summary>The game loop in a nutshell.</summary>
    public static HelpWindow HowToPlay(App app)
    {
        var col = Ui.Column(10,
            Ui.Heading("How Delvework works", 24),
            Ui.Para("1.  Delve. Your golems run their code in the mines and carry gold home, plus stone, iron ore and timber they mine() from veins in the walls. You can watch, pause and scrub every run.", Palette.Text, 14),
            Ui.Para("2.  Learn. Spend gold in the Library on new language features: loops, decisions, variables and more. Each one pops up a short explanation.", Palette.Text, 14),
            Ui.Para("3.  Improve your code. Better code opens more chests, mines more and loses fewer golems, so every delve brings more home.", Palette.Text, 14),
            Ui.Para("4.  Build. Buildings cost gold and materials. The Woodcutter, Quarry and Fields deliver more after every delve.", Palette.Text, 14),
            Ui.Para("5.  Craft. The Smelter turns ore into iron and the Bakery wheat into bread, but only as well as the script you write for them. They work one shift after every delve.", Palette.Text, 14),
            Tip("Click a name in your code and the strip under the editor explains it. Ctrl+click opens its window. Every feature also has an optional challenge that pays a gold bonus."));
        return app.OpenWindow("howto", "How to play", col, new Vector2(440, 400));
    }

    /// <summary>A "? Help" menu: every feature, all functions, how to play.</summary>
    public static MenuButton Menu(App app)
    {
        var menu = new MenuButton { Text = "?  Help", FocusMode = Control.FocusModeEnum.None, Flat = false, TooltipText = "Open small help windows while you play" };
        menu.AddThemeStyleboxOverride("normal", Ui.Box(Palette.Panel2, Palette.Border, 9, 7));
        menu.AddThemeStyleboxOverride("hover", Ui.Box(Palette.Panel2.Lightened(0.08f), Palette.Border.Lightened(0.2f), 9, 7));
        menu.AddThemeStyleboxOverride("pressed", Ui.Box(Palette.Panel, Palette.Accent, 9, 7));
        var popup = menu.GetPopup();
        menu.AboutToPopup += () =>
        {
            popup.Clear();
            var p = app.Progress;
            popup.AddItem("How to play", 0);
            popup.AddItem("All functions", 1);
            popup.AddSeparator("Language features");
            for (var i = 0; i < p.Lessons.Count; i++)
            {
                var l = p.Lessons[i];
                var mark = p.IsLearned(l) ? "✓" : "🔒";
                var cost = p.IsLearned(l) ? "" : $"  ({l.Cost} gold)";
                popup.AddItem($"{mark}  {l.Title}{cost}", 100 + i);
            }
        };
        popup.IdPressed += id =>
        {
            app.Audio.Play(Sfx.Page);
            if (id == 0) HowToPlay(app);
            else if (id == 1) Functions(app);
            else Topic(app, app.Progress.Lessons[(int)id - 100]);
        };
        return menu;
    }
}
