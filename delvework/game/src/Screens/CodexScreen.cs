using Delvework.Core.Content;
using Delvework.Core.Glyph;
using Delvework.Core.Progress;
using Delvework.Core.Sim;
using Delvework.Game.Audio;
using Godot;

namespace Delvework.Game.Screens;

/// <summary>
/// The Library: one Codex entry per language feature. Buy a feature with delve gold to unlock it
/// for every golem; each has a few explanation pages and an optional challenge for bonus gold.
/// </summary>
public partial class CodexScreen : Control
{
    public required App App { get; init; }
    public required LessonDef Initial { get; init; }
    public int InitialPage { get; init; }

    private VBoxContainer _list = null!, _content = null!;
    private Label _wallet = null!;
    private ScrollContainer _scroll = null!;
    private LessonDef? _lesson;
    private int _page;

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
        _wallet = Ui.Label("", Palette.Accent, 16);
        root.AddChild(Ui.Row(14, back, Ui.Heading("The Library", 28, Palette.Accent),
            Ui.Label("Spend delve gold on new language features. Every golem can use what you learn.", Palette.Muted, 13),
            Ui.Spacer(), _wallet, Help.Menu(App)));

        var split = Ui.Row(16);
        split.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(split);

        _list = Ui.Column(6);
        var listScroll = new ScrollContainer { CustomMinimumSize = new Vector2(330, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        listScroll.AddChild(_list);
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var listCard = Ui.Card(listScroll);
        listCard.SizeFlagsVertical = SizeFlags.ExpandFill;
        split.AddChild(listCard);

        _content = Ui.Column(14);
        _content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        var pad = Ui.Pad(_content, 10);
        pad.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(pad);
        var contentCard = Ui.Card(_scroll);
        contentCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        contentCard.SizeFlagsVertical = SizeFlags.ExpandFill;
        split.AddChild(contentCard);

        Open(Initial, InitialPage);
        App.Learned += OnLearned;
    }

    public override void _ExitTree() => App.Learned -= OnLearned;

    private void OnLearned()
    {
        if (_lesson is { } l) Open(l, 0);
        else ShowReference();
    }

    private void RebuildList()
    {
        foreach (var c in _list.GetChildren()) c.QueueFree();
        var p = App.Progress;
        _wallet.Text = $"{App.Profile.Gold} gold";
        foreach (var lesson in p.Lessons)
        {
            var status = p.LearnStatus(lesson);
            var done = p.IsCompleted(lesson);
            var (mark, suffix) = status switch
            {
                NodeStatus.Owned => (done ? "✓" : "▶", done ? "" : "   · challenge"),
                NodeStatus.Available => ("✦", $"   · learn for {lesson.Cost} gold"),
                _ => ("🔒", $"   · {lesson.Cost} gold"),
            };
            var b = new Button
            {
                Text = $"{mark}  {lesson.Tier}. {lesson.Title}{suffix}",
                Alignment = HorizontalAlignment.Left,
                FocusMode = FocusModeEnum.None,
                TooltipText = lesson.Summary,
                CustomMinimumSize = new Vector2(0, 40),
            };
            if (lesson == _lesson) b.AddThemeStyleboxOverride("normal", Ui.Box(Palette.Panel2, Palette.Accent, 9, 7));
            b.AddThemeColorOverride("font_color", status switch
            {
                NodeStatus.Owned => done ? Palette.Ok : Palette.Text,
                NodeStatus.Available => Palette.Accent,
                _ => Palette.Muted.Darkened(0.2f),
            });
            var l = lesson;
            b.Pressed += () =>
            {
                App.Audio.Play(Sfx.Page);
                Open(l, 0);
            };
            _list.AddChild(b);
        }
        _list.AddChild(new HSeparator());
        var reference = new Button
        {
            Text = "📖  Reference: everything you know",
            Alignment = HorizontalAlignment.Left,
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 40),
        };
        if (_lesson is null) reference.AddThemeStyleboxOverride("normal", Ui.Box(Palette.Panel2, Palette.Accent, 9, 7));
        reference.Pressed += () =>
        {
            App.Audio.Play(Sfx.Page);
            ShowReference();
        };
        _list.AddChild(reference);
        var locked = p.Lessons.Count(l => !p.IsLearned(l));
        _list.AddChild(Ui.Para(locked == 0 ? "You know the whole language. Well done, Runewright." : $"{locked} feature{(locked == 1 ? "" : "s")} left to learn. Challenges are optional and pay a gold bonus.", Palette.Muted, 12));
    }

