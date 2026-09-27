using Delvework.Core.Content;
using Delvework.Core.Replay;
using Delvework.Core.Sim;
using Godot;

namespace Delvework.Game.View3D;

/// <summary>
/// The dungeon in 2.5D: a lit 3D scene rendered at low resolution for a pixel-art look, with a
/// crisp 2D overlay for health bars, intent icons and damage numbers. Everything is derived
/// from the timeline and a fractional playhead, so scrubbing backwards works like playing.
/// </summary>
public partial class DungeonView3D : Control
{
    private const float WallHeight = 1.25f;
    private const int EffectWindow = 8;
    private const float FigureScale = 1.25f;

    private SubViewport _viewport = null!;
    private Camera3D _camera = null!;
    private Node3D _level = null!, _entities = null!, _fx = null!;
    private DungeonOverlay _overlay = null!;
    private Button _followButton = null!;

    private Timeline? _timeline;
    private double _playhead;
    private int _fogTick = -1;
    private MultiMesh? _floorMm, _wallMm, _capMm;
    private int[] _floorIndex = [], _wallIndex = [];
    private float[] _wallHeight = [];
    private readonly List<(Node3D Node, int TileIdx)> _hideInFog = [];
    private readonly List<(Node3D Node, Node3D Lid, OmniLight3D Glint)> _chests = [];
    private readonly List<Node3D> _traps = [];
    private readonly List<(List<Node3D> Chunks, int Amount)> _veins = [];
    private readonly List<(OmniLight3D Light, float Phase)> _torches = [];
    private readonly Dictionary<int, Figure> _golems = [];
    private readonly Dictionary<int, Figure> _monsters = [];
    private readonly List<Node3D> _coinPool = [];
    private readonly List<MeshInstance3D> _sparkPool = [];
    private readonly List<MeshInstance3D> _beamPool = [];
    private readonly List<MeshInstance3D> _ringPool = [];
    private readonly Dictionary<int, MeshInstance3D> _shields = [];
    private readonly List<MeshInstance3D> _markPool = [];
    private readonly List<MeshInstance3D> _threadPool = [];
    private int _sparksUsed, _beamsUsed, _ringsUsed;

    /// <summary>Scent colours: each mark label gets one, so the threads of different trails differ.</summary>
    private static readonly Color[] ScentColors = [new("c792ea"), new("5dd3e8"), new("7bd88f"), new("f5b041"), new("f07178")];

    /// <summary>Ticks a mark lasts (it fades out toward the end).</summary>
    private const float MarkLife = 600f;

    private Vector3 _target;
    private float _zoom = 10f;
    private bool _follow = true;
    private bool _dragging;
    private double _time;

    public bool Follow
    {
        get => _follow;
        set
        {
            _follow = value;
            if (_followButton is not null) _followButton.ButtonPressed = value;
        }
    }

    public override void _Ready()
    {
        ClipContents = true;
        var (container, viewport) = Iso.PixelViewport();
        container.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(container);
        _viewport = viewport;

        var env = Iso.Environment(new Color("07080a"), new Color("6a7aa0"), 0.5f);
        viewport.AddChild(new WorldEnvironment { Environment = env });
        _camera = Iso.Camera(_zoom);
        viewport.AddChild(_camera);
        viewport.AddChild(new DirectionalLight3D
        {
            LightColor = new Color("6f7fa8"),
            LightEnergy = 0.12f,
            RotationDegrees = new Vector3(-60, 30, 0),
        });
        _level = new Node3D { Name = "Level" };
        _entities = new Node3D { Name = "Entities" };
        _fx = new Node3D { Name = "Effects" };
        viewport.AddChild(_level);
        viewport.AddChild(_entities);
        viewport.AddChild(_fx);

        _overlay = new DungeonOverlay(this) { MouseFilter = MouseFilterEnum.Ignore };
        _overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_overlay);

