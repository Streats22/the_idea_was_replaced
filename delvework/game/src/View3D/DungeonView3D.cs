using Delvework.Core.Content;
using Delvework.Core.Replay;
using Delvework.Core.Sim;
using Godot;

namespace Delvework.Game.View3D;

/// <summary>Dungeon diorama driven by a timeline playhead; 2D overlay for bars and numbers.</summary>
public partial class DungeonView3D : Control
{
    private const float WallHeight = 1.25f;
    private const int EffectWindow = 8;
    private const float FigureScale = 1.25f;
    private const int ChunkSize = 8;
    private const float Pitch = 56f;
    private const float KenneyWall = 1.1f;
    private static readonly Color TorchColor = new(1f, 0.62f, 0.3f);

    private sealed class TileLayer
    {
        public readonly List<MultiMesh> Chunks = [];
        public int[] Chunk = [];
        public int[] Slot = [];

        public void Set(int idx, Transform3D transform, Color color)
        {
            if (Chunk.Length == 0 || Chunk[idx] < 0) return;
            var mm = Chunks[Chunk[idx]];
            mm.SetInstanceTransform(Slot[idx], transform);
            mm.SetInstanceColor(Slot[idx], color);
        }
    }

    private SubViewport _viewport = null!;
    private Camera3D _camera = null!;
    private DirectionalLight3D _moon = null!;
    private Godot.Environment _env = null!;
    private Node3D _level = null!, _entities = null!, _fx = null!;
    private DungeonOverlay _overlay = null!;
    private Button _followButton = null!;
    private OmniLightPool _torchPool = null!;

