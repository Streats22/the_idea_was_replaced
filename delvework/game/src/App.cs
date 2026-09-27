using System.Globalization;
using Delvework.Core.Content;
using Delvework.Core.Progress;
using Delvework.Game.Audio;
using Delvework.Game.Screens;
using Godot;

namespace Delvework.Game;

/// <summary>
/// The root node: owns the content, the player's profile and the music, and switches between
/// screens (title, town, skill trees, workshops, Almanac, delve).
/// </summary>
public partial class App : Control
{
    private const string ProfilePath = "user://profile.json";

    private Control? _screen;
    private readonly Stack<Control> _modals = [];
    private Control _screenLayer = null!, _windowLayer = null!, _modalLayer = null!;
    private VBoxContainer _toasts = null!;
    private ColorRect _fade = null!;
    private readonly Dictionary<string, HelpWindow> _windows = [];
    private bool _persist = true;

    public ContentPack Content { get; private set; } = null!;
    public Profile Profile { get; private set; } = null!;
    public Progression Progress { get; private set; } = null!;
    public AudioDirector Audio { get; private set; } = null!;
    public bool HasSave { get; private set; }

    /// <summary>Raised after new language features are learned, so open screens can refresh.</summary>
    public event Action? Learned;

    public override void _Ready()
    {
        // Godot hosts .NET itself, so the project's InvariantGlobalization setting does not apply.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Theme = Ui.BuildTheme();
        SetAnchorsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = Palette.Bg };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        _screenLayer = new Control();
        _screenLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_screenLayer);
        _windowLayer = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _windowLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_windowLayer);
        _modalLayer = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _modalLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_modalLayer);
        _toasts = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Begin };
        _toasts.AddThemeConstantOverride("separation", 8);
        _toasts.SetAnchorsPreset(LayoutPreset.TopRight);
        _toasts.GrowHorizontal = GrowDirection.Begin;
        _toasts.OffsetLeft = -360;
        _toasts.OffsetRight = -20;
        _toasts.OffsetTop = 64;
        AddChild(_toasts);
        _fade = new ColorRect { Color = new Color(Palette.Bg, 0), MouseFilter = MouseFilterEnum.Ignore };
        _fade.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_fade);

        Content = LoadContent();
        Audio = new AudioDirector();
        AddChild(Audio);

        var args = OS.GetCmdlineUserArgs();
        if (args.Length > 0)
        {
            _persist = false;
            UseProfile(new Profile());
            _ = CommandLine.Run(this, args);
            return;
        }
        LoadProfile();
        GoTitle();
    }

    private static ContentPack LoadContent()
    {
        var dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "content"));
        return File.Exists(System.IO.Path.Combine(dir, "manifest.json")) ? ContentPack.Load(dir) : ContentPack.LoadDefault();
    }

    private void LoadProfile()
    {
        var path = ProjectSettings.GlobalizePath(ProfilePath);
        Profile profile;
        try
        {
            HasSave = File.Exists(path);
            profile = HasSave ? Profile.FromJson(File.ReadAllText(path)) : new Profile();
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException)
        {
            GD.PushWarning($"Could not read the profile, starting fresh: {e.Message}");
            profile = new Profile();
        }
        UseProfile(profile);
    }

    public void UseProfile(Profile profile)
    {
        Profile = profile;
        Progress = new Progression(Content, profile);
        AudioDirector.SetVolumes(profile.MusicVolume, profile.SfxVolume);
    }

    public void NewGame()
    {
        UseProfile(new Profile());
        HasSave = true;
        Save();
    }

    public void Save()
    {
        if (!_persist) return;
        try
        {
            File.WriteAllText(ProjectSettings.GlobalizePath(ProfilePath), Profile.ToJson());
            HasSave = true;
        }
        catch (IOException e)
        {
            GD.PushError($"Could not save the profile: {e.Message}");
        }
    }

    // ----- Screens -----

    public T Show<T>(T screen)
        where T : Control
    {
        CloseAllModals();
        _screen?.QueueFree();
        _screen = screen;
        screen.SetAnchorsPreset(LayoutPreset.FullRect);
        _screenLayer.AddChild(screen);
        _fade.Color = new Color(Palette.Bg, 1);
        CreateTween().TweenProperty(_fade, "color:a", 0f, 0.28f).SetEase(Tween.EaseType.Out);
        return screen;
    }

    // ----- Toasts -----

    /// <summary>A notice that slides in at the top right and fades away by itself.</summary>
    public void Toast(string title, string text, Color accent, float seconds = 4.5f)
    {
        var body = Ui.Column(2, Ui.Label(title, accent, 12));
        if (text.Length > 0) body.AddChild(Ui.Para(text, Palette.Text, 14));
        var card = Ui.Frame(body, FrameKind.Plate, opacity: 0.96f);
        card.Trim = accent;
        card.CustomMinimumSize = new Vector2(320, 0);
        card.MouseFilter = MouseFilterEnum.Ignore;
        card.Modulate = new Color(1, 1, 1, 0);
        _toasts.AddChild(card);
        while (_toasts.GetChildCount() > 5)
        {
            var oldest = _toasts.GetChild(0);
            _toasts.RemoveChild(oldest);
            oldest.QueueFree();
        }
        var tween = card.CreateTween();
        tween.TweenProperty(card, "modulate:a", 1f, 0.25f);
        tween.TweenInterval(seconds);
        tween.TweenProperty(card, "modulate:a", 0f, 0.6f);
        tween.TweenCallback(Callable.From(card.QueueFree));
    }

    /// <summary>Toast every Almanac entry found since the last call.</summary>
    public void ToastFinds()
    {
        var fresh = Progress.TakeFresh();
        foreach (var e in fresh.Take(4))
        {
            Toast($"NEW IN THE ALMANAC · +{e.Reward} gold", $"{e.Title}: {e.Hint}", Palette.Accent);
        }
        if (fresh.Count > 4) Toast("NEW IN THE ALMANAC", $"and {fresh.Count - 4} more entries. Read them in the Almanac at the Library.", Palette.Accent);
        if (fresh.Count > 0) Audio.Play(Sfx.Unlock);
    }

    public Control? Screen => _screen;

    public TitleScreen GoTitle()
    {
        Audio.SetMood(Mood.Title);
        return Show(new TitleScreen { App = this });
    }

    public TownScreen GoTown()
    {
        Audio.SetMood(Mood.Town);
        return Show(new TownScreen { App = this });
    }

    public SkillScreen GoSkills(SkillTree tree)
    {
        Audio.SetMood(Mood.Town);
        return Show(new SkillScreen { App = this, Tree = tree });
    }

    public WorkshopScreen GoWorkshop(WorkshopDef workshop)
    {
        Audio.SetMood(Mood.Town);
        return Show(new WorkshopScreen { App = this, Workshop = workshop });
    }

    public AlmanacScreen GoAlmanac(string? category = null)
    {
        Audio.SetMood(Mood.Town);
        return Show(new AlmanacScreen { App = this, InitialCategory = category });
    }

    public CommissionScreen GoCommissions(CommissionDef? commission = null)
    {
        Audio.SetMood(Mood.Town);
        return Show(new CommissionScreen { App = this, Initial = commission });
    }

    public DelveScreen GoSite(DelveSite site)
    {
        Audio.SetMood(Mood.Dungeon);
        Profile.Site = site.Id;
        return Show(new DelveScreen { App = this, Site = site });
    }

    /// <summary>
    /// Runes the party just brought home are learned already (<see cref="Progression.Apply"/>);
    /// tell open screens, and unroll a scroll for each with the feature and a tiny example.
    /// </summary>
    public void RunesLearned(IReadOnlyList<string> tablets, Action? then = null)
    {
        var runes = tablets.Select(id => Content.Lessons.FirstOrDefault(l => l.Id == id)).OfType<LessonDef>().ToList();
        if (runes.Count == 0)
        {
            then?.Invoke();
            return;
        }
        Save();
        Audio.Play(Sfx.Unlock);
        Learned?.Invoke();
        void Next(int i)
        {
            if (i < runes.Count) RuneScroll.Show(this, runes[i], () => Next(i + 1));
            else then?.Invoke();
        }
        Next(0);
    }

    // ----- Help windows -----

    /// <summary>Open a floating window, or bring the one with this <paramref name="key"/> to the front with new content.</summary>
    public HelpWindow OpenWindow(string key, string title, Control content, Vector2 size)
    {
        if (!_windows.TryGetValue(key, out var w) || !IsInstanceValid(w))
        {
            var n = _windows.Count % 5;
            w = new HelpWindow { Title = title, StartSize = size };
            w.Position = new Vector2(Math.Max(20, Size.X - size.X - 40 - 90 * n), 70 + 50 * n);
            w.Closed += () => _windows.Remove(key);
            _windowLayer.AddChild(w);
            _windows[key] = w;
            Audio.Play(Sfx.Page);
        }
        w.SetBody(title, content);
        w.MoveToFront();
        return w;
    }

    public void CloseWindow(string key)
    {
        if (_windows.TryGetValue(key, out var w) && IsInstanceValid(w)) w.Close();
    }

    public int WindowCount => _windows.Count;

    // ----- Modals -----

    /// <summary>Show <paramref name="content"/> in a centered card over a dimmed screen.</summary>
    public Control ShowModal(Control content, float width = 560, bool dismissable = true)
    {
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.62f), MouseFilter = MouseFilterEnum.Stop };
        shade.SetAnchorsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        shade.AddChild(center);
        PanelContainer card;
        if (content is Frame framed)
        {
            card = framed;
        }
        else
        {
            card = Ui.Frame(Ui.Pad(content, 10));
        }
        card.CustomMinimumSize = new Vector2(width, 0);
        center.AddChild(card);
        card.PivotOffset = new Vector2(width / 2, 60);
        card.Scale = new Vector2(0.96f, 0.96f);
        card.Modulate = new Color(1, 1, 1, 0);
        var pop = card.CreateTween().SetParallel();
        pop.TweenProperty(card, "scale", Vector2.One, 0.18f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        pop.TweenProperty(card, "modulate:a", 1f, 0.15f);
        if (dismissable)
        {
            shade.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && !card.GetGlobalRect().HasPoint(shade.GetGlobalMousePosition()))
                {
                    CloseModal(shade);
                }
            };
        }
        _modalLayer.AddChild(shade);
        _modals.Push(shade);
        return shade;
    }

    public void CloseModal(Control? modal = null)
    {
        if (_modals.Count == 0) return;
        var top = modal ?? _modals.Peek();
        var rest = _modals.Where(m => m != top).Reverse().ToList();
        _modals.Clear();
        foreach (var m in rest) _modals.Push(m);
        top.QueueFree();
    }

    public void CloseAllModals()
    {
        while (_modals.Count > 0) _modals.Pop().QueueFree();
    }

    public bool ModalOpen => _modals.Count > 0;

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Keycode: Key.Escape }) return;
        if (ModalOpen)
        {
            CloseModal();
            GetViewport().SetInputAsHandled();
        }
        else if (_windowLayer.GetChildren().OfType<HelpWindow>().LastOrDefault(w => !w.IsQueuedForDeletion()) is { } top)
        {
            top.Close();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>A small cog that opens <see cref="ShowSettings"/>.</summary>
    public Button SettingsButton()
    {
        var b = Ui.Button("⚙", "Settings: music and sound volume");
        b.Pressed += ShowSettings;
        return b;
    }

    /// <summary>Settings dialog: music and sound volume.</summary>
    public void ShowSettings()
    {
        var music = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = Profile.MusicVolume, CustomMinimumSize = new Vector2(260, 24) };
        var sfx = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = Profile.SfxVolume, CustomMinimumSize = new Vector2(260, 24) };
        music.ValueChanged += v =>
        {
            Profile.MusicVolume = v;
            AudioDirector.SetVolumes(Profile.MusicVolume, Profile.SfxVolume);
        };
        sfx.ValueChanged += v =>
        {
            Profile.SfxVolume = v;
            AudioDirector.SetVolumes(Profile.MusicVolume, Profile.SfxVolume);
            Audio.Play(Sfx.Click);
        };
        var close = Ui.Button("Done", primary: true);
        var body = Ui.Column(14,
            Ui.Banner("Settings", 14),
            Ui.Row(12, Ui.Label("Music", Palette.Muted), Ui.Spacer(), music),
            Ui.Row(12, Ui.Label("Sound effects", Palette.Muted), Ui.Spacer(), sfx),
            Ui.Row(8, Ui.Spacer(), close));
        var modal = ShowModal(body, 460);
        close.Pressed += () =>
        {
            Save();
            CloseModal(modal);
        };
    }
}
