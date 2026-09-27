using Godot;

namespace Delvework.Game;

/// <summary>
/// A small window that floats over any screen: drag it by its title bar, resize it from the
/// corner, close it with ✕. The game keeps playing underneath.
/// </summary>
public partial class HelpWindow : Frame
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
        base._Ready();
        Size = StartSize;
        if (GetThemeStylebox("panel") is StyleBoxFlat frame)
        {
            frame.SetContentMarginAll(8);
            frame.ShadowSize = 18;
            frame.ShadowColor = new Color(0, 0, 0, 0.6f);
        }
        var col = Ui.Column(0);
        AddChild(col);

        var bar = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.Move };
        var barStyle = Ui.Box(Palette.Panel2, null, 3, 6);
        barStyle.BorderColor = Palette.BrassDim;
        barStyle.BorderWidthBottom = 1;
        barStyle.ContentMarginLeft = 12;
        bar.AddThemeStyleboxOverride("panel", barStyle);
        var close = Ui.Button("✕", "Close (Esc)");
        close.Flat = true;
        close.Pressed += Close;
        _title = Ui.Heading(Title, 15, Palette.BrassLight);
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

