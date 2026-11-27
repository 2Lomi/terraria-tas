using System;
using System.Collections.Generic;
using System.IO;
using TerrariaTas.Core.World;

namespace TerrariaTas.Core.Studio;

/// <summary>Items that send the player to a random place.</summary>
public enum TeleportKind : byte
{
    /// <summary>Teleportation Potion (<c>Player.TeleportationPotion</c>): anywhere between the edges, the sky and the underworld.</summary>
    Potion,
}

/// <summary>Asks where a random teleport would land for each offset of a stream. Only answered while the game is paused.</summary>
public sealed class TeleportRequest : StudioMessage
{
    public override MessageType Type => MessageType.TeleportRequest;

    /// <summary>Echoed in the answer, so Studio can ignore answers to outdated requests.</summary>
    public int RequestId { get; set; }

    public TeleportKind Kind { get; set; }

    /// <summary>How many offsets to evaluate per stream.</summary>
    public int Rows { get; set; }

    /// <summary>Streams to evaluate, by name. Empty means the one item use rolls on (UpdatePlayers).</summary>
    public List<string> Streams { get; set; } = new();

    internal override void Write(BinaryWriter writer)
    {
        writer.Write(RequestId);
        writer.Write((byte)Kind);
        writer.Write(Rows);
        writer.WriteList(Streams, (w, stream) => w.Write(stream));
    }

    internal static TeleportRequest Read(BinaryReader reader) => new()
    {
        RequestId = reader.ReadInt32(),
        Kind = (TeleportKind)reader.ReadByte(),
        Rows = reader.ReadInt32(),
        Streams = reader.ReadList(r => r.ReadString()),
    };
}

/// <summary>
/// Where a random teleport would land if its destination roll started a given number of draws ("offset") after the
/// frame boundary the game is paused at. Each column is one stream; destination k of a column is offset k.
/// </summary>
public sealed class TeleportForecast : StudioMessage
{
    public override MessageType Type => MessageType.TeleportForecast;

    public int RequestId { get; set; }

    /// <summary>The frame boundary the forecast was computed at.</summary>
    public int Frame { get; set; }

    public TeleportKind Kind { get; set; }

    /// <summary>Why there is no forecast, empty when there is one.</summary>
    public string Error { get; set; } = "";

    /// <summary>Limits of this forecast worth showing.</summary>
    public string Notes { get; set; } = "";

    public WorldGeometry World { get; set; } = new();

    /// <summary>The player's top-left corner now (<c>Entity.position</c>), in world pixels.</summary>
    public float PlayerX { get; set; }

    public float PlayerY { get; set; }

    /// <summary>
    /// While a potion is being used: the frame during which it rolls its destination, read from the player's item
    /// timer; -1 when none is being used.
    /// </summary>
    public int RollFrame { get; set; } = -1;

    /// <summary>How long the game took to compute it: the game waits that long on every paused frame that asks.</summary>
    public int Milliseconds { get; set; }

    public List<TeleportColumn> Columns { get; set; } = new();

    internal override void Write(BinaryWriter writer)
    {
        writer.Write(RequestId);
        writer.Write(Frame);
        writer.Write((byte)Kind);
        writer.Write(Error);
        writer.Write(Notes);
        World.Write(writer);
        writer.Write(PlayerX);
        writer.Write(PlayerY);
        writer.Write(RollFrame);
        writer.Write(Milliseconds);
        writer.WriteList(Columns, (w, column) => column.Write(w));
    }

    internal static TeleportForecast Read(BinaryReader reader) => new()
    {
        RequestId = reader.ReadInt32(),
        Frame = reader.ReadInt32(),
        Kind = (TeleportKind)reader.ReadByte(),
        Error = reader.ReadString(),
        Notes = reader.ReadString(),
        World = WorldGeometry.Read(reader),
        PlayerX = reader.ReadSingle(),
        PlayerY = reader.ReadSingle(),
        RollFrame = reader.ReadInt32(),
        Milliseconds = reader.ReadInt32(),
        Columns = reader.ReadList(TeleportColumn.Read),
    };
}

