using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Utilities;
using TerrariaTas.Core;
using TerrariaTas.Core.Studio;
using TerrariaTas.Runtime.GameAccess;

namespace TerrariaTas.Runtime.RngTools;

/// <summary>
/// Teleport forecast: for each offset k of a stream, where a Teleportation Potion would send the player if its
/// destination roll started k draws after the current frame boundary. It runs the game's own search for a landing
/// spot (<c>Utils.CheckForGoodTeleportationSpot</c>) on copies of the stream, so no placement rule is re-implemented.
/// The search reads the world; what it writes (collision flags, blank tiles under a small world) is put back.
/// </summary>
internal sealed class TeleportForecaster
{
    /// <summary>Items are used during the players' update, so the destination rolls on its stream.</summary>
    public const string ItemUseStream = "UpdatePlayers";

    private const int MaxRows = 20_000;

    // Player.ItemCheck counts the item timer down once per frame, before the potion's code, which teleports when the
    // timer reaches this value.
    private const int RollAtItemTime = 2;

    // A roll tries up to 1000 places, two draws each.
    private const long MaxDrawsPerRoll = 2 * 1000 + 2;

    // The search clamps places 45 tiles from the bottom edge, then falls less than 100 tiles from there.
    private const int RowsReachedBelowWorld = 100 - 45;

    private readonly RngTracker _rng;

    public TeleportForecaster(RngTracker rng)
    {
        _rng = rng;
    }

    /// <summary>Call at a paused frame boundary inside a world.</summary>
    public TeleportForecast Forecast(TeleportRequest request, int frame)
    {
        var started = Stopwatch.StartNew();
        var player = Main.LocalPlayer;
        var forecast = new TeleportForecast
        {
            RequestId = request.RequestId,
            Frame = frame,
            Kind = request.Kind,
            World = GameWorld.Geometry(),
            PlayerX = player.position.X,
            PlayerY = player.position.Y,
            RollFrame = RollFrame(player, frame),
            Notes = "Destinations depend on the tiles, the player's gravity, Plantera and Skeletron being down (Lihzahrd and dungeon walls) "
                    + "and the world seed's special rules, as they are at this frame.",
        };
        if (request.Kind != TeleportKind.Potion)
        {
            forecast.Error = $"Teleports by {request.Kind} cannot be forecast yet.";
            return forecast;
        }

        var rows = Math.Max(1, Math.Min(request.Rows, MaxRows));
        var sandbox = new Sandbox();
        try
        {
            IReadOnlyList<string> streams = request.Streams.Count > 0 ? request.Streams : new[] { ItemUseStream };
            foreach (var stream in streams)
                forecast.Columns.Add(Column(stream, rows, player, sandbox));
        }
        finally
        {
            sandbox.Undo();
        }

        sandbox.EnsureUndone();
        forecast.Milliseconds = (int)started.ElapsedMilliseconds;
        return forecast;
    }

    /// <summary>
    /// The destination roll of <c>Player.TeleportationPotion</c>, with the same arguments. When no spot is found the
    /// potion "teleports" the player to where it already is.
    /// </summary>
    [MirrorsGameMethod("Terraria.Player", "TeleportationPotion")]
    public static Vector2 RollPotionDestination(Player player, out bool found)
    {
        found = false;
        var spot = Utils.CheckForGoodTeleportationSpot(ref found, 100, Main.maxTilesX - 200, 100, Main.UnderworldLayer, new Utils.RandomTeleportationAttemptSettings
        {
            teleporteeSize = player.Size,
            teleporteeVelocity = player.velocity,
            teleporteeGravityDirection = player.gravDir,
            avoidLava = true,
            avoidHurtTiles = true,
            maximumFallDistanceFromOrignalPoint = 100,
            attemptsBeforeGivingUp = 1000,
        });
        return found ? spot : player.position;
    }

