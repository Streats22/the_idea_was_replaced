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
/// Write each golem's program, run the delve, then watch and scrub the replay. Used for Codex
/// challenges (<see cref="Lesson"/>) and for free delves into a site (<see cref="Site"/>).
/// </summary>
public partial class DelveScreen : Control
{
    private static readonly int[] Speeds = [1, 3, 8];
    private const string StaleHint = "Code changed since this run: the replay shows the old program. Press Run to test the new one.";

    public required App App { get; init; }
    public LessonDef? Lesson { get; init; }
    public DelveSite? Site { get; init; }

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
    private int _attempts;
    private Action? _pendingResult;

    private DungeonView3D _view = null!;
    private GlyphEditor _editor = null!;
    private TabBar _tabs = null!;
    private HSlider _slider = null!;
    private Button _play = null!, _skip = null!, _run = null!, _stuck = null!;
    private Label _meta = null!, _status = null!, _hint = null!, _logMeta = null!;
    private HBoxContainer _gold = null!;
    private Label _wordSig = null!, _wordDoc = null!;
    private Control _wordRow = null!;
    private string _word = "";
    private Button _learn = null!;
    private VBoxContainer _goals = null!;
    private RichTextLabel _log = null!, _inspector = null!, _reference = null!;

    private int CurrentTick => _timeline is null ? 0 : (int)Math.Floor(_playhead);
    public Timeline? Timeline => _timeline;
    public DungeonView3D View => _view;

    public override void _Ready()
    {
        var p = App.Progress;
        if (Lesson is { } lesson)
        {
            _party = [.. lesson.Challenge.Party];
            _sources = p.LessonSources(lesson);
            _tier = lesson.Tier;
            _loadout = Loadout.None;
        }
        else
        {
            _party = [.. p.Party];
            _sources = _party.ToDictionary(id => id, p.DelveProgram);
            _tier = p.KnownTier;
            _loadout = p.Loadout();
        }
        _locked = Spells.LockedFor(_loadout);
        _activeTab = _party[0];
        BuildUi();
        _editor.Tier = _tier;
        _editor.HiddenNames = new HashSet<string>(_locked.Keys, StringComparer.Ordinal);
        _editor.ReplaceText(_sources[_activeTab]);
        LiveCompile();
        RefreshTabs();
        RefreshWallet();
        Run(apply: false);
        App.Learned += OnLearned;
    }

    public override void _ExitTree() => App.Learned -= OnLearned;

    private void OnLearned()
    {
        RefreshWallet();
        if (Lesson is not null) return;
        _tier = App.Progress.KnownTier;
        _editor.Tier = _tier;
        _reference.Text = ReferenceText();
        _word = "";
        LiveCompile();
        RefreshWord();
    }

    private void RefreshWallet()
    {
        var p = App.Progress;
        foreach (var c in _gold.GetChildren()) c.QueueFree();
        _gold.AddChild(Goods.Bar(App.Profile, 14));
        var next = p.NextToLearn;
        _learn.Visible = next is not null;
        if (next is null) return;
        var ready = p.LearnStatus(next) == NodeStatus.Available;
        _learn.Text = ready ? $"✦ Learn {next.Title} · {next.Cost} gold" : $"Next: {next.Title} · {next.Cost} gold";
        _learn.TooltipText = ready ? "You can afford it: learn it now" : $"You have {App.Profile.Gold} of {next.Cost} gold";
        var accent = ready ? Palette.Accent : Palette.Border;
        _learn.AddThemeStyleboxOverride("normal", Ui.Box(ready ? new Color(Palette.Accent, 0.18f) : Palette.Panel2, accent, 9, 7));
        _learn.AddThemeColorOverride("font_color", ready ? Palette.Accent : Palette.Muted);
    }

    private string Slot(string chassis) => Lesson is { } l ? Profile.LessonSlot(l.Id, chassis) : Profile.DelveSlot(chassis);

    // ----- Layout -----

    private void BuildUi()
    {
        var root = Ui.Column(10);
        var margin = Ui.Pad(root, 14);
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(margin);
        root.AddChild(BuildTopBar());

        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddThemeConstantOverride("separation", 12);
        root.AddChild(split);
        split.AddChild(BuildLeftColumn());
        split.AddChild(BuildRightColumn());
    }

