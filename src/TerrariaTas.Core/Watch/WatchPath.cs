using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TerrariaTas.Core.Watch;

/// <summary>One step of a watch path: a member name, or the indices of an array or list element.</summary>
public sealed class WatchStep
{
    private WatchStep(string? member, bool isCall, int[]? indices)
    {
        Member = member;
        IsCall = isCall;
        Indices = indices;
    }

    /// <summary>A field, or with <see cref="IsCall"/> a property; null for an index step.</summary>
    public string? Member { get; }

    /// <summary>Written "Name()": read the property even though its getter runs game code.</summary>
    public bool IsCall { get; }

    public int[]? Indices { get; }

    public static WatchStep Field(string name) => new(name, isCall: false, null);

    public static WatchStep Property(string name) => new(name, isCall: true, null);

    public static WatchStep Index(params int[] indices) => new(null, isCall: false, indices);

    public override string ToString() => Member != null ? Member + (IsCall ? "()" : "") : "[" + string.Join(",", Indices!) + "]";
}

/// <summary>
/// A path to a value of the game: <c>Main.player[0].velocity.X</c>, <c>Main.windSpeedCurrent</c>,
/// <c>player.position</c>. It starts with a type (its short name, or its full name) or an alias, then names fields
/// and indexes arrays. A property is written <c>Name()</c>: its getter runs game code, which a field read never does.
/// </summary>
public sealed class WatchPath
{
    private WatchPath(string text, IReadOnlyList<WatchStep> steps)
    {
        Text = text;
        Steps = steps;
    }

    public string Text { get; }

    /// <summary>Every step, the type or alias included (as leading member steps).</summary>
    public IReadOnlyList<WatchStep> Steps { get; }

    /// <exception cref="FormatException">The text is not a path; the message says where.</exception>
    public static WatchPath Parse(string text)
    {
        var trimmed = text.Trim();
        var steps = new List<WatchStep>();
        var position = 0;
        var expectName = true;
        while (position < trimmed.Length)
        {
            var c = trimmed[position];
            if (expectName)
            {
                var start = position;
                while (position < trimmed.Length && IsNameChar(trimmed[position], position == start))
                    position++;
                if (position == start)
                    throw new FormatException($"Expected a name at position {start + 1} of '{trimmed}'.");

                var name = trimmed.Substring(start, position - start);
                var isCall = position + 1 < trimmed.Length && trimmed[position] == '(' && trimmed[position + 1] == ')';
                if (isCall)
                    position += 2;
                steps.Add(isCall ? WatchStep.Property(name) : WatchStep.Field(name));
                expectName = false;
            }
            else if (c == '.')
            {
                position++;
                expectName = true;
            }
            else if (c == '[')
            {
                var end = trimmed.IndexOf(']', position);
                if (end < 0)
                    throw new FormatException($"'[' at position {position + 1} of '{trimmed}' has no ']'.");
                steps.Add(WatchStep.Index(ParseIndices(trimmed.Substring(position + 1, end - position - 1), trimmed)));
                position = end + 1;
            }
            else
            {
                throw new FormatException($"Unexpected '{c}' at position {position + 1} of '{trimmed}'.");
            }
        }

        if (steps.Count == 0 || expectName)
            throw new FormatException(steps.Count == 0 ? "Write a path, e.g. Main.player[0].velocity.X." : $"'{trimmed}' ends with a dot.");
        if (steps[0].Member == null)
            throw new FormatException($"'{trimmed}' must start with a type or 'player', not an index.");
        return new WatchPath(trimmed, steps);
    }

    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var step in Steps)
        {
            if (step.Member != null && text.Length > 0)
                text.Append('.');
            text.Append(step);
        }

        return text.ToString();
    }

    private static int[] ParseIndices(string text, string path)
    {
        var parts = text.Split(',');
        var indices = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out indices[i]))
                throw new FormatException($"'[{text}]' in '{path}' is not an index: write whole numbers, e.g. [0] or [10,4].");
        }

        return indices;
    }

    // Compiler-generated names (backing fields) contain '<' and '>'.
    private static bool IsNameChar(char c, bool first) => char.IsLetter(c) || c == '_' || c == '<' || c == '>' || (!first && char.IsDigit(c));
}
