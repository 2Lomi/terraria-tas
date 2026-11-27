using System;
using Terraria;
using Terraria.Map;
using TerrariaTas.Core.Studio;
using TerrariaTas.Runtime.GameAccess;

namespace TerrariaTas.Runtime.Inspection;

/// <summary>
/// Reads the world map for Studio: every tile in the color the game's own map gives it, fully revealed, through the
/// game's map code (<c>MapHelper</c>), so no color table is re-implemented. Reading changes nothing: the player's
/// exploration map is only read (the snowy tint of cave backgrounds looks at it), never written.
/// </summary>
internal static class WorldMapReader
{
    /// <summary>Rows per message: about half a million tiles in a large world.</summary>
    public const int RowsPerMessage = 64;

    // The tiles along the edges, which the player can never reach. The game's map code looks 36 tiles around a cave
    // background tile there, past the ends of its arrays, so they get a plain color instead.
    private const int OutsideColor = 0x000000;

    /// <summary>Rows in the loaded world.</summary>
    public static int Height => Main.maxTilesY;

    public static MapHeader Header(int requestId, int frame) => new()
    {
        RequestId = requestId,
        Frame = frame,
        WorldName = Main.worldName,
        World = GameWorld.Geometry(),
    };

    /// <summary>Up to <paramref name="count"/> rows from <paramref name="firstRow"/> on. Call at a frame boundary inside a world.</summary>
    public static MapRows ReadRows(int requestId, int frame, int firstRow, int count)
    {
        var width = Main.maxTilesX;
        var height = Main.maxTilesY;
        var border = Main.offLimitBorderTiles;
        var end = Math.Min(height, firstRow + count);
        var builder = new MapRowsBuilder(width);
        for (var y = firstRow; y < end; y++)
        {
            var outsideRow = y < border || y >= height - border;
            for (var x = 0; x < width; x++)
                builder.Add(outsideRow || x < border || x >= width - border ? OutsideColor : ColorOf(x, y));
        }

        return builder.Build(requestId, frame, firstRow);
    }

    private static int ColorOf(int x, int y)
    {
        var color = MapHelper.GetMapTileXnaColor(MapHelper.CreateMapTile(x, y, byte.MaxValue), x, y);
        return (color.R << 16) | (color.G << 8) | color.B;
    }
}
