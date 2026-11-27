using System.IO;

namespace TerrariaTas.Core.World;

/// <summary>
/// The size of a world and the heights of its layers, in tiles: what coordinate readouts and the world map need.
/// Read from the running game (<c>Main.maxTilesX</c>, <c>Main.worldSurface</c>...) and sent to TAS Studio.
/// </summary>
public sealed class WorldGeometry
{
    /// <summary>Pixels per tile side.</summary>
    public const int TileSize = 16;

    /// <summary>Width in tiles (<c>Main.maxTilesX</c>): 4200, 6400 or 8400 for small, medium and large worlds.</summary>
    public int Width { get; set; }

    /// <summary>Height in tiles (<c>Main.maxTilesY</c>).</summary>
    public int Height { get; set; }

    /// <summary>Tile row where the surface ends and the underground begins (<c>Main.worldSurface</c>).</summary>
    public double WorldSurface { get; set; }

    /// <summary>Tile row where the caverns begin (<c>Main.rockLayer</c>).</summary>
    public double RockLayer { get; set; }

    public int SpawnX { get; set; }

    public int SpawnY { get; set; }

    /// <summary>Tiles along each edge of the world that the player can never reach (<c>Main.offLimitBorderTiles</c>).</summary>
    public int BorderTiles { get; set; }

    /// <summary>Tile row where the underworld begins (<c>Main.UnderworldLayer</c>).</summary>
    public int UnderworldLayer => Height - 200;

    internal void Write(BinaryWriter writer)
    {
        writer.Write(Width);
        writer.Write(Height);
        writer.Write(WorldSurface);
        writer.Write(RockLayer);
        writer.Write(SpawnX);
        writer.Write(SpawnY);
        writer.Write(BorderTiles);
    }

    internal static WorldGeometry Read(BinaryReader reader) => new()
    {
        Width = reader.ReadInt32(),
        Height = reader.ReadInt32(),
        WorldSurface = reader.ReadDouble(),
        RockLayer = reader.ReadDouble(),
        SpawnX = reader.ReadInt32(),
        SpawnY = reader.ReadInt32(),
        BorderTiles = reader.ReadInt32(),
    };
}
