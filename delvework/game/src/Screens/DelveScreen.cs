using System.Globalization;
using System.Text;
using Delvework.Core.Content;
using Delvework.Core.Glyph;
using Delvework.Core.Progress;
using Delvework.Core.Replay;
using Delvework.Core.Sim;
using Delvework.Game.Audio;
using Delvework.Game.View3D;
using Godot;

namespace Delvework.Game.Screens;

/// <summary>
/// A delve into a site: the dungeon fills the screen, the party's cards and the floor badge sit
/// on top, each golem's code in a carved panel on the right and the replay timeline along the bottom.
/// </summary>
public partial class DelveScreen : Control
{
    private static readonly int[] Speeds = [1, 3, 8];
    private const string StaleHint = "Code changed since this run: the replay shows the old program. Press Delve to try the new one.";
    private const float PanelWidth = 470;

    public required App App { get; init; }
    public required DelveSite Site { get; init; }

    private List<string> _party = [];
    private Dictionary<string, string> _sources = [];
    private int _tier;
    private Loadout _loadout = Loadout.None;
    private Dictionary<string, string> _locked = [];

    private Timeline? _timeline;
    private Dictionary<string, string> _ranSources = [];
    private double _playhead;
    private bool _playing;
    private int _speed = 1;
    private string _activeTab = "";
    private GlyphError? _liveError;
    private long _logShown;
    private (int Exec, int Err) _markers = (-1, -1);
    private (int Tick, string Tab) _inspected = (-1, "");
    private double _saveDue = -1, _compileDue = -1, _inspectDue = -1;
    private int _soundTick = -1;
    private Action? _pendingResult;

    private DungeonView3D _view = null!;
    private GlyphEditor _editor = null!;
    private TabBar _tabs = null!;
    private HSlider _slider = null!;
    private TimelineMarks _marks = null!;
    private Button _play = null!, _skip = null!, _run = null!, _codeToggle = null!;
    private Label _status = null!, _hint = null!, _logMeta = null!, _clock = null!, _floorNote = null!;
    private HBoxContainer _gold = null!, _cards = null!;
    private Label _wordSig = null!, _wordDoc = null!;
    private Control _wordRow = null!, _codePanel = null!;
    private string _word = "";
    private RichTextLabel _log = null!, _inspector = null!, _reference = null!;
    private readonly Dictionary<string, GolemCard> _golemCards = [];

    private int CurrentTick => _timeline is null ? 0 : (int)Math.Floor(_playhead);
    public Timeline? Timeline => _timeline;
    public DungeonView3D View => _view;

    public override void _Ready()
    {
        var p = App.Progress;
        _party = [.. p.Party];
        _sources = _party.ToDictionary(id => id, p.DelveProgram);
        _tier = p.KnownTier;
        _loadout = p.Loadout();
        _locked = Spells.LockedFor(_loadout);
        _activeTab = _party[0];
        BuildUi();
        _editor.Tier = _tier;
        _editor.HiddenNames = new HashSet<string>(_locked.Keys, StringComparer.Ordinal);
        _editor.ReplaceText(_sources[_activeTab]);
        LiveCompile();
        RefreshTabs();
        RefreshWallet();
        RefreshFloorNote();
        Run(apply: false);
        App.Learned += OnLearned;
    }

    public override void _ExitTree() => App.Learned -= OnLearned;

    private void OnLearned()
    {
        RefreshWallet();
        RefreshFloorNote();
        _tier = App.Progress.KnownTier;
        _editor.Tier = _tier;
        _reference.Text = ReferenceText();
        _word = "";
        LiveCompile();
        RefreshWord();
    }

    private void RefreshWallet()
    {
        foreach (var c in _gold.GetChildren()) c.QueueFree();
        _gold.AddChild(Goods.Bar(App.Profile, 13));
    }

    private void RefreshFloorNote()
    {
        var p = App.Progress;
        _floorNote.Text = p.TabletAt(Site) is not null
            ? "✦ A rune tablet glows somewhere on this floor. Carry it home to learn it."
            : p.NextToLearn is null ? "Every rune is found. Delve for gold and materials." : "No rune lies this shallow any more: the next one waits deeper.";
        _floorNote.AddThemeColorOverride("font_color", p.TabletAt(Site) is not null ? Palette.Rune : Palette.Muted);
    }

    // ----- Layout -----

    private void BuildUi()
    {
        _view = new DungeonView3D();
        _view.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_view);

