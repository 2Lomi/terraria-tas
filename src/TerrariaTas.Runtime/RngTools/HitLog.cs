using System.Collections.Generic;
using HarmonyLib;
using Terraria;
using TerrariaTas.Core;
using TerrariaTas.Core.Studio;
using TerrariaTas.Runtime.GameAccess;

namespace TerrariaTas.Runtime.RngTools;

/// <summary>
/// Records every hit of the local player on an NPC, by a swing or by a projectile: the stream it rolled on, where its
/// first roll was in the frame (the offset a damage forecast taken at the frame start would show), the crit and the
/// damage. It only reads, so it runs in every mode, --verify included.
/// </summary>
/// <remarks>
/// The game's hit code runs for every NPC a swing or a projectile could touch, every frame, so entering it only stores
/// references. The stream is read when the damage is actually rolled; the crit rolls came right before that roll.
/// </remarks>
internal sealed class HitLog
{
    private static HitLog? _active;

    private readonly RngTracker _rng;
    private List<HitRecord> _records = new();

    // The hit the game is processing, between entering and leaving its hit code.
    private bool _inHit;
    private HitKind _kind;
    private Item? _item;
    private Projectile? _projectile;
    private string _stream = "";
    private bool _rolled;
    private long _rollOffset = -1;
    private int _rolledDamage = -1;

    public HitLog(RngTracker rng)
    {
        _rng = rng;
    }

    public void Install(Harmony harmony)
    {
        _active = this;
        harmony.CreateClassProcessor(typeof(Hooks)).Patch();
    }

    /// <summary>Hits since the previous call. Their frame is left to the caller, which owns the timeline.</summary>
    public List<HitRecord> TakeRecords()
    {
        Leave(); // at a frame boundary no hit is being processed
        var records = _records;
        _records = new List<HitRecord>();
        return records;
    }

    /// <summary>The number of crit rolls a projectile makes: one per class it has, none for traps and NPC projectiles.</summary>
    [MirrorsGameMethod("Terraria.Projectile", "Damage_PVE_Inner")]
    private static int CritRolls(Projectile projectile) =>
        !projectile.OwnedBySomeone ? 0 : (projectile.melee ? 1 : 0) + (projectile.ranged ? 1 : 0) + (projectile.magic ? 1 : 0);

    private void EnterSwing(Player player, Item item)
    {
        if (player.whoAmI != Main.myPlayer)
            return;
        Enter(HitKind.Swing);
        _item = item;
    }

    private void EnterProjectile(Projectile projectile)
    {
        // In single player the projectiles of town NPCs and of traps (falling stars too) belong to the local player as
        // well, but they are not the player's hits: the game rolls them no crit and no luck.
        if (projectile.owner != Main.myPlayer || !projectile.OwnedBySomeone)
            return;
        Enter(HitKind.Projectile);
        _projectile = projectile;
    }

    private void Enter(HitKind kind)
    {
        _inHit = true;
        _kind = kind;
        _rolled = false;
        _rollOffset = -1;
        _rolledDamage = -1;
    }

    private void Leave()
    {
        _inHit = false;
        _item = null;
        _projectile = null;
    }

    private void BeforeDamageRoll()
    {
        // The streams are [ThreadStatic]: a hit outside the main thread has none to read.
        if (!_inHit || GameRng.Streams == null)
            return;

        _rolled = true;
        _stream = GameRng.CurrentName();
        _rollOffset = _rng.DrawsThisFrame(_stream, Main.rand); // -1 when unknown: the hit is still recorded
    }

    private void AfterDamageRoll(int damage)
    {
        if (_inHit)
            _rolledDamage = damage;
    }

    private void Struck(NPC npc, bool crit, int dealt)
    {
        if (!_inHit || !_rolled)
            return;

        var critRolls = _kind == HitKind.Swing ? 1 : _projectile == null ? 0 : CritRolls(_projectile);
        var offset = _rollOffset < 0 ? -1 : _rollOffset - critRolls;
        _records.Add(new HitRecord
        {
            NpcSlot = npc.whoAmI,
            NetId = npc.netID,
            NpcName = npc.FullName,
            Kind = _kind,
            Source = _kind == HitKind.Swing ? _item?.AffixName() ?? "" : _projectile == null ? "" : Lang.GetProjectileName(_projectile.type).Value,
            SourceId = _kind == HitKind.Swing ? _item?.type ?? 0 : _projectile?.type ?? 0,
            Stream = _stream,
            Offset = offset < 0 ? -1 : offset,
            Crit = crit,
            Damage = _rolledDamage,
            Dealt = dealt,
        });

        // One strike per damage roll: anything struck later in the same hit code rolled nothing of its own.
        _rolled = false;
    }

    [HarmonyPatch]
    private static class Hooks
    {
        [HarmonyPatch(typeof(Player), "ProcessHitAgainstNPC"), HarmonyPrefix]
        private static void SwingPrefix(Player __instance, Item sItem) => _active?.EnterSwing(__instance, sItem);

        [HarmonyPatch(typeof(Player), "ProcessHitAgainstNPC"), HarmonyPostfix]
        private static void SwingPostfix() => _active?.Leave();

        [HarmonyPatch(typeof(Projectile), "Damage_PVE_Inner"), HarmonyPrefix]
        private static void ProjectilePrefix(Projectile __instance) => _active?.EnterProjectile(__instance);

        [HarmonyPatch(typeof(Projectile), "Damage_PVE_Inner"), HarmonyPostfix]
        private static void ProjectilePostfix() => _active?.Leave();

        [HarmonyPatch(typeof(Main), nameof(Main.DamageVar)), HarmonyPrefix]
        private static void DamageRollPrefix() => _active?.BeforeDamageRoll();

        [HarmonyPatch(typeof(Main), nameof(Main.DamageVar)), HarmonyPostfix]
        private static void DamageRollPostfix(int __result) => _active?.AfterDamageRoll(__result);

        [HarmonyPatch(typeof(NPC), nameof(NPC.StrikeNPC)), HarmonyPostfix]
        private static void StrikePostfix(NPC __instance, bool crit, int __result) => _active?.Struck(__instance, crit, __result);
    }
}
