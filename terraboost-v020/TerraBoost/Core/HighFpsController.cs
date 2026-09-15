using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Enums;
using Terraria.ModLoader;
using TerraBoost.Config;

namespace TerraBoost.Core;

/// <summary>
/// Unlocks rendering while leaving Terraria's 60 Hz simulation alone.
/// Terraria already maintains Main.UpdateTimeAccumulator; when Frame Skip is Off,
/// render frames can happen between complete 60 Hz updates. We use that accumulator
/// only for visual interpolation and never speed up world logic.
/// </summary>
internal static class HighFpsController
{
    private const double LogicStepSeconds = 1.0 / 60.0;
    private const float MaximumInterpolationDistance = 512f;

    private static bool _loaded;
    private static bool _settingsRefreshRequested = true;
    private static bool _runtimeSettingsApplied;

    private static FrameSkipMode _savedFrameSkipMode;
    private static bool _savedVSync;

    private static long _nextFrameTimestamp;
    private static int _lastEffectiveCap = -1;

    private static Vector2[]? _playerRestore;
    private static Vector2[]? _npcRestore;
    private static Vector2[]? _projectileRestore;
    private static Vector2[]? _itemRestore;
    private static int[]? _playerChanged;
    private static int[]? _npcChanged;
    private static int[]? _projectileChanged;
    private static int[]? _itemChanged;

    private static int _playerChangedCount;
    private static int _npcChangedCount;
    private static int _projectileChangedCount;
    private static int _itemChangedCount;
    private static Vector2 _savedScreenPosition;
    private static bool _screenPositionChanged;

    private static readonly Stopwatch MetricClock = Stopwatch.StartNew();
    private static int _framesSinceMetricSample;
    private static uint _lastGameUpdateCount;

    internal static double MeasuredFPS { get; private set; }
    internal static double MeasuredUPS { get; private set; }

    internal static bool Enabled
    {
        get
        {
            if (Main.dedServ)
                return false;

            return ModContent.GetInstance<TerraBoostConfig>().EnableHighFPS;
        }
    }

    internal static int EffectiveFrameCap
    {
        get
        {
            TerraBoostConfig config = ModContent.GetInstance<TerraBoostConfig>();
            if (config.FrameCap == 0)
                return 0;

            return Math.Max(60, config.FrameCap);
        }
    }

    internal static void Load()
    {
        if (_loaded || Main.dedServ)
            return;

        On_Main.DoDraw += Main_DoDraw;
        _lastGameUpdateCount = Main.GameUpdateCount;
        MetricClock.Restart();
        _loaded = true;
    }

    internal static void Unload()
    {
        if (!_loaded)
            return;

        On_Main.DoDraw -= Main_DoDraw;
        RestoreRuntimeSettings();

        _playerRestore = null;
        _npcRestore = null;
        _projectileRestore = null;
        _itemRestore = null;
        _playerChanged = null;
        _npcChanged = null;
        _projectileChanged = null;
        _itemChanged = null;

        _nextFrameTimestamp = 0;
        _lastEffectiveCap = -1;
        MeasuredFPS = 0;
        MeasuredUPS = 0;
        _loaded = false;
    }

    internal static void RequestSettingsRefresh()
    {
        _settingsRefreshRequested = true;
        _nextFrameTimestamp = 0;
    }

    internal static void UpdateRuntimeSettings()
    {
        if (Main.dedServ)
            return;

        TerraBoostConfig config = ModContent.GetInstance<TerraBoostConfig>();

        if (config.EnableHighFPS)
        {
            if (!_runtimeSettingsApplied)
            {
                _savedFrameSkipMode = Main.FrameSkipMode;
                _savedVSync = Main.graphics?.SynchronizeWithVerticalRetrace ?? true;
                _runtimeSettingsApplied = true;
                _settingsRefreshRequested = true;
            }

            Main.FrameSkipMode = FrameSkipMode.Off;

            if (_settingsRefreshRequested && Main.graphics is not null)
            {
                bool wantedVSync = config.DisableVSync ? false : _savedVSync;
                if (Main.graphics.SynchronizeWithVerticalRetrace != wantedVSync)
                {
                    Main.graphics.SynchronizeWithVerticalRetrace = wantedVSync;
                    Main.graphics.ApplyChanges();
                }
            }
        }
        else if (_runtimeSettingsApplied)
        {
            RestoreRuntimeSettings();
        }

        _settingsRefreshRequested = false;
    }

