using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Utilities;
using TerrariaTas.Core.Verification;
using TerrariaTas.Runtime.GameAccess;

namespace TerrariaTas.Runtime.Verification;

/// <summary>
/// Fingerprints the gameplay state. Two runs of a movie are in sync as long as their fingerprints match
/// frame by frame; the parts that differ first point at the cause of a desync.
/// </summary>
internal static class StateHasher
{
    public static Fingerprint Capture()
    {
        var parts = new Dictionary<string, ulong>
        {
            ["time"] = Hash(AddTime),
            ["weather"] = Hash(AddWeather),
            ["camera"] = Hash(h => h.Add(Main.screenPosition)),
            ["players"] = Hash(AddPlayers),
            ["npcs"] = Hash(AddNpcs),
            ["projectiles"] = Hash(AddProjectiles),
            ["items"] = Hash(AddItems),
            ["rng:Main.rand"] = Hash(h => AddRng(h, Main.rand)),
        };
        foreach (var stream in GameRng.Streams)
            parts["rng:" + stream.Key] = Hash(h => AddRng(h, stream.Value));

        return new Fingerprint(parts);
    }

    /// <summary>Every tile of the world. Takes a noticeable fraction of a second on large worlds.</summary>
    public static ulong HashTiles() => Hash(h =>
    {
        for (var x = 0; x < Main.maxTilesX; x++)
        {
            for (var y = 0; y < Main.maxTilesY; y++)
            {
                var tile = Main.tile[x, y];
                h.Add(tile.type | tile.wall << 16);
                h.Add(tile.sTileHeader | tile.liquid << 16 | tile.bTileHeader << 24);
                h.Add(tile.bTileHeader2 | tile.bTileHeader3 << 8);
                h.Add((ushort)tile.frameX | tile.frameY << 16);
            }
        }
    });

    private static void AddTime(Fnv h)
    {
        h.Add(Main.GameUpdateCount);
        h.Add(BitConverter.DoubleToInt64Bits(Main.time));
        h.Add(Main.dayTime ? 1 : 0);
    }

    private static void AddWeather(Fnv h)
    {
        h.Add(Main.raining ? 1 : 0);
        h.Add(Main.rainTime);
        h.Add(Main.maxRaining);
        h.Add(Main.cloudAlpha);
        h.Add(Main.windSpeedCurrent);
        h.Add(Main.windSpeedTarget);
        h.Add(Main.numClouds);
    }

    private static void AddPlayers(Fnv h)
    {
        foreach (var player in Main.player.Where(p => p is { active: true }))
        {
            h.Add(player.position);
            h.Add(player.velocity);
            h.Add(player.statLife);
            h.Add(player.statMana);
            h.Add(player.selectedItem);
            h.Add(player.itemAnimation);
            foreach (var item in player.inventory)
            {
                h.Add(item.type);
                h.Add(item.stack);
            }
        }
    }

    private static void AddNpcs(Fnv h)
    {
        for (var i = 0; i < Main.npc.Length; i++)
        {
            var npc = Main.npc[i];
            if (npc is not { active: true })
                continue;
            h.Add(i);
            h.Add(npc.type);
            h.Add(npc.position);
            h.Add(npc.velocity);
            h.Add(npc.life);
            h.Add(npc.target);
            foreach (var ai in npc.ai)
                h.Add(ai);
        }
    }

    private static void AddProjectiles(Fnv h)
    {
        for (var i = 0; i < Main.projectile.Length; i++)
        {
            var projectile = Main.projectile[i];
            if (projectile is not { active: true })
                continue;
            h.Add(i);
            h.Add(projectile.type);
            h.Add(projectile.position);
            h.Add(projectile.velocity);
            h.Add(projectile.timeLeft);
            foreach (var ai in projectile.ai)
                h.Add(ai);
        }
    }

    private static void AddItems(Fnv h)
    {
        for (var i = 0; i < Main.item.Length; i++)
        {
            var item = Main.item[i];
            if (item == null || item.IsAir)
                continue;
            h.Add(i);
            h.Add(item.type);
            h.Add(item.stack);
            h.Add(item.position);
        }
    }

    private static void AddRng(Fnv h, UnifiedRandom random)
    {
        var model = GameRng.ToModel(random);
        h.Add(model.Index);
        foreach (var value in model.CopyState())
            h.Add(value);
    }

    private static ulong Hash(Action<Fnv> add)
    {
        var hash = new Fnv();
        add(hash);
        return hash.Value;
    }

    /// <summary>64-bit FNV-1a over 32-bit words.</summary>
    private sealed class Fnv
    {
        public ulong Value { get; private set; } = 14695981039346656037UL;

        public void Add(int word)
        {
            for (var i = 0; i < 4; i++)
            {
                Value ^= (byte)(word >> (8 * i));
                Value *= 1099511628211UL;
            }
        }

        public void Add(uint word) => Add(unchecked((int)word));

        public void Add(long word)
        {
            Add((int)word);
            Add((int)(word >> 32));
        }

        public void Add(float value) => Add(new FloatBits { Float = value }.Int);

        public void Add(Vector2 vector)
        {
            Add(vector.X);
            Add(vector.Y);
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct FloatBits
    {
        [FieldOffset(0)]
        public float Float;

        [FieldOffset(0)]
        public int Int;
    }
}
