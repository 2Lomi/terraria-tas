using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TerrariaTas.Core.Studio;

/// <summary>Asks for the drop forecast of one NPC. Only answered while the game is paused.</summary>
public sealed class ForecastRequest : StudioMessage
{
    public override MessageType Type => MessageType.ForecastRequest;

    /// <summary>Echoed in the answer, so Studio can ignore answers to outdated requests.</summary>
    public int RequestId { get; set; }

    public int NpcSlot { get; set; }

    /// <summary>How many offsets to evaluate per stream.</summary>
    public int Rows { get; set; }

    /// <summary>Streams to evaluate, by name. Empty means the three streams kills usually happen on.</summary>
    public List<string> Streams { get; set; } = new();

    internal override void Write(BinaryWriter writer)
    {
        writer.Write(RequestId);
        writer.Write(NpcSlot);
        writer.Write(Rows);
        writer.WriteList(Streams, (w, stream) => w.Write(stream));
    }

    internal static ForecastRequest Read(BinaryReader reader) => new()
    {
        RequestId = reader.ReadInt32(),
        NpcSlot = reader.ReadInt32(),
        Rows = reader.ReadInt32(),
        Streams = reader.ReadList(r => r.ReadString()),
    };
}

/// <summary>
/// What an NPC would drop if its loot were rolled after a given number of further draws ("offset") on a stream.
/// Each column is one stream; cell k of a column is an index into <see cref="Outcomes"/>.
/// </summary>
public sealed class DropForecast : StudioMessage
{
    public override MessageType Type => MessageType.DropForecast;

    public int RequestId { get; set; }

    /// <summary>The frame boundary the forecast was computed at.</summary>
    public int Frame { get; set; }

    public int NpcSlot { get; set; }

    public int NetId { get; set; }

    public string NpcName { get; set; } = "";

    /// <summary>Luck of the player the loot code uses (the closest one).</summary>
    public float Luck { get; set; }

    /// <summary>Why there is no forecast, empty when there is one.</summary>
    public string Error { get; set; } = "";

    /// <summary>Limits of this forecast worth showing (e.g. what is not simulated for bosses).</summary>
    public string Notes { get; set; } = "";

    public List<PossibleDrop> PossibleDrops { get; set; } = new();

    /// <summary>Distinct outcomes, referenced by the cells.</summary>
    public List<DropOutcome> Outcomes { get; set; } = new();

    public List<ForecastColumn> Columns { get; set; } = new();

    internal override void Write(BinaryWriter writer)
    {
        writer.Write(RequestId);
        writer.Write(Frame);
        writer.Write(NpcSlot);
        writer.Write(NetId);
        writer.Write(NpcName);
        writer.Write(Luck);
        writer.Write(Error);
        writer.Write(Notes);
        writer.WriteList(PossibleDrops, (w, drop) => drop.Write(w));
        writer.WriteList(Outcomes, (w, outcome) => outcome.Write(w));
        writer.WriteList(Columns, (w, column) => column.Write(w));
    }

    internal static DropForecast Read(BinaryReader reader) => new()
    {
        RequestId = reader.ReadInt32(),
        Frame = reader.ReadInt32(),
        NpcSlot = reader.ReadInt32(),
        NetId = reader.ReadInt32(),
        NpcName = reader.ReadString(),
        Luck = reader.ReadSingle(),
        Error = reader.ReadString(),
        Notes = reader.ReadString(),
        PossibleDrops = reader.ReadList(PossibleDrop.Read),
        Outcomes = reader.ReadList(DropOutcome.Read),
        Columns = reader.ReadList(ForecastColumn.Read),
    };
}

public sealed class ForecastColumn
{
    public string Stream { get; set; } = "";

    /// <summary>Position of the stream at offset 0, or -1 when unknown.</summary>
    public long StartPosition { get; set; }

    /// <summary>Outcome index for each offset, starting at 0.</summary>
    public int[] Cells { get; set; } = Array.Empty<int>();

    internal void Write(BinaryWriter writer)
    {
        writer.Write(Stream);
        writer.Write(StartPosition);
        writer.WriteInts(Cells);
    }

    internal static ForecastColumn Read(BinaryReader reader) => new()
    {
        Stream = reader.ReadString(),
        StartPosition = reader.ReadInt64(),
        Cells = reader.ReadInts(),
    };
}

/// <summary>The loot of one kill: items (coins excluded) and the total coin value.</summary>
public sealed class DropOutcome
{
    public List<DroppedItem> Items { get; set; } = new();

    public long Copper { get; set; }

