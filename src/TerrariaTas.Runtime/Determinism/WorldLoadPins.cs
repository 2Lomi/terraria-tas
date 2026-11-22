using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Terraria;
using Terraria.IO;
using Terraria.Utilities;

namespace TerrariaTas.Runtime.Determinism;

/// <summary>
/// Terraria loads a world on a thread of its own while the main thread keeps running the menu, and both draw from the
/// shared RNG (<c>WorldGen.genRand</c> is <c>Main.rand</c>): which numbers the loading thread gets depends on how the
/// two interleave. The ones that stay in the world are the liquids it settles (<c>Liquid.Update</c> rounds some tiles
/// up at random), so a world with water still moving, such as a newly generated one, started movies with other liquid
/// on every launch. While a world loads, liquids take their numbers from a generator of the loading thread, seeded
/// from the world: they settle the same way on every load. The other draws of loading (weather, clouds, critter
/// chances) are rolled again at the movie start (<see cref="EnvironmentPins.ResetWorldSession"/>).
/// </summary>
internal sealed class WorldLoadPins
{
    [ThreadStatic]
    private static UnifiedRandom? _loadingRandom;

    public void Install(Harmony harmony) => harmony.CreateClassProcessor(typeof(Hooks)).Patch();

    /// <summary>The generator liquids draw from: the loading thread's while it loads a world, the game's otherwise.</summary>
    private static UnifiedRandom LiquidRandom() => _loadingRandom ?? WorldGen.genRand;

    [HarmonyPatch]
    private static class Hooks
    {
        [HarmonyPatch(typeof(WorldFile), nameof(WorldFile.LoadWorld)), HarmonyPrefix]
        private static void BeforeLoad() => _loadingRandom = new UnifiedRandom(Main.ActiveWorldFileData.Seed);

        [HarmonyPatch(typeof(WorldFile), nameof(WorldFile.LoadWorld)), HarmonyFinalizer]
        private static void AfterLoad() => _loadingRandom = null;

        [HarmonyPatch(typeof(Liquid), nameof(Liquid.Update)), HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> LiquidsDrawFromLiquidRandom(IEnumerable<CodeInstruction> instructions)
        {
            var genRand = AccessTools.PropertyGetter(typeof(WorldGen), nameof(WorldGen.genRand));
            var code = instructions.ToList();
            var replaced = 0;
            foreach (var instruction in code.Where(instruction => instruction.Calls(genRand)))
            {
                instruction.operand = AccessTools.Method(typeof(WorldLoadPins), nameof(LiquidRandom));
                replaced++;
            }

            if (replaced == 0)
                throw new InvalidOperationException("Liquid.Update no longer draws from WorldGen.genRand: review WorldLoadPins.");
            return code;
        }
    }
}
