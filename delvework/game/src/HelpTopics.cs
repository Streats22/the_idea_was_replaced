using Delvework.Core.Content;
using Delvework.Core.Glyph;
using Delvework.Core.Sim;
using Delvework.Game.Audio;
using Godot;

namespace Delvework.Game;

/// <summary>What goes into help windows: the Grimoire of runes, functions and the game itself.</summary>
public static class Help
{
    public static LessonDef? LessonFor(App app, int tier) => app.Content.Lessons.FirstOrDefault(l => l.Tier == tier);

    /// <summary>A documented function in <paramref name="env"/> (the golems' functions by default).</summary>
    public static BuiltinDef? Builtin(string name, GlyphEnvironment? env = null) =>
        (env ?? GolemApi.Environment).Builtins.FirstOrDefault(b => b.Name == name && b.Signature.Length > 0);

    private static bool IsSpell(string name) => name == "mana" || Spells.All.Any(s => s.Id == name);

    /// <summary>What a feature lets golems write, in a few words.</summary>
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

    /// <summary>Where the tablet of a feature not yet learned can be found.</summary>
    public static string WhereToFind(App app, LessonDef lesson)
    {
        var p = app.Progress;
        if (p.NextToLearn == lesson && p.TabletSite is { } site) return $"Its tablet lies in {site.Name}. Delve there and carry it home.";
        if (p.NextToLearn == lesson) return "Its tablet lies deeper in the mines than you can reach yet.";
        return "It lies deeper in the mines. Find the runes before it first: each one builds on the last.";
    }

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
            var locked = b.Tier > tier ? $"  (needs {Where(app, b.Tier)}, not found yet)" : "";
            return $"{b.Signature}{(b.IsAction ? "  · action" : "")}\n{b.Doc}{locked}";
        }
        if (GlyphEditor.KeywordTier(word) is { } t)
        {
            var locked = t > tier ? "  (not found yet)" : "";
            return $"{word}  · keyword\nPart of {Where(app, t)}{locked}. Click More for the explanation.";
        }
        return null;
    }

    private static string Where(App app, int tier) => LessonFor(app, tier) is { } l ? $"the {l.Title} rune" : Tiers.Describe(tier);

    private static Control Tip(string text)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", Ui.Box(new Color(Palette.Accent, 0.07f), Palette.BrassDim, 3, 10));
        card.AddChild(Ui.Row(10, Ui.Label("TIP", Palette.Accent, 11), Ui.Para(text, Palette.Text, 13)));
        return card;
    }

    /// <summary>A Grimoire page: how a found rune's feature works, or a hint where its tablet lies.</summary>
    public static HelpWindow Topic(App app, LessonDef lesson)
    {
        var p = app.Progress;
        var col = Ui.Column(10);
        var learned = p.IsLearned(lesson);
        col.AddChild(Ui.Label(learned ? $"RUNE {lesson.Tier} · FOUND" : $"RUNE {lesson.Tier} · NOT FOUND YET", learned ? Palette.Rune : Palette.Muted, 11));
        col.AddChild(Ui.Heading(learned ? lesson.Title : "An undiscovered rune", 26, learned ? Palette.BrassLight : Palette.Muted));
        if (!learned)
        {
            col.AddChild(Ui.Para($"Somewhere in the mines lies a rune tablet that teaches golems {Unlocks(lesson)}.", Palette.Text, 14));
            col.AddChild(Ui.Para(WhereToFind(app, lesson), Palette.Accent, 14));
            return app.OpenWindow(Key(lesson), "Grimoire", col, new Vector2(440, 300));
        }
        col.AddChild(Ui.Para(lesson.Summary, Palette.Muted, 14));
        col.AddChild(Ui.Para($"Your golems understand {Unlocks(lesson)}.", Palette.Ok, 13));
        foreach (var page in lesson.Pages)
        {
            col.AddChild(Ui.Banner(page.Title, 13));
            foreach (var block in page.Blocks)
            {
                if (block.Text is { } text) col.AddChild(Ui.Para(text, Palette.Text, 14));
                if (block.Code is { Count: > 0 } code) col.AddChild(GlyphEditor.Snippet(string.Join("\n", code)));
                if (block.Tip is { } tip) col.AddChild(Tip(tip));
            }
        }
        return app.OpenWindow(Key(lesson), lesson.Title, col, new Vector2(470, 540));
    }

    private static string Key(LessonDef lesson) => "topic/" + lesson.Id;

    /// <summary>The Grimoire: every rune, found ones to read, the rest greyed out with a hint.</summary>
    public static HelpWindow Grimoire(App app)
    {
        var p = app.Progress;
        var col = Ui.Column(8,
            Ui.Para($"The runes your golems have learned, {p.LearnedCount} of {p.Lessons.Count}. New ones lie on the mine floors: when a golem carries a tablet home, the whole party learns it.", Palette.Muted, 13));
        foreach (var lesson in p.Lessons)
        {
            var found = p.IsLearned(lesson);
            var name = Ui.Label(found ? lesson.Title : "Undiscovered rune", found ? Palette.BrassLight : Palette.Muted.Darkened(0.2f), 16);
            name.AddThemeFontOverride("font", Ui.Serif);
            var what = Ui.Para(found ? Unlocks(lesson) : lesson == p.NextToLearn ? WhereToFind(app, lesson) : "Deeper in the mines.", found ? Palette.Muted : Palette.Muted.Darkened(0.3f), 12);
            var glyph = new RuneMark { Tier = lesson.Tier, Found = found, CustomMinimumSize = new Vector2(34, 34) };
            var info = Ui.Column(0, name, what);
            info.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var entry = new Button { FocusMode = Control.FocusModeEnum.None, Flat = true, Disabled = !found && lesson != p.NextToLearn, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            entry.AddThemeStyleboxOverride("hover", Ui.Box(new Color(Palette.Accent, 0.08f), Palette.BrassDim, 3, 6));
            var row = Ui.Row(12, glyph, info);
            row.MouseFilter = Control.MouseFilterEnum.Ignore;
            info.MouseFilter = Control.MouseFilterEnum.Ignore;
            foreach (var c in info.GetChildren().OfType<Control>()) c.MouseFilter = Control.MouseFilterEnum.Ignore;
            var margin = Ui.Pad(row, 6);
            margin.MouseFilter = Control.MouseFilterEnum.Ignore;
            margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            entry.AddChild(margin);
            entry.CustomMinimumSize = new Vector2(0, 58);
            var def = lesson;
            entry.Pressed += () => Topic(app, def);
            col.AddChild(entry);
        }
        var functions = Ui.Button("All functions");
        functions.Pressed += () => Functions(app);
        col.AddChild(Ui.Row(8, Ui.Spacer(), functions));
        return app.OpenWindow("grimoire", "Grimoire", col, new Vector2(440, 620));
    }

    /// <summary>A built-in function: what it does, whether you have it, and an example from the Grimoire.</summary>
    public static HelpWindow Function(App app, BuiltinDef b, GlyphEnvironment? env = null)
    {
        var p = app.Progress;
        var col = Ui.Column(10);
        var sig = Ui.Para(b.Signature, Palette.Accent, 17);
        sig.AddThemeFontOverride("font", Ui.Mono);
        col.AddChild(sig);
        var who = env is null || env == GolemApi.Environment ? "golem's" : "workshop's";
        col.AddChild(Ui.Label(b.IsAction ? $"ACTION · ENDS THE {who.ToUpperInvariant()} TURN" : "INSTANT · NO TIME PASSES", b.IsAction ? Palette.Danger : Palette.Muted, 11));
        col.AddChild(Ui.Para(b.Doc, Palette.Text, 14));

        var lesson = LessonFor(app, b.Tier);
        var known = b.Tier <= p.KnownTier;
        if (IsSpell(b.Name))
        {
            var owned = !Spells.LockedFor(p.Loadout()).ContainsKey(b.Name);
            col.AddChild(Ui.Para(owned ? "A spell you know. It costs mana: check mana() first." : "A spell: learn it in the Arcana tree at the Arcane Tower.", owned ? Palette.Ok : Palette.Muted, 13));
        }
        else if (known)
        {
            col.AddChild(Ui.Label("✓ Your golems know this.", Palette.Ok, 13));
        }
        else if (lesson is not null)
        {
            col.AddChild(Ui.Para($"Comes with a rune your golems haven't found yet. {WhereToFind(app, lesson)}", Palette.Muted, 13));
        }

        var pages = env is null || env == GolemApi.Environment
            ? app.Content.Lessons.Where(l => l.Tier <= p.KnownTier).SelectMany(l => l.Pages)
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
        if (lesson is not null && known && (env is null || env == GolemApi.Environment))
        {
            var about = Ui.Button($"{lesson.Title} in the Grimoire →");
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
        var col = Ui.Column(10, Ui.Label("WORKSHOP", Palette.Accent, 11), Ui.Heading(w.Name, 26, Palette.BrassLight), Ui.Para(w.Summary, Palette.Muted, 14));
        foreach (var page in w.Pages)
        {
            col.AddChild(Ui.Banner(page.Title, 13));
            foreach (var block in page.Blocks)
            {
                if (block.Text is { } text) col.AddChild(Ui.Para(text, Palette.Text, 14));
                if (block.Code is { Count: > 0 } code) col.AddChild(GlyphEditor.Snippet(string.Join("\n", code), env));
                if (block.Tip is { } tip) col.AddChild(Tip(tip));
            }
        }
        col.AddChild(Ui.Banner("Functions", 13));
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 6);
        flow.AddThemeConstantOverride("v_separation", 6);
        foreach (var b in env.Builtins.Where(b => b.Signature.Length > 0 && (b.Tier <= Tiers.Calls || Stdlib.Functions.All(s => s.Name != b.Name))))
        {
            flow.AddChild(Chip(b.Name, b.Doc, true, () => Function(app, b, env)));
        }
        col.AddChild(flow);
        return app.OpenWindow("workshop/" + w.Id, w.Name, col, new Vector2(460, 540));
    }

    private static Button Chip(string name, string doc, bool known, Action open)
    {
        var chip = Ui.Button(name + "()", doc);
        chip.AddThemeFontOverride("font", Ui.Mono);
        chip.AddThemeColorOverride("font_color", known ? Palette.Accent : Palette.Muted.Darkened(0.25f));
        chip.Pressed += open;
        return chip;
    }

    /// <summary>Every function as a clickable chip, greyed out until its rune is found.</summary>
    public static HelpWindow Functions(App app)
    {
        var p = app.Progress;
        var locked = Spells.LockedFor(p.Loadout());
        var col = Ui.Column(10, Ui.Para("Click a name to see what it does and an example.", Palette.Muted, 13));
        foreach (var group in GolemApi.Environment.Builtins.Where(b => b.Signature.Length > 0).GroupBy(b => IsSpell(b.Name) ? -1 : b.Tier).OrderBy(g => g.Key < 0 ? int.MaxValue : g.Key))
        {
            var known = group.Key < 0 || group.Key <= p.KnownTier;
            var title = group.Key < 0 ? "Spells" : known ? LessonFor(app, group.Key)?.Title ?? Tiers.Name(group.Key) : "Undiscovered rune";
            col.AddChild(Ui.Banner(title, 12, known ? null : Palette.Muted));
            var flow = new HFlowContainer();
            flow.AddThemeConstantOverride("h_separation", 6);
            flow.AddThemeConstantOverride("v_separation", 6);
            foreach (var b in group)
            {
                var have = group.Key < 0 ? !locked.ContainsKey(b.Name) : b.Tier <= p.KnownTier;
                flow.AddChild(Chip(b.Name, b.Doc, have, () => Function(app, b)));
            }
            col.AddChild(flow);
        }
        return app.OpenWindow("functions", "All functions", col, new Vector2(460, 520));
    }

    /// <summary>The game loop in a nutshell.</summary>
    public static HelpWindow HowToPlay(App app)
    {
        var col = Ui.Column(10,
            Ui.Heading("How Delvework works", 24, Palette.BrassLight),
            Ui.Para("1.  Delve. Your golems run their code in the mines and carry gold home, plus stone, iron ore and timber they mine() from veins in the walls. You can watch, pause and scrub every run.", Palette.Text, 14),
            Ui.Para("2.  Find runes. Rune tablets lie on the mine floors. When a golem carries one home, the whole party learns a new part of the language: loops, decisions, variables and more. Deeper floors hold the advanced runes.", Palette.Text, 14),
            Ui.Para("3.  Improve your code. Better code opens more chests, mines more and loses fewer golems, so every delve brings more home.", Palette.Text, 14),
            Ui.Para("4.  Build. Buildings cost gold and materials. The Woodcutter, Quarry and Fields deliver more after every delve.", Palette.Text, 14),
            Ui.Para("5.  Craft. The Smelter turns ore into iron and the Bakery wheat into bread, but only as well as the script you write for them. They work one shift after every delve.", Palette.Text, 14),
            Tip("Click a name in your code and the strip under the editor explains it. Ctrl+click opens its window. The Grimoire button above the editor holds every rune you've found."));
        return app.OpenWindow("howto", "How to play", col, new Vector2(440, 420));
    }

    /// <summary>A "? Help" menu: the Grimoire, all functions, how to play, and every rune.</summary>
    public static MenuButton Menu(App app)
    {
        var menu = new MenuButton { Text = "?  Help", FocusMode = Control.FocusModeEnum.None, Flat = false, TooltipText = "Open small help windows while you play" };
        var popup = menu.GetPopup();
        menu.AboutToPopup += () =>
        {
            popup.Clear();
            var p = app.Progress;
            popup.AddItem("Grimoire", 2);
            popup.AddItem("All functions", 1);
            popup.AddItem("How to play", 0);
            popup.AddSeparator("Runes");
            for (var i = 0; i < p.Lessons.Count; i++)
            {
                var l = p.Lessons[i];
                popup.AddItem(p.IsLearned(l) ? $"✦  {l.Title}" : "·  Undiscovered", 100 + i);
                popup.SetItemDisabled(popup.ItemCount - 1, !p.IsLearned(l) && l != p.NextToLearn);
            }
        };
        popup.IdPressed += id =>
        {
            app.Audio.Play(Sfx.Page);
            if (id == 0) HowToPlay(app);
            else if (id == 1) Functions(app);
            else if (id == 2) Grimoire(app);
            else Topic(app, app.Progress.Lessons[(int)id - 100]);
        };
        return menu;
    }
}

/// <summary>A small rune seal for Grimoire entries: glowing when found, a dim outline when not.</summary>
public partial class RuneMark : Control
{
    public int Tier { get; init; }
    public bool Found { get; init; }

    public override void _Draw()
    {
        var c = Size / 2;
        var r = Math.Min(Size.X, Size.Y) / 2 - 2;
        var color = Found ? Palette.Rune : Palette.Muted.Darkened(0.45f);
        DrawCircle(c, r, Found ? new Color(Palette.Rune, 0.1f) : new Color(0, 0, 0, 0.3f));
        DrawArc(c, r, 0, Mathf.Tau, 32, color, 1.5f, true);
        if (!Found)
        {
            DrawString(Ui.Serif, c + new Vector2(-4, 6), "?", HorizontalAlignment.Left, -1, 16, color);
            return;
        }
        var n = 2 + Tier % 5;
        for (var i = 0; i < n; i++)
        {
            var a = Mathf.Tau * i / n + Tier * 0.4f;
            var b = a + Mathf.Tau * (1 + Tier % 3) / n;
            DrawLine(c + Vector2.FromAngle(a) * (r - 4), c + Vector2.FromAngle(b) * (r - 4), color, 1.6f, true);
        }
    }
}
