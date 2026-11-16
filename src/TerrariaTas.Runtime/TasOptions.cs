namespace TerrariaTas.Runtime;

/// <summary>How the TAS runtime was started, filled from the launcher's command line.</summary>
public sealed class TasOptions
{
    public string TerrariaDir { get; set; } = "";

    /// <summary>Folder given to Terraria as its save directory: Players/, Worlds/ and config.json live here.</summary>
    public string DataDir { get; set; } = "";

    public string? MoviePath { get; set; }

    /// <summary>Start playing once the movie start is reached instead of waiting paused.</summary>
    public bool AutoPlay { get; set; }

    /// <summary>Run the determinism self-test on the movie, write the result and exit.</summary>
    public bool Verify { get; set; }

    public bool IgnoreVersionMismatch { get; set; }
}
