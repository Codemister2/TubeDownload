using System;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using TerraBoost.Config;
using TerraBoost.Core;

namespace TerraBoost.Commands;

public sealed class TerraBoostCommand : ModCommand
{
    public override string Command => "terraboost";
    public override CommandType Type => CommandType.Chat;
    public override string Usage => "/terraboost [status|on|off|optimize <on|off>|cap <60-1000|unlimited>|overlay|profile <auto|balanced|performance|lowend|potato|custom>]";
    public override string Description => "Controls TerraBoost's high-FPS and optimization settings for this session.";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        TerraBoostConfig config = ModContent.GetInstance<TerraBoostConfig>();

        if (args.Length == 0 || args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            string cap = config.FrameCap == 0 ? "Unlimited" : Math.Max(60, config.FrameCap).ToString();
            string optimizer = config.EnableOptimizations ? config.Preset.ToString() : "OFF";
            caller.Reply($"TerraBoost: High FPS {(config.EnableHighFPS ? "ON" : "OFF")}, cap {cap}, optimizer {optimizer}, measured {HighFpsController.MeasuredFPS:0.0} FPS / {HighFpsController.MeasuredUPS:0.0} UPS.", Color.LightGreen);
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "on":
                config.EnableHighFPS = true;
                HighFpsController.RequestSettingsRefresh();
                caller.Reply("TerraBoost High FPS enabled for this session.", Color.LightGreen);
                break;

            case "off":
                config.EnableHighFPS = false;
                HighFpsController.RequestSettingsRefresh();
                caller.Reply("TerraBoost High FPS disabled for this session.", Color.Orange);
                break;

            case "optimize":
            case "optimise":
                SetOptimizer(caller, config, args);
                break;

            case "overlay":
                config.ShowPerformanceOverlay = !config.ShowPerformanceOverlay;
                caller.Reply($"TerraBoost overlay {(config.ShowPerformanceOverlay ? "enabled" : "disabled")} for this session.", Color.LightGreen);
                break;

            case "cap":
                SetCap(caller, config, args);
                break;

            case "profile":
            case "preset":
                SetProfile(caller, config, args);
                break;

            default:
                caller.Reply(Usage, Color.Orange);
                break;
        }
    }

    private static void SetOptimizer(CommandCaller caller, TerraBoostConfig config, string[] args)
    {
        if (args.Length < 2 ||
            (!args[1].Equals("on", StringComparison.OrdinalIgnoreCase) && !args[1].Equals("off", StringComparison.OrdinalIgnoreCase)))
        {
            caller.Reply("Usage: /terraboost optimize <on|off>", Color.Orange);
            return;
        }

        config.EnableOptimizations = args[1].Equals("on", StringComparison.OrdinalIgnoreCase);
        VisualOptimizer.RequestSettingsRefresh();
        caller.Reply($"TerraBoost optimizer {(config.EnableOptimizations ? "enabled" : "disabled")} for this session.", Color.LightGreen);
    }

    private static void SetCap(CommandCaller caller, TerraBoostConfig config, string[] args)
    {
        if (args.Length < 2)
        {
            caller.Reply("Usage: /terraboost cap <60-1000|unlimited>", Color.Orange);
            return;
        }

        if (args[1].Equals("unlimited", StringComparison.OrdinalIgnoreCase) || args[1].Equals("inf", StringComparison.OrdinalIgnoreCase))
        {
            config.FrameCap = 0;
            HighFpsController.RequestSettingsRefresh();
            caller.Reply("Frame cap set to Unlimited for this session.", Color.LightGreen);
            return;
        }

        if (!int.TryParse(args[1], out int cap))
        {
            caller.Reply("Cap must be a number from 60-1000, or 'unlimited'.", Color.OrangeRed);
            return;
        }

        config.FrameCap = Math.Clamp(cap, 60, 1000);
        HighFpsController.RequestSettingsRefresh();
        caller.Reply($"Frame cap set to {config.FrameCap} FPS for this session.", Color.LightGreen);
    }

    private static void SetProfile(CommandCaller caller, TerraBoostConfig config, string[] args)
    {
        if (args.Length < 2)
        {
            caller.Reply("Profiles: auto, balanced, performance, lowend, potato, custom", Color.Orange);
            return;
        }

        string value = args[1].Replace("-", string.Empty).Replace("_", string.Empty);
        OptimizationPreset preset;

        if (value.Equals("low", StringComparison.OrdinalIgnoreCase) || value.Equals("lowend", StringComparison.OrdinalIgnoreCase))
            preset = OptimizationPreset.LowEnd;
        else if (!Enum.TryParse(value, true, out preset))
        {
            caller.Reply("Profiles: auto, balanced, performance, lowend, potato, custom", Color.Orange);
            return;
        }

        config.Preset = preset;
        config.EnableOptimizations = true;
        VisualOptimizer.RequestSettingsRefresh();
        caller.Reply($"Optimization profile set to {preset} for this session.", Color.LightGreen);
    }
}
