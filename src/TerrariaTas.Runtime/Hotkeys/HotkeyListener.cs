using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using TerrariaTas.Core.Hotkeys;
using TerrariaTas.Core.Movies;

namespace TerrariaTas.Runtime.Hotkeys;

/// <summary>
/// Reads TAS hotkeys straight from the physical keyboard, independently of the virtual input the game sees.
/// Active only while the game window or its console has focus. The keys come from the data folder's hotkeys.txt.
/// </summary>
internal sealed class HotkeyListener
{
    private readonly HotkeyMap _map;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<HotkeyAction, TimeSpan> _heldSince = new();
    private readonly HashSet<HotkeyAction> _pressed = new();
    private readonly HashSet<HotkeyAction> _released = new();

    public HotkeyListener(HotkeyMap map)
    {
        _map = map;
    }

    public HotkeyMap Map => _map;

    /// <summary>
    /// The hotkeys of <paramref name="dataDir"/>'s hotkeys.txt, which is created with the default keys when missing so
    /// that there is a file to edit.
    /// </summary>
    /// <exception cref="TasSetupException">The file has a mistake: the TAS would not react to the keys its user expects.</exception>
    public static HotkeyMap Load(string dataDir, out string path)
    {
        path = Path.Combine(dataDir, HotkeyMap.FileName);
        if (!File.Exists(path))
        {
            try
            {
                File.WriteAllText(path, HotkeyMap.Default.Write());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Could not create {path} ({e.Message}): the default hotkeys apply.");
            }

            return HotkeyMap.Default;
        }

        try
        {
            return HotkeyMap.Parse(File.ReadAllText(path));
        }
        catch (MovieFormatException e)
        {
            throw new TasSetupException($"{path}{(e.LineNumber > 0 ? $", line {e.LineNumber}" : "")}: {e.Reason} Fix the file, or delete it to get the default hotkeys back.");
        }
    }

    public void Poll(IntPtr gameWindow)
    {
        _pressed.Clear();
        _released.Clear();
        var foreground = GetForegroundWindow();
        var focused = foreground == gameWindow || foreground == GetConsoleWindow();

        foreach (var binding in _map.Keys)
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
