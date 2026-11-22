using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ID;
using TerrariaTas.Core.Movies;
using TerrariaTas.Core.Studio;
using TerrariaTas.Runtime.GameAccess;

namespace TerrariaTas.Runtime.Inspection;

/// <summary>Reads what TAS Studio displays: the player, NPCs, items on the ground and the input the game saw.</summary>
internal sealed class WorldInspector
{
    private const float TileSize = 16f;

    /// <summary>Call at a frame boundary inside a world. RNG streams are left to the caller.</summary>
    public WorldSnapshot Capture(int frame)
    {
        var player = Main.LocalPlayer;
        return new WorldSnapshot
        {
            Frame = frame,
            WorldName = Main.worldName,
            WorldSeed = GameRng.WorldSeed,
            Player = Describe(player),
            Npcs = Main.npc.Take(Main.maxNPCs).Where(npc => npc is { active: true }).Select(npc => Describe(npc, player)).ToList(),
            Items = Main.item.Take(Main.maxItems).Where(item => item is { active: true }).Select(item => Describe(item, player)).ToList(),
        };
    }

    /// <summary>
    /// Keyboard and mouse as the game read them during the last update, whether they came from the movie
    /// or from the real devices.
    /// </summary>
    public InputView ReadInput()
    {
        var mouse = PlayerInput.MouseInfo;
        var cursorInWorld = Main.MouseWorld;
        return new InputView
        {
            Keys = Main.keyState.GetPressedKeys().Select(key => (Key)(byte)key).ToArray(),
            Buttons = ButtonsOf(mouse),
            CursorX = mouse.X,
            CursorY = mouse.Y,
            CursorTileX = (int)Math.Floor(cursorInWorld.X / TileSize),
            CursorTileY = (int)Math.Floor(cursorInWorld.Y / TileSize),
        };
    }

    /// <summary>The local player. Cheap enough for every frame boundary, also while the game runs.</summary>
    public PlayerView ReadPlayer() => Describe(Main.LocalPlayer);

    private static PlayerView Describe(Player player) => new()
    {
        X = player.Center.X,
        Y = player.Center.Y,
        PositionX = player.position.X,
        PositionY = player.position.Y,
        OldPositionX = player.oldPosition.X,
        OldPositionY = player.oldPosition.Y,
        VelocityX = player.velocity.X,
        VelocityY = player.velocity.Y,
        Direction = player.direction,
        GravityDirection = player.gravDir,
        Jump = player.jump,
        WingTime = player.wingTime,
        RocketTime = player.rocketTime,
        DashDelay = player.dashDelay,
        ImmuneTime = player.immuneTime,
        FallStart = player.fallStart,
        SelectedItem = player.selectedItem,
        ItemAnimation = player.itemAnimation,
        ItemTime = player.itemTime,
        Life = player.statLife,
        LifeMax = player.statLifeMax2,
        Mana = player.statMana,
        ManaMax = player.statManaMax2,
        Defense = player.statDefense,
        Breath = player.breath,
        BreathMax = player.breathMax,
        Dead = player.dead,
        RespawnTimer = player.respawnTimer,
        Luck = player.luck,
        HeldItem = player.HeldItem?.AffixName() ?? "",
        Buffs = Enumerable.Range(0, Player.maxBuffs)
            .Where(slot => player.buffType[slot] > 0)
            .Select(slot => new BuffView { Type = player.buffType[slot], Name = Lang.GetBuffName(player.buffType[slot]), Time = player.buffTime[slot] })
            .ToList(),
    };

    private static NpcView Describe(NPC npc, Player player) => new()
    {
        Slot = npc.whoAmI,
        NetId = npc.netID,
        TypeId = npc.type,
        Name = npc.FullName,
        Category = CategoryOf(npc),
        Life = npc.life,
        LifeMax = npc.lifeMax,
        X = npc.Center.X,
        Y = npc.Center.Y,
        VelocityX = npc.velocity.X,
        VelocityY = npc.velocity.Y,
        Distance = Vector2.Distance(npc.Center, player.Center) / TileSize,
        Copper = (long)npc.value + npc.extraValue,
        FromStatue = npc.SpawnedFromStatue,
        Ai = (float[])npc.ai.Clone(),
    };

    private static ItemView Describe(WorldItem item, Player player) => new()
    {
        Slot = item.whoAmI,
        ItemId = item.type,
        Name = item.inner.AffixName(),
        Stack = item.stack,
        X = item.Center.X,
        Y = item.Center.Y,
        Distance = Vector2.Distance(item.Center, player.Center) / TileSize,
        Age = item.timeSinceItemSpawned,
    };

    private static NpcCategory CategoryOf(NPC npc)
    {
        if (npc.boss || NPCID.Sets.ShouldBeCountedAsBossForBestiary[npc.type])
            return NpcCategory.Boss;
        if (npc.townNPC)
            return NpcCategory.TownNpc;
        if (npc.CountsAsACritter)
            return NpcCategory.Critter;
        return npc.friendly ? NpcCategory.Friendly : NpcCategory.Enemy;
    }

    private static MouseButtons ButtonsOf(MouseState mouse) =>
        IfPressed(mouse.LeftButton, MouseButtons.Left)
        | IfPressed(mouse.RightButton, MouseButtons.Right)
        | IfPressed(mouse.MiddleButton, MouseButtons.Middle)
        | IfPressed(mouse.XButton1, MouseButtons.X1)
        | IfPressed(mouse.XButton2, MouseButtons.X2);

    private static MouseButtons IfPressed(ButtonState state, MouseButtons button) =>
        state == ButtonState.Pressed ? button : MouseButtons.None;
}
