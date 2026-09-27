using Godot;

namespace Delvework.Game;

/// <summary>Colours of the dark stone, iron and brass theme shared by every panel.</summary>
public static class Palette
{
    public static readonly Color Bg = Color.FromHtml("#0b0908");
    public static readonly Color Panel = Color.FromHtml("#16120f");
    public static readonly Color Panel2 = Color.FromHtml("#211b16");
    public static readonly Color Border = Color.FromHtml("#3a2f26");
    public static readonly Color Iron = Color.FromHtml("#0a0807");
    public static readonly Color Brass = Color.FromHtml("#b08d57");
    public static readonly Color BrassDim = Color.FromHtml("#6e5634");
    public static readonly Color BrassLight = Color.FromHtml("#e6c98f");
    public static readonly Color Text = Color.FromHtml("#efe4d2");
    public static readonly Color Muted = Color.FromHtml("#a29481");
    public static readonly Color Accent = Color.FromHtml("#f5b041");
    public static readonly Color AccentInk = Color.FromHtml("#1d1206");
    public static readonly Color Seeker = Color.FromHtml("#5dd3e8");
    public static readonly Color Rune = Color.FromHtml("#7fe0ff");
    public static readonly Color Danger = Color.FromHtml("#f07178");
    public static readonly Color Ok = Color.FromHtml("#7bd88f");
    public static readonly Color Void = Color.FromHtml("#07080a");
    public static readonly Color Parchment = Color.FromHtml("#e8d6ad");
    public static readonly Color Ink = Color.FromHtml("#3a2814");

    public static Color ForGolem(string chassisId) => chassisId switch
    {
        "warden" => Accent,
        "seeker" => Seeker,
        "striker" => Color.FromHtml("#e07bd8"),
        _ => Text,
    };
}

/// <summary>How a <see cref="Frame"/> is dressed.</summary>
public enum FrameKind
{
    /// <summary>A heavy stone panel with a brass inlay, corner brackets and rivets.</summary>
    Stone,
    /// <summary>A slimmer plate for bars and badges: brass inlay and brackets, no rivets.</summary>
    Plate,
    /// <summary>A parchment scroll with dark ink, for runes and discoveries.</summary>
    Parchment,
}

/// <summary>
/// A panel drawn like the concept art: dark stone behind an iron border, a thin brass line set
/// in from the edge, L-shaped brass brackets at the corners and a rivet in each one.
/// </summary>
public partial class Frame : PanelContainer
{
    public FrameKind Kind { get; init; } = FrameKind.Stone;
    public Color Trim { get; set; } = Palette.Brass;
    public float Opacity { get; init; } = 1f;

    public override void _Ready()
    {
        var parchment = Kind == FrameKind.Parchment;
        var sb = Ui.Box(new Color(parchment ? Palette.Parchment : Kind == FrameKind.Plate ? Palette.Panel2 : Palette.Panel, Opacity),
            parchment ? Palette.Ink.Lightened(0.15f) : Palette.Iron, parchment ? 3 : 5, Kind == FrameKind.Plate ? 10 : 18);
        sb.SetBorderWidthAll(parchment ? 2 : 3);
        sb.ShadowColor = new Color(0, 0, 0, 0.5f);
        sb.ShadowSize = Kind == FrameKind.Plate ? 6 : 14;
        sb.ShadowOffset = new Vector2(0, 4);
        if (Kind == FrameKind.Plate) sb.ContentMarginTop = sb.ContentMarginBottom = 7;
        AddThemeStyleboxOverride("panel", sb);
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        var trim = Kind == FrameKind.Parchment ? Palette.Ink.Lightened(0.25f) : Trim;
        var inset = Kind == FrameKind.Plate ? 4f : 6f;
        var inner = r.Grow(-inset);
        DrawRect(inner, new Color(trim, 0.45f), false, 1f);
        var arm = Kind == FrameKind.Plate ? 9f : 16f;
        var c = new Color(trim, 0.95f);
        foreach (var (corner, sx, sy) in new[]
        {
            (inner.Position, 1f, 1f),
            (new Vector2(inner.End.X, inner.Position.Y), -1f, 1f),
            (new Vector2(inner.Position.X, inner.End.Y), 1f, -1f),
            (inner.End, -1f, -1f),
        })
        {
            DrawLine(corner, corner + new Vector2(arm * sx, 0), c, 2f);
            DrawLine(corner, corner + new Vector2(0, arm * sy), c, 2f);
            if (Kind != FrameKind.Stone) continue;
            var rivet = corner + new Vector2(7 * sx, 7 * sy);
            DrawCircle(rivet, 2.6f, Palette.Iron);
            DrawCircle(rivet, 1.8f, Palette.BrassLight.Darkened(0.2f));
        }
    }
}

