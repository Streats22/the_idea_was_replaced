using System.Globalization;
using System.Text;
using Delvework.Core.Content;
using Delvework.Core.Glyph;
using Delvework.Core.Progress;
using Delvework.Core.Village;
using Delvework.Game.Audio;
using Godot;

namespace Delvework.Game.Screens;

/// <summary>
/// A village workshop (Farm, Smelter, Bakery): write its script, watch a preview shift on the
/// current stock, and run a shift for real. Built workshops also run one shift after every delve.
/// With a <see cref="Commission"/> the same screen grades a script on a fixed order instead.
/// </summary>
public partial class WorkshopScreen : Control
{
    private static readonly int[] Speeds = [1, 3, 8];
    private const double TicksPerSecond = 10;

    public required App App { get; init; }
    public required WorkshopDef Workshop { get; init; }
    public CommissionDef? Commission { get; init; }

    private WorkshopKind _kind = null!;
    private WorkshopResult? _result;
    private CommissionScore? _score;
    private Label _best = null!;
    private string _ranSource = "";
    private bool _applied;
    private double _playhead;
    private bool _playing;
    private int _speed = 3;
    private double _saveDue = -1, _previewDue = -1;
    private int _logShown = -1;
    private GlyphError? _liveError;
    private (int Exec, int Err) _markers = (-1, -1);

    private WorkshopView _view = null!;
    private GlyphEditor _editor = null!;
    private HSlider _slider = null!;
    private Button _play = null!, _run = null!;
    private Label _meta = null!, _hint = null!, _preview = null!;
    private HBoxContainer _stock = null!;
    private RichTextLabel _log = null!, _reference = null!;

    public WorkshopView View => _view;
    public WorkshopResult? Result => _result;

    public override void _Ready()
    {
        _kind = Workshops.KindOf(Workshop);
        BuildUi();
        _editor.Environment = _kind.Environment;
        _editor.Tier = App.Progress.KnownTier;
        _editor.ReplaceText(Commission is { } c ? App.Progress.CommissionProgram(c) : App.Progress.WorkshopProgram(Workshop));
        RefreshStock();
        Preview();
        App.Learned += OnLearned;
    }

    public override void _ExitTree() => App.Learned -= OnLearned;

    private void OnLearned()
    {
        _editor.Tier = App.Progress.KnownTier;
        _reference.Text = ReferenceText();
        RefreshStock();
        Preview();
    }

    private bool Built => Commission is { } c ? App.Progress.IsOpen(c) : App.Progress.IsBuilt(Workshop);

    private string Slot => Commission is { } c ? Commissions.Slot(c) : Profile.WorkshopSlot(Workshop.Id);

    private static readonly Color[] MedalColors = [Palette.Muted, new("c08a5a"), new("c8d0d8"), new("ffd24a")];

    public static Color MedalColor(Medal m) => MedalColors[(int)m];

    public static string MedalName(Medal m) => m == Medal.None ? "no medal" : m.ToString().ToLowerInvariant();

    // ----- Layout -----

