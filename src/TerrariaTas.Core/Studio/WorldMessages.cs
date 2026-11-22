using System;
using System.Collections.Generic;
using System.IO;

namespace TerrariaTas.Core.Studio;

/// <summary>The inspectable state of the world at a paused frame boundary.</summary>
public sealed class WorldSnapshot : StudioMessage
{
    public override MessageType Type => MessageType.WorldSnapshot;

    /// <summary>The next frame to run: the snapshot shows the game right before it.</summary>
    public int Frame { get; set; }

    public string WorldName { get; set; } = "";

    public int WorldSeed { get; set; }

    public PlayerView Player { get; set; } = new();

    public List<NpcView> Npcs { get; set; } = new();

    public List<ItemView> Items { get; set; } = new();

    public List<RngStreamView> Streams { get; set; } = new();

    internal override void Write(BinaryWriter writer)
    {
        writer.Write(Frame);
        writer.Write(WorldName);
        writer.Write(WorldSeed);
        Player.Write(writer);
        writer.WriteList(Npcs, (w, npc) => npc.Write(w));
        writer.WriteList(Items, (w, item) => item.Write(w));
        writer.WriteList(Streams, (w, stream) => stream.Write(w));
    }

    internal static WorldSnapshot Read(BinaryReader reader) => new()
    {
        Frame = reader.ReadInt32(),
        WorldName = reader.ReadString(),
        WorldSeed = reader.ReadInt32(),
        Player = PlayerView.Read(reader),
        Npcs = reader.ReadList(NpcView.Read),
        Items = reader.ReadList(ItemView.Read),
        Streams = reader.ReadList(RngStreamView.Read),
    };
}

/// <summary>The local player, with the values movement code works with, exactly as the game holds them.</summary>
public sealed class PlayerView
{
    /// <summary>Center of the player in world pixels.</summary>
    public float X { get; set; }

    public float Y { get; set; }

    /// <summary>Top-left corner in world pixels (<c>Entity.position</c>), which is what movement code moves.</summary>
    public float PositionX { get; set; }

    public float PositionY { get; set; }

    /// <summary>Where the last update started (<c>Entity.oldPosition</c>): the position minus this is what the last frame moved.</summary>
    public float OldPositionX { get; set; }

    public float OldPositionY { get; set; }

    public float VelocityX { get; set; }

    public float VelocityY { get; set; }

    /// <summary>1 facing right, -1 facing left.</summary>
    public int Direction { get; set; }

    /// <summary>1, or -1 with gravity flipped.</summary>
    public float GravityDirection { get; set; }

    /// <summary>Frames of jump left (<c>Player.jump</c>).</summary>
    public int Jump { get; set; }

    public float WingTime { get; set; }

    public int RocketTime { get; set; }

    /// <summary>Frames before the next dash (<c>Player.dashDelay</c>); negative while dashing.</summary>
    public int DashDelay { get; set; }

    /// <summary>Frames of invincibility left after a hit.</summary>
    public int ImmuneTime { get; set; }

    /// <summary>Tile row the current fall started from (<c>Player.fallStart</c>): fall damage counts from there.</summary>
    public int FallStart { get; set; }

    /// <summary>Hotbar or inventory slot of the held item.</summary>
    public int SelectedItem { get; set; }

    /// <summary>Frames left in the current swing or use animation, and before the item can be used again.</summary>
    public int ItemAnimation { get; set; }

    public int ItemTime { get; set; }

    public int Life { get; set; }

    public int LifeMax { get; set; }

    public int Mana { get; set; }

    public int ManaMax { get; set; }

    public int Defense { get; set; }

    public int Breath { get; set; }

    public int BreathMax { get; set; }

    public bool Dead { get; set; }

    public int RespawnTimer { get; set; }

    /// <summary>Changes both drop odds and how many random numbers a drop roll uses.</summary>
    public float Luck { get; set; }

    public string HeldItem { get; set; } = "";

    public List<BuffView> Buffs { get; set; } = new();