    private Timeline? _timeline;
    private double _playhead;
    private int _fogTick = -1;
    private TileLayer? _floors, _floorDetails, _walls, _caps;
    private bool _kenneyTiles;
    private float[] _wallHeight = [];
    private readonly List<(Node3D Node, int TileIdx)> _hideInFog = [];
    private readonly List<(Node3D Node, Node3D Lid, Node3D Glint)> _chests = [];
    private readonly List<Node3D> _traps = [];
    private readonly List<(List<Node3D> Chunks, int Amount)> _veins = [];
    private readonly List<(Node3D Node, OmniSpot Spot)> _torches = [];
    private readonly List<OmniSpot> _litTorches = [];
    private readonly Dictionary<int, Figure> _golems = [];
    private readonly Dictionary<int, Figure> _monsters = [];
    private readonly List<Node3D> _coinPool = [];
    private readonly List<Node3D> _tabletPool = [];
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
    private float _zoom = 10f, _shownZoom = 10f;
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
        var scene = Iso.Mount(this, new Color("0e1117"), new Color("7282a8"), 0.6f, new Color("8a98c8"), 0.35f, new Vector3(-58, 30, 0), 40f);
        _viewport = scene.Viewport;
        _camera = scene.Camera;
        _moon = scene.Sun;
        _env = scene.Env;
        _torchPool = new OmniLightPool(_viewport, TorchColor, 1.3f, 4.5f);
        _level = new Node3D { Name = "Level" };
        _entities = new Node3D { Name = "Entities" };
        _fx = new Node3D { Name = "Effects" };
        _viewport.AddChild(_level);
        _viewport.AddChild(_entities);
        _viewport.AddChild(_fx);
        Graphics.Changed += OnGraphicsChanged;

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
        _followButton.SetAnchorsPreset(LayoutPreset.BottomLeft);
        _followButton.OffsetLeft = 16;
        _followButton.OffsetTop = -148;
        _followButton.OffsetBottom = -114;
        _followButton.Toggled += on => _follow = on;
        AddChild(_followButton);
        Iso.Aim(_camera, _target, Distance(_zoom), Iso.Yaw, Pitch);
    }

    public override void _ExitTree() => Graphics.Changed -= OnGraphicsChanged;

    private void OnGraphicsChanged() => Graphics.Apply(_moon, _env, _viewport);

    private float Distance(float height) => height / (2f * Mathf.Tan(Mathf.DegToRad(_camera.Fov) / 2));

    public Camera3D Camera => _camera;
    public float ShiftPixels { get; set; }
    public Timeline? Timeline => _timeline;
    public double Playhead => _playhead;

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
        var desired = _follow ? PartyCenter() ?? _target : _target;
        _target = _target.Lerp(desired, (float)Math.Min(1, delta * 4));
        _shownZoom = Mathf.Lerp(_shownZoom, _zoom, (float)Math.Min(1, delta * 8));
        var dist = Distance(_shownZoom);
        Iso.Aim(_camera, _target, dist, Iso.Yaw, Pitch);
        var worldShift = Size.Y > 0 ? ShiftPixels * Iso.UnitsPerPixel(_camera, dist, Size.Y) : 0;
        _camera.HOffset = Mathf.Lerp(_camera.HOffset, worldShift, (float)Math.Min(1, delta * 6));
        UpdateTorchLights();
        foreach (var coins in _coinPool)
        {
            if (coins.Visible) coins.Rotation = new Vector3(0, (float)_time * 1.6f + coins.Position.X, 0);
        }
        foreach (var tablet in _tabletPool)
        {
            if (tablet.Visible) tablet.Rotation = new Vector3(0, 0.35f * Mathf.Sin((float)_time * 0.8f + tablet.Position.X), 0);
        }
        _overlay.QueueRedraw();
    }

    private void UpdateTorchLights()
    {
        _litTorches.Clear();
        foreach (var (node, spot) in _torches)
        {
            if (node.Visible) _litTorches.Add(spot);
        }
        _torchPool.AssignNearest(_litTorches, _target, 1f, (float)_time);
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
                var unitsPerPixel = Iso.UnitsPerPixel(_camera, Distance(_shownZoom), Size.Y);
                _target += Iso.GroundPan(_camera, m.Relative, unitsPerPixel * 1.2f);
                AcceptEvent();
                break;
        }
    }

    // ----- Level -----

    private static Vector3 W(Pos p, float y = 0) => new(p.X, y, p.Y);

    private static void FreeChildren(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.Free();
        }
    }

    private void Rebuild()
    {
        FreeChildren(_level);
        FreeChildren(_entities);
        FreeChildren(_fx);
        _hideInFog.Clear();
        _chests.Clear();
        _traps.Clear();
        _veins.Clear();
        _torches.Clear();
        _golems.Clear();
        _monsters.Clear();
        _coinPool.Clear();
        _tabletPool.Clear();
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

        var floorMesh = Kenney.Mesh(Kenney.Dungeon, "floor");
        var wallMesh = Kenney.Mesh(Kenney.Dungeon, "wall");
        var tileMaterial = Kenney.TileMaterial(Kenney.Dungeon, "floor");
        _kenneyTiles = floorMesh is not null && wallMesh is not null && tileMaterial is not null;

        var floors = new List<Pos>();
        var details = new List<Pos>();
        var walls = new List<Pos>();
        for (var y = 0; y < grid.Height; y++)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                if (Floor(x, y)) (_kenneyTiles && Iso.Hash(x, y, 4) < 0.14f ? details : floors).Add(new Pos(x, y));
                else if (NearFloor(x, y)) walls.Add(new Pos(x, y));
            }
        }
        _wallHeight = new float[grid.Width * grid.Height];
        foreach (var p in walls)
        {
            // The camera looks from +X/+Z, so walls on that side of a floor would hide it: cut them down.
            var front = Floor(p.X - 1, p.Y) || Floor(p.X, p.Y - 1) || Floor(p.X - 1, p.Y - 1);
            _wallHeight[grid.Idx(p)] = front ? 0.32f : WallHeight * (1f + (Iso.Hash(p.X, p.Y, 3) - 0.5f) * 0.12f);
        }

        BuildSlab(grid.Width, grid.Height);
        if (floorMesh is not null && wallMesh is not null && tileMaterial is not null)
        {
            _floors = Layer(floors, grid, floorMesh, tileMaterial);
            _floorDetails = Layer(details, grid, Kenney.Mesh(Kenney.Dungeon, "floor-detail") ?? floorMesh, tileMaterial);
            _walls = Layer(walls, grid, wallMesh, tileMaterial);
            _caps = null;
        }
        else
        {
            _floors = Layer(floors, grid, new BoxMesh { Size = new Vector3(1, 0.2f, 1) }, Iso.Flat(new Color("5e564e")));
            _floorDetails = null;
            _walls = Layer(walls, grid, new BoxMesh { Size = Vector3.One }, Iso.Flat(new Color("565049")));
            _caps = Layer(walls, grid, new BoxMesh { Size = new Vector3(1.02f, 0.08f, 1.02f) }, Iso.Flat(new Color("7d7366")));
        }

        var sconce = Iso.Solid(new Color("5a4632"), 0.5f, 0.6f);
        var flame = Iso.Glow(new Color("ffa640"), 5f);
        foreach (var p in walls)
        {
            var roll = Iso.Hash(p.X, p.Y, 7);
            if (roll > 0.15f || _wallHeight[grid.Idx(p)] < WallHeight * 0.8f) continue;
            var face = WallFaces.FirstOrDefault(d => Floor(p.X + d.Item1, p.Y + d.Item2));
            if (face == default) continue;
            var yaw = face.Item1 != 0 ? 90 : 0;
            Node3D? node;
            if (roll < 0.07f)
            {
                node = new Node3D { Position = W(p) + new Vector3(face.Item1 * 0.52f, 0.85f, face.Item2 * 0.52f) };
                Iso.Box(node, new Vector3(0.1f, 0.25f, 0.1f), Vector3.Zero, sconce);
                Iso.Ball(node, 0.07f, new Vector3(0, 0.18f, 0), flame, new Vector3(1, 1.6f, 1));
                _level.AddChild(node);
                var at = node.Position + new Vector3(face.Item1 * 0.3f, 0.3f, face.Item2 * 0.3f);
                _torches.Add((node, new OmniSpot(at, TorchColor, 1.3f, 4.5f, Iso.Hash(p.X, p.Y, 9) * 10)));
            }
            else if (roll < 0.12f)
            {
                node = Kenney.Spawn(_level, Kenney.Dungeon, "wood-support", W(p, 0.1f) + new Vector3(face.Item1 * 0.62f, 0, face.Item2 * 0.62f), yaw, 1.1f);
            }
            else
            {
                node = Kenney.Spawn(_level, Kenney.Dungeon, "banner", W(p, 0.1f) + new Vector3(face.Item1, 0, face.Item2), yaw, 1.2f);
            }
            if (node is not null) _hideInFog.Add((node, grid.Idx(p)));
        }

        BuildStairs(w.Stairs);
        foreach (var c in w.Chests) BuildChest(c.Pos);
        foreach (var t in w.Traps) BuildTrap(t.Pos, t.Def.Id);
        foreach (var v in w.Veins) BuildVein(v, Floor, _wallHeight[grid.Idx(v.Pos)]);

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

        _zoom = Mathf.Clamp(Math.Max(grid.Width, grid.Height) * 0.4f, 6f, 8f);
        _shownZoom = _zoom;
        _target = PartyCenter() ?? W(w.Start);
        Follow = true;
    }

    /// <summary>Wall faces the camera sees: a wall with floor to its +Z or +X side.</summary>
    private static readonly (int, int)[] WallFaces = [(0, 1), (1, 0)];

    /// <summary>The diorama board under the grid: its top shows where nothing is explored yet.</summary>
    private void BuildSlab(int width, int height)
    {
        var center = new Vector3((width - 1) / 2f, 0, (height - 1) / 2f);
        Iso.Box(_level, new Vector3(width + 1.2f, 1.2f, height + 1.2f), center + new Vector3(0, -0.6f, 0), Iso.Flat(new Color("2b2622")));
        Iso.Box(_level, new Vector3(width + 0.7f, 0.8f, height + 0.7f), center + new Vector3(0, -1.6f, 0), Iso.Flat(new Color("1a1614")));
    }

    private static Transform3D FogHidden(Pos p) => new(Basis.Identity.Scaled(Vector3.Zero), new Vector3(p.X, 0, p.Y));

    /// <summary>One multimesh per <see cref="ChunkSize"/> square of the grid, all tiles hidden until the fog lifts.</summary>
    private TileLayer Layer(List<Pos> tiles, Grid grid, Mesh mesh, Material material)
    {
        var layer = new TileLayer { Chunk = new int[grid.Width * grid.Height], Slot = new int[grid.Width * grid.Height] };
        Array.Fill(layer.Chunk, -1);
        foreach (var group in tiles.GroupBy(p => (p.X / ChunkSize, p.Y / ChunkSize)))
        {
            var list = group.ToList();
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh };
            mm.InstanceCount = list.Count;
            for (var i = 0; i < list.Count; i++)
            {
                var idx = grid.Idx(list[i]);
                layer.Chunk[idx] = layer.Chunks.Count;
                layer.Slot[idx] = i;
                mm.SetInstanceTransform(i, FogHidden(list[i]));
            }
            layer.Chunks.Add(mm);
            _level.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = material });
        }
        return layer;
    }

    private void BuildStairs(Pos p)
    {
        var node = new Node3D { Position = W(p) };
        var dark = Iso.Solid(new Color("050507"));
        var step = Iso.Flat(new Color("6b6259"));
        Iso.Box(node, new Vector3(0.9f, 0.02f, 0.9f), new Vector3(0, 0.1f, 0), dark);
        for (var i = 0; i < 3; i++) Iso.Box(node, new Vector3(0.8f, 0.12f, 0.24f), new Vector3(0, 0.02f - i * 0.12f, -0.3f + i * 0.25f), step);
        var rune = Iso.Glow(new Color("5dd3e8"), 3f);
        var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.42f, OuterRadius = 0.48f, Rings = 24, RingSegments = 4 }, Position = new Vector3(0, 0.12f, 0), MaterialOverride = rune };
        node.AddChild(ring);
        Iso.Ball(node, 0.08f, new Vector3(0, 0.5f, 0), Iso.Glow(new Color("5dd3e8"), 2.5f));
        _level.AddChild(node);
        _hideInFog.Add((node, _timeline!.Final.Grid.Idx(p)));
    }

    private void BuildChest(Pos p)
    {
        var node = new Node3D { Position = W(p, 0.1f), RotationDegrees = new Vector3(0, Iso.Hash(p.X, p.Y) * 40 - 20, 0) };
        Node3D hinge;
        if (Kenney.Spawn(node, Kenney.Dungeon, "chest", Vector3.Zero, 0, 1.3f) is { } model && model.FindChild("lid", true, false) is Node3D kenneyLid)
        {
            hinge = kenneyLid;
        }
        else
        {
            var wood = Iso.Flat(new Color("7a4e28"));
            var brass = Iso.Solid(new Color("d4a24a"), 0.35f, 0.8f);
            Iso.Box(node, new Vector3(0.62f, 0.32f, 0.42f), new Vector3(0, 0.16f, 0), wood);
            Iso.Box(node, new Vector3(0.64f, 0.05f, 0.44f), new Vector3(0, 0.3f, 0), brass);
            hinge = new Node3D { Position = new Vector3(0, 0.32f, -0.21f) };
            node.AddChild(hinge);
            var lid = Iso.Box(hinge, new Vector3(0.62f, 0.16f, 0.42f), new Vector3(0, 0.08f, 0.21f), wood);
            Iso.Box(lid, new Vector3(0.1f, 0.12f, 0.03f), new Vector3(0, -0.04f, 0.22f), brass);
        }
        var glint = new Node3D { Position = new Vector3(0, 0.75f, 0) };
        node.AddChild(glint);
        Iso.Box(glint, new Vector3(0.07f, 0.07f, 0.07f), Vector3.Zero, Iso.Glow(new Color("ffd27a"), 3f), new Vector3(45, 0, 45));
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
            Resources.Ore => (Iso.Flat(new Color("a0583a")), Iso.Glow(new Color("ff9a50"), 1.6f)),
            Resources.Wood => (Iso.Flat(new Color("7a5030")), Iso.Flat(new Color("573a20"))),
            Resources.Crystal => (Iso.Glow(new Color("7ab8ff"), 1.8f), Iso.Glow(new Color("d0a0ff"), 3.2f)),
            _ => (Iso.Flat(new Color("b4aea4")), Iso.Solid(new Color("e8e4dc"), 0.4f)),
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
        _level.AddChild(node);
        _veins.Add((chunks, v.Amount));
        _hideInFog.Add((node, _timeline!.Final.Grid.Idx(v.Pos)));
    }

    private void BuildTrap(Pos p, string id)
    {
        var node = new Node3D { Position = W(p, 0.1f), Visible = false };
        _level.AddChild(node);
        _traps.Add(node);
        if (id is not ("fire_vent" or "snare") && Kenney.Spawn(node, Kenney.Dungeon, "trap", Vector3.Zero) is not null) return;
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
    }

    private void UpdateFog(TickFrame f)
    {
        _fogTick = f.Tick;
        var w = _timeline!.Final;
        var grid = w.Grid;
        for (var idx = 0; idx < f.Fog.Length; idx++)
        {
            var x = idx % grid.Width;
            var y = idx / grid.Width;
            var fog = f.Fog[idx];
            var hidden = FogHidden(new Pos(x, y));
            var shade = 0.9f + Iso.Hash(x, y, 1) * 0.2f;
            var color = fog == 2 ? new Color(shade, shade, shade) : new Color(0.42f * shade, 0.45f * shade, 0.58f * shade);
            var jitter = (Iso.Hash(x, y, 2) - 0.5f) * 0.03f;
            // Kenney tiles have their origin at the bottom; the fallback boxes are centred.
            var floor = new Transform3D(Basis.Identity, new Vector3(x, _kenneyTiles ? 0.1f + jitter : jitter, y));
            _floors?.Set(idx, fog == 0 ? hidden : floor, color);
            _floorDetails?.Set(idx, fog == 0 ? hidden : floor, color);
            var h = _wallHeight[idx];
            var wall = _kenneyTiles
                ? new Transform3D(Basis.Identity.Scaled(new Vector3(1, h / KenneyWall, 1)), new Vector3(x, 0, y))
                : new Transform3D(Basis.Identity.Scaled(new Vector3(1, h, 1)), new Vector3(x, h / 2, y));
            _walls?.Set(idx, fog == 0 ? hidden : wall, color);
            _caps?.Set(idx, fog == 0 ? hidden : new Transform3D(Basis.Identity, new Vector3(x, h + 0.04f, y)), color);
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
        foreach (var t in _tabletPool) t.Visible = false;
        var used = 0;
        var tablets = 0;
        foreach (var d in f.Drops)
        {
            if (f.Fog[grid.Idx(d.Pos)] == 0) continue;
            if (d.Tablets is { Count: > 0 })
            {
                if (tablets == _tabletPool.Count) _tabletPool.Add(MakeTablet());
                var slab = _tabletPool[tablets++];
                var bob = (float)_time * 1.6f + d.Pos.X * 0.7f;
                slab.Position = W(d.Pos, 0.08f + 0.05f * Mathf.Sin(bob));
                slab.Visible = true;
                continue;
            }
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
            Iso.Tint((StandardMaterial3D)rune.MaterialOverride, color, 3f * fade);
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
                    Iso.Tint((StandardMaterial3D)dot.MaterialOverride, color);
                }
            }
        }
        for (var i = used; i < _markPool.Count; i++) _markPool[i].Visible = false;
        for (var i = dots; i < _threadPool.Count; i++) _threadPool[i].Visible = false;
    }

    private Node3D MakeTablet()
    {
        var node = new Node3D();
        var stone = Iso.Solid(new Color("8a8478"), 0.85f, 0.1f);
        Iso.Box(node, new Vector3(0.46f, 0.08f, 0.26f), new Vector3(0, 0.04f, 0), stone);
        Iso.Box(node, new Vector3(0.36f, 0.5f, 0.1f), new Vector3(0, 0.33f, 0), stone);
        var rune = Iso.Glow(new Color("7fe0ff"), 4f);
        Iso.Box(node, new Vector3(0.05f, 0.3f, 0.02f), new Vector3(0, 0.34f, 0.055f), rune);
        Iso.Box(node, new Vector3(0.2f, 0.04f, 0.02f), new Vector3(0, 0.4f, 0.055f), rune);
        Iso.Box(node, new Vector3(0.14f, 0.04f, 0.02f), new Vector3(0, 0.26f, 0.055f), rune);
        node.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.34f, OuterRadius = 0.38f, Rings = 24, RingSegments = 4 },
            Position = new Vector3(0, 0.02f, 0),
            MaterialOverride = Iso.Glow(new Color("5dd3e8"), 2.5f),
        });
        _fx.AddChild(node);
        return node;
    }

    private Node3D MakeCoins()
    {
        var node = new Node3D();
        _fx.AddChild(node);
        if (Kenney.Spawn(node, Kenney.Dungeon, "coin", Vector3.Zero, 0, 0.75f) is not null) return node;
        var gold = new StandardMaterial3D { AlbedoColor = new Color("ffcc4d"), Metallic = 0.9f, Roughness = 0.25f, EmissionEnabled = true, Emission = new Color("ffaa00"), EmissionEnergyMultiplier = 0.4f };
        for (var i = 0; i < 4; i++)
        {
            Iso.Cylinder(node, 0.07f, 0.07f, 0.03f, new Vector3((i % 2) * 0.1f - 0.05f, 0.02f + i * 0.02f, (i / 2) * 0.08f - 0.04f), gold, 8);
        }
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
            Kenney.Play(fig.Anim, dir.LengthSquared() > 0 ? "walk" : "idle");
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
                    if (fig.Core is not null) fig.Core.EmissionEnergyMultiplier = 2.5f + 0.8f * Mathf.Sin(time * 5 + phase);
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
        Iso.Tint((StandardMaterial3D)mi.MaterialOverride, color);
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
                    case EffectKind.Tablet:
                        for (var i = 0; i < 14; i++) Spark(at, new Color("7fe0ff"), age, i, 0.7f, 2.2f);
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
                        Iso.Tint((StandardMaterial3D)col.MaterialOverride, color);
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

    public Vector2? ToScreen(Vector3 world) => Iso.ToScreen(_camera, world);

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
            GolemStatus.Append(g, status);
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
            yield return new OverlayItem(s, m.Kind, m.Hp, m.MaxHp, -1, Palette.Danger, m.Intent, "", false);
        }
    }

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
                    yield return (s, g.Hp < p.Hp ? $"-{p.Hp - g.Hp}" : $"+{g.Hp - p.Hp}", g.Hp < p.Hp ? Palette.Danger : Palette.Ok, age);
                }
            }
            foreach (var m in cur.Monsters)
            {
                var p = prev.Monsters.FirstOrDefault(x => x.Id == m.Id);
                if (p is null || p.Hp <= m.Hp || cur.Fog[grid.Idx(m.Pos)] != 2) continue;
                if (ToScreen(W(m.Pos) + Vector3.Up * 1.4f) is { } s) yield return (s, $"-{p.Hp - m.Hp}", Palette.Accent, age);
            }
        }
    }
}
