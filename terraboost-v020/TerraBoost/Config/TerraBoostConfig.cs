using System.ComponentModel;
using Terraria.ModLoader.Config;
using TerraBoost.Core;

namespace TerraBoost.Config;

public enum OptimizationPreset
{
    Auto,
    Balanced,
    Performance,
    LowEnd,
    Potato,
    Custom
}

public enum LightingOverrideMode
{
    KeepCurrent,
    Color,
    White,
    Retro,
    Trippy
}

public sealed class TerraBoostConfig : ModConfig
{
    public override ConfigScope Mode => ConfigScope.ClientSide;

    [Header("HighFPS")]
    [DefaultValue(true)]
    public bool EnableHighFPS = true;

    // 0 means unlimited. Values from 1-59 are treated as 60.
    [DefaultValue(144)]
    [Range(0, 1000)]
    public int FrameCap = 144;

    [DefaultValue(true)]
    public bool DisableVSync = true;

    [DefaultValue(true)]
    public bool SmoothEntityMotion = true;

    [DefaultValue(true)]
    public bool SmoothCamera = true;

    [DefaultValue(true)]
    public bool SmoothWorldItems = true;

    [DefaultValue(true)]
    public bool InterpolateOnlyNearScreen = true;

    [DefaultValue(1200)]
    [Range(200, 4000)]
    public int InterpolationCullMargin = 1200;

    [DefaultValue(true)]
    public bool LimitFPSWhenUnfocused = true;

    [DefaultValue(30)]
    [Range(15, 120)]
    public int UnfocusedFPSCap = 30;

    [DefaultValue(true)]
    public bool LimitMenuFPS = true;

    [DefaultValue(120)]
    [Range(60, 360)]
    public int MenuFPSCap = 120;

    [Header("Optimization")]
    [DefaultValue(true)]
    public bool EnableOptimizations = true;

    [DefaultValue(OptimizationPreset.Auto)]
    public OptimizationPreset Preset = OptimizationPreset.Auto;

    [DefaultValue(true)]
    public bool AdaptiveVisualQuality = true;

    [DefaultValue(120)]
    [Range(60, 360)]
    public int AdaptiveTargetFPS = 120;

    [Header("CustomProfile")]
    [DefaultValue(3500)]
    [Range(100, 6000)]
    public int MaxDustToDraw = 3500;

    // 0 disables the hard active-dust cap.
    [DefaultValue(0)]
    [Range(0, 6000)]
    public int MaxActiveDust = 0;

    [DefaultValue(450)]
    [Range(0, 750)]
    public int MaxRain = 450;

    // 0 disables the hard active-gore cap unless DisableGore is enabled.
    [DefaultValue(300)]
    [Range(0, 600)]
    public int MaxActiveGore = 300;

    [DefaultValue(false)]
    public bool DisableGore = false;

    [DefaultValue(true)]
    public bool CullOffscreenDust = true;

    [DefaultValue(true)]
    public bool CullOffscreenGore = true;

    [DefaultValue(1200)]
    [Range(200, 4000)]
    public int VisualCullMargin = 1200;

    [DefaultValue(8)]
    [Range(2, 60)]
    public int VisualCullIntervalTicks = 8;

    [DefaultValue(false)]
    public bool DisableBackgrounds = false;

    [DefaultValue(false)]
    public bool DisableHeatDistortion = false;

    [DefaultValue(false)]
    public bool DisableStormEffects = false;

    // -1 keeps the user's original setting. Vanilla values are 0-3.
    [DefaultValue(-1)]
    [Range(-1, 3)]
    public int WaveQualityOverride = -1;

    [DefaultValue(LightingOverrideMode.KeepCurrent)]
    public LightingOverrideMode LightingModeOverride = LightingOverrideMode.KeepCurrent;

    [Header("Diagnostics")]
    [DefaultValue(false)]
    public bool ShowPerformanceOverlay = false;

    public override void OnChanged()
    {
        HighFpsController.RequestSettingsRefresh();
        VisualOptimizer.RequestSettingsRefresh();
    }
}
