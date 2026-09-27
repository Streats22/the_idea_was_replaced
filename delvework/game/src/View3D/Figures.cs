using Godot;

namespace Delvework.Game.View3D;

public sealed class Figure
{
    public required Node3D Root { get; init; }
    public required Node3D Body { get; init; }
    public StandardMaterial3D? Core { get; init; }
    public Node3D? LeftArm { get; init; }
    public Node3D? RightArm { get; init; }
    public Node3D? LeftWing { get; init; }
    public Node3D? RightWing { get; init; }
    public AnimationPlayer? Anim { get; init; }
    public Color Accent { get; init; }
    public float Height { get; init; } = 1f;
}

public static class Figures
{
    public static Color MonsterAccent(string defId) => defId switch
    {
        "slime" => new Color("7bd88f"),
        "skeleton" => new Color("d9d2c0"),
        "bat" => new Color("c7a0ff"),
        "fire_beetle" => new Color("ff8a3a"),
        "troll" => new Color("a7c080"),
        "spider" => new Color("b0ff60"),
        "mimic" => new Color("d4a24a"),
        "wisp" => new Color("9ae8ff"),
        _ => new Color("cc99cc"),
    };

    private static StandardMaterial3D Stone(Color c) => new() { AlbedoColor = c, Roughness = 0.85f, Metallic = 0.15f };

    public static Figure Golem(string chassis)
    {
        var accent = Palette.ForGolem(chassis);
        var root = new Node3D { Name = "Golem_" + chassis };
        var body = new Node3D();
        root.AddChild(body);
        var stone = Stone(chassis switch
        {
            "warden" => new Color("9a8a78"),
            "seeker" => new Color("7f8a92"),
            _ => new Color("927f90"),
        });
        var trim = new StandardMaterial3D { AlbedoColor = new Color("8a6a3a"), Metallic = 0.7f, Roughness = 0.4f };
        var core = Iso.Glow(accent, 3f);
        var bulk = chassis == "warden" ? 1.15f : chassis == "seeker" ? 0.85f : 1f;

        Iso.Box(body, new Vector3(0.16f, 0.26f, 0.18f) * bulk, new Vector3(-0.12f * bulk, 0.13f, 0), stone);
        Iso.Box(body, new Vector3(0.16f, 0.26f, 0.18f) * bulk, new Vector3(0.12f * bulk, 0.13f, 0), stone);
        Iso.Box(body, new Vector3(0.5f, 0.42f, 0.34f) * bulk, new Vector3(0, 0.45f * bulk, 0), stone);
        Iso.Box(body, new Vector3(0.52f, 0.06f, 0.36f) * bulk, new Vector3(0, 0.66f * bulk, 0), trim);
        Iso.Box(body, new Vector3(0.28f, 0.24f, 0.26f) * bulk, new Vector3(0, 0.8f * bulk, 0), stone);
        Iso.Box(body, new Vector3(0.18f, 0.04f, 0.02f) * bulk, new Vector3(0, 0.82f * bulk, 0.135f * bulk), core);
        Iso.Box(body, new Vector3(0.14f, 0.14f, 0.04f) * bulk, new Vector3(0, 0.48f * bulk, 0.18f * bulk), core, new Vector3(0, 0, 45));

        Node3D Arm(float side)
        {
            var pivot = new Node3D { Position = new Vector3(side * 0.33f * bulk, 0.6f * bulk, 0) };
            body.AddChild(pivot);
            Iso.Box(pivot, new Vector3(0.14f, 0.4f, 0.16f) * bulk, new Vector3(0, -0.2f * bulk, 0), stone);
            Iso.Box(pivot, new Vector3(0.17f, 0.12f, 0.19f) * bulk, new Vector3(0, -0.42f * bulk, 0), trim);
            return pivot;
        }
        var left = Arm(-1);
        var right = Arm(1);

        switch (chassis)
        {
            case "warden":
                Iso.Box(body, new Vector3(0.2f, 0.1f, 0.3f), new Vector3(-0.33f, 0.72f, 0), trim);
                Iso.Box(body, new Vector3(0.2f, 0.1f, 0.3f), new Vector3(0.33f, 0.72f, 0), trim);
                var shield = Iso.Box(left, new Vector3(0.06f, 0.42f, 0.34f), new Vector3(-0.1f, -0.25f, 0.05f), trim);
                Iso.Box(shield, new Vector3(0.02f, 0.14f, 0.14f), new Vector3(-0.035f, 0, 0), core, new Vector3(45, 0, 0));
                break;
            case "seeker":
                Iso.Cylinder(right, 0.015f, 0.015f, 0.3f, new Vector3(0.05f, -0.55f, 0.12f), trim);
                Iso.Box(right, new Vector3(0.1f, 0.12f, 0.1f), new Vector3(0.05f, -0.72f, 0.12f), Iso.Glow(accent, 4f));
                Iso.Box(body, new Vector3(0.05f, 0.18f, 0.05f), new Vector3(0, 1.0f, 0), trim);
                break;
            default:
                foreach (var s in new[] { -1f, 1f })
                    Iso.Box(body, new Vector3(0.06f, 0.2f, 0.06f), new Vector3(s * 0.2f, 0.95f, 0), trim, new Vector3(0, 0, -s * 25));
                break;
        }

        return new Figure
        {
            Root = root, Body = body, Core = core, LeftArm = left, RightArm = right,
            Accent = accent, Height = 1.05f * bulk,
        };
    }

