using System;

namespace TerrariaTas.Core.Movies;

/// <summary>Everything besides inputs that is needed to replay a movie identically.</summary>
public sealed class MovieHeader
{
    /// <summary>Terraria build the movie was made on, e.g. "1.4.5.8". Null when not specified.</summary>
    public string? TerrariaVersion { get; set; }

    /// <summary>Player file name inside the TAS data folder's Players directory.</summary>
    public string? Player { get; set; }

    /// <summary>World file name inside the TAS data folder's Worlds directory.</summary>
    public string? World { get; set; }

    /// <summary>Seed of the main-thread RNG (Main.rand) when the movie starts.</summary>
    public int Seed { get; set; }

    /// <summary>Calendar date seen by the game (Halloween and Christmas depend on it).</summary>
    public DateTime Date { get; set; } = new(2026, 6, 15);

    public int ScreenWidth { get; set; } = 1280;

    public int ScreenHeight { get; set; } = 720;

    public bool StartsInWorld => Player != null && World != null;
}
