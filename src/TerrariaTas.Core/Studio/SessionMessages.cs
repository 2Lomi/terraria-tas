using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TerrariaTas.Core.Hotkeys;
using TerrariaTas.Core.Movies;
using TerrariaTas.Core.Playback;

namespace TerrariaTas.Core.Studio;

public enum SessionPhase : byte
{
    Booting,
    LoadingWorld,
    Running,
}

/// <summary>First message of every connection. Its layout must never change, so that versions can be compared.</summary>
public sealed class Hello : StudioMessage
{
    public override MessageType Type => MessageType.Hello;

    public int ProtocolVersion { get; set; } = StudioProtocol.Version;

    public string TasVersion { get; set; } = "";

    public string TerrariaVersion { get; set; } = "";

    /// <summary>File name of the movie, empty in free play.</summary>
    public string MovieName { get; set; } = "";

    internal override void Write(BinaryWriter writer)
    {
        writer.Write(ProtocolVersion);
        writer.Write(TasVersion);
        writer.Write(TerrariaVersion);
        writer.Write(MovieName);
    }

    internal static Hello Read(BinaryReader reader) => new()
    {
        ProtocolVersion = reader.ReadInt32(),
        TasVersion = reader.ReadString(),
        TerrariaVersion = reader.ReadString(),
        MovieName = reader.ReadString(),
    };
}

/// <summary>The game's TAS hotkeys (its hotkeys.txt), sent after <see cref="Hello"/>: Studio windows answer the same keys.</summary>
public sealed class HotkeyBindings : StudioMessage
{
    public override MessageType Type => MessageType.HotkeyBindings;

    public HotkeyMap Map { get; set; } = HotkeyMap.Default;

    internal override void Write(BinaryWriter writer) =>
        writer.WriteList(Map.Keys.ToList(), (w, binding) =>
        {
            w.Write((byte)binding.Key);
            w.Write((byte)binding.Value);
        });

    internal static HotkeyBindings Read(BinaryReader reader)
    {
        var keys = reader.ReadList(r => (Action: (HotkeyAction)r.ReadByte(), Key: (Key)r.ReadByte()));
        return new HotkeyBindings { Map = new HotkeyMap(keys.ToDictionary(binding => binding.Action, binding => binding.Key)) };
    }
}

/// <summary>
/// The movie's input lines from <see cref="FirstLine"/> on, replacing the ones Studio has from there: the whole
/// movie on connection, then what changed (recording only changes the end).
/// </summary>
public sealed class MovieData : StudioMessage
{
    public override MessageType Type => MessageType.MovieData;

    public int FirstLine { get; set; }

    public List<InputLine> Lines { get; set; } = new();

    /// <summary>All the breakpoints of the movie.</summary>
    public int[] BreakpointFrames { get; set; } = Array.Empty<int>();

    /// <summary>The movie after this update, given the one before it.</summary>
    public Movie ApplyTo(Movie current)
    {
        var lines = current.Lines.Take(Math.Min(FirstLine, current.Lines.Count)).Concat(Lines).ToArray();
        return new Movie(new MovieHeader(), lines, BreakpointFrames);
    }

    internal override void Write(BinaryWriter writer)
    {
        writer.Write(FirstLine);
        writer.WriteList(Lines, (w, line) => w.WriteInputLine(line));
        writer.WriteInts(BreakpointFrames);
    }

    internal static MovieData Read(BinaryReader reader) => new()
    {
        FirstLine = reader.ReadInt32(),
        Lines = reader.ReadList(r => r.ReadInputLine()),
        BreakpointFrames = reader.ReadInts(),
    };
}

/// <summary>Where the TAS is, sent on every change while paused and about 60 times per second while playing.</summary>
public sealed class FrameStatus : StudioMessage
{
    public override MessageType Type => MessageType.FrameStatus;

    public SessionPhase Phase { get; set; }

    /// <summary>The next frame to run, which is also the number of frames played.</summary>
    public int Frame { get; set; }

    public int TotalFrames { get; set; }

    public PlaybackState State { get; set; }

    /// <summary>Frames played take the player's input and write it into the movie.</summary>
    public bool Recording { get; set; }

    /// <summary>Savestate slot that the save and load hotkeys use.</summary>
    public int Slot { get; set; }

    /// <summary>For each slot, the frame its savestate was taken at, or -1 when empty.</summary>
    public int[] SlotFrames { get; set; } = Array.Empty<int>();

    /// <summary>Last message of the TAS (what was saved, loaded, reloaded...).</summary>
    public string Notice { get; set; } = "";

    /// <summary>What the game read from the devices during the last frame.</summary>
    public InputView Input { get; set; } = new();

    /// <summary>While recording: what the next frame would record right now (the player's keyboard and mouse).</summary>
    public InputView? NextInput { get; set; }

    /// <summary>The player as the frame boundary leaves it, in a world; null elsewhere (menus, loading).</summary>
    public PlayerView? Player { get; set; }

    /// <summary>The frame, position and velocity show over the game.</summary>
    public bool Overlay { get; set; }

    /// <summary>The <see cref="WatchList.Version"/> that <see cref="WatchValues"/> answers.</summary>
    public long WatchVersion { get; set; }

    /// <summary>The value of each watched path, in the order of the list; "! reason" when one cannot be read.</summary>
    public string[] WatchValues { get; set; } = Array.Empty<string>();