    public static Figure Monster(string defId)
    {
        var root = new Node3D { Name = "Monster_" + defId };
        var body = new Node3D();
        root.AddChild(body);
        var accent = MonsterAccent(defId);
        return defId switch
        {
            "slime" => Slime(root, body, accent),
            "skeleton" => Skeleton(root, body, accent),
            "bat" => Bat(root, body, accent),
            "fire_beetle" => FireBeetle(root, body, accent),
            "troll" => Troll(root, body, accent),
            "spider" => Spider(root, body, accent),
            "mimic" => Mimic(root, body, accent),
            "wisp" => Wisp(root, body, accent),
            _ => Fallback(root, body, accent),
        };
    }

    private static Figure Slime(Node3D root, Node3D body, Color accent)
    {
        var gel = Iso.Translucent(new Color(0.35f, 0.85f, 0.35f, 0.82f), 0.35f);
        Iso.Ball(body, 0.32f, new Vector3(0, 0.22f, 0), gel, new Vector3(1, 0.7f, 1));
        var eye = Iso.Glow(new Color("e8ffe0"), 1.5f);
        Iso.Ball(body, 0.05f, new Vector3(-0.1f, 0.3f, 0.24f), eye);
        Iso.Ball(body, 0.05f, new Vector3(0.1f, 0.3f, 0.24f), eye);
        return new Figure { Root = root, Body = body, Core = eye, Accent = accent, Height = 0.55f };
    }

    private static Figure Skeleton(Node3D root, Node3D body, Color accent)
    {
        var bone = Stone(accent);
        var eyes = Iso.Glow(new Color("ff4040"), 3f);
        Iso.Box(body, new Vector3(0.06f, 0.36f, 0.06f), new Vector3(-0.09f, 0.18f, 0), bone);
        Iso.Box(body, new Vector3(0.06f, 0.36f, 0.06f), new Vector3(0.09f, 0.18f, 0), bone);
        Iso.Box(body, new Vector3(0.26f, 0.08f, 0.14f), new Vector3(0, 0.38f, 0), bone);
        Iso.Box(body, new Vector3(0.05f, 0.3f, 0.05f), new Vector3(0, 0.56f, -0.03f), bone);
        for (var i = 0; i < 3; i++) Iso.Box(body, new Vector3(0.3f - i * 0.04f, 0.035f, 0.18f), new Vector3(0, 0.5f + i * 0.08f, 0), bone);
        Iso.Ball(body, 0.13f, new Vector3(0, 0.84f, 0), bone);
        Iso.Box(body, new Vector3(0.05f, 0.03f, 0.02f), new Vector3(-0.05f, 0.85f, 0.12f), eyes);
        Iso.Box(body, new Vector3(0.05f, 0.03f, 0.02f), new Vector3(0.05f, 0.85f, 0.12f), eyes);
        Node3D Arm(float side)
        {
            var p = new Node3D { Position = new Vector3(side * 0.2f, 0.72f, 0) };
            body.AddChild(p);
            Iso.Box(p, new Vector3(0.05f, 0.38f, 0.05f), new Vector3(0, -0.19f, 0), bone);
            return p;
        }
        var left = Arm(-1);
        var right = Arm(1);
        Iso.Box(right, new Vector3(0.03f, 0.05f, 0.4f), new Vector3(0, -0.38f, 0.18f), Stone(new Color("8a8f96")));
        return new Figure { Root = root, Body = body, Core = eyes, LeftArm = left, RightArm = right, Accent = accent, Height = 1.0f };
    }

