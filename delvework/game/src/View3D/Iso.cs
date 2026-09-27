using Godot;

namespace Delvework.Game.View3D;

/// <summary>
/// Shared diorama helpers: reduced-res 3D viewport, flat materials, primitive props.
/// Budget: 1/<see cref="Graphics.PixelScale"/> resolution, one sun shadow, few short omnis — see <see cref="Graphics"/>.
/// </summary>
public static class Iso
{
    public const float Fov = 34f;
    public const float Elevation = 50f;
    public const float Yaw = 45f;

    public readonly record struct Scene(
        SubViewportContainer Container,
        SubViewport Viewport,
        Camera3D Camera,
        DirectionalLight3D Sun,
        Godot.Environment Env);

    public static (SubViewportContainer Container, SubViewport Viewport) SceneViewport()
    {
        var container = new SubViewportContainer
        {
            Stretch = true,
            StretchShrink = Graphics.PixelScale,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        var viewport = new SubViewport
        {
            OwnWorld3D = true,
            Msaa3D = Viewport.Msaa.Disabled,
            PositionalShadowAtlasSize = 0,
            HandleInputLocally = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenParentVisible,
        };
        container.AddChild(viewport);
        return (container, viewport);
    }

    public static Scene Mount(
        Control host,
        Color background,
        Color ambient,
        float ambientEnergy,
        Color sunColor,
        float sunEnergy,
        Vector3 sunRotation,
        float shadowReach,
        float glow = 0.6f,
        Control.MouseFilterEnum mouseFilter = Control.MouseFilterEnum.Pass)
    {
        var (container, viewport) = SceneViewport();
        container.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        container.MouseFilter = mouseFilter;
        host.AddChild(container);
        var env = Environment(background, ambient, ambientEnergy, glow);
        viewport.AddChild(new WorldEnvironment { Environment = env });
        var camera = Camera();
        viewport.AddChild(camera);
        var sun = Sun(sunColor, sunEnergy, sunRotation, shadowReach);
        viewport.AddChild(sun);
        Graphics.Apply(sun, env, viewport);
        return new Scene(container, viewport, camera, sun, env);
    }

    public static Camera3D Camera(float fov = Fov) => new()
    {
        Projection = Camera3D.ProjectionType.Perspective,
        Fov = fov,
        Near = 0.1f,
        Far = 250f,
    };

    public static void Aim(Camera3D cam, Vector3 target, float distance, float yaw = Yaw, float pitch = Elevation)
    {
        var el = Mathf.DegToRad(pitch);
        var y = Mathf.DegToRad(yaw);
        var offset = new Vector3(Mathf.Sin(y) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(y) * Mathf.Cos(el)) * distance;
        cam.LookAtFromPosition(target + offset, target, Vector3.Up);
    }

    public static Vector2? ToScreen(Camera3D cam, Vector3 world)
    {
        if (cam.IsPositionBehind(world)) return null;
        return cam.UnprojectPosition(world) * Graphics.PixelScale;
    }

    public static float UnitsPerPixel(Camera3D cam, float distance, float screenHeight) =>
        2f * distance * Mathf.Tan(Mathf.DegToRad(cam.Fov) / 2) / Math.Max(1, screenHeight);

    public static Vector3 GroundPan(Camera3D cam, Vector2 screenDelta, float unitsPerPixel)
    {
        var right = cam.GlobalBasis.X;
        var forward = -cam.GlobalBasis.Z;
        forward.Y = 0;
        forward = forward.Normalized();
        right.Y = 0;
        right = right.Normalized();
        return (-right * screenDelta.X + forward * screenDelta.Y) * unitsPerPixel;
    }

    public static Godot.Environment Environment(Color background, Color ambient, float ambientEnergy, float glow = 0.6f) => new()
    {
        BackgroundMode = Godot.Environment.BGMode.Color,
        BackgroundColor = background,
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = ambient,
        AmbientLightEnergy = ambientEnergy,
        TonemapMode = Godot.Environment.ToneMapper.Filmic,
        TonemapExposure = 1.05f,
        GlowEnabled = Graphics.Glow,
        GlowIntensity = glow,
        GlowBloom = 0.04f,
        GlowHdrThreshold = 1.0f,
    };

    public static DirectionalLight3D Sun(Color color, float energy, Vector3 rotation, float shadowReach = 60f) => new()
    {
        LightColor = color,
        LightEnergy = energy,
        RotationDegrees = rotation,
        ShadowEnabled = Graphics.Shadows,
        DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
        DirectionalShadowMaxDistance = shadowReach,
        ShadowBlur = 1.5f,
    };

    private static readonly Dictionary<string, StandardMaterial3D> Cache = new(StringComparer.Ordinal);

    /// <summary>Shared flat colour; multiplies vertex/instance colours for per-tile fog shading.</summary>
    public static StandardMaterial3D Flat(Color color, float roughness = 0.9f)
    {
        var key = color.ToHtml() + "/" + roughness.ToString("0.00");
        if (!Cache.TryGetValue(key, out var m))
        {
            m = new StandardMaterial3D { AlbedoColor = color, Roughness = roughness, VertexColorUseAsAlbedo = true };
            Cache[key] = m;
        }
        return m;
    }

    public static StandardMaterial3D Solid(Color color, float roughness = 0.7f, float metallic = 0f) => new()
    {
        AlbedoColor = color,
        Roughness = roughness,
        Metallic = metallic,
    };

    public static StandardMaterial3D Wood(Color grain) => Solid(grain, 0.85f, 0.05f);

    public static StandardMaterial3D Glow(Color color, float energy = 2.5f) => new()
    {
        AlbedoColor = color,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = energy,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    public static void Tint(StandardMaterial3D mat, Color color, float? emissionEnergy = null)
    {
        mat.AlbedoColor = color;
        mat.Emission = color;
        if (emissionEnergy is { } energy) mat.EmissionEnergyMultiplier = energy;
    }

    public static StandardMaterial3D Translucent(Color color, float emission = 0f)
    {
        var m = new StandardMaterial3D
        {
            AlbedoColor = color,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.2f,
        };
        if (emission > 0)
        {
            m.EmissionEnabled = true;
            m.Emission = new Color(color, 1);
            m.EmissionEnergyMultiplier = emission;
        }
        return m;
    }

    public static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Material mat, Vector3? rot = null)
    {
        var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = pos, MaterialOverride = mat };
        if (rot is { } r) mi.RotationDegrees = r;
        parent.AddChild(mi);
        return mi;
    }

