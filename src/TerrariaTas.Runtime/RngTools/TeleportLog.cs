using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaTas.Core.Studio;
using TerrariaTas.Runtime.GameAccess;

namespace TerrariaTas.Runtime.RngTools;

/// <summary>
/// Records every Teleportation Potion the local player drinks: the stream its destination rolled on, how many draws
/// that stream had already made in the frame (the offset a teleport forecast taken at the frame start would show) and
/// where the player went. It only reads, so it runs in every mode, --verify included.
/// </summary>
internal sealed class TeleportLog
{
    private static TeleportLog? _active;

    private readonly RngTracker _rng;
    private List<TeleportRecord> _records = new();

    public TeleportLog(RngTracker rng)
    {
        _rng = rng;
    }

    public void Install(Harmony harmony)
    {
        _active = this;
        harmony.CreateClassProcessor(typeof(Hooks)).Patch();
    }

    /// <summary>Teleports since the previous call. Their frame is left to the caller, which owns the timeline.</summary>
    public List<TeleportRecord> TakeRecords()
    {
        var records = _records;
        _records = new List<TeleportRecord>();
        return records;
    }

    private Departure? Begin(Player player)
    {
        // The streams are [ThreadStatic]: a teleport outside the main thread has none to read.
        if (GameRng.Streams == null || player.whoAmI != Main.myPlayer)
            return null;

        var stream = GameRng.CurrentName();
        var offset = _rng.DrawsThisFrame(stream, Main.rand);
        var start = _rng.PositionOf(stream);
        return new Departure(stream, offset, offset < 0 || start < 0 ? -1 : start + offset, player.position);
    }

    private void End(Player player, Departure departure) => _records.Add(new TeleportRecord
    {
        Kind = TeleportKind.Potion,
        Stream = departure.Stream,
        Offset = departure.Offset,
        Position = departure.Position,
        FromX = departure.From.X,
        FromY = departure.From.Y,
        ToX = player.position.X,
        ToY = player.position.Y,
    });

    private sealed class Departure
    {
        public Departure(string stream, long offset, long position, Vector2 from)
        {
            Stream = stream;
            Offset = offset;
            Position = position;
            From = from;
        }

        public string Stream { get; }

        public long Offset { get; }

        public long Position { get; }

        public Vector2 From { get; }
    }

    [HarmonyPatch]
    private static class Hooks
    {
        [HarmonyPatch(typeof(Player), nameof(Player.TeleportationPotion)), HarmonyPrefix]
        private static void PotionPrefix(Player __instance, out Departure? __state) => __state = _active?.Begin(__instance);

        [HarmonyPatch(typeof(Player), nameof(Player.TeleportationPotion)), HarmonyPostfix]
        private static void PotionPostfix(Player __instance, Departure? __state)
        {
            if (__state != null)
                _active!.End(__instance, __state);
        }
    }
}
