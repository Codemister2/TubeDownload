using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Graphics.Light;
using Terraria.ModLoader;
using TerraBoost.Config;

namespace TerraBoost.Core;

internal static class VisualOptimizer
{
    private readonly record struct EffectiveSettings(
        int MaxDustDraw,
        int MaxActiveDust,
        int MaxRain,
        int MaxActiveGore,
        bool DisableGore,
        bool CullDust,
        bool CullGore,
        int CullMargin,
        int CullInterval,
        bool DisableBackgrounds,
        bool DisableHeatDistortion,
        bool DisableStormEffects,
        int WaveQualityOverride,
        LightMode? LightingOverride,
        float MinimumAdaptiveScale);

    private static bool _capturedOriginals;
    private static bool _visualSettingsApplied;
    private static bool _settingsRefreshRequested = true;
    private static int _logicTicks;
    private static int _lastCullTick;
    private static int _lastAdaptiveTick;
    private static float _adaptiveScale = 1f;
    private static double _smoothedFps;

    private static int _originalMaxDustToDraw;
    private static int _originalMaxRain;
    private static bool _originalBackgrounds;
    private static bool _originalHeatDistortion;
    private static bool _originalStormEffects;
    private static int _originalWaveQuality;
    private static LightMode _originalLightMode;

    internal static int ActiveDust { get; private set; }
    internal static int ActiveGore { get; private set; }
    internal static int EffectiveDustBudget { get; private set; }
    internal static int EffectiveRainBudget { get; private set; }
    internal static float AdaptiveScale => _adaptiveScale;
    internal static double SmoothedFPS => _smoothedFps;

    internal static void RequestSettingsRefresh()
    {
        _settingsRefreshRequested = true;
        _adaptiveScale = 1f;
        _smoothedFps = 0;
        _lastAdaptiveTick = 0;
    }

    internal static void OnLogicTick()
    {
        if (Main.dedServ)
            return;

        _logicTicks++;
        TerraBoostConfig config = ModContent.GetInstance<TerraBoostConfig>();

        if (!config.EnableOptimizations)
        {
            if (_visualSettingsApplied)
                RestoreVisualSettings(clearCapture: true);

            ActiveDust = 0;
            ActiveGore = 0;
            EffectiveDustBudget = 0;
            EffectiveRainBudget = 0;
            _adaptiveScale = 1f;
            _smoothedFps = 0;
            _settingsRefreshRequested = false;
            return;
        }

        CaptureOriginalsIfNeeded();
        EffectiveSettings settings = GetEffectiveSettings(config);
        UpdateAdaptiveScale(config, settings);
        ApplyVisualSettings(settings);
        _visualSettingsApplied = true;

        if (!Main.gameMenu && _logicTicks - _lastCullTick >= Math.Max(2, settings.CullInterval))
        {
            _lastCullTick = _logicTicks;
            CullCosmeticEntities(settings);
        }

        _settingsRefreshRequested = false;
    }

    internal static void Unload()
    {
        if (_visualSettingsApplied || _capturedOriginals)
            RestoreVisualSettings(clearCapture: true);

        _settingsRefreshRequested = true;
        _logicTicks = 0;
        _lastCullTick = 0;
        _lastAdaptiveTick = 0;
        _adaptiveScale = 1f;
        _smoothedFps = 0;
        ActiveDust = 0;
        ActiveGore = 0;
        EffectiveDustBudget = 0;
        EffectiveRainBudget = 0;
    }

    private static void CaptureOriginalsIfNeeded()
    {
        if (_capturedOriginals)
            return;

        _originalMaxDustToDraw = Main.maxDustToDraw;
        _originalMaxRain = Main.maxRain;
        _originalBackgrounds = Main.BackgroundEnabled;
        _originalHeatDistortion = Main.UseHeatDistortion;
        _originalStormEffects = Main.UseStormEffects;
        _originalWaveQuality = Main.WaveQuality;
        _originalLightMode = Lighting.Mode;
        _capturedOriginals = true;
    }

    private static void RestoreVisualSettings(bool clearCapture)
    {
        if (_capturedOriginals)
        {
            Main.maxDustToDraw = _originalMaxDustToDraw;
            Main.maxRain = _originalMaxRain;
            Main.BackgroundEnabled = _originalBackgrounds;
            Main.UseHeatDistortion = _originalHeatDistortion;
            Main.UseStormEffects = _originalStormEffects;
            Main.WaveQuality = _originalWaveQuality;
            Lighting.Mode = _originalLightMode;
        }

        _visualSettingsApplied = false;
        if (clearCapture)
            _capturedOriginals = false;
    }

