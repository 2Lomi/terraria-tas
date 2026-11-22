using System;
using System.Collections.Generic;

namespace TerrariaTas.Core.Movies;

/// <summary>Edits of a movie's header that keep the rest of its text (comments, spacing, input lines) as it is.</summary>
public static class MovieHeaderText
{
    /// <summary>
    /// Adds a <c>PlayerChecksum</c> line under the <c>Player</c> line and a <c>WorldChecksum</c> line under the <c>World</c>
    /// line, each only when given and when the header has none yet.
    /// </summary>
    public static string AddChecksums(string text, string? playerChecksum, string? worldChecksum)
    {
        var lines = new List<string>(text.Split('\n'));
        var header = new List<(int Index, string Name)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var content = StripComment(lines[i]).Trim();
            if (content.Length == 0)
                continue;
            var colon = content.IndexOf(':');
            if (char.IsDigit(content[0]) || colon <= 0)
                break; // the first input line, breakpoint or command ends the header
            header.Add((i, content.Substring(0, colon).Trim()));
        }

        // From the bottom up, so that inserting a line leaves the indexes still to use in place.
        var inserts = new List<(int After, string Line)>();
        Plan("World", "WorldChecksum", worldChecksum);
        Plan("Player", "PlayerChecksum", playerChecksum);
        inserts.Sort((a, b) => b.After.CompareTo(a.After));
        var cr = text.Contains("\r\n") ? "\r" : ""; // lines keep their '\r' when the file ends them with "\r\n"
        foreach (var (after, line) in inserts)
        {
            if (after == lines.Count - 1)
            {
                // The header line ends the file, without a newline: the new line takes over the end.
                lines[after] += cr;
                lines.Add(line);
            }
            else
            {
                lines.Insert(after + 1, line + cr);
            }
        }

        return string.Join("\n", lines);

        void Plan(string property, string checksumProperty, string? checksum)
        {
            if (checksum == null || header.Exists(entry => Is(entry.Name, checksumProperty)))
                return;
            var index = header.FindIndex(entry => Is(entry.Name, property));
            if (index >= 0)
                inserts.Add((header[index].Index, $"{checksumProperty}: {checksum}"));
        }
    }

    private static bool Is(string name, string property) => name.Equals(property, StringComparison.OrdinalIgnoreCase);

    private static string StripComment(string line)
    {
        var hash = line.IndexOf('#');
        return hash < 0 ? line : line.Substring(0, hash);
    }
}
