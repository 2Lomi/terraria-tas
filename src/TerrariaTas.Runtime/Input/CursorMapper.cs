using System;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaTas.Core.Movies;

namespace TerrariaTas.Runtime.Input;

/// <summary>Turns movie cursor targets into window pixels using the camera of the coming frame.</summary>
internal static class CursorMapper
{
    private const int TileSize = 16;

    public static Point ToScreen(CursorTarget target, Point screenSize)
    {
        if (target.Space == CursorSpace.Screen)
            return new Point(target.X, target.Y);

        // Inverse of PlayerInput.SetZoom_MouseInWorld followed by Main.MouseWorld, aimed at the tile's center
        // so that rounding can never land on a neighbouring tile.
        var world = new Vector2(target.X * TileSize + TileSize / 2f, target.Y * TileSize + TileSize / 2f);
        var offset = world - Main.screenPosition;
        if (Main.LocalPlayer.gravDir == -1f)
            offset.Y = screenSize.Y - offset.Y;

        var center = new Vector2(screenSize.X, screenSize.Y) / 2f;
        var zoom = Main.GameViewMatrix.RenderZoom.X;
        var raw = center + (offset - center) * zoom;
        return new Point((int)Math.Round(raw.X), (int)Math.Round(raw.Y));
    }
}