    internal override void Write(BinaryWriter writer)
    {
        writer.Write((byte)Phase);
        writer.Write(Frame);
        writer.Write(TotalFrames);
        writer.Write((byte)State);
        writer.Write(Recording);
        writer.Write(Slot);
        writer.WriteInts(SlotFrames);
        writer.Write(Notice);
        Input.Write(writer);
        writer.Write(NextInput != null);
        NextInput?.Write(writer);
        writer.Write(Player != null);
        Player?.Write(writer);
        writer.Write(Overlay);
        writer.Write(WatchVersion);
        writer.WriteList(WatchValues, (w, value) => w.Write(value));
    }

    internal static FrameStatus Read(BinaryReader reader) => new()
    {
        Phase = (SessionPhase)reader.ReadByte(),
        Frame = reader.ReadInt32(),
        TotalFrames = reader.ReadInt32(),
        State = (PlaybackState)reader.ReadByte(),
        Recording = reader.ReadBoolean(),
        Slot = reader.ReadInt32(),
        SlotFrames = reader.ReadInts(),
        Notice = reader.ReadString(),
        Input = InputView.Read(reader),
        NextInput = reader.ReadBoolean() ? InputView.Read(reader) : null,
        Player = reader.ReadBoolean() ? PlayerView.Read(reader) : null,
        Overlay = reader.ReadBoolean(),
        WatchVersion = reader.ReadInt64(),
        WatchValues = reader.ReadList(r => r.ReadString()).ToArray(),
    };
}

/// <summary>
/// The memory watch of Studio: paths into the game's objects (TerrariaTas.Core.Watch.WatchPath) whose values come back in
/// every <see cref="FrameStatus"/>. Replaces the previous list; an empty one stops watching.
/// </summary>
public sealed class WatchList : StudioMessage
{
    public override MessageType Type => MessageType.WatchList;

    /// <summary>Increases with every list Studio sends, so that it knows which list values answer.</summary>
    public long Version { get; set; }

    public List<string> Paths { get; set; } = new();

    /// <summary>Also write the values of every frame to a file in the TAS data folder.</summary>
    public bool LogToFile { get; set; }

    internal override void Write(BinaryWriter writer)
    {
        writer.Write(Version);
        writer.WriteList(Paths, (w, path) => w.Write(path));
        writer.Write(LogToFile);
    }

    internal static WatchList Read(BinaryReader reader) => new()
    {
        Version = reader.ReadInt64(),
        Paths = reader.ReadList(r => r.ReadString()),
        LogToFile = reader.ReadBoolean(),
    };
}

/// <summary>Keyboard and mouse as the game saw them, whether they came from the movie or from the real devices.</summary>
public sealed class InputView
{
    public Key[] Keys { get; set; } = Array.Empty<Key>();

    public MouseButtons Buttons { get; set; }

    /// <summary>Cursor in window pixels.</summary>
    public int CursorX { get; set; }

    public int CursorY { get; set; }

    /// <summary>World tile under the cursor.</summary>
    public int CursorTileX { get; set; }

    public int CursorTileY { get; set; }

    internal void Write(BinaryWriter writer)
    {
        writer.WriteKeys(Keys);
        writer.Write((byte)Buttons);
        writer.Write(CursorX);
        writer.Write(CursorY);
        writer.Write(CursorTileX);
        writer.Write(CursorTileY);
    }

    internal static InputView Read(BinaryReader reader) => new()
    {
        Keys = reader.ReadKeys(),
        Buttons = (MouseButtons)reader.ReadByte(),
        CursorX = reader.ReadInt32(),
        CursorY = reader.ReadInt32(),
        CursorTileX = reader.ReadInt32(),
        CursorTileY = reader.ReadInt32(),
    };
}

public enum CommandKind : byte
{
    TogglePause,
    FrameAdvance,
    FastForwardOn,
    FastForwardOff,
    PreviousSlot,
    NextSlot,

    /// <summary>Argument: the slot (0-9).</summary>
    SelectSlot,

    /// <summary>Argument: the slot (1-9). Saves the current frame.</summary>
    SaveSlot,

    /// <summary>Argument: the slot (0-9).</summary>
    LoadSlot,

    Restart,
    ReloadMovie,

    /// <summary>Argument: the frame. Loads the closest earlier savestate if needed, then fast-forwards and pauses there.</summary>
    RunToFrame,

    /// <summary>Starts or stops recording the player's input into the movie.</summary>
    ToggleRecording,

    /// <summary>Shows or hides the frame, position and velocity over the game.</summary>
    ToggleOverlay,
}

/// <summary>A playback or savestate action, the same as the TAS hotkeys.</summary>
public sealed class Command : StudioMessage
{
    public Command()
    {
    }

    public Command(CommandKind kind, int argument = 0)
    {
        Kind = kind;
        Argument = argument;
    }

    public override MessageType Type => MessageType.Command;

    public CommandKind Kind { get; set; }

    public int Argument { get; set; }

    internal override void Write(BinaryWriter writer)
    {
        writer.Write((byte)Kind);
        writer.Write(Argument);
    }

    internal static Command Read(BinaryReader reader) => new((CommandKind)reader.ReadByte(), reader.ReadInt32());
}
