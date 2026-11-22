using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TerrariaTas.Core.Movies;

namespace TerrariaTas.Core.Hotkeys;

/// <summary>What a TAS hotkey does. The values travel to TAS Studio: never renumber them.</summary>
public enum HotkeyAction : byte
{
    ToggleRecording,
    PlayPause,
    FrameAdvance,
    FastForward,
    SaveState,
    LoadState,
    PreviousSlot,
    NextSlot,
    Restart,
    ReloadMovie,
    ToggleOverlay,
}

/// <summary>
/// The keys of the TAS hotkeys: F1 to F10 unless the TAS data folder's <c>hotkeys.txt</c> says otherwise. Keys are
/// named as in movies. The game never sees a key bound here while the TAS runs, so movies cannot press it.
/// </summary>
public sealed class HotkeyMap
{
    public const string FileName = "hotkeys.txt";

    // Grouped like the F-key row: recording on F1, playback on F2-F4, savestates on F5-F8, movie on F9-F10. F11 stays
    // Terraria's "hide UI" key. The overlay has no key until the user gives it one.
    private static readonly (HotkeyAction Action, Key Key, string Description)[] Actions =
    {
        (HotkeyAction.ToggleRecording, Key.F1, "record on/off"),
        (HotkeyAction.PlayPause, Key.F2, "play/pause"),
        (HotkeyAction.FrameAdvance, Key.F3, "frame advance (hold to repeat)"),
        (HotkeyAction.FastForward, Key.F4, "fast-forward (hold)"),
        (HotkeyAction.SaveState, Key.F5, "save state"),
        (HotkeyAction.LoadState, Key.F6, "load state"),
        (HotkeyAction.PreviousSlot, Key.F7, "previous slot"),
        (HotkeyAction.NextSlot, Key.F8, "next slot"),
        (HotkeyAction.Restart, Key.F9, "back to the movie start"),
        (HotkeyAction.ReloadMovie, Key.F10, "reload the movie"),
        (HotkeyAction.ToggleOverlay, Key.None, "frame, position and velocity over the game on/off"),
    };

    private readonly Dictionary<HotkeyAction, Key> _keys;

    public HotkeyMap(IReadOnlyDictionary<HotkeyAction, Key> keys)
    {
        _keys = Actions.Where(entry => keys.ContainsKey(entry.Action) && keys[entry.Action] != Key.None)
            .ToDictionary(entry => entry.Action, entry => keys[entry.Action]);
    }

    public static HotkeyMap Default { get; } = new(Actions.ToDictionary(entry => entry.Action, entry => entry.Key));

    /// <summary>Every bound action and its key, in the order of <see cref="HotkeyAction"/>.</summary>
    public IReadOnlyDictionary<HotkeyAction, Key> Keys => _keys;

    /// <summary>The keys the game must never see.</summary>
    public IEnumerable<Key> BoundKeys => _keys.Values;

    /// <summary>One line listing every hotkey: "F1 record on/off | F2 play/pause | ...".</summary>
    public string Help(string separator = " | ") =>
        string.Join(separator, Actions.Where(entry => _keys.ContainsKey(entry.Action)).Select(entry => $"{_keys[entry.Action]} {entry.Description}"));

    /// <summary>The key of <paramref name="action"/>, or null when it has none.</summary>
    public Key? KeyOf(HotkeyAction action) => _keys.TryGetValue(action, out var key) ? key : null;

    /// <summary>The action of <paramref name="key"/>, or null when it is no hotkey.</summary>
    public HotkeyAction? ActionOf(Key key)
    {
        foreach (var binding in _keys)
        {
            if (binding.Value == key)
                return binding.Key;
        }

        return null;
    }

    /// <summary>
    /// Reads a hotkeys file: "Action: Key" lines (nothing after the colon unbinds the action), comments after '#'.
    /// An action the file leaves out keeps its default key.
    /// </summary>
    /// <exception cref="MovieFormatException">A line is not understood, or two actions share a key.</exception>
    public static HotkeyMap Parse(string text)
    {
        var keys = Actions.ToDictionary(entry => entry.Action, entry => entry.Key);
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = (lines[i].IndexOf('#') is var hash and >= 0 ? lines[i].Substring(0, hash) : lines[i]).Trim();
            if (line.Length == 0)
                continue;

            var colon = line.IndexOf(':');
            var name = colon > 0 ? line.Substring(0, colon).Trim() : line;
            if (colon <= 0 || !Enum.TryParse<HotkeyAction>(name, ignoreCase: true, out var action) || !Enum.IsDefined(typeof(HotkeyAction), action) || char.IsDigit(name[0]))
                throw new MovieFormatException(i + 1, $"Write hotkeys as '<action>: <key>'. Actions: {string.Join(", ", Actions.Select(entry => entry.Action))}.");

            var value = line.Substring(colon + 1).Trim();
            if (value.Length == 0)
            {
                keys[action] = Key.None;
                continue;
            }

            if (!MovieParser.TryParseKey(value, out var key))
                throw new MovieFormatException(i + 1, $"{action}: '{value}' is not a key name (F1, Q, OemTilde... as in movies).");
            keys[action] = key;
        }

        var shared = keys.Where(binding => binding.Value != Key.None).GroupBy(binding => binding.Value).FirstOrDefault(group => group.Count() > 1);
        if (shared != null)
            throw new MovieFormatException(0, $"{shared.Key} is the key of both {string.Join(" and ", shared.Select(binding => binding.Key))}: give each action its own key.");
        return new HotkeyMap(keys);
    }

    /// <summary>A hotkeys file for this map, with the explanations a user needs to edit it.</summary>
    public string Write()
    {
        var text = new StringBuilder();
        text.Append("# Terraria TAS hotkeys: <action>: <key>, with keys named as in movies (F1, Q, OemTilde...).\n");
        text.Append("# Nothing after the colon unbinds an action. The game never sees these keys while the TAS runs,\n");
        text.Append("# so a movie cannot press them: keep them off the keys your movies use. Restart the TAS after editing.\n");
        foreach (var (action, _, description) in Actions)
            text.Append($"{action}: {(_keys.TryGetValue(action, out var key) ? key.ToString() : "")}".PadRight(24)).Append("# ").Append(description).Append('\n');
        return text.ToString();
    }
}