    private static EffectiveSettings GetEffectiveSettings(TerraBoostConfig config)
    {
        return config.Preset switch
        {
            OptimizationPreset.Auto => new EffectiveSettings(
                MaxDustDraw: 4200,
                MaxActiveDust: 4500,
                MaxRain: 550,
                MaxActiveGore: 320,
                DisableGore: false,
                CullDust: true,
                CullGore: true,
                CullMargin: 1500,
                CullInterval: 10,
                DisableBackgrounds: false,
                DisableHeatDistortion: false,
                DisableStormEffects: false,
                WaveQualityOverride: -1,
                LightingOverride: null,
                MinimumAdaptiveScale: 0.40f),

            OptimizationPreset.Balanced => new EffectiveSettings(
                MaxDustDraw: 4800,
                MaxActiveDust: 0,
                MaxRain: 650,
                MaxActiveGore: 420,
                DisableGore: false,
                CullDust: true,
                CullGore: true,
                CullMargin: 1800,
                CullInterval: 12,
                DisableBackgrounds: false,
                DisableHeatDistortion: false,
                DisableStormEffects: false,
                WaveQualityOverride: -1,
                LightingOverride: null,
                MinimumAdaptiveScale: 0.70f),

            OptimizationPreset.Performance => new EffectiveSettings(
                MaxDustDraw: 2600,
                MaxActiveDust: 3000,
                MaxRain: 350,
                MaxActiveGore: 220,
                DisableGore: false,
                CullDust: true,
                CullGore: true,
                CullMargin: 1000,
                CullInterval: 8,
                DisableBackgrounds: false,
                DisableHeatDistortion: true,
                DisableStormEffects: false,
                WaveQualityOverride: 1,
                LightingOverride: null,
                MinimumAdaptiveScale: 0.50f),

            OptimizationPreset.LowEnd => new EffectiveSettings(
                MaxDustDraw: 1200,
                MaxActiveDust: 1500,
                MaxRain: 100,
                MaxActiveGore: 80,
                DisableGore: false,
                CullDust: true,
                CullGore: true,
                CullMargin: 650,
                CullInterval: 6,
                DisableBackgrounds: true,
                DisableHeatDistortion: true,
                DisableStormEffects: true,
                WaveQualityOverride: 0,
                LightingOverride: null,
                MinimumAdaptiveScale: 0.40f),

            OptimizationPreset.Potato => new EffectiveSettings(
                MaxDustDraw: 600,
                MaxActiveDust: 800,
                MaxRain: 0,
                MaxActiveGore: 0,
                DisableGore: true,
                CullDust: true,
                CullGore: true,
                CullMargin: 350,
                CullInterval: 2,
                DisableBackgrounds: true,
                DisableHeatDistortion: true,
                DisableStormEffects: true,
                WaveQualityOverride: 0,
                LightingOverride: LightMode.Retro,
                MinimumAdaptiveScale: 0.30f),

            _ => new EffectiveSettings(
                MaxDustDraw: config.MaxDustToDraw,
                MaxActiveDust: config.MaxActiveDust,
                MaxRain: config.MaxRain,
                MaxActiveGore: config.MaxActiveGore,
                DisableGore: config.DisableGore,
                CullDust: config.CullOffscreenDust,
                CullGore: config.CullOffscreenGore,
                CullMargin: config.VisualCullMargin,
                CullInterval: config.VisualCullIntervalTicks,
                DisableBackgrounds: config.DisableBackgrounds,
                DisableHeatDistortion: config.DisableHeatDistortion,
                DisableStormEffects: config.DisableStormEffects,
                WaveQualityOverride: config.WaveQualityOverride,
                LightingOverride: GetCustomLightingOverride(config.LightingModeOverride),
                MinimumAdaptiveScale: 0.35f)
        };
    }

    private static LightMode? GetCustomLightingOverride(LightingOverrideMode mode)
    {
        return mode switch
        {
            LightingOverrideMode.Color => LightMode.Color,
            LightingOverrideMode.White => LightMode.White,
            LightingOverrideMode.Retro => LightMode.Retro,
            LightingOverrideMode.Trippy => LightMode.Trippy,
            _ => null
        };
    }

