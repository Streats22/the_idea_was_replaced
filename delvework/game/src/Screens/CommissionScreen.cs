using Delvework.Core.Content;
using Delvework.Core.Progress;
using Delvework.Game.Audio;
using Godot;

namespace Delvework.Game.Screens;

/// <summary>
/// The commission board: orders from the valley, each a workshop puzzle on fixed stores. Scripts
/// are graded for speed (ticks) and size (lines); gold, silver or bronze for each.
/// </summary>
public partial class CommissionScreen : Control
{
    public required App App { get; init; }
    public CommissionDef? Initial { get; init; }

    public override void _Ready()
    {
        var p = App.Progress;
        var root = Ui.Column(12);
        var margin = Ui.Pad(root, 16);
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(margin);

        var back = Ui.Button("← Town");
        back.Pressed += () =>
        {
            App.Audio.Play(Sfx.Click);
            App.GoTown();
        };
        var golds = App.Profile.Commissions.Values.Sum(b => (b.TicksMedal == Medal.Gold ? 1 : 0) + (b.LinesMedal == Medal.Gold ? 1 : 0));
        root.AddChild(Ui.Row(14, back,
            Ui.Column(0, Ui.Label("PLAZA", Palette.Accent, 11), Ui.Heading("Commission board", 24)),
            Ui.Label($"Delivered {App.Profile.Commissions.Count} of {p.Content.Commissions.Count} · {golds} of {p.Content.Commissions.Count * 2} gold medals", Palette.Muted, 14),
            Ui.Spacer(), Goods.Bar(App.Profile, 14), Help.Menu(App)));
        root.AddChild(Ui.Para("Each order brings its own stores, so nothing leaves your village. Get the goal done and you are paid once; then make it faster (fewer ticks) or shorter (fewer lines) for gold medals. Your best result for each is kept.", Palette.Muted, 13));

        var list = Ui.Column(10);
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(Ui.Pad(list, 4));
        root.AddChild(scroll);
        foreach (var w in p.Content.Workshops)
        {
            var orders = p.Content.Commissions.Where(c => c.Workshop == w.Id).ToList();
            if (orders.Count == 0) continue;
            var built = p.IsBuilt(w);
            list.AddChild(Ui.Row(8, Ui.Label(w.Name.ToUpperInvariant(), built ? Palette.Accent : Palette.Muted, 12),
                Ui.Label(built ? "" : "not built yet: you can practise, but deliveries need the building", Palette.Muted, 11)));
            var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            grid.AddThemeConstantOverride("h_separation", 10);
            grid.AddThemeConstantOverride("v_separation", 10);
            foreach (var c in orders) grid.AddChild(Card(c, w));
            list.AddChild(grid);
        }
    }

    private PanelContainer Card(CommissionDef c, WorkshopDef w)
    {
        var best = App.Progress.Best(c);
        var open = Ui.Button(best is null ? "Take the order" : "Improve", $"Open the {w.Name} on this order's stores", primary: best is null && App.Progress.IsOpen(c));
        open.Pressed += () =>
        {
            App.Audio.Play(Sfx.Click);
            App.Show(new WorkshopScreen { App = App, Workshop = w, Commission = c });
        };
        var col = Ui.Column(6,
            Ui.Row(8, Ui.Label(c.Title, Palette.Text, 17), Ui.Spacer(), Badge(best?.TicksMedal ?? Medal.None, "speed"), Badge(best?.LinesMedal ?? Medal.None, "size")),
            Ui.Label($"from {c.Client}", Palette.Muted, 12),
            Ui.Para(c.Brief, Palette.Text, 13),
            Ui.Row(10, Ui.Label($"Goal: {c.Goal.Amount} {Resources.Name(c.Goal.Resource)}", Palette.Accent, 13),
                Ui.Label("Stores:", Palette.Muted, 12), Goods.Price(c.Stock, null, 12)),
            Ui.Row(10, Ui.Label(best is null ? "Pays:" : "Paid:", Palette.Muted, 12), Goods.Price(c.Reward, null, 12),
                Ui.Label($"+{c.GoldMedalBonus} per gold medal", Palette.Muted, 12), Ui.Spacer(), open));
        if (best is not null) col.AddChild(Ui.Label($"Best: {best.Ticks} ticks (gold ≤ {c.Par.Ticks[0]}) · {best.Lines} lines (gold ≤ {c.Par.Lines[0]})", Palette.Muted, 12));
        var card = Ui.Card(col);
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.CustomMinimumSize = new Vector2(420, 0);
        var highlight = Initial?.Id == c.Id;
        card.AddThemeStyleboxOverride("panel", Ui.Box(Palette.Panel2, highlight ? Palette.Accent : best is null ? Palette.Border : WorkshopScreen.MedalColor(best.TicksMedal).Darkened(0.5f), 10, 12));
        return card;
    }

    private static Control Badge(Medal m, string what)
    {
        var color = WorkshopScreen.MedalColor(m);
        var badge = new PanelContainer { TooltipText = $"{WorkshopScreen.MedalName(m)} for {what}" };
        badge.AddThemeStyleboxOverride("panel", Ui.Box(m == Medal.None ? Palette.Panel : color.Darkened(0.65f), color, 12, 4));
        badge.AddChild(Ui.Label((m == Medal.None ? "○ " : "● ") + what, color, 11));
        return badge;
    }
}