    private static void RestoreRuntimeSettings()
    {
        if (!_runtimeSettingsApplied)
            return;

        Main.FrameSkipMode = _savedFrameSkipMode;

        if (Main.graphics is not null && Main.graphics.SynchronizeWithVerticalRetrace != _savedVSync)
        {
            Main.graphics.SynchronizeWithVerticalRetrace = _savedVSync;
            Main.graphics.ApplyChanges();
        }

        _runtimeSettingsApplied = false;
        _settingsRefreshRequested = true;
        _nextFrameTimestamp = 0;
    }

    private static void Main_DoDraw(On_Main.orig_DoDraw orig, Main self, GameTime gameTime)
    {
        TerraBoostConfig config = ModContent.GetInstance<TerraBoostConfig>();

        if (!config.EnableHighFPS)
        {
            orig(self, gameTime);
            RecordRenderedFrame();
            return;
        }

        int cap = GetCapForCurrentFocusState(config);
        PaceFrame(cap);

        bool interpolated = false;
        try
        {
            if (config.SmoothEntityMotion && !Main.gameMenu)
            {
                ApplyInterpolation(config);
                interpolated = true;
            }

            orig(self, gameTime);
        }
        finally
        {
            if (interpolated)
                RestoreInterpolatedPositions();

            RecordRenderedFrame();
        }
    }

    private static int GetCapForCurrentFocusState(TerraBoostConfig config)
    {
        if (config.LimitFPSWhenUnfocused && Main.instance is not null && !Main.instance.IsActive)
            return Math.Max(15, config.UnfocusedFPSCap);

        if (Main.gameMenu && config.LimitMenuFPS)
        {
            int menuCap = Math.Clamp(config.MenuFPSCap, 60, 360);
            if (config.FrameCap == 0)
                return menuCap;

            return Math.Min(Math.Max(60, config.FrameCap), menuCap);
        }

        if (config.FrameCap == 0)
            return 0;

        return Math.Max(60, config.FrameCap);
    }

    private static void PaceFrame(int cap)
    {
        if (cap <= 0)
        {
            _nextFrameTimestamp = 0;
            _lastEffectiveCap = 0;
            return;
        }

        long now = Stopwatch.GetTimestamp();
        double ticksPerFrame = (double)Stopwatch.Frequency / cap;

        if (_lastEffectiveCap != cap || _nextFrameTimestamp == 0)
        {
            _lastEffectiveCap = cap;
            _nextFrameTimestamp = now + (long)ticksPerFrame;
            return;
        }

        if (now > _nextFrameTimestamp + ticksPerFrame * 4.0)
        {
            _nextFrameTimestamp = now + (long)ticksPerFrame;
            return;
        }

        while (now < _nextFrameTimestamp)
        {
            long remainingTicks = _nextFrameTimestamp - now;
            double remainingMs = remainingTicks * 1000.0 / Stopwatch.Frequency;

            if (remainingMs > 2.0)
                Thread.Sleep(Math.Max(0, (int)remainingMs - 1));
            else if (remainingMs > 0.35)
                Thread.Yield();
            else
                Thread.SpinWait(8);

            now = Stopwatch.GetTimestamp();
        }

        _nextFrameTimestamp += (long)ticksPerFrame;
    }

