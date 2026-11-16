using System;
using System.Collections.Generic;
using System.Linq;

namespace TerrariaTas.Core.Movies;

/// <summary>One movie line: the same inputs held for <see cref="FrameCount"/> consecutive frames.</summary>
public sealed class InputLine
{
    public InputLine(int frameCount, IEnumerable<Key> keys, MouseButtons buttons, CursorTarget? cursor, int wheel, int sourceLine = 0)
    {
        FrameCount = frameCount;
        Keys = keys.Distinct().OrderBy(k => k).ToArray();
        Buttons = buttons;
        Cursor = cursor;
        Wheel = wheel;
        SourceLine = sourceLine;
    }

    public int FrameCount { get; }

    /// <summary>Keys held down, sorted and without duplicates.</summary>
    public IReadOnlyList<Key> Keys { get; }

    public MouseButtons Buttons { get; }

    /// <summary>Cursor position in effect on this line (positions carry over from earlier lines).</summary>
    public CursorTarget? Cursor { get; }

    /// <summary>Mouse wheel notches applied on every frame of the line; positive scrolls up.</summary>
    public int Wheel { get; }

    /// <summary>1-based line number in the movie file, 0 when the line was not parsed from text.</summary>
    public int SourceLine { get; }

    public bool HasSameInputs(InputLine other) =>
        Buttons == other.Buttons
        && Wheel == other.Wheel
        && Nullable.Equals(Cursor, other.Cursor)
        && Keys.SequenceEqual(other.Keys);

    public InputLine WithFrameCount(int frameCount) => new(frameCount, Keys, Buttons, Cursor, Wheel, SourceLine);
}
