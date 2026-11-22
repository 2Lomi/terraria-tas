using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TerrariaTas.Core.Movies;

/// <summary>
/// A game setting that changes the simulation, so the movie fixes it: a header property, the values it allows and
/// the value a movie gets when its header does not state it.
/// </summary>
public sealed class GameSetting
{
    /// <summary>The value of a setting whose fresh-install value the game computes (see <see cref="Fraction"/>).</summary>
    public const string Auto = "auto";

    private GameSetting(string name, string defaultValue, IReadOnlyList<string>? choices, double min, double max, bool isFraction, string meaning)
    {
        Name = name;
        Default = defaultValue;
        Choices = choices;
        Min = min;
        Max = max;
        IsFraction = isFraction;
        Meaning = meaning;
    }

    /// <summary>Header property, e.g. "Zoom".</summary>
    public string Name { get; }

    public string Default { get; }

    /// <summary>The allowed values of a named setting, or null for a number.</summary>
    public IReadOnlyList<string>? Choices { get; }

    public double Min { get; }

    public double Max { get; }

    /// <summary>A number the game keeps as a float: written exactly as the game holds it, like its configuration file does.</summary>
    public bool IsFraction { get; }

    /// <summary>What it changes, in a few words.</summary>
    public string Meaning { get; }

    public string AllowedValues =>
        Choices != null ? string.Join(", ", Choices)
        : $"{Bound(Min)} to {Bound(Max)}" + (Default == Auto ? " or auto" : "");

    public static GameSetting Switch(string name, bool defaultOn, string meaning) =>
        new(name, defaultOn ? "on" : "off", new[] { "on", "off" }, 0, 0, isFraction: false, meaning);

    public static GameSetting Choice(string name, string defaultValue, string[] choices, string meaning) =>
        new(name, defaultValue, choices, 0, 0, isFraction: false, meaning);

    public static GameSetting Number(string name, int defaultValue, int min, int max, string meaning) =>
        new(name, defaultValue.ToString(CultureInfo.InvariantCulture), null, min, max, isFraction: false, meaning);

    /// <param name="defaultValue">A number, or <see cref="Auto"/> when a fresh install computes it (e.g. from the window size).</param>
    public static GameSetting Fraction(string name, string defaultValue, float min, float max, string meaning) =>
        new(name, defaultValue, null, min, max, isFraction: true, meaning);

    /// <summary>A float as movies write it: nine significant digits, which always read back as exactly the same float.</summary>
    public static string Format(float value) => value.ToString("G9", CultureInfo.InvariantCulture);

    /// <summary>The value as movies write it, or null when <paramref name="value"/> is not allowed.</summary>
    public string? Normalize(string value)
    {
        value = value.Trim();
        if (Choices != null)
            return Choices.FirstOrDefault(choice => choice.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (Default == Auto && value.Equals(Auto, StringComparison.OrdinalIgnoreCase))
            return Auto;

        if (IsFraction)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fraction) && fraction >= Min && fraction <= Max
                ? Format(fraction)
                : null;
        }

        return int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) && number >= Min && number <= Max
            ? number.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string Bound(double bound) => bound.ToString(CultureInfo.InvariantCulture);
}
