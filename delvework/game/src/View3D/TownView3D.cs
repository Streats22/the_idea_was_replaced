using Godot;

namespace Delvework.Game.View3D;

public sealed record TownSign(string Id, string Title, string Subtitle, bool Enabled, bool Highlight);

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

/// <summary>Hollowmere diorama: day cycle, Kenney houses, hanging signs.</summary>
public partial class TownView3D : Control
{
    public const float DayLength = 300f;

    private const float BoardHalfWidth = 26f, BoardFront = 16f, BoardBack = -18f;

    private readonly record struct Sky(float At, Color Background, Color Ambient, float AmbientEnergy, Color Sun, float SunEnergy, float SunPitch, float Lamps);

    private static readonly Sky[] Skies =
    [
        new(0.00f, new Color("b69ea6"), new Color("e8d0cc"), 0.6f, new Color("ffc098"), 0.8f, -20, 0.4f),
        new(0.08f, new Color("8fa9bc"), new Color("dde6ee"), 0.85f, new Color("fff3de"), 1.35f, -55, 0f),
        new(0.55f, new Color("93a8b8"), new Color("e2ddd2"), 0.8f, new Color("ffe9c8"), 1.25f, -45, 0.05f),
        new(0.66f, new Color("8a6f86"), new Color("d8a890"), 0.62f, new Color("ff9a60"), 0.85f, -18, 0.6f),
        new(0.76f, new Color("27314a"), new Color("7d8fbd"), 0.55f, new Color("a8b8e8"), 0.45f, -55, 1f),
        new(0.94f, new Color("27314a"), new Color("7d8fbd"), 0.55f, new Color("a8b8e8"), 0.45f, -55, 1f),
        new(1.00f, new Color("b69ea6"), new Color("e8d0cc"), 0.6f, new Color("ffc098"), 0.8f, -20, 0.4f),
    ];

