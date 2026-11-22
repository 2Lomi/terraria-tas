using System.Linq;
using System.Windows.Forms;
using TerrariaTas.Core.Hotkeys;
using TerrariaTas.Core.Movies;
using TerrariaTas.Core.Studio;
using TerrariaTas.Studio.Connection;

namespace TerrariaTas.Studio.Tools;

/// <summary>
/// The TAS hotkeys, so that they work in Studio windows too: the game only reads them while its own window has
/// focus. The keys are the game's (its hotkeys.txt, received on connection; F1 to F10 until then). Buttons that do
/// what a hotkey does show its key (<see cref="Label"/>).
/// </summary>
internal static class Hotkeys
{
    private static readonly (HotkeyAction Action, CommandKind Command)[] Commands =
    {
        (HotkeyAction.ToggleRecording, CommandKind.ToggleRecording),
        (HotkeyAction.PlayPause, CommandKind.TogglePause),
        (HotkeyAction.FrameAdvance, CommandKind.FrameAdvance),
        (HotkeyAction.FastForward, CommandKind.FastForwardOn), // its release stops fast-forward
        (HotkeyAction.SaveState, CommandKind.SaveSlot),
        (HotkeyAction.LoadState, CommandKind.LoadSlot),
        (HotkeyAction.PreviousSlot, CommandKind.PreviousSlot),
        (HotkeyAction.NextSlot, CommandKind.NextSlot),
        (HotkeyAction.Restart, CommandKind.Restart),
        (HotkeyAction.ReloadMovie, CommandKind.ReloadMovie),
        (HotkeyAction.ToggleOverlay, CommandKind.ToggleOverlay),
    };

    public static HotkeyMap Map { get; private set; } = HotkeyMap.Default;

    public static string Help => Map.Help(" · ");

    /// <summary>Takes the game's hotkeys.</summary>
    public static void Use(HotkeyMap map) => Map = map;

    /// <summary>The key of <paramref name="action"/> as Windows Forms names it, or null.</summary>
    public static Keys? KeyOf(HotkeyAction action) => Map.KeyOf(action) is { } key ? (Keys)(int)key : null;

    /// <summary><paramref name="text"/> followed by the key of <paramref name="command"/>: "Play / pause (F2)".</summary>
    public static string Label(string text, CommandKind command)
    {
        var action = Commands.First(entry => entry.Command == command).Action;
        return Map.KeyOf(action) is { } key ? $"{text} ({key})" : text;
    }

    /// <summary>Runs <paramref name="command"/> as its hotkey does: saving and loading act on the current slot.</summary>
    public static void Run(StudioState state, CommandKind command) =>
        state.Run(command, command is CommandKind.SaveSlot or CommandKind.LoadSlot ? state.Status?.Slot ?? 1 : 0);

    /// <returns>True when the key is a TAS hotkey.</returns>
    public static bool Press(StudioState state, Keys key, bool repeat)
    {
        if (ActionOf(key) is not { } action)
            return false;

        // Holding a key repeats only frame advance; the others act once per press.
        if (!repeat || action == HotkeyAction.FrameAdvance)
            Run(state, Commands.First(entry => entry.Action == action).Command);
        return true;
    }

    /// <returns>True when the key is a TAS hotkey.</returns>
    public static bool Release(StudioState state, Keys key)
    {
        var action = ActionOf(key);
        if (action == HotkeyAction.FastForward)
            state.Run(CommandKind.FastForwardOff);
        return action != null;
    }

    private static HotkeyAction? ActionOf(Keys key) => key is > Keys.None and <= (Keys)255 ? Map.ActionOf((Key)(int)key) : null;
}
