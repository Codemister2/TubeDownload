using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using TerraBoost.Config;
using TerraBoost.Core;

namespace TerraBoost.Systems;

public sealed class TerraBoostSystem : ModSystem
{
    public override void PostUpdateEverything()
    {
        if (Main.dedServ)
            return;

        HighFpsController.UpdateRuntimeSettings();
        VisualOptimizer.OnLogicTick();
    }

    public override void PostDrawInterface(SpriteBatch spriteBatch)
    {
        TerraBoostConfig config = ModContent.GetInstance<TerraBoostConfig>();
        if (!config.ShowPerformanceOverlay || Main.dedServ)
            return;

        string capText = config.FrameCap == 0 ? "Unlimited" : $"{System.Math.Max(60, config.FrameCap)}";
        string highFpsText = config.EnableHighFPS ? "ON" : "OFF";
        string optimizerText = config.EnableOptimizations ? config.Preset.ToString() : "OFF";
        string adaptive = (config.AdaptiveVisualQuality || config.Preset == OptimizationPreset.Auto) && config.EnableOptimizations
            ? $"  AQ {VisualOptimizer.AdaptiveScale:P0}"
            : string.Empty;

        string text =
            $"TerraBoost  High FPS {highFpsText}\n" +
            $"FPS {HighFpsController.MeasuredFPS:0.0} / {capText}   UPS {HighFpsController.MeasuredUPS:0.0}\n" +
            $"Optimizer {optimizerText}{adaptive}\n" +
            $"Dust {VisualOptimizer.ActiveDust}/{VisualOptimizer.EffectiveDustBudget}   Gore {VisualOptimizer.ActiveGore}   Rain {VisualOptimizer.EffectiveRainBudget}";

        Vector2 position = new(18f, 18f);
        Utils.DrawBorderString(spriteBatch, text, position, Color.White);
    }
}
