using TerrariaTas.Core.Launch;

namespace TerrariaTas.Runtime;

/// <summary>How the TAS runtime was started, filled from the launcher's command line.</summary>
public sealed class TasOptions
{
    public string TerrariaDir { get; set; } = "";

    /// <summary>Folder given to Terraria as its save directory: Players/, Worlds/ and config.json live here.</summary>
    public string DataDir { get; set; } = "";

    public string? MoviePath { get; set; }

    public LaunchMode Mode { get; set; } = LaunchMode.FreePlay;

    /// <summary>For the creation launch of --new: the character and world to create, or the existing files to use.</summary>
    public NewMovieOptions NewMovie { get; set; } = new();

    public bool IgnoreVersionMismatch { get; set; }

    /// <summary>Run the determinism self-test on the movie, write the result and exit.</summary>
    public bool Verify => Mode == LaunchMode.Verify;

    /// <summary>A movie --new just created: recording starts at its first frame.</summary>
    public bool RecordFromStart => Mode == LaunchMode.New;

    /// <summary>The movie is watched (or verified): nothing records into it, reloads it or writes it.</summary>
    public bool ReadOnly => Mode is LaunchMode.Play or LaunchMode.Verify;
}