    private static void ApplyInterpolation(TerraBoostConfig config)
    {
        EnsureBuffers();
        ResetChangedCounts();

        float interpolationBackstep = (float)(Main.UpdateTimeAccumulator / LogicStepSeconds) - 1f;
        interpolationBackstep = MathHelper.Clamp(interpolationBackstep, -1f, 0f);

        if (Math.Abs(interpolationBackstep) < 0.0001f)
            return;

        InterpolatePlayers(interpolationBackstep, config);
        InterpolateNPCs(interpolationBackstep, config);
        InterpolateProjectiles(interpolationBackstep, config);

        if (config.SmoothWorldItems)
            InterpolateItems(interpolationBackstep, config);

        if (config.SmoothCamera)
            InterpolateCamera(interpolationBackstep);
    }

    private static void EnsureBuffers()
    {
        ResizeBuffers(Main.player?.Length ?? 0, ref _playerRestore, ref _playerChanged);
        ResizeBuffers(Main.npc?.Length ?? 0, ref _npcRestore, ref _npcChanged);
        ResizeBuffers(Main.projectile?.Length ?? 0, ref _projectileRestore, ref _projectileChanged);
        ResizeBuffers(Main.item?.Length ?? 0, ref _itemRestore, ref _itemChanged);
    }

    private static void ResizeBuffers(int required, ref Vector2[]? restore, ref int[]? changed)
    {
        if (required <= 0)
            return;

        if (restore is null || restore.Length < required)
            restore = new Vector2[required];

        if (changed is null || changed.Length < required)
            changed = new int[required];
    }

    private static void ResetChangedCounts()
    {
        _playerChangedCount = 0;
        _npcChangedCount = 0;
        _projectileChangedCount = 0;
        _itemChangedCount = 0;
        _screenPositionChanged = false;
    }

    private static bool IsSafeInterpolationOffset(Vector2 offset)
    {
        return offset.LengthSquared() <= MaximumInterpolationDistance * MaximumInterpolationDistance;
    }

    private static bool IsNearScreen(Vector2 position, int width, int height, TerraBoostConfig config)
    {
        if (!config.InterpolateOnlyNearScreen)
            return true;

        float margin = Math.Max(200, config.InterpolationCullMargin);
        float left = Main.screenPosition.X - margin;
        float top = Main.screenPosition.Y - margin;
        float right = Main.screenPosition.X + Main.screenWidth + margin;
        float bottom = Main.screenPosition.Y + Main.screenHeight + margin;

        return position.X + Math.Max(1, width) >= left &&
               position.X <= right &&
               position.Y + Math.Max(1, height) >= top &&
               position.Y <= bottom;
    }

    private static void InterpolatePlayers(float factor, TerraBoostConfig config)
    {
        if (Main.player is null || _playerRestore is null || _playerChanged is null)
            return;

        for (int i = 0; i < Main.player.Length; i++)
        {
            Player player = Main.player[i];
            if (player is null || !player.active || !IsNearScreen(player.position, player.width, player.height, config))
                continue;

            Vector2 offset = player.velocity * factor;
            if (!IsSafeInterpolationOffset(offset))
                continue;

            _playerRestore[i] = player.position;
            _playerChanged[_playerChangedCount++] = i;
            player.position += offset;
        }
    }

    private static void InterpolateNPCs(float factor, TerraBoostConfig config)
    {
        if (Main.npc is null || _npcRestore is null || _npcChanged is null)
            return;

        for (int i = 0; i < Main.npc.Length; i++)
        {
            NPC npc = Main.npc[i];
            if (npc is null || !npc.active || !IsNearScreen(npc.position, npc.width, npc.height, config))
                continue;

            Vector2 offset = npc.velocity * factor;
            if (!IsSafeInterpolationOffset(offset))
                continue;

            _npcRestore[i] = npc.position;
            _npcChanged[_npcChangedCount++] = i;
            npc.position += offset;
        }
    }