    private void BuildUi()
    {
        var root = Ui.Column(10);
        var margin = Ui.Pad(root, 14);
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(margin);

        var back = Ui.Button(Commission is null ? "← Town" : "← Commissions");
        back.Pressed += () =>
        {
            App.Audio.Play(Sfx.Click);
            Persist();
            if (Commission is null) App.GoTown();
            else App.GoCommissions(Commission);
        };
        var how = Ui.Button("How it works", "The rules of this workshop and its functions");
        how.Pressed += () => Help.Workshop(App, Workshop);
        _stock = Ui.Row(0);
        _run = Commission is null
            ? Ui.Button("▶  Run shift", "Run one shift for real: materials go in, goods come out (Ctrl+Enter)", primary: true)
            : Ui.Button("✔  Submit", "Hand the script in: it is graded on speed and size, and your best result is kept (Ctrl+Enter)", primary: true);
        _run.CustomMinimumSize = new Vector2(140, 40);
        _run.AddThemeFontSizeOverride("font_size", 16);
        _run.Pressed += RunShift;
        var title = Commission is { } order
            ? Ui.Column(0, Ui.Label($"COMMISSION · {order.Client.ToUpperInvariant()} · {Workshop.Name.ToUpperInvariant()}", Palette.Accent, 11), Ui.Heading(order.Title, 22))
            : Ui.Column(0, Ui.Label("WORKSHOP", Palette.Accent, 11), Ui.Heading(Workshop.Name, 22));
        root.AddChild(Ui.Row(12, back, title, Ui.Spacer(), _stock, how, Help.Menu(App), _run));

        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddThemeConstantOverride("separation", 12);
        root.AddChild(split);

        _view = new WorkshopView { Kind = Workshop.Id, CustomMinimumSize = new Vector2(520, 320), SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var frame = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        frame.AddThemeStyleboxOverride("panel", Ui.Box(Palette.Void, Palette.Border, 10, 2));
        frame.AddChild(_view);
        _play = Ui.Button("Play");
        _play.CustomMinimumSize = new Vector2(70, 0);
        _play.Pressed += TogglePlay;
        _slider = new HSlider { MinValue = 0, MaxValue = 1, Step = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter, TooltipText = "Shift timeline: drag to scrub" };
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
        _meta = Ui.Label("", Palette.Muted, 12);
        var stage = Ui.Card(Ui.Column(8, frame, Ui.Row(8, _play, _slider, speeds), _meta), expand: true);
        stage.SizeFlagsStretchRatio = 2.2f;

        _log = new RichTextLabel { SizeFlagsVertical = SizeFlags.ExpandFill, ScrollFollowing = true, SelectionEnabled = true };
        var log = Ui.Card(Ui.Column(6, Ui.Row(8, Ui.Label("Shift log", Palette.Text, 14), Ui.Label("print() writes here", Palette.Muted, 11)), _log), expand: true);
        var left = Ui.Column(12, stage, log);
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        left.SizeFlagsStretchRatio = 1.3f;
        split.AddChild(left);

        _preview = Ui.Para("", Palette.Text, 14);
        _best = Ui.Label("", Palette.Muted, 13);
        var summary = Commission is { } c
            ? Ui.Card(Ui.Column(6,
                Ui.Para(c.Brief, Palette.Text, 14),
                Ui.Row(18,
                    Ui.Label($"Goal: {c.Goal.Amount} {Resources.Name(c.Goal.Resource)}", Palette.Accent, 14),
                    Ui.Label($"Speed: gold ≤ {c.Par.Ticks[0]} ticks, silver ≤ {c.Par.Ticks[1]}", Palette.Muted, 13),
                    Ui.Label($"Size: gold ≤ {c.Par.Lines[0]} lines, silver ≤ {c.Par.Lines[1]}", Palette.Muted, 13)),
                _best, _preview))
            : Ui.Card(Ui.Column(4, Ui.Para(Workshop.Summary, Palette.Muted, 13), _preview));

        _editor = new GlyphEditor();
        _editor.TextChanged += () =>
        {
            _saveDue = 0.4;
            _previewDue = 0.35;
        };
        _editor.Describe = word => Help.Brief(App, word, App.Progress.KnownTier, _kind.Environment);
        _editor.WordClicked += word => Help.Word(App, word, _kind.Environment);
        var reset = Ui.Button("Reset", "Put the starter script back");
        reset.Pressed += () =>
        {
            App.Audio.Play(Sfx.Click);
            _editor.ReplaceText(Workshop.Starter);
            Persist();
            Preview();
        };
        _hint = Ui.Label("", Palette.Muted, 12);
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var file = Commission is { } cc ? cc.Id + ".glyph" : Workshop.Id + ".glyph";
        var note = Commission is null ? "runs one shift after every delve" : "graded on the order's own stores";
        var editorCard = Ui.Card(Ui.Column(8,
            Ui.Row(8, Ui.Label(file, Palette.Text, 14), Ui.Label(note, Palette.Muted, 11), Ui.Spacer(), reset),
            _editor, _hint), expand: true);
        editorCard.SizeFlagsStretchRatio = 2.4f;

        _reference = new RichTextLabel { BbcodeEnabled = true, SelectionEnabled = true, Text = ReferenceText(), MetaUnderlined = false, SizeFlagsVertical = SizeFlags.ExpandFill };
        _reference.MetaClicked += meta => Help.Word(App, meta.AsString(), _kind.Environment);
        var refCard = Ui.Card(Ui.Column(6, Ui.Label("Functions", Palette.Text, 14), _reference), expand: true);

        var right = Ui.Column(10, summary, editorCard, refCard);
        right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        split.AddChild(right);
    }

    private static string Hex(Color c) => c.ToHtml(false);

    private static string Esc(string s) => s.Replace("[", "[lb]", StringComparison.Ordinal);

    private string ReferenceText()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"[color=#{Hex(Palette.Muted)}]Every action takes one tick. Click a name for details.[/color]\n\n");
        var stdlib = Stdlib.Functions.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var b in _kind.Environment.Builtins.Where(b => b.Signature.Length > 0 && (!stdlib.Contains(b.Name) || b.Name == "wait") && b.Tier <= App.Progress.KnownTier))
        {
            var kind = b.IsAction ? " [color=#f07178]action[/color]" : "";
            sb.Append(CultureInfo.InvariantCulture, $"[url={b.Name}][color=#{Hex(Palette.Accent)}]{Esc(b.Signature)}[/color][/url]{kind}\n");
            sb.Append(CultureInfo.InvariantCulture, $"[color=#{Hex(Palette.Muted)}]    {Esc(b.Doc)}[/color]\n");
        }
        return sb.ToString();
    }

    private void RefreshStock()
    {
        foreach (var c in _stock.GetChildren()) c.QueueFree();
        var row = Ui.Row(14);
        if (Commission is { } order)
        {
            row.AddChild(Ui.Label("Order stores:", Palette.Muted, 13));
            foreach (var id in _kind.Inputs) row.AddChild(Goods.Chip(id, order.Stock.GetValueOrDefault(id), 15));
            RefreshBest();
        }
        else
        {
            foreach (var id in _kind.Inputs.Concat(_kind.Outputs)) row.AddChild(Goods.Chip(id, App.Profile.Amount(id), 15));
        }
        _stock.AddChild(row);
        _run.Disabled = !Built;
        if (!Built) _run.TooltipText = $"Build the {Workshop.Name} at the Village notice board first";
    }

    private void RefreshBest()
    {
        if (Commission is not { } c) return;
        var best = App.Progress.Best(c);
        _best.Text = best is null
            ? $"Not delivered yet. First delivery pays {Resources.Format(c.Reward)}; each gold medal adds {c.GoldMedalBonus} gold."
            : $"Your best: {best.Ticks} ticks ({MedalName(best.TicksMedal)}) · {best.Lines} lines ({MedalName(best.LinesMedal)})";
        _best.AddThemeColorOverride("font_color", best is null ? Palette.Muted : MedalColor((Medal)Math.Min((int)best.TicksMedal, (int)best.LinesMedal)));
    }

    // ----- Running -----

    private void Persist()
    {
        _saveDue = -1;
        if (_editor.Text == Workshop.Starter) App.Profile.Programs.Remove(Slot);
        else App.Profile.Programs[Slot] = _editor.Text;
        App.Save();
    }

    /// <summary>Run the script on the current stock without keeping anything, and play it.</summary>
    private void Preview()
    {
        _previewDue = -1;
        _liveError = null;
        try
        {
            Workshops.Compile(Workshop, _editor.Text, App.Progress.KnownTier);
        }
        catch (GlyphError e)
        {
            _liveError = e;
        }
        if (Commission is { } c)
        {
            _score = App.Progress.TryCommission(c, _editor.Text);
            Present(_score.Result, applied: false, play: _result is null || !_applied);
        }
        else
        {
            Present(App.Progress.TryShift(Workshop, _editor.Text), applied: false, play: _result is null || !_applied);
        }
    }

    private void RunShift()
    {
        if (!Built) return;
        Persist();
        App.Audio.Play(Sfx.Click);
        if (Commission is { } c)
        {
            var (score, paid) = App.Progress.SubmitCommission(c, _editor.Text);
            _score = score;
            App.Audio.Play(score.Passed ? Sfx.Unlock : Sfx.Fail);
            if (score.Passed)
            {
                var medals = $"{MedalName(score.TicksMedal)} for speed, {MedalName(score.LinesMedal)} for size";
                App.Toast(paid.Count > 0 ? $"DELIVERED · +{Resources.Format(paid)}" : "DELIVERED", $"{c.Title}: {medals}.", MedalColor((Medal)Math.Min((int)score.TicksMedal, (int)score.LinesMedal)));
            }
            App.Save();
            App.ToastFinds();
            Present(score.Result, applied: true, play: true);
            RefreshStock();
            return;
        }
        var result = App.Progress.Shift(Workshop, _editor.Text);
        App.Save();
        App.ToastFinds();
        Present(result, applied: true, play: true);
        RefreshStock();
    }

    private void Present(WorkshopResult result, bool applied, bool play)
    {
        _result = result;
        _applied = applied;
        _ranSource = _editor.Text;
        _playhead = 0;
        _playing = play;
        _logShown = -1;
        _slider.MaxValue = Math.Max(1, result.LastTick);
        _slider.SetValueNoSignal(0);
        var made = result.Made.Count == 0 ? "nothing" : Resources.Format(result.Made);
        var used = result.Used.Count == 0 ? "" : $" from {Resources.Format(result.Used)}";
        if (Commission is { } c && _score is { } s)
        {
            _preview.Text = !s.Passed ? s.Summary
                : $"{(applied ? "Delivered" : "This script delivers")} {c.Goal.Amount} {Resources.Name(c.Goal.Resource)} after {s.Ticks} ticks ({MedalName(s.TicksMedal)}) with {s.Lines} lines ({MedalName(s.LinesMedal)}).";
            _preview.AddThemeColorOverride("font_color", !s.Passed ? (result.Error is not null ? Palette.Danger : Palette.Text) : MedalColor((Medal)Math.Min((int)s.TicksMedal, (int)s.LinesMedal)));
            RefreshHint();
            RefreshAll();
            return;
        }
        _preview.Text = result.Error is not null && result.Frames.Count <= 1
            ? $"The script has an error on line {result.Error.Line}: {result.Error.Message}"
            : applied ? $"Shift done: made {made}{used}. It's in your stores."
            : $"With what's in your stores now, one shift makes {made}{used}.";
        _preview.AddThemeColorOverride("font_color", applied ? Palette.Ok : result.Error is not null ? Palette.Danger : Palette.Text);
        RefreshHint();
        RefreshAll();
    }

    private void TogglePlay()
    {
        if (_result is null) return;
        if (_playhead >= _result.LastTick) _playhead = 0;
        _playing = !_playing;
        RefreshAll();
    }

    public override void _Input(InputEvent e)
    {
        if (App.ModalOpen) return;
        if (e is InputEventKey { Pressed: true, CtrlPressed: true } key && key.Keycode is Key.Enter or Key.KpEnter)
        {
            RunShift();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (_result is not null && _playing)
        {
            _playhead = Math.Min(_result.LastTick, _playhead + delta * TicksPerSecond * _speed);
            if (_playhead >= _result.LastTick) _playing = false;
            RefreshAll();
        }
        if (_saveDue > 0 && (_saveDue -= delta) <= 0) Persist();
        if (_previewDue > 0 && (_previewDue -= delta) <= 0) Preview();
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
        if (_result is null) return;
        var tick = (int)Math.Floor(_playhead);
        var frame = _result.At(tick);
        _slider.SetValueNoSignal(tick);
        _view.Display(frame, _result.Frames.Take(tick + 1).LastOrDefault(f => f.Event is not null)?.Event, _playing);
        var mode = Commission is not null ? (_applied ? "submitted" : "preview on the order's stores") : _applied ? "this shift" : "preview on your current stores";
        _meta.Text = $"{tick / TicksPerSecond:0.0}s of {_result.LastTick / TicksPerSecond:0.0}s · {mode}";
        RefreshLog(tick);
        var unchanged = _ranSource == _editor.Text;
        var exec = unchanged ? frame.Line : 0;
        var err = _liveError?.Line ?? (unchanged && tick >= _result.LastTick ? _result.Error?.Line ?? 0 : 0);
        if (_markers != (exec, err))
        {
            _markers = (exec, err);
            _editor.SetMarkers(exec, err);
        }
    }

    private void RefreshHint()
    {
        if (_liveError is { } e)
        {
            _hint.Text = $"Line {e.Line}: {e.Message}";
            _hint.AddThemeColorOverride("font_color", Palette.Danger);
        }
        else if (_result?.Error is { } r)
        {
            _hint.Text = $"The script stopped on line {r.Line}: {r.Message}";
            _hint.AddThemeColorOverride("font_color", Palette.Danger);
        }
        else
        {
            _hint.Text = Built ? "" : $"Not built yet: buy the {Workshop.Name} at the Village notice board to deliver. You can try scripts here already.";
            _hint.AddThemeColorOverride("font_color", Palette.Muted);
        }
    }

    private void RefreshLog(int tick)
    {
        if (_result is null) return;
        var shown = _result.Log.Count(l => ParseTick(l) <= tick);
        if (shown == _logShown) return;
        _logShown = shown;
        _log.Clear();
        foreach (var line in _result.Log.Take(shown))
        {
            var split = line.IndexOf("  ", StringComparison.Ordinal);
            _log.PushColor(Palette.Muted.Darkened(0.2f));
            _log.AddText(line[..(split + 2)]);
            _log.Pop();
            var text = line[(split + 2)..];
            _log.PushColor(text.StartsWith("print:", StringComparison.Ordinal) ? Palette.Text
                : text.StartsWith("The script", StringComparison.Ordinal) || text.StartsWith("Burnt", StringComparison.Ordinal) ? Palette.Danger
                : Palette.Muted);
            _log.AddText(text);
            _log.Pop();
            _log.Newline();
        }
    }

    private static int ParseTick(string line)
    {
        var s = line.IndexOf('s', StringComparison.Ordinal);
        return s > 0 && double.TryParse(line[..s], NumberStyles.Float, CultureInfo.InvariantCulture, out var secs) ? (int)Math.Round(secs * TicksPerSecond) : 0;
    }
}

