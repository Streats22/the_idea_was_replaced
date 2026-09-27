using Godot;

namespace Delvework.Game.View3D;

/// <summary>A clickable place in town, shown as a wooden sign hanging over it.</summary>
public sealed record TownSign(string Id, string Title, string Subtitle, bool Enabled, bool Highlight);

/// <summary>
/// A wooden plaque on two ropes, like the signs in the concept art: a carved title, a small line
/// under it, an ember edge when it's where to go next.
/// </summary>
public partial class HangingSign : PanelContainer
{
    public const float RopeLength = 14;

    public float Phase { get; init; }
    public event Action? Pressed;

    private readonly Label _title = Ui.Title("", 14, Palette.Parchment);
    private readonly Label _sub = Ui.Label("", Palette.Parchment.Darkened(0.25f), 12);
    private bool _enabled = true, _highlight, _hover;

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _sub.HorizontalAlignment = HorizontalAlignment.Center;
        _title.MouseFilter = _sub.MouseFilter = MouseFilterEnum.Ignore;
        var col = Ui.Column(0, _title, _sub);
        col.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(col);
        MouseEntered += () => Hover(true);
        MouseExited += () => Hover(false);
        Restyle();
    }

    private void Hover(bool on)
    {
        _hover = on;
        Restyle();
    }

    public void Set(TownSign s)
    {
        _title.Text = s.Title.ToUpperInvariant();
        _sub.Text = s.Subtitle;
        _sub.Visible = s.Subtitle.Length > 0;
        _enabled = s.Enabled;
        _highlight = s.Highlight;
        if (IsInsideTree()) Restyle();
    }

    private void Restyle()
    {
        var wood = _enabled ? new Color("5a3a20") : new Color("3a2a1e");
        if (_hover && _enabled) wood = wood.Lightened(0.12f);
        var sb = Ui.Box(wood, _highlight ? Palette.Accent : new Color("24160b"), 3, 10);
        sb.SetBorderWidthAll(2);
        sb.ContentMarginLeft = sb.ContentMarginRight = 14;
        sb.ContentMarginTop = 6;
        sb.ContentMarginBottom = 7;
        sb.ShadowColor = _highlight ? new Color(Palette.Accent, 0.45f) : new Color(0, 0, 0, 0.55f);
        sb.ShadowSize = _highlight ? 12 : 6;
        sb.ShadowOffset = _highlight ? Vector2.Zero : new Vector2(0, 3);
        AddThemeStyleboxOverride("panel", sb);
        Modulate = _enabled ? Colors.White : new Color(1, 1, 1, 0.7f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var w = Size.X;
        var rope = new Color("2a1c10");
        foreach (var x in new[] { w * 0.22f, w * 0.78f })
        {
            DrawLine(new Vector2(x, -RopeLength), new Vector2(x, 2), rope, 2f);
            DrawCircle(new Vector2(x, 4), 2.2f, Palette.BrassDim);
        }
        DrawLine(new Vector2(w * 0.12f, -RopeLength), new Vector2(w * 0.88f, -RopeLength), new Color("1c120a"), 3f);
        var grain = new Color(0, 0, 0, 0.16f);
        for (var y = 8f; y < Size.Y - 4; y += 9) DrawLine(new Vector2(5, y), new Vector2(w - 5, y), grain, 1f);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (!_enabled || e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
        AcceptEvent();
        Pressed?.Invoke();
    }
}

/// <summary>
/// The village of Hollowmere, in the same pixelated 2.5D look as the dungeon. Days pass slowly
/// (dawn, day, dusk, a long night); ruined lots are rebuilt as Village skills are bought and
/// townsfolk move back in. The cave in the cliff leads to the dungeon.
/// </summary>
public partial class TownView3D : Control
{
    /// <summary>Seconds for one full day.</summary>
    public const float DayLength = 240f;

    /// <summary>A moment of the day: sky, ambient light, sun (or moon) and how bright the lamps burn.</summary>
    private readonly record struct Sky(float At, Color Background, Color Ambient, float AmbientEnergy, Color Sun, float SunEnergy, float SunPitch, float Lamps);

    private static readonly Sky[] Skies =
    [
        new(0.00f, new Color("3a2f4a"), new Color("c9a0b0"), 0.45f, new Color("ffb080"), 0.55f, -18, 0.55f),
        new(0.10f, new Color("6f96bb"), new Color("c8d8e8"), 0.75f, new Color("fff0d8"), 1.25f, -55, 0.08f),
        new(0.38f, new Color("7fa0c0"), new Color("d8d0c0"), 0.7f, new Color("ffe8c8"), 1.15f, -45, 0.1f),
        new(0.48f, new Color("4a2a3a"), new Color("d08a70"), 0.5f, new Color("ff8a50"), 0.75f, -16, 0.7f),
        new(0.58f, new Color("0b0f1a"), new Color("5b6f9e"), 0.35f, new Color("8ea4d8"), 0.35f, -50, 1f),
        new(0.92f, new Color("0b0f1a"), new Color("5b6f9e"), 0.35f, new Color("8ea4d8"), 0.35f, -50, 1f),
        new(1.00f, new Color("3a2f4a"), new Color("c9a0b0"), 0.45f, new Color("ffb080"), 0.55f, -18, 0.55f),
    ];

    /// <summary>Someone walking a loop through town: a golem of the party or a villager.</summary>
    private sealed record Stroller(Node3D Root, Node3D Body, Node3D? LeftArm, Node3D? RightArm, Vector3[] Loop, float Speed, float Offset, bool NightOwl);

    /// <summary>Where each sign floats (world position) and which building owns it.</summary>
    private static readonly Dictionary<string, Vector3> SignSpots = new(StringComparer.Ordinal)
    {
        ["delve"] = new(0, 4.2f, -12.5f),
        ["library"] = new(-8, 4.6f, -1),
        ["village"] = new(2, 2.8f, 0.5f),
        ["equipment"] = new(7, 4.2f, -4),
        ["arcana"] = new(-10, 9.5f, -8),
        ["smelter"] = new(5.5f, 4.4f, -9.5f),
        ["bakery"] = new(12.5f, 4.2f, 4.5f),
        ["farm"] = new(6, 2.6f, 10.5f),
        ["commissions"] = new(-4.2f, 3.4f, 3.2f),
    };

    /// <summary>Lots for the material buildings; trees keep clear of them.</summary>
    private static readonly Vector3 LumberLot = new(-13.5f, 0, 1.5f), QuarryLot = new(-5, 0, -10.5f), FarmLot = new(6, 0, 10.5f),
        SmelterLot = new(5.5f, 0, -9.5f), BakeryLot = new(12.5f, 0, 4.5f), TavernLot = new(-14f, 0, -3.5f);

    /// <summary>Windows share one material so they can dim by day.</summary>
    private static readonly StandardMaterial3D WindowGlow = Iso.Glow(new Color("ffc46b"), 2.4f);

    private SubViewport _viewport = null!;
    private Camera3D _camera = null!;
    private Node3D _world = null!, _lots = null!;
    private Control _signLayer = null!;
    private readonly Dictionary<string, HangingSign> _signs = [];
    private readonly List<Node3D> _wheels = [];
    private readonly List<(Node3D Node, Vector3 Base, float Phase)> _smoke = [];
    private readonly List<(Node3D Node, Vector3 Base, float Phase)> _fireflies = [];
    private readonly List<Node3D> _spinners = [];
    private readonly List<(OmniLight3D Light, float Base, float Phase)> _flicker = [];
    private readonly List<Stroller> _walkers = [];
    private DirectionalLight3D _sun = null!;
    private Godot.Environment _env = null!;
    private double _time;
    private string _builtKey = "";

    /// <summary>Where in the day it is: 0 dawn, 0.25 noon, 0.5 dusk, 0.75 midnight. Advances by itself.</summary>
    public float TimeOfDay { get; set; } = 0.62f;

    /// <summary>Stop the clock (screenshots).</summary>
    public bool Frozen { get; set; }

    public event Action<string>? Picked;

    public override void _Ready()
    {
        ClipContents = true;
        var (container, viewport) = Iso.PixelViewport();
        container.SetAnchorsPreset(LayoutPreset.FullRect);
        container.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(container);
        _viewport = viewport;
        _env = Iso.Environment(new Color("0b0f1a"), new Color("5b6f9e"), 0.35f, 0.8f);
        viewport.AddChild(new WorldEnvironment { Environment = _env });
        _camera = Iso.Camera(26f);
        viewport.AddChild(_camera);
        Iso.Aim(_camera, new Vector3(0, 0, -2));
        _sun = new DirectionalLight3D
        {
            LightColor = new Color("8ea4d8"),
            LightEnergy = 0.35f,
            ShadowEnabled = true,
            RotationDegrees = new Vector3(-50, -35, 0),
        };
        viewport.AddChild(_sun);
        _world = new Node3D();
        _lots = new Node3D();
        viewport.AddChild(_world);
        viewport.AddChild(_lots);
        BuildScenery();

        _signLayer = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _signLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_signLayer);
    }

    public Camera3D Camera => _camera;

    /// <summary>Rebuild lots for the given building levels and show the golems of the party.</summary>
    public void Refresh(IReadOnlyDictionary<string, int> buildings, IReadOnlyList<string> party)
    {
        var key = string.Join(",", buildings.OrderBy(b => b.Key, StringComparer.Ordinal).Select(b => $"{b.Key}{b.Value}")) + "|" + string.Join(",", party);
        if (key == _builtKey) return;
        _builtKey = key;
        foreach (var n in _lots.GetChildren()) n.QueueFree();
        _wheels.Clear();
        _smoke.RemoveAll(s => !IsInstanceValid(s.Node) || s.Node.GetParent() != _world);
        _spinners.Clear();
        _walkers.Clear();
        _flicker.RemoveAll(f => !IsInstanceValid(f.Light) || !_world.IsAncestorOf(f.Light));
        int L(string b) => buildings.GetValueOrDefault(b);

        Forge(new Vector3(7, 0, -4), L("forge"));
        Tower(new Vector3(-10, 0, -8), L("tower"));
        Guild(new Vector3(8, 0, 5), L("guild"));
        Market(new Vector3(0, 0, 6), L("market"));
        Mill(new Vector3(14, 0, -1), L("mill"));
        Cottages(L("houses"));
        Lumber(LumberLot, L("lumber"));
        Quarry(QuarryLot, L("quarry"));
        Farm(FarmLot, L("farm"));
        Smelter(SmelterLot, L("smelter"));
        Bakery(BakeryLot, L("bakery"));
        Tavern(TavernLot, L("tavern"));

        for (var i = 0; i < party.Count; i++)
        {
            var fig = Figures.Golem(party[i]);
            _lots.AddChild(fig.Root);
            _walkers.Add(new Stroller(fig.Root, fig.Body, fig.LeftArm, fig.RightArm, GolemLoop, 0.9f, i / (float)Math.Max(1, party.Count), true));
        }
        var folk = Math.Min(VillagerLoops.Length * 2, 1 + L("mill") + L("market") * 2 + L("houses") * 3 + L("tavern") * 2 + L("bakery") + L("farm"));
        for (var i = 0; i < folk; i++) Villager(i);
    }

    private static readonly Vector3[] GolemLoop =
    [
        new(-3, 0, 1.5f), new(3, 0, 1.5f), new(4, 0, -3), new(0, 0, -7), new(-4, 0, -3),
    ];

    /// <summary>Routes the townsfolk walk: round the plaza, down to the market and fields, over to the mill.</summary>
    private static readonly Vector3[][] VillagerLoops =
    [
        [new(-2, 0, -2), new(2, 0, -2), new(2.5f, 0, 3), new(-2.5f, 0, 3)],
        [new(0, 0, 2), new(0.4f, 0, 8.5f), new(5, 0, 8.4f), new(4, 0, 2.2f)],
        [new(-6, 0, 4.5f), new(-9.5f, 0, 3.4f), new(-4, 0, 9), new(-1, 0, 4)],
        [new(4, 0, 0), new(11, 0, 0.4f), new(11.5f, 0, 7.8f), new(8, 0, 2.2f)],
        [new(-1, 0, -5), new(-1, 0, -10), new(1, 0, -10), new(1, 0, -5)],
    ];

    private static readonly Color[] Clothes =
    [
        new("8e2f3a"), new("3f7a8e"), new("d9b44a"), new("5a7a3a"), new("7a4a8e"), new("b86a3a"), new("4a5a8e"), new("a0a0a0"),
    ];

    private void Villager(int i)
    {
        var root = new Node3D();
        _lots.AddChild(root);
        var body = new Node3D();
        root.AddChild(body);
        var cloth = Iso.Solid(Clothes[i % Clothes.Length], 0.9f);
        var skin = Iso.Solid(new[] { new Color("e0b890"), new Color("b88a60"), new Color("8a6040") }[i % 3], 0.8f);
        Iso.Cylinder(body, 0.18f, 0.3f, 0.75f, new Vector3(0, 0.38f, 0), cloth, 8);
        Iso.Ball(body, 0.17f, new Vector3(0, 0.92f, 0), skin);
        if (i % 3 == 0) Iso.Cylinder(body, 0.08f, 0.26f, 0.2f, new Vector3(0, 1.1f, 0), Iso.Solid(new Color("5a3a22")), 8);
        if (i % 4 == 1) Iso.Box(body, new Vector3(0.3f, 0.22f, 0.22f), new Vector3(0.28f, 0.45f, 0.1f), Iso.Wood(new Color("9a7040"), new Color("5a3a1a"), "town-basket"));
        var arms = new Node3D[2];
        for (var s = 0; s < 2; s++)
        {
            arms[s] = new Node3D { Position = new Vector3(s == 0 ? -0.22f : 0.22f, 0.65f, 0) };
            body.AddChild(arms[s]);
            Iso.Box(arms[s], new Vector3(0.08f, 0.4f, 0.08f), new Vector3(0, -0.2f, 0), cloth);
        }
        var loop = VillagerLoops[i % VillagerLoops.Length];
        _walkers.Add(new Stroller(root, body, arms[0], arms[1], i / VillagerLoops.Length % 2 == 0 ? loop : [.. loop.Reverse()],
            0.55f + Iso.Hash(i, 0, 60) * 0.35f, Iso.Hash(i, 1, 60), i % 3 == 0));
    }

    public void SetSigns(IEnumerable<TownSign> signs)
    {
        foreach (var s in signs)
        {
            if (!_signs.TryGetValue(s.Id, out var b))
            {
                b = new HangingSign { Phase = _signs.Count * 1.7f };
                var id = s.Id;
                b.Pressed += () => Picked?.Invoke(id);
                _signLayer.AddChild(b);
                _signs[s.Id] = b;
            }
            b.Set(s);
        }
    }

    public override void _Process(double delta)
    {
        _time += delta;
        var t = (float)_time;
        if (!Frozen) TimeOfDay = (TimeOfDay + (float)delta / DayLength) % 1f;
        var night = UpdateSky();
        Iso.Aim(_camera, new Vector3(Mathf.Sin(t * 0.07f) * 0.8f, 0, -2 + Mathf.Cos(t * 0.05f) * 0.5f));
        foreach (var w in _wheels) w.Rotation = new Vector3(0, 0, -t * 0.9f);
        foreach (var s in _spinners) s.Rotation = new Vector3(0, t * 0.8f, 0);
        foreach (var (node, b, phase) in _smoke)
        {
            var k = (t * 0.25f + phase) % 1f;
            node.Position = b + new Vector3(Mathf.Sin(k * 5 + phase) * 0.3f + k * 0.8f, k * 3.2f, 0);
            node.Scale = Vector3.One * (0.4f + k * 1.3f);
            node.Visible = true;
        }
        foreach (var (node, b, phase) in _fireflies)
        {
            node.Position = b + new Vector3(Mathf.Sin(t * 0.6f + phase) * 1.4f, Mathf.Sin(t * 1.3f + phase * 2) * 0.4f, Mathf.Cos(t * 0.5f + phase) * 1.4f);
            var blink = 0.6f + 0.4f * Mathf.Sin(t * 2.1f + phase * 3);
            node.Scale = Vector3.One * Mathf.Clamp((night - 0.4f) * 1.7f, 0, 1) * blink;
            node.Visible = night > 0.4f;
        }
        foreach (var (light, energy, phase) in _flicker)
        {
            light.LightEnergy = energy * (0.2f + 0.8f * night) * (0.85f + 0.15f * Mathf.Sin(t * 11 + phase) + 0.08f * Mathf.Sin(t * 27 + phase));
        }
        Walk(t, night);
        foreach (var (id, button) in _signs)
        {
            var at = SignSpots[id];
            if (_camera.IsPositionBehind(at)) continue;
            var screen = _camera.UnprojectPosition(at) * Iso.PixelScale;
            var size = button.GetCombinedMinimumSize();
            button.Size = size;
            button.Position = screen - new Vector2(size.X / 2, size.Y);
            button.PivotOffset = new Vector2(size.X / 2, -HangingSign.RopeLength);
            button.Rotation = 0.025f * Mathf.Sin(t * 1.1f + button.Phase);
        }
    }

    /// <summary>
    /// Blend the sky keyframes for <see cref="TimeOfDay"/> into the environment and sun; returns
    /// how dark it is (0 bright day, 1 deep night), which drives lamps, windows and fireflies.
    /// </summary>
    private float UpdateSky()
    {
        var at = TimeOfDay;
        var k = 0;
        while (k < Skies.Length - 2 && Skies[k + 1].At <= at) k++;
        var (a, b) = (Skies[k], Skies[k + 1]);
        var f = Mathf.SmoothStep(0, 1, (at - a.At) / Math.Max(0.0001f, b.At - a.At));
        _env.BackgroundColor = a.Background.Lerp(b.Background, f);
        _env.AmbientLightColor = a.Ambient.Lerp(b.Ambient, f);
        _env.AmbientLightEnergy = Mathf.Lerp(a.AmbientEnergy, b.AmbientEnergy, f);
        _sun.LightColor = a.Sun.Lerp(b.Sun, f);
        _sun.LightEnergy = Mathf.Lerp(a.SunEnergy, b.SunEnergy, f);
        var yaw = at < 0.5f ? Mathf.Lerp(-80, 30, at * 2) : -35;
        _sun.RotationDegrees = new Vector3(Mathf.Lerp(a.SunPitch, b.SunPitch, f), yaw, 0);
        var lamps = Mathf.Lerp(a.Lamps, b.Lamps, f);
        WindowGlow.EmissionEnergyMultiplier = 0.5f + 2f * lamps;
        return lamps;
    }

    private void Walk(float t, float night)
    {
        for (var g = 0; g < _walkers.Count; g++)
        {
            var s = _walkers[g];
            var home = !s.NightOwl && night > 0.85f && Iso.Hash(g, 7, 61) > 0.35f;
            s.Root.Visible = !home;
            if (home) continue;
            var loop = s.Loop;
            var total = 0f;
            for (var i = 0; i < loop.Length; i++) total += loop[i].DistanceTo(loop[(i + 1) % loop.Length]);
            var d = (t * s.Speed + s.Offset * total) % total;
            for (var i = 0; i < loop.Length; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % loop.Length];
                var len = a.DistanceTo(b);
                if (d > len)
                {
                    d -= len;
                    continue;
                }
                s.Root.Position = a.Lerp(b, d / len) + new Vector3(0, 0.02f, 0);
                var dir = b - a;
                s.Root.Rotation = new Vector3(0, Mathf.Atan2(dir.X, dir.Z), 0);
                var step = Mathf.Sin(t * 7 * s.Speed + g);
                s.Body.Position = new Vector3(0, Mathf.Abs(step) * 0.05f, 0);
                if (s.LeftArm is not null) s.LeftArm.Rotation = new Vector3(step * 0.5f, 0, 0);
                if (s.RightArm is not null) s.RightArm.Rotation = new Vector3(-step * 0.5f, 0, 0);
                break;
            }
        }
    }

    // ----- Materials -----

    private static Material Grass => Iso.Rock(new Color("3d5a36"), new Color("243a22"), 0.25f, "town-grass");
    private static Material Path => Iso.Flagstone(new Color("8a7f70"), new Color("4a433b"), 0.7f, "town-path");
    private static Material StoneWall => Iso.Flagstone(new Color("8b8378"), new Color("4a443d"), 0.9f, "town-stone");
    private static Material Plaster => Iso.Rock(new Color("cfc2a6"), new Color("9c8f75"), 0.6f, "town-plaster");
    private static Material Timber => Iso.Wood(new Color("6b4a2c"), new Color("3a2716"), "town-timber");
    private static Material RoofRed => Iso.Solid(new Color("7a3b2e"), 0.8f);
    private static Material RoofSlate => Iso.Solid(new Color("3f4652"), 0.7f);
    private static Material Rubble => Iso.Rock(new Color("6d665c"), new Color("3a352f"), 0.8f, "town-rubble");

    // ----- Scenery that never changes -----

    private void BuildScenery()
    {
        Iso.Box(_world, new Vector3(64, 0.4f, 52), new Vector3(0, -0.2f, 0), Grass);
        // Paths: plaza, road to the cave, branches to each lot.
        Iso.Box(_world, new Vector3(9, 0.06f, 7), new Vector3(0, 0.03f, -1), Path);
        Iso.Box(_world, new Vector3(2.4f, 0.06f, 9), new Vector3(0, 0.03f, -8.5f), Path);
        Iso.Box(_world, new Vector3(2, 0.06f, 7), new Vector3(0, 0.03f, 5.5f), Path);
        Iso.Box(_world, new Vector3(9, 0.06f, 1.8f), new Vector3(8, 0.03f, 0), Path);
        Iso.Box(_world, new Vector3(8, 0.06f, 1.8f), new Vector3(-8, 0.03f, -1), Path);
        Iso.Box(_world, new Vector3(1.8f, 0.06f, 6), new Vector3(-8, 0.03f, -5), Path);

        // The plaza: a well with the notice board (the Village tree).
        var well = new Node3D { Position = new Vector3(0, 0, 0.5f) };
        _world.AddChild(well);
        Iso.Cylinder(well, 0.9f, 1f, 0.8f, new Vector3(0, 0.4f, 0), StoneWall, 12);
        Iso.Cylinder(well, 0.72f, 0.72f, 0.05f, new Vector3(0, 0.8f, 0), Iso.Glow(new Color("2a4d7a"), 0.8f), 12);
        Iso.Box(well, new Vector3(0.12f, 1.4f, 0.12f), new Vector3(-0.8f, 1.1f, 0), Timber);
        Iso.Box(well, new Vector3(0.12f, 1.4f, 0.12f), new Vector3(0.8f, 1.1f, 0), Timber);
        Iso.Roof(well, new Vector3(2.1f, 0.6f, 1.3f), new Vector3(0, 2.0f, 0), RoofRed);
        var board = new Node3D { Position = new Vector3(2.2f, 0, 1.2f), RotationDegrees = new Vector3(0, -20, 0) };
        _world.AddChild(board);
        Iso.Box(board, new Vector3(0.1f, 1.6f, 0.1f), new Vector3(-0.6f, 0.8f, 0), Timber);
        Iso.Box(board, new Vector3(0.1f, 1.6f, 0.1f), new Vector3(0.6f, 0.8f, 0), Timber);
        Iso.Box(board, new Vector3(1.5f, 0.9f, 0.08f), new Vector3(0, 1.25f, 0), Timber);
        for (var i = 0; i < 4; i++) Iso.Box(board, new Vector3(0.28f, 0.34f, 0.02f), new Vector3(-0.5f + i * 0.33f, 1.25f + (i % 2) * 0.08f, 0.05f), Iso.Solid(new Color("e8dcc0")));
        CommissionBoard(new Vector3(-3.4f, 0, 3.2f));

        Library(new Vector3(-8, 0, -1));
        Cliff();
        River();
        Trees();

        foreach (var p in new[] { new Vector3(-2.6f, 0, -4), new Vector3(2.6f, 0, -4), new Vector3(4.8f, 0, 2.5f), new Vector3(-4.8f, 0, 2.5f), new Vector3(1.6f, 0, 8), new Vector3(-5, 0, -1.6f), new Vector3(11, 0, 1) })
        {
            Lamp(p);
        }
        for (var i = 0; i < 18; i++)
        {
            var b = new Vector3(Iso.Hash(i, 1, 40) * 40 - 20, 1 + Iso.Hash(i, 2, 40) * 1.5f, Iso.Hash(i, 3, 40) * 26 - 12);
            var f = Iso.Ball(_world, 0.05f, b, Iso.Glow(new Color("d8ff8a"), 4f));
            _fireflies.Add((f, b, i * 1.7f));
        }
    }

    /// <summary>A roofed board with orders pinned to it and a lantern: where commissions are posted.</summary>
    private void CommissionBoard(Vector3 at)
    {
        var n = new Node3D { Position = at, RotationDegrees = new Vector3(0, 25, 0) };
        _world.AddChild(n);
        foreach (var x in new[] { -0.8f, 0.8f }) Iso.Box(n, new Vector3(0.12f, 2f, 0.12f), new Vector3(x, 1f, 0), Timber);
        Iso.Box(n, new Vector3(1.7f, 1.1f, 0.08f), new Vector3(0, 1.35f, 0), Iso.Wood(new Color("8a6a44"), new Color("4a3620"), "town-board"));
        Iso.Roof(n, new Vector3(2f, 0.45f, 0.6f), new Vector3(0, 2.15f, 0), RoofSlate);
        var paper = new[] { new Color("e8dcc0"), new Color("f0e0a0"), new Color("d8e4f0") };
        for (var i = 0; i < 5; i++)
        {
            Iso.Box(n, new Vector3(0.26f, 0.32f, 0.02f), new Vector3(-0.6f + i * 0.3f, 1.3f + (i % 2) * 0.18f, 0.05f), Iso.Solid(paper[i % 3]), new Vector3(0, 0, (i - 2) * 4));
            Iso.Box(n, new Vector3(0.05f, 0.05f, 0.02f), new Vector3(-0.6f + i * 0.3f, 1.44f + (i % 2) * 0.18f, 0.065f), Iso.Solid(new Color("c04040")));
        }
        Iso.Box(n, new Vector3(0.18f, 0.22f, 0.18f), new Vector3(0.95f, 1.85f, 0.15f), Iso.Glow(new Color("ffc46b"), 3f));
        var l = Iso.Light(n, new Vector3(0.9f, 1.8f, 0.6f), new Color("ffb86b"), 1.0f, 4f);
        _flicker.Add((l, 1.0f, 23));
    }

    private void Lamp(Vector3 p)
    {
        var n = new Node3D { Position = p };
        _world.AddChild(n);
        Iso.Cylinder(n, 0.05f, 0.07f, 2.2f, new Vector3(0, 1.1f, 0), Iso.Solid(new Color("2a2622"), 0.5f, 0.6f), 6);
        Iso.Box(n, new Vector3(0.28f, 0.34f, 0.28f), new Vector3(0, 2.3f, 0), Iso.Glow(new Color("ffc46b"), 3f));
        var l = Iso.Light(n, new Vector3(0, 2.3f, 0), new Color("ffb86b"), 1.4f, 6f);
        _flicker.Add((l, 1.4f, p.X * 3 + p.Z));
    }

    private void Cliff()
    {
        var rock = Iso.Rock(new Color("5d5752"), new Color("2b2826"), 0.3f, "cliff");
        for (var x = -30; x <= 30; x += 2)
        {
            if (Math.Abs(x) <= 1) continue;
            var h = 5 + Iso.Hash(x, 0, 21) * 4 + Math.Abs(x) * 0.08f;
            var z = -14 - Iso.Hash(x, 1, 21) * 1.5f;
            Iso.Box(_world, new Vector3(2.4f, h, 3 + Iso.Hash(x, 2, 21) * 2), new Vector3(x, h / 2 - 0.2f, z), rock, new Vector3(0, Iso.Hash(x, 3, 21) * 12 - 6, 0));
        }
        var cave = new Node3D { Position = new Vector3(0, 0, -13.2f) };
        _world.AddChild(cave);
        Iso.Box(cave, new Vector3(3, 3.4f, 2), new Vector3(0, 1.5f, -0.8f), Iso.Solid(new Color("020203")));
        Iso.Box(cave, new Vector3(1, 4.4f, 2.6f), new Vector3(-2, 2f, -0.6f), rock);
        Iso.Box(cave, new Vector3(1, 4.4f, 2.6f), new Vector3(2, 2f, -0.6f), rock);
        Iso.Box(cave, new Vector3(5, 1.4f, 2.6f), new Vector3(0, 4.4f, -0.6f), rock);
        var runes = Iso.Glow(new Color("5dd3e8"), 3.5f);
        for (var i = 0; i < 5; i++)
        {
            var a = Mathf.Pi * (i + 0.5f) / 5;
            Iso.Box(cave, new Vector3(0.18f, 0.18f, 0.05f), new Vector3(Mathf.Cos(a) * -1.7f, 1.3f + Mathf.Sin(a) * 2.2f, 0.72f), runes, new Vector3(0, 0, 45));
        }
        var glow = Iso.Light(cave, new Vector3(0, 1.4f, 1.2f), new Color("5dd3e8"), 2.2f, 7f);
        _flicker.Add((glow, 2.2f, 0));
        foreach (var s in new[] { -1f, 1f })
        {
            var torch = new Node3D { Position = new Vector3(s * 2.8f, 0, 1f) };
            cave.AddChild(torch);
            Iso.Cylinder(torch, 0.06f, 0.08f, 1.6f, new Vector3(0, 0.8f, 0), Timber, 6);
            Iso.Ball(torch, 0.16f, new Vector3(0, 1.75f, 0), Iso.Glow(new Color("ff9a40"), 5f), new Vector3(1, 1.5f, 1));
            var l = Iso.Light(torch, new Vector3(0, 1.9f, 0.2f), new Color("ff9a50"), 1.8f, 6f, shadows: true);
            _flicker.Add((l, 1.8f, s * 5));
        }
        // Mine cart rails into the cave.
        foreach (var s in new[] { -0.45f, 0.45f }) Iso.Box(_world, new Vector3(0.08f, 0.08f, 8), new Vector3(s, 0.1f, -9), Iso.Solid(new Color("6a6a70"), 0.4f, 0.8f));
    }

    private void River()
    {
        var water = Iso.Translucent(new Color(0.18f, 0.35f, 0.55f, 0.85f), 0.25f);
        Iso.Box(_world, new Vector3(3.2f, 0.1f, 60), new Vector3(17, 0.02f, 0), water);
        Iso.Box(_world, new Vector3(0.6f, 0.2f, 60), new Vector3(15.3f, 0.05f, 0), Rubble);
        Iso.Box(_world, new Vector3(0.6f, 0.2f, 60), new Vector3(18.7f, 0.05f, 0), Rubble);
        var bridge = new Node3D { Position = new Vector3(17, 0.25f, 8) };
        _world.AddChild(bridge);
        Iso.Box(bridge, new Vector3(4.2f, 0.15f, 1.6f), Vector3.Zero, Timber);
        foreach (var s in new[] { -0.75f, 0.75f }) Iso.Box(bridge, new Vector3(4.2f, 0.1f, 0.08f), new Vector3(0, 0.5f, s), Timber);
    }

    private void Trees()
    {
        var trunk = Iso.Wood(new Color("5a3d24"), new Color("2e1f12"), "tree-trunk");
        var leaves = Iso.Rock(new Color("2f5a38"), new Color("16301c"), 0.5f, "tree-leaves");
        var pine = Iso.Rock(new Color("24483a"), new Color("0f2419"), 0.5f, "tree-pine");
        for (var i = 0; i < 70; i++)
        {
            var x = Iso.Hash(i, 0, 31) * 56 - 28;
            var z = Iso.Hash(i, 1, 31) * 36 - 18;
            if (z < -12) continue;
            if (Math.Abs(x) < 13 && z > -11 && z < 10) continue;
            if (x > 14 && x < 20) continue;
            if (new Vector3(x, 0, z).DistanceTo(LumberLot) < 3.2f || new Vector3(x, 0, z).DistanceTo(TavernLot) < 3.8f) continue;
            var h = 1.5f + Iso.Hash(i, 2, 31) * 1.8f;
            var n = new Node3D { Position = new Vector3(x, 0, z) };
            _world.AddChild(n);
            Iso.Cylinder(n, 0.12f, 0.18f, h * 0.5f, new Vector3(0, h * 0.25f, 0), trunk, 6);
            if (Iso.Hash(i, 3, 31) > 0.45f)
            {
                for (var k = 0; k < 3; k++) Iso.Cylinder(n, 0, 1.0f - k * 0.25f, h * 0.55f, new Vector3(0, h * 0.5f + k * h * 0.3f, 0), pine, 7);
            }
            else
            {
                Iso.Ball(n, 0.9f + Iso.Hash(i, 4, 31) * 0.4f, new Vector3(0, h * 0.8f, 0), leaves, new Vector3(1, 0.9f, 1));
            }
        }
    }

    // ----- Buildings -----

    private Node3D Lot(Vector3 at, float yaw = 0)
    {
        var n = new Node3D { Position = at, RotationDegrees = new Vector3(0, yaw, 0) };
        _lots.AddChild(n);
        return n;
    }

    private static void Windows(Node3D parent, Vector3 size, float y, int count, float zFace)
    {
        for (var i = 0; i < count; i++)
        {
            var x = -size.X / 2 + size.X * (i + 0.5f) / count;
            Iso.Box(parent, new Vector3(0.32f, 0.42f, 0.04f), new Vector3(x, y, zFace), WindowGlow);
        }
    }

    private void Chimney(Node3D parent, Vector3 top, float amount = 1)
    {
        Iso.Box(parent, new Vector3(0.45f, 1.2f, 0.45f), top - new Vector3(0, 0.6f, 0), StoneWall);
        var smoke = Iso.Translucent(new Color(0.55f, 0.55f, 0.6f, 0.25f));
        for (var i = 0; i < (int)(5 * amount); i++)
        {
            var puff = Iso.Ball(_lots, 0.25f, Vector3.Zero, smoke);
            _smoke.Add((puff, parent.ToGlobal(top), i / (5f * amount)));
        }
    }

    private void Ruin(Node3D lot, Vector3 size, int salt)
    {
        for (var i = 0; i < 4; i++)
        {
            var side = i % 2 == 0 ? size.X : size.Z;
            var h = 0.3f + Iso.Hash(i, salt, 50) * size.Y * 0.6f;
            var len = side * (0.4f + Iso.Hash(i, salt, 51) * 0.5f);
            var pos = i switch
            {
                0 => new Vector3(-size.X / 2 + len / 2, h / 2, size.Z / 2),
                1 => new Vector3(size.X / 2, h / 2, size.Z / 2 - len / 2),
                2 => new Vector3(size.X / 2 - len / 2, h / 2, -size.Z / 2),
                _ => new Vector3(-size.X / 2, h / 2, -size.Z / 2 + len / 2),
            };
            var dims = i % 2 == 0 ? new Vector3(len, h, 0.3f) : new Vector3(0.3f, h, len);
            Iso.Box(lot, dims, pos, Rubble);
        }
        for (var i = 0; i < 6; i++)
        {
            var s = 0.2f + Iso.Hash(i, salt, 52) * 0.35f;
            Iso.Box(lot, new Vector3(s, s * 0.7f, s), new Vector3(Iso.Hash(i, salt, 53) * size.X - size.X / 2, s * 0.3f, Iso.Hash(i, salt, 54) * size.Z - size.Z / 2), Rubble, new Vector3(0, Iso.Hash(i, salt, 55) * 90, Iso.Hash(i, salt, 56) * 30));
        }
        Iso.Box(lot, new Vector3(0.1f, 2.2f, 0.1f), new Vector3(size.X / 2 - 0.3f, 1.1f, size.Z / 2 + 0.3f), Timber);
        Iso.Box(lot, new Vector3(0.1f, 0.1f, 1.2f), new Vector3(size.X / 2 - 0.3f, 2.0f, size.Z / 2 - 0.2f), Timber, new Vector3(20, 0, 0));
    }

    private void Library(Vector3 at)
    {
        var n = new Node3D { Position = at };
        _world.AddChild(n);
        var size = new Vector3(4.2f, 2.8f, 3.2f);
        Iso.Box(n, size, new Vector3(0, size.Y / 2, 0), StoneWall);
        Iso.Roof(n, new Vector3(4.6f, 1.6f, 3.6f), new Vector3(0, size.Y + 0.8f, 0), RoofSlate);
        var rune = Iso.Glow(new Color("5dd3e8"), 3f);
        var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.35f, OuterRadius = 0.5f, Rings = 16, RingSegments = 4 }, Position = new Vector3(0, 2.1f, 1.62f), RotationDegrees = new Vector3(90, 0, 0), MaterialOverride = rune };
        n.AddChild(ring);
        Iso.Box(n, new Vector3(0.9f, 1.4f, 0.06f), new Vector3(0, 0.7f, 1.62f), Timber);
        Windows(n, size, 1.3f, 2, 1.62f);
        foreach (var s in new[] { -1.8f, 1.8f }) Iso.Box(n, new Vector3(0.4f, 3f, 0.4f), new Vector3(s, 1.5f, 1.5f), StoneWall);
        var l = Iso.Light(n, new Vector3(0, 2.2f, 2.4f), new Color("7ad8ff"), 1.4f, 5f);
        _flicker.Add((l, 1.4f, 3));
    }

    private void Forge(Vector3 at, int level)
    {
        var lot = Lot(at);
        var size = new Vector3(4, 2.4f, 3);
        if (level == 0)
        {
            Ruin(lot, size, 1);
            return;
        }
        Iso.Box(lot, size, new Vector3(0, size.Y / 2, 0), StoneWall);
        Iso.Roof(lot, new Vector3(4.4f, 1.3f, 3.4f), new Vector3(0, size.Y + 0.65f, 0), RoofSlate);
        Iso.Box(lot, new Vector3(1.8f, 1.4f, 0.06f), new Vector3(-0.6f, 0.7f, 1.52f), Iso.Glow(new Color("ff7a2a"), 2.5f));
        Chimney(lot, new Vector3(1.3f, size.Y + 1.8f, -0.6f), 1.2f);
        var fire = Iso.Light(lot, new Vector3(-0.6f, 1, 2.2f), new Color("ff7a30"), 2.4f, 6f, shadows: true);
        _flicker.Add((fire, 2.4f, 7));
        var anvil = new Node3D { Position = new Vector3(1.2f, 0, 2.2f) };
        lot.AddChild(anvil);
        var iron = Iso.Solid(new Color("3a3a40"), 0.4f, 0.9f);
        Iso.Box(anvil, new Vector3(0.3f, 0.4f, 0.3f), new Vector3(0, 0.2f, 0), iron);
        Iso.Box(anvil, new Vector3(0.7f, 0.18f, 0.32f), new Vector3(0, 0.48f, 0), iron);
        if (level >= 2)
        {
            Iso.Box(lot, new Vector3(2.2f, 1.6f, 2.2f), new Vector3(-2.9f, 0.8f, 0), StoneWall);
            Iso.Roof(lot, new Vector3(2.4f, 0.9f, 2.4f), new Vector3(-2.9f, 2.05f, 0), RoofRed);
            Chimney(lot, new Vector3(-2.9f, 3.6f, 0), 1.5f);
            Iso.Box(lot, new Vector3(0.8f, 0.8f, 0.06f), new Vector3(-2.9f, 0.6f, 1.12f), Iso.Glow(new Color("ffb040"), 4f));
        }
    }

    private void Tower(Vector3 at, int level)
    {
        var lot = Lot(at);
        if (level == 0)
        {
            Ruin(lot, new Vector3(3, 3.5f, 3), 2);
            Iso.Cylinder(lot, 1.2f, 1.3f, 1.4f, new Vector3(0, 0.7f, 0), Rubble, 10);
            return;
        }
        Iso.Cylinder(lot, 1.3f, 1.5f, 3.2f, new Vector3(0, 1.6f, 0), StoneWall, 12);
        Iso.Cylinder(lot, 1.1f, 1.25f, 2.6f, new Vector3(0, 4.5f, 0), StoneWall, 12);
        Iso.Cylinder(lot, 0, 1.5f, 2.2f, new Vector3(0, 6.9f, 0), Iso.Solid(new Color("4b3a6e"), 0.6f), 12);
        var violet = Iso.Glow(new Color("c792ea"), 3f);
        for (var i = 0; i < 4; i++)
        {
            var a = Mathf.Tau * i / 4 + 0.4f;
            Iso.Box(lot, new Vector3(0.3f, 0.5f, 0.06f), new Vector3(Mathf.Sin(a) * 1.28f, 2.2f + (i % 2) * 2.4f, Mathf.Cos(a) * 1.28f), violet, new Vector3(0, Mathf.RadToDeg(a), 0));
        }
        var crystal = new Node3D { Position = new Vector3(0, 8.6f, 0) };
        lot.AddChild(crystal);
        Iso.Box(crystal, new Vector3(0.35f, 0.6f, 0.35f), Vector3.Zero, Iso.Glow(new Color("e0b0ff"), 5f), new Vector3(0, 0, 45));
        _spinners.Add(crystal);
        var l = Iso.Light(lot, new Vector3(0, 8.4f, 0), new Color("c792ea"), 2.5f, 9f);
        _flicker.Add((l, 2.5f, 11));
        if (level >= 2)
        {
            Iso.Ball(lot, 1.0f, new Vector3(2.2f, 3.3f, 0), Iso.Solid(new Color("6a7a8a"), 0.3f, 0.7f), new Vector3(1, 0.8f, 1));
            Iso.Cylinder(lot, 1.1f, 1.1f, 2.6f, new Vector3(2.2f, 1.3f, 0), StoneWall, 10);
            var scope = Iso.Cylinder(lot, 0.12f, 0.2f, 1.6f, new Vector3(2.6f, 4f, 0.3f), Iso.Solid(new Color("c9a35a"), 0.3f, 0.9f), 8);
            scope.RotationDegrees = new Vector3(40, 0, -30);
        }
    }

    private void Guild(Vector3 at, int level)
    {
        var lot = Lot(at, -10);
        var size = new Vector3(5, 2.6f, 3.4f);
        if (level == 0)
        {
            Ruin(lot, size, 3);
            return;
        }
        Iso.Box(lot, size, new Vector3(0, size.Y / 2, 0), Plaster);
        foreach (var x in new[] { -2.4f, -0.8f, 0.8f, 2.4f }) Iso.Box(lot, new Vector3(0.18f, size.Y, 0.08f), new Vector3(x, size.Y / 2, 1.72f), Timber);
        Iso.Box(lot, new Vector3(size.X, 0.18f, 0.08f), new Vector3(0, size.Y - 0.1f, 1.72f), Timber);
        Windows(lot, size, 1.5f, 3, 1.73f);
        var top = size.Y;
        if (level >= 2)
        {
            Iso.Box(lot, new Vector3(4.6f, 2f, 3.2f), new Vector3(0, top + 1, 0), Plaster);
            Windows(lot, size, top + 1.1f, 4, 1.62f);
            top += 2;
        }
        Iso.Roof(lot, new Vector3(5.6f, 1.6f, 4), new Vector3(0, top + 0.8f, 0), RoofRed);
        Iso.Box(lot, new Vector3(0.08f, 2.6f, 0.08f), new Vector3(2.9f, 1.3f, 2.2f), Timber);
        Iso.Box(lot, new Vector3(0.7f, 1.1f, 0.03f), new Vector3(2.9f, 2f, 2.25f), Iso.Solid(new Color("8e2f3a")));
        Iso.Box(lot, new Vector3(0.24f, 0.24f, 0.02f), new Vector3(2.9f, 2.1f, 2.27f), Iso.Glow(new Color("f5b041"), 2f), new Vector3(0, 0, 45));
        Chimney(lot, new Vector3(-1.8f, top + 1.6f, -0.5f), 0.6f);
    }

    private void Market(Vector3 at, int level)
    {
        var lot = Lot(at);
        if (level == 0)
        {
            Ruin(lot, new Vector3(4, 1.4f, 2.4f), 4);
            return;
        }
        var colors = new[] { new Color("b8453a"), new Color("d9b44a"), new Color("3f7a8e"), new Color("7a4a8e") };
        var stalls = level >= 2 ? 4 : 2;
        for (var i = 0; i < stalls; i++)
        {
            var x = (i - (stalls - 1) / 2f) * 2.4f;
            var s = new Node3D { Position = new Vector3(x, 0, (i % 2) * 0.6f) };
            lot.AddChild(s);
            Iso.Box(s, new Vector3(1.8f, 0.8f, 0.9f), new Vector3(0, 0.4f, 0), Timber);
            foreach (var px in new[] { -0.85f, 0.85f }) Iso.Box(s, new Vector3(0.08f, 2f, 0.08f), new Vector3(px, 1f, -0.4f), Timber);
            var cloth = Iso.Box(s, new Vector3(2.1f, 0.06f, 1.4f), new Vector3(0, 2f, 0.1f), Iso.Solid(colors[i % colors.Length], 0.9f));
            cloth.RotationDegrees = new Vector3(-14, 0, 0);
            for (var k = 0; k < 3; k++) Iso.Ball(s, 0.14f, new Vector3(-0.5f + k * 0.5f, 0.92f, 0.1f), Iso.Solid(colors[(i + k + 1) % colors.Length]));
        }
        var lantern = Iso.Light(lot, new Vector3(0, 2.6f, 1.2f), new Color("ffc46b"), 1.2f, 5f);
        _flicker.Add((lantern, 1.2f, 13));
        if (level >= 2)
        {
            for (var i = 0; i < 9; i++) Iso.Ball(lot, 0.08f, new Vector3(-4.8f + i * 1.2f, 2.5f - Mathf.Sin(i / 8f * Mathf.Pi) * 0.3f, 1.3f), Iso.Glow(new Color("ffd27a"), 4f));
        }
    }

    private void Mill(Vector3 at, int level)
    {
        var lot = Lot(at);
        var size = new Vector3(3, 2.4f, 3);
        if (level == 0)
        {
            Ruin(lot, size, 5);
            return;
        }
        Iso.Box(lot, size, new Vector3(0, size.Y / 2, 0), Plaster);
        Iso.Roof(lot, new Vector3(3.4f, 1.4f, 3.4f), new Vector3(0, size.Y + 0.7f, 0), RoofRed, 90);
        Windows(lot, size, 1.4f, 2, 1.52f);
        var wheel = new Node3D { Position = new Vector3(1.9f, 1.3f, 0), RotationDegrees = new Vector3(0, 90, 0) };
        lot.AddChild(wheel);
        var spin = new Node3D();
        wheel.AddChild(spin);
        for (var i = 0; i < 8; i++)
        {
            var paddle = Iso.Box(spin, new Vector3(0.14f, 2.6f, 0.5f), Vector3.Zero, Timber);
            paddle.RotationDegrees = new Vector3(0, 0, i * 22.5f);
        }
        _wheels.Add(spin);
        if (level >= 2)
        {
            foreach (var z in new[] { -2.6f, -4.2f })
            {
                var cart = new Node3D { Position = new Vector3(-2.5f, 0, z) };
                lot.AddChild(cart);
                Iso.Box(cart, new Vector3(0.9f, 0.6f, 1.1f), new Vector3(0, 0.5f, 0), Timber);
                Iso.Box(cart, new Vector3(0.7f, 0.2f, 0.9f), new Vector3(0, 0.85f, 0), Iso.Rock(new Color("8a7a60"), new Color("3a3025"), 0.8f, "ore"));
                Iso.Ball(cart, 0.08f, new Vector3(0.1f, 0.95f, 0.1f), Iso.Glow(new Color("ffd24a"), 3f));
            }
        }
    }

    /// <summary>An empty plot: four stakes and a rope, waiting for a building.</summary>
    private static void Plot(Node3D lot, Vector3 size)
    {
        var rope = Iso.Solid(new Color("b8a07a"));
        foreach (var (x, z) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            Iso.Box(lot, new Vector3(0.1f, 0.7f, 0.1f), new Vector3(x * size.X / 2, 0.35f, z * size.Z / 2), Timber);
        }
        Iso.Box(lot, new Vector3(size.X, 0.03f, 0.03f), new Vector3(0, 0.6f, size.Z / 2), rope);
        Iso.Box(lot, new Vector3(size.X, 0.03f, 0.03f), new Vector3(0, 0.6f, -size.Z / 2), rope);
        Iso.Box(lot, new Vector3(0.03f, 0.03f, size.Z), new Vector3(size.X / 2, 0.6f, 0), rope);
        Iso.Box(lot, new Vector3(0.03f, 0.03f, size.Z), new Vector3(-size.X / 2, 0.6f, 0), rope);
    }

    private void Lumber(Vector3 at, int level)
    {
        var lot = Lot(at, 20);
        if (level == 0)
        {
            Plot(lot, new Vector3(3, 0, 2.6f));
            return;
        }
        var logs = Iso.Wood(new Color("7a5534"), new Color("3e2a18"), "town-logs");
        Iso.Box(lot, new Vector3(2.6f, 1.7f, 2.2f), new Vector3(0, 0.85f, 0), logs);
        Iso.Roof(lot, new Vector3(3f, 1.1f, 2.6f), new Vector3(0, 2.25f, 0), RoofSlate);
        Iso.Box(lot, new Vector3(0.6f, 1.1f, 0.05f), new Vector3(0.5f, 0.55f, 1.12f), Timber);
        Windows(lot, new Vector3(1.2f, 0, 0), 1.1f, 1, 1.12f);
        for (var i = 0; i < 6; i++)
        {
            var log = Iso.Cylinder(lot, 0.2f, 0.2f, 1.6f, new Vector3(-2.1f + (i % 3) * 0.42f + (i / 3) * 0.21f, 0.2f + (i / 3) * 0.36f, 0.4f), logs, 8);
            log.RotationDegrees = new Vector3(90, 0, 0);
        }
        Iso.Cylinder(lot, 0.35f, 0.4f, 0.5f, new Vector3(1.8f, 0.25f, 1.3f), logs, 10);
        Iso.Box(lot, new Vector3(0.06f, 0.7f, 0.06f), new Vector3(1.8f, 0.75f, 1.3f), Timber, new Vector3(0, 0, 25));
        Iso.Box(lot, new Vector3(0.3f, 0.16f, 0.05f), new Vector3(1.95f, 1.05f, 1.3f), Iso.Solid(new Color("9a9aa0"), 0.4f, 0.8f), new Vector3(0, 0, 25));
    }

    private void Quarry(Vector3 at, int level)
    {
        var lot = Lot(at, 10);
        if (level == 0)
        {
            Plot(lot, new Vector3(3.2f, 0, 2.6f));
            return;
        }
        var cut = Iso.Flagstone(new Color("a39a8c"), new Color("5a534a"), 0.9f, "town-quarry");
        for (var i = 0; i < 3; i++) Iso.Box(lot, new Vector3(3.4f - i * 0.9f, 0.6f, 1.2f), new Vector3(0, 0.3f + i * 0.6f, -0.9f - i * 0.2f), cut);
        foreach (var (x, z, r) in new[] { (-1.3f, 0.9f, 10f), (-0.5f, 1.2f, -8f), (0.4f, 0.8f, 25f) })
        {
            Iso.Box(lot, new Vector3(0.6f, 0.45f, 0.45f), new Vector3(x, 0.22f, z), cut, new Vector3(0, r, 0));
        }
        Iso.Box(lot, new Vector3(0.12f, 2.6f, 0.12f), new Vector3(1.6f, 1.3f, 0.6f), Timber);
        var arm = Iso.Box(lot, new Vector3(1.8f, 0.1f, 0.1f), new Vector3(1.0f, 2.5f, 0.6f), Timber);
        arm.RotationDegrees = new Vector3(0, 0, -8);
        Iso.Box(lot, new Vector3(0.02f, 1f, 0.02f), new Vector3(0.2f, 2f, 0.6f), Iso.Solid(new Color("b8a07a")));
        Iso.Box(lot, new Vector3(0.45f, 0.35f, 0.35f), new Vector3(0.2f, 1.35f, 0.6f), cut);
    }

    private void Farm(Vector3 at, int level)
    {
        var lot = Lot(at, -5);
        var size = new Vector3(5, 0, 3);
        if (level == 0)
        {
            Plot(lot, size);
            return;
        }
        var soil = Iso.Rock(new Color("5a4030"), new Color("2e2018"), 0.6f, "town-soil");
        var wheat = Iso.Rock(new Color("d9b85a"), new Color("8a6a2a"), 0.5f, "town-wheat");
        Iso.Box(lot, new Vector3(size.X, 0.1f, size.Z), new Vector3(0, 0.05f, 0), soil);
        for (var r = 0; r < 3; r++) Iso.Box(lot, new Vector3(size.X - 0.4f, 0.55f, 0.32f), new Vector3(0, 0.35f, -size.Z / 2 + 0.35f + r * 0.58f), wheat);
        var pumpkin = Iso.Solid(new Color("e07a20"), 0.6f);
        var stem = Iso.Solid(new Color("4a6a2a"));
        for (var i = 0; i < 7; i++)
        {
            var at2 = new Vector3(-size.X / 2 + 0.5f + i * 0.62f, 0, size.Z / 2 - 0.9f + (i % 2) * 0.35f);
            var big = 0.2f + Iso.Hash(i, 0, 62) * 0.12f;
            Iso.Ball(lot, big, at2 + new Vector3(0, big * 0.8f, 0), pumpkin, new Vector3(1.2f, 0.85f, 1.2f));
            Iso.Box(lot, new Vector3(0.05f, 0.12f, 0.05f), at2 + new Vector3(0, big * 1.55f, 0), stem);
            Iso.Box(lot, new Vector3(0.3f, 0.03f, 0.18f), at2 + new Vector3(0.18f, 0.1f, 0.1f), stem, new Vector3(0, i * 40, 0));
        }
        if (level >= 2) Greenhouse(lot, new Vector3(-size.X / 2 - 2.2f, 0, 0));
        for (var i = 0; i <= 10; i++)
        {
            Iso.Box(lot, new Vector3(0.08f, 0.6f, 0.08f), new Vector3(-size.X / 2 + i * size.X / 10, 0.3f, size.Z / 2 + 0.2f), Timber);
        }
        Iso.Box(lot, new Vector3(size.X, 0.06f, 0.05f), new Vector3(0, 0.5f, size.Z / 2 + 0.2f), Timber);
        var crow = new Node3D { Position = new Vector3(1.6f, 0, 0) };
        lot.AddChild(crow);
        Iso.Box(crow, new Vector3(0.08f, 1.7f, 0.08f), new Vector3(0, 0.85f, 0), Timber);
        Iso.Box(crow, new Vector3(1.1f, 0.07f, 0.07f), new Vector3(0, 1.3f, 0), Timber);
        Iso.Ball(crow, 0.2f, new Vector3(0, 1.8f, 0), Iso.Solid(new Color("d9c08a")));
        Iso.Cylinder(crow, 0, 0.32f, 0.3f, new Vector3(0, 2.05f, 0), Iso.Solid(new Color("6a4a2a")), 8);
    }

    /// <summary>Crystal glass panes over rows of seedlings, lit from inside.</summary>
    private void Greenhouse(Node3D lot, Vector3 at)
    {
        var n = new Node3D { Position = at };
        lot.AddChild(n);
        var size = new Vector3(2.6f, 1.6f, 2.8f);
        var glass = Iso.Translucent(new Color(0.6f, 0.85f, 1f, 0.28f), 0.4f);
        var frame = Iso.Solid(new Color("d8d8d0"), 0.4f, 0.6f);
        Iso.Box(n, new Vector3(size.X, 0.25f, size.Z), new Vector3(0, 0.12f, 0), StoneWall);
        Iso.Box(n, size, new Vector3(0, size.Y / 2 + 0.25f, 0), glass);
        Iso.Roof(n, new Vector3(size.X + 0.1f, 0.9f, size.Z + 0.1f), new Vector3(0, size.Y + 0.7f, 0), glass, 90);
        foreach (var x in new[] { -1f, 0, 1f })
        {
            foreach (var z in new[] { -1f, 1f }) Iso.Box(n, new Vector3(0.06f, size.Y, 0.06f), new Vector3(x * size.X / 2, size.Y / 2 + 0.25f, z * size.Z / 2), frame);
        }
        var sprout = Iso.Glow(new Color("7bd88f"), 0.9f);
        for (var i = 0; i < 8; i++) Iso.Box(n, new Vector3(0.14f, 0.3f, 0.14f), new Vector3(-0.8f + (i % 4) * 0.55f, 0.45f, -0.6f + (i / 4) * 1.2f), sprout, new Vector3(0, i * 30, 0));
        var l = Iso.Light(n, new Vector3(0, 1.2f, 0), new Color("b0f0c0"), 1.2f, 4.5f);
        _flicker.Add((l, 1.2f, 29));
    }

    private void Tavern(Vector3 at, int level)
    {
        var lot = Lot(at, 20);
        var size = new Vector3(3.6f, 2.2f, 2.8f);
        if (level == 0)
        {
            Plot(lot, size);
            return;
        }
        Iso.Box(lot, new Vector3(size.X, 1.1f, size.Z), new Vector3(0, 0.55f, 0), StoneWall);
        Iso.Box(lot, new Vector3(size.X + 0.2f, 1.3f, size.Z + 0.2f), new Vector3(0, 1.75f, 0), Plaster);
        foreach (var x in new[] { -1.8f, -0.6f, 0.6f, 1.8f }) Iso.Box(lot, new Vector3(0.14f, 1.3f, 0.06f), new Vector3(x, 1.75f, size.Z / 2 + 0.13f), Timber);
        Iso.Roof(lot, new Vector3(size.X + 0.6f, 1.4f, size.Z + 0.6f), new Vector3(0, 3.1f, 0), RoofRed);
        Iso.Box(lot, new Vector3(0.7f, 1.0f, 0.05f), new Vector3(0.9f, 0.5f, size.Z / 2 + 0.01f), Timber);
        Windows(lot, new Vector3(1.6f, 0, 0), 0.65f, 2, size.Z / 2 + 0.02f);
        Windows(lot, new Vector3(3f, 0, 0), 1.8f, 3, size.Z / 2 + 0.14f);
        Chimney(lot, new Vector3(-1.2f, 4.2f, -0.6f), 0.8f);
        var sign = new Node3D { Position = new Vector3(-1.2f, 2.0f, size.Z / 2 + 0.5f) };
        lot.AddChild(sign);
        Iso.Box(sign, new Vector3(0.06f, 0.06f, 0.7f), new Vector3(0, 0.3f, -0.25f), Timber);
        Iso.Box(sign, new Vector3(0.05f, 0.5f, 0.6f), new Vector3(0, 0, 0), Iso.Solid(new Color("5a3a22")));
        Iso.Ball(sign, 0.16f, new Vector3(0.04f, 0, 0), Iso.Solid(new Color("e07a20")), new Vector3(0.4f, 0.8f, 1));
        for (var i = 0; i < 3; i++)
        {
            var bench = new Node3D { Position = new Vector3(-1.6f + i * 1.3f, 0, size.Z / 2 + 1.1f) };
            lot.AddChild(bench);
            Iso.Box(bench, new Vector3(0.9f, 0.08f, 0.5f), new Vector3(0, 0.5f, 0), Timber);
            Iso.Box(bench, new Vector3(0.1f, 0.5f, 0.1f), new Vector3(0, 0.25f, 0), Timber);
            Iso.Cylinder(bench, 0.07f, 0.06f, 0.16f, new Vector3(0.2f, 0.62f, 0), Iso.Solid(new Color("c9a35a"), 0.3f, 0.6f), 6);
        }
        foreach (var x in new[] { -2.2f, 2.2f }) Iso.Cylinder(lot, 0.3f, 0.34f, 0.7f, new Vector3(x, 0.35f, size.Z / 2 + 0.3f), Iso.Wood(new Color("8a5a30"), new Color("43291a"), "town-barrel"), 10);
        var lanterns = Iso.Glow(new Color("ffd27a"), 4f);
        for (var i = 0; i < 6; i++) Iso.Ball(lot, 0.07f, new Vector3(-2 + i * 0.8f, 2.4f - Mathf.Sin(i / 5f * Mathf.Pi) * 0.25f, size.Z / 2 + 1.6f), lanterns);
        var l = Iso.Light(lot, new Vector3(0, 1.6f, size.Z / 2 + 1.4f), new Color("ffb060"), 1.8f, 6f);
        _flicker.Add((l, 1.8f, 31));
    }

    private void Smelter(Vector3 at, int level)
    {
        var lot = Lot(at, -15);
        if (level == 0)
        {
            Ruin(lot, new Vector3(3, 2.2f, 2.6f), 20);
            return;
        }
        var brick = Iso.Flagstone(new Color("8a5a44"), new Color("4a2e22"), 0.9f, "town-brick");
        Iso.Cylinder(lot, 0.9f, 1.4f, 2.6f, new Vector3(0, 1.3f, 0), brick, 10);
        Iso.Cylinder(lot, 0.45f, 0.6f, 1.6f, new Vector3(0, 3.4f, 0), brick, 8);
        Iso.Box(lot, new Vector3(0.9f, 0.8f, 0.1f), new Vector3(0, 0.6f, 1.25f), Iso.Glow(new Color("ff7a2a"), 3.5f));
        Chimney(lot, new Vector3(0, 4.5f, 0), 1.3f);
        var fire = Iso.Light(lot, new Vector3(0, 0.8f, 2f), new Color("ff8030"), 2.6f, 6f, shadows: true);
        _flicker.Add((fire, 2.6f, 17));
        var ore = Iso.Rock(new Color("9a5a3a"), new Color("4a2a1a"), 0.8f, "town-ore");
        for (var i = 0; i < 5; i++) Iso.Ball(lot, 0.28f, new Vector3(-1.9f + (i % 3) * 0.35f, 0.2f + (i / 3) * 0.3f, 0.6f + (i % 2) * 0.3f), ore);
        var iron = Iso.Solid(new Color("9fb0c0"), 0.35f, 0.9f);
        for (var i = 0; i < 4; i++) Iso.Box(lot, new Vector3(0.7f, 0.14f, 0.26f), new Vector3(1.9f, 0.08f + (i / 2) * 0.15f, 0.6f + (i % 2) * 0.3f), iron);
    }

    private void Bakery(Vector3 at, int level)
    {
        var lot = Lot(at, -20);
        var size = new Vector3(3, 2.2f, 2.6f);
        if (level == 0)
        {
            Ruin(lot, size, 21);
            return;
        }
        Iso.Box(lot, size, new Vector3(0, size.Y / 2, 0), Plaster);
        Iso.Roof(lot, new Vector3(3.4f, 1.2f, 3f), new Vector3(0, size.Y + 0.6f, 0), RoofRed);
        Iso.Box(lot, new Vector3(0.6f, 1.1f, 0.05f), new Vector3(-0.7f, 0.55f, 1.32f), Timber);
        Windows(lot, new Vector3(1.2f, 0, 0), 1.3f, 1, 1.32f);
        Iso.Ball(lot, 0.95f, new Vector3(2.1f, 0.3f, 0), StoneWall, new Vector3(1, 0.85f, 1));
        Iso.Box(lot, new Vector3(0.1f, 0.45f, 0.5f), new Vector3(3.0f, 0.4f, 0), Iso.Glow(new Color("ff9a40"), 3.5f));
        Chimney(lot, new Vector3(0.9f, size.Y + 1.5f, -0.5f), 0.9f);
        var glow = Iso.Light(lot, new Vector3(3.4f, 0.8f, 0), new Color("ffa050"), 1.6f, 5f);
        _flicker.Add((glow, 1.6f, 19));
        Iso.Box(lot, new Vector3(0.08f, 0.08f, 0.8f), new Vector3(1.55f, 2f, 1.5f), Timber);
        Iso.Box(lot, new Vector3(0.05f, 0.5f, 0.6f), new Vector3(1.6f, 1.7f, 1.8f), Iso.Solid(new Color("5a3a22")));
        Iso.Ball(lot, 0.18f, new Vector3(1.65f, 1.7f, 1.8f), Iso.Solid(new Color("e0a86a")), new Vector3(0.5f, 0.8f, 1.4f));
    }

    private void Cottages(int level)
    {
        var spots = new[] { (new Vector3(-6, 0, 7), 15f), (new Vector3(-11, 0, 5), -10f), (new Vector3(-3.5f, 0, 11), 5f) };
        for (var i = 0; i < spots.Length; i++)
        {
            var (at, yaw) = spots[i];
            var lot = Lot(at, yaw);
            var size = new Vector3(2.6f, 1.8f, 2.2f);
            if (level == 0)
            {
                Ruin(lot, size, 10 + i);
                continue;
            }
            Iso.Box(lot, size, new Vector3(0, size.Y / 2, 0), Plaster);
            Iso.Roof(lot, new Vector3(3, 1.3f, 2.6f), new Vector3(0, size.Y + 0.65f, 0), i == 1 ? RoofSlate : RoofRed);
            Iso.Box(lot, new Vector3(0.55f, 1.1f, 0.05f), new Vector3(-0.6f, 0.55f, 1.12f), Timber);
            Windows(lot, new Vector3(1.2f, 0, 0), 1.1f, 1, 1.12f);
            Chimney(lot, new Vector3(0.8f, size.Y + 1.4f, -0.4f), 0.5f);
        }
    }
}