    private void Clear()
    {
        foreach (var c in _content.GetChildren()) c.QueueFree();
        _scroll.ScrollVertical = 0;
    }

    public void Open(LessonDef lesson, int page)
    {
        _lesson = lesson;
        _page = Math.Clamp(page, 0, lesson.Pages.Count);
        RebuildList();
        Clear();

        var done = App.Progress.IsCompleted(lesson);
        var learned = App.Progress.IsLearned(lesson);
        _content.AddChild(Ui.Label($"CODEX {lesson.Tier}{(done ? "  ·  CHALLENGE DONE" : learned ? "  ·  LEARNED" : "")}", done || learned ? Palette.Ok : Palette.Accent, 12));
        _content.AddChild(Ui.Heading(lesson.Title, 34));
        _content.AddChild(Ui.Para(lesson.Summary, Palette.Muted, 16));
        if (!learned)
        {
            ShowLearnCard(lesson);
            return;
        }
        var key = $"{lesson.Id}/{_page}";
        if (!App.Profile.Read.Contains(key)) App.Profile.Read.Add(key);

        var dots = Ui.Row(6);
        for (var i = 0; i <= lesson.Pages.Count; i++)
        {
            var title = i < lesson.Pages.Count ? lesson.Pages[i].Title : "Optional challenge";
            var b = Ui.Button($"{i + 1}. {title}");
            b.ToggleMode = true;
            b.ButtonPressed = i == _page;
            var index = i;
            b.Pressed += () =>
            {
                App.Audio.Play(Sfx.Page);
                Open(lesson, index);
            };
            dots.AddChild(b);
        }
        _content.AddChild(dots);
        _content.AddChild(new HSeparator());

        if (_page < lesson.Pages.Count) ShowPage(lesson.Pages[_page]);
        else ShowChallenge(lesson);

        var nav = Ui.Row(8);
        if (_page > 0)
        {
            var prev = Ui.Button("← Back");
            prev.Pressed += () =>
            {
                App.Audio.Play(Sfx.Page);
                Open(lesson, _page - 1);
            };
            nav.AddChild(prev);
        }
        nav.AddChild(Ui.Spacer());
        if (_page < lesson.Pages.Count)
        {
            var next = Ui.Button(_page == lesson.Pages.Count - 1 ? "Optional challenge →" : "Next page →", primary: true);
            next.Pressed += () =>
            {
                App.Audio.Play(Sfx.Page);
                Open(lesson, _page + 1);
            };
            nav.AddChild(next);
        }
        _content.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        _content.AddChild(nav);
    }

    private void ShowLearnCard(LessonDef lesson)
    {
        var p = App.Progress;
        _content.AddChild(new HSeparator());
        _content.AddChild(Ui.Heading("What you get", 22, Palette.Accent));
        _content.AddChild(Ui.Para($"Your golems will understand {Unlocks(lesson)}.", Palette.Text, 16));
        if (lesson.Pages.Count > 0 && lesson.Pages[0].Blocks.FirstOrDefault(b => b.Code is { Count: > 0 })?.Code is { } code)
        {
            _content.AddChild(Ui.Label("A TASTE OF IT", Palette.Muted, 12));
            _content.AddChild(GlyphEditor.Snippet(string.Join("\n", code)));
        }
        _content.AddChild(Ui.Para($"Learning it opens {lesson.Pages.Count} short pages that explain it, and an optional challenge worth {lesson.Reward} bonus gold.", Palette.Muted, 14));
        var card = Ui.Card(Help.LearnButton(App, lesson));
        card.AddThemeStyleboxOverride("panel", Ui.Box(Palette.Panel2, p.LearnStatus(lesson) == NodeStatus.Available ? Palette.Accent : Palette.Border, 10, 14));
        _content.AddChild(card);
    }

