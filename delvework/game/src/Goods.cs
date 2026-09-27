using Delvework.Core.Content;
using Delvework.Core.Progress;
using Godot;

namespace Delvework.Game;

/// <summary>Gold and materials as small coloured chips: the stock bar, prices and rewards.</summary>
public static class Goods
{
    public static Color ColorOf(string id) => id switch
    {
        Resources.Gold => Palette.Accent,
        Resources.Wood => Color.FromHtml("#c08a5a"),
        Resources.Stone => Color.FromHtml("#b4b0a8"),
        Resources.Ore => Color.FromHtml("#d9774a"),
        Resources.Iron => Color.FromHtml("#9fb8d0"),
        Resources.Wheat => Color.FromHtml("#e8cf6a"),
        Resources.Bread => Color.FromHtml("#e0a86a"),
        Resources.Pumpkin => Color.FromHtml("#f08a24"),
        Resources.Crystal => Color.FromHtml("#9ab8ff"),
        Resources.Essence => Color.FromHtml("#c792ea"),
        _ => Palette.Text,
    };

    public static string Icon(string id) => id switch
    {
        Resources.Gold => "●",
        Resources.Wood => "▮",
        Resources.Stone => "■",
        Resources.Ore => "◆",
        Resources.Iron => "▬",
        Resources.Wheat => "✱",
        Resources.Bread => "◗",
        Resources.Pumpkin => "◉",
        Resources.Crystal => "◇",
        Resources.Essence => "✦",
        _ => "•",
    };

    public static string Title(string id) => id switch
    {
        Resources.Ore => "Iron ore",
        Resources.Essence => "Monster essence",
        _ => char.ToUpperInvariant(id[0]) + id[1..],
    };

    /// <summary>What each top bar showed last, so a new bar can count up from there.</summary>
    private static readonly Dictionary<string, int> Shown = new(StringComparer.Ordinal);

    /// <summary>A chip (or a <paramref name="tile"/>) whose number rolls from the value last shown to <paramref name="amount"/>.</summary>
    private static Control Counter(string id, int amount, int size, Color? color = null, bool tile = false)
    {
        var from = Shown.GetValueOrDefault(id, amount);
        Shown[id] = amount;
        var chip = tile ? Tile(id, from, color) : Chip(id, from, size, color);
        if (from == amount) return chip;
        var label = chip.GetChild<Label>(1);
        label.AddThemeColorOverride("font_color", amount > from ? Palette.Ok : Palette.Danger);
        chip.Ready += () =>
        {
            var tween = chip.CreateTween();
            tween.TweenInterval(0.35f);
            tween.TweenMethod(Callable.From<int>(v => label.Text = v.ToString()), from, amount, Math.Clamp(Math.Abs(amount - from) * 0.04f, 0.4f, 1.4f)).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            tween.TweenCallback(Callable.From(() => label.AddThemeColorOverride("font_color", color ?? Palette.Text)));
        };
        return chip;
    }

    /// <summary>"◆ 3" in the material's colour, with its name as tooltip.</summary>
    public static Control Chip(string id, int amount, int size = 14, Color? color = null, string? text = null)
    {
        var row = Ui.Row(4, Ui.Label(Icon(id), ColorOf(id), size), Ui.Label(text ?? amount.ToString(), color ?? Palette.Text, size));
        row.TooltipText = Title(id);
        row.MouseFilter = Control.MouseFilterEnum.Pass;
        foreach (var c in row.GetChildren().OfType<Control>()) c.MouseFilter = Control.MouseFilterEnum.Pass;
        return row;
    }

    /// <summary>A big icon over its count, like a resource counter floating over the farm.</summary>
    public static Control Tile(string id, int amount, Color? color = null)
    {
        var icon = Ui.Label(Icon(id), ColorOf(id), 24);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var count = Ui.Label(amount.ToString(), color ?? Palette.Text, 13);
        count.HorizontalAlignment = HorizontalAlignment.Center;
        count.AddThemeFontOverride("font", Ui.Mono);
        var col = Ui.Column(0, icon, count);
        col.CustomMinimumSize = new Vector2(46, 0);
        col.TooltipText = Title(id);
        col.MouseFilter = Control.MouseFilterEnum.Pass;
        icon.MouseFilter = count.MouseFilter = Control.MouseFilterEnum.Pass;
        return col;
    }

    /// <summary>Gold plus every material the profile has (or has ever needed), for top bars; as big tiles with <paramref name="tiles"/>.</summary>
    public static HBoxContainer Bar(Profile profile, int size = 15, bool tiles = false)
    {
        var row = Ui.Row(tiles ? 6 : 14, Counter(Resources.Gold, profile.Gold, size + 1, Palette.Accent, tiles));
        foreach (var id in Resources.All)
        {
            var n = profile.Amount(id);
            if (n > 0 || Shown.GetValueOrDefault(id) > 0) row.AddChild(Counter(id, n, size, null, tiles));
        }
        return row;
    }

    /// <summary>A price: each part turns red when the profile doesn't have enough.</summary>
    public static HBoxContainer Price(IReadOnlyDictionary<string, int> price, Profile? profile, int size = 13)
    {
        var row = Ui.Row(10);
        foreach (var id in new[] { Resources.Gold }.Concat(Resources.All))
        {
            if (!price.TryGetValue(id, out var n) || n <= 0) continue;
            var enough = profile is null || profile.Amount(id) >= n;
            row.AddChild(Chip(id, n, size, enough ? Palette.Text : Palette.Danger));
        }
        return row;
    }

    /// <summary>"+3 ◆  +2 ■" for rewards.</summary>
    public static HBoxContainer Gains(IReadOnlyDictionary<string, int> amounts, int size = 14, string sign = "+")
    {
        var row = Ui.Row(12);
        foreach (var id in new[] { Resources.Gold }.Concat(Resources.All))
        {
            if (amounts.TryGetValue(id, out var n) && n != 0) row.AddChild(Chip(id, n, size, Palette.Text, $"{sign}{n} {Resources.Name(id)}"));
        }
        return row;
    }
}