    /// <summary>The frame during which a potion being used rolls its destination, or -1 when none is being used.</summary>
    [MirrorsGameMethod("Terraria.Player", "ItemCheck")]
    private static int RollFrame(Player player, int frame)
    {
        if (player.HeldItem?.type != ItemID.TeleportationPotion || player.itemAnimation <= 0 || player.itemTime <= RollAtItemTime)
            return -1;

        // The next frame to run counts the timer down once before checking it.
        return frame + player.itemTime - RollAtItemTime - 1;
    }

    private TeleportColumn Column(string name, int rows, Player player, Sandbox sandbox)
    {
        var column = new TeleportColumn { Stream = name, StartPosition = _rng.StartPositionOf(name) };
        var cursor = GameRng.CloneForNextFrame(name);
        for (var offset = 0; offset < rows; offset++)
        {
            column.Destinations.Add(sandbox.Roll(player, GameRng.Clone(cursor)));
            cursor.Next();
        }

        return column;
    }

    /// <summary>Runs destination rolls with a chosen generator and puts back what they change. Main thread, frame boundary only.</summary>
    private sealed class Sandbox
    {
        private readonly UnifiedRandom _shared = Main.rand;
        private readonly StaticFieldsSnapshot _collision = new(typeof(Collision));
        private readonly bool _bubbleSolid = Main.tileSolid[TileID.Bubble];
        private readonly List<(int X, int Y)> _emptySlots = EmptySlotsBelowWorld();

        public TeleportDestination Roll(Player player, UnifiedRandom generator)
        {
            var before = GameRng.ToModel(generator);
            Main.rand = generator; // the search draws from Main.rand, which is the stream during the players' update
            AsDuringPlayersUpdate();
            try
            {
                var spot = RollPotionDestination(player, out var found);
                return new TeleportDestination
                {
                    X = spot.X,
                    Y = spot.Y,
                    Found = found,
                    Draws = (int)before.DrawsUntil(GameRng.ToModel(generator), MaxDrawsPerRoll),
                };
            }
            finally
            {
                UndoRoll();
            }
        }

        /// <summary>Everything, the tiles the rolls created included. Call once after the last roll.</summary>
        public void Undo()
        {
            UndoRoll();
            foreach (var (x, y) in _emptySlots)
                Main.tile[x, y] = null;
        }

        /// <summary>Fails loudly if the rolls left anything behind: a forecast must never change the game.</summary>
        public void EnsureUndone()
        {
            if (Main.rand != _shared || !_collision.IsCurrent() || Main.tileSolid[TileID.Bubble] != _bubbleSolid
                || _emptySlots.Any(slot => Main.tile[slot.X, slot.Y] != null))
                throw new GameStateChangedException("The teleport forecast changed the game state. Report this as a bug.");
        }

        /// <summary>The world update makes bubbles passable for the players' update, where items are used.</summary>
        [MirrorsGameMethod("Terraria.Main", "DoUpdateInWorld_Inner")]
        private static void AsDuringPlayersUpdate() => Main.tileSolid[TileID.Bubble] = false;

        /// <summary>
        /// The tile array is made for the largest world, and its slots under a smaller world stay null. The search's fall
        /// can reach a little under the world, where it fills a null slot with a blank tile before reading it.
        /// </summary>
        private static List<(int X, int Y)> EmptySlotsBelowWorld()
        {
            var slots = new List<(int X, int Y)>();
            var columns = Math.Min(Main.maxTilesX, Main.tile.GetLength(0));
            var end = Math.Min(Main.maxTilesY + RowsReachedBelowWorld, Main.tile.GetLength(1));
            for (var y = Main.maxTilesY; y < end; y++)
            {
                for (var x = 0; x < columns; x++)
                {
                    if (Main.tile[x, y] == null)
                        slots.Add((x, y));
                }
            }

            return slots;
        }

        private void UndoRoll()
        {
            Main.rand = _shared;
            _collision.Restore();
            Main.tileSolid[TileID.Bubble] = _bubbleSolid;
        }
    }
}