    private static Figure Bat(Node3D root, Node3D body, Color accent)
    {
        var fur = Stone(new Color("3a3036"));
        var eyes = Iso.Glow(new Color("ffcc33"), 3f);
        Iso.Ball(body, 0.12f, new Vector3(0, 0.75f, 0), fur, new Vector3(1, 1.1f, 1));
        Iso.Box(body, new Vector3(0.04f, 0.03f, 0.02f), new Vector3(-0.04f, 0.8f, 0.11f), eyes);
        Iso.Box(body, new Vector3(0.04f, 0.03f, 0.02f), new Vector3(0.04f, 0.8f, 0.11f), eyes);
        Node3D Wing(float side)
        {
            var p = new Node3D { Position = new Vector3(side * 0.08f, 0.78f, 0) };
            body.AddChild(p);
            Iso.Box(p, new Vector3(0.32f, 0.02f, 0.2f), new Vector3(side * 0.17f, 0, 0), Stone(new Color("4a3a44")));
            return p;
        }
        return new Figure
        {
            Root = root, Body = body, Core = eyes, LeftWing = Wing(-1), RightWing = Wing(1),
            Accent = accent, Height = 1.0f,
        };
    }

    private static Figure FireBeetle(Node3D root, Node3D body, Color accent)
    {
        var shell = new StandardMaterial3D
        {
            AlbedoColor = new Color("8a2a10"), Metallic = 0.4f, Roughness = 0.35f,
            EmissionEnabled = true, Emission = new Color("ff5a10"), EmissionEnergyMultiplier = 0.6f,
        };
        var glow = Iso.Glow(new Color("ffb030"), 4f);
        Iso.Ball(body, 0.3f, new Vector3(0, 0.22f, -0.03f), shell, new Vector3(1, 0.6f, 1.25f));
        Iso.Ball(body, 0.12f, new Vector3(0, 0.2f, 0.32f), Stone(new Color("2a1a14")));
        Iso.Box(body, new Vector3(0.04f, 0.2f, 0.5f), new Vector3(0, 0.34f, -0.02f), glow);
        for (var i = -1; i <= 1; i++)
        {
            foreach (var s in new[] { -1f, 1f })
                Iso.Box(body, new Vector3(0.22f, 0.03f, 0.03f), new Vector3(s * 0.28f, 0.1f, i * 0.14f), Stone(new Color("2a1a14")));
        }
        return new Figure { Root = root, Body = body, Core = glow, Accent = accent, Height = 0.55f };
    }

    private static Figure Troll(Node3D root, Node3D body, Color accent)
    {
        if (Kenney.Spawn(body, Kenney.Dungeon, "character-orc", Vector3.Zero, 0, 1.95f) is { } orc)
        {
            var anim = Kenney.Animations(orc);
            Kenney.Play(anim, "idle");
            return new Figure { Root = root, Body = body, Anim = anim, Accent = accent, Height = 1.55f };
        }
        var hide = Stone(new Color("55644a"));
        var eyes = Iso.Glow(new Color("ffe066"), 2.5f);
        Iso.Box(body, new Vector3(0.24f, 0.4f, 0.28f), new Vector3(-0.2f, 0.2f, 0), hide);
        Iso.Box(body, new Vector3(0.24f, 0.4f, 0.28f), new Vector3(0.2f, 0.2f, 0), hide);
        Iso.Ball(body, 0.45f, new Vector3(0, 0.8f, 0), hide, new Vector3(1.1f, 1f, 0.85f));
        Iso.Ball(body, 0.2f, new Vector3(0, 1.28f, 0.12f), hide);
        Iso.Box(body, new Vector3(0.06f, 0.04f, 0.02f), new Vector3(-0.08f, 1.32f, 0.3f), eyes);
        Iso.Box(body, new Vector3(0.06f, 0.04f, 0.02f), new Vector3(0.08f, 1.32f, 0.3f), eyes);
        Node3D Arm(float side)
        {
            var p = new Node3D { Position = new Vector3(side * 0.52f, 1.05f, 0) };
            body.AddChild(p);
            Iso.Box(p, new Vector3(0.22f, 0.8f, 0.24f), new Vector3(0, -0.4f, 0), hide);
            Iso.Ball(p, 0.16f, new Vector3(0, -0.82f, 0), hide);
            return p;
        }
        return new Figure
        {
            Root = root, Body = body, Core = eyes, LeftArm = Arm(-1), RightArm = Arm(1),
            Accent = accent, Height = 1.55f,
        };
    }

