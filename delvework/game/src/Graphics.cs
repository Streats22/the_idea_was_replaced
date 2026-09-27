using Godot;

namespace Delvework.Game;

/// <summary>
/// Graphics budget in <c>user://settings.cfg</c>. Normal: 60 fps, half-res 3D, one sun shadow.
/// Low: 30 fps, third-res 3D, no shadows/glow. Background drops to <see cref="BackgroundFps"/>.
/// </summary>
public static class Graphics
{
    private const string Path = "user://settings.cfg";
    public const int BackgroundFps = 10;
    public const int MaxOmniLights = 6;

    public static event Action? Changed;

    public static bool Low { get; private set; }

    public static int FrameCap => Low ? 30 : 60;
    public static bool Shadows => !Low;
    public static bool Glow => !Low;
    public static int PixelScale => Low ? 3 : 2;

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) == Error.Ok) Low = cfg.GetValue("graphics", "low", false).AsBool();
        Engine.MaxFps = FrameCap;
    }

    public static void SetLow(bool low)
    {
        if (Low == low)
        {
            Engine.MaxFps = FrameCap;
            return;
        }
        Low = low;
        Engine.MaxFps = FrameCap;
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue("graphics", "low", low);
        cfg.Save(Path);
        Changed?.Invoke();
    }

    public static void Apply(DirectionalLight3D sun, Godot.Environment env, SubViewport? viewport = null)
    {
        if (sun.ShadowEnabled != Shadows) sun.ShadowEnabled = Shadows;
        if (env.GlowEnabled != Glow) env.GlowEnabled = Glow;
        if (viewport is not null)
        {
            viewport.Msaa3D = Viewport.Msaa.Disabled;
            if (viewport.GetParent() is SubViewportContainer container && container.StretchShrink != PixelScale)
            {
                container.StretchShrink = PixelScale;
            }
        }
    }
}