    internal void Write(BinaryWriter writer)
    {
        writer.Write(X);
        writer.Write(Y);
        writer.Write(PositionX);
        writer.Write(PositionY);
        writer.Write(OldPositionX);
        writer.Write(OldPositionY);
        writer.Write(VelocityX);
        writer.Write(VelocityY);
        writer.Write(Direction);
        writer.Write(GravityDirection);
        writer.Write(Jump);
        writer.Write(WingTime);
        writer.Write(RocketTime);
        writer.Write(DashDelay);
        writer.Write(ImmuneTime);
        writer.Write(FallStart);
        writer.Write(SelectedItem);
        writer.Write(ItemAnimation);
        writer.Write(ItemTime);
        writer.Write(Life);
        writer.Write(LifeMax);
        writer.Write(Mana);
        writer.Write(ManaMax);
        writer.Write(Defense);
        writer.Write(Breath);
        writer.Write(BreathMax);
        writer.Write(Dead);
        writer.Write(RespawnTimer);
        writer.Write(Luck);
        writer.Write(HeldItem);
        writer.WriteList(Buffs, (w, buff) => buff.Write(w));
    }

    internal static PlayerView Read(BinaryReader reader) => new()
    {
        X = reader.ReadSingle(),
        Y = reader.ReadSingle(),
        PositionX = reader.ReadSingle(),
        PositionY = reader.ReadSingle(),
        OldPositionX = reader.ReadSingle(),
        OldPositionY = reader.ReadSingle(),
        VelocityX = reader.ReadSingle(),
        VelocityY = reader.ReadSingle(),
        Direction = reader.ReadInt32(),
        GravityDirection = reader.ReadSingle(),
        Jump = reader.ReadInt32(),
        WingTime = reader.ReadSingle(),
        RocketTime = reader.ReadInt32(),
        DashDelay = reader.ReadInt32(),
        ImmuneTime = reader.ReadInt32(),
        FallStart = reader.ReadInt32(),
        SelectedItem = reader.ReadInt32(),
        ItemAnimation = reader.ReadInt32(),
        ItemTime = reader.ReadInt32(),
        Life = reader.ReadInt32(),
        LifeMax = reader.ReadInt32(),
        Mana = reader.ReadInt32(),
        ManaMax = reader.ReadInt32(),
        Defense = reader.ReadInt32(),
        Breath = reader.ReadInt32(),
        BreathMax = reader.ReadInt32(),
        Dead = reader.ReadBoolean(),
        RespawnTimer = reader.ReadInt32(),
        Luck = reader.ReadSingle(),
        HeldItem = reader.ReadString(),
        Buffs = reader.ReadList(BuffView.Read),
    };
}

/// <summary>A buff or debuff on the player.</summary>
public sealed class BuffView
{
    public int Type { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Frames left.</summary>
    public int Time { get; set; }

    internal void Write(BinaryWriter writer)
    {
        writer.Write(Type);
        writer.Write(Name);
        writer.Write(Time);
    }

    internal static BuffView Read(BinaryReader reader) => new()
    {
        Type = reader.ReadInt32(),
        Name = reader.ReadString(),
        Time = reader.ReadInt32(),
    };
}

public enum NpcCategory : byte
{
    Enemy,
    Boss,
    TownNpc,
    Critter,

    /// <summary>Friendly but not a town NPC, e.g. bound NPCs waiting to be rescued.</summary>
    Friendly,
}

public sealed class NpcView
{
    /// <summary>Index in the game's NPC array.</summary>
    public int Slot { get; set; }

    /// <summary>Variant id: drops are registered per netID (a Green Slime is a Blue Slime type with its own netID).</summary>
    public int NetId { get; set; }

    public int TypeId { get; set; }

    public string Name { get; set; } = "";

    public NpcCategory Category { get; set; }

    public int Life { get; set; }

    public int LifeMax { get; set; }

    /// <summary>Center in world pixels.</summary>
    public float X { get; set; }

    public float Y { get; set; }

    public float VelocityX { get; set; }