        _followButton = new Button
        {
            Text = "Follow party",
            ToggleMode = true,
            ButtonPressed = true,
            FocusMode = FocusModeEnum.None,
            TooltipText = "Keep the camera on the golems. Drag with the right mouse button to look around, scroll to zoom.",
        };
        _followButton.SetAnchorsPreset(LayoutPreset.TopRight);
        _followButton.Position = new Vector2(-130, 8);
        _followButton.Toggled += on => _follow = on;
        AddChild(_followButton);
        Iso.Aim(_camera, _target);
    }

    public Camera3D Camera => _camera;
    public Timeline? Timeline => _timeline;
    public double Playhead => _playhead;

    /// <summary>Show <paramref name="timeline"/> at <paramref name="playhead"/> (fractional ticks).</summary>
    public void Display(Timeline? timeline, double playhead)
    {
        if (!ReferenceEquals(timeline, _timeline))
        {
            _timeline = timeline;
            Rebuild();
        }
        _playhead = playhead;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (_timeline is null) return;
        var tick = (int)Math.Floor(_playhead);
        var frame = _timeline.At(tick);
        if (tick != _fogTick) UpdateFog(frame);
        UpdateEntities(delta);
        UpdateEffects();
        UpdateMarks(frame);
        foreach (var (light, phase) in _torches)
        {
            light.LightEnergy = 1.3f + 0.25f * Mathf.Sin((float)_time * 9f + phase) + 0.15f * Mathf.Sin((float)_time * 23f + phase * 2);
        }
        var desired = _follow ? PartyCenter() ?? _target : _target;
        _target = _target.Lerp(desired, (float)Math.Min(1, delta * 4));
        _camera.Size = Mathf.Lerp(_camera.Size, _zoom, (float)Math.Min(1, delta * 8));
        Iso.Aim(_camera, _target);
        _overlay.QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _zoom = Mathf.Clamp(_zoom * 0.88f, 5f, 34f);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _zoom = Mathf.Clamp(_zoom / 0.88f, 5f, 34f);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right or MouseButton.Middle } b:
                _dragging = b.Pressed;
                AcceptEvent();
                break;
            case InputEventMouseMotion m when _dragging:
                Follow = false;
                var unitsPerPixel = _camera.Size / Math.Max(1, Size.Y);
                _target += Iso.GroundPan(_camera, m.Relative, unitsPerPixel * 1.4f);
                AcceptEvent();
                break;
        }
    }

    // ----- Level -----

    private static Vector3 W(Pos p, float y = 0) => new(p.X, y, p.Y);

    private void Rebuild()
    {
        foreach (var n in _level.GetChildren()) n.QueueFree();
        foreach (var n in _entities.GetChildren()) n.QueueFree();
        foreach (var n in _fx.GetChildren()) n.QueueFree();
        _hideInFog.Clear();
        _chests.Clear();
        _traps.Clear();
        _veins.Clear();
        _torches.Clear();
        _golems.Clear();
        _monsters.Clear();
        _coinPool.Clear();
        _sparkPool.Clear();
        _beamPool.Clear();
        _ringPool.Clear();
        _shields.Clear();
        _markPool.Clear();
        _threadPool.Clear();
        _fogTick = -1;
        if (_timeline is null) return;

        var w = _timeline.Final;
        var grid = w.Grid;
        bool Floor(int x, int y) => grid.InBounds(new Pos(x, y)) && !grid.IsWall(x, y);
        bool NearFloor(int x, int y)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (Floor(x + dx, y + dy)) return true;
                }
            }
            return false;
        }

        _floorIndex = new int[grid.Width * grid.Height];
        _wallIndex = new int[grid.Width * grid.Height];
        Array.Fill(_floorIndex, -1);
        Array.Fill(_wallIndex, -1);
        var floors = new List<Pos>();
        var walls = new List<Pos>();
        for (var y = 0; y < grid.Height; y++)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                if (Floor(x, y)) floors.Add(new Pos(x, y));
                else if (NearFloor(x, y)) walls.Add(new Pos(x, y));
            }
        }

        _floorMm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new BoxMesh { Size = new Vector3(1, 0.2f, 1) } };
        _floorMm.InstanceCount = floors.Count;
        for (var i = 0; i < floors.Count; i++) _floorIndex[grid.Idx(floors[i])] = i;
        _level.AddChild(new MultiMeshInstance3D
        {
            Multimesh = _floorMm,
            MaterialOverride = Iso.Flagstone(new Color("6b6259"), new Color("3a332d"), 0.55f, "dungeon-floor"),
        });

        _wallMm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new BoxMesh { Size = Vector3.One } };
        _wallMm.InstanceCount = walls.Count;
        _capMm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new BoxMesh { Size = new Vector3(1.02f, 0.08f, 1.02f) } };
        _capMm.InstanceCount = walls.Count;
        _wallHeight = new float[walls.Count];
        for (var i = 0; i < walls.Count; i++)
        {
            var p = walls[i];
            _wallIndex[grid.Idx(p)] = i;
            // The camera looks from +X/+Z, so walls on that side of a floor would hide it: cut them down.
            var front = Floor(p.X - 1, p.Y) || Floor(p.X, p.Y - 1) || Floor(p.X - 1, p.Y - 1);
            _wallHeight[i] = front ? 0.32f : WallHeight * (1f + (Iso.Hash(p.X, p.Y, 3) - 0.5f) * 0.12f);
        }
        _level.AddChild(new MultiMeshInstance3D
        {
            Multimesh = _wallMm,
            MaterialOverride = Iso.Flagstone(new Color("6a5f55"), new Color("2e2823"), 0.8f, "dungeon-wall"),
        });
        _level.AddChild(new MultiMeshInstance3D
        {
            Multimesh = _capMm,
            MaterialOverride = Iso.Flagstone(new Color("8f8374"), new Color("4a4038"), 0.9f, "dungeon-cap"),
        });

        var mortar = Iso.Solid(new Color("2a2420"));
        var sconce = new StandardMaterial3D { AlbedoColor = new Color("5a4632"), Metallic = 0.6f, Roughness = 0.5f };
        var flame = Iso.Glow(new Color("ffa640"), 5f);
        foreach (var p in walls)
        {
            if (Iso.Hash(p.X, p.Y, 7) > 0.07f) continue;
            var face = new[] { (0, 1), (1, 0) }.FirstOrDefault(d => Floor(p.X + d.Item1, p.Y + d.Item2));
            if (face == default) continue;
            var node = new Node3D { Position = W(p) + new Vector3(face.Item1 * 0.52f, 0.85f, face.Item2 * 0.52f) };
            Iso.Box(node, new Vector3(0.1f, 0.25f, 0.1f), Vector3.Zero, sconce);
            Iso.Ball(node, 0.07f, new Vector3(0, 0.18f, 0), flame, new Vector3(1, 1.6f, 1));
            var light = Iso.Light(node, new Vector3(face.Item1 * 0.3f, 0.3f, face.Item2 * 0.3f), new Color(1f, 0.62f, 0.3f), 1.3f, 4.5f);
            _torches.Add((light, Iso.Hash(p.X, p.Y, 9) * 10));
            _level.AddChild(node);
            _hideInFog.Add((node, grid.Idx(p)));
        }
        _ = mortar;

        BuildStairs(w.Stairs);
        foreach (var c in w.Chests) BuildChest(c.Pos);
        foreach (var t in w.Traps) BuildTrap(t.Pos, t.Def.Id);
        foreach (var v in w.Veins) BuildVein(v, Floor, _wallHeight[_wallIndex[grid.Idx(v.Pos)]]);

        foreach (var g in _timeline.Frames[0].Golems)
        {
            var fig = Figures.Golem(g.Chassis);
            fig.Root.Position = W(g.Pos);
            fig.Root.Scale = Vector3.One * FigureScale;
            _entities.AddChild(fig.Root);
            _golems[g.Id] = fig;
        }
        foreach (var m in _timeline.Frames[0].Monsters)
        {
            var fig = Figures.Monster(m.DefId);
            fig.Root.Position = W(m.Pos);
            fig.Root.Scale = Vector3.One * FigureScale;
            fig.Root.Visible = false;
            _entities.AddChild(fig.Root);
            _monsters[m.Id] = fig;
        }

        _zoom = Mathf.Clamp(Math.Max(grid.Width, grid.Height) * 0.35f, 5f, 6.5f);
        _camera.Size = _zoom;
        _target = PartyCenter() ?? W(w.Start);
        Follow = true;
    }

    private void BuildStairs(Pos p)
    {
        var node = new Node3D { Position = W(p) };
        var dark = Iso.Solid(new Color("050507"));
        var step = Iso.Flagstone(new Color("6b6259"), new Color("3a332d"), 0.55f, "dungeon-floor");
        Iso.Box(node, new Vector3(0.9f, 0.02f, 0.9f), new Vector3(0, 0.1f, 0), dark);
        for (var i = 0; i < 3; i++) Iso.Box(node, new Vector3(0.8f, 0.12f, 0.24f), new Vector3(0, 0.02f - i * 0.12f, -0.3f + i * 0.25f), step);
        var rune = Iso.Glow(new Color("5dd3e8"), 3f);
        var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.42f, OuterRadius = 0.48f, Rings = 24, RingSegments = 4 }, Position = new Vector3(0, 0.12f, 0), MaterialOverride = rune };
        node.AddChild(ring);
        Iso.Light(node, new Vector3(0, 0.8f, 0), new Color("5dd3e8"), 1.4f, 3.5f);
        _level.AddChild(node);
        _hideInFog.Add((node, _timeline!.Final.Grid.Idx(p)));
    }

    private void BuildChest(Pos p)
    {
        var node = new Node3D { Position = W(p, 0.1f), RotationDegrees = new Vector3(0, Iso.Hash(p.X, p.Y) * 40 - 20, 0) };
        var wood = Iso.Wood(new Color("8a5a2e"), new Color("4a2e16"), "chest-wood");
        var brass = new StandardMaterial3D { AlbedoColor = new Color("d4a24a"), Metallic = 0.8f, Roughness = 0.35f };
        Iso.Box(node, new Vector3(0.62f, 0.32f, 0.42f), new Vector3(0, 0.16f, 0), wood);
        Iso.Box(node, new Vector3(0.64f, 0.05f, 0.44f), new Vector3(0, 0.3f, 0), brass);
        var hinge = new Node3D { Position = new Vector3(0, 0.32f, -0.21f) };
        node.AddChild(hinge);
        var lid = Iso.Box(hinge, new Vector3(0.62f, 0.16f, 0.42f), new Vector3(0, 0.08f, 0.21f), wood);
        Iso.Box(lid, new Vector3(0.1f, 0.12f, 0.03f), new Vector3(0, -0.04f, 0.22f), brass);
        var glint = Iso.Light(node, new Vector3(0, 0.7f, 0.3f), new Color("ffd27a"), 0.5f, 2f);
        _level.AddChild(node);
        _chests.Add((node, hinge, glint));
        _hideInFog.Add((node, _timeline!.Final.Grid.Idx(p)));
    }

    /// <summary>Stone, ore or timber chunks on the faces of a wall tile that touch the floor; one chunk disappears per unit mined.</summary>
    private void BuildVein(Vein v, Func<int, int, bool> floor, float height)
    {
        var node = new Node3D { Position = W(v.Pos) };
        var (main, accent) = Resources.Mined[v.Kind] switch
        {
            Resources.Ore => (Iso.Rock(new Color("b0603a"), new Color("5a2a18"), 0.6f, "vein-ore"), Iso.Glow(new Color("ff9a50"), 1.6f)),
            Resources.Wood => (Iso.Wood(new Color("8a5a30"), new Color("43291a"), "vein-timber"), Iso.Wood(new Color("6a4424"), new Color("2e1c10"), "vein-timber-dark")),
            Resources.Crystal => (Iso.Glow(new Color("7ab8ff"), 1.8f), Iso.Glow(new Color("d0a0ff"), 3.2f)),
            _ => (Iso.Rock(new Color("c8c2b8"), new Color("7a746a"), 0.5f, "vein-stone"), Iso.Solid(new Color("e8e4dc"), 0.4f)),
        };
        var faces = Dirs.All.Where(d => floor(v.Pos.Step(d).X, v.Pos.Step(d).Y)).ToList();
        var chunks = new List<Node3D>();
        var h = Math.Max(0.3f, height);
        var count = Math.Max(4, v.Amount * 3);
        for (var i = 0; i < count; i++)
        {
            var d = faces.Count == 0 ? Dir.South : faces[i % faces.Count];
            var step = v.Pos.Step(d);
            var n = new Vector3(step.X - v.Pos.X, 0, step.Y - v.Pos.Y);
            var side = new Vector3(-n.Z, 0, n.X);
            var along = (Iso.Hash(v.Pos.X + i, v.Pos.Y, 41) - 0.5f) * 0.7f;
            var up = 0.12f + Iso.Hash(v.Pos.X, v.Pos.Y + i, 42) * (h - 0.2f);
            var at = n * 0.5f + side * along + Vector3.Up * up;
            Node3D chunk;
            if (i % 3 == 0)
            {
                var top = new Vector3((Iso.Hash(i, v.Pos.Y, 45) - 0.5f) * 0.6f, h + 0.06f, (Iso.Hash(v.Pos.X, i, 46) - 0.5f) * 0.6f);
                chunk = Resources.Mined[v.Kind] == Resources.Wood
                    ? Iso.Box(node, new Vector3(0.5f, 0.12f, 0.12f), top, main, new Vector3(0, Iso.Hash(i, 3, 44) * 180, 0))
                    : Iso.Box(node, Vector3.One * (0.14f + Iso.Hash(i, v.Pos.Y, 47) * 0.08f), top, i % 2 == 0 ? accent : main, new Vector3(Iso.Hash(i, 1, 44) * 60, Iso.Hash(i, 2, 44) * 90, 45));
            }
            else if (Resources.Mined[v.Kind] == Resources.Wood)
            {
                chunk = Iso.Box(node, new Vector3(0.14f, Math.Min(h, 0.9f), 0.14f), n * 0.5f + side * (i % 2 == 0 ? -0.3f : 0.3f) + Vector3.Up * Math.Min(h, 0.9f) / 2, i % 3 == 2 ? accent : main);
                if (i % 2 == 1) chunk = Iso.Box(node, new Vector3(0.14f, 0.12f, 0.14f) + side.Abs() * 0.6f, n * 0.52f + Vector3.Up * (Math.Min(h, 0.9f) - 0.06f), main);
            }
            else
            {
                var s = 0.09f + Iso.Hash(i, v.Pos.X, 43) * 0.08f;
                chunk = Iso.Box(node, new Vector3(s, s, s), at, i % 3 == 0 ? accent : main, new Vector3(Iso.Hash(i, 1, 44) * 60, Iso.Hash(i, 2, 44) * 90, 45));
            }
            chunks.Add(chunk);
        }
        if (Resources.Mined[v.Kind] == Resources.Crystal)
        {
            var glow = Iso.Light(node, new Vector3(0, 0.6f, 0), new Color("a0b8ff"), 0.9f, 2.6f);
            chunks.Insert(0, glow);
        }
        _level.AddChild(node);
        _veins.Add((chunks, v.Amount));
        _hideInFog.Add((node, _timeline!.Final.Grid.Idx(v.Pos)));
    }

    private void BuildTrap(Pos p, string id)
    {
        var node = new Node3D { Position = W(p, 0.1f), Visible = false };
        var plate = Iso.Solid(new Color("3c3a38"), 0.6f, 0.5f);
        Iso.Box(node, new Vector3(0.8f, 0.03f, 0.8f), new Vector3(0, 0.015f, 0), plate);
        var tip = id switch
        {
            "fire_vent" => Iso.Glow(new Color("ff6a20"), 2f),
            "snare" => Iso.Solid(new Color("8a7a50")),
            _ => Iso.Solid(new Color("b0b4ba"), 0.3f, 0.8f),
        };
        for (var i = 0; i < 4; i++)
        {
            var spike = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = 0.06f, Height = 0.22f, RadialSegments = 4 },
                Position = new Vector3((i % 2 - 0.5f) * 0.36f, 0.11f, (i / 2 - 0.5f) * 0.36f),
                MaterialOverride = tip,
            };
            node.AddChild(spike);
        }
        _level.AddChild(node);
        _traps.Add(node);
    }

    private void UpdateFog(TickFrame f)
    {
        _fogTick = f.Tick;
        var w = _timeline!.Final;
        var grid = w.Grid;
        var hidden = new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero);
        for (var idx = 0; idx < f.Fog.Length; idx++)
        {
            var x = idx % grid.Width;
            var y = idx / grid.Width;
            var fog = f.Fog[idx];
            var shade = 0.85f + Iso.Hash(x, y, 1) * 0.3f;
            var color = fog == 2 ? new Color(shade, shade, shade) : new Color(0.42f * shade, 0.45f * shade, 0.58f * shade);
            if (_floorIndex[idx] is var fi and >= 0)
            {
                var jitter = (Iso.Hash(x, y, 2) - 0.5f) * 0.04f;
                _floorMm!.SetInstanceTransform(fi, fog == 0 ? hidden : new Transform3D(Basis.Identity, new Vector3(x, jitter, y)));
                _floorMm.SetInstanceColor(fi, color);
            }
            if (_wallIndex[idx] is var wi and >= 0)
            {
                var h = _wallHeight[wi];
                _wallMm!.SetInstanceTransform(wi, fog == 0 ? hidden : new Transform3D(Basis.Identity.Scaled(new Vector3(1, h, 1)), new Vector3(x, h / 2, y)));
                _wallMm.SetInstanceColor(wi, color);
                _capMm!.SetInstanceTransform(wi, fog == 0 ? hidden : new Transform3D(Basis.Identity, new Vector3(x, h + 0.04f, y)));
                _capMm.SetInstanceColor(wi, color);
            }
        }
        foreach (var (node, tile) in _hideInFog) node.Visible = f.Fog[tile] > 0;
        for (var i = 0; i < _chests.Count; i++)
        {
            var open = f.ChestsOpened[i];
            _chests[i].Lid.RotationDegrees = new Vector3(open ? -105 : 0, 0, 0);
            _chests[i].Glint.Visible = !open;
        }
        for (var i = 0; i < _veins.Count && f.VeinsLeft is { } left && i < left.Length; i++)
        {
            var (chunks, amount) = _veins[i];
            var keep = amount == 0 ? 0 : (int)Math.Ceiling(chunks.Count * left[i] / (double)amount);
            for (var c = 0; c < chunks.Count; c++) chunks[c].Visible = c < keep;
        }
        for (var i = 0; i < _traps.Count; i++)
        {
            var t = w.Traps[i];
            _traps[i].Visible = f.TrapsRevealed[i] && f.Fog[grid.Idx(t.Pos)] > 0;
        }
        foreach (var c in _coinPool) c.Visible = false;
        var used = 0;
        foreach (var d in f.Drops)
        {
            if (f.Fog[grid.Idx(d.Pos)] == 0) continue;
            if (used == _coinPool.Count) _coinPool.Add(MakeCoins());
            var coins = _coinPool[used++];
            coins.Position = W(d.Pos, 0.1f);
            coins.Visible = true;
        }
    }

    private static Color ScentColor(string label)
    {
        var h = 0;
        foreach (var ch in label) h = h * 31 + ch;
        return ScentColors[Math.Abs(h % ScentColors.Length)];
    }

    /// <summary>
    /// Marks glow as runes on the floor. Marks with the same label are joined, oldest to newest,
    /// by a thread of light that drifts along the trail: the scent smell() follows.
    /// </summary>
    private void UpdateMarks(TickFrame f)
    {
        var marks = f.Marks ?? [];
        var grid = _timeline!.Final.Grid;
        var time = (float)_time;
        var used = 0;
        var dots = 0;
        foreach (var m in marks)
        {
            if (f.Fog[grid.Idx(m.Pos)] == 0) continue;
            var color = ScentColor(m.Label);
            var fade = Math.Clamp(1 - (f.Tick - m.CreatedTick) / MarkLife, 0.25f, 1f);
            var rune = Pooled(_markPool, ref used, () => new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = 0.2f, OuterRadius = 0.27f, Rings = 16, RingSegments = 4 },
                MaterialOverride = Iso.Glow(Colors.White, 3f),
            });
            rune.Position = W(m.Pos, 0.13f);
            rune.Rotation = new Vector3(0, time * 0.8f + m.CreatedTick, 0);
            rune.Scale = Vector3.One * (0.9f + 0.1f * Mathf.Sin(time * 3 + m.CreatedTick));
            var mat = (StandardMaterial3D)rune.MaterialOverride;
            mat.Emission = color;
            mat.AlbedoColor = color;
            mat.EmissionEnergyMultiplier = 3f * fade;
        }
        foreach (var trail in marks.GroupBy(m => m.Label))
        {
            var list = trail.OrderBy(m => m.CreatedTick).ToList();
            var color = ScentColor(trail.Key);
            for (var i = 0; i + 1 < list.Count; i++)
            {
                var (a, b) = (list[i], list[i + 1]);
                if (f.Fog[grid.Idx(a.Pos)] == 0 || f.Fog[grid.Idx(b.Pos)] == 0) continue;
                var from = W(a.Pos, 0.3f);
                var to = W(b.Pos, 0.3f);
                var count = Math.Clamp((int)(from.DistanceTo(to) * 4), 2, 40);
                for (var k = 0; k < count; k++)
                {
                    var s = (k + time * 1.2f % 1f) / count % 1f;
                    var dot = Pooled(_threadPool, ref dots, () => new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.05f, 0.05f) },
                        MaterialOverride = Iso.Glow(color, 2.5f),
                    });
                    dot.Position = from.Lerp(to, s) + new Vector3(0, 0.06f * Mathf.Sin(s * Mathf.Tau * 2 + time * 3), 0);
                    dot.Scale = Vector3.One * (0.6f + 0.6f * Mathf.Sin(s * Mathf.Pi));
                    ((StandardMaterial3D)dot.MaterialOverride).Emission = color;
                    ((StandardMaterial3D)dot.MaterialOverride).AlbedoColor = color;
                }
            }
        }
        for (var i = used; i < _markPool.Count; i++) _markPool[i].Visible = false;
        for (var i = dots; i < _threadPool.Count; i++) _threadPool[i].Visible = false;
    }

    private Node3D MakeCoins()
    {
        var node = new Node3D();
        var gold = new StandardMaterial3D { AlbedoColor = new Color("ffcc4d"), Metallic = 0.9f, Roughness = 0.25f, EmissionEnabled = true, Emission = new Color("ffaa00"), EmissionEnergyMultiplier = 0.4f };
        for (var i = 0; i < 4; i++)
        {
            Iso.Cylinder(node, 0.07f, 0.07f, 0.03f, new Vector3((i % 2) * 0.1f - 0.05f, 0.02f + i * 0.02f, (i / 2) * 0.08f - 0.04f), gold, 8);
        }
        _fx.AddChild(node);
        return node;
    }

    // ----- Entities -----

    private (TickFrame A, TickFrame B, float T) Frames()
    {
        var tick = (int)Math.Floor(_playhead);
        var a = _timeline!.At(tick);
        var b = _timeline.At(tick + 1);
        return (a, b, (float)(_playhead - tick));
    }

    private static float Yaw(Vector3 dir) => Mathf.Atan2(dir.X, dir.Z);

    private static float FacingYaw(Dir d) => d switch
    {
        Dir.North => Mathf.Pi,
        Dir.East => Mathf.Pi / 2,
        Dir.South => 0,
        _ => -Mathf.Pi / 2,
    };

    private Vector3? PartyCenter()
    {
        if (_timeline is null) return null;
        var (a, b, t) = Frames();
        var pts = new List<Vector3>();
        foreach (var g in a.Golems)
        {
            if (g.State is GolemState.Recalled or GolemState.Descended) continue;
            var gb = b.Golems.FirstOrDefault(x => x.Id == g.Id) ?? g;
            pts.Add(W(g.Pos).Lerp(W(gb.Pos), t));
        }
        if (pts.Count == 0) return null;
        return pts.Aggregate(Vector3.Zero, (s, p) => s + p) / pts.Count;
    }

    /// <summary>Attack lunges and hit flashes active at the current playhead, by tile.</summary>
    private (Dictionary<Pos, Vector3> Lunge, Dictionary<Pos, float> Flash) Reactions()
    {
        var lunge = new Dictionary<Pos, Vector3>();
        var flash = new Dictionary<Pos, float>();
        var tick = (int)Math.Floor(_playhead);
        for (var k = Math.Max(0, tick - 3); k <= tick; k++)
        {
            var age = (float)(_playhead - k);
            if (_timeline!.At(k).Effects is not { } fx) continue;
            foreach (var e in fx)
            {
                if (e.Kind == EffectKind.Attack && age < 3)
                {
                    var dir = (W(e.To) - W(e.From)).Normalized();
                    lunge[e.From] = dir * 0.32f * Mathf.Sin(Mathf.Pi * age / 3);
                }
                else if (e.Kind == EffectKind.Hit && age < 2.5f)
                {
                    flash[e.From] = Math.Max(flash.GetValueOrDefault(e.From), 1 - age / 2.5f);
                }
            }
        }
        return (lunge, flash);
    }

    private void UpdateEntities(double delta)
    {
        var (a, b, t) = Frames();
        var (lunge, flash) = Reactions();
        var fog = a.Fog;
        var grid = _timeline!.Final.Grid;
        var time = (float)_time;

        foreach (var g in a.Golems)
        {
            if (!_golems.TryGetValue(g.Id, out var fig)) continue;
            var gb = b.Golems.FirstOrDefault(x => x.Id == g.Id) ?? g;
            var gone = g.State is GolemState.Recalled or GolemState.Descended;
            fig.Root.Visible = !gone;
            if (gone) continue;
            var pos = W(g.Pos).Lerp(W(gb.Pos), t) + lunge.GetValueOrDefault(g.Pos);
            var moving = g.Pos != gb.Pos;
            fig.Root.Position = pos;
            fig.Root.Rotation = new Vector3(0, Mathf.LerpAngle(fig.Root.Rotation.Y, FacingYaw(gb.Facing), (float)Math.Min(1, delta * 12)), 0);
            var broken = g.State == GolemState.Broken;
            fig.Body.Rotation = broken ? new Vector3(Mathf.Pi / 2.2f, 0, 0.3f) : Vector3.Zero;
            fig.Body.Position = new Vector3(0, broken ? 0.15f : 0.1f + (moving ? Mathf.Abs(Mathf.Sin(t * Mathf.Pi)) * 0.06f : 0.012f * Mathf.Sin(time * 2 + g.Id)), 0);
            var swing = moving ? Mathf.Sin(t * Mathf.Pi * 2) * 0.5f : 0;
            if (fig.LeftArm is not null) fig.LeftArm.Rotation = new Vector3(swing, 0, 0);
            if (fig.RightArm is not null) fig.RightArm.Rotation = new Vector3(lunge.ContainsKey(g.Pos) ? -1.2f : -swing, 0, 0);
            if (fig.Core is not null)
            {
                var f = flash.GetValueOrDefault(g.Pos);
                var pulse = g.State == GolemState.Active ? 2.2f + Math.Min(3f, g.Ops / 12f) + Mathf.Sin(time * 6) * 0.3f : 0.4f;
                if (g.State == GolemState.Halted) pulse = (Mathf.Sin(time * 10) > 0) ? 2.5f : 0.2f;
                fig.Core.Emission = broken ? new Color("333333") : g.State == GolemState.Halted ? new Color("f07178") : fig.Accent.Lerp(Colors.White, f * 0.8f);
                fig.Core.EmissionEnergyMultiplier = broken ? 0 : pulse + f * 4;
            }
            if (fig.Light is not null) fig.Light.Visible = !broken;
            UpdateShield(g, fig);
        }

        foreach (var m in a.Monsters)
        {
            if (!_monsters.TryGetValue(m.Id, out var fig)) continue;
            var mb = b.Monsters.FirstOrDefault(x => x.Id == m.Id) ?? m;
            var visible = m.Alive && fog[grid.Idx(m.Pos)] == 2;
            fig.Root.Visible = visible;
            if (!visible) continue;
            var pos = W(m.Pos).Lerp(W(mb.Pos), t) + lunge.GetValueOrDefault(m.Pos);
            fig.Root.Position = pos;
            var dir = W(mb.Pos) - W(m.Pos);
            var nearest = a.Golems.Where(g => g.State == GolemState.Active).OrderBy(g => g.Pos.Manhattan(m.Pos)).FirstOrDefault();
            if (dir.LengthSquared() > 0.01f) fig.Root.Rotation = new Vector3(0, Yaw(dir), 0);
            else if (nearest is not null && nearest.Pos.Manhattan(m.Pos) <= 2) fig.Root.Rotation = new Vector3(0, Mathf.LerpAngle(fig.Root.Rotation.Y, Yaw(W(nearest.Pos) - W(m.Pos)), 0.2f), 0);
            var phase = m.Id * 1.7f;
            switch (m.DefId)
            {
                case "slime":
                    var squash = 1 + 0.12f * Mathf.Sin(time * 4 + phase) + (dir.LengthSquared() > 0 ? 0.2f * Mathf.Sin(t * Mathf.Pi) : 0);
                    fig.Body.Scale = new Vector3(1 / Mathf.Sqrt(squash), squash, 1 / Mathf.Sqrt(squash));
                    fig.Body.Position = new Vector3(0, 0.1f, 0);
                    break;
                case "bat":
                    fig.Body.Position = new Vector3(0, 0.1f + 0.12f * Mathf.Sin(time * 5 + phase), 0);
                    var flap = Mathf.Sin(time * 22 + phase) * 0.8f;
                    if (fig.LeftWing is not null) fig.LeftWing.Rotation = new Vector3(0, 0, flap);
                    if (fig.RightWing is not null) fig.RightWing.Rotation = new Vector3(0, 0, -flap);
                    break;
                case "wisp":
                    fig.Body.Position = new Vector3(Mathf.Sin(time * 1.7f + phase) * 0.06f, 0.15f * Mathf.Sin(time * 2.3f + phase), 0);
                    fig.Body.Scale = Vector3.One * (1 + 0.08f * Mathf.Sin(time * 7 + phase));
                    if (fig.LeftWing is not null) fig.LeftWing.Rotation = new Vector3(0, Mathf.Sin(time * 3 + phase) * 0.6f, 0);
                    if (fig.Light is not null) fig.Light.LightEnergy = 1.2f + 0.4f * Mathf.Sin(time * 5 + phase);
                    break;
                case "mimic":
                    var moved = dir.LengthSquared() > 0;
                    var awake = moved || m.Intent == IntentKind.Attack || nearest is not null && nearest.Pos.Manhattan(m.Pos) <= 1;
                    fig.Body.Position = new Vector3(0, 0.1f + (moved ? Mathf.Abs(Mathf.Sin(t * Mathf.Pi * 2)) * 0.12f : 0), 0);
                    var chomp = lunge.ContainsKey(m.Pos) ? -1.1f : awake ? -0.35f - 0.25f * Mathf.Abs(Mathf.Sin(time * 9 + phase)) : 0;
                    if (fig.RightArm is not null) fig.RightArm.Rotation = new Vector3(chomp, 0, 0);
                    if (fig.LeftArm is not null) fig.LeftArm.Visible = awake;
                    break;
                case "spider":
                    fig.Body.Position = new Vector3(0, 0.08f + (dir.LengthSquared() > 0 ? Mathf.Abs(Mathf.Sin(t * Mathf.Pi * 4)) * 0.03f : 0), 0);
                    var skitter = dir.LengthSquared() > 0 ? Mathf.Sin(t * Mathf.Pi * 6) * 0.35f : Mathf.Sin(time * 3 + phase) * 0.05f;
                    if (fig.LeftArm is not null) fig.LeftArm.Rotation = new Vector3(0, skitter, 0);
                    if (fig.RightArm is not null) fig.RightArm.Rotation = new Vector3(0, -skitter, 0);
                    break;
                default:
                    fig.Body.Position = new Vector3(0, 0.1f + (dir.LengthSquared() > 0 ? Mathf.Abs(Mathf.Sin(t * Mathf.Pi)) * 0.05f : 0), 0);
                    var sw = dir.LengthSquared() > 0 ? Mathf.Sin(t * Mathf.Pi * 2) * 0.5f : Mathf.Sin(time * 1.5f + phase) * 0.08f;
                    if (fig.LeftArm is not null) fig.LeftArm.Rotation = new Vector3(sw, 0, 0);
                    if (fig.RightArm is not null) fig.RightArm.Rotation = new Vector3(lunge.ContainsKey(m.Pos) ? -1.3f : -sw, 0, 0);
                    break;
            }
            if (fig.Core is not null)
            {
                var f = flash.GetValueOrDefault(m.Pos);
                fig.Core.EmissionEnergyMultiplier = (m.Intent == IntentKind.Attack ? 5f : 2.5f) + f * 5;
            }
        }
    }

    private void UpdateShield(GolemFrame g, Figure fig)
    {
        var on = g.ShieldTicks > 0 && g.State == GolemState.Active;
        if (!_shields.TryGetValue(g.Id, out var bubble))
        {
            if (!on) return;
            bubble = Iso.Ball(fig.Root, 0.62f, new Vector3(0, 0.55f, 0), Iso.Translucent(new Color(0.4f, 0.7f, 1f, 0.22f), 1.2f));
            _shields[g.Id] = bubble;
        }
        bubble.Visible = on;
        if (on) bubble.Scale = Vector3.One * (1 + 0.04f * Mathf.Sin((float)_time * 5));
    }

    // ----- Effects -----

    private MeshInstance3D Pooled(List<MeshInstance3D> pool, ref int used, Func<MeshInstance3D> make)
    {
        if (used == pool.Count)
        {
            var m = make();
            _fx.AddChild(m);
            pool.Add(m);
        }
        var mi = pool[used++];
        mi.Visible = true;
        return mi;
    }

    private void Spark(Vector3 at, Color color, float age, int index, float spread, float rise)
    {
        var mi = Pooled(_sparkPool, ref _sparksUsed, () => new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.07f, 0.07f, 0.07f) },
            MaterialOverride = Iso.Glow(Colors.White, 3f),
        });
        var angle = Iso.Hash(index, (int)(at.X * 7 + at.Z * 13), 5) * Mathf.Tau;
        var speed = 0.5f + Iso.Hash(index, 3, 6) * 0.8f;
        var tt = age / EffectWindow;
        mi.Position = at + new Vector3(Mathf.Cos(angle) * spread * speed * tt, 0.3f + rise * tt - 1.5f * tt * tt, Mathf.Sin(angle) * spread * speed * tt);
        mi.Scale = Vector3.One * Math.Max(0.05f, 1 - tt);
        ((StandardMaterial3D)mi.MaterialOverride).Emission = color;
        ((StandardMaterial3D)mi.MaterialOverride).AlbedoColor = color;
    }

    private void UpdateEffects()
    {
        _sparksUsed = _beamsUsed = _ringsUsed = 0;
        var tick = (int)Math.Floor(_playhead);
        for (var k = Math.Max(0, tick - EffectWindow + 1); k <= tick; k++)
        {
            if (_timeline!.At(k).Effects is not { Count: > 0 } fx) continue;
            var age = (float)(_playhead - k);
            foreach (var e in fx)
            {
                var at = W(e.From);
                switch (e.Kind)
                {
                    case EffectKind.Bolt when age < 3:
                        var beam = Pooled(_beamPool, ref _beamsUsed, () => new MeshInstance3D
                        {
                            Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 1, RadialSegments = 6 },
                            MaterialOverride = Iso.Glow(new Color("c792ea"), 6f),
                        });
                        var from = at + Vector3.Up * 0.6f;
                        var to = W(e.To) + Vector3.Up * 0.5f;
                        var mid = from.Lerp(to, 0.5f);
                        var len = from.DistanceTo(to);
                        beam.Position = mid;
                        beam.Basis = Basis.LookingAt((to - from).Normalized(), Vector3.Up) * Basis.FromEuler(new Vector3(Mathf.Pi / 2, 0, 0));
                        beam.Scale = new Vector3(1 - age / 3, len, 1 - age / 3);
                        for (var i = 0; i < 6; i++) Spark(W(e.To), new Color("c792ea"), age * 2.5f, i, 1.2f, 0.8f);
                        break;
                    case EffectKind.Heal:
                        for (var i = 0; i < 8; i++) Spark(at, new Color("7bd88f"), age, i, 0.5f, 1.6f);
                        break;
                    case EffectKind.Reveal:
                    {
                        var ring = Pooled(_ringPool, ref _ringsUsed, () => new MeshInstance3D
                        {
                            Mesh = new TorusMesh { InnerRadius = 0.9f, OuterRadius = 1f, Rings = 32, RingSegments = 4 },
                            MaterialOverride = Iso.Translucent(new Color(0.6f, 0.9f, 1f, 0.6f), 3f),
                        });
                        ring.Position = at + Vector3.Up * 0.15f;
                        ring.Scale = Vector3.One * (0.3f + age / EffectWindow * Spells.RevealRadius);
                        break;
                    }
                    case EffectKind.Shield:
                        for (var i = 0; i < 6; i++) Spark(at, new Color("6fb6ff"), age, i, 0.8f, 1f);
                        break;
                    case EffectKind.Chest:
                        for (var i = 0; i < 10; i++) Spark(at, new Color("ffd24a"), age, i, 1f, 2.2f);
                        break;
                    case EffectKind.Mine when age < 4:
                    {
                        var face = at.Lerp(W(e.To), 0.45f);
                        for (var i = 0; i < 7; i++) Spark(face, i % 2 == 0 ? new Color("ffe0a0") : new Color("b4b0a8"), age * 1.5f, i, 0.6f, 1f);
                        break;
                    }
                    case EffectKind.Trap:
                        for (var i = 0; i < 6; i++) Spark(at, new Color("ff5050"), age, i, 0.7f, 1.2f);
                        break;
                    case EffectKind.Kill:
                        for (var i = 0; i < 10; i++) Spark(at, new Color("9a8c7e"), age, i, 1.1f, 1f);
                        break;
                    case EffectKind.Break:
                        for (var i = 0; i < 12; i++) Spark(at, new Color("f07178"), age, i, 1.2f, 1.4f);
                        break;
                    case EffectKind.Descend:
                    case EffectKind.Recall:
                    {
                        var col = Pooled(_beamPool, ref _beamsUsed, () => new MeshInstance3D
                        {
                            Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 1, RadialSegments = 6 },
                            MaterialOverride = Iso.Glow(new Color("c792ea"), 6f),
                        });
                        var color = e.Kind == EffectKind.Descend ? new Color("5dd3e8") : new Color("ffd24a");
                        ((StandardMaterial3D)col.MaterialOverride).Emission = color;
                        ((StandardMaterial3D)col.MaterialOverride).AlbedoColor = color;
                        col.Basis = Basis.Identity;
                        col.Position = at + Vector3.Up * 1.5f;
                        col.Scale = new Vector3(6 * (1 - age / EffectWindow), 3, 6 * (1 - age / EffectWindow));
                        for (var i = 0; i < 8; i++) Spark(at, color, age, i, 0.4f, 2.5f);
                        break;
                    }
                }
            }
        }
        for (var i = _sparksUsed; i < _sparkPool.Count; i++) _sparkPool[i].Visible = false;
        for (var i = _beamsUsed; i < _beamPool.Count; i++) _beamPool[i].Visible = false;
        for (var i = _ringsUsed; i < _ringPool.Count; i++) _ringPool[i].Visible = false;
    }

    // ----- Overlay data -----

    /// <summary>Screen position (in this control) of a world point, or null if off-screen.</summary>
    public Vector2? ToScreen(Vector3 world)
    {
        if (_camera.IsPositionBehind(world)) return null;
        return _camera.UnprojectPosition(world) * Iso.PixelScale;
    }

    internal IEnumerable<OverlayItem> OverlayItems()
    {
        if (_timeline is null) yield break;
        var (a, b, t) = Frames();
        var grid = _timeline.Final.Grid;
        foreach (var g in a.Golems)
        {
            if (g.State is GolemState.Recalled or GolemState.Descended || !_golems.TryGetValue(g.Id, out var fig)) continue;
            var gb = b.Golems.FirstOrDefault(x => x.Id == g.Id) ?? g;
            var at = W(g.Pos).Lerp(W(gb.Pos), t) + Vector3.Up * (fig.Height * FigureScale + 0.3f);
            if (ToScreen(at) is not { } s) continue;
            var status = new List<string>();
            if (g.StunTicks > 0) status.Add("stunned");
            if (g.BurnTicks > 0) status.Add("burning");
            if (g.SlowTicks > 0) status.Add("slowed");
            if (g.State == GolemState.Halted) status.Add("halted");
            if (g.State == GolemState.Broken) status.Add("broken");
            yield return new OverlayItem(s, g.Name, g.Hp, g.MaxHp, g.MaxMana > 0 ? g.Mana / (float)g.MaxMana : -1, fig.Accent, null, string.Join(" ", status), true);
        }
        foreach (var m in a.Monsters)
        {
            if (!m.Alive || a.Fog[grid.Idx(m.Pos)] != 2 || !_monsters.TryGetValue(m.Id, out var fig)) continue;
            var mb = b.Monsters.FirstOrDefault(x => x.Id == m.Id) ?? m;
            var at = W(m.Pos).Lerp(W(mb.Pos), t) + Vector3.Up * (fig.Height * FigureScale + 0.25f);
            if (ToScreen(at) is not { } s) continue;
            yield return new OverlayItem(s, m.Kind, m.Hp, m.MaxHp, -1, new Color("f07178"), m.Intent, "", false);
        }
    }

    /// <summary>Damage and healing numbers from HP changes in the last few ticks.</summary>
    internal IEnumerable<(Vector2 At, string Text, Color Color, float Age)> FloatingNumbers()
    {
        if (_timeline is null) yield break;
        var tick = (int)Math.Floor(_playhead);
        var grid = _timeline.Final.Grid;
        for (var k = Math.Max(1, tick - 9); k <= tick; k++)
        {
            var prev = _timeline.At(k - 1);
            var cur = _timeline.At(k);
            var age = (float)(_playhead - k) / 10f;
            foreach (var g in cur.Golems)
            {
                var p = prev.Golems.FirstOrDefault(x => x.Id == g.Id);
                if (p is null || p.Hp == g.Hp || g.State is GolemState.Recalled or GolemState.Descended) continue;
                if (ToScreen(W(g.Pos) + Vector3.Up * 1.6f) is { } s)
                {
                    yield return (s, g.Hp < p.Hp ? $"-{p.Hp - g.Hp}" : $"+{g.Hp - p.Hp}", g.Hp < p.Hp ? new Color("ff6b6b") : new Color("7bd88f"), age);
                }
            }
            foreach (var m in cur.Monsters)
            {
                var p = prev.Monsters.FirstOrDefault(x => x.Id == m.Id);
                if (p is null || p.Hp <= m.Hp || cur.Fog[grid.Idx(m.Pos)] != 2) continue;
                if (ToScreen(W(m.Pos) + Vector3.Up * 1.4f) is { } s) yield return (s, $"-{p.Hp - m.Hp}", new Color("ffd27a"), age);
            }
        }
    }
}

internal sealed record OverlayItem(Vector2 At, string Name, int Hp, int MaxHp, float Mana, Color Color, IntentKind? Intent, string Status, bool IsGolem);

/// <summary>Crisp full-resolution 2D drawing on top of the pixelated 3D view.</summary>
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
                if (item.Status.Length > 0) DrawString(font, new Vector2(x - 30, y - 22), item.Status, HorizontalAlignment.Center, w + 60, 11, new Color("f07178"));
            }
            DrawRect(new Rect2(x - 1, y - 1, w + 2, 7), new Color(0, 0, 0, 0.75f));
            var frac = item.MaxHp > 0 ? Math.Clamp(item.Hp / (float)item.MaxHp, 0, 1) : 0;
            var hpColor = item.IsGolem ? (frac > 0.3f ? new Color("7bd88f") : new Color("f07178")) : new Color("e0564f");
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
                    IntentKind.Flee => ("<<", new Color("7bd88f")),
                    IntentKind.Move => ("~", new Color("9a8c7e")),
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