/// <summary>A thin brass line with a diamond at each end.</summary>
public partial class BrassRule : Control
{
    public override void _Draw()
    {
        var y = Size.Y / 2;
        DrawLine(new Vector2(5, y), new Vector2(Size.X - 5, y), new Color(Palette.Brass, 0.55f), 1);
        foreach (var x in new[] { 3f, Size.X - 3 })
        {
            var tip = new Vector2(x, y);
            DrawColoredPolygon([tip + new Vector2(-3, 0), tip + new Vector2(0, -3), tip + new Vector2(3, 0), tip + new Vector2(0, 3)], Palette.Brass);
        }
    }
}

public static class Ui
{
    public static Font Mono { get; } = new SystemFont
    {
        FontNames = ["JetBrains Mono", "Cascadia Mono", "Consolas", "DejaVu Sans Mono", "Liberation Mono", "monospace"],
    };

    public static Font Serif { get; } = new SystemFont
    {
        FontNames = ["Palatino Linotype", "Book Antiqua", "Constantia", "Georgia", "Cambria", "DejaVu Serif", "Liberation Serif", "serif"],
        FontWeight = 700,
    };

    public static Font Body { get; } = new SystemFont
    {
        FontNames = ["Segoe UI", "Noto Sans", "DejaVu Sans", "Liberation Sans", "sans-serif"],
    };

    /// <summary>The serif with its letters spread apart, for titles and small labels cut in stone.</summary>
    public static Font Carved { get; } = new FontVariation { BaseFont = Serif, SpacingGlyph = 2 };

    public static Label Heading(string text, int size = 28, Color? color = null)
    {
        var l = Label(text, color ?? Palette.Text, size);
        l.AddThemeFontOverride("font", Serif);
        Shadow(l);
        return l;
    }

    /// <summary>An all-caps title in the carved face, like the plates in the concept art.</summary>
    public static Label Title(string text, int size = 20, Color? color = null)
    {
        var l = Label(text.ToUpperInvariant(), color ?? Palette.BrassLight, size);
        l.AddThemeFontOverride("font", Carved);
        Shadow(l);
        return l;
    }

    private static void Shadow(Label l)
    {
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
        l.AddThemeConstantOverride("shadow_offset_x", 0);
        l.AddThemeConstantOverride("shadow_offset_y", 2);
    }

    /// <summary>A paragraph that wraps to its container.</summary>
    public static Label Para(string text, Color? color = null, int size = 15)
    {
        var l = Label(text, color ?? Palette.Text, size);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        l.CustomMinimumSize = new Vector2(80, 0);
        return l;
    }

    public static MarginContainer Pad(Control content, int all)
    {
        var m = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" }) m.AddThemeConstantOverride("margin_" + side, all);
        m.AddChild(content);
        return m;
    }

    public static StyleBoxFlat Box(Color bg, Color? border = null, int radius = 6, int padding = 10)
    {
        var sb = new StyleBoxFlat { BgColor = bg };
        sb.SetCornerRadiusAll(radius);
        sb.SetContentMarginAll(padding);
        if (border is { } b)
        {
            sb.BorderColor = b;
            sb.SetBorderWidthAll(1);
        }
        return sb;
    }

    /// <summary>A button face: dark stone with a brass edge, lit from above.</summary>
    private static StyleBoxFlat Slab(Color bg, Color border, int padding = 8)
    {
        var sb = Box(bg, border, 3, padding);
        sb.ContentMarginLeft = sb.ContentMarginRight = padding + 6;
        sb.BorderWidthBottom = 2;
        sb.ShadowColor = new Color(0, 0, 0, 0.4f);
        sb.ShadowSize = 3;
        sb.ShadowOffset = new Vector2(0, 2);
        return sb;
    }

