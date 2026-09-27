using Godot;

namespace Delvework.Game.View3D;

/// <summary>
/// Shared pieces of the 2.5D look: a low-resolution 3D viewport scaled up with nearest
/// filtering (chunky pixels), an isometric orthographic camera, procedural materials and
/// small helpers for building props out of primitive meshes.
/// </summary>
public static class Iso
{
    /// <summary>Screen pixels per rendered pixel.</summary>
    public const int PixelScale = 2;

    public const float Elevation = 48f;
    public const float Yaw = 45f;

    /// <summary>A viewport container that renders its own 3D world at 1/<see cref="PixelScale"/> resolution.</summary>
    public static (SubViewportContainer Container, SubViewport Viewport) PixelViewport()
    {
        var container = new SubViewportContainer
        {
            Stretch = true,
            StretchShrink = PixelScale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        var viewport = new SubViewport
        {
            OwnWorld3D = true,
            Msaa3D = Viewport.Msaa.Disabled,
            HandleInputLocally = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        container.AddChild(viewport);
        return (container, viewport);
    }

    public static Camera3D Camera(float size) => new()
    {
        Projection = Camera3D.ProjectionType.Orthogonal,
        Size = size,
        Near = 0.05f,
        Far = 200f,
    };

    /// <summary>Point the camera at <paramref name="target"/> from the fixed isometric angle.</summary>
    public static void Aim(Camera3D cam, Vector3 target, float distance = 40f)
    {
        var el = Mathf.DegToRad(Elevation);
        var yaw = Mathf.DegToRad(Yaw);
        var offset = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(yaw) * Mathf.Cos(el)) * distance;
        cam.LookAtFromPosition(target + offset, target, Vector3.Up);
    }

    /// <summary>World-space movement for a screen-space drag (camera right and camera "up" projected on the ground).</summary>
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

    public static Godot.Environment Environment(Color background, Color ambient, float ambientEnergy, float glow = 0.9f)
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = background,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = ambient,
            AmbientLightEnergy = ambientEnergy,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.1f,
            GlowEnabled = true,
            GlowIntensity = glow,
            GlowBloom = 0.08f,
            GlowHdrThreshold = 0.9f,
            SsaoEnabled = true,
            SsaoRadius = 0.6f,
            SsaoIntensity = 1.6f,
        };
        return env;
    }

    // ----- Materials -----

    private static readonly Dictionary<string, Material> Cache = [];

    private static NoiseTexture2D Noise(FastNoiseLite noise, Gradient ramp, int size = 128) => new()
    {
        Width = size,
        Height = size,
        Seamless = true,
        Noise = noise,
        ColorRamp = ramp,
    };

    private static Gradient Ramp(params (float At, Color Color)[] stops)
    {
        var g = new Gradient();
        g.Offsets = stops.Select(s => s.At).ToArray();
        g.Colors = stops.Select(s => s.Color).ToArray();
        return g;
    }

    /// <summary>Flagstones: cellular noise gives stone slabs with dark mortar lines.</summary>
    public static Material Flagstone(Color light, Color dark, float scale = 0.5f, string key = "flag") => Cached(key, () => new StandardMaterial3D
    {
        AlbedoTexture = Noise(new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular,
            Frequency = 0.06f,
            CellularDistanceFunction = FastNoiseLite.CellularDistanceFunctionEnum.Manhattan,
            CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance2Sub,
            FractalType = FastNoiseLite.FractalTypeEnum.None,
            Seed = 3,
        }, Ramp((0f, dark.Darkened(0.55f)), (0.07f, dark), (0.16f, light.Darkened(0.12f)), (0.6f, light), (1f, light.Lightened(0.08f)))),
        Uv1Triplanar = true,
        Uv1Scale = new Vector3(scale, scale, scale),
        VertexColorUseAsAlbedo = true,
        Roughness = 0.92f,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
    });

    /// <summary>Rough rock or plaster: fractal noise.</summary>
    public static Material Rock(Color light, Color dark, float scale = 0.35f, string key = "rock") => Cached(key, () => new StandardMaterial3D
    {
        AlbedoTexture = Noise(new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency = 0.05f,
            FractalOctaves = 4,
            Seed = 11,
        }, Ramp((0f, dark), (0.5f, light.Lerp(dark, 0.4f)), (1f, light))),
        Uv1Triplanar = true,
        Uv1Scale = new Vector3(scale, scale, scale),
        VertexColorUseAsAlbedo = true,
        Roughness = 0.95f,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
    });

    /// <summary>Wooden planks: stretched noise gives grain.</summary>
    public static Material Wood(Color light, Color dark, string key = "wood") => Cached(key, () => new StandardMaterial3D
    {
        AlbedoTexture = Noise(new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = 0.08f,
            FractalOctaves = 2,
            Seed = 5,
        }, Ramp((0f, dark), (1f, light))),
        Uv1Triplanar = true,
        Uv1Scale = new Vector3(0.3f, 2.2f, 0.3f),
        Roughness = 0.85f,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
    });

    public static StandardMaterial3D Solid(Color color, float roughness = 0.7f, float metallic = 0f) => new()
    {
        AlbedoColor = color,
        Roughness = roughness,
        Metallic = metallic,
    };

    public static StandardMaterial3D Glow(Color color, float energy = 2.5f) => new()
    {
        AlbedoColor = color,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = energy,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

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

    private static Material Cached(string key, Func<Material> make)
    {
        if (!Cache.TryGetValue(key, out var m))
        {
            m = make();
            Cache[key] = m;
        }
        return m;
    }

    // ----- Building props -----

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
            Mesh = new SphereMesh { Radius = radius, Height = radius * 2, RadialSegments = 12, Rings = 6 },
            Position = pos,
            MaterialOverride = mat,
        };
        if (scale is { } s) mi.Scale = s;
        parent.AddChild(mi);
        return mi;
    }

    public static MeshInstance3D Cylinder(Node3D parent, float top, float bottom, float height, Vector3 pos, Material mat, int segments = 10)
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

    /// <summary>A triangular prism, used for roofs. Its ridge runs along X.</summary>
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

    public static OmniLight3D Light(Node3D parent, Vector3 pos, Color color, float energy, float range, bool shadows = false)
    {
        var l = new OmniLight3D
        {
            Position = pos,
            LightColor = color,
            LightEnergy = energy,
            OmniRange = range,
            OmniAttenuation = 1.4f,
            ShadowEnabled = shadows,
        };
        parent.AddChild(l);
        return l;
    }

    /// <summary>Stable pseudo-random number in [0, 1) for a grid position, so decoration never flickers between frames.</summary>
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
