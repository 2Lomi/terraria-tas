using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using TerrariaTas.Core.Movies;

namespace TerrariaTas.Runtime.Hotkeys;

internal enum HotkeyAction
{
    PlayPause,
    FrameAdvance,
    FastForward,
    SaveState,
    LoadState,
    ReloadMovie,
    PreviousSlot,
    NextSlot,
    Restart,
}

/// <summary>
/// Reads TAS hotkeys straight from the physical keyboard, independently of the virtual input the game sees.
/// Active only while the game window or its console has focus.
/// </summary>
internal sealed class HotkeyListener
{
    private static readonly Dictionary<HotkeyAction, Key> Bindings = new()
    {
        [HotkeyAction.PlayPause] = Key.NumPad0,
        [HotkeyAction.FrameAdvance] = Key.NumPad1,
        [HotkeyAction.FastForward] = Key.NumPad2,
        [HotkeyAction.SaveState] = Key.NumPad4,
        [HotkeyAction.LoadState] = Key.NumPad5,
        [HotkeyAction.ReloadMovie] = Key.NumPad6,
        [HotkeyAction.PreviousSlot] = Key.NumPad7,
        [HotkeyAction.NextSlot] = Key.NumPad8,
        [HotkeyAction.Restart] = Key.NumPad9,
    };

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<HotkeyAction, TimeSpan> _heldSince = new();
    private readonly HashSet<HotkeyAction> _pressed = new();
    private readonly HashSet<HotkeyAction> _released = new();

    public static string Help =>
        "NumPad0 play/pause | NumPad1 frame advance (hold to repeat) | NumPad2 fast-forward (hold) | "
        + "NumPad4 save state | NumPad5 load state | NumPad7/8 previous/next slot | NumPad9 restart | NumPad6 reload movie";

    public void Poll(IntPtr gameWindow)
    {
        _pressed.Clear();
        _released.Clear();
        var foreground = GetForegroundWindow();
        var focused = foreground == gameWindow || foreground == GetConsoleWindow();

        foreach (var binding in Bindings)
        {
            var down = focused && (GetAsyncKeyState((int)binding.Value) & 0x8000) != 0;
            var wasDown = _heldSince.ContainsKey(binding.Key);
            if (down && !wasDown)
            {
                _heldSince[binding.Key] = _clock.Elapsed;
                _pressed.Add(binding.Key);
            }
            else if (!down && wasDown)
            {
                _heldSince.Remove(binding.Key);
                _released.Add(binding.Key);
            }
        }
    }

    public bool WasPressed(HotkeyAction action) => _pressed.Contains(action);

    public bool WasReleased(HotkeyAction action) => _released.Contains(action);

    public TimeSpan HeldFor(HotkeyAction action) =>
        _heldSince.TryGetValue(action, out var since) ? _clock.Elapsed - since : TimeSpan.Zero;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
}