    public static Theme BuildTheme()
    {
        var t = new Theme { DefaultFontSize = 14, DefaultFont = Body };
        foreach (var type in new[] { "Label", "Button", "CheckBox", "OptionButton", "LineEdit", "TabBar", "RichTextLabel", "SpinBox", "MenuButton" })
        {
            t.SetColor("font_color", type, Palette.Text);
        }
        t.SetColor("default_color", "RichTextLabel", Palette.Text);

        t.SetStylebox("panel", "PanelContainer", Box(Palette.Panel, Palette.Border, 5, 12));
        foreach (var type in new[] { "Button", "MenuButton", "OptionButton" })
        {
            t.SetStylebox("normal", type, Slab(Palette.Panel2, Palette.BrassDim));
            t.SetStylebox("hover", type, Slab(Palette.Panel2.Lightened(0.07f), Palette.Brass));
            t.SetStylebox("pressed", type, Slab(Palette.Panel, Palette.Accent));
            t.SetStylebox("disabled", type, Slab(Palette.Panel.Darkened(0.1f), Palette.Border));
            t.SetStylebox("focus", type, new StyleBoxEmpty());
            t.SetFont("font", type, Serif);
            t.SetColor("font_disabled_color", type, Palette.Muted.Darkened(0.3f));
            t.SetColor("font_pressed_color", type, Palette.BrassLight);
            t.SetColor("font_hover_color", type, Palette.BrassLight);
        }

        t.SetStylebox("normal", "LineEdit", Box(Palette.Void, Palette.Border, 4, 6));
        t.SetStylebox("focus", "LineEdit", Box(Palette.Void, Palette.Brass, 4, 6));

        var tabSelected = Box(Palette.Panel2, Palette.BrassDim, 3, 8);
        tabSelected.BorderWidthBottom = 0;
        tabSelected.BorderColor = Palette.Brass;
        tabSelected.BorderWidthTop = 2;
        foreach (var type in new[] { "TabBar", "TabContainer" })
        {
            t.SetStylebox("tab_selected", type, tabSelected);
            t.SetStylebox("tab_unselected", type, Box(Palette.Panel, null, 3, 8));
            t.SetStylebox("tab_hovered", type, Box(Palette.Panel2.Darkened(0.1f), null, 3, 8));
            t.SetStylebox("tab_disabled", type, Box(Palette.Panel, null, 3, 8));
            t.SetColor("font_selected_color", type, Palette.BrassLight);
            t.SetColor("font_unselected_color", type, Palette.Muted);
            t.SetColor("font_hovered_color", type, Palette.Text);
            t.SetColor("font_disabled_color", type, Palette.Muted.Darkened(0.4f));
            t.SetFont("font", type, Serif);
        }
        t.SetStylebox("panel", "TabContainer", Box(Palette.Panel2, Palette.Border, 4, 10));

        t.SetStylebox("normal", "RichTextLabel", new StyleBoxEmpty());
        t.SetFont("normal_font", "RichTextLabel", Mono);
        t.SetFontSize("normal_font_size", "RichTextLabel", 13);

        t.SetStylebox("normal", "CodeEdit", Box(Palette.Void, Palette.Iron, 4, 8));
        t.SetStylebox("focus", "CodeEdit", Box(Palette.Void, Palette.BrassDim, 4, 8));
        t.SetFont("font", "CodeEdit", Mono);
        t.SetFontSize("font_size", "CodeEdit", 15);
        t.SetColor("font_color", "CodeEdit", Palette.Text);
        t.SetColor("background_color", "CodeEdit", Palette.Void);
        t.SetColor("current_line_color", "CodeEdit", new Color(1, 1, 1, 0.035f));
        t.SetColor("caret_color", "CodeEdit", Palette.Accent);
        t.SetColor("selection_color", "CodeEdit", new Color(Palette.Accent, 0.25f));
        t.SetColor("line_number_color", "CodeEdit", Palette.Muted.Darkened(0.35f));
        t.SetColor("executing_line_color", "CodeEdit", Palette.Accent);
        t.SetColor("completion_background_color", "CodeEdit", Palette.Panel2);
        t.SetColor("completion_selected_color", "CodeEdit", new Color(Palette.Accent, 0.25f));
        t.SetColor("completion_font_color", "CodeEdit", Palette.Text);

        var groove = Box(Palette.Iron, Palette.Border, 3, 2);
        t.SetStylebox("slider", "HSlider", groove);
        t.SetStylebox("grabber_area", "HSlider", Box(Palette.Accent.Darkened(0.15f), null, 3, 2));
        t.SetStylebox("grabber_area_highlight", "HSlider", Box(Palette.Accent, null, 3, 2));
        t.SetIcon("grabber", "HSlider", Diamond(Palette.Accent, 18));
        t.SetIcon("grabber_highlight", "HSlider", Diamond(Palette.BrassLight, 18));

        foreach (var bar in new[] { "VScrollBar", "HScrollBar" })
        {
            t.SetStylebox("scroll", bar, Box(new Color(Palette.Iron, 0.6f), null, 3, 3));
            t.SetStylebox("grabber", bar, Box(Palette.BrassDim, null, 3, 3));
            t.SetStylebox("grabber_highlight", bar, Box(Palette.Brass, null, 3, 3));
            t.SetStylebox("grabber_pressed", bar, Box(Palette.BrassLight, null, 3, 3));
        }

        t.SetStylebox("panel", "PopupMenu", Box(Palette.Panel2, Palette.BrassDim, 4, 6));
        t.SetColor("font_color", "PopupMenu", Palette.Text);
        t.SetColor("font_hover_color", "PopupMenu", Palette.BrassLight);
        t.SetColor("font_separator_color", "PopupMenu", Palette.Brass);
        t.SetStylebox("hover", "PopupMenu", Box(new Color(Palette.Accent, 0.18f), null, 3, 4));
        t.SetStylebox("panel", "TooltipPanel", Box(Palette.Panel2, Palette.BrassDim, 4, 6));
        t.SetColor("font_color", "TooltipLabel", Palette.Text);

        t.SetStylebox("background", "ProgressBar", Box(Palette.Iron, Palette.Border, 2, 0));
        t.SetStylebox("fill", "ProgressBar", Box(Palette.Accent, null, 2, 0));
        t.SetStylebox("panel", "ScrollContainer", new StyleBoxEmpty());
        t.SetColor("separator", "HSeparator", Palette.BrassDim);
        return t;
    }

