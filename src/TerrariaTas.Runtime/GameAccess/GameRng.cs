using System.Collections.Generic;
using HarmonyLib;
using Terraria;
using Terraria.Utilities;
using TerrariaTas.Core;
using TerrariaTas.Core.Rng;

namespace TerrariaTas.Runtime.GameAccess;

/// <summary>
/// The game's random number generators. Since 1.4.5 every world-update phase runs on its own stream
/// (<c>Main.SwapRandom("SpawnNPCs")</c>, "UpdateNPCs", "UpdateDust"...). Every stream is created on first
/// use with the world seed and lives until the game closes. Outside those phases (menus, UI, drawing,
/// weather) the shared <c>Main.rand</c> is used. All of them are [ThreadStatic]: only touch them from the main thread.
/// </summary>
internal static class GameRng
{
    /// <summary>Name given to the shared generator, which has no stream name of its own.</summary>
    public const string SharedName = "Main.rand";

    private static readonly AccessTools.FieldRef<UnifiedRandom, int[]> State =
        AccessTools.FieldRefAccess<UnifiedRandom, int[]>(AccessTools.Field(typeof(UnifiedRandom), "SeedArray"));

    private static readonly AccessTools.FieldRef<UnifiedRandom, uint> Index =
        AccessTools.FieldRefAccess<UnifiedRandom, uint>(AccessTools.Field(typeof(UnifiedRandom), "inext"));

    private static readonly AccessTools.FieldRef<Dictionary<string, UnifiedRandom>> StreamTable =
        AccessTools.StaticFieldRefAccess<Dictionary<string, UnifiedRandom>>(AccessTools.Field(typeof(Main), "_rngs"));

    public static Dictionary<string, UnifiedRandom> Streams => StreamTable();

    /// <summary>The seed every stream starts from.</summary>
    public static int WorldSeed => Main.ActiveWorldFileData.Seed;

    /// <summary>True for the generator shared by weather, UI and visuals (Main.rand outside any swap).</summary>
    public static bool IsShared(UnifiedRandom random) =>
        random == Main.rand && !Streams.ContainsValue(random);

    /// <summary>Name of the generator Main.rand currently is: a stream inside a world-update phase, else the shared one.</summary>
    public static string CurrentName()
    {
        foreach (var stream in Streams)
        {
            if (stream.Value == Main.rand)
                return stream.Key;
        }

        return SharedName;
    }

    /// <summary>
    /// Every generator by name, the shared one first. Only meaningful at a frame boundary, where no
    /// world-update phase has swapped Main.rand.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, UnifiedRandom>> AtFrameBoundary()
    {
        yield return new KeyValuePair<string, UnifiedRandom>(SharedName, Main.rand);
        foreach (var stream in Streams)
            yield return stream;
    }

    /// <summary>Makes every generator the movie depends on start from a known state.</summary>
    public static void ResetForMovieStart(int seed)
    {
        Main.rand = new UnifiedRandom(seed);
        Streams.Clear();
    }

    /// <summary>Copies a game generator into the prediction model of TerrariaTas.Core.</summary>
    public static TerrariaRandom ToModel(UnifiedRandom random) => TerrariaRandom.FromState(State(random), Index(random));

    /// <summary>An independent game generator in the same state, for running game code without touching the original.</summary>
    public static UnifiedRandom Clone(UnifiedRandom random)
    {
        var copy = new UnifiedRandom(0);
        State(copy) = (int[])State(random).Clone();
        Index(copy) = Index(random);
        return copy;
    }

    /// <summary>
    /// An independent copy of a stream as the next frame will start drawing from it. A stream the game has not created
    /// yet (at the movie start there is none) is created from the world seed on its first use: the copy is that stream.
    /// Call at a frame boundary.
    /// </summary>
    [MirrorsGameMethod("Terraria.Main", "SwapRandom")]
    public static UnifiedRandom CloneForNextFrame(string name) =>
        Streams.TryGetValue(name, out var stream) ? Clone(stream) : new UnifiedRandom(WorldSeed);
}
