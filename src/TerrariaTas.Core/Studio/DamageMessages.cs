using System;
using System.Collections.Generic;
using System.IO;

namespace TerrariaTas.Core.Studio;

/// <summary>How a hit reaches an NPC; each rolls on the stream of its update phase.</summary>
public enum HitKind : byte
{
    /// <summary>The held item's swing (<c>Player.ProcessHitAgainstNPC</c>, UpdatePlayers).</summary>
    Swing,

    /// <summary>A projectile (<c>Projectile.Damage_PVE_Inner</c>, UpdateProjectiles).</summary>
    Projectile,
}

/// <summary>Asks for the damage rolls of the held weapon for each offset. Only answered while the game is paused.</summary>
public sealed class DamageRequest : StudioMessage
{
    public override MessageType Type => MessageType.DamageRequest;

    /// <summary>Echoed in the answer, so Studio can ignore answers to outdated requests.</summary>
    public int RequestId { get; set; }

    /// <summary>How many offsets to evaluate per stream.</summary>
    public int Rows { get; set; }

    /// <summary>NPC slot whose defense turns the rolled damage into damage dealt, or -1 for none.</summary>
    public int TargetSlot { get; set; } = -1;

    /// <summary>Damage before the roll, replacing the game's (for example a projectile's own damage); -1 keeps the game's.</summary>
    public int BaseDamage { get; set; } = -1;

    /// <summary>Crit chance in percent, replacing the game's for every crit roll; -1 keeps the game's.</summary>
    public int CritChance { get; set; } = -1;

    internal override void Write(BinaryWriter writer)
    {
        writer.Write(RequestId);
        writer.Write(Rows);
        writer.Write(TargetSlot);
        writer.Write(BaseDamage);
        writer.Write(CritChance);
    }

    internal static DamageRequest Read(BinaryReader reader) => new()
    {
        RequestId = reader.ReadInt32(),
        Rows = reader.ReadInt32(),
        TargetSlot = reader.ReadInt32(),
        BaseDamage = reader.ReadInt32(),
        CritChance = reader.ReadInt32(),
    };
}

/// <summary>
/// The crit and damage of a hit of the held weapon if its first roll were a given number of draws ("offset") after the
/// frame boundary the game is paused at. Each column is one kind of hit, on its own stream.
/// </summary>
public sealed class DamageForecast : StudioMessage
{
    public override MessageType Type => MessageType.DamageForecast;

    public int RequestId { get; set; }

    /// <summary>The frame boundary the forecast was computed at.</summary>
    public int Frame { get; set; }

    /// <summary>Why there is no forecast, empty when there is one.</summary>
    public string Error { get; set; } = "";

    /// <summary>Limits of this forecast worth showing.</summary>
    public string Notes { get; set; } = "";

    public int ItemId { get; set; }

    /// <summary>The held item, with its prefix.</summary>
    public string ItemName { get; set; } = "";

    /// <summary>The player's luck: above 0 the roll may draw again and keep the higher damage, below 0 the lower.</summary>
    public float Luck { get; set; }

    /// <summary>The NPC the damage dealt is computed against, or -1.</summary>
    public int TargetSlot { get; set; } = -1;

    public int TargetNetId { get; set; }

    public string TargetName { get; set; } = "";

    public int TargetLife { get; set; }

    public int TargetDefense { get; set; }

    public List<DamageColumn> Columns { get; set; } = new();

    internal override void Write(BinaryWriter writer)
    {
        writer.Write(RequestId);
        writer.Write(Frame);
        writer.Write(Error);
        writer.Write(Notes);
        writer.Write(ItemId);
        writer.Write(ItemName);
        writer.Write(Luck);
        writer.Write(TargetSlot);
        writer.Write(TargetNetId);
        writer.Write(TargetName);
        writer.Write(TargetLife);
        writer.Write(TargetDefense);
        writer.WriteList(Columns, (w, column) => column.Write(w));
    }

    internal static DamageForecast Read(BinaryReader reader) => new()
    {
        RequestId = reader.ReadInt32(),
        Frame = reader.ReadInt32(),
        Error = reader.ReadString(),
        Notes = reader.ReadString(),
        ItemId = reader.ReadInt32(),
        ItemName = reader.ReadString(),
        Luck = reader.ReadSingle(),
        TargetSlot = reader.ReadInt32(),
        TargetNetId = reader.ReadInt32(),
        TargetName = reader.ReadString(),
        TargetLife = reader.ReadInt32(),
        TargetDefense = reader.ReadInt32(),
        Columns = reader.ReadList(DamageColumn.Read),
    };
}

/// <summary>One kind of hit: what it starts from, and its rolls for every offset.</summary>
public sealed class DamageColumn
{
    public HitKind Kind { get; set; }

    public string Stream { get; set; } = "";

    /// <summary>Position of the stream at offset 0, or -1 when unknown.</summary>
    public long StartPosition { get; set; }

    /// <summary>Where the damage comes from: the weapon, or a projectile already in flight.</summary>
    public string Source { get; set; } = "";

