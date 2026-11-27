using Terraria;
using TerrariaTas.Core.World;

namespace TerrariaTas.Runtime.GameAccess;

/// <summary>The loaded world's size and layer heights. Only meaningful inside a world.</summary>
internal static class GameWorld
{
    public static WorldGeometry Geometry() => new()
    {
        Width = Main.maxTilesX,
        Height = Main.maxTilesY,
        WorldSurface = Main.worldSurface,
        RockLayer = Main.rockLayer,
        SpawnX = Main.spawnTileX,
        SpawnY = Main.spawnTileY,
        BorderTiles = Main.offLimitBorderTiles,
    };
}