/// <summary>A little animated field, furnace or oven, drawn from one shift frame.</summary>
public partial class WorkshopView : Control
{
    public string Kind { get; init; } = Smelter.Id;

    private IReadOnlyDictionary<string, int> _state = new Dictionary<string, int>();
    private IReadOnlyList<int>? _tiles;
    private string? _event;
    private bool _animate;
    private double _time;
    private Vector2 _drone = new(-1, -1);

    public void Display(WorkshopFrame frame, string? lastEvent, bool playing)
    {
        _state = frame.State;
        _tiles = frame.Tiles;
        _event = frame.Event ?? lastEvent;
        _animate = playing;
        QueueRedraw();
    }

    private int S(string key) => _state.GetValueOrDefault(key);

    public override void _Process(double delta)
    {
        _time += delta;
        if (Kind == Farm.Id)
        {
            var target = new Vector2(S("x"), S("y"));
            var wrapped = _drone.X < 0 || _drone.DistanceTo(target) > 1.5f;
            _drone = wrapped ? target : _drone.Lerp(target, (float)Math.Min(1, delta * 14));
            QueueRedraw();
        }
        else if (S("heat") > 0 || S("fire") > 0) QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        if (Kind == Farm.Id)
        {
            DrawFarm(size);
        }
        else
        {
            DrawRect(new Rect2(Vector2.Zero, size), Color.FromHtml("#14100d"));
            for (var y = 0f; y < size.Y * 0.72f; y += 26)
            {
                var offset = (int)(y / 26) % 2 == 0 ? 0 : 30;
                for (var x = -offset; x < size.X; x += 60) DrawRect(new Rect2(x + 2, y + 2, 56, 22), Color.FromHtml("#1c1612"));
            }
            DrawRect(new Rect2(0, size.Y * 0.72f, size.X, size.Y * 0.28f), Color.FromHtml("#241c16"));
            if (Kind == Bakery.Id) DrawBakery(size);
            else DrawSmelter(size);
        }
        if (_event is { } e)
        {
            var font = ThemeDB.FallbackFont;
            var w = font.GetStringSize(e, HorizontalAlignment.Left, -1, 15).X + 24;
            var r = new Rect2((size.X - w) / 2, size.Y - 40, w, 28);
            DrawStyleBox(Ui.Box(new Color(Palette.Panel, 0.92f), Palette.Accent.Darkened(0.4f), 8, 0), r);
            DrawString(font, new Vector2(r.Position.X + 12, r.Position.Y + 19), e, HorizontalAlignment.Left, -1, 15, Palette.Text);
        }
    }

