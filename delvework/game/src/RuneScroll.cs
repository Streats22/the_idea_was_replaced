using Delvework.Core.Content;
using Delvework.Game.Audio;
using Godot;

namespace Delvework.Game;

/// <summary>
/// The parchment that unrolls when a rune tablet comes home: which feature it teaches, what the
/// golems can now write, and the smallest example from its Grimoire page.
/// </summary>
public static class RuneScroll
{
    /// <summary>The first code sample on the feature's pages: short enough to read at a glance.</summary>
    public static string? Example(LessonDef lesson) =>
        lesson.Pages.SelectMany(p => p.Blocks).Select(b => b.Code).FirstOrDefault(c => c is { Count: > 0 }) is { } code ? string.Join("\n", code) : null;

    public static void Show(App app, LessonDef lesson, Action? then = null)
    {
        var ink = Palette.Ink;
        var body = Ui.Column(10);
        var kicker = Ui.Label($"RUNE {lesson.Tier} OF {app.Content.Lessons.Count} FOUND", ink.Lightened(0.25f), 12);
        kicker.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(kicker);
        var title = Ui.Label(lesson.Title.ToUpperInvariant(), ink, 34);
        title.AddThemeFontOverride("font", Ui.Carved);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(title);
        body.AddChild(new RuneGlyph { CustomMinimumSize = new Vector2(0, 54), Tier = lesson.Tier });
        var summary = Ui.Para(lesson.Summary, ink, 15);
        summary.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(summary);
        var unlocks = Ui.Para($"Every golem in your party now understands {Help.Unlocks(lesson)}.", ink.Lightened(0.2f), 14);
        unlocks.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(unlocks);
        if (Example(lesson) is { } example)
        {
            body.AddChild(Ui.Label("A TINY EXAMPLE", ink.Lightened(0.25f), 11));
            body.AddChild(GlyphEditor.Snippet(example));
        }
        var grimoire = Ui.Button("Read it in the Grimoire");
        var onward = Ui.Button("Onward", primary: true);
        onward.CustomMinimumSize = new Vector2(130, 0);
        body.AddChild(Ui.Row(8, Ui.Spacer(), grimoire, onward));

        var scroll = Ui.Frame(Ui.Pad(body, 12), FrameKind.Parchment);
        var modal = app.ShowModal(scroll, 560, dismissable: false);
        app.Audio.Play(Sfx.Page);
        onward.Pressed += () =>
        {
            app.Audio.Play(Sfx.Click);
            app.CloseModal(modal);
            then?.Invoke();
        };
        grimoire.Pressed += () =>
        {
            app.CloseModal(modal);
            Help.Topic(app, lesson);
            then?.Invoke();
        };
    }
}

/// <summary>A rune drawn in glowing strokes; each feature's tier gives it a different shape.</summary>
public partial class RuneGlyph : Control
{
    public int Tier { get; init; }

    public override void _Draw()
    {
        var c = Size / 2;
        var r = Math.Min(Size.Y / 2 - 3, 24);
        var ink = Palette.Ink;
        DrawArc(c, r, 0, Mathf.Tau, 40, new Color(ink, 0.8f), 2f, true);
        DrawArc(c, r - 5, 0, Mathf.Tau, 40, new Color(ink, 0.35f), 1f, true);
        var n = 2 + Tier % 5;
        for (var i = 0; i < n; i++)
        {
            var a = Mathf.Tau * i / n + Tier * 0.4f;
            var b = a + Mathf.Tau * (1 + Tier % 3) / n;
            DrawLine(c + Vector2.FromAngle(a) * (r - 7), c + Vector2.FromAngle(b) * (r - 7), ink, 2.2f, true);
        }
        DrawCircle(c, 3, ink);
    }
}
