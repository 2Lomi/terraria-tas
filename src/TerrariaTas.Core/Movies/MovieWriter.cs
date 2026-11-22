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

            AppendLine(text, movie.Lines[i], lastCursor, "\n");
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
        if (header.PlayerChecksum != null)
            text.Append("PlayerChecksum: ").Append(header.PlayerChecksum).Append('\n');
        if (header.World != null)
            text.Append("World: ").Append(header.World).Append('\n');
        if (header.WorldChecksum != null)
            text.Append("WorldChecksum: ").Append(header.WorldChecksum).Append('\n');
        text.Append("Seed: ").Append(header.Seed.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("Date: ").Append(header.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
        text.Append("Resolution: ").Append(header.ScreenWidth.ToString(CultureInfo.InvariantCulture))
            .Append('x').Append(header.ScreenHeight.ToString(CultureInfo.InvariantCulture)).Append('\n');

        var settings = GameSettings.All.Where(setting => header.Settings.ContainsKey(setting.Name)).ToList();
        if (settings.Count > 0)
            text.Append("# Game settings that change the simulation, applied when the movie loads (docs/movie-format.md).\n");
        foreach (var setting in settings)
            text.Append(setting.Name).Append(": ").Append(header.Settings[setting.Name]).Append('\n');

        var bindings = KeyBindings.All.Where(binding => header.Bindings.ContainsKey(binding.Name)).ToList();
        if (bindings.Count > 0)
            text.Append("# Key bindings; the actions not listed have a fresh install's keys (docs/movie-format.md).\n");
        foreach (var binding in bindings)
        {
            text.Append("Bind ").Append(binding.Name).Append(':');
            foreach (var input in header.Bindings[binding.Name])
                text.Append(' ').Append(input);
            text.Append('\n');
        }

        text.Append('\n');
    }

    /// <summary>Writes one input line; its cursor only when it differs from <paramref name="lastCursor"/>, the one in effect before it.</summary>
    internal static void AppendLine(StringBuilder text, InputLine line, CursorTarget? lastCursor, string newline)
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

        text.Append(newline);
    }
}