public sealed class TeleportColumn
{
    public string Stream { get; set; } = "";

    /// <summary>Position of the stream at offset 0, or -1 when unknown.</summary>
    public long StartPosition { get; set; }

    /// <summary>The destination for each offset, starting at 0.</summary>
    public List<TeleportDestination> Destinations { get; set; } = new();

    internal void Write(BinaryWriter writer)
    {
        writer.Write(Stream);
        writer.Write(StartPosition);
        writer.WriteList(Destinations, (w, destination) => destination.Write(w));
    }

    internal static TeleportColumn Read(BinaryReader reader) => new()
    {
        Stream = reader.ReadString(),
        StartPosition = reader.ReadInt64(),
        Destinations = reader.ReadList(TeleportDestination.Read),
    };
}

/// <summary>Where one destination roll sends the player.</summary>
public sealed class TeleportDestination
{
    /// <summary>The player's top-left corner after the teleport (<c>Entity.position</c>), in world pixels.</summary>
    public float X { get; set; }

    public float Y { get; set; }

    /// <summary>False when every attempt failed: the potion then leaves the player where it is.</summary>
    public bool Found { get; set; }

    /// <summary>Random numbers the roll drew: two per place it tried.</summary>
    public int Draws { get; set; }

    /// <summary>The tile column of the middle of the player's feet.</summary>
    public int TileX => (int)Math.Floor((X + WorldCoordinates.PlayerWidth / 2) / WorldGeometry.TileSize);

    /// <summary>The tile row right under the player's feet: the one it stands on.</summary>
    public int TileY => (int)Math.Floor((Y + WorldCoordinates.PlayerHeight) / WorldGeometry.TileSize);

    internal void Write(BinaryWriter writer)
    {
        writer.Write(X);
        writer.Write(Y);
        writer.Write(Found);
        writer.Write(Draws);
    }

    internal static TeleportDestination Read(BinaryReader reader) => new()
    {
        X = reader.ReadSingle(),
        Y = reader.ReadSingle(),
        Found = reader.ReadBoolean(),
        Draws = reader.ReadInt32(),
    };
}

/// <summary>
/// A real random teleport: the stream its destination was rolled on, how many draws that stream had made earlier in
/// the same frame (comparable with a forecast offset taken at the start of that frame), and where it went.
/// </summary>
public sealed class TeleportRecord : StudioMessage
{
    public override MessageType Type => MessageType.TeleportRecord;

    /// <summary>The frame during which the teleport happened.</summary>
    public int Frame { get; set; }

    public TeleportKind Kind { get; set; }

    /// <summary>Stream the destination was rolled on, empty when unknown.</summary>
    public string Stream { get; set; } = "";

    /// <summary>Draws the stream made during the frame before the roll, or -1 when unknown.</summary>
    public long Offset { get; set; }

    /// <summary>Position of the stream when the roll started, or -1 when unknown.</summary>
    public long Position { get; set; }

    /// <summary>The player's top-left corner before and after, in world pixels.</summary>
    public float FromX { get; set; }

    public float FromY { get; set; }

    public float ToX { get; set; }

    public float ToY { get; set; }

    internal override void Write(BinaryWriter writer)
    {
        writer.Write(Frame);
        writer.Write((byte)Kind);
        writer.Write(Stream);
        writer.Write(Offset);
        writer.Write(Position);
        writer.Write(FromX);
        writer.Write(FromY);
        writer.Write(ToX);
        writer.Write(ToY);
    }

    internal static TeleportRecord Read(BinaryReader reader) => new()
    {
        Frame = reader.ReadInt32(),
        Kind = (TeleportKind)reader.ReadByte(),
        Stream = reader.ReadString(),
        Offset = reader.ReadInt64(),
        Position = reader.ReadInt64(),
        FromX = reader.ReadSingle(),
        FromY = reader.ReadSingle(),
        ToX = reader.ReadSingle(),
        ToY = reader.ReadSingle(),
    };
}
