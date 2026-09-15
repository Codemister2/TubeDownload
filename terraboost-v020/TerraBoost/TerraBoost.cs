using Terraria;
using Terraria.ModLoader;
using TerraBoost.Core;

namespace TerraBoost;

public sealed class TerraBoost : Mod
{
    public override void Load()
    {
        if (!Main.dedServ)
            HighFpsController.Load();
    }

    public override void Unload()
    {
        if (!Main.dedServ)
            HighFpsController.Unload();

        VisualOptimizer.Unload();
    }
}
