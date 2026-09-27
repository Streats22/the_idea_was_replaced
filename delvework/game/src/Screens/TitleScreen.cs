using Delvework.Game.Audio;
using Delvework.Game.View3D;
using Godot;

namespace Delvework.Game.Screens;

public partial class TitleScreen : Control
{
    public required App App { get; init; }

    private const string Intro =
        "Hollowmere was a mining town, until the tunnels below it filled with monsters and the miners left.\n\n" +
        "What they left behind are golems: stone workers that do exactly what their rune code tells them, nothing more.\n\n" +
        "You are the new Runewright. You can't go into the mines yourself, but you can write the code that sends the golems in. " +
        "Your first golem already knows a few commands, like move(East). The old miners carved the rest of the language into rune tablets and left them in the tunnels. Send your golems in, carry the runes home, and they learn loops, decisions, variables and more.\n\n" +
        "Rebuild the village, forge equipment, learn spells. And if a golem walks into a wall, that's a bug: find it, fix it, send it back.";

    public override void _Ready()
    {
        var town = new TownView3D { Interactive = false, TimeOfDay = 0.6f };
        town.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(town);
        town.Refresh(App.Progress.Buildings(), App.Progress.Party);

        var shade = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = new Gradient { Offsets = [0f, 0.55f, 1f], Colors = [new Color(0.03f, 0.03f, 0.05f, 0.94f), new Color(0.03f, 0.03f, 0.05f, 0.6f), new Color(0, 0, 0, 0)] },
                FillFrom = new Vector2(0, 0),
                FillTo = new Vector2(1, 0),
            },
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        shade.SetAnchorsPreset(LayoutPreset.LeftWide);
        shade.CustomMinimumSize = new Vector2(760, 0);
        AddChild(shade);

        var col = Ui.Column(14);
        col.SetAnchorsPreset(LayoutPreset.CenterLeft);
        col.Position = new Vector2(90, -230);
        col.CustomMinimumSize = new Vector2(440, 0);
        AddChild(col);

        col.AddChild(Ui.Title("Delvework", 66, Palette.Accent));
        col.AddChild(Ui.Banner("Write the code · Send the golems · Rebuild the village", 12, Palette.Muted));
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 26) });

        if (App.HasSave)
        {
            var p = App.Progress;
            var cont = Big("Continue", primary: true);
            cont.Pressed += () => Go(() => App.GoTown());
            col.AddChild(cont);
            col.AddChild(Ui.Label($"{p.LearnedCount} of {p.Lessons.Count} runes · {App.Profile.Gold} gold · {p.OwnedNodes.Count()} skills", Palette.Muted, 13));
        }
        var fresh = Big("New game", primary: !App.HasSave);
        fresh.Pressed += () =>
        {
            if (App.HasSave) ConfirmNewGame();
            else StartNewGame();
        };
        col.AddChild(fresh);
        var settings = Big("Settings");
        settings.Pressed += () =>
        {
            App.Audio.Play(Sfx.Click);
            App.ShowSettings();
        };
        col.AddChild(settings);
        var quit = Big("Quit");
        quit.Pressed += () => GetTree().Quit();
        col.AddChild(quit);

        var foot = Ui.Label("Music, art and language made for Delvework. Glyph looks like Python, so what you learn here works in real code too.", Palette.Muted.Darkened(0.2f), 12);
        foot.SetAnchorsPreset(LayoutPreset.BottomLeft);
        foot.Position = new Vector2(90, -40);
        AddChild(foot);
    }

    private static Button Big(string text, bool primary = false)
    {
        var b = Ui.Button(text, primary: primary);
        b.CustomMinimumSize = new Vector2(280, 46);
        b.AddThemeFontSizeOverride("font_size", 17);
        b.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        return b;
    }

    private void Go(Action action)
    {
        App.Audio.Play(Sfx.Click);
        action();
    }

    private void ConfirmNewGame()
    {
        var yes = Ui.Button("Start over", primary: true);
        var no = Ui.Button("Cancel");
        var modal = App.ShowModal(Ui.Column(14,
            Ui.Heading("Start a new game?", 22),
            Ui.Para("This replaces your saved progress: gold, skills, found runes and all your code.", Palette.Muted),
            Ui.Row(8, Ui.Spacer(), no, yes)), 480);
        no.Pressed += () => App.CloseModal(modal);
        yes.Pressed += StartNewGame;
    }

    private void StartNewGame()
    {
        App.Audio.Play(Sfx.Click);
        App.NewGame();
        var begin = Ui.Button("To the village", primary: true);
        App.CloseAllModals();
        var modal = App.ShowModal(Ui.Column(16,
            Ui.Banner("Hollowmere", 18),
            Ui.Para(Intro),
            Ui.Row(8, Ui.Spacer(), begin)), 640, dismissable: false);
        begin.Pressed += () =>
        {
            App.CloseModal(modal);
            App.GoTown();
        };
    }
}