    private float Flicker => _animate || S("heat") > 0 || S("fire") > 0 ? 0.85f + 0.15f * MathF.Sin((float)_time * 11f) * MathF.Sin((float)_time * 4.3f) : 1f;

    private void Fire(Vector2 center, float width, float strength)
    {
        if (strength <= 0) return;
        var h = width * (0.35f + 0.65f * strength) * Flicker;
        for (var i = 0; i < 5; i++)
        {
            var t = i / 4f;
            var w = width * (1 - t * 0.55f);
            var color = new Color(1f, 0.35f + 0.5f * t, 0.1f + 0.3f * t, 0.35f + 0.13f * i);
            var sway = MathF.Sin((float)_time * 7 + i) * 3 * strength;
            DrawColoredPolygon([center + new Vector2(-w / 2, 0), center + new Vector2(sway, -h * (1 - t * 0.35f)), center + new Vector2(w / 2, 0)], color);
        }
        DrawCircle(center, width * 0.8f, new Color(1f, 0.5f, 0.15f, 0.12f * strength));
    }

    private void Pile(Vector2 origin, string id, int count, string label)
    {
        var color = Goods.ColorOf(id);
        var shown = Math.Min(count, 15);
        for (var i = 0; i < shown; i++)
        {
            var row = i / 5;
            var col = i % 5;
            var p = origin + new Vector2(col * 16 + row * 8, -row * 12);
            switch (id)
            {
                case Resources.Iron:
                    DrawRect(new Rect2(p.X, p.Y - 8, 15, 8), color);
                    DrawRect(new Rect2(p.X + 2, p.Y - 8, 11, 3), color.Lightened(0.35f));
                    break;
                case Resources.Wood:
                    DrawCircle(p + new Vector2(7, -6), 6.5f, color.Darkened(0.2f));
                    DrawCircle(p + new Vector2(7, -6), 3.5f, color.Lightened(0.2f));
                    break;
                case Resources.Bread:
                    DrawCircle(p + new Vector2(7, -5), 7, color);
                    DrawRect(new Rect2(p.X, p.Y - 5, 15, 5), color.Darkened(0.1f));
                    break;
                case Resources.Pumpkin:
                    DrawCircle(p + new Vector2(7, -6), 7, color);
                    DrawLine(p + new Vector2(7, -12), p + new Vector2(7, -1), color.Darkened(0.25f), 1.5f);
                    DrawRect(new Rect2(p.X + 6, p.Y - 15, 2, 4), Color.FromHtml("#4a6a2a"));
                    break;
                case Resources.Wheat:
                    for (var k = 0; k < 3; k++) DrawLine(p + new Vector2(4 + k * 3, 0), p + new Vector2(3 + k * 4, -13), color, 2);
                    DrawCircle(p + new Vector2(7, -12), 3, color.Lightened(0.2f));
                    break;
                default:
                    DrawColoredPolygon([p + new Vector2(1, 0), p + new Vector2(7, -11), p + new Vector2(14, 0)], color);
                    DrawColoredPolygon([p + new Vector2(4, 0), p + new Vector2(7, -6), p + new Vector2(11, 0)], color.Lightened(0.2f));
                    break;
            }
        }
        var font = ThemeDB.FallbackFont;
        DrawString(font, origin + new Vector2(0, 20), $"{label} {count}", HorizontalAlignment.Left, -1, 14, count > 0 ? Palette.Text : Palette.Muted);
    }

