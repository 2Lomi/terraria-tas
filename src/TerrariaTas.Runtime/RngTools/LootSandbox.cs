using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria;
using Terraria.GameContent;
using Terraria.Utilities;
using TerrariaTas.Core.Studio;

namespace TerrariaTas.Runtime.RngTools;

/// <summary>
/// Runs an NPC's loot code with a chosen generator, then undoes everything it changed: the items it
/// spawned, the item-slot reuse timers, pending emergency-stacking transfers and the NPC's own fields
/// (some drop rules zero its coin value). Only use it at a frame boundary, on the main thread.
/// </summary>
internal sealed class LootSandbox
{
    private static readonly Action<NPC, Player> BeforeLoot = LootStep(AccessTools.Method(typeof(NPC), "DoDeathEvents_BeforeLoot"));
    private static readonly Action<NPC, Player> DropItems = LootStep(AccessTools.Method(typeof(NPC), "NPCLoot_DropItems"));
    private static readonly Action<NPC, Player> DropMoney = LootStep(AccessTools.Method(typeof(NPC), "NPCLoot_DropMoney"));
    private static readonly Action<NPC, Player> DropHeals = LootStep(AccessTools.Method(typeof(NPC), "NPCLoot_DropHeals"));
    private static readonly IList PendingTransfers = (IList)AccessTools.Field(typeof(EmergencyStacking), "PendingTransfers").GetValue(null);
    private static readonly MethodInfo ShallowClone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly Action<NPC, NPC> CopyNpcFields = FieldCopier.Create<NPC>();

    private readonly NPC _npc;
    private readonly Player _player;
    private readonly NPC _npcBefore;
    private readonly UnifiedRandom _shared = Main.rand;
    private readonly WorldItem[] _items = SpawnedLoot.CopyItemTable();
    private readonly int[] _slotTimers = (int[])Main.timeItemSlotCannotBeReusedFor.Clone();
    private readonly object[] _transfers = PendingTransfers.Cast<object>().ToArray();

    public LootSandbox(NPC npc, Player player)
    {
        _npc = npc;
        _player = player;
        _npcBefore = (NPC)ShallowClone.Invoke(npc, null);
    }

    /// <summary>The loot <c>NPC.NPCLoot</c> would give if <paramref name="generator"/> were the stream it rolls on.</summary>
    public DropOutcome Roll(UnifiedRandom generator)
    {
        // The drop rules use the generator they are handed, but luck rolls and item spawning read Main.rand.
        Main.rand = generator;
        try
        {
            var noLoot = LootGate.Check(_npc);
            if (noLoot != null)
                return new DropOutcome { NoLootReason = noLoot };

            // NPCLoot's order, minus kill counting and DoDeathEvents, which change the world (flags, spawns, chat).
            BeforeLoot(_npc, _player);
            DropItems(_npc, _player);
            DropMoney(_npc, _player);
            DropHeals(_npc, _player);
            return SpawnedLoot.Since(_items);
        }
        finally
        {
            Undo();
        }
    }

    /// <summary>Fails loudly if a roll left anything behind: a forecast must never change the game.</summary>
    public void EnsureUndone()
    {
        if (Main.rand != _shared || !Main.item.SequenceEqual(_items) || !Main.timeItemSlotCannotBeReusedFor.SequenceEqual(_slotTimers))
            throw new GameStateChangedException("The drop forecast changed the game state. Report this as a bug.");
    }

    private void Undo()
    {
        Main.rand = _shared;
        Array.Copy(_items, Main.item, _items.Length);
        Array.Copy(_slotTimers, Main.timeItemSlotCannotBeReusedFor, _slotTimers.Length);
        PendingTransfers.Clear();
        foreach (var transfer in _transfers)
            PendingTransfers.Add(transfer);
        CopyNpcFields(_npcBefore, _npc);
    }

    private static Action<NPC, Player> LootStep(MethodInfo method) => AccessTools.MethodDelegate<Action<NPC, Player>>(method);
}

/// <summary>A prediction left the game changed: the TAS can no longer trust its state and must stop.</summary>
internal sealed class GameStateChangedException : Exception
{
    public GameStateChangedException(string message)
        : base(message)
    {
    }
}
