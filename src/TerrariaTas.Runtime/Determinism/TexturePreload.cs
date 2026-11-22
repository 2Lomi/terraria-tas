using System;
using System.Diagnostics;
using System.IO;
using Terraria;
using Terraria.ID;

namespace TerrariaTas.Runtime.Determinism;

/// <summary>
/// Loads every NPC and gore texture before the movie. Terraria loads them the first time something draws them, keeps
/// them for the life of the process, and gameplay reads whether they are loaded: until then an NPC's animation frame
/// does not advance (some AI reads the frame) and a gore has no size (its collision box decides where it lands and
/// what it touches). So what the game happened to draw before (an earlier attempt, frames played before loading a
/// savestate) changed the frames after it. Loaded up front, every attempt sees the same thing.
/// Item and projectile textures are always loaded right before gameplay measures them, and tile, wall and background
/// textures only matter to drawing.
/// </summary>
internal static class TexturePreload
{
    public static void LoadNpcsAndGores()
    {
        var watch = Stopwatch.StartNew();
        var memoryBefore = Process.GetCurrentProcess().PrivateMemorySize64;
        var images = Path.Combine(Path.GetFullPath(Main.instance.Content.RootDirectory), "Images");

        var npcs = 0;
        for (var type = 0; type < NPCID.Count; type++)
        {
            if (!File.Exists(Path.Combine(images, $"NPC_{type}.xnb")))
                continue; // a failed load opens an error dialog: only load what exists
            Main.instance.LoadNPC(type);
            npcs++;
        }

        var gores = 0;
        for (var type = 0; type < GoreID.Count; type++)
        {
            if (!File.Exists(Path.Combine(images, $"Gore_{type}.xnb")))
                continue;
            Main.instance.LoadGore(type);
            gores++;
        }

        var added = (Process.GetCurrentProcess().PrivateMemorySize64 - memoryBefore) / (1024 * 1024);
        Log.Info($"Loaded {npcs} NPC and {gores} gore textures in {watch.Elapsed.TotalSeconds:0.0} s (+{added} MB): "
                 + "gameplay reads whether they are loaded, so they all are from the start");
    }
}
