using System.Collections.Generic;
using HarmonyLib;
using Terraria;
using TerrariaTas.Core.Studio;
using TerrariaTas.Runtime.GameAccess;

namespace TerrariaTas.Runtime.RngTools;

/// <summary>
/// Records every real kill: the stream that rolled the loot, how many draws that stream had already made
/// in the frame (the offset a drop forecast taken at the frame start would show) and what dropped.
/// It only reads, so it runs in every mode, --verify included.
/// </summary>
internal sealed class KillLog
{
    private static KillLog? _active;

    private readonly RngTracker _rng;
    private List<KillRecord> _records = new();

    public KillLog(RngTracker rng)
    {
        _rng = rng;
    }

    public void Install(Harmony harmony)
    {
        _active = this;
        harmony.CreateClassProcessor(typeof(Hooks)).Patch();
    }

    /// <summary>Kills since the previous call. Their frame is left to the caller, which owns the timeline.</summary>
    public List<KillRecord> TakeRecords()
    {
        var records = _records;
        _records = new List<KillRecord>();
        return records;
    }

    private KillContext? Begin()
    {
        // The streams are [ThreadStatic]: a kill outside the main thread has none to read.
        if (GameRng.Streams == null)
            return null;

        var stream = GameRng.CurrentName();
        var offset = _rng.DrawsThisFrame(stream, Main.rand);
        var start = _rng.PositionOf(stream);
        return new KillContext(stream, offset, offset < 0 || start < 0 ? -1 : start + offset, SpawnedLoot.CopyItemTable());
    }

    private void End(NPC npc, KillContext context) => _records.Add(new KillRecord
    {
        NpcSlot = npc.whoAmI,
        NetId = npc.netID,
        NpcName = npc.FullName,
        Stream = context.Stream,
        Offset = context.Offset,
        Position = context.Position,
        Drops = SpawnedLoot.Since(context.ItemsBefore),
    });

    private sealed class KillContext
    {
        public KillContext(string stream, long offset, long position, WorldItem[] itemsBefore)
        {
            Stream = stream;
            Offset = offset;
            Position = position;
            ItemsBefore = itemsBefore;
        }

        public string Stream { get; }

        public long Offset { get; }

        public long Position { get; }

        public WorldItem[] ItemsBefore { get; }
    }

    [HarmonyPatch]
    private static class Hooks
    {
        [HarmonyPatch(typeof(NPC), nameof(NPC.NPCLoot)), HarmonyPrefix]
        private static void LootPrefix(out KillContext? __state) => __state = _active?.Begin();

        [HarmonyPatch(typeof(NPC), nameof(NPC.NPCLoot)), HarmonyPostfix]
        private static void LootPostfix(NPC __instance, KillContext? __state)
        {
            if (__state != null)
                _active!.End(__instance, __state);
        }
    }
}