    private void ShowPage(LessonPage page)
    {
        _content.AddChild(Ui.Heading(page.Title, 24, Palette.Accent));
        foreach (var block in page.Blocks)
        {
            if (block.Text is { } text) _content.AddChild(Ui.Para(text, Palette.Text, 16));
            if (block.Code is { Count: > 0 } code) _content.AddChild(GlyphEditor.Snippet(string.Join("\n", code)));
            if (block.Tip is { } tip)
            {
                var card = new PanelContainer();
                card.AddThemeStyleboxOverride("panel", Ui.Box(new Color(Palette.Accent, 0.08f), Palette.Accent.Darkened(0.45f), 10, 12));
                card.AddChild(Ui.Row(10, Ui.Label("TIP", Palette.Accent, 12), Ui.Para(tip, Palette.Text, 14)));
                _content.AddChild(card);
            }
        }
    }

    private void ShowChallenge(LessonDef lesson)
    {
        var c = lesson.Challenge;
        var p = App.Progress;
        _content.AddChild(Ui.Heading("Optional challenge", 24, Palette.Accent));
        _content.AddChild(Ui.Para(c.Brief, Palette.Text, 16));
        var goals = Ui.Column(6, Ui.Label("TO PASS", Palette.Muted, 12));
        foreach (var g in Challenge.Goals(lesson)) goals.AddChild(Ui.Label("○  " + g.Text, Palette.Text, 15));
        var goalCard = Ui.Card(goals);
        goalCard.AddThemeStyleboxOverride("panel", Ui.Box(Palette.Panel2, Palette.Border, 10, 14));
        _content.AddChild(goalCard);

        var party = string.Join(" and ", c.Party.Select(id => p.Content.Chassis[id].Name));
        _content.AddChild(Ui.Para($"Your golem{(c.Party.Count > 1 ? "s" : "")}: {party}. Challenges use standard golems without equipment or spells, so only your code counts.", Palette.Muted, 13));
        _content.AddChild(Ui.Label(p.IsCompleted(lesson) ? "Already completed: you can replay it any time." : $"Bonus: {lesson.Reward} gold the first time you pass", Palette.Accent, 15));

        var start = Ui.Button(p.IsCompleted(lesson) ? "Replay the challenge" : "Start the challenge", primary: true);
        start.CustomMinimumSize = new Vector2(240, 44);
        start.Pressed += () =>
        {
            App.Audio.Play(Sfx.Click);
            App.GoLesson(lesson);
        };
        _content.AddChild(start);
    }

    /// <summary>What a lesson makes available: its keywords plus the built-in functions of its tier.</summary>
    public static string Unlocks(LessonDef lesson)
    {
        var words = lesson.Tier switch
        {
            Tiers.Calls => "commands like move() and explore()",
            Tiers.Loops => "while loops",
            Tiers.Conditions => "if / elif / else, comparisons, and / or / not",
            Tiers.Variables => "variables, maths and fields like enemy.hp",
            Tiers.Functions => "def, parameters and return",
            Tiers.Lists => "lists, for loops and [ ] indexing",
            Tiers.Events => "event handlers with on",
            Tiers.Dicts => "dictionaries and in",
            _ => "",
        };
        var fns = GolemApi.Environment.Builtins.Where(b => b.Tier == lesson.Tier && b.Tier > Tiers.Calls && b.Signature.Length > 0).Select(b => b.Name + "()").ToList();
        if (fns.Count > 0) words += (words.Length > 0 ? ", plus " : "") + string.Join(", ", fns);
        return words;
    }