        var vignette = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.42f, 0.5f),
                FillTo = new Vector2(1.05f, 1.05f),
                Gradient = new Gradient { Colors = [new Color(0, 0, 0, 0), new Color(0, 0, 0, 0.7f)], Offsets = [0.45f, 1f] },
                Width = 256,
                Height = 256,
            },
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        vignette.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(vignette);

        var top = BuildTopBar();
        top.SetAnchorsPreset(LayoutPreset.TopWide);
        top.OffsetLeft = 12;
        top.OffsetRight = -12;
        top.OffsetTop = 10;
        AddChild(top);

        var note = BuildFloorPlate();
        note.Position = new Vector2(12, 96);
        AddChild(note);

        _codePanel = BuildCodePanel();
        _codePanel.AnchorLeft = 1;
        _codePanel.AnchorRight = 1;
        _codePanel.AnchorBottom = 1;
        _codePanel.OffsetLeft = -PanelWidth - 12;
        _codePanel.OffsetRight = -12;
        _codePanel.OffsetTop = 92;
        _codePanel.OffsetBottom = -96;
        AddChild(_codePanel);

        var bottom = BuildBottomBar();
        bottom.SetAnchorsPreset(LayoutPreset.BottomWide);
        bottom.GrowVertical = GrowDirection.Begin;
        bottom.OffsetLeft = 12;
        bottom.OffsetRight = -12;
        bottom.OffsetBottom = -10;
        AddChild(bottom);
        ShowCode(true);
    }

    private Control BuildTopBar()
    {
        var index = App.Content.Sites.ToList().IndexOf(Site);
        var badge = Ui.Frame(Ui.Column(0, Centered(Ui.Label("FLOOR", Palette.Muted, 11)), Centered(Ui.Title($"B{index + 1}", 26, Palette.BrassLight))), FrameKind.Plate, opacity: 0.94f);
        badge.CustomMinimumSize = new Vector2(92, 72);
        badge.TooltipText = Site.Name;

        _cards = Ui.Row(8);
        foreach (var id in _party)
        {
            var card = new GolemCard(App.Content.Chassis[id].Name, Palette.ForGolem(id));
            _golemCards[id] = card;
            var chassis = id;
            card.Frame.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) SelectTab(chassis);
            };
            _cards.AddChild(card.Frame);
        }

        _clock = Ui.Label("00:00", Palette.BrassLight, 17);
        _clock.AddThemeFontOverride("font", Ui.Mono);
        var clock = Ui.Frame(Ui.Row(8, Ui.Label("⧗", Palette.Brass, 18), _clock), FrameKind.Plate, opacity: 0.94f);
        clock.CustomMinimumSize = new Vector2(0, 72);
        clock.TooltipText = "Time in the replay";

        _gold = Ui.Row(0);
        var wallet = Ui.Frame(_gold, FrameKind.Plate, opacity: 0.94f);
        wallet.CustomMinimumSize = new Vector2(0, 72);

        var back = Ui.Button("⌂  Town", "Back to the village");
        back.Pressed += () => Leave(() => App.GoTown());
        var tools = Ui.Frame(Ui.Row(6, Help.Menu(App), App.SettingsButton(), back), FrameKind.Plate, opacity: 0.94f);
        tools.CustomMinimumSize = new Vector2(0, 72);

        var row = Ui.Row(8, badge, _cards, clock, Ui.Spacer(), wallet, tools);
        row.MouseFilter = MouseFilterEnum.Ignore;
        return row;
    }

    private static Control Centered(Label l)
    {
        l.HorizontalAlignment = HorizontalAlignment.Center;
        return l;
    }

    private Control BuildFloorPlate()
    {
        _floorNote = Ui.Para("", Palette.Rune, 13);
        _floorNote.CustomMinimumSize = new Vector2(330, 0);
        var gear = new List<string>();
        var l = _loadout;
        if (l.Hp > 0) gear.Add($"+{l.Hp} HP");
        if (l.Armor > 0) gear.Add($"+{l.Armor} armor");
        if (l.Attack > 0) gear.Add($"+{l.Attack} attack");
        if (l.Sight > 0) gear.Add($"+{l.Sight} sight");
        if (l.Budget > 0) gear.Add($"+{l.Budget} instructions");
        if (l.Speed > 0) gear.Add("faster steps");
        if (l.Mana > 0) gear.Add($"{l.Mana} mana");
        if (l.Spells.Count > 0) gear.Add("spells: " + string.Join(", ", l.Spells.Select(s => s + "()")));
        var plate = Ui.Frame(Ui.Column(4,
            Ui.Title(Site.Name, 14),
            _floorNote,
            Ui.Label(gear.Count == 0 ? $"Seed {App.Profile.Seed} · no equipment yet" : $"Seed {App.Profile.Seed} · " + string.Join(" · ", gear), Palette.Muted, 12)), FrameKind.Plate, opacity: 0.88f);
        plate.TooltipText = Site.Description;
        return plate;
    }

    private Control BuildCodePanel()
    {
        _tabs = new TabBar { FocusMode = FocusModeEnum.None, TabAlignment = TabBar.AlignmentMode.Left, ClipTabs = false };
        foreach (var id in _party) _tabs.AddTab(id + ".glyph");
        _tabs.TabChanged += i => SwitchTab(_party[(int)i]);
        _status = Ui.Label("", Palette.Muted, 12);
        _status.ClipText = true;
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        var grimoire = Ui.Button("✦ Grimoire", "Every rune your golems have found, with examples");
        grimoire.Pressed += () =>
        {
            App.Audio.Play(Sfx.Page);
            Help.Grimoire(App);
        };
        var reset = Ui.Button("Reset", "Start over from the starter program");
        reset.Pressed += ResetCode;

        _editor = new GlyphEditor { SizeFlagsVertical = SizeFlags.ExpandFill };
        _editor.TextChanged += () =>
        {
            _saveDue = 0.4;
            _compileDue = 0.25;
            RefreshHint();
        };
        _hint = Ui.Label("", Palette.Muted, 12);
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _editor.Describe = word => Help.Brief(App, word, _tier);
        _editor.WordClicked += word => Help.Word(App, word);
        _editor.CaretChanged += RefreshWord;

        _wordSig = Ui.Label("", Palette.Accent, 13);
        _wordSig.AddThemeFontOverride("font", Ui.Mono);
        _wordDoc = Ui.Label("", Palette.Muted, 12);
        _wordDoc.ClipText = true;
        _wordDoc.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _wordDoc.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var more = Ui.Button("More ▸", "Open a window with the details (or Ctrl+click the name)");
        more.Pressed += () => Help.Word(App, _word);
        var strip = new PanelContainer { Visible = false };
        strip.AddThemeStyleboxOverride("panel", Ui.Box(Palette.Panel2, Palette.BrassDim, 3, 6));
        strip.AddChild(Ui.Row(10, _wordSig, _wordDoc, more));
        _wordRow = strip;

        _logMeta = Ui.Label("", Palette.Muted, 11);
        _log = new RichTextLabel { ScrollFollowing = true, SelectionEnabled = true, BbcodeEnabled = false, SizeFlagsVertical = SizeFlags.ExpandFill };
        var logPage = Ui.Column(4, Ui.Row(8, Ui.Label("print() writes here", Palette.Muted, 11), Ui.Spacer(), _logMeta), _log);
        logPage.Name = "Log";
        _inspector = new RichTextLabel { Name = "Inspector", BbcodeEnabled = true, SelectionEnabled = true };
        _reference = new RichTextLabel { Name = "Functions", BbcodeEnabled = true, SelectionEnabled = true, Text = ReferenceText(), MetaUnderlined = false };
        _reference.MetaClicked += meta => Help.Word(App, meta.AsString());
        var pages = new TabContainer { CustomMinimumSize = new Vector2(0, 180) };
        pages.AddChild(logPage);
        pages.AddChild(_inspector);
        pages.AddChild(_reference);

        var header = Ui.Banner("Protocol", 12);
        var col = Ui.Column(8,
            header,
            Ui.Row(6, _tabs, Ui.Spacer(), grimoire, reset),
            _status,
            _editor,
            _wordRow,
            _hint,
            pages);
        return Ui.Frame(col, opacity: 0.97f);
    }

    private Control BuildBottomBar()
    {
        _play = Ui.Button("▶  Replay", "Play or pause the replay (Space)");
        _play.CustomMinimumSize = new Vector2(128, 44);
        _play.AddThemeFontSizeOverride("font_size", 16);
        _play.Pressed += TogglePlay;
        var speeds = Ui.Row(2);
        var group = new ButtonGroup();
        foreach (var s in Speeds)
        {
            var b = Ui.Button($"{s}×", $"Play back at {s}× speed");
            b.ToggleMode = true;
            b.ButtonGroup = group;
            b.ButtonPressed = s == _speed;
            b.Pressed += () => _speed = s;
            speeds.AddChild(b);
        }
        var left = Ui.Frame(Ui.Row(8, _play, speeds), FrameKind.Plate, opacity: 0.95f);

        _slider = new HSlider { MinValue = 0, MaxValue = 1, Step = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "Replay timeline: drag to scrub. Right-drag the dungeon to look around, scroll to zoom." };
        _slider.ValueChanged += v =>
        {
            _playhead = v;
            _playing = false;
            RefreshAll();
        };
        _marks = new TimelineMarks { CustomMinimumSize = new Vector2(0, 30), SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        var track = Ui.Column(0, _slider, _marks);
        track.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        track.Alignment = BoxContainer.AlignmentMode.Center;
        var middle = Ui.Frame(Ui.Pad(track, 2), FrameKind.Plate, opacity: 0.95f);
        middle.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        _skip = Ui.Button("⏭", "Skip to the end");
        _skip.CustomMinimumSize = new Vector2(44, 44);
        _skip.Pressed += () =>
        {
            if (_timeline is null) return;
            _playhead = _timeline.LastTick;
            _playing = false;
            RefreshAll();
        };
        _codeToggle = Ui.Button("Hide code", "Show or hide the code panel");
        _codeToggle.CustomMinimumSize = new Vector2(0, 44);
        _codeToggle.Pressed += () => ShowCode(!_codePanel.Visible);
        _run = Ui.Button("▶  DELVE", "Send the party in with this code (Ctrl+Enter)", primary: true);
        _run.CustomMinimumSize = new Vector2(150, 44);
        _run.AddThemeFontOverride("font", Ui.Carved);
        _run.AddThemeFontSizeOverride("font_size", 18);
        _run.Pressed += () => Run(apply: true);
        var right = Ui.Frame(Ui.Row(8, _skip, _codeToggle, _run), FrameKind.Plate, opacity: 0.95f);

        var row = Ui.Row(8, left, middle, right);
        row.MouseFilter = MouseFilterEnum.Ignore;
        return row;
    }

    private static string Esc(string s) => s.Replace("[", "[lb]", StringComparison.Ordinal);

    private static string Hex(Color c) => c.ToHtml(false);

    private string ReferenceText()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"[color=#{Hex(Palette.Muted)}]What your golems know ({Tiers.Describe(_tier)}). Click a name for an example. Actions end the golem's turn and take a few ticks.[/color]\n\n");
        foreach (var b in GolemApi.Environment.Builtins)
        {
            if (string.IsNullOrEmpty(b.Signature) || b.Tier > _tier || _locked.ContainsKey(b.Name)) continue;
            var kind = b.IsAction ? " [color=#f07178]action[/color]" : "";
            sb.Append(CultureInfo.InvariantCulture, $"[url={b.Name}][color=#{Hex(Palette.Accent)}]{Esc(b.Signature)}[/color][/url]{kind}\n");
            sb.Append(CultureInfo.InvariantCulture, $"[color=#{Hex(Palette.Muted)}]    {Esc(b.Doc)}[/color]\n");
        }
        if (_tier >= Tiers.Events)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\n[color=#{Hex(Palette.Text)}]Events[/color]\n");
            foreach (var (sig, doc) in new[]
            {
                ("on see(enemy):", "An enemy comes into view."),
                ("on hurt(amount, source):", "This golem took damage."),
                ("on low_hp():", "HP fell to 30% or less (once)."),
                ("on status(kind, ticks):", "Stunned, burning or slowed."),
                ("on signal \"name\"(data):", "A teammate called signal(\"name\", data)."),
            })
            {
                sb.Append(CultureInfo.InvariantCulture, $"[url=on][color=#c792ea]{Esc(sig)}[/color][/url]\n[color=#{Hex(Palette.Muted)}]    {doc}[/color]\n");
            }
        }
        return sb.ToString();
    }

    /// <summary>The strip under the editor explains whatever name the caret is on.</summary>
    private void RefreshWord()
    {
        var word = _editor.WordAtCaret();
        if (word == _word) return;
        _word = word;
        var brief = word.Length > 0 ? Help.Brief(App, word, _tier) : null;
        _wordRow.Visible = brief is not null;
        if (brief is null) return;
        var parts = brief.Split('\n', 2);
        _wordSig.Text = parts[0];
        _wordDoc.Text = parts.Length > 1 ? parts[1] : "";
        _wordDoc.TooltipText = _wordDoc.Text;
    }

    // ----- Actions -----

    public override void _Input(InputEvent e)
    {
        if (App.ModalOpen) return;
        if (e is InputEventKey { Pressed: true, CtrlPressed: true } key && key.Keycode is Key.Enter or Key.KpEnter)
        {
            Run(apply: true);
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventKey { Pressed: true, Keycode: Key.Space } && GetViewport().GuiGetFocusOwner() is not TextEdit and not LineEdit)
        {
            TogglePlay();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Leave(Action go)
    {
        App.Audio.Play(Sfx.Click);
        Persist();
        App.Audio.SetCombat(0);
        go();
    }

    /// <summary>Simulate the delve. With <paramref name="apply"/>, it counts: rewards are paid and the result is shown at the end of the replay.</summary>
    public void Run(bool apply)
    {
        Persist();
        var p = App.Progress;
        DelveSetup setup;
        try
        {
            setup = p.SiteSetup(Site, App.Profile.Seed);
            _timeline = Timeline.Record(App.Content, setup);
        }
        catch (ContentException e)
        {
            _hint.Text = e.Message;
            _hint.AddThemeColorOverride("font_color", Palette.Danger);
            return;
        }
        _ranSources = new Dictionary<string, string>(_sources);
        _playhead = 0;
        _playing = apply;
        _soundTick = 0;
        _logShown = 0;
        _log.Clear();
        _inspected = (-1, "");
        _slider.MaxValue = Math.Max(1, _timeline.LastTick);
        _slider.SetValueNoSignal(0);
        _marks.Show(_timeline);
        _pendingResult = null;
        if (apply)
        {
            App.Audio.Play(Sfx.Click);
            _pendingResult = Judge();
            App.Save();
        }
        RefreshTabs();
        RefreshAll();
        RefreshInspector();
    }

    private Action Judge()
    {
        var p = App.Progress;
        var outcome = _timeline!.Outcome;
        var reward = p.SiteReward(outcome, Site);
        var seed = App.Profile.Seed;
        p.Apply(reward);
        var shifts = p.RunWorkshops();
        App.Profile.Seed++;
        return () => App.RunesLearned(reward.Tablets, () => ShowResult(outcome, reward, seed, shifts));
    }

    private void TogglePlay()
    {
        if (_timeline is null) return;
        if (_playhead >= _timeline.LastTick) _playhead = 0;
        _playing = !_playing;
        _soundTick = CurrentTick;
        RefreshAll();
    }

    private void ShowCode(bool on)
    {
        _codePanel.Visible = on;
        _codeToggle.Text = on ? "Hide code" : "Show code";
        _view.ShiftPixels = on ? (PanelWidth + 12) / 2 : 0;
    }

    private void SelectTab(string id)
    {
        if (!_codePanel.Visible) ShowCode(true);
        _tabs.CurrentTab = _party.IndexOf(id);
    }

    private void SwitchTab(string id)
    {
        if (id == _activeTab) return;
        _sources[_activeTab] = _editor.Text;
        Persist();
        _activeTab = id;
        _editor.ReplaceText(_sources[id]);
        _markers = (-1, -1);
        LiveCompile();
        RefreshAll();
        RefreshInspector();
    }

    private void ResetCode()
    {
        App.Audio.Play(Sfx.Click);
        var code = App.Content.Programs.GetValueOrDefault(Progression.FirstProgram, "");
        _editor.ReplaceText(code);
        _sources[_activeTab] = code;
        Persist();
        LiveCompile();
    }

    private void Persist()
    {
        if (_editor is not null) _sources[_activeTab] = _editor.Text;
        foreach (var (id, src) in _sources) App.Profile.Programs[Profile.DelveSlot(id)] = src;
        App.Save();
        _saveDue = -1;
    }

    private void LiveCompile()
    {
        _compileDue = -1;
        try
        {
            GlyphCompiler.Compile(_editor.Text, GolemApi.Environment, new CompileOptions
            {
                Tier = _tier,
                Locked = _locked,
                ModuleResolver = m => App.Content.Programs.TryGetValue(m, out var s) ? s : null,
            });
            _liveError = null;
        }
        catch (GlyphError e)
        {
            _liveError = e;
        }
        RefreshHint();
        RefreshMarkers();
    }

    // ----- Results -----

    private void ShowResultIfDone()
    {
        if (_pendingResult is null || _timeline is null || _playhead < _timeline.LastTick) return;
        var show = _pendingResult;
        _pendingResult = null;
        show();
    }

    public void ShowResultNow()
    {
        if (_timeline is null) return;
        _playhead = _timeline.LastTick;
        _playing = false;
        RefreshAll();
    }

    private static Control Line(string what, string value, Color color)
    {
        var v = Ui.Label(value, color, 15);
        v.HorizontalAlignment = HorizontalAlignment.Right;
        v.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return Ui.Row(10, Ui.Label(what, Palette.Muted, 15), v);
    }

    private void ShowResult(Outcome outcome, DelveReward reward, ulong seed, IReadOnlyList<Core.Village.WorkshopResult> shifts)
    {
        App.Audio.Play(outcome.Kind == OutcomeKind.Wiped ? Sfx.Fail : Sfx.Coin);
        App.ToastFinds();
        Control modal = null!;
        var mined = outcome.Goods ?? new Dictionary<string, int>();
        var produced = App.Progress.Production();
        var materials = Ui.Column(6);
        if (mined.Count > 0) materials.AddChild(Ui.Row(10, Ui.Label("Brought home", Palette.Muted, 14), Ui.Spacer(), Goods.Gains(mined)));
        if (produced.Count > 0) materials.AddChild(Ui.Row(10, Ui.Label("Village", Palette.Muted, 14), Ui.Spacer(), Goods.Gains(produced)));
        foreach (var s in shifts)
        {
            var open = Ui.Button($"{s.Def.Name} ▸", $"Open the {s.Def.Name} to change its script");
            var def = s.Def;
            open.Pressed += () => Leave(() => App.GoWorkshop(def));
            var what = s.Error is not null && s.Made.Count == 0 ? Ui.Label("script error: nothing made", Palette.Danger, 14)
                : s.Made.Count == 0 ? Ui.Label("nothing made (out of materials?)", Palette.Muted, 14)
                : (Control)Goods.Gains(s.Made);
            materials.AddChild(Ui.Row(10, open, Ui.Spacer(), what, s.Used.Count > 0 ? Ui.Label("from " + Resources.Format(s.Used), Palette.Muted, 13) : new Control()));
        }
        var again = Ui.Button("Delve again", primary: true);
        again.Pressed += () =>
        {
            App.CloseModal(modal);
            RefreshFloorNote();
            Run(apply: true);
        };
        var town = Ui.Button("Back to town");
        town.Pressed += () => Leave(() => App.GoTown());
        var stay = Ui.Button("Watch the replay");
        stay.Pressed += () => App.CloseModal(modal);
        var p = App.Progress;
        var lostRune = _timeline!.Final.Drops.Any(d => d.Tablets is { Count: > 0 });
        var tip = outcome.Kind switch
        {
            _ when lostRune && reward.Tablets.Count == 0 => "The rune tablet stayed behind in the mine. Walk a golem next to it and bring that golem home: it will be waiting on the next delve.",
            OutcomeKind.Wiped => "Every golem broke, so nothing came home. Recall earlier, avoid fights you can't win, or buy plating at the Forge.",
            OutcomeKind.Costly => "Some golems broke. Their loot was lost with them. A low_hp handler that calls recall() saves a lot of gold.",
            _ when mined.Count == 0 => "Veins of stone, iron ore and old timber glint in the mine walls. Stand next to one and mine() it: the village needs materials as well as gold.",
            _ => "Tune your code to open more chests, mine more veins and lose fewer golems. Better code brings more home.",
        };
        var headline = outcome.Summary.Split(" gold came back.")[0].Split(". ")[0].TrimEnd('.') + ".";
        var body = Ui.Column(12,
            Ui.Banner($"Delve report · seed {seed}", 12),
            Ui.Heading(headline, 24, outcome.Kind == OutcomeKind.Wiped ? Palette.Danger : Palette.BrassLight));
        if (reward.Tablets.Count > 0)
        {
            var names = string.Join(", ", reward.Tablets.Select(id => p.Lessons.FirstOrDefault(l => l.Id == id)?.Title ?? id));
            body.AddChild(Ui.Label($"✦ Rune learned: {names}", Palette.Rune, 16));
        }
        body.AddChild(Ui.Column(3,
            Line("Loot carried home", $"{reward.Loot}", Palette.Text),
            Line("Market bonus", $"{reward.Bonus}", reward.Bonus > 0 ? Palette.Text : Palette.Muted),
            Line("Village income", $"{reward.Income}", reward.Income > 0 ? Palette.Text : Palette.Muted),
            new HSeparator(),
            Line("Gold", $"+{reward.Total}  (you now have {App.Profile.Gold})", Palette.Accent)));
        if (materials.GetChildCount() > 0) body.AddChild(materials);
        body.AddChild(Ui.Para(tip, Palette.Muted, 14));
        body.AddChild(Ui.Row(8, Ui.Spacer(), stay, town, again));
        modal = App.ShowModal(body, 580);
        RefreshWallet();
        RefreshFloorNote();
    }

    // ----- Per-frame -----

    public override void _Process(double delta)
    {
        if (_timeline is not null && _playing)
        {
            _playhead = Math.Min(_timeline.LastTick, _playhead + delta * World.TicksPerSecond * _speed);
            if (_playhead >= _timeline.LastTick) _playing = false;
            RefreshAll();
        }
        if (_saveDue > 0 && (_saveDue -= delta) <= 0) Persist();
        if (_compileDue > 0 && (_compileDue -= delta) <= 0) LiveCompile();
        if (_inspectDue > 0 && (_inspectDue -= delta) <= 0) RefreshInspector();
    }

    public void SetPlayhead(double tick)
    {
        _playhead = tick;
        _playing = false;
        RefreshAll();
    }

    private static string Clock(int tick)
    {
        var s = tick / World.TicksPerSecond;
        return $"{s / 60:00}:{s % 60:00}";
    }

    private void RefreshAll()
    {
        _play.Text = _playing ? "❚❚  Pause" : "▶  Replay";
        _play.Disabled = _timeline is null;
        _skip.Disabled = _timeline is null;
        _view.Display(_timeline, _playhead);
        if (_timeline is null) return;
        var tick = CurrentTick;
        _slider.SetValueNoSignal(tick);
        _marks.Playhead = tick;
        _clock.Text = Clock(tick);
        PlaySounds(tick);
        RefreshMarkers();
        RefreshLog(tick);
        RefreshStatus(tick);
        RefreshCards(tick);
        RefreshHint();
        if (_inspected != (tick, _activeTab) && _inspectDue <= 0) _inspectDue = _playing ? 0.3 : 0.08;
        ShowResultIfDone();
    }

    private void RefreshCards(int tick)
    {
        var frame = _timeline!.At(tick);
        foreach (var (id, card) in _golemCards)
        {
            card.Show(frame.Golems.FirstOrDefault(g => g.Chassis == id), id == _activeTab);
        }
    }

    private void PlaySounds(int tick)
    {
        var frame = _timeline!.At(tick);
        var grid = _timeline.Final.Grid;
        var nearest = int.MaxValue;
        foreach (var g in frame.Golems.Where(g => g.State == GolemState.Active))
        {
            foreach (var m in frame.Monsters.Where(m => m.Alive && frame.Fog[grid.Idx(m.Pos)] == 2)) nearest = Math.Min(nearest, g.Pos.Manhattan(m.Pos));
        }
        App.Audio.SetCombat(!_playing ? 0 : nearest <= 2 ? 1 : nearest <= 5 ? 0.5f : 0);
        if (!_playing || tick <= _soundTick)
        {
            _soundTick = tick;
            return;
        }
        var from = Math.Max(_soundTick + 1, tick - 12);
        _soundTick = tick;
        for (var k = from; k <= tick; k++)
        {
            if (_timeline.At(k).Effects is not { Count: > 0 } fx) continue;
            foreach (var e in fx)
            {
                switch (e.Kind)
                {
                    case EffectKind.Attack:
                        App.Audio.Play(Sfx.Hit, 0.9f + (k % 5) * 0.05f);
                        break;
                    case EffectKind.Chest:
                        App.Audio.Play(Sfx.Chest);
                        break;
                    case EffectKind.Heal or EffectKind.Bolt or EffectKind.Reveal or EffectKind.Shield:
                        App.Audio.Play(Sfx.Spell, e.Kind == EffectKind.Bolt ? 1.3f : 1f);
                        break;
                    case EffectKind.Tablet:
                    {
                        App.Audio.Play(Sfx.Unlock);
                        var finder = _timeline.At(k).Golems.Where(g => g.State == GolemState.Active).MinBy(g => g.Pos.Manhattan(e.From));
                        App.Toast("RUNE TABLET", $"{finder?.Name ?? "A golem"} picked up a rune tablet. Bring it home and the whole party learns it.", Palette.Rune);
                        break;
                    }
                    case EffectKind.Break:
                        App.Audio.Play(Sfx.Break);
                        break;
                    case EffectKind.Kill:
                        App.Audio.Play(Sfx.Hit, 0.6f);
                        break;
                    case EffectKind.Trap:
                        App.Audio.Play(Sfx.Hit, 1.4f);
                        break;
                    case EffectKind.Mine:
                        App.Audio.Play(Sfx.Hit, 1.7f + (k % 3) * 0.1f);
                        break;
                    case EffectKind.Descend or EffectKind.Recall:
                        App.Audio.Play(Sfx.Descend);
                        break;
                }
            }
        }
    }

    private GolemFrame? ActiveGolem(int tick) => _timeline?.At(tick).Golems.FirstOrDefault(g => g.Chassis == _activeTab);

    private bool EditorMatchesRun => _ranSources.TryGetValue(_activeTab, out var src) && src == _editor.Text;

    private void RefreshMarkers()
    {
        var tick = CurrentTick;
        var g = ActiveGolem(tick);
        var unchanged = EditorMatchesRun;
        var exec = g is { State: GolemState.Active } && unchanged ? g.Line : 0;
        var err = 0;
        if (_liveError is not null)
        {
            err = _liveError.Line;
        }
        else if (unchanged && g is { State: GolemState.Halted })
        {
            err = _timeline!.Final.Golems.FirstOrDefault(x => x.Chassis.Id == _activeTab)?.Error?.Line ?? 0;
        }
        if (_markers == (exec, err)) return;
        _markers = (exec, err);
        _editor.SetMarkers(exec, err);
    }

    private void RefreshHint()
    {
        string text;
        var color = Palette.Muted;
        var g = ActiveGolem(CurrentTick);
        var finalError = _timeline?.Final.Golems.FirstOrDefault(x => x.Chassis.Id == _activeTab)?.Error;
        if (_liveError is not null)
        {
            text = $"Line {_liveError.Line}: {_liveError.Message}";
            color = Palette.Danger;
        }
        else if (_timeline is not null && _ranSources.ContainsKey(_activeTab) && !EditorMatchesRun)
        {
            text = StaleHint;
        }
        else if (g is { State: GolemState.Halted } && finalError is not null)
        {
            text = $"{App.Content.Chassis[_activeTab].Name} stopped. Line {finalError.Line}: {finalError.Message}";
            color = Palette.Danger;
        }
        else
        {
            text = "";
        }
        _hint.Visible = text.Length > 0;
        if (_hint.Text == text) return;
        _hint.Text = text;
        _hint.AddThemeColorOverride("font_color", color);
    }

    private void RefreshStatus(int tick)
    {
        var g = ActiveGolem(tick);
        if (g is null)
        {
            _status.Text = "";
            return;
        }
        var effects = new List<string>();
        if (g.StunTicks > 0) effects.Add("stunned");
        if (g.BurnTicks > 0) effects.Add("burning");
        if (g.SlowTicks > 0) effects.Add("slowed");
        var extra = effects.Count > 0 ? " · " + string.Join(", ", effects) : "";
        var mana = g.MaxMana > 0 ? $" · {g.Mana}/{g.MaxMana} mana" : "";
        _status.Text = $"{g.Name} · {g.State.ToString().ToLowerInvariant()} · line {g.Line}{mana}{extra}";
        _status.AddThemeColorOverride("font_color", g.State switch
        {
            GolemState.Active => Palette.Ok,
            GolemState.Halted or GolemState.Broken => Palette.Danger,
            _ => Palette.Muted,
        });
    }

    private void RefreshLog(int tick)
    {
        var frameCount = _timeline!.At(tick).LogCount;
        if (frameCount < _logShown)
        {
            _log.Clear();
            _logShown = 0;
        }
        if (frameCount == _logShown) return;
        var dropped = _timeline.Final.LogCount - _timeline.Final.Log.Count;
        var from = (int)Math.Max(0, _logShown - dropped);
        var to = (int)Math.Max(0, frameCount - dropped);
        for (var i = from; i < to; i++)
        {
            var e = _timeline.Final.Log[i];
            _log.PushColor(Palette.Muted.Darkened(0.2f));
            _log.AddText($"{e.Tick / (double)World.TicksPerSecond,6:0.0}s  ");
            _log.Pop();
            _log.PushColor(e.Kind switch
            {
                LogKind.Output => Palette.Text,
                LogKind.Combat => Color.FromHtml("#e6a07a"),
                LogKind.Error => Palette.Danger,
                _ => Palette.Muted,
            });
            _log.AddText(e.Text);
            _log.Pop();
            _log.Newline();
        }
        _logShown = frameCount;
        _logMeta.Text = $"{frameCount} of {_timeline.Final.LogCount} entries";
    }

    private void RefreshInspector()
    {
        _inspectDue = -1;
        if (_timeline is null) return;
        var tick = CurrentTick;
        _inspected = (tick, _activeTab);
        var name = App.Content.Chassis[_activeTab].Name;
        var info = _timeline.Inspect(tick, name);
        var muted = Hex(Palette.Muted);
        var sb = new StringBuilder();
        if (info is null)
        {
            _inspector.Text = $"[color=#{muted}]The {name} is not in this delve.[/color]";
            return;
        }
        var color = Hex(Palette.ForGolem(_activeTab));
        sb.Append(CultureInfo.InvariantCulture, $"[color=#{color}]{name}[/color]  [color=#{muted}]at tick {tick} · {info.State.ToString().ToLowerInvariant()} · line {info.Line}[/color]\n");
        if (info.Error is { } err) sb.Append(CultureInfo.InvariantCulture, $"[color=#{Hex(Palette.Danger)}]Line {err.Line}: {Esc(err.Message)}[/color]\n");
        if (info.CallStack.Count > 1)
        {
            sb.Append(CultureInfo.InvariantCulture, $"[color=#{muted}]Call stack:[/color] {Esc(string.Join("  ←  ", info.CallStack.Select(f => $"{f.Function} (line {f.Line})")))}\n");
        }
        void Section(string title, IReadOnlyList<Variable> vars)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\n[color=#{Hex(Palette.Text)}]{title}[/color]\n");
            if (vars.Count == 0)
            {
                var none = _tier < Tiers.Variables ? "(find the Variables rune to store values)" : "(none yet)";
                sb.Append(CultureInfo.InvariantCulture, $"[color=#{muted}]    {none}[/color]\n");
            }
            foreach (var v in vars)
            {
                var value = v.Value.Length > 160 ? v.Value[..160] + "…" : v.Value;
                sb.Append(CultureInfo.InvariantCulture, $"    [color=#c3a6ff]{Esc(v.Name)}[/color] = {Esc(value)}  [color=#{muted}]{v.Type}[/color]\n");
            }
        }
        Section("Variables", info.Globals);
        if (info.CallStack.Count > 1) Section($"Inside {info.CallStack[0].Function}()", info.Locals);
        _inspector.Text = sb.ToString();
    }

    private void RefreshTabs()
    {
        for (var i = 0; i < _party.Count; i++)
        {
            var id = _party[i];
            var halted = _timeline?.Final.Golems.FirstOrDefault(g => g.Chassis.Id == id)?.Error is not null;
            _tabs.SetTabTitle(i, id + ".glyph" + (halted ? "  (stopped)" : ""));
        }
        _tabs.CurrentTab = _party.IndexOf(_activeTab);
    }
}

/// <summary>A golem's card in the top bar: its colour, name, HP bar and what it carries.</summary>
public sealed class GolemCard
{
    public Frame Frame { get; }
    private readonly Label _name, _hp, _carry;
    private readonly ProgressBar _bar;
    private readonly Color _color;

    public GolemCard(string name, Color color)
    {
        _color = color;
        _name = Ui.Title(name, 13, color);
        _hp = Ui.Label("", Palette.Text, 12);
        _hp.AddThemeFontOverride("font", Ui.Mono);
        _carry = Ui.Label("", Palette.Muted, 11);
        _bar = Ui.Bar(color, 7);
        var heart = Ui.Label("♥", color, 22);
        var info = Ui.Column(2, Ui.Row(8, _name, Ui.Spacer(), _hp), _bar, _carry);
        info.CustomMinimumSize = new Vector2(150, 0);
        Frame = Ui.Frame(Ui.Row(8, heart, info), FrameKind.Plate, opacity: 0.94f);
        Frame.CustomMinimumSize = new Vector2(0, 72);
        Frame.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        Frame.TooltipText = $"Click to edit the {name}'s code";
    }

    public void Show(GolemFrame? g, bool selected)
    {
        Frame.Trim = selected ? _color : Palette.Brass;
        Frame.QueueRedraw();
        if (g is null)
        {
            _hp.Text = "";
            _carry.Text = "not in this delve";
            _bar.Value = 0;
            return;
        }
        _bar.MaxValue = Math.Max(1, g.MaxHp);
        _bar.Value = g.Hp;
        _hp.Text = $"{g.Hp} / {g.MaxHp}";
        var bag = g.Bag is { } b && b.Sum() > 0
            ? " · " + string.Join(" · ", Enumerable.Range(0, b.Count).Where(i => b[i] > 0).Select(i => $"{b[i]} {Resources.Name(Resources.Mined[i])}"))
            : "";
        _carry.Text = g.State switch
        {
            GolemState.Broken => "broken",
            GolemState.Halted => "stopped: code error",
            GolemState.Descended => $"home safe · {g.Loot} gold{bag}",
            GolemState.Recalled => $"recalled · {g.Loot} gold{bag}",
            _ => $"{g.Loot} gold{bag}",
        };
        _carry.AddThemeColorOverride("font_color", g.State is GolemState.Broken or GolemState.Halted ? Palette.Danger : Palette.Muted);
    }
}

/// <summary>Under the timeline: second labels, and diamonds where something happened (runes, chests, breaks).</summary>
public partial class TimelineMarks : Control
{
    private readonly List<(int Tick, Color Color)> _events = [];
    private int _last = 1;
    private int _playhead;

    public int Playhead
    {
        get => _playhead;
        set
        {
            if (_playhead == value) return;
            _playhead = value;
            QueueRedraw();
        }
    }

    public void Show(Timeline timeline)
    {
        _events.Clear();
        _last = Math.Max(1, timeline.LastTick);
        for (var k = 0; k <= timeline.LastTick; k++)
        {
            if (timeline.At(k).Effects is not { Count: > 0 } fx) continue;
            foreach (var e in fx)
            {
                Color? c = e.Kind switch
                {
                    EffectKind.Tablet => Palette.Rune,
                    EffectKind.Chest => Palette.Accent,
                    EffectKind.Break => Palette.Danger,
                    EffectKind.Descend => Palette.Ok,
                    _ => null,
                };
                if (c is { } color) _events.Add((k, color));
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        const float inset = 9;
        var width = Size.X - inset * 2;
        float X(int tick) => inset + width * tick / _last;
        var seconds = _last / World.TicksPerSecond;
        var step = seconds <= 40 ? 10 : seconds <= 120 ? 20 : 60;
        for (var s = 0; s <= seconds; s += step)
        {
            var x = X(s * World.TicksPerSecond);
            DrawLine(new Vector2(x, 0), new Vector2(x, 7), new Color(Palette.Brass, 0.6f), 1);
            var label = $"{s / 60:00}:{s % 60:00}";
            DrawString(Ui.Mono, new Vector2(x - 17, 22), label, HorizontalAlignment.Left, -1, 11, _playhead >= s * World.TicksPerSecond ? Palette.BrassLight : Palette.Muted);
        }
        foreach (var (tick, color) in _events)
        {
            var x = X(tick);
            var y = 4f;
            DrawColoredPolygon([new Vector2(x - 4, y), new Vector2(x, y - 4), new Vector2(x + 4, y), new Vector2(x, y + 4)], tick <= _playhead ? color : new Color(color, 0.45f));
        }
    }
}