    private Control BuildTopBar()
    {
        var back = Ui.Button("← Town");
        back.Pressed += () => Leave(() => App.GoTown());
        var row = Ui.Row(12, back);
        if (Lesson is { } lesson)
        {
            var codex = Ui.Button("← Lesson");
            codex.TooltipText = "Read the lesson pages again";
            codex.Pressed += () => Leave(() => App.GoCodex(lesson));
            row.AddChild(codex);
            row.AddChild(Ui.Column(0,
                Ui.Label($"CODEX {lesson.Tier} CHALLENGE", Palette.Accent, 11),
                Ui.Heading(lesson.Title, 22)));
        }
        else
        {
            row.AddChild(Ui.Column(0,
                Ui.Label("DELVE", Palette.Accent, 11),
                Ui.Heading(Site!.Name, 22)));
        }
        row.AddChild(Ui.Spacer());
        _learn = Ui.Button("");
        _learn.Pressed += () =>
        {
            if (App.Progress.NextToLearn is { } next) Help.Topic(App, next);
        };
        row.AddChild(_learn);
        _gold = Ui.Row(0);
        row.AddChild(_gold);
        row.AddChild(Help.Menu(App));
        _run = Ui.Button(Lesson is null ? "▶  Delve" : "▶  Run", "Run your code and watch the replay (Ctrl+Enter)", primary: true);
        _run.CustomMinimumSize = new Vector2(130, 40);
        _run.AddThemeFontSizeOverride("font_size", 16);
        _run.Pressed += () => Run(apply: true);
        row.AddChild(_run);
        return row;
    }