    private static void ApplyVisualSettings(EffectiveSettings settings)
    {
        float scale = MathHelper.Clamp(_adaptiveScale, settings.MinimumAdaptiveScale, 1f);

        int dustBudget = Math.Clamp((int)MathF.Round(settings.MaxDustDraw * scale), 100, 6000);
        int rainBudget = Math.Clamp((int)MathF.Round(settings.MaxRain * scale), 0, 750);

        Main.maxDustToDraw = dustBudget;
        Main.maxRain = rainBudget;
        EffectiveDustBudget = dustBudget;
        EffectiveRainBudget = rainBudget;

        Main.BackgroundEnabled = settings.DisableBackgrounds ? false : _originalBackgrounds;
        Main.UseHeatDistortion = settings.DisableHeatDistortion ? false : _originalHeatDistortion;
        Main.UseStormEffects = settings.DisableStormEffects ? false : _originalStormEffects;

        Main.WaveQuality = settings.WaveQualityOverride >= 0
            ? Math.Clamp(settings.WaveQualityOverride, 0, 3)
            : _originalWaveQuality;

        Lighting.Mode = settings.LightingOverride ?? _originalLightMode;
    }

    private static void UpdateAdaptiveScale(TerraBoostConfig config, EffectiveSettings settings)
    {
        bool adaptiveEnabled = config.AdaptiveVisualQuality || config.Preset == OptimizationPreset.Auto;
        if (!adaptiveEnabled || Main.gameMenu)
        {
            _adaptiveScale = 1f;
            _smoothedFps = 0;
            return;
        }

        if (_logicTicks - _lastAdaptiveTick < 90 && !_settingsRefreshRequested)
            return;

        _lastAdaptiveTick = _logicTicks;

        double fps = HighFpsController.MeasuredFPS;
        if (fps <= 1)
            return;

        _smoothedFps = _smoothedFps <= 1
            ? fps
            : (_smoothedFps * 0.72) + (fps * 0.28);

        int target = Math.Clamp(config.AdaptiveTargetFPS, 60, 360);
        if (config.FrameCap > 0)
            target = Math.Min(target, Math.Max(60, config.FrameCap));

        if (_smoothedFps < target * 0.78)
            _adaptiveScale = Math.Max(settings.MinimumAdaptiveScale, _adaptiveScale - 0.12f);
        else if (_smoothedFps < target * 0.90)
            _adaptiveScale = Math.Max(settings.MinimumAdaptiveScale, _adaptiveScale - 0.05f);
        else if (_smoothedFps > target * 0.98)
            _adaptiveScale = Math.Min(1f, _adaptiveScale + 0.05f);
    }

    private static void CullCosmeticEntities(EffectiveSettings settings)
    {
        float margin = Math.Max(0, settings.CullMargin);
        float left = Main.screenPosition.X - margin;
        float top = Main.screenPosition.Y - margin;
        float right = Main.screenPosition.X + Main.screenWidth + margin;
        float bottom = Main.screenPosition.Y + Main.screenHeight + margin;

        float scale = MathHelper.Clamp(_adaptiveScale, settings.MinimumAdaptiveScale, 1f);
        int activeDustLimit = settings.MaxActiveDust > 0
            ? Math.Max(100, (int)MathF.Round(settings.MaxActiveDust * scale))
            : 0;
        int activeGoreLimit = settings.MaxActiveGore > 0
            ? Math.Max(10, (int)MathF.Round(settings.MaxActiveGore * scale))
            : 0;

        int dustCount = 0;
        if (Main.dust is not null)
        {
            for (int i = 0; i < Main.dust.Length; i++)
            {
                Dust dust = Main.dust[i];
                if (dust is null || !dust.active)
                    continue;

                Vector2 p = dust.position;
                if (settings.CullDust && (p.X < left || p.X > right || p.Y < top || p.Y > bottom))
                {
                    dust.active = false;
                    continue;
                }

                if (activeDustLimit > 0 && dustCount >= activeDustLimit)
                {
                    dust.active = false;
                    continue;
                }

                dustCount++;
            }
        }

        int goreCount = 0;
        if (Main.gore is not null)
        {
            for (int i = 0; i < Main.gore.Length; i++)
            {
                Gore gore = Main.gore[i];
                if (gore is null || !gore.active)
                    continue;

                if (settings.DisableGore)
                {
                    gore.active = false;
                    continue;
                }

                Vector2 p = gore.position;
                if (settings.CullGore && (p.X < left || p.X > right || p.Y < top || p.Y > bottom))
                {
                    gore.active = false;
                    continue;
                }

                if (activeGoreLimit > 0 && goreCount >= activeGoreLimit)
                {
                    gore.active = false;
                    continue;
                }

                goreCount++;
            }
        }

        ActiveDust = dustCount;
        ActiveGore = goreCount;
    }
}
