using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TerrariaTas.Core.Movies;

/// <summary>Writes movies back to text; <see cref="MovieParser.Parse"/> reads the output unchanged.</summary>
public static class MovieWriter
{
    public static string Write(Movie movie)
    {
        var text = new StringBuilder();
        WriteHeader(text, movie.Header);

        var breakpoints = new HashSet<int>(movie.BreakpointFrames);
        CursorTarget? lastCursor = null;
        for (var i = 0; i < movie.Lines.Count; i++)
        {
            if (breakpoints.Contains(movie.StartFrameOf(i)))
                text.Append("***\n");

            WriteLine(text, movie.Lines[i], lastCursor);
            lastCursor = movie.Lines[i].Cursor;
        }

        if (breakpoints.Contains(movie.TotalFrames))
            text.Append("***\n");

        return text.ToString();
    }

    private static void WriteHeader(StringBuilder text, MovieHeader header)
    {
        if (header.TerrariaVersion != null)
            text.Append("Terraria: ").Append(header.TerrariaVersion).Append('\n');
        if (header.Player != null)
            text.Append("Player: ").Append(header.Player).Append('\n');
        if (header.World != null)
            text.Append("World: ").Append(header.World).Append('\n');
        text.Append("Seed: ").Append(header.Seed.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("Date: ").Append(header.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
        text.Append("Resolution: ").Append(header.ScreenWidth.ToString(CultureInfo.InvariantCulture))
            .Append('x').Append(header.ScreenHeight.ToString(CultureInfo.InvariantCulture)).Append("\n\n");
    }

    private static void WriteLine(StringBuilder text, InputLine line, CursorTarget? lastCursor)
    {
        text.Append(line.FrameCount.ToString(CultureInfo.InvariantCulture).PadLeft(6));

        foreach (var key in line.Keys)
            text.Append(' ').Append(key);

        foreach (var token in MovieParser.ButtonTokens.Where(t => (line.Buttons & t.Value) != 0))
            text.Append(' ').Append(token.Key);

        var wheelToken = line.Wheel > 0 ? "WheelUp" : "WheelDown";
        for (var i = 0; i < Math.Abs(line.Wheel); i++)
            text.Append(' ').Append(wheelToken);

        if (line.Cursor.HasValue && !Nullable.Equals(line.Cursor, lastCursor))
            text.Append(' ').Append(line.Cursor.Value);

        text.Append('\n');
    }
}
