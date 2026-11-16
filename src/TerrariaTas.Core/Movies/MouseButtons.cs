using System;

namespace TerrariaTas.Core.Movies;

[Flags]
public enum MouseButtons : byte
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 4,
    X1 = 8,
    X2 = 16,
}
