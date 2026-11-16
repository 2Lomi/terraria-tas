using System;

namespace TerrariaTas.Core.Movies;

public enum CursorSpace
{
    /// <summary>Pixels in the game window's client area.</summary>
    Screen,

    /// <summary>A world tile; the game layer aims at its center using the camera of that frame.</summary>
    Tile,
}

/// <summary>Where the mouse cursor is during a frame.</summary>
public readonly struct CursorTarget : IEquatable<CursorTarget>
{
    public CursorTarget(CursorSpace space, int x, int y)
    {
        Space = space;
        X = x;
        Y = y;
    }

    public CursorSpace Space { get; }

    public int X { get; }

    public int Y { get; }

    public static CursorTarget Screen(int x, int y) => new(CursorSpace.Screen, x, y);

    public static CursorTarget Tile(int x, int y) => new(CursorSpace.Tile, x, y);

    public bool Equals(CursorTarget other) => Space == other.Space && X == other.X && Y == other.Y;

    public override bool Equals(object? obj) => obj is CursorTarget other && Equals(other);

    public override int GetHashCode() => ((int)Space * 397 ^ X) * 397 ^ Y;

    public override string ToString() => Space == CursorSpace.Tile ? $"@t{X},{Y}" : $"@{X},{Y}";
}
