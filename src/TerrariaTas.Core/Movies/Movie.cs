using System;
using System.Collections.Generic;
using System.Linq;

namespace TerrariaTas.Core.Movies;

/// <summary>A parsed TAS movie: header plus input lines laid out on a frame timeline.</summary>
public sealed class Movie
{
    private readonly int[] _lineStartFrames;

    public Movie(MovieHeader header, IReadOnlyList<InputLine> lines, IEnumerable<int>? breakpointFrames = null)
    {
        Header = header;
        Lines = lines;
        BreakpointFrames = (breakpointFrames ?? Array.Empty<int>()).Distinct().OrderBy(f => f).ToArray();

        _lineStartFrames = new int[lines.Count];
        var frame = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            _lineStartFrames[i] = frame;
            frame += lines[i].FrameCount;
        }

        TotalFrames = frame;
    }

    public static Movie Empty { get; } = new(new MovieHeader(), Array.Empty<InputLine>());

    public MovieHeader Header { get; }

    public IReadOnlyList<InputLine> Lines { get; }

    /// <summary>Frames before which playback pauses (from "***" lines), sorted ascending.</summary>
    public IReadOnlyList<int> BreakpointFrames { get; }

    public int TotalFrames { get; }

    public InputLine LineAt(int frame)
    {
        if (frame < 0 || frame >= TotalFrames)
            throw new ArgumentOutOfRangeException(nameof(frame), $"Frame {frame} is outside the movie (0..{TotalFrames - 1}).");

        var index = Array.BinarySearch(_lineStartFrames, frame);
        if (index < 0)
            index = ~index - 1;

        // Several lines can't start on the same frame (each has FrameCount >= 1), so this is the owning line.
        return Lines[index];
    }

    public int StartFrameOf(int lineIndex) => _lineStartFrames[lineIndex];
}
