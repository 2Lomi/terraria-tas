using System.IO;

namespace TerrariaTas.Core.Studio;

public enum MessageType : byte
{
    // Game to Studio.
    Hello = 1,
    MovieData = 2,
    FrameStatus = 3,
    WorldSnapshot = 4,
    DropForecast = 5,
    KillRecord = 6,
    ItemIcons = 7,
    HotkeyBindings = 8,

    // Studio to game.
    Command = 64,
    ForecastRequest = 65,
    WatchList = 66,
}

/// <summary>A message of the Studio link. <see cref="StudioCodec"/> turns it into bytes and back.</summary>
public abstract class StudioMessage
{
    public abstract MessageType Type { get; }

    internal abstract void Write(BinaryWriter writer);
}
