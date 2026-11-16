using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Terraria;

namespace TerrariaTas.Runtime.WorldEntry;

/// <summary>
/// Enters the movie's world straight from the title screen with the movie's player,
/// the same way Terraria's own QuickLoad debug tool does.
/// </summary>
internal sealed class WorldLoader
{
    private static volatile bool _loadFinished;

    public void Install(Harmony harmony) => harmony.CreateClassProcessor(typeof(Hooks)).Patch();

    /// <summary>
    /// True once Terraria's world-loading thread has fully returned. Terraria leaves the menu
    /// slightly before that thread ends, so the movie must not start earlier.
    /// </summary>
    public bool LoadFinished => _loadFinished;

    /// <summary>Terraria gave up loading the world (it shows "Load failed" in the menu).</summary>
    public bool LoadFailed => _loadFinished && WorldGen.loadFailed;

    public void Start(string playerFile, string worldFile)
    {
        var playerPath = Path.Combine(Main.PlayerPath, playerFile);
        var worldPath = Path.Combine(Main.WorldPath, worldFile);

        Main.LoadPlayers();
        var player = Main.PlayerList.FirstOrDefault(p => SamePath(p.Path, playerPath))
                     ?? throw new FileNotFoundException("Movie player not found. Copy it into the TAS data folder.", playerPath);
        Main.LoadWorlds();
        var world = Main.WorldList.FirstOrDefault(w => SamePath(w.Path, worldPath))
                    ?? throw new FileNotFoundException("Movie world not found. Copy it into the TAS data folder.", worldPath);

        Main.SelectPlayer(player);
        world.SetAsActive();
        _loadFinished = false;
        Main.menuMode = 10;
        WorldGen.playWorld();
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    [HarmonyPatch]
    private static class Hooks
    {
        [HarmonyPatch(typeof(WorldGen), nameof(WorldGen.playWorldCallBack)), HarmonyPostfix]
        private static void LoadFinished() => _loadFinished = true;
    }
}