    /// <summary>Set when the loot code gives nothing at all (e.g. a statue-spawned enemy failing its roll).</summary>
    public string NoLootReason { get; set; } = "";

    public bool Contains(int itemId) => Items.Any(item => item.ItemId == itemId);

    /// <summary>Identical outcomes have the same key.</summary>
    public string Key => $"{NoLootReason}|{Copper}|{string.Join(";", Items.Select(i => $"{i.ItemId}:{i.Prefix}:{i.Stack}"))}";

    public string Describe()
    {
        if (NoLootReason.Length > 0)
            return "no loot (" + NoLootReason + ")";

        var parts = Items.Select(item => item.Stack > 1 ? $"{item.Name} ×{item.Stack}" : item.Name).ToList();
        if (Copper > 0)
            parts.Add(Coins.Format(Copper));
        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    internal void Write(BinaryWriter writer)
    {
        writer.WriteList(Items, (w, item) => item.Write(w));
        writer.Write(Copper);
        writer.Write(NoLootReason);
    }

    internal static DropOutcome Read(BinaryReader reader) => new()
    {
        Items = reader.ReadList(DroppedItem.Read),
        Copper = reader.ReadInt64(),
        NoLootReason = reader.ReadString(),
    };
}

public sealed class DroppedItem
{
    public int ItemId { get; set; }

    /// <summary>Name including the prefix.</summary>
    public string Name { get; set; } = "";

    public int Stack { get; set; }

    public int Prefix { get; set; }

    /// <summary>The prefix alone ("Keen"), empty without one.</summary>
    public string PrefixName { get; set; } = "";

    internal void Write(BinaryWriter writer)
    {
        writer.Write(ItemId);
        writer.Write(Name);
        writer.Write(Stack);
        writer.Write(Prefix);
        writer.Write(PrefixName);
    }

    internal static DroppedItem Read(BinaryReader reader) => new()
    {
        ItemId = reader.ReadInt32(),
        Name = reader.ReadString(),
        Stack = reader.ReadInt32(),
        Prefix = reader.ReadInt32(),
        PrefixName = reader.ReadString(),
    };
}

/// <summary>An item the NPC's drop rules can give, with the chance the bestiary shows.</summary>
public sealed class PossibleDrop
{
    public int ItemId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>0 to 1, before luck.</summary>
    public float Chance { get; set; }

    public int StackMin { get; set; }

    public int StackMax { get; set; }

    internal void Write(BinaryWriter writer)
    {
        writer.Write(ItemId);
        writer.Write(Name);
        writer.Write(Chance);
        writer.Write(StackMin);
        writer.Write(StackMax);
    }

    internal static PossibleDrop Read(BinaryReader reader) => new()
    {
        ItemId = reader.ReadInt32(),
        Name = reader.ReadString(),
        Chance = reader.ReadSingle(),
        StackMin = reader.ReadInt32(),
        StackMax = reader.ReadInt32(),
    };
}

/// <summary>
/// A real kill: which stream rolled its loot, how many draws that stream had made earlier in the same
/// frame (comparable with a forecast offset taken at the start of that frame), and what dropped.
/// </summary>
public sealed class KillRecord : StudioMessage
{
    public override MessageType Type => MessageType.KillRecord;

    /// <summary>The frame during which the NPC died.</summary>
    public int Frame { get; set; }

    public int NpcSlot { get; set; }

    public int NetId { get; set; }

    public string NpcName { get; set; } = "";

    /// <summary>Stream the loot was rolled on, empty when unknown.</summary>
    public string Stream { get; set; } = "";

    /// <summary>Draws the stream made during the frame before the loot roll, or -1 when unknown.</summary>
    public long Offset { get; set; }

    /// <summary>Position of the stream when the loot roll started, or -1 when unknown.</summary>
    public long Position { get; set; }

    public DropOutcome Drops { get; set; } = new();

    internal override void Write(BinaryWriter writer)
    {
        writer.Write(Frame);
        writer.Write(NpcSlot);
        writer.Write(NetId);
        writer.Write(NpcName);
        writer.Write(Stream);
        writer.Write(Offset);
        writer.Write(Position);
        Drops.Write(writer);
    }

    internal static KillRecord Read(BinaryReader reader) => new()
    {
        Frame = reader.ReadInt32(),
        NpcSlot = reader.ReadInt32(),
        NetId = reader.ReadInt32(),
        NpcName = reader.ReadString(),
        Stream = reader.ReadString(),
        Offset = reader.ReadInt64(),
        Position = reader.ReadInt64(),
        Drops = DropOutcome.Read(reader),
    };
}
