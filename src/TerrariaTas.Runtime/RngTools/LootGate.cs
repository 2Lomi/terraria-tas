using Terraria;
using Terraria.ID;
using TerrariaTas.Core;

namespace TerrariaTas.Runtime.RngTools;

/// <summary>
/// The early exits of <c>NPC.NPCLoot</c>, copied because the forecast cannot call NPCLoot itself
/// (it also counts the kill for banners, the bestiary and achievements, and runs death events).
/// </summary>
[MirrorsGameMethod("Terraria.NPC", "NPCLoot")]
internal static class LootGate
{
    private const int MultiplayerClient = 1;

    /// <returns>Why NPCLoot would end without rolling anything, or null when it rolls the loot.</returns>
    public static string? Check(NPC npc)
    {
        if (Main.netMode == MultiplayerClient || npc.type >= NPCID.Count)
            return "rolled by the server";

        if ((Main.getGoodWorld || !Main.isThereAWorldSurface) && !NPC.downedBoss3 && !Main.onlyShimmerOceanWorlds && IsEarlyDungeonEnemy(npc.type))
            return "dungeon enemy before Skeletron";

        if (npc.type == NPCID.DarkCaster && npc.localAI[3] == 1f)
            return "Dark Caster clone";

        if (npc.SpawnedFromStatue && NPCID.Sets.NoEarlymodeLootWhenSpawnedFromStatue[npc.type] && !Main.hardMode)
            return "statue enemy before hardmode";

        // The game draws this number on the loot stream, before any drop rule.
        var rarity = NPCID.Sets.StatueSpawnedDropRarity[npc.type];
        if (npc.SpawnedFromStatue && rarity != -1f && (Main.rand.NextFloat() >= rarity || !npc.AnyInteractions()))
            return "statue enemy, roll failed";

        return null;
    }

    private static bool IsEarlyDungeonEnemy(int type) =>
        type is NPCID.AngryBones or NPCID.AngryBonesBig or NPCID.AngryBonesBigMuscle or NPCID.AngryBonesBigHelmet
            or NPCID.DarkCaster or NPCID.CursedSkull or NPCID.DungeonSlime or NPCID.LibrarianSkeleton or NPCID.WaterBoltMimic;
}