    private static Figure Spider(Node3D root, Node3D body, Color accent)
    {
        var chitin = Stone(new Color("2a2430"));
        var eyes = Iso.Glow(accent, 3f);
        Iso.Ball(body, 0.2f, new Vector3(0, 0.26f, -0.14f), chitin, new Vector3(1, 0.8f, 1.2f));
        Iso.Ball(body, 0.12f, new Vector3(0, 0.24f, 0.12f), chitin);
        Iso.Box(body, new Vector3(0.1f, 0.03f, 0.1f), new Vector3(0, 0.42f, -0.14f), Iso.Solid(new Color("8a2a3a")), new Vector3(0, 45, 0));
        for (var i = 0; i < 4; i++) Iso.Box(body, new Vector3(0.03f, 0.03f, 0.02f), new Vector3(-0.045f + i * 0.03f, 0.28f + (i % 2) * 0.03f, 0.23f), eyes);
        Node3D Legs(float side)
        {
            var p = new Node3D { Position = new Vector3(side * 0.1f, 0.28f, 0.02f) };
            body.AddChild(p);
            for (var k = 0; k < 4; k++)
            {
                var z = 0.12f - k * 0.1f;
                Iso.Box(p, new Vector3(0.26f, 0.025f, 0.025f), new Vector3(side * 0.12f, 0.04f, z), chitin, new Vector3(0, side * (k - 1.5f) * 12, side * 25));
                Iso.Box(p, new Vector3(0.025f, 0.24f, 0.025f), new Vector3(side * 0.24f, -0.07f, z * 1.2f), chitin, new Vector3(0, 0, side * -15));
            }
            return p;
        }
        return new Figure { Root = root, Body = body, Core = eyes, LeftArm = Legs(-1), RightArm = Legs(1), Accent = accent, Height = 0.55f };
    }

    private static Figure Mimic(Node3D root, Node3D body, Color accent)
    {
        var wood = Iso.Wood(new Color("8a5a2e"));
        var brass = new StandardMaterial3D { AlbedoColor = accent, Metallic = 0.8f, Roughness = 0.35f };
        var tooth = Stone(new Color("ece4d0"));
        var eyes = Iso.Glow(new Color("ff5050"), 3f);
        Iso.Box(body, new Vector3(0.62f, 0.32f, 0.42f), new Vector3(0, 0.16f, 0), wood);
        Iso.Box(body, new Vector3(0.64f, 0.05f, 0.44f), new Vector3(0, 0.3f, 0), brass);
        Iso.Box(body, new Vector3(0.5f, 0.02f, 0.3f), new Vector3(0, 0.31f, 0.02f), Iso.Glow(new Color("5a0a14"), 0.8f));
        for (var i = 0; i < 5; i++) Iso.Box(body, new Vector3(0.05f, 0.08f, 0.03f), new Vector3(-0.24f + i * 0.12f, 0.36f, 0.2f), tooth, new Vector3(0, 0, 45));
        var hinge = new Node3D { Position = new Vector3(0, 0.32f, -0.21f) };
        body.AddChild(hinge);
        var lid = Iso.Box(hinge, new Vector3(0.62f, 0.16f, 0.42f), new Vector3(0, 0.08f, 0.21f), wood);
        Iso.Box(lid, new Vector3(0.1f, 0.12f, 0.03f), new Vector3(0, -0.04f, 0.22f), brass);
        for (var i = 0; i < 4; i++) Iso.Box(hinge, new Vector3(0.05f, 0.08f, 0.03f), new Vector3(-0.18f + i * 0.12f, -0.02f, 0.4f), tooth, new Vector3(0, 0, 45));
        Iso.Box(hinge, new Vector3(0.06f, 0.04f, 0.02f), new Vector3(-0.12f, 0.12f, 0.43f), eyes);
        Iso.Box(hinge, new Vector3(0.06f, 0.04f, 0.02f), new Vector3(0.12f, 0.12f, 0.43f), eyes);
        var tongue = new Node3D { Position = new Vector3(0, 0.3f, 0.1f) };
        body.AddChild(tongue);
        Iso.Box(tongue, new Vector3(0.12f, 0.03f, 0.3f), new Vector3(0, 0, 0.15f), Iso.Solid(new Color("c04060")));
        return new Figure { Root = root, Body = body, Core = eyes, LeftArm = tongue, RightArm = hinge, Accent = accent, Height = 0.6f };
    }

    private static Figure Wisp(Node3D root, Node3D body, Color accent)
    {
        var core = Iso.Glow(accent, 4f);
        Iso.Ball(body, 0.13f, new Vector3(0, 0.7f, 0), core);
        Iso.Ball(body, 0.24f, new Vector3(0, 0.7f, 0), Iso.Translucent(new Color(0.6f, 0.9f, 1f, 0.25f), 1.5f));
        var tail = new Node3D { Position = new Vector3(0, 0.7f, 0) };
        body.AddChild(tail);
        for (var i = 1; i <= 3; i++) Iso.Ball(tail, 0.1f - i * 0.02f, new Vector3(0, -i * 0.08f, -i * 0.12f), Iso.Glow(new Color("5dd3e8"), 3f - i * 0.6f));
        return new Figure { Root = root, Body = body, Core = core, LeftWing = tail, Accent = accent, Height = 1.0f };
    }

    private static Figure Fallback(Node3D root, Node3D body, Color accent)
    {
        Iso.Ball(body, 0.3f, new Vector3(0, 0.35f, 0), Stone(new Color("886688")));
        return new Figure { Root = root, Body = body, Accent = accent, Height = 0.7f };
    }
}