    public float VelocityY { get; set; }

    /// <summary>Distance from the player's center, in tiles.</summary>
    public float Distance { get; set; }

    /// <summary>Base coin value before the drop roll, in copper.</summary>
    public long Copper { get; set; }

    public bool FromStatue { get; set; }

    public float[] Ai { get; set; } = Array.Empty<float>();

    internal void Write(BinaryWriter writer)
    {
        writer.Write(Slot);
        writer.Write(NetId);
        writer.Write(TypeId);
        writer.Write(Name);
        writer.Write((byte)Category);
        writer.Write(Life);
        writer.Write(LifeMax);
        writer.Write(X);
        writer.Write(Y);
        writer.Write(VelocityX);
        writer.Write(VelocityY);
        writer.Write(Distance);
        writer.Write(Copper);
        writer.Write(FromStatue);
        writer.WriteFloats(Ai);
    }

    internal static NpcView Read(BinaryReader reader) => new()
    {
        Slot = reader.ReadInt32(),
        NetId = reader.ReadInt32(),
        TypeId = reader.ReadInt32(),
        Name = reader.ReadString(),
        Category = (NpcCategory)reader.ReadByte(),
        Life = reader.ReadInt32(),
        LifeMax = reader.ReadInt32(),
        X = reader.ReadSingle(),
        Y = reader.ReadSingle(),
        VelocityX = reader.ReadSingle(),
        VelocityY = reader.ReadSingle(),
        Distance = reader.ReadSingle(),
        Copper = reader.ReadInt64(),
        FromStatue = reader.ReadBoolean(),
        Ai = reader.ReadFloats(),
    };
}

/// <summary>An item lying in the world, ready to be picked up.</summary>
public sealed class ItemView
{
    public int Slot { get; set; }

    public int ItemId { get; set; }

    /// <summary>Name including the prefix, e.g. "Keen Bone Sword".</summary>
    public string Name { get; set; } = "";

    public int Stack { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public float Distance { get; set; }

    /// <summary>Frames since the item appeared.</summary>
    public int Age { get; set; }

    internal void Write(BinaryWriter writer)
    {
        writer.Write(Slot);
        writer.Write(ItemId);
        writer.Write(Name);
        writer.Write(Stack);
        writer.Write(X);
        writer.Write(Y);
        writer.Write(Distance);
        writer.Write(Age);
    }

    internal static ItemView Read(BinaryReader reader) => new()
    {
        Slot = reader.ReadInt32(),
        ItemId = reader.ReadInt32(),
        Name = reader.ReadString(),
        Stack = reader.ReadInt32(),
        X = reader.ReadSingle(),
        Y = reader.ReadSingle(),
        Distance = reader.ReadSingle(),
        Age = reader.ReadInt32(),
    };
}

/// <summary>One random number generator of the game, with enough state to predict all its upcoming values.</summary>
public sealed class RngStreamView
{
    /// <summary>"Main.rand" for the shared generator, otherwise the world-update phase that owns the stream.</summary>
    public string Name { get; set; } = "";

    /// <summary>Draws made since the stream was seeded, or -1 when unknown.</summary>
    public long Position { get; set; }

    /// <summary>Draws made during the last frame, or -1 when unknown.</summary>
    public long LastFrameDraws { get; set; }

    /// <summary>The generator's private state; <see cref="Rng.TerrariaRandom.FromState"/> rebuilds it.</summary>
    public int[] State { get; set; } = Array.Empty<int>();

    public uint Index { get; set; }

    internal void Write(BinaryWriter writer)
    {
        writer.Write(Name);
        writer.Write(Position);
        writer.Write(LastFrameDraws);
        writer.WriteInts(State);
        writer.Write(Index);
    }

    internal static RngStreamView Read(BinaryReader reader) => new()
    {
        Name = reader.ReadString(),
        Position = reader.ReadInt64(),
        LastFrameDraws = reader.ReadInt64(),
        State = reader.ReadInts(),
        Index = reader.ReadUInt32(),
    };
}