    /// <summary>A small glowing diamond, the timeline's playhead.</summary>
    public static ImageTexture Diamond(Color color, int size)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var h = size / 2f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var d = Math.Abs(x + 0.5f - h) + Math.Abs(y + 0.5f - h);
                if (d <= h - 1) img.SetPixel(x, y, d > h - 3 ? color.Darkened(0.45f) : color.Lerp(Colors.White, Math.Max(0, 0.35f - d / h)));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    public static Label Label(string text, Color? color = null, int size = 14)
    {
        var l = new Label { Text = text };
        if (color is { } c) l.AddThemeColorOverride("font_color", c);
        if (size != 14) l.AddThemeFontSizeOverride("font_size", size);
        if (size <= 12 && text.Length > 2 && text.Any(char.IsLetter) && !text.Any(char.IsLower)) l.AddThemeFontOverride("font", Carved);
        return l;
    }

    public static Button Button(string text, string? tooltip = null, bool primary = false)
    {
        var b = new Button { Text = text, TooltipText = tooltip ?? "", FocusMode = Control.FocusModeEnum.None };
        if (primary) Primary(b);
        return b;
    }

    /// <summary>Dress a button as the one to press: glowing ember brass with dark ink.</summary>
    public static void Primary(BaseButton b)
    {
        b.AddThemeStyleboxOverride("normal", Slab(Palette.Accent.Darkened(0.12f), Palette.BrassLight));
        b.AddThemeStyleboxOverride("hover", Slab(Palette.Accent, Colors.White.Lerp(Palette.BrassLight, 0.5f)));
        b.AddThemeStyleboxOverride("pressed", Slab(Palette.Accent.Darkened(0.25f), Palette.BrassLight));
        b.AddThemeStyleboxOverride("disabled", Slab(Palette.Panel2, Palette.Border));
        b.AddThemeColorOverride("font_color", Palette.AccentInk);
        b.AddThemeColorOverride("font_hover_color", Palette.AccentInk);
        b.AddThemeColorOverride("font_pressed_color", Palette.AccentInk);
    }

    public static HBoxContainer Row(int separation = 8, params Control[] children)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", separation);
        foreach (var c in children) row.AddChild(c);
        return row;
    }

    public static VBoxContainer Column(int separation = 8, params Control[] children)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", separation);
        foreach (var c in children) col.AddChild(c);
        return col;
    }

    public static Control Spacer() => new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

    public static PanelContainer Card(Control content, bool expand = false)
    {
        var card = new PanelContainer();
        if (expand) card.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        card.AddChild(content);
        return card;
    }

    /// <summary>Wrap <paramref name="content"/> in a stone, plate or parchment <see cref="Game.Frame"/>.</summary>
    public static Frame Frame(Control content, FrameKind kind = FrameKind.Stone, bool expand = false, float opacity = 1f)
    {
        var f = new Frame { Kind = kind, Opacity = opacity };
        if (expand) f.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        f.AddChild(content);
        return f;
    }

    /// <summary>A carved heading between two brass rules: ⟡──  TITLE  ──⟡.</summary>
    public static Control Banner(string text, int size = 13, Color? color = null)
    {
        var row = Row(10);
        row.AddChild(Rule());
        var title = Title(text, size, color);
        title.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        row.AddChild(title);
        row.AddChild(Rule());
        return row;
    }

    private static Control Rule() => new BrassRule { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(12, 12) };

    /// <summary>A thin HP-style bar: an iron groove with a coloured fill.</summary>
    public static ProgressBar Bar(Color fill, float height = 6)
    {
        var bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0, height), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("fill", Box(fill, null, 2, 0));
        return bar;
    }
}