    private static void InterpolateProjectiles(float factor, TerraBoostConfig config)
    {
        if (Main.projectile is null || _projectileRestore is null || _projectileChanged is null)
            return;

        for (int i = 0; i < Main.projectile.Length; i++)
        {
            Projectile projectile = Main.projectile[i];
            if (projectile is null || !projectile.active || !IsNearScreen(projectile.position, projectile.width, projectile.height, config))
                continue;

            Vector2 offset = projectile.velocity * factor;
            if (!IsSafeInterpolationOffset(offset))
                continue;

            _projectileRestore[i] = projectile.position;
            _projectileChanged[_projectileChangedCount++] = i;
            projectile.position += offset;
        }
    }

    private static void InterpolateItems(float factor, TerraBoostConfig config)
    {
        if (Main.item is null || _itemRestore is null || _itemChanged is null)
            return;

        for (int i = 0; i < Main.item.Length; i++)
        {
            Item item = Main.item[i];
            if (item is null || !item.active || !IsNearScreen(item.position, item.width, item.height, config))
                continue;

            Vector2 offset = item.velocity * factor;
            if (!IsSafeInterpolationOffset(offset))
                continue;

            _itemRestore[i] = item.position;
            _itemChanged[_itemChangedCount++] = i;
            item.position += offset;
        }
    }

    private static void InterpolateCamera(float factor)
    {
        Player player = Main.LocalPlayer;
        if (player is null || !player.active)
            return;

        Vector2 offset = player.velocity * factor;
        if (!IsSafeInterpolationOffset(offset))
            return;

        _savedScreenPosition = Main.screenPosition;
        Main.screenPosition += offset;
        _screenPositionChanged = true;
    }

    private static void RestoreInterpolatedPositions()
    {
        if (_playerChanged is not null && _playerRestore is not null && Main.player is not null)
        {
            for (int n = 0; n < _playerChangedCount; n++)
            {
                int i = _playerChanged[n];
                if (i >= 0 && i < Main.player.Length && Main.player[i] is not null)
                    Main.player[i].position = _playerRestore[i];
            }
        }

        if (_npcChanged is not null && _npcRestore is not null && Main.npc is not null)
        {
            for (int n = 0; n < _npcChangedCount; n++)
            {
                int i = _npcChanged[n];
                if (i >= 0 && i < Main.npc.Length && Main.npc[i] is not null)
                    Main.npc[i].position = _npcRestore[i];
            }
        }

        if (_projectileChanged is not null && _projectileRestore is not null && Main.projectile is not null)
        {
            for (int n = 0; n < _projectileChangedCount; n++)
            {
                int i = _projectileChanged[n];
                if (i >= 0 && i < Main.projectile.Length && Main.projectile[i] is not null)
                    Main.projectile[i].position = _projectileRestore[i];
            }
        }

        if (_itemChanged is not null && _itemRestore is not null && Main.item is not null)
        {
            for (int n = 0; n < _itemChangedCount; n++)
            {
                int i = _itemChanged[n];
                if (i >= 0 && i < Main.item.Length && Main.item[i] is not null)
                    Main.item[i].position = _itemRestore[i];
            }
        }

        if (_screenPositionChanged)
            Main.screenPosition = _savedScreenPosition;

        ResetChangedCounts();
    }

    private static void RecordRenderedFrame()
    {
        _framesSinceMetricSample++;
        double elapsed = MetricClock.Elapsed.TotalSeconds;
        if (elapsed < 0.5)
            return;

        MeasuredFPS = _framesSinceMetricSample / elapsed;

        uint nowUpdates = Main.GameUpdateCount;
        uint updateDelta = unchecked(nowUpdates - _lastGameUpdateCount);
        MeasuredUPS = updateDelta / elapsed;

        _lastGameUpdateCount = nowUpdates;
        _framesSinceMetricSample = 0;
        MetricClock.Restart();
    }
}
