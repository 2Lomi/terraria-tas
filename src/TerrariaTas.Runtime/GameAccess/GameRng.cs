using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Terraria;
using Terraria.Utilities;
using TerrariaTas.Core.Rng;

namespace TerrariaTas.Runtime.GameAccess;

/// <summary>
/// The game's random number generators. Since 1.4.5 every world-update phase runs on its own stream
/// (<c>Main.SwapRandom("SpawnNPCs")</c>, "UpdateNPCs", "UpdateDust"...), seeded from the world seed on first use.
/// Outside those phases (menus, UI, drawing, weather) the base <c>Main.rand</c> is used.
/// All of them are [ThreadStatic]: only touch them from the main thread.
/// </summary>
internal static class GameRng
{
    private static readonly FieldInfo StreamsField = AccessTools.Field(typeof(Main), "_rngs");
    private static readonly FieldInfo StateField = AccessTools.Field(typeof(UnifiedRandom), "SeedArray");
    private static readonly FieldInfo IndexField = AccessTools.Field(typeof(UnifiedRandom), "inext");

    public static Dictionary<string, UnifiedRandom> Streams => (Dictionary<string, UnifiedRandom>)StreamsField.GetValue(null);

    /// <summary>True for the generator shared by weather, UI and visuals (Main.rand outside any swap).</summary>
    public static bool IsShared(UnifiedRandom random) =>
        random == Main.rand && !Streams.ContainsValue(random);

    /// <summary>Makes every generator the movie depends on start from a known state.</summary>
    public static void ResetForMovieStart(int seed)
    {
        Main.rand = new UnifiedRandom(seed);
        Streams.Clear();
    }

    /// <summary>Copies a game generator into the prediction model of TerrariaTas.Core.</summary>
    public static TerrariaRandom ToModel(UnifiedRandom random) =>
        TerrariaRandom.FromState((int[])StateField.GetValue(random), (uint)IndexField.GetValue(random));
}
