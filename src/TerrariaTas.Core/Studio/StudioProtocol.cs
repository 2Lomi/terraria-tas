namespace TerrariaTas.Core.Studio;

/// <summary>
/// The link between the TAS running inside the game and TAS Studio, the external tool windows.
/// The game hosts a local named pipe and Studio connects to it. Every message travels as a
/// length-prefixed frame: a type byte, then a body written with <see cref="System.IO.BinaryWriter"/>.
/// </summary>
public static class StudioProtocol
{
    public const string PipeName = "TerrariaTas.Studio";

    /// <summary>Bumped on any change to a message layout. Studio refuses to talk to another version.</summary>
    public const int Version = 5;

    /// <summary>Upper bound for one frame. Anything larger means the stream is corrupted.</summary>
    public const int MaxFrameBytes = 64 * 1024 * 1024;

    /// <summary>Savestate slots: 0 holds the movie start, 1-9 are the user's.</summary>
    public const int SlotCount = 10;
}
