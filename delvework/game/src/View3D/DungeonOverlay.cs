using Delvework.Core.Sim;
using Godot;

namespace Delvework.Game.View3D;

internal sealed record OverlayItem(Vector2 At, string Name, int Hp, int MaxHp, float Mana, Color Color, IntentKind? Intent, string Status, bool IsGolem);

internal sealed partial class DungeonOverlay(DungeonView3D view) : Control
{
    public DungeonOverlay() : this(null!)
    {
    }

    public override void _Draw()
    {
        if (view is null) return;
        var font = Ui.Mono;
        foreach (var item in view.OverlayItems())
        {
            var w = item.IsGolem ? 64f : 44f;
            var x = item.At.X - w / 2;
            var y = item.At.Y;
            if (item.IsGolem)
            {
                DrawString(font, new Vector2(x - 20, y - 8), item.Name, HorizontalAlignment.Center, w + 40, 12, new Color(item.Color, 0.95f));
                if (item.Status.Length > 0) DrawString(font, new Vector2(x - 30, y - 22), item.Status, HorizontalAlignment.Center, w + 60, 11, Palette.Danger);
            }
            DrawRect(new Rect2(x - 1, y - 1, w + 2, 7), new Color(0, 0, 0, 0.75f));
            var frac = item.MaxHp > 0 ? Math.Clamp(item.Hp / (float)item.MaxHp, 0, 1) : 0;
            var hpColor = item.IsGolem ? (frac > 0.3f ? Palette.Ok : Palette.Danger) : new Color("e0564f");
            DrawRect(new Rect2(x, y, w * frac, 5), hpColor);
            if (item.Mana >= 0)
            {
                DrawRect(new Rect2(x - 1, y + 6, w + 2, 4), new Color(0, 0, 0, 0.75f));
                DrawRect(new Rect2(x, y + 7, w * item.Mana, 2), new Color("6fb6ff"));
            }
            if (item.Intent is { } intent)
            {
                var (glyph, color) = intent switch
                {
                    IntentKind.Attack => ("!", new Color("ff5a4f")),
                    IntentKind.Chase => (">>", new Color("ffb347")),
                    IntentKind.Flee => ("<<", Palette.Ok),
                    IntentKind.Move => ("~", Palette.Muted),
                    _ => ("z", new Color("6a6f86")),
                };
                var c = new Vector2(item.At.X, y - 14);
                DrawCircle(c, 9, new Color(0, 0, 0, 0.7f));
                DrawArc(c, 9, 0, Mathf.Tau, 20, color, 1.5f);
                DrawString(font, c + new Vector2(-12, 5), glyph, HorizontalAlignment.Center, 24, 13, color);
            }
        }
        foreach (var (at, text, color, age) in view.FloatingNumbers())
        {
            var alpha = Math.Clamp(1 - age, 0, 1);
            var p = at + new Vector2(-20, -age * 30);
            DrawString(font, p + new Vector2(1, 1), text, HorizontalAlignment.Center, 40, 16, new Color(0, 0, 0, alpha * 0.8f));
            DrawString(font, p, text, HorizontalAlignment.Center, 40, 16, new Color(color, alpha));
        }
    }
}
