using Godot;

namespace Delvework.Game;

/// <summary>Colours and the dark, warm theme shared by every panel.</summary>
public static class Palette
{
    public static readonly Color Bg = Color.FromHtml("#0d0b0a");
    public static readonly Color Panel = Color.FromHtml("#171311");
    public static readonly Color Panel2 = Color.FromHtml("#1f1a16");
    public static readonly Color Border = Color.FromHtml("#2e2621");
    public static readonly Color Text = Color.FromHtml("#ece4da");
    public static readonly Color Muted = Color.FromHtml("#9a8c7e");
    public static readonly Color Accent = Color.FromHtml("#f5b041");
    public static readonly Color AccentInk = Color.FromHtml("#2a1a05");
    public static readonly Color Seeker = Color.FromHtml("#5dd3e8");
    public static readonly Color Danger = Color.FromHtml("#f07178");
    public static readonly Color Ok = Color.FromHtml("#7bd88f");
    public static readonly Color Void = Color.FromHtml("#07080a");

    public static Color ForGolem(string chassisId) => chassisId switch
    {
        "warden" => Accent,
        "seeker" => Seeker,
        "striker" => Color.FromHtml("#e07bd8"),
        _ => Text,
    };
}

public static class Ui
{
    public static Font Mono { get; } = new SystemFont
    {
        FontNames = ["JetBrains Mono", "Cascadia Mono", "Consolas", "DejaVu Sans Mono", "Liberation Mono", "monospace"],
    };

    public static Font Serif { get; } = new SystemFont
    {
        FontNames = ["Georgia", "Cambria", "Palatino Linotype", "DejaVu Serif", "Liberation Serif", "serif"],
        FontWeight = 700,
    };

