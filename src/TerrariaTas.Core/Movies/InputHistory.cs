using System;
using System.Collections.Generic;

namespace TerrariaTas.Core.Movies;

/// <summary>
/// The input of a movie's first frames. Kept with each savestate: it tells which version of the movie the
/// state belongs to, and lets a recording follow the state when it is loaded.
/// </summary>
public sealed class InputHistory
{
    internal InputHistory(IReadOnlyList<InputLine> lines, int frames)
    {
        Lines = lines;
        Frames = frames;
    }

    public static InputHistory Empty { get; } = new(Array.Empty<InputLine>(), 0);

    /// <summary>Movie lines covering exactly <see cref="Frames"/> frames.</summary>
    public IReadOnlyList<InputLine> Lines { get; }

    public int Frames { get; }

    /// <summary>The first <paramref name="frames"/> frames of <paramref name="lines"/> (fewer if they cover less).</summary>
    public static InputHistory Of(IReadOnlyList<InputLine> lines, int frames)
    {
        var kept = new List<InputLine>();
        var covered = 0;
        foreach (var line in lines)
        {
            if (covered >= frames)
                break;

            var count = Math.Min(line.FrameCount, frames - covered);
            kept.Add(count == line.FrameCount ? line : line.WithFrameCount(count));
            covered += count;
        }

        return new InputHistory(kept, covered);
    }

    /// <summary>
    /// The first frame where <paramref name="a"/> and <paramref name="b"/> differ, counting the end of the shorter one
    /// as a difference, or null when they hold the same input.
    /// </summary>
    public static int? FirstDifference(InputHistory a, InputHistory b)
    {
        int frame = 0, lineA = -1, lineB = -1, endA = 0, endB = 0;
        var frames = Math.Min(a.Frames, b.Frames);
        while (frame < frames)
        {
            if (frame == endA)
                endA += a.Lines[++lineA].FrameCount;
            if (frame == endB)
                endB += b.Lines[++lineB].FrameCount;
            if (!a.Lines[lineA].HasSameInputs(b.Lines[lineB]))
                return frame;
            frame = Math.Min(endA, endB);
        }

        return a.Frames == b.Frames ? null : frames;
    }

    /// <summary>
    /// The frames before <paramref name="at"/> from <paramref name="first"/>, then the frames from <paramref name="at"/>
    /// on from <paramref name="second"/>. When <paramref name="first"/> stops before <paramref name="at"/>, so does the result.
    /// </summary>
    public static InputHistory Splice(InputHistory first, int at, InputHistory second)
    {
        if (first.Frames < at)
            return first;

        var lines = new List<InputLine>(Of(first.Lines, at).Lines);
        var start = 0;
        foreach (var line in second.Lines)
        {
            var end = start + line.FrameCount;
            if (end > at)
                lines.Add(start >= at ? line : line.WithFrameCount(end - at));
            start = end;
        }

        return new InputHistory(lines, Math.Max(at, second.Frames));
    }
}