    private sealed record Stroller(Node3D Root, Node3D Body, Node3D? LeftArm, Node3D? RightArm, AnimationPlayer? Anim, Vector3[] Loop, float Speed, float Offset, bool NightOwl);

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
        ["commissions"] = new(-3.4f, 3f, 3.2f),
    };

    private static readonly Vector3 LumberLot = new(-13.5f, 0, 1.5f), QuarryLot = new(-5, 0, -10.5f), FarmLot = new(6, 0, 10.5f),
        SmelterLot = new(5.5f, 0, -9.5f), BakeryLot = new(12.5f, 0, 4.5f), TavernLot = new(-14f, 0, -3.5f);

    private static readonly StandardMaterial3D WindowGlow = new()
    {
        AlbedoColor = new Color("3a2c20"),
        Roughness = 1f,
        EmissionEnabled = true,
        Emission = new Color("ffc46b"),
        EmissionEnergyMultiplier = 0f,
    };

    private static readonly string[] Folk = ["character-male-a", "character-female-a", "character-male-b", "character-female-b", "character-male-c", "character-female-c"];

    private SubViewport _viewport = null!;
    private Camera3D _camera = null!;
    private Node3D _world = null!, _lots = null!;
    private Control _signLayer = null!;
    private readonly Dictionary<string, HangingSign> _signs = [];
    private readonly List<Node3D> _wheels = [];
    private readonly List<(Node3D Node, Vector3 Base, float Phase)> _smoke = [];
    private readonly List<(Node3D Node, Vector3 Base, float Phase)> _fireflies = [];
    private readonly List<Node3D> _spinners = [];
    private readonly List<OmniSpot> _lampSpots = [];
    private OmniLightPool _lamps = null!;
    private int _sceneryLamps;
    private readonly List<Stroller> _walkers = [];
    private DirectionalLight3D _sun = null!;
    private Godot.Environment _env = null!;
    private double _time;
    private string _builtKey = "";
    private float _yaw = 32f, _pitch = 42f, _distance = 46f;
    private float _shownYaw = 32f, _shownPitch = 42f, _shownDistance = 46f;
    private bool _orbiting;

    public float TimeOfDay { get; set; } = 0.3f;
    public bool Frozen { get; set; }
    public bool Interactive { get; set; } = true;

    public event Action<string>? Picked;

    public override void _Ready()
    {
        ClipContents = true;
        var sky = Skies[1];
        var scene = Iso.Mount(this, sky.Background, sky.Ambient, sky.AmbientEnergy, sky.Sun, sky.SunEnergy, new Vector3(-50, -35, 0), 75f, 0.5f, MouseFilterEnum.Ignore);
        _viewport = scene.Viewport;
        _camera = scene.Camera;
        _sun = scene.Sun;
        _env = scene.Env;
        _world = new Node3D();
        _lots = new Node3D();
        _viewport.AddChild(_world);
        _viewport.AddChild(_lots);
        _lamps = new OmniLightPool(_viewport, new Color("ffb86b"), 1.2f, 5f);
        BuildScenery();
        _sceneryLamps = _lampSpots.Count;

        _signLayer = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _signLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_signLayer);
        Graphics.Changed += OnGraphicsChanged;
    }

    public override void _ExitTree() => Graphics.Changed -= OnGraphicsChanged;

    private void OnGraphicsChanged() => Graphics.Apply(_sun, _env, _viewport);

    private void RegisterLamp(Vector3 worldPos, Color color, float energy, float range, float phase) =>
        _lampSpots.Add(new OmniSpot(worldPos, color, energy, range, phase));

    public Camera3D Camera => _camera;

    public void Refresh(IReadOnlyDictionary<string, int> buildings, IReadOnlyList<string> party)
    {
        var key = string.Join(",", buildings.OrderBy(b => b.Key, StringComparer.Ordinal).Select(b => $"{b.Key}{b.Value}")) + "|" + string.Join(",", party);
        if (key == _builtKey) return;
        _builtKey = key;
        foreach (var (node, _, _) in _smoke)
        {
            if (IsInstanceValid(node)) node.QueueFree();
        }
        _smoke.Clear();
        foreach (var n in _lots.GetChildren()) n.QueueFree();
        _wheels.Clear();
        _spinners.Clear();
        _walkers.Clear();
        if (_lampSpots.Count > _sceneryLamps) _lampSpots.RemoveRange(_sceneryLamps, _lampSpots.Count - _sceneryLamps);
        int Level(string building) => buildings.GetValueOrDefault(building);

        Forge(new Vector3(7, 0, -4), Level("forge"));
        Tower(new Vector3(-10, 0, -8), Level("tower"));
        Guild(new Vector3(8, 0, 5), Level("guild"));
        Market(new Vector3(0, 0, 6), Level("market"));
        Mill(new Vector3(14, 0, -1), Level("mill"));
        Cottages(Level("houses"));
        Lumber(LumberLot, Level("lumber"));
        Quarry(QuarryLot, Level("quarry"));
        Farm(FarmLot, Level("farm"));
        Smelter(SmelterLot, Level("smelter"));
        Bakery(BakeryLot, Level("bakery"));
        Tavern(TavernLot, Level("tavern"));

        for (var i = 0; i < party.Count; i++)
        {
            var fig = Figures.Golem(party[i]);
            _lots.AddChild(fig.Root);
            _walkers.Add(new Stroller(fig.Root, fig.Body, fig.LeftArm, fig.RightArm, null, GolemLoop, 0.9f, i / (float)Math.Max(1, party.Count), true));
        }
        var folk = Math.Min(VillagerLoops.Length * 2, 1 + Level("mill") + Level("market") * 2 + Level("houses") * 3 + Level("tavern") * 2 + Level("bakery") + Level("farm"));
        for (var i = 0; i < folk; i++) Villager(i);
    }

    private static readonly Vector3[] GolemLoop =
    [
        new(-3, 0, 1.5f), new(3, 0, 1.5f), new(4, 0, -3), new(0, 0, -7), new(-4, 0, -3),
    ];

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
        new("b8453a"), new("3f7a8e"), new("d9b44a"), new("5a8a3a"), new("7a4a8e"), new("c8783a"), new("4a5a9e"), new("b0b0b0"),
    ];

    private void Villager(int i)
    {
        var root = new Node3D();
        _lots.AddChild(root);
        var body = new Node3D();
        root.AddChild(body);
        AnimationPlayer? anim = null;
        Node3D? left = null, right = null;
        if (Kenney.Spawn(body, Kenney.Characters, Folk[i % Folk.Length], Vector3.Zero, 0, 1.25f) is { } model)
        {
            anim = Kenney.Animations(model);
        }
        else
        {
            var cloth = Iso.Flat(Clothes[i % Clothes.Length]);
            var skin = Iso.Flat(new[] { new Color("e0b890"), new Color("b88a60"), new Color("8a6040") }[i % 3]);
            Iso.Cylinder(body, 0.18f, 0.3f, 0.75f, new Vector3(0, 0.38f, 0), cloth, 8);
            Iso.Ball(body, 0.17f, new Vector3(0, 0.92f, 0), skin);
            if (i % 3 == 0) Iso.Cylinder(body, 0.08f, 0.26f, 0.2f, new Vector3(0, 1.1f, 0), Iso.Flat(new Color("5a3a22")), 8);
            var arms = new Node3D[2];
            for (var s = 0; s < 2; s++)
            {
                arms[s] = new Node3D { Position = new Vector3(s == 0 ? -0.22f : 0.22f, 0.65f, 0) };
                body.AddChild(arms[s]);
                Iso.Box(arms[s], new Vector3(0.08f, 0.4f, 0.08f), new Vector3(0, -0.2f, 0), cloth);
            }
            (left, right) = (arms[0], arms[1]);
        }
        var loop = VillagerLoops[i % VillagerLoops.Length];
        _walkers.Add(new Stroller(root, body, left, right, anim, i / VillagerLoops.Length % 2 == 0 ? loop : [.. loop.Reverse()],
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

    public override void _GuiInput(InputEvent e)
    {
        if (!Interactive) return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _distance = Mathf.Clamp(_distance * 0.9f, 22f, 64f);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _distance = Mathf.Clamp(_distance / 0.9f, 22f, 64f);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right or MouseButton.Middle } b:
                _orbiting = b.Pressed;
                AcceptEvent();
                break;
            case InputEventMouseMotion m when _orbiting:
                _yaw -= m.Relative.X * 0.25f;
                _pitch = Mathf.Clamp(_pitch + m.Relative.Y * 0.15f, 25f, 70f);
                AcceptEvent();
                break;
        }
    }

    public override void _Process(double delta)
    {
        _time += delta;
        var t = (float)_time;
        if (!Frozen) TimeOfDay = (TimeOfDay + (float)delta / DayLength) % 1f;
        var night = UpdateSky();
        if (!Interactive) _yaw = 32f + Mathf.Sin(t * 0.04f) * 16f;
        var k = (float)Math.Min(1, delta * 6);
        _shownYaw = Mathf.Lerp(_shownYaw, _yaw, k);
        _shownPitch = Mathf.Lerp(_shownPitch, _pitch, k);
        _shownDistance = Mathf.Lerp(_shownDistance, _distance, k);
        var sway = _orbiting ? Vector3.Zero : new Vector3(Mathf.Sin(t * 0.07f) * 0.6f, 0, Mathf.Cos(t * 0.05f) * 0.4f);
        Iso.Aim(_camera, new Vector3(0, 0, -1.5f) + sway, _shownDistance, _shownYaw, _shownPitch);
        foreach (var w in _wheels) w.Rotation = new Vector3(0, 0, -t * 0.9f);
        foreach (var s in _spinners) s.Rotation = new Vector3(0, t * 0.8f, 0);
        foreach (var (node, b, phase) in _smoke)
        {
            var f = (t * 0.25f + phase) % 1f;
            node.Position = b + new Vector3(Mathf.Sin(f * 5 + phase) * 0.3f + f * 0.8f, f * 3.2f, 0);
            node.Scale = Vector3.One * (0.4f + f * 1.3f);
            node.Visible = true;
        }
        foreach (var (node, b, phase) in _fireflies)
        {
            node.Position = b + new Vector3(Mathf.Sin(t * 0.6f + phase) * 1.4f, Mathf.Sin(t * 1.3f + phase * 2) * 0.4f, Mathf.Cos(t * 0.5f + phase) * 1.4f);
            var blink = 0.6f + 0.4f * Mathf.Sin(t * 2.1f + phase * 3);
            node.Scale = Vector3.One * Mathf.Clamp((night - 0.4f) * 1.7f, 0, 1) * blink;
            node.Visible = night > 0.4f;
        }
        _lamps.AssignNearest(_lampSpots, new Vector3(0, 0, -1.5f), night, t);
        Walk(t, night);
        foreach (var (id, sign) in _signs)
        {
            if (Iso.ToScreen(_camera, SignSpots[id]) is not { } screen) continue;
            var size = sign.GetCombinedMinimumSize();
            sign.Size = size;
            var wiggle = Mathf.Sin((float)_time * 1.4f + sign.Phase) * 3f;
            sign.Position = screen - new Vector2(size.X / 2 - wiggle, size.Y + HangingSign.RopeLength);
            sign.Rotation = wiggle * 0.01f;
        }
    }

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
        var yaw = at < 0.66f ? Mathf.Lerp(-80, 40, at / 0.66f) : -35;
        _sun.RotationDegrees = new Vector3(Mathf.Lerp(a.SunPitch, b.SunPitch, f), yaw, 0);
        var lamps = Mathf.Lerp(a.Lamps, b.Lamps, f);
        WindowGlow.EmissionEnergyMultiplier = 2.4f * lamps;
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
                if (s.Anim is not null)
                {
                    Kenney.Play(s.Anim, "walk", 0.7f + s.Speed * 0.6f);
                    break;
                }
                var step = Mathf.Sin(t * 7 * s.Speed + g);
                s.Body.Position = new Vector3(0, Mathf.Abs(step) * 0.05f, 0);
                if (s.LeftArm is not null) s.LeftArm.Rotation = new Vector3(step * 0.5f, 0, 0);
                if (s.RightArm is not null) s.RightArm.Rotation = new Vector3(-step * 0.5f, 0, 0);
                break;
            }
        }
    }


    private static Material Grass => Iso.Flat(new Color("79a35a"));
    private static Material Dirt => Iso.Flat(new Color("8b6b47"));
    private static Material DirtDark => Iso.Flat(new Color("5f4731"));
    private static Material Path => Iso.Flat(new Color("cdb68c"));
    private static Material StoneWall => Iso.Flat(new Color("a39c92"));
    private static Material Plaster => Iso.Flat(new Color("e6d8b8"));
    private static Material Timber => Iso.Flat(new Color("8a6040"));
    private static Material RoofRed => Iso.Flat(new Color("b5523b"), 0.8f);
    private static Material RoofSlate => Iso.Flat(new Color("4e6a8c"), 0.8f);
    private static Material Rubble => Iso.Flat(new Color("8f877c"));
    private static Material RockFace => Iso.Flat(new Color("8d8883"));


    private void BuildScenery()
    {
        Board();
        Iso.Box(_world, new Vector3(9, 0.06f, 7), new Vector3(0, 0.03f, -1), Path);
        Iso.Box(_world, new Vector3(2.4f, 0.06f, 9), new Vector3(0, 0.03f, -8.5f), Path);
        Iso.Box(_world, new Vector3(2, 0.06f, 7), new Vector3(0, 0.03f, 5.5f), Path);
        Iso.Box(_world, new Vector3(9, 0.06f, 1.8f), new Vector3(8, 0.03f, 0), Path);
        Iso.Box(_world, new Vector3(8, 0.06f, 1.8f), new Vector3(-8, 0.03f, -1), Path);
        Iso.Box(_world, new Vector3(1.8f, 0.06f, 6), new Vector3(-8, 0.03f, -5), Path);

        var well = new Node3D { Position = new Vector3(0, 0, 0.5f) };
        _world.AddChild(well);
        Iso.Cylinder(well, 0.9f, 1f, 0.8f, new Vector3(0, 0.4f, 0), StoneWall, 12);
        Iso.Cylinder(well, 0.72f, 0.72f, 0.05f, new Vector3(0, 0.8f, 0), Iso.Flat(new Color("3a6a9a"), 0.2f), 12);
        Iso.Box(well, new Vector3(0.12f, 1.4f, 0.12f), new Vector3(-0.8f, 1.1f, 0), Timber);
        Iso.Box(well, new Vector3(0.12f, 1.4f, 0.12f), new Vector3(0.8f, 1.1f, 0), Timber);
        Iso.Roof(well, new Vector3(2.1f, 0.6f, 1.3f), new Vector3(0, 2.0f, 0), RoofRed);
        var board = new Node3D { Position = new Vector3(2.2f, 0, 1.2f), RotationDegrees = new Vector3(0, -20, 0) };
        _world.AddChild(board);
        Iso.Box(board, new Vector3(0.1f, 1.6f, 0.1f), new Vector3(-0.6f, 0.8f, 0), Timber);
        Iso.Box(board, new Vector3(0.1f, 1.6f, 0.1f), new Vector3(0.6f, 0.8f, 0), Timber);
        Iso.Box(board, new Vector3(1.5f, 0.9f, 0.08f), new Vector3(0, 1.25f, 0), Timber);
        for (var i = 0; i < 4; i++) Iso.Box(board, new Vector3(0.28f, 0.34f, 0.02f), new Vector3(-0.5f + i * 0.33f, 1.25f + (i % 2) * 0.08f, 0.05f), Iso.Flat(new Color("f0e6cc")));
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

    private void Board()
    {
        var width = BoardHalfWidth * 2;
        var depth = BoardFront - BoardBack;
        var centerZ = (BoardFront + BoardBack) / 2;
        for (var cx = 0; cx < 4; cx++)
        {
            for (var cz = 0; cz < 4; cz++)
            {
                var at = new Vector3(-BoardHalfWidth + width / 8 + cx * width / 4, -0.15f, BoardBack + depth / 8 + cz * depth / 4);
                Iso.Box(_world, new Vector3(width / 4, 0.3f, depth / 4), at, Grass);
            }
        }
        Iso.Box(_world, new Vector3(width, 2.2f, depth), new Vector3(0, -1.4f, centerZ), Dirt);
        Iso.Box(_world, new Vector3(width - 0.8f, 0.7f, depth - 0.8f), new Vector3(0, -2.85f, centerZ), DirtDark);
        for (var i = 0; i < 14; i++)
        {
            var side = i % 2 == 0;
            var along = Iso.Hash(i, 0, 90) * 2 - 1;
            var at = side ? new Vector3(along * (BoardHalfWidth - 1), 0, BoardFront - 0.8f) : new Vector3(-BoardHalfWidth + 0.9f, 0, BoardBack + 4 + (along + 1) * 0.5f * (depth - 6));
            if (Math.Abs(at.X - 17) < 3) continue;
            var model = Iso.Hash(i, 1, 90) < 0.5f ? "rock-small" : "rock-wide";
            if (Kenney.Spawn(_world, Kenney.Town, model, at, Iso.Hash(i, 2, 90) * 360, 0.6f + Iso.Hash(i, 3, 90) * 0.5f) is null)
            {
                Iso.Box(_world, new Vector3(0.7f, 0.45f, 0.6f), at + new Vector3(0, 0.2f, 0), Rubble, new Vector3(0, Iso.Hash(i, 2, 90) * 90, 8));
            }
        }
    }

    private void CommissionBoard(Vector3 at)
    {
        var n = new Node3D { Position = at, RotationDegrees = new Vector3(0, 25, 0) };
        _world.AddChild(n);
        foreach (var x in new[] { -0.8f, 0.8f }) Iso.Box(n, new Vector3(0.12f, 2f, 0.12f), new Vector3(x, 1f, 0), Timber);
        Iso.Box(n, new Vector3(1.7f, 1.1f, 0.08f), new Vector3(0, 1.35f, 0), Iso.Flat(new Color("9a7650")));
        Iso.Roof(n, new Vector3(2f, 0.45f, 0.6f), new Vector3(0, 2.15f, 0), RoofSlate);
        var paper = new[] { new Color("f0e6cc"), new Color("f4e4a4"), new Color("dce8f4") };
        for (var i = 0; i < 5; i++)
        {
            Iso.Box(n, new Vector3(0.26f, 0.32f, 0.02f), new Vector3(-0.6f + i * 0.3f, 1.3f + (i % 2) * 0.18f, 0.05f), Iso.Flat(paper[i % 3]), new Vector3(0, 0, (i - 2) * 4));
            Iso.Box(n, new Vector3(0.05f, 0.05f, 0.02f), new Vector3(-0.6f + i * 0.3f, 1.44f + (i % 2) * 0.18f, 0.065f), Iso.Flat(new Color("c04040")));
        }
        Iso.Box(n, new Vector3(0.18f, 0.22f, 0.18f), new Vector3(0.95f, 1.85f, 0.15f), Iso.Glow(new Color("ffc46b"), 3f));
        RegisterLamp(n.ToGlobal(new Vector3(0.9f, 1.8f, 0.6f)), new Color("ffb86b"), 1.0f, 4f, 23);
    }

    private void Lamp(Vector3 p)
    {
        var n = new Node3D { Position = p };
        _world.AddChild(n);
        Iso.Cylinder(n, 0.05f, 0.07f, 2.2f, new Vector3(0, 1.1f, 0), Iso.Flat(new Color("3a3632"), 0.5f), 6);
        Iso.Box(n, new Vector3(0.28f, 0.34f, 0.28f), new Vector3(0, 2.3f, 0), Iso.Glow(new Color("ffc46b"), 3f));
        RegisterLamp(p + new Vector3(0, 2.3f, 0), new Color("ffb86b"), 1.4f, 6f, p.X * 3 + p.Z);
    }

    private void Cliff()
    {
        var moss = Iso.Flat(new Color("6f9a52"));
        for (var x = -25; x <= 25; x += 2)
        {
            if (Math.Abs(x) <= 1) continue;
            var h = 5 + Iso.Hash(x, 0, 21) * 4 + Math.Abs(x) * 0.08f;
            var d = 2.4f + Iso.Hash(x, 2, 21) * 1.2f;
            var z = BoardBack + d / 2 + 0.2f + Iso.Hash(x, 1, 21) * 0.6f;
            var rot = new Vector3(0, Iso.Hash(x, 3, 21) * 12 - 6, 0);
            Iso.Box(_world, new Vector3(2.4f, h, d), new Vector3(x, h / 2 - 0.2f, z), RockFace, rot);
            Iso.Box(_world, new Vector3(2.5f, 0.25f, d + 0.1f), new Vector3(x, h - 0.1f, z), moss, rot);
        }
        var cave = new Node3D { Position = new Vector3(0, 0, -13.2f) };
        _world.AddChild(cave);
        Iso.Box(cave, new Vector3(3, 3.4f, 2), new Vector3(0, 1.5f, -0.8f), Iso.Flat(new Color("0a0a0c")));
        Iso.Box(cave, new Vector3(1, 4.4f, 2.6f), new Vector3(-2, 2f, -0.6f), RockFace);
        Iso.Box(cave, new Vector3(1, 4.4f, 2.6f), new Vector3(2, 2f, -0.6f), RockFace);
        Iso.Box(cave, new Vector3(5, 1.4f, 2.6f), new Vector3(0, 4.4f, -0.6f), RockFace);
        var runes = Iso.Glow(new Color("5dd3e8"), 3.5f);
        for (var i = 0; i < 5; i++)
        {
            var a = Mathf.Pi * (i + 0.5f) / 5;
            Iso.Box(cave, new Vector3(0.18f, 0.18f, 0.05f), new Vector3(Mathf.Cos(a) * -1.7f, 1.3f + Mathf.Sin(a) * 2.2f, 0.72f), runes, new Vector3(0, 0, 45));
        }
        RegisterLamp(cave.Position + new Vector3(0, 1.4f, 1.2f), new Color("5dd3e8"), 2.2f, 7f, 0);
        foreach (var s in new[] { -1f, 1f })
        {
            var torch = new Node3D { Position = new Vector3(s * 2.8f, 0, 1f) };
            cave.AddChild(torch);
            Iso.Cylinder(torch, 0.06f, 0.08f, 1.6f, new Vector3(0, 0.8f, 0), Timber, 6);
            Iso.Ball(torch, 0.16f, new Vector3(0, 1.75f, 0), Iso.Glow(new Color("ff9a40"), 5f), new Vector3(1, 1.5f, 1));
            RegisterLamp(cave.Position + torch.Position + new Vector3(0, 1.9f, 0.2f), new Color("ff9a50"), 1.8f, 6f, s * 5);
        }
        foreach (var s in new[] { -0.45f, 0.45f }) Iso.Box(_world, new Vector3(0.08f, 0.08f, 8), new Vector3(s, 0.1f, -9), Iso.Flat(new Color("6a6a70"), 0.4f));
    }

    private void River()
    {
        var depth = BoardFront - BoardBack;
        var centerZ = (BoardFront + BoardBack) / 2;
        var water = Iso.Translucent(new Color(0.3f, 0.58f, 0.8f, 0.82f), 0.15f);
        Iso.Box(_world, new Vector3(3.2f, 0.1f, depth), new Vector3(17, 0.02f, centerZ), water);
        Iso.Box(_world, new Vector3(0.6f, 0.2f, depth), new Vector3(15.3f, 0.05f, centerZ), Rubble);
        Iso.Box(_world, new Vector3(0.6f, 0.2f, depth), new Vector3(18.7f, 0.05f, centerZ), Rubble);
        Iso.Box(_world, new Vector3(3.2f, 2.9f, 0.14f), new Vector3(17, -1.4f, BoardFront + 0.08f), water);
        var bridge = new Node3D { Position = new Vector3(17, 0.25f, 8) };
        _world.AddChild(bridge);
        Iso.Box(bridge, new Vector3(4.2f, 0.15f, 1.6f), Vector3.Zero, Timber);
        foreach (var s in new[] { -0.75f, 0.75f }) Iso.Box(bridge, new Vector3(4.2f, 0.1f, 0.08f), new Vector3(0, 0.5f, s), Timber);
    }

    private static readonly string[] TreeModels = ["tree", "tree-high", "tree-high-round", "tree-crooked"];

    private void Trees()
    {
        var trunk = Iso.Flat(new Color("7a5334"));
        var leaves = Iso.Flat(new Color("5f9a48"));
        var pine = Iso.Flat(new Color("3f7a4a"));
        for (var i = 0; i < 70; i++)
        {
            var x = Iso.Hash(i, 0, 31) * (BoardHalfWidth * 2 - 2) - BoardHalfWidth + 1;
            var z = Iso.Hash(i, 1, 31) * (BoardFront - BoardBack - 2) + BoardBack + 1;
            if (z < -12) continue;
            if (Math.Abs(x) < 13 && z > -11 && z < 10) continue;
            if (x > 14 && x < 20) continue;
            if (new Vector3(x, 0, z).DistanceTo(LumberLot) < 3.2f || new Vector3(x, 0, z).DistanceTo(TavernLot) < 3.8f) continue;
            var h = 1.5f + Iso.Hash(i, 2, 31) * 1.8f;
            var n = new Node3D { Position = new Vector3(x, 0, z) };
            _world.AddChild(n);
            var model = TreeModels[(int)(Iso.Hash(i, 3, 31) * TreeModels.Length) % TreeModels.Length];
            if (Kenney.Spawn(n, Kenney.Town, model, Vector3.Zero, Iso.Hash(i, 5, 31) * 360, 1.1f + Iso.Hash(i, 2, 31) * 0.6f) is not null) continue;
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


    private Node3D Lot(Vector3 at, float yaw = 0)
    {
        var n = new Node3D { Position = at, RotationDegrees = new Vector3(0, yaw, 0) };
        _lots.AddChild(n);
        return n;
    }

    private static void ForEachEdge(int w, int d, Action<Vector3, float, bool, int> panel)
    {
        var n = 0;
        for (var i = 0; i < w; i++)
        {
            for (var j = 0; j < d; j++)
            {
                var cell = new Vector3(i - (w - 1) / 2f, 0, j - (d - 1) / 2f);
                if (i == w - 1) panel(cell, 0, false, n++);
                if (i == 0) panel(cell, 180, false, n++);
                if (j == d - 1) panel(cell, -90, i == w / 2, n++);
                if (j == 0) panel(cell, 90, false, n++);
            }
        }
    }

    private static float Shell(Node3D lot, Vector3 at, int w, int d, int floors, bool woodGround, bool woodUpper, float scale, int salt, Material fallback, Material roof)
    {
        var width = w * scale;
        var depth = d * scale;
        var top = floors * scale;
        if (Kenney.Mesh(Kenney.Town, "wall") is null)
        {
            Iso.Box(lot, new Vector3(width, top, depth), at + new Vector3(0, top / 2, 0), fallback);
        }
        else
        {
            var house = new Node3D { Position = at, Scale = Vector3.One * scale };
            lot.AddChild(house);
            for (var f = 0; f < floors; f++)
            {
                var kind = (f == 0 ? woodGround : woodUpper) ? "wall-wood" : "wall";
                var level = f;
                ForEachEdge(w, d, (cell, yaw, door, n) =>
                {
                    var roll = Iso.Hash(n, level, salt);
                    var piece = door && level == 0 ? kind + "-door" : roll < 0.4f ? kind + "-window-shutters" : roll < 0.6f ? kind + "-window-small" : kind;
                    Kenney.Spawn(house, Kenney.Town, piece, cell + new Vector3(0, level, 0), yaw);
                });
            }
            Iso.Box(house, new Vector3(w - 0.24f, floors - 0.02f, d - 0.24f), new Vector3(0, floors / 2f, 0), WindowGlow);
        }
        var alongX = width >= depth;
        var span = alongX ? depth : width;
        var h = span * 0.45f + 0.3f;
        Iso.Roof(lot, new Vector3((alongX ? width : depth) + 0.4f, h, span + 0.5f), at + new Vector3(0, top + h / 2, 0), roof, alongX ? 0 : 90);
        return top;
    }

    private void Chimney(Node3D parent, Vector3 top, float amount = 1)
    {
        Iso.Box(parent, new Vector3(0.45f, 1.2f, 0.45f), top - new Vector3(0, 0.6f, 0), StoneWall);
        var smoke = Iso.Translucent(new Color(0.9f, 0.9f, 0.92f, 0.35f));
        var origin = parent.ToGlobal(top);
        for (var i = 0; i < (int)(5 * amount); i++)
        {
            var puff = Iso.Ball(_world, 0.25f, origin, smoke);
            _smoke.Add((puff, origin, i / (5f * amount)));
        }
    }

    private static void Ruin(Node3D lot, Vector3 size, int salt)
    {
        const float scale = 1.4f;
        var built = false;
        if (Kenney.Mesh(Kenney.Town, "wall-broken") is not null)
        {
            var house = new Node3D { Scale = Vector3.One * scale };
            lot.AddChild(house);
            ForEachEdge(Math.Max(1, (int)Math.Round(size.X / scale)), Math.Max(1, (int)Math.Round(size.Z / scale)), (cell, yaw, _, n) =>
            {
                if (Iso.Hash(n, salt, 57) < 0.3f) return;
                Kenney.Spawn(house, Kenney.Town, Iso.Hash(n, salt, 58) < 0.5f ? "wall-broken" : "wall-wood-broken", cell, yaw);
            });
            built = true;
        }
        for (var i = 0; i < 4 && !built; i++)
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
        var top = Shell(n, Vector3.Zero, 3, 2, 2, false, false, 1.4f, 70, StoneWall, RoofSlate);
        var rune = Iso.Glow(new Color("5dd3e8"), 3f);
        var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.35f, OuterRadius = 0.5f, Rings = 16, RingSegments = 4 }, Position = new Vector3(0, top * 0.72f, 1.5f), RotationDegrees = new Vector3(90, 0, 0), MaterialOverride = rune };
        n.AddChild(ring);
        foreach (var s in new[] { -2.3f, 2.3f }) Iso.Box(n, new Vector3(0.4f, top + 0.2f, 0.4f), new Vector3(s, (top + 0.2f) / 2, 1.5f), StoneWall);
        RegisterLamp(at + new Vector3(0, 2.2f, 2.4f), new Color("7ad8ff"), 1.4f, 5f, 3);
    }

    private void Forge(Vector3 at, int level)
    {
        var lot = Lot(at);
        if (level == 0)
        {
            Ruin(lot, new Vector3(4, 2.4f, 3), 1);
            return;
        }
        var top = Shell(lot, Vector3.Zero, 3, 2, 1, false, false, 1.6f, 71, StoneWall, RoofSlate);
        Chimney(lot, new Vector3(1.3f, top + 2.1f, -0.6f), 1.2f);
        var hearth = new Node3D { Position = new Vector3(-1.3f, 0, 2.3f) };
        lot.AddChild(hearth);
        Iso.Box(hearth, new Vector3(1.1f, 0.7f, 0.8f), new Vector3(0, 0.35f, 0), StoneWall);
        Iso.Box(hearth, new Vector3(0.85f, 0.1f, 0.55f), new Vector3(0, 0.72f, 0), Iso.Glow(new Color("ff7a2a"), 3f));
        RegisterLamp(at + new Vector3(-1.3f, 1.2f, 2.6f), new Color("ff7a30"), 2.4f, 6f, 7);
        var anvil = new Node3D { Position = new Vector3(1.2f, 0, 2.3f) };
        lot.AddChild(anvil);
        var iron = Iso.Flat(new Color("4a4a52"), 0.4f);
        Iso.Box(anvil, new Vector3(0.3f, 0.4f, 0.3f), new Vector3(0, 0.2f, 0), iron);
        Iso.Box(anvil, new Vector3(0.7f, 0.18f, 0.32f), new Vector3(0, 0.48f, 0), iron);
        if (level >= 2)
        {
            var annex = Shell(lot, new Vector3(-3.4f, 0, 0), 1, 2, 1, false, false, 1.4f, 72, StoneWall, RoofRed);
            Chimney(lot, new Vector3(-3.4f, annex + 2f, 0), 1.5f);
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
        var stone = Iso.Flat(new Color("aaa39a"));
        Iso.Cylinder(lot, 1.3f, 1.5f, 3.2f, new Vector3(0, 1.6f, 0), stone, 12);
        Iso.Cylinder(lot, 1.1f, 1.25f, 2.6f, new Vector3(0, 4.5f, 0), stone, 12);
        Iso.Cylinder(lot, 1.32f, 1.32f, 0.2f, new Vector3(0, 3.25f, 0), Timber, 12);
        Iso.Cylinder(lot, 0, 1.5f, 2.2f, new Vector3(0, 6.9f, 0), Iso.Flat(new Color("6b4f9a"), 0.7f), 12);
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
        RegisterLamp(at + new Vector3(0, 8.4f, 0), new Color("c792ea"), 2.5f, 9f, 11);
        if (level >= 2)
        {
            Iso.Ball(lot, 1.0f, new Vector3(2.2f, 3.3f, 0), Iso.Flat(new Color("8a9aaa"), 0.4f), new Vector3(1, 0.8f, 1));
            Iso.Cylinder(lot, 1.1f, 1.1f, 2.6f, new Vector3(2.2f, 1.3f, 0), stone, 10);
            var scope = Iso.Cylinder(lot, 0.12f, 0.2f, 1.6f, new Vector3(2.6f, 4f, 0.3f), Iso.Solid(new Color("c9a35a"), 0.3f, 0.9f), 8);
            scope.RotationDegrees = new Vector3(40, 0, -30);
        }
    }

    private void Guild(Vector3 at, int level)
    {
        var lot = Lot(at, -10);
        if (level == 0)
        {
            Ruin(lot, new Vector3(5, 2.6f, 3.4f), 3);
            return;
        }
        var top = Shell(lot, Vector3.Zero, 4, 2, level >= 2 ? 2 : 1, false, true, 1.3f, 73, Plaster, RoofRed);
        Iso.Box(lot, new Vector3(0.08f, 2.6f, 0.08f), new Vector3(2.9f, 1.3f, 2.2f), Timber);
        Iso.Box(lot, new Vector3(0.7f, 1.1f, 0.03f), new Vector3(2.9f, 2f, 2.25f), Iso.Flat(new Color("b8453a")));
        Iso.Box(lot, new Vector3(0.24f, 0.24f, 0.02f), new Vector3(2.9f, 2.1f, 2.27f), Iso.Glow(new Color("f5b041"), 2f), new Vector3(0, 0, 45));
        Chimney(lot, new Vector3(-1.8f, top + 1.9f, -0.5f), 0.6f);
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
            if (Kenney.Spawn(s, Kenney.Town, i % 2 == 0 ? "stall-red" : "stall-green", Vector3.Zero, 0, 1.8f) is not null) continue;
            Iso.Box(s, new Vector3(1.8f, 0.8f, 0.9f), new Vector3(0, 0.4f, 0), Timber);
            foreach (var px in new[] { -0.85f, 0.85f }) Iso.Box(s, new Vector3(0.08f, 2f, 0.08f), new Vector3(px, 1f, -0.4f), Timber);
            var cloth = Iso.Box(s, new Vector3(2.1f, 0.06f, 1.4f), new Vector3(0, 2f, 0.1f), Iso.Flat(colors[i % colors.Length]));
            cloth.RotationDegrees = new Vector3(-14, 0, 0);
            for (var k = 0; k < 3; k++) Iso.Ball(s, 0.14f, new Vector3(-0.5f + k * 0.5f, 0.92f, 0.1f), Iso.Flat(colors[(i + k + 1) % colors.Length]));
        }
        RegisterLamp(at + new Vector3(0, 2.6f, 1.2f), new Color("ffc46b"), 1.2f, 5f, 13);
        if (level >= 2)
        {
            for (var i = 0; i < 9; i++) Iso.Ball(lot, 0.08f, new Vector3(-4.8f + i * 1.2f, 2.5f - Mathf.Sin(i / 8f * Mathf.Pi) * 0.3f, 1.3f), Iso.Glow(new Color("ffd27a"), 4f));
        }
    }

    private void Mill(Vector3 at, int level)
    {
        var lot = Lot(at);
        if (level == 0)
        {
            Ruin(lot, new Vector3(3, 2.4f, 3), 5);
            return;
        }
        Shell(lot, Vector3.Zero, 2, 2, 2, false, true, 1.3f, 74, Plaster, RoofRed);
        var wheel = new Node3D { Position = new Vector3(1.85f, 1.3f, 0), RotationDegrees = new Vector3(0, 90, 0) };
        lot.AddChild(wheel);
        var spin = new Node3D();
        wheel.AddChild(spin);
        if (Kenney.Spawn(spin, Kenney.Town, "watermill", Vector3.Zero, 90, 1.4f) is null)
        {
            for (var i = 0; i < 8; i++)
            {
                var paddle = Iso.Box(spin, new Vector3(0.14f, 2.6f, 0.5f), Vector3.Zero, Timber);
                paddle.RotationDegrees = new Vector3(0, 0, i * 22.5f);
            }
        }
        _wheels.Add(spin);
        if (level >= 2)
        {
            foreach (var z in new[] { -2.6f, -4.2f })
            {
                var cart = new Node3D { Position = new Vector3(-2.5f, 0, z) };
                lot.AddChild(cart);
                if (Kenney.Spawn(cart, Kenney.Town, "cart", Vector3.Zero, 90, 1.5f) is null)
                {
                    Iso.Box(cart, new Vector3(0.9f, 0.6f, 1.1f), new Vector3(0, 0.5f, 0), Timber);
                }
                Iso.Box(cart, new Vector3(0.6f, 0.2f, 0.8f), new Vector3(0, 0.85f, 0), Iso.Flat(new Color("9a8a70")));
                Iso.Ball(cart, 0.08f, new Vector3(0.1f, 0.95f, 0.1f), Iso.Glow(new Color("ffd24a"), 3f));
            }
        }
    }

    private static void Plot(Node3D lot, Vector3 size)
    {
        var rope = Iso.Flat(new Color("c8b08a"));
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
        var logs = Iso.Flat(new Color("8a6038"));
        Shell(lot, Vector3.Zero, 2, 2, 1, true, true, 1.3f, 75, logs, RoofSlate);
        for (var i = 0; i < 6; i++)
        {
            var log = Iso.Cylinder(lot, 0.2f, 0.2f, 1.6f, new Vector3(-2.7f + (i % 3) * 0.42f + (i / 3) * 0.21f, 0.2f + (i / 3) * 0.36f, 0.4f), logs, 8);
            log.RotationDegrees = new Vector3(90, 0, 0);
        }
        Iso.Cylinder(lot, 0.35f, 0.4f, 0.5f, new Vector3(1.9f, 0.25f, 1.4f), logs, 10);
        Iso.Box(lot, new Vector3(0.06f, 0.7f, 0.06f), new Vector3(1.9f, 0.75f, 1.4f), Timber, new Vector3(0, 0, 25));
        Iso.Box(lot, new Vector3(0.3f, 0.16f, 0.05f), new Vector3(2.05f, 1.05f, 1.4f), Iso.Solid(new Color("9a9aa0"), 0.4f, 0.8f), new Vector3(0, 0, 25));
    }

    private void Quarry(Vector3 at, int level)
    {
        var lot = Lot(at, 10);
        if (level == 0)
        {
            Plot(lot, new Vector3(3.2f, 0, 2.6f));
            return;
        }
        var cut = Iso.Flat(new Color("b4ab9c"));
        for (var i = 0; i < 3; i++) Iso.Box(lot, new Vector3(3.4f - i * 0.9f, 0.6f, 1.2f), new Vector3(0, 0.3f + i * 0.6f, -0.9f - i * 0.2f), cut);
        foreach (var (x, z, r) in new[] { (-1.3f, 0.9f, 10f), (-0.5f, 1.2f, -8f), (0.4f, 0.8f, 25f) })
        {
            Iso.Box(lot, new Vector3(0.6f, 0.45f, 0.45f), new Vector3(x, 0.22f, z), cut, new Vector3(0, r, 0));
        }
        Iso.Box(lot, new Vector3(0.12f, 2.6f, 0.12f), new Vector3(1.6f, 1.3f, 0.6f), Timber);
        var arm = Iso.Box(lot, new Vector3(1.8f, 0.1f, 0.1f), new Vector3(1.0f, 2.5f, 0.6f), Timber);
        arm.RotationDegrees = new Vector3(0, 0, -8);
        Iso.Box(lot, new Vector3(0.02f, 1f, 0.02f), new Vector3(0.2f, 2f, 0.6f), Iso.Flat(new Color("c8b08a")));
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
        var soil = Iso.Flat(new Color("6e5038"));
        var wheat = Iso.Flat(new Color("e2c262"));
        Iso.Box(lot, new Vector3(size.X, 0.1f, size.Z), new Vector3(0, 0.05f, 0), soil);
        for (var r = 0; r < 3; r++) Iso.Box(lot, new Vector3(size.X - 0.4f, 0.55f, 0.32f), new Vector3(0, 0.35f, -size.Z / 2 + 0.35f + r * 0.58f), wheat);
        var pumpkin = Iso.Flat(new Color("e98a2a"), 0.6f);
        var stem = Iso.Flat(new Color("5a7a32"));
        for (var i = 0; i < 7; i++)
        {
            var at2 = new Vector3(-size.X / 2 + 0.5f + i * 0.62f, 0, size.Z / 2 - 0.9f + (i % 2) * 0.35f);
            var big = 0.2f + Iso.Hash(i, 0, 62) * 0.12f;
            Iso.Ball(lot, big, at2 + new Vector3(0, big * 0.8f, 0), pumpkin, new Vector3(1.2f, 0.85f, 1.2f));
            Iso.Box(lot, new Vector3(0.05f, 0.12f, 0.05f), at2 + new Vector3(0, big * 1.55f, 0), stem);
            Iso.Box(lot, new Vector3(0.3f, 0.03f, 0.18f), at2 + new Vector3(0.18f, 0.1f, 0.1f), stem, new Vector3(0, i * 40, 0));
        }
        if (level >= 2) Greenhouse(lot, new Vector3(-size.X / 2 - 2.2f, 0, 0));
        const float fence = 1.25f;
        var fenceRoot = new Node3D();
        lot.AddChild(fenceRoot);
        var kenneyFence = true;
        for (var i = 0; i < 4; i++)
        {
            var x = -size.X / 2 + fence / 2 + i * fence;
            if (Kenney.Spawn(fenceRoot, Kenney.Town, i == 2 ? "fence-broken" : "fence", new Vector3(x, 0, size.Z / 2 + 0.2f - 0.46f * fence), -90, fence) is null)
            {
                kenneyFence = false;
                break;
            }
        }
        if (!kenneyFence)
        {
            foreach (var n in fenceRoot.GetChildren()) n.QueueFree();
            for (var i = 0; i <= 10; i++)
            {
                Iso.Box(lot, new Vector3(0.08f, 0.6f, 0.08f), new Vector3(-size.X / 2 + i * size.X / 10, 0.3f, size.Z / 2 + 0.2f), Timber);
            }
            Iso.Box(lot, new Vector3(size.X, 0.06f, 0.05f), new Vector3(0, 0.5f, size.Z / 2 + 0.2f), Timber);
        }
        var crow = new Node3D { Position = new Vector3(1.6f, 0, 0) };
        lot.AddChild(crow);
        Iso.Box(crow, new Vector3(0.08f, 1.7f, 0.08f), new Vector3(0, 0.85f, 0), Timber);
        Iso.Box(crow, new Vector3(1.1f, 0.07f, 0.07f), new Vector3(0, 1.3f, 0), Timber);
        Iso.Ball(crow, 0.2f, new Vector3(0, 1.8f, 0), Iso.Flat(new Color("e0c890")));
        Iso.Cylinder(crow, 0, 0.32f, 0.3f, new Vector3(0, 2.05f, 0), Iso.Flat(new Color("7a5a32")), 8);
    }

    private void Greenhouse(Node3D lot, Vector3 at)
    {
        var n = new Node3D { Position = at };
        lot.AddChild(n);
        var size = new Vector3(2.6f, 1.6f, 2.8f);
        var glass = Iso.Translucent(new Color(0.7f, 0.9f, 1f, 0.3f), 0.3f);
        var frame = Iso.Flat(new Color("f0f0e8"), 0.5f);
        Iso.Box(n, new Vector3(size.X, 0.25f, size.Z), new Vector3(0, 0.12f, 0), StoneWall);
        Iso.Box(n, size, new Vector3(0, size.Y / 2 + 0.25f, 0), glass);
        Iso.Roof(n, new Vector3(size.X + 0.1f, 0.9f, size.Z + 0.1f), new Vector3(0, size.Y + 0.7f, 0), glass, 90);
        foreach (var x in new[] { -1f, 0, 1f })
        {
            foreach (var z in new[] { -1f, 1f }) Iso.Box(n, new Vector3(0.06f, size.Y, 0.06f), new Vector3(x * size.X / 2, size.Y / 2 + 0.25f, z * size.Z / 2), frame);
        }
        var sprout = Iso.Flat(new Color("7bd88f"));
        for (var i = 0; i < 8; i++) Iso.Box(n, new Vector3(0.14f, 0.3f, 0.14f), new Vector3(-0.8f + (i % 4) * 0.55f, 0.45f, -0.6f + (i / 4) * 1.2f), sprout, new Vector3(0, i * 30, 0));
        RegisterLamp(n.ToGlobal(new Vector3(0, 1.2f, 0)), new Color("b0f0c0"), 1.2f, 4.5f, 29);
    }

    private void Tavern(Vector3 at, int level)
    {
        var lot = Lot(at, 20);
        if (level == 0)
        {
            Plot(lot, new Vector3(3.6f, 2.2f, 2.8f));
            return;
        }
        var top = Shell(lot, Vector3.Zero, 3, 2, 2, false, true, 1.3f, 76, StoneWall, RoofRed);
        const float front = 1.3f;
        Chimney(lot, new Vector3(-1.2f, top + 1.9f, -0.6f), 0.8f);
        var sign = new Node3D { Position = new Vector3(-1.2f, 2.0f, front + 0.5f) };
        lot.AddChild(sign);
        Iso.Box(sign, new Vector3(0.06f, 0.06f, 0.7f), new Vector3(0, 0.3f, -0.25f), Timber);
        Iso.Box(sign, new Vector3(0.05f, 0.5f, 0.6f), new Vector3(0, 0, 0), Iso.Flat(new Color("6a4a2a")));
        Iso.Ball(sign, 0.16f, new Vector3(0.04f, 0, 0), Iso.Flat(new Color("e98a2a")), new Vector3(0.4f, 0.8f, 1));
        for (var i = 0; i < 3; i++)
        {
            var bench = new Node3D { Position = new Vector3(-1.6f + i * 1.3f, 0, front + 1.1f) };
            lot.AddChild(bench);
            Iso.Box(bench, new Vector3(0.9f, 0.08f, 0.5f), new Vector3(0, 0.5f, 0), Timber);
            Iso.Box(bench, new Vector3(0.1f, 0.5f, 0.1f), new Vector3(0, 0.25f, 0), Timber);
            Iso.Cylinder(bench, 0.07f, 0.06f, 0.16f, new Vector3(0.2f, 0.62f, 0), Iso.Solid(new Color("c9a35a"), 0.3f, 0.6f), 6);
        }
        foreach (var x in new[] { -2.3f, 2.3f })
        {
            if (Kenney.Spawn(lot, Kenney.Dungeon, "barrel", new Vector3(x, 0, front + 0.35f), x * 20, 1.4f) is null)
            {
                Iso.Cylinder(lot, 0.3f, 0.34f, 0.7f, new Vector3(x, 0.35f, front + 0.35f), Iso.Flat(new Color("8a5a30")), 10);
            }
        }
        var lanterns = Iso.Glow(new Color("ffd27a"), 4f);
        for (var i = 0; i < 6; i++) Iso.Ball(lot, 0.07f, new Vector3(-2 + i * 0.8f, 2.4f - Mathf.Sin(i / 5f * Mathf.Pi) * 0.25f, front + 1.6f), lanterns);
        RegisterLamp(at + new Vector3(0, 1.6f, front + 1.4f), new Color("ffb060"), 1.8f, 6f, 31);
    }

    private void Smelter(Vector3 at, int level)
    {
        var lot = Lot(at, -15);
        if (level == 0)
        {
            Ruin(lot, new Vector3(3, 2.2f, 2.6f), 20);
            return;
        }
        var brick = Iso.Flat(new Color("a8644a"));
        Iso.Cylinder(lot, 0.9f, 1.4f, 2.6f, new Vector3(0, 1.3f, 0), brick, 10);
        Iso.Cylinder(lot, 0.45f, 0.6f, 1.6f, new Vector3(0, 3.4f, 0), brick, 8);
        Iso.Box(lot, new Vector3(0.9f, 0.8f, 0.1f), new Vector3(0, 0.6f, 1.25f), Iso.Glow(new Color("ff7a2a"), 3.5f));
        Chimney(lot, new Vector3(0, 4.5f, 0), 1.3f);
        RegisterLamp(at + new Vector3(0, 0.8f, 2f), new Color("ff8030"), 2.6f, 6f, 17);
        var ore = Iso.Flat(new Color("a8603c"));
        for (var i = 0; i < 5; i++) Iso.Ball(lot, 0.28f, new Vector3(-1.9f + (i % 3) * 0.35f, 0.2f + (i / 3) * 0.3f, 0.6f + (i % 2) * 0.3f), ore);
        var iron = Iso.Solid(new Color("9fb0c0"), 0.35f, 0.9f);
        for (var i = 0; i < 4; i++) Iso.Box(lot, new Vector3(0.7f, 0.14f, 0.26f), new Vector3(1.9f, 0.08f + (i / 2) * 0.15f, 0.6f + (i % 2) * 0.3f), iron);
    }

    private void Bakery(Vector3 at, int level)
    {
        var lot = Lot(at, -20);
        if (level == 0)
        {
            Ruin(lot, new Vector3(3, 2.2f, 2.6f), 21);
            return;
        }
        var top = Shell(lot, Vector3.Zero, 2, 2, 1, false, false, 1.45f, 77, Plaster, RoofRed);
        Iso.Ball(lot, 0.95f, new Vector3(2.4f, 0.3f, 0), StoneWall, new Vector3(1, 0.85f, 1));
        Iso.Box(lot, new Vector3(0.1f, 0.45f, 0.5f), new Vector3(3.3f, 0.4f, 0), Iso.Glow(new Color("ff9a40"), 3.5f));
        Chimney(lot, new Vector3(0.9f, top + 2f, -0.5f), 0.9f);
        RegisterLamp(at + new Vector3(3.7f, 0.8f, 0), new Color("ffa050"), 1.6f, 5f, 19);
        Iso.Box(lot, new Vector3(0.08f, 0.08f, 0.8f), new Vector3(1.0f, 2f, 1.75f), Timber);
        Iso.Box(lot, new Vector3(0.05f, 0.5f, 0.6f), new Vector3(1.05f, 1.7f, 2.05f), Iso.Flat(new Color("6a4a2a")));
        Iso.Ball(lot, 0.18f, new Vector3(1.1f, 1.7f, 2.05f), Iso.Flat(new Color("e0a86a")), new Vector3(0.5f, 0.8f, 1.4f));
    }

    private void Cottages(int level)
    {
        var spots = new[] { (new Vector3(-6, 0, 7), 15f), (new Vector3(-11, 0, 5), -10f), (new Vector3(-3.5f, 0, 11), 5f) };
        for (var i = 0; i < spots.Length; i++)
        {
            var (at, yaw) = spots[i];
            var lot = Lot(at, yaw);
            if (level == 0)
            {
                Ruin(lot, new Vector3(2.6f, 1.8f, 2.2f), 10 + i);
                continue;
            }
            var top = Shell(lot, Vector3.Zero, 2, 2, 1, i == 1, true, 1.45f, 80 + i, Plaster, i == 1 ? RoofSlate : RoofRed);
            Chimney(lot, new Vector3(0.8f, top + 1.9f, -0.4f), 0.5f);
        }
    }
}