    public void ShowReference()
    {
        _lesson = null;
        RebuildList();
        Clear();
        var p = App.Progress;
        var tier = p.KnownTier;
        var locked = Spells.LockedFor(p.Loadout());
        _content.AddChild(Ui.Label("REFERENCE", Palette.Accent, 12));
        _content.AddChild(Ui.Heading("Everything your golems know", 30));
        _content.AddChild(Ui.Para("Every function and keyword. Click one to open a small window with an example. Actions (marked) end the golem's turn and take a few ticks; everything else is instant.", Palette.Muted, 14));

        void Group(string title, IEnumerable<BuiltinDef> defs)
        {
            var list = defs.ToList();
            if (list.Count == 0) return;
            _content.AddChild(Ui.Heading(title, 20, Palette.Accent));
            foreach (var b in list)
            {
                var known = b.Tier <= tier && !locked.ContainsKey(b.Name);
                var sig = Link(b.Signature, known ? Palette.Accent : Palette.Muted.Darkened(0.2f));
                var def = b;
                sig.Pressed += () => Help.Function(App, def);
                var tag = b.IsAction ? Ui.Label("action", Palette.Danger, 11) : Ui.Label("", Palette.Muted, 11);
                var where = known ? "" : locked.ContainsKey(b.Name) ? "  (spell: learn it in the Arcana tree)" : $"  (learn it in the Library: Codex {b.Tier}: {Tiers.Name(b.Tier)})";
                var doc = Ui.Para(b.Doc + where, known ? Palette.Text : Palette.Muted.Darkened(0.2f), 13);
                _content.AddChild(Ui.Column(2, Ui.Row(8, sig, tag), doc));
            }
        }

        var all = GolemApi.Environment.Builtins.Where(b => b.Signature.Length > 0).OrderBy(b => b.Tier).ToList();
        var spells = new HashSet<string>(Spells.All.Select(s => s.Id), StringComparer.Ordinal);
        Group("Actions", all.Where(b => b.IsAction && !spells.Contains(b.Name)));
        Group("Looking around", all.Where(b => !b.IsAction && !spells.Contains(b.Name) && b.Name != "mana" && !IsUtility(b.Name)));
        Group("Utilities", all.Where(b => !b.IsAction && IsUtility(b.Name)));
        Group("Spells", all.Where(b => spells.Contains(b.Name) || b.Name == "mana"));

        _content.AddChild(Ui.Heading("Keywords", 20, Palette.Accent));
        (int Tier, string Code, string What)[] keywords =
        [
            (Tiers.Loops, "while condition:", "Repeat the indented lines as long as the condition is true. while True: repeats forever."),
            (Tiers.Loops, "break / continue", "Leave the loop / skip to the next round."),
            (Tiers.Conditions, "if / elif / else:", "Run lines only when something is true."),
            (Tiers.Conditions, "== != < > <= >=  and or not", "Compare values and combine conditions."),
            (Tiers.Variables, "name = value", "Store a value under a name. name += 1 adds to it."),
            (Tiers.Functions, "def name(param):  return value", "Define your own function."),
            (Tiers.Lists, "[a, b, c]   for x in items:", "A list of values and a loop over it."),
            (Tiers.Events, "on see(enemy):", "Run a handler when something happens: see, hurt, low_hp, status, signal."),
            (Tiers.Dicts, "{\"key\": value}   key in d", "Look values up by key."),
        ];
        foreach (var (t, code, what) in keywords)
        {
            var known = t <= tier;
            var sig = Link(code, known ? new Color("c792ea") : Palette.Muted.Darkened(0.2f));
            if (Help.LessonFor(App, t) is { } lesson) sig.Pressed += () => Help.Topic(App, lesson);
            _content.AddChild(Ui.Column(2, sig, Ui.Para(what + (known ? "" : $"  (learn it in the Library: Codex {t}: {Tiers.Name(t)})"), known ? Palette.Text : Palette.Muted.Darkened(0.2f), 13)));
        }
    }

    private static LinkButton Link(string text, Color color)
    {
        var l = new LinkButton { Text = text, Underline = LinkButton.UnderlineMode.OnHover, FocusMode = FocusModeEnum.None, TooltipText = "Open a window about it" };
        l.AddThemeFontOverride("font", Ui.Mono);
        l.AddThemeFontSizeOverride("font_size", 15);
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color" }) l.AddThemeColorOverride(state, state == "font_hover_color" ? color.Lightened(0.2f) : color);
        l.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        return l;
    }

    private static bool IsUtility(string name) => name is "print" or "len" or "range" or "abs" or "min" or "max" or "str" or "int" or "float" or "round" or "signal" or "mark" or "random" or "sorted" or "append";
}