    private void Gauge(Rect2 r, float value, float max, float mark, string label, Color fill)
    {
        DrawRect(r, Color.FromHtml("#0b0908"));
        var h = r.Size.Y * Math.Clamp(value / max, 0, 1);
        DrawRect(new Rect2(r.Position.X, r.End.Y - h, r.Size.X, h), fill);
        if (mark > 0)
        {
            var y = r.End.Y - r.Size.Y * mark / max;
            DrawLine(new Vector2(r.Position.X - 4, y), new Vector2(r.End.X + 4, y), Palette.Text, 2);
        }
        DrawRect(r, Palette.Border.Lightened(0.2f), false, 1.5f);
        DrawString(ThemeDB.FallbackFont, new Vector2(r.Position.X - 6, r.End.Y + 18), label, HorizontalAlignment.Left, -1, 13, Palette.Muted);
    }

    private void DrawSmelter(Vector2 size)
    {
        var floor = size.Y * 0.72f;
        var cx = size.X * 0.5f;
        var heat = S("heat");
        var strength = heat / (float)Smelter.MaxHeat;
        var body = new Rect2(cx - 90, floor - 190, 180, 190);
        DrawRect(new Rect2(cx - 28, floor - 250, 56, 70), Color.FromHtml("#3a2f28"));
        DrawStyleBox(Ui.Box(Color.FromHtml("#4a3a30"), Color.FromHtml("#2a201a"), 18, 0), body);
        for (var y = body.Position.Y + 14; y < body.End.Y - 10; y += 22)
        {
            DrawLine(new Vector2(body.Position.X + 8, y), new Vector2(body.End.X - 8, y), Color.FromHtml("#3b2e26"), 2);
        }
        var mouth = new Rect2(cx - 50, floor - 110, 100, 80);
        DrawStyleBox(Ui.Box(Color.FromHtml("#120c09"), null, 40, 0), mouth);
        var glow = new Color(1f, 0.45f, 0.1f, 0.15f + 0.6f * strength * Flicker);
        DrawStyleBox(Ui.Box(glow, null, 40, 0), mouth.Grow(-6));
        Fire(new Vector2(cx, floor - 34), 70, strength);
        if (heat >= Smelter.MeltHeat && S(Resources.Ore) > 0)
        {
            DrawCircle(new Vector2(cx, floor - 70), 12, new Color(1f, 0.75f, 0.3f, 0.7f * Flicker));
        }
        DrawRect(new Rect2(cx + 78, floor - 18, 60, 10), Color.FromHtml("#6a3a1a"));
        DrawRect(new Rect2(cx + 100, floor - 30, 8, 14), new Color(1f, 0.55f, 0.2f, heat >= Smelter.MeltHeat ? 0.9f : 0.2f));
        Gauge(new Rect2(cx + 150, floor - 180, 18, 160), heat, Smelter.MaxHeat, Smelter.MeltHeat, $"heat {heat}", new Color(1f, 0.35f + 0.4f * (1 - strength), 0.1f));

        Pile(new Vector2(40, floor + 40), Resources.Ore, S(Resources.Ore), "iron ore");
        Pile(new Vector2(40, floor + 110), Resources.Wood, S(Resources.Wood), "wood");
        Pile(new Vector2(size.X - 150, floor + 40), Resources.Iron, S(Resources.Iron), "iron");
        if (S("wasted") > 0)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(size.X - 150, floor + 110), $"heat wasted {S("wasted")}", HorizontalAlignment.Left, -1, 13, Palette.Danger);
        }
    }

    private void DrawFarm(Vector2 size)
    {
        var t = (float)_time;
        DrawRect(new Rect2(Vector2.Zero, size), Color.FromHtml("#1f3322"));
        for (var i = 0; i < 90; i++)
        {
            var gx = (i * 97 % 1000) / 1000f * size.X;
            var gy = (i * 61 % 1000) / 1000f * size.Y;
            DrawLine(new Vector2(gx, gy), new Vector2(gx + 2, gy - 5), Color.FromHtml("#2c4a2e"), 1.5f);
        }
        var cell = MathF.Min((size.X - 220) / Farm.Width, (size.Y - 70) / Farm.Height);
        var origin = new Vector2(24, (size.Y - cell * Farm.Height) / 2 - 10);
        var fence = Color.FromHtml("#6b4a2c");
        var field = new Rect2(origin - new Vector2(8, 8), new Vector2(cell * Farm.Width + 16, cell * Farm.Height + 16));
        DrawRect(field, Color.FromHtml("#3a2a1c"));
        DrawRect(field, fence, false, 3);
        for (var i = 0; i < Farm.Width * Farm.Height; i++)
        {
            var (x, y) = (i % Farm.Width, i / Farm.Width);
            var r = new Rect2(origin + new Vector2(x * cell, y * cell), new Vector2(cell, cell)).Grow(-3);
            var plot = _tiles is { } tiles && i < tiles.Count ? Farm.Decode(tiles[i]) : default;
            var soil = Color.FromHtml("#5a4030").Lerp(Color.FromHtml("#34303a"), plot.Water > 0 ? 0.55f : 0);
            DrawRect(r, soil);
            for (var k = 1; k < 4; k++) DrawLine(new Vector2(r.Position.X + 4, r.Position.Y + r.Size.Y * k / 4), new Vector2(r.End.X - 4, r.Position.Y + r.Size.Y * k / 4), soil.Darkened(0.25f), 2);
            if (plot.Water > 0)
            {
                for (var k = 0; k < 3; k++) DrawCircle(r.Position + new Vector2(r.Size.X * (0.2f + 0.3f * k), r.Size.Y * (0.8f - 0.25f * (k % 2))), 2.5f, new Color(0.5f, 0.7f, 1f, 0.35f * plot.Water / Farm.WaterTicks + 0.15f));
            }
            DrawCrop(r, plot, t + i);
        }
        var bob = MathF.Sin(t * 4) * 3;
        var at = origin + (_drone + new Vector2(0.5f, 0.5f)) * cell;
        DrawCircle(at + new Vector2(0, cell * 0.28f), cell * 0.18f, new Color(0, 0, 0, 0.3f));
        var body = at + new Vector2(0, -cell * 0.12f + bob);
        DrawStyleBox(Ui.Box(Color.FromHtml("#8a8f96"), Color.FromHtml("#3a3f46"), 6, 0), new Rect2(body - new Vector2(cell * 0.2f, cell * 0.12f), new Vector2(cell * 0.4f, cell * 0.24f)));
        DrawCircle(body + new Vector2(0, 1), cell * 0.06f, Palette.Accent.Lerp(Colors.White, 0.3f + 0.2f * MathF.Sin(t * 6)));
        foreach (var sx in new[] { -1f, 1f })
        {
            var hub = body + new Vector2(sx * cell * 0.26f, -cell * 0.12f);
            DrawLine(body + new Vector2(sx * cell * 0.15f, -cell * 0.08f), hub, Color.FromHtml("#5a5f66"), 2);
            var spin = MathF.Cos(t * 40 + sx) * cell * 0.14f;
            DrawLine(hub - new Vector2(spin, 0), hub + new Vector2(spin, 0), new Color(0.85f, 0.9f, 1f, 0.8f), 2.5f);
        }

        var font = ThemeDB.FallbackFont;
        var side = new Vector2(field.End.X + 26, origin.Y + 30);
        Pile(side, Resources.Wheat, S(Resources.Wheat), "wheat");
        Pile(side + new Vector2(0, 80), Resources.Pumpkin, S(Resources.Pumpkin), "pumpkins");
        var y2 = side.Y + 140;
        if (S("rotten") > 0) DrawString(font, new Vector2(side.X, y2), $"rotten {S("rotten")}", HorizontalAlignment.Left, -1, 13, Palette.Danger);
        if (S("lost") > 0) DrawString(font, new Vector2(side.X, y2 + 18), $"picked too early {S("lost")}", HorizontalAlignment.Left, -1, 13, Palette.Danger);
        DrawString(font, new Vector2(side.X, size.Y - 56), $"drone at x={S("x")}, y={S("y")}", HorizontalAlignment.Left, -1, 13, Palette.Muted);
    }

    private void DrawCrop(Rect2 r, Farm.Plot plot, float t)
    {
        var c = r.GetCenter();
        var s = r.Size.X;
        switch (plot.Crop)
        {
            case Farm.Crop.Wheat:
            {
                var k = Math.Min(1f, plot.Growth / (float)Farm.WheatRipe);
                var color = Color.FromHtml("#6a9a3a").Lerp(Color.FromHtml("#e0c060"), plot.Ripe ? 1 : k * 0.6f);
                for (var i = 0; i < 5; i++)
                {
                    var x = r.Position.X + s * (0.18f + 0.16f * i);
                    var h = s * (0.15f + 0.55f * k);
                    var sway = MathF.Sin(t * 1.7f + i) * 2 * k;
                    var baseY = r.End.Y - s * 0.12f;
                    DrawLine(new Vector2(x, baseY), new Vector2(x + sway, baseY - h), color, 2.5f);
                    if (plot.Ripe) DrawCircle(new Vector2(x + sway, baseY - h), 3.5f, color.Lightened(0.25f));
                }
                break;
            }
            case Farm.Crop.Pumpkin:
            {
                var k = Math.Min(1f, plot.Growth / (float)Farm.PumpkinRipe);
                var vine = Color.FromHtml("#4a7a2a");
                DrawArc(c + new Vector2(0, s * 0.1f), s * 0.28f, 0.3f, 2.8f, 8, vine, 2.5f);
                DrawCircle(c + new Vector2(-s * 0.26f, s * 0.12f), s * 0.07f, vine);
                DrawCircle(c + new Vector2(s * 0.24f, s * 0.2f), s * 0.07f, vine);
                var radius = s * (0.08f + 0.22f * k);
                var color = plot.Ripe ? Color.FromHtml("#f08a24") : Color.FromHtml("#8aa040").Lerp(Color.FromHtml("#d08a30"), k);
                DrawCircle(c, radius, color);
                DrawArc(c, radius * 0.6f, -1.2f, 1.2f, 6, color.Darkened(0.2f), 1.5f);
                DrawArc(c, radius * 0.6f, Mathf.Pi - 1.2f, Mathf.Pi + 1.2f, 6, color.Darkened(0.2f), 1.5f);
                DrawRect(new Rect2(c.X - 2, c.Y - radius - 5, 4, 6), vine.Darkened(0.2f));
                if (plot.Ripe) DrawArc(c, radius + 3 + MathF.Sin(t * 3) * 1.5f, 0, Mathf.Tau, 20, new Color(1f, 0.85f, 0.4f, 0.35f), 1.5f);
                break;
            }
            case Farm.Crop.Rotten:
            {
                DrawCircle(c + new Vector2(0, s * 0.06f), s * 0.24f, Color.FromHtml("#3a2a1a"));
                DrawCircle(c + new Vector2(-s * 0.06f, 0), s * 0.08f, Color.FromHtml("#5a6a2a"));
                for (var i = 0; i < 3; i++)
                {
                    var a = t * 3 + i * 2.1f;
                    DrawCircle(c + new Vector2(MathF.Cos(a) * s * 0.3f, -s * 0.2f + MathF.Sin(a * 1.3f) * s * 0.1f), 1.8f, Color.FromHtml("#101010"));
                }
                break;
            }
        }
    }

    private void DrawBakery(Vector2 size)
    {
        var floor = size.Y * 0.72f;
        var cx = size.X * 0.5f;
        var fire = S("fire");
        var strength = Math.Min(1f, fire / (float)Bakery.FireTicks);
        var dome = new Vector2(cx, floor - 80);
        DrawRect(new Rect2(cx - 120, floor - 80, 240, 80), Color.FromHtml("#5a4436"));
        DrawCircle(dome, 120, Color.FromHtml("#6a5040"));
        DrawCircle(dome, 104, Color.FromHtml("#5a4436"));
        DrawRect(new Rect2(cx - 130, floor - 16, 260, 16), Color.FromHtml("#3a2c22"));
        var mouth = new Rect2(cx - 70, floor - 120, 140, 90);
        DrawStyleBox(Ui.Box(Color.FromHtml("#120c09"), null, 45, 0), mouth);
        DrawStyleBox(Ui.Box(new Color(1f, 0.45f, 0.1f, 0.1f + 0.5f * strength * Flicker), null, 45, 0), mouth.Grow(-6));
        Fire(new Vector2(cx - 40, floor - 36), 40, strength);
        var tray = S("tray");
        var baked = S("baked");
        if (tray > 0)
        {
            DrawRect(new Rect2(cx - 20, floor - 50, 80, 6), Color.FromHtml("#8a8a8a"));
            var color = baked < Bakery.DoneAt ? Color.FromHtml("#e9dcc0").Lerp(Color.FromHtml("#e0a86a"), baked / (float)Bakery.DoneAt)
                : baked <= Bakery.BurntAfter ? Color.FromHtml("#d08a40")
                : Color.FromHtml("#3a2618");
            for (var i = 0; i < tray; i++) DrawCircle(new Vector2(cx - 8 + i * 19, floor - 58), 9, color);
        }
        Gauge(new Rect2(cx + 150, floor - 180, 18, 160), fire, Bakery.MaxFire, 0, $"fire {fire}", new Color(1f, 0.5f, 0.1f));
        var oven = Ui.Box(new Color(Palette.Panel, 0.85f), Palette.Border, 6, 0);
        var info = tray == 0 ? "oven empty" : baked < Bakery.DoneAt ? $"baking {baked}: raw" : baked <= Bakery.BurntAfter ? $"baked {baked}: done!" : $"baked {baked}: burnt!";
        var font = ThemeDB.FallbackFont;
        var w = font.GetStringSize(info, HorizontalAlignment.Left, -1, 14).X + 16;
        DrawStyleBox(oven, new Rect2(cx - w / 2, floor - 230, w, 24));
        var infoColor = tray == 0 ? Palette.Muted : baked < Bakery.DoneAt ? Palette.Text : baked <= Bakery.BurntAfter ? Palette.Ok : Palette.Danger;
        DrawString(font, new Vector2(cx - w / 2 + 8, floor - 212), info, HorizontalAlignment.Left, -1, 14, infoColor);

        Pile(new Vector2(40, floor + 40), Resources.Wheat, S(Resources.Wheat), "wheat");
        Pile(new Vector2(40, floor + 110), Resources.Wood, S(Resources.Wood), "wood");
        Pile(new Vector2(size.X - 150, floor + 40), Resources.Bread, S(Resources.Bread), "bread");
        Pile(new Vector2(size.X - 300, floor + 40), Resources.Wheat, S("dough"), "dough");
        if (S("burnt") > 0) DrawString(font, new Vector2(size.X - 150, floor + 110), $"burnt {S("burnt")}", HorizontalAlignment.Left, -1, 13, Palette.Danger);
    }
}