    /// <summary>The item type of a swing, the projectile type of a projectile hit: hits of another one roll otherwise.</summary>
    public int SourceId { get; set; }

    /// <summary>Studio replaced the game's base damage or crit chance: real hits may differ.</summary>
    public bool Overridden { get; set; }

    /// <summary>The damage the roll varies (±15%).</summary>
    public int BaseDamage { get; set; }

    /// <summary>One crit roll per entry, in the order the game draws them, with its chance in percent. Empty: the hit cannot crit.</summary>
    public int[] CritChances { get; set; } = Array.Empty<int>();

    /// <summary>The rolls for each offset, starting at 0.</summary>
    public List<DamageRoll> Rolls { get; set; } = new();

    internal void Write(BinaryWriter writer)
    {
        writer.Write((byte)Kind);
        writer.Write(Stream);
        writer.Write(StartPosition);
        writer.Write(Source);
        writer.Write(SourceId);
        writer.Write(Overridden);
        writer.Write(BaseDamage);
        writer.WriteInts(CritChances);
        writer.WriteList(Rolls, (w, roll) => roll.Write(w));
    }

    internal static DamageColumn Read(BinaryReader reader) => new()
    {
        Kind = (HitKind)reader.ReadByte(),
        Stream = reader.ReadString(),
        StartPosition = reader.ReadInt64(),
        Source = reader.ReadString(),
        SourceId = reader.ReadInt32(),
        Overridden = reader.ReadBoolean(),
        BaseDamage = reader.ReadInt32(),
        CritChances = reader.ReadInts(),
        Rolls = reader.ReadList(DamageRoll.Read),
    };
}

/// <summary>The rolls of one hit.</summary>
public sealed class DamageRoll
{
    public bool Crit { get; set; }

    /// <summary>The damage after its ±15% roll (<c>Main.DamageVar</c>), before defense and crit.</summary>
    public int Damage { get; set; }

    /// <summary>What the target would lose (defense, crit and armor penetration applied), or -1 without a target.</summary>
    public int Dealt { get; set; } = -1;

    /// <summary>Random numbers the crit and damage rolls drew together.</summary>
    public int Draws { get; set; }

    internal void Write(BinaryWriter writer)
    {
        writer.Write(Crit);
        writer.Write(Damage);
        writer.Write(Dealt);
        writer.Write(Draws);
    }

    internal static DamageRoll Read(BinaryReader reader) => new()
    {
        Crit = reader.ReadBoolean(),
        Damage = reader.ReadInt32(),
        Dealt = reader.ReadInt32(),
        Draws = reader.ReadInt32(),
    };
}

/// <summary>
/// A real hit of the local player on an NPC: where its rolls started on its stream (comparable with a damage
/// forecast offset taken at the start of that frame), and what they gave.
/// </summary>
public sealed class HitRecord : StudioMessage
{
    public override MessageType Type => MessageType.HitRecord;

    /// <summary>The frame during which the hit happened.</summary>
    public int Frame { get; set; }

    public int NpcSlot { get; set; }

    public int NetId { get; set; }

    public string NpcName { get; set; } = "";

    public HitKind Kind { get; set; }

    /// <summary>The item swung or the projectile that hit.</summary>
    public string Source { get; set; } = "";

    /// <summary>The item type of a swing, the projectile type of a projectile hit.</summary>
    public int SourceId { get; set; }

    /// <summary>Stream the hit rolled on, empty when unknown.</summary>
    public string Stream { get; set; } = "";

    /// <summary>Draws the stream made during the frame before the hit's first roll, or -1 when unknown.</summary>
    public long Offset { get; set; }

    public bool Crit { get; set; }

    /// <summary>The damage roll's result (<c>Main.DamageVar</c>), or -1 when the hit made none.</summary>
    public int Damage { get; set; }

    /// <summary>What the NPC lost (<c>NPC.StrikeNPC</c>'s result).</summary>
    public int Dealt { get; set; }

    internal override void Write(BinaryWriter writer)
    {
        writer.Write(Frame);
        writer.Write(NpcSlot);
        writer.Write(NetId);
        writer.Write(NpcName);
        writer.Write((byte)Kind);
        writer.Write(Source);
        writer.Write(SourceId);
        writer.Write(Stream);
        writer.Write(Offset);
        writer.Write(Crit);
        writer.Write(Damage);
        writer.Write(Dealt);
    }

    internal static HitRecord Read(BinaryReader reader) => new()
    {
        Frame = reader.ReadInt32(),
        NpcSlot = reader.ReadInt32(),
        NetId = reader.ReadInt32(),
        NpcName = reader.ReadString(),
        Kind = (HitKind)reader.ReadByte(),
        Source = reader.ReadString(),
        SourceId = reader.ReadInt32(),
        Stream = reader.ReadString(),
        Offset = reader.ReadInt64(),
        Crit = reader.ReadBoolean(),
        Damage = reader.ReadInt32(),
        Dealt = reader.ReadInt32(),
    };
}
