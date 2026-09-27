using Godot;

namespace Delvework.Game.View3D;

/// <summary>Kenney CC0 packs under res://assets/kenney; missing models return null.</summary>
public static class Kenney
{
    public const string Town = "town";
    public const string Dungeon = "dungeon";
    public const string Characters = "characters";

    private static readonly Dictionary<string, Node3D?> Templates = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, StandardMaterial3D> TileMaterials = new(StringComparer.Ordinal);
    private static readonly string[] Looping = ["idle", "walk", "sprint"];

    public static Node3D? Spawn(Node3D parent, string pack, string model, Vector3 pos, float yaw = 0, float scale = 1)
    {
        if (Template(pack, model) is not { } template) return null;
        var node = (Node3D)template.Duplicate();
        node.Position = pos;
        node.RotationDegrees = new Vector3(0, yaw, 0);
        node.Scale = Vector3.One * scale;
        parent.AddChild(node);
        return node;
    }

    public static Mesh? Mesh(string pack, string model) => Template(pack, model) is { } t ? FindMesh(t)?.Mesh : null;

    public static StandardMaterial3D? TileMaterial(string pack, string model)
    {
        if (TileMaterials.TryGetValue(pack, out var cached)) return cached;
        if (Template(pack, model) is not { } t || FindMesh(t) is not { } mi || mi.GetActiveMaterial(0) is not BaseMaterial3D source) return null;
        var m = new StandardMaterial3D
        {
            AlbedoTexture = source.AlbedoTexture,
            AlbedoColor = source.AlbedoColor,
            VertexColorUseAsAlbedo = true,
            Roughness = 0.95f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
        };
        TileMaterials[pack] = m;
        return m;
    }

    public static AnimationPlayer? Animations(Node node) => node.FindChild("AnimationPlayer", true, false) as AnimationPlayer;

    public static void Play(AnimationPlayer? player, string name, float speed = 1)
    {
        if (player is null || !player.HasAnimation(name)) return;
        player.SpeedScale = speed;
        if (player.CurrentAnimation.ToString() != name) player.Play(name, 0.15);
    }

    public static void Clear()
    {
        foreach (var t in Templates.Values) t?.Free();
        Templates.Clear();
        TileMaterials.Clear();
    }

    private static Node3D? Template(string pack, string model)
    {
        var key = pack + "/" + model;
        if (Templates.TryGetValue(key, out var t)) return t;
        t = Load($"res://assets/kenney/{pack}/{model}.glb");
        if (t is not null && Animations(t) is { } player)
        {
            foreach (var name in Looping)
            {
                if (player.HasAnimation(name)) player.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
            }
        }
        Templates[key] = t;
        return t;
    }

    private static Node3D? Load(string path)
    {
        if (Godot.FileAccess.FileExists(path + ".import") && ResourceLoader.Exists(path))
        {
            return ResourceLoader.Load<PackedScene>(path)?.Instantiate<Node3D>();
        }
        if (!Godot.FileAccess.FileExists(path)) return null;
        var doc = new GltfDocument();
        var state = new GltfState();
        return doc.AppendFromFile(path, state) == Error.Ok ? doc.GenerateScene(state) as Node3D : null;
    }

    private static MeshInstance3D? FindMesh(Node node)
    {
        if (node is MeshInstance3D mi && mi.Mesh is not null) return mi;
        foreach (var child in node.GetChildren())
        {
            if (FindMesh(child) is { } found) return found;
        }
        return null;
    }
}