    private Control BuildLeftColumn()
    {
        _meta = Ui.Label("", Palette.Muted, 12);
        _view = new DungeonView3D
        {
            CustomMinimumSize = new Vector2(560, 360),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        var frame = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        frame.AddThemeStyleboxOverride("panel", Ui.Box(Palette.Void, Palette.Border, 10, 2));
        frame.AddChild(_view);

        _play = Ui.Button("Play");
        _play.CustomMinimumSize = new Vector2(70, 0);
        _play.Pressed += TogglePlay;
        _slider = new HSlider { MinValue = 0, MaxValue = 1, Step = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter, TooltipText = "Replay timeline: drag to scrub" };
        _slider.ValueChanged += v =>
        {
            _playhead = v;
            _playing = false;
            RefreshAll();
        };
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
        _skip = Ui.Button("Skip to end");
        _skip.Pressed += () =>
        {
            if (_timeline is null) return;
            _playhead = _timeline.LastTick;
            _playing = false;
            RefreshAll();
        };

        var dungeon = Ui.Card(Ui.Column(8, frame, Ui.Row(8, _play, _slider, speeds, _skip), _meta), expand: true);
        dungeon.SizeFlagsStretchRatio = 3f;

        _logMeta = Ui.Label("", Palette.Muted, 12);
        _log = new RichTextLabel { SizeFlagsVertical = SizeFlags.ExpandFill, ScrollFollowing = true, SelectionEnabled = true, BbcodeEnabled = false };
        var log = Ui.Card(Ui.Column(6, Ui.Row(8, Ui.Label("Delve log", Palette.Text, 14), Ui.Label("print() writes here", Palette.Muted, 11), Ui.Spacer(), _logMeta), _log), expand: true);

        var col = Ui.Column(12, dungeon, log);
        col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        col.SizeFlagsStretchRatio = 1.45f;
        return col;
    }

    private Control BuildRightColumn()
    {
        _goals = Ui.Column(4);
        var goalsCard = Ui.Card(_goals);
        RefreshGoals(null);

        _tabs = new TabBar { FocusMode = FocusModeEnum.None, TabAlignment = TabBar.AlignmentMode.Left, ClipTabs = false };
        foreach (var id in _party) _tabs.AddTab(id + ".glyph");
        _tabs.TabChanged += i => SwitchTab(_party[(int)i]);
        _status = Ui.Label("", Palette.Muted, 12);

        var reset = Ui.Button("Reset", Lesson is null ? "Start over from the code of your latest challenge, or the starter program" : "Put the lesson's starting code back");
        reset.Pressed += ResetCode;
        _stuck = Ui.Button("Stuck?", "Show one way to solve it");
        _stuck.Pressed += ShowSolution;
        _stuck.Visible = false;

        _editor = new GlyphEditor();
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
        strip.AddThemeStyleboxOverride("panel", Ui.Box(Palette.Panel2, Palette.Border, 8, 6));
        strip.AddChild(Ui.Row(10, _wordSig, _wordDoc, more));
        _wordRow = strip;

        var editorCard = Ui.Card(Ui.Column(8,
            Ui.Row(8, _tabs, Ui.Spacer(), _status, _stuck, reset),
            _editor,
            _wordRow,
            _hint), expand: true);
        editorCard.SizeFlagsStretchRatio = 2.4f;

        _inspector = new RichTextLabel { Name = "Inspector", BbcodeEnabled = true, SelectionEnabled = true };
        _reference = new RichTextLabel { Name = "Functions", BbcodeEnabled = true, SelectionEnabled = true, Text = ReferenceText(), MetaUnderlined = false };
        _reference.MetaClicked += meta => Help.Word(App, meta.AsString());
        var bottom = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        bottom.AddChild(_inspector);
        bottom.AddChild(_reference);

        var col = Ui.Column(10, goalsCard, editorCard, bottom);
        col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return col;
    }

    private void RefreshGoals(ChallengeResult? result)
    {
        foreach (var c in _goals.GetChildren()) c.QueueFree();
        var p = App.Progress;
        if (Lesson is { } lesson)
        {
            _goals.AddChild(Ui.Para(lesson.Challenge.Brief, Palette.Text, 14));
            var checks = result?.Checks ?? Challenge.Goals(lesson);
            foreach (var c in checks)
            {
                var (mark, color) = result is null ? ("○", Palette.Text) : c.Ok ? ("✓", Palette.Ok) : ("✗", Palette.Danger);
                _goals.AddChild(Ui.Label($"{mark}  {c.Text}", color, 14));
            }
            if (p.IsCompleted(lesson)) _goals.AddChild(Ui.Label("Completed. Replays are free practice.", Palette.Ok, 12));
        }
        else
        {
            var site = Site!;
            _goals.AddChild(Ui.Para(site.Description, Palette.Text, 14));
            var l = _loadout;
            var gear = new List<string>();
            if (l.Hp > 0) gear.Add($"+{l.Hp} HP");
            if (l.Armor > 0) gear.Add($"+{l.Armor} armor");
            if (l.Attack > 0) gear.Add($"+{l.Attack} attack");
            if (l.Sight > 0) gear.Add($"+{l.Sight} sight");
            if (l.Budget > 0) gear.Add($"+{l.Budget} instructions");
            if (l.Speed > 0) gear.Add("faster steps");
            if (l.Mana > 0) gear.Add($"{l.Mana} mana");
            if (l.Spells.Count > 0) gear.Add("spells: " + string.Join(", ", l.Spells.Select(s => s + "()")));
            _goals.AddChild(Ui.Para("Equipment: " + (gear.Count == 0 ? "none yet (visit the Forge)" : string.Join(" · ", gear)), Palette.Muted, 13));
            var bonus = p.GoldPercent > 0 ? $" +{p.GoldPercent}% market bonus" : "";
            var income = p.Income > 0 ? $" +{p.Income} village income" : "";
            _goals.AddChild(Ui.Label($"You keep all gold the golems carry home{bonus}{income}. Next dungeon: seed {App.Profile.Seed}.", Palette.Accent, 13));
        }
    }

    private static string Esc(string s) => s.Replace("[", "[lb]", StringComparison.Ordinal);

    private static string Hex(Color c) => c.ToHtml(false);

    private string ReferenceText()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"[color=#{Hex(Palette.Muted)}]What this golem core knows ({Tiers.Describe(_tier)}). Click a name for an example. Actions end the golem's turn and take a few ticks.[/color]\n\n");
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
            setup = Lesson is { } lesson ? Challenge.Setup(App.Content, lesson, _sources) : p.SiteSetup(Site!, App.Profile.Seed);
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
        _pendingResult = null;
        if (apply)
        {
            App.Audio.Play(Sfx.Click);
            _attempts++;
            _pendingResult = Lesson is { } l ? JudgeLesson(l) : JudgeSite();
            App.Save();
        }
        RefreshGoals(null);
        RefreshTabs();
        RefreshAll();
        RefreshInspector();
    }

    private Action JudgeLesson(LessonDef lesson)
    {
        var p = App.Progress;
        var outcome = _timeline!.Outcome;
        var result = Challenge.Evaluate(lesson, _ranSources, _timeline.Final);
        var reward = p.LessonReward(lesson, result, outcome);
        var firstPass = result.Passed && !p.IsCompleted(lesson);
        if (firstPass)
        {
            p.Complete(lesson);
            p.Apply(reward);
        }
        return () => ShowLessonResult(lesson, result, reward, firstPass);
    }

    private Action JudgeSite()
    {
        var p = App.Progress;
        var outcome = _timeline!.Outcome;
        var reward = p.SiteReward(outcome, Site);
        var seed = App.Profile.Seed;
        p.Apply(reward);
        var shifts = p.RunWorkshops();
        App.Profile.Seed++;
        return () => ShowSiteResult(outcome, reward, seed, shifts);
    }

    private void TogglePlay()
    {
        if (_timeline is null) return;
        if (_playhead >= _timeline.LastTick) _playhead = 0;
        _playing = !_playing;
        _soundTick = CurrentTick;
        RefreshAll();
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
        var p = App.Progress;
        var code = Lesson is { } l ? l.Challenge.Starter
            : p.Lessons.LastOrDefault(x => x.Tier >= Tiers.Conditions && p.IsCompleted(x))?.Challenge.Solution ?? App.Content.Programs.GetValueOrDefault(Progression.FirstProgram, "");
        _editor.ReplaceText(code);
        _sources[_activeTab] = code;
        Persist();
        LiveCompile();
    }

    private void ShowSolution()
    {
        if (Lesson is not { } lesson) return;
        var use = Ui.Button("Use this code", primary: true);
        var close = Ui.Button("I'll try myself");
        var modal = App.ShowModal(Ui.Column(12,
            Ui.Heading("One way to solve it", 22, Palette.Accent),
            Ui.Para("Read it line by line and compare it with yours. There are many right answers: this is just one of them.", Palette.Muted, 14),
            GlyphEditor.Snippet(lesson.Challenge.Solution.TrimEnd()),
            Ui.Row(8, Ui.Spacer(), close, use)), 620);
        close.Pressed += () => App.CloseModal(modal);
        use.Pressed += () =>
        {
            App.CloseModal(modal);
            _editor.ReplaceText(lesson.Challenge.Solution);
            _sources[_activeTab] = lesson.Challenge.Solution;
            Persist();
            LiveCompile();
        };
    }

    private void Persist()
    {
        if (_editor is not null) _sources[_activeTab] = _editor.Text;
        foreach (var (id, src) in _sources) App.Profile.Programs[Slot(id)] = src;
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

    private static Control CheckList(IEnumerable<ChallengeCheck> checks)
    {
        var col = Ui.Column(4);
        foreach (var c in checks) col.AddChild(Ui.Label($"{(c.Ok ? "✓" : "✗")}  {c.Text}", c.Ok ? Palette.Ok : Palette.Danger, 15));
        return col;
    }

    private void ShowLessonResult(LessonDef lesson, ChallengeResult result, DelveReward reward, bool firstPass)
    {
        RefreshGoals(result);
        App.ToastFinds();
        var p = App.Progress;
        var body = Ui.Column(12);
        var buttons = Ui.Row(8, Ui.Spacer());
        Control modal = null!;
        if (result.Passed)
        {
            App.Audio.Play(Sfx.Unlock);
            body.AddChild(Ui.Label(firstPass ? $"CODEX {lesson.Tier} CHALLENGE COMPLETE" : "PASSED AGAIN", Palette.Ok, 12));
            body.AddChild(Ui.Heading(firstPass ? "Challenge passed!" : "Still works!", 30, Palette.Accent));
            body.AddChild(CheckList(result.Checks));
            if (firstPass)
            {
                body.AddChild(Ui.Para($"You've mastered {lesson.Title}. Your free delves can use this code as a starting point.", Palette.Text, 15));
                body.AddChild(Ui.Label($"+{reward.Total} gold  ({reward.LessonReward} bonus + {reward.Loot} loot)", Palette.Accent, 17));
                App.Audio.Play(Sfx.Coin);
            }
            RefreshWallet();
            if (p.NextToLearn is { } learn && p.LearnStatus(learn) == NodeStatus.Available)
            {
                var buy = Ui.Button($"Learn {learn.Title} · {learn.Cost} gold");
                buy.Pressed += () =>
                {
                    App.CloseModal(modal);
                    App.Learn(learn);
                };
                buttons.AddChild(buy);
            }
            var next = p.NextChallenge;
            if (next is not null && next != lesson)
            {
                var go = Ui.Button($"Next challenge: {next.Title}", primary: true);
                go.Pressed += () => Leave(() => App.GoLesson(next));
                buttons.AddChild(go);
            }
            var town = Ui.Button("Back to town", primary: next is null || next == lesson);
            town.Pressed += () => Leave(() => App.GoTown());
            buttons.AddChild(town);
            var stay = Ui.Button("Stay here");
            stay.Pressed += () => App.CloseModal(modal);
            buttons.AddChild(stay);
        }
        else
        {
            App.Audio.Play(Sfx.Fail);
            body.AddChild(Ui.Label("NOT YET", Palette.Danger, 12));
            body.AddChild(Ui.Heading("Almost: not every goal was met", 26));
            body.AddChild(CheckList(result.Checks));
            var halted = _timeline!.Final.Golems.FirstOrDefault(g => g.Error is not null);
            if (halted?.Error is { } err)
            {
                body.AddChild(Ui.Para($"{halted.Name} stopped at line {err.Line}: {err.Message}", Palette.Danger, 14));
            }
            body.AddChild(Ui.Para("Drag the timeline back to watch where it went wrong. The Inspector shows every variable at that moment, and the yellow arrow in the editor shows the line the golem was running.", Palette.Muted, 14));
            if (_attempts >= 2)
            {
                var solution = Ui.Button("Show a solution");
                solution.Pressed += () =>
                {
                    App.CloseModal(modal);
                    ShowSolution();
                };
                buttons.AddChild(solution);
            }
            var reread = Ui.Button("Re-read the lesson");
            reread.Pressed += () => Leave(() => App.GoCodex(lesson));
            buttons.AddChild(reread);
            var retry = Ui.Button("Fix my code", primary: true);
            retry.Pressed += () => App.CloseModal(modal);
            buttons.AddChild(retry);
            _stuck.Visible = _attempts >= 2;
        }
        body.AddChild(buttons);
        modal = App.ShowModal(body, 600);
    }

    private void ShowSiteResult(Outcome outcome, DelveReward reward, ulong seed, IReadOnlyList<Core.Village.WorkshopResult> shifts)
    {
        App.Audio.Play(outcome.Kind == OutcomeKind.Wiped ? Sfx.Fail : Sfx.Coin);
        App.ToastFinds();
        Control modal = null!;
        var lines = Ui.Column(4,
            Ui.Label($"Loot carried home: {reward.Loot}", Palette.Text, 15),
            Ui.Label($"Market bonus: {reward.Bonus}", reward.Bonus > 0 ? Palette.Text : Palette.Muted, 15),
            Ui.Label($"Village income: {reward.Income}", reward.Income > 0 ? Palette.Text : Palette.Muted, 15));
        var mined = outcome.Goods ?? new Dictionary<string, int>();
        var produced = App.Progress.Production();
        var materials = Ui.Column(6);
        if (mined.Count > 0) materials.AddChild(Ui.Row(10, Ui.Label("Brought home:", Palette.Muted, 14), Goods.Gains(mined)));
        if (produced.Count > 0) materials.AddChild(Ui.Row(10, Ui.Label("Village:", Palette.Muted, 14), Goods.Gains(produced)));
        foreach (var s in shifts)
        {
            var open = Ui.Button($"{s.Def.Name} ▸", $"Open the {s.Def.Name} to change its script");
            var def = s.Def;
            open.Pressed += () => Leave(() => App.GoWorkshop(def));
            var what = s.Error is not null && s.Made.Count == 0 ? Ui.Label("script error: nothing made", Palette.Danger, 14)
                : s.Made.Count == 0 ? Ui.Label("nothing made (out of materials?)", Palette.Muted, 14)
                : (Control)Goods.Gains(s.Made);
            materials.AddChild(Ui.Row(10, open, what, s.Used.Count > 0 ? Ui.Label("from " + Resources.Format(s.Used), Palette.Muted, 13) : new Control()));
        }
        var again = Ui.Button("Delve again", primary: true);
        again.Pressed += () =>
        {
            App.CloseModal(modal);
            RefreshGoals(null);
            Run(apply: true);
        };
        var town = Ui.Button("Back to town");
        town.Pressed += () => Leave(() => App.GoTown());
        var stay = Ui.Button("Watch the replay");
        stay.Pressed += () => App.CloseModal(modal);
        var tip = outcome.Kind switch
        {
            OutcomeKind.Wiped => "Every golem broke, so nothing came home. Recall earlier, avoid fights you can't win, or buy plating at the Forge.",
            OutcomeKind.Costly => "Some golems broke. Their loot was lost with them. A low_hp handler that calls recall() saves a lot of gold.",
            _ when mined.Count == 0 => "Veins of stone, iron ore and old timber glint in the mine walls. Stand next to one and mine() it: the village needs materials as well as gold.",
            _ => "Tune your code to open more chests, mine more veins and lose fewer golems. Better code brings more home.",
        };
        var summary = outcome.Summary.Split(" Brought home:")[0];
        var body = Ui.Column(12,
            Ui.Label($"DELVE REPORT · SEED {seed}", Palette.Accent, 12),
            Ui.Heading(summary, 24),
            lines,
            Ui.Label($"+{reward.Total} gold  (you now have {App.Profile.Gold})", Palette.Accent, 18));
        if (materials.GetChildCount() > 0) body.AddChild(materials);
        body.AddChild(Ui.Para(tip, Palette.Muted, 14));
        var p = App.Progress;
        if (p.NextToLearn is { } learn)
        {
            if (p.LearnStatus(learn) == NodeStatus.Available)
            {
                var buy = Ui.Button($"✦ Learn {learn.Title} · {learn.Cost} gold", primary: true);
                buy.Pressed += () =>
                {
                    App.CloseModal(modal);
                    App.Learn(learn);
                };
                body.AddChild(Ui.Card(Ui.Row(10, Ui.Para($"You can afford {learn.Title}: {learn.Summary}", Palette.Text, 14), buy)));
            }
            else
            {
                var bar = new ProgressBar { MaxValue = learn.Cost, Value = App.Profile.Gold, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8), SizeFlagsHorizontal = SizeFlags.ExpandFill };
                body.AddChild(Ui.Column(4, Ui.Label($"Saving for {learn.Title}: {App.Profile.Gold} of {learn.Cost} gold", Palette.Muted, 13), bar));
            }
        }
        body.AddChild(Ui.Row(8, Ui.Spacer(), stay, town, again));
        modal = App.ShowModal(body, 580);
        RefreshWallet();
        RefreshGoals(null);
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

    private void RefreshAll()
    {
        _play.Text = _playing ? "Pause" : "Play";
        _play.Disabled = _timeline is null;
        _skip.Disabled = _timeline is null;
        _view.Display(_timeline, _playhead);
        if (_timeline is null) return;
        var tick = CurrentTick;
        _slider.SetValueNoSignal(tick);
        _meta.Text = $"{tick / (double)World.TicksPerSecond:0.0}s of {_timeline.LastTick / (double)World.TicksPerSecond:0.0}s · tick {tick} · right-drag to look around, scroll to zoom";
        PlaySounds(tick);
        RefreshMarkers();
        RefreshLog(tick);
        RefreshStatus(tick);
        RefreshHint();
        if (_inspected != (tick, _activeTab) && _inspectDue <= 0) _inspectDue = _playing ? 0.3 : 0.08;
        ShowResultIfDone();
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
        if (Lesson is { Challenge.MaxLines: > 0 } l)
        {
            var lines = Challenge.CountLines(_editor.Text);
            text = $"Lines: {lines} of {l.Challenge.MaxLines}" + (text.Length > 0 ? "  ·  " + text : "");
            if (lines > l.Challenge.MaxLines && _liveError is null) color = Palette.Accent;
        }
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
        var bag = g.Bag is { } b && b.Sum() > 0
            ? " · " + string.Join(", ", Enumerable.Range(0, b.Count).Where(i => b[i] > 0).Select(i => $"{b[i]} {Resources.Name(Resources.Mined[i])}"))
            : "";
        _status.Text = $"{g.State.ToString().ToLowerInvariant()} · HP {g.Hp}/{g.MaxHp}{mana} · {g.Loot} gold{bag}{extra}";
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
                var none = _tier < Tiers.Variables ? "(learn Variables in the Library to store values)" : "(none yet)";
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
