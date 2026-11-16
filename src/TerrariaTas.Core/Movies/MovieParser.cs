using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TerrariaTas.Core.Movies;

/// <summary>Reads the text movie format described in docs/movie-format.md.</summary>
public static class MovieParser
{
    internal static readonly IReadOnlyDictionary<string, MouseButtons> ButtonTokens =
        new Dictionary<string, MouseButtons>(StringComparer.OrdinalIgnoreCase)
        {
            ["LMB"] = MouseButtons.Left,
            ["RMB"] = MouseButtons.Right,
            ["MMB"] = MouseButtons.Middle,
            ["XB1"] = MouseButtons.X1,
            ["XB2"] = MouseButtons.X2,
        };

    public static Movie Load(string path) => Parse(File.ReadAllText(path));

    public static Movie Parse(string text)
    {
        var header = new MovieHeader();
        var lines = new List<InputLine>();
        var breakpoints = new List<int>();
        var frame = 0;
        CursorTarget? cursor = null;

        var rawLines = text.Split('\n');
        for (var i = 0; i < rawLines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = StripComment(rawLines[i]).Trim();
            if (line.Length == 0)
                continue;

            if (line == "***")
            {
                breakpoints.Add(frame);
                continue;
            }

            if (char.IsDigit(line[0]))
            {
                var input = ParseInput(line, lineNumber, ref cursor);
                lines.Add(input);
                frame += input.FrameCount;
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                if (lines.Count > 0)
                    throw new MovieFormatException(lineNumber, "Header properties must come before the first input line.");
                ApplyHeader(header, line.Substring(0, colon).Trim(), line.Substring(colon + 1).Trim(), lineNumber);
                continue;
            }

            throw new MovieFormatException(lineNumber, $"Cannot understand '{line}'.");
        }

        return new Movie(header, lines, breakpoints);
    }

    private static string StripComment(string line)
    {
        var hash = line.IndexOf('#');
        return hash < 0 ? line : line.Substring(0, hash);
    }

    private static InputLine ParseInput(string line, int lineNumber, ref CursorTarget? cursor)
    {
        var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (!int.TryParse(tokens[0], NumberStyles.None, CultureInfo.InvariantCulture, out var frames) || frames < 1)
            throw new MovieFormatException(lineNumber, $"'{tokens[0]}' is not a valid frame count.");

        var keys = new List<Key>();
        var buttons = MouseButtons.None;
        var wheel = 0;
        foreach (var token in tokens.Skip(1))
        {
            if (token[0] == '@')
                cursor = ParseCursor(token, lineNumber);
            else if (ButtonTokens.TryGetValue(token, out var button))
                buttons |= button;
            else if (token.Equals("WheelUp", StringComparison.OrdinalIgnoreCase))
                wheel++;
            else if (token.Equals("WheelDown", StringComparison.OrdinalIgnoreCase))
                wheel--;
            else
                keys.Add(ParseKey(token, lineNumber));
        }

        return new InputLine(frames, keys, buttons, cursor, wheel, lineNumber);
    }

    private static Key ParseKey(string token, int lineNumber)
    {
        // Enum.TryParse also accepts numbers and comma-separated flag lists; movies only allow plain names.
        var isPlainName = char.IsLetter(token[0]) && token.IndexOf(',') < 0;
        if (isPlainName && Enum.TryParse(token, ignoreCase: true, out Key key) && key != Key.None && Enum.IsDefined(typeof(Key), key))
            return key;

        throw new MovieFormatException(lineNumber,
            $"Unknown input '{token}'. Expected a key name (A, Space, D1...), LMB/RMB/MMB/XB1/XB2, WheelUp/WheelDown, or a cursor (@x,y or @tX,Y).");
    }

    private static CursorTarget ParseCursor(string token, int lineNumber)
    {
        var isTile = token.Length > 1 && char.ToLowerInvariant(token[1]) == 't';
        var parts = token.Substring(isTile ? 2 : 1).Split(',');
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x)
            && int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y))
        {
            return isTile ? CursorTarget.Tile(x, y) : CursorTarget.Screen(x, y);
        }

        throw new MovieFormatException(lineNumber, $"Invalid cursor '{token}'. Use @x,y for window pixels or @tX,Y for a tile.");
    }

    private static void ApplyHeader(MovieHeader header, string name, string value, int lineNumber)
    {
        switch (name.ToLowerInvariant())
        {
            case "terraria":
                header.TerrariaVersion = value;
                break;
            case "player":
                header.Player = value;
                break;
            case "world":
                header.World = value;
                break;
            case "seed":
                header.Seed = ParseInt(value, name, lineNumber);
                break;
            case "date":
                if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    throw new MovieFormatException(lineNumber, $"Date must be written yyyy-MM-dd, got '{value}'.");
                header.Date = date;
                break;
            case "resolution":
                var size = value.ToLowerInvariant().Split('x');
                if (size.Length != 2)
                    throw new MovieFormatException(lineNumber, $"Resolution must be written WIDTHxHEIGHT, got '{value}'.");
                header.ScreenWidth = ParseInt(size[0], name, lineNumber);
                header.ScreenHeight = ParseInt(size[1], name, lineNumber);
                break;
            default:
                throw new MovieFormatException(lineNumber,
                    $"Unknown header property '{name}'. Known properties: Terraria, Player, World, Seed, Date, Resolution.");
        }
    }

    private static int ParseInt(string value, string property, int lineNumber)
    {
        if (int.TryParse(value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result))
            return result;
        throw new MovieFormatException(lineNumber, $"{property} expects a whole number, got '{value}'.");
    }
}