    public static Label Heading(string text, int size = 28, Color? color = null)
    {
        var l = Label(text, color ?? Palette.Text, size);
        l.AddThemeFontOverride("font", Serif);
        return l;
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

    public static StyleBoxFlat Box(Color bg, Color? border = null, int radius = 10, int padding = 10)
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

    public static Theme BuildTheme()
    {
        var t = new Theme { DefaultFontSize = 14 };
        foreach (var type in new[] { "Label", "Button", "CheckBox", "OptionButton", "LineEdit", "TabBar", "RichTextLabel", "SpinBox" })
        {
            t.SetColor("font_color", type, Palette.Text);
        }
        t.SetColor("default_color", "RichTextLabel", Palette.Text);

        t.SetStylebox("panel", "PanelContainer", Box(Palette.Panel, Palette.Border, 14, 12));
        t.SetStylebox("normal", "Button", Box(Palette.Panel2, Palette.Border, 9, 7));
        t.SetStylebox("hover", "Button", Box(Palette.Panel2.Lightened(0.08f), Palette.Border.Lightened(0.2f), 9, 7));
        t.SetStylebox("pressed", "Button", Box(Palette.Panel, Palette.Accent, 9, 7));
        t.SetStylebox("disabled", "Button", Box(Palette.Panel2.Darkened(0.2f), Palette.Border, 9, 7));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty());
        t.SetColor("font_disabled_color", "Button", Palette.Muted.Darkened(0.3f));
        t.SetColor("font_pressed_color", "Button", Palette.Text);
        t.SetColor("font_hover_color", "Button", Palette.Text);

        t.SetStylebox("normal", "OptionButton", Box(Palette.Panel2, Palette.Border, 9, 7));
        t.SetStylebox("hover", "OptionButton", Box(Palette.Panel2.Lightened(0.08f), Palette.Border.Lightened(0.2f), 9, 7));
        t.SetStylebox("pressed", "OptionButton", Box(Palette.Panel2, Palette.Accent, 9, 7));
        t.SetStylebox("focus", "OptionButton", new StyleBoxEmpty());

        t.SetStylebox("normal", "LineEdit", Box(Palette.Panel2, Palette.Border, 8, 6));
        t.SetStylebox("focus", "LineEdit", Box(Palette.Panel2, Palette.Accent, 8, 6));

        t.SetStylebox("tab_selected", "TabBar", Box(Palette.Panel2, Palette.Border, 8, 8));
        t.SetStylebox("tab_unselected", "TabBar", Box(Palette.Panel, null, 8, 8));
        t.SetStylebox("tab_hovered", "TabBar", Box(Palette.Panel2.Darkened(0.1f), null, 8, 8));
        t.SetStylebox("tab_disabled", "TabBar", Box(Palette.Panel, null, 8, 8));
        t.SetColor("font_selected_color", "TabBar", Palette.Text);
        t.SetColor("font_unselected_color", "TabBar", Palette.Muted);
        t.SetColor("font_disabled_color", "TabBar", Palette.Muted.Darkened(0.4f));
        t.SetFont("font", "TabBar", Mono);

        t.SetStylebox("panel", "TabContainer", Box(Palette.Panel, Palette.Border, 14, 12));
        t.SetStylebox("tab_selected", "TabContainer", Box(Palette.Panel2, Palette.Border, 8, 8));
        t.SetStylebox("tab_unselected", "TabContainer", Box(Palette.Panel, null, 8, 8));
        t.SetStylebox("tab_hovered", "TabContainer", Box(Palette.Panel2.Darkened(0.1f), null, 8, 8));
        t.SetColor("font_selected_color", "TabContainer", Palette.Text);
        t.SetColor("font_unselected_color", "TabContainer", Palette.Muted);

        t.SetStylebox("normal", "RichTextLabel", new StyleBoxEmpty());
        t.SetFont("normal_font", "RichTextLabel", Mono);
        t.SetFontSize("normal_font_size", "RichTextLabel", 13);

        t.SetStylebox("normal", "CodeEdit", Box(Palette.Void, Palette.Border, 10, 8));
        t.SetStylebox("focus", "CodeEdit", Box(Palette.Void, Palette.Border.Lightened(0.15f), 10, 8));
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

        t.SetStylebox("slider", "HSlider", Box(Palette.Panel2, Palette.Border, 4, 2));
        t.SetStylebox("grabber_area", "HSlider", Box(Palette.Accent, null, 4, 2));
        t.SetStylebox("grabber_area_highlight", "HSlider", Box(Palette.Accent.Lightened(0.15f), null, 4, 2));

        t.SetStylebox("panel", "PopupMenu", Box(Palette.Panel2, Palette.Border, 8, 6));
        t.SetColor("font_color", "PopupMenu", Palette.Text);
        t.SetColor("font_hover_color", "PopupMenu", Palette.Text);
        t.SetStylebox("hover", "PopupMenu", Box(new Color(Palette.Accent, 0.2f), null, 6, 4));
        t.SetStylebox("panel", "TooltipPanel", Box(Palette.Panel2, Palette.Border, 6, 6));
        t.SetColor("font_color", "TooltipLabel", Palette.Text);

        t.SetStylebox("grabber_area", "HSlider", Box(Palette.Accent, null, 4, 2));
        t.SetStylebox("normal", "ProgressBar", Box(Palette.Panel2, Palette.Border, 4, 0));
        t.SetStylebox("background", "ProgressBar", Box(Palette.Panel2, Palette.Border, 4, 0));
        t.SetStylebox("fill", "ProgressBar", Box(Palette.Accent, null, 4, 0));
        t.SetStylebox("panel", "ScrollContainer", new StyleBoxEmpty());
        return t;
    }

    public static Label Label(string text, Color? color = null, int size = 14)
    {
        var l = new Label { Text = text };
        if (color is { } c) l.AddThemeColorOverride("font_color", c);
        if (size != 14) l.AddThemeFontSizeOverride("font_size", size);
        return l;
    }

    public static Button Button(string text, string? tooltip = null, bool primary = false)
    {
        var b = new Button { Text = text, TooltipText = tooltip ?? "", FocusMode = Control.FocusModeEnum.None };
        if (primary)
        {
            b.AddThemeStyleboxOverride("normal", Box(Palette.Accent, null, 9, 7));
            b.AddThemeStyleboxOverride("hover", Box(Palette.Accent.Lightened(0.1f), null, 9, 7));
            b.AddThemeStyleboxOverride("pressed", Box(Palette.Accent.Darkened(0.1f), null, 9, 7));
            b.AddThemeColorOverride("font_color", Palette.AccentInk);
            b.AddThemeColorOverride("font_hover_color", Palette.AccentInk);
            b.AddThemeColorOverride("font_pressed_color", Palette.AccentInk);
        }
        return b;
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
}