    public static MeshInstance3D Ball(Node3D parent, float radius, Vector3 pos, Material mat, Vector3? scale = null)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = radius, Height = radius * 2, RadialSegments = 10, Rings = 5 },
            Position = pos,
            MaterialOverride = mat,
        };
        if (scale is { } s) mi.Scale = s;
        parent.AddChild(mi);
        return mi;
    }

    public static MeshInstance3D Cylinder(Node3D parent, float top, float bottom, float height, Vector3 pos, Material mat, int segments = 8)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = top, BottomRadius = bottom, Height = height, RadialSegments = segments, Rings = 1 },
            Position = pos,
            MaterialOverride = mat,
        };
        parent.AddChild(mi);
        return mi;
    }

    public static MeshInstance3D Roof(Node3D parent, Vector3 size, Vector3 pos, Material mat, float yaw = 0)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new PrismMesh { Size = new Vector3(size.Z, size.Y, size.X) },
            Position = pos,
            RotationDegrees = new Vector3(0, 90 + yaw, 0),
            MaterialOverride = mat,
        };
        parent.AddChild(mi);
        return mi;
    }

    public static OmniLight3D Light(Node3D parent, Vector3 pos, Color color, float energy, float range)
    {
        var l = new OmniLight3D
        {
            Position = pos,
            LightColor = color,
            LightEnergy = energy,
            OmniRange = range,
            OmniAttenuation = 1.4f,
            ShadowEnabled = false,
            DistanceFadeEnabled = true,
            DistanceFadeBegin = 40f,
            DistanceFadeLength = 10f,
        };
        parent.AddChild(l);
        return l;
    }

    public static float Hash(int x, int y, int salt = 0)
    {
        unchecked
        {
            var h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
