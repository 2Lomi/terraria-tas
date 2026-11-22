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

    // A typo in a Repeat count must not make the TAS fill its memory with movie lines.
    private const int MaxRepeatedLines = 100_000;

    public static Movie Load(string path) => Parse(File.ReadAllText(path));

    public static Movie Parse(string text)
    {
        var header = new MovieHeader();
        var lines = new List<InputLine>();
        var breakpoints = new List<int>();
        var frame = 0;
        CursorTarget? cursor = null;
        RepeatBlock? block = null; // open until its EndRepeat, which plays its lines

        var rawLines = text.Split('\n');
        for (var i = 0; i < rawLines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = StripComment(rawLines[i]).Trim();
            if (line.Length == 0)
                continue;

            if (line == "***")
            {
                if (block != null)
                    throw new MovieFormatException(lineNumber, $"A breakpoint cannot be inside a Repeat block (line {block.Line}): put it before or after the block.");
                breakpoints.Add(frame);
                continue;
            }

            if (char.IsDigit(line[0]))
            {
                if (block != null)
                {
                    // The cursor of a line without one carries over from the line before it, which differs between rounds.
                    CursorTarget? ownCursor = null;
                    block.Lines.Add((ParseInput(line, lineNumber, block.Line, ref ownCursor), ownCursor));
                    continue;
                }

                var input = ParseInput(line, lineNumber, 0, ref cursor);
                lines.Add(input);
                frame += input.FrameCount;
                continue;
            }

            if (IsCommand(line, "Repeat", out var argument))
            {
                if (block != null)
                    throw new MovieFormatException(lineNumber, $"Repeat blocks cannot be nested: end the one of line {block.Line} with EndRepeat first.");
                block = new RepeatBlock(lineNumber, ParseRepeatCount(argument, lineNumber));
                continue;
            }

            if (IsCommand(line, "EndRepeat", out argument))
            {
                if (block == null || argument.Length > 0)
                    throw new MovieFormatException(lineNumber, block == null ? "EndRepeat without a Repeat before it." : "EndRepeat takes nothing after it.");
                frame += Play(block, lines, ref cursor);
                block = null;
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                if (lines.Count > 0 || block != null)
                    throw new MovieFormatException(lineNumber, "Header properties must come before the first input line.");
                ApplyHeader(header, line.Substring(0, colon).Trim(), line.Substring(colon + 1).Trim(), lineNumber);
                continue;
            }

            throw new MovieFormatException(lineNumber, $"Cannot understand '{line}'.");
        }

        if (block != null)
            throw new MovieFormatException(block.Line, "This Repeat block has no EndRepeat.");
        return new Movie(header, lines, breakpoints);
    }

    /// <summary>"Repeat" or "Repeat 5": true when <paramref name="line"/> starts with the command word.</summary>
    private static bool IsCommand(string line, string command, out string argument)
    {
        argument = "";
        if (!line.StartsWith(command, StringComparison.OrdinalIgnoreCase) || (line.Length > command.Length && !char.IsWhiteSpace(line[command.Length])))
            return false;

        argument = line.Substring(command.Length).Trim();
        return true;
    }

    private static int ParseRepeatCount(string argument, int lineNumber)
    {
        if (int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count >= 1)
            return count;
        throw new MovieFormatException(lineNumber, $"Write Repeat with the number of rounds, e.g. 'Repeat 5', got 'Repeat {argument}'.");
    }

    /// <summary>Adds the lines of every round of <paramref name="block"/>. Returns the frames they last.</summary>
    private static int Play(RepeatBlock block, List<InputLine> lines, ref CursorTarget? cursor)
    {
        if (block.Lines.Count == 0)
            throw new MovieFormatException(block.Line, "This Repeat block has no input line.");
        if ((long)block.Lines.Count * block.Rounds > MaxRepeatedLines)
            throw new MovieFormatException(block.Line, $"This Repeat block plays {(long)block.Lines.Count * block.Rounds} lines, more than the {MaxRepeatedLines} a block may.");

        var frames = 0;
        for (var round = 0; round < block.Rounds; round++)
        {
            foreach (var (line, ownCursor) in block.Lines)
            {
                cursor = ownCursor ?? cursor;
                lines.Add(new InputLine(line.FrameCount, line.Keys, line.Buttons, cursor, line.Wheel, line.SourceLine, block.Line));
                frames += line.FrameCount;
            }
        }

        return frames;
    }

    private static string StripComment(string line)
    {
        var hash = line.IndexOf('#');
        return hash < 0 ? line : line.Substring(0, hash);
    }

    private static InputLine ParseInput(string line, int lineNumber, int repeatLine, ref CursorTarget? cursor)
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

        return new InputLine(frames, keys, buttons, cursor, wheel, lineNumber, repeatLine);
    }

    private static Key ParseKey(string token, int lineNumber)
    {
        if (TryParseKey(token, out var key))
            return key;

        throw new MovieFormatException(lineNumber,
            $"Unknown input '{token}'. Expected a key name (A, Space, D1...), LMB/RMB/MMB/XB1/XB2, WheelUp/WheelDown, or a cursor (@x,y or @tX,Y).");
    }

    internal static bool TryParseKey(string token, out Key key)
    {
        // Enum.TryParse also accepts numbers and comma-separated flag lists; movies only allow plain names.
        key = Key.None;
        var isPlainName = token.Length > 0 && char.IsLetter(token[0]) && token.IndexOf(',') < 0;
        return isPlainName && Enum.TryParse(token, ignoreCase: true, out key) && key != Key.None && Enum.IsDefined(typeof(Key), key);
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
            case "playerchecksum":
                header.PlayerChecksum = ParseChecksum(value, "PlayerChecksum", lineNumber);
                break;
            case "world":
                header.World = value;
                break;
            case "worldchecksum":
                header.WorldChecksum = ParseChecksum(value, "WorldChecksum", lineNumber);
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
                if (!IsPlayableSize(header.ScreenWidth, MovieHeader.MinScreenWidth, MovieHeader.MaxScreenWidth)
                    || !IsPlayableSize(header.ScreenHeight, MovieHeader.MinScreenHeight, MovieHeader.MaxScreenHeight))
                {
                    throw new MovieFormatException(lineNumber,
                        $"Resolution must be in even numbers from {MovieHeader.MinScreenWidth}x{MovieHeader.MinScreenHeight} to "
                        + $"{MovieHeader.MaxScreenWidth}x{MovieHeader.MaxScreenHeight} (Terraria changes any other size), got '{value}'.");
                }

                break;
            default:
                var words = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (words[0].Equals("Bind", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyBinding(header, words, value, lineNumber);
                    break;
                }

                if (GameSettings.Find(name) is not { } setting)
                {
                    throw new MovieFormatException(lineNumber,
                        $"Unknown header property '{name}'. Known properties: Terraria, Player, PlayerChecksum, World, WorldChecksum, Seed, Date, Resolution, "
                        + string.Join(", ", GameSettings.All.Select(s => s.Name)) + ", and Bind <action> (key bindings).");
                }

                header.Settings[setting.Name] = setting.Normalize(value)
                    ?? throw new MovieFormatException(lineNumber, $"{setting.Name} takes {setting.AllowedValues}, got '{value}'.");
                break;
        }
    }

    /// <summary>"Bind Jump: Space W": the keys and mouse buttons of an action; nothing after the colon unbinds it.</summary>
    private static void ApplyBinding(MovieHeader header, string[] words, string value, int lineNumber)
    {
        if (words.Length != 2)
            throw new MovieFormatException(lineNumber, "Write key bindings as 'Bind <action>: <keys>', e.g. 'Bind Jump: Space'.");
        if (KeyBindings.Find(words[1]) is not { } binding)
        {
            throw new MovieFormatException(lineNumber,
                $"Unknown action '{words[1]}'. Actions: {string.Join(", ", KeyBindings.All.Select(b => b.Name))}.");
        }

        var inputs = new List<string>();
        foreach (var token in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var input = KeyBindings.NormalizeInput(token)
                ?? throw new MovieFormatException(lineNumber, $"Bind {binding.Name}: '{token}' is not a key name (A, Space, D1...) or LMB/RMB/MMB/XB1/XB2.");
            if (!inputs.Contains(input))
                inputs.Add(input);
        }

        header.Bindings[binding.Name] = inputs;
    }

    private static bool IsPlayableSize(int pixels, int min, int max) => pixels >= min && pixels <= max && pixels % 2 == 0;

    private static string ParseChecksum(string value, string property, int lineNumber) =>
        FileChecksum.Normalize(value)
        ?? throw new MovieFormatException(lineNumber, $"{property} must be the {FileChecksum.Length} hexadecimal digits of a SHA-256, got '{value}'.");

    private static int ParseInt(string value, string property, int lineNumber)
    {
        if (int.TryParse(value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result))
            return result;
        throw new MovieFormatException(lineNumber, $"{property} expects a whole number, got '{value}'.");
    }

    /// <summary>A Repeat block being read: its lines, each with the cursor it sets itself (null to keep the one before).</summary>
    private sealed class RepeatBlock
    {
        public RepeatBlock(int line, int rounds)
        {
            Line = line;
            Rounds = rounds;
        }

        /// <summary>File line of the Repeat command.</summary>
        public int Line { get; }

        public int Rounds { get; }

        public List<(InputLine Line, CursorTarget? OwnCursor)> Lines { get; } = new();
    }
}
