using System;

namespace TerrariaTas.Core.World;

/// <summary>
/// What the Compass and the Depth Meter read for a player at a given place, computed as the game does: the
/// coordinates speedrunners share. Both count in "feet", two per tile: the compass from the world's center
/// column, the depth from the surface line.
/// </summary>
[MirrorsGameMethod("Terraria.Main", "DrawInfoAccs")]
public static class WorldCoordinates
{
    /// <summary>The player's hitbox in pixels (<c>Player.width</c>, <c>Player.height</c>).</summary>
    public const int PlayerWidth = 20;

    public const int PlayerHeight = 42;

    /// <summary>The compass value for a player whose center is at <paramref name="centerX"/>: positive east of the world's center.</summary>
    public static int CompassValue(float centerX, WorldGeometry world) => (int)(centerX * 2f / 16f - world.Width);

    /// <summary>"1234' East", "1234' West" or "Center".</summary>
    public static string Compass(float centerX, WorldGeometry world)
    {
        var value = CompassValue(centerX, world);
        return value > 0 ? $"{value}' East" : value < 0 ? $"{-value}' West" : "Center";
    }

    /// <summary>The depth value for a player whose feet are at <paramref name="bottomY"/>: positive below the surface line.</summary>
    public static int DepthValue(float bottomY, WorldGeometry world) => (int)((double)(bottomY * 2f / 16f) - world.WorldSurface * 2.0);

    /// <summary>The layer the depth meter names: Space, Surface, Underground, Caverns or Underworld.</summary>
    public static string Layer(float bottomY, float centerY, WorldGeometry world)
    {
        if (bottomY > (float)((world.Height - 204) * 16))
            return "Underworld";
        if ((double)bottomY > world.RockLayer * 16.0)
            return "Caverns";
        if (DepthValue(bottomY, world) > 0)
            return "Underground";

        // Space starts higher in larger worlds.
        var sizeFactor = (float)world.Width / 4200f;
        sizeFactor *= sizeFactor;
        var height = (float)((double)(centerY / 16f - (65f + 10f * sizeFactor)) / (world.WorldSurface / 5.0));
        return height >= 1f ? "Surface" : "Space";
    }

    /// <summary>"345' Caverns", "Level Surface"...</summary>
    public static string Depth(float bottomY, float centerY, WorldGeometry world)
    {
        var value = Math.Abs(DepthValue(bottomY, world));
        return (value == 0 ? "Level" : $"{value}'") + " " + Layer(bottomY, centerY, world);
    }

    /// <summary>Both readings for a player whose top-left corner (<c>Entity.position</c>) is at (x, y) in world pixels.</summary>
    public static string AtPosition(float x, float y, WorldGeometry world) =>
        Compass(x + PlayerWidth / 2, world) + ", " + Depth(y + PlayerHeight, y + PlayerHeight / 2, world);

    /// <summary>Both readings for a player standing with the middle of its feet on the world pixel (x, y).</summary>
    public static string AtFeet(float x, float y, WorldGeometry world) => AtPosition(x - PlayerWidth / 2, y - PlayerHeight, world);
}
