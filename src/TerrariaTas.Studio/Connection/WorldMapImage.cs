using System;
using System.IO;
using TerrariaTas.Core.Studio;
using TerrariaTas.Core.World;

namespace TerrariaTas.Studio.Connection;

/// <summary>
/// The world map as the game sends it: one 0xRRGGBB color per tile, filled row by row as the rows arrive.
/// Only used from the UI thread.
/// </summary>
internal sealed class WorldMapImage
{
    public WorldMapImage(MapHeader header)
    {
        RequestId = header.RequestId;
        WorldName = header.WorldName;
        World = header.World;
        FirstFrame = header.Frame;
        LastFrame = header.Frame;
        Pixels = new int[checked(header.World.Width * header.World.Height)];
    }

    public int RequestId { get; }

    public string WorldName { get; }

    public WorldGeometry World { get; }

    /// <summary>Row-major: the color of tile (x, y) is at y * width + x.</summary>
    public int[] Pixels { get; }

    /// <summary>Rows filled so far, from the top.</summary>
    public int RowsReceived { get; private set; }

    /// <summary>The frames the rows were read at: the game may have run frames while they were sent.</summary>
    public int FirstFrame { get; private set; }

    public int LastFrame { get; private set; }

    /// <summary>Why the map is incomplete, empty while it is fine.</summary>
    public string Error { get; private set; } = "";

    public bool Complete => RowsReceived >= World.Height;

    public void Add(MapRows rows)
    {
        if (Error.Length > 0)
            return;
        if (rows.FirstRow != RowsReceived)
        {
            Error = "Rows of the map went missing on the way: refresh it.";
            return;
        }

        try
        {
            rows.CopyTo(Pixels, World.Width);
        }
        catch (InvalidDataException e)
        {
            Error = e.Message;
            return;
        }

        RowsReceived += rows.RowCount;
        FirstFrame = Math.Min(FirstFrame, rows.Frame);
        LastFrame = Math.Max(LastFrame, rows.Frame);
    }

    public void Fail(string error) => Error = error;
}

/// <summary>A place the teleport forecast looks for: destinations within <see cref="Radius"/> tiles of a tile.</summary>
internal sealed record TeleportTarget(int TileX, int TileY, int Radius)
{
    public bool Contains(TeleportDestination destination) => destination.Found && DistanceTo(destination) <= Radius;

    public double DistanceTo(TeleportDestination destination)
    {
        var dx = destination.TileX - TileX;
        var dy = destination.TileY - TileY;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
