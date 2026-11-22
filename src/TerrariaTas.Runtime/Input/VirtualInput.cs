using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using TerrariaTas.Core.Movies;
using XnaKeys = Microsoft.Xna.Framework.Input.Keys;

namespace TerrariaTas.Runtime.Input;

/// <summary>
/// Replaces XNA's keyboard, mouse and gamepad with virtual devices. Terraria reads input through
/// these three calls only, so menus, UI and gameplay all see the movie's input. When not overriding,
/// the real devices pass through, minus the keys hidden from the game.
/// </summary>
internal sealed class VirtualInput
{
    private const int WheelUnitsPerNotch = 120;

    // Real keys a movie can hold, by XNA key code.
    private static readonly bool[] MovieKeys = Enum.GetValues(typeof(Key)).Cast<Key>()
        .Where(key => key != Key.None)
        .Aggregate(new bool[256], (keys, key) =>
        {
            keys[(int)key] = true;
            return keys;
        });

    private static VirtualInput? _active;

    private bool _overriding;
    private KeyboardState _keyboard;
    private MouseState _mouse;
    private Point _cursor;
    private int _wheel;
    private XnaKeys[] _hiddenKeys = Array.Empty<XnaKeys>();
    private int? _realWheel;
    private int _realWheelRemainder;

    public void Install(Harmony harmony)
    {
        _active = this;
        harmony.CreateClassProcessor(typeof(Hooks)).Patch();
    }

    /// <summary>
    /// Real keys the game never sees, so that a TAS hotkey does not also act in the game
    /// (Terraria toggles overlays, FPS display and lighting mode on F7-F10).
    /// </summary>
    public void HideFromGame(IEnumerable<Key> keys) => _hiddenKeys = keys.Select(k => (XnaKeys)k).ToArray();

    /// <summary>Uses a movie line as the input of the next frame.</summary>
    public void Apply(InputLine line, Point screenSize)
    {
        if (line.Cursor is { } cursor)
            _cursor = CursorMapper.ToScreen(cursor, screenSize);
        _wheel += line.Wheel * WheelUnitsPerNotch;

        _keyboard = new KeyboardState(line.Keys.Select(k => (XnaKeys)k).ToArray());
        _mouse = new MouseState(_cursor.X, _cursor.Y, _wheel,
            Button(line, MouseButtons.Left), Button(line, MouseButtons.Middle), Button(line, MouseButtons.Right),
            Button(line, MouseButtons.X1), Button(line, MouseButtons.X2));
        _overriding = true;
        DropTypedCharacters();
    }

    /// <summary>Nothing pressed, cursor unchanged. Keeps stray real input out while the TAS sets things up.</summary>
    public void ApplyNeutral()
    {
        _keyboard = default;
        _mouse = new MouseState(_cursor.X, _cursor.Y, _wheel,
            ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        _overriding = true;
        DropTypedCharacters();
    }

    /// <summary>
    /// Characters typed on the real keyboard reach chat, signs and name fields through window messages, apart from
    /// the keyboard state above, and movies cannot hold them yet: they would play while recording and not on replay.
    /// </summary>
    private static void DropTypedCharacters() => Main.keyCount = 0;

    /// <summary>Lets the real keyboard, mouse and gamepad through.</summary>
    public void PassThrough() => _overriding = false;

    /// <summary>
    /// What the real keyboard and mouse do right now, as one movie frame: the keys a movie can hold (TAS hotkeys
    /// excluded), the buttons, the wheel notches turned since the last read and the cursor in window pixels. While
    /// another window has focus, nothing is pressed and the cursor stays put: input meant for other windows is
    /// never recorded.
    /// </summary>
    public InputLine ReadRealDevices(IntPtr gameWindow) => ReadRealDevices(gameWindow, takeWheel: true);

    /// <summary>What <see cref="ReadRealDevices(IntPtr)"/> would give right now, leaving the wheel turns for it.</summary>
    public InputLine PeekRealDevices(IntPtr gameWindow) => ReadRealDevices(gameWindow, takeWheel: false);

    /// <summary>
    /// Shows Windows' own cursor over the game window. While paused the game draws nothing, so this is the only way
    /// to see where the mouse is, which is where the next recorded frame aims.
    /// </summary>
    public void ShowRealCursor(bool visible)
    {
        if (Main.instance.IsMouseVisible != visible)
            Main.instance.IsMouseVisible = visible;
    }

    private InputLine ReadRealDevices(IntPtr gameWindow, bool takeWheel)
    {
        var keyboard = ReadReal(() => Keyboard.GetState()); // through the hooks, which take the hidden keys out
        var mouse = ReadReal(() => Mouse.GetState());
        var notches = takeWheel ? WheelNotches(mouse.ScrollWheelValue) : PendingNotches(mouse.ScrollWheelValue);
        if (GetForegroundWindow() != gameWindow)
            return new InputLine(1, Array.Empty<Key>(), MouseButtons.None, CursorTarget.Screen(_cursor.X, _cursor.Y), 0);

        var keys = keyboard.GetPressedKeys().Where(key => (int)key < MovieKeys.Length && MovieKeys[(int)key]).Select(key => (Key)(int)key);
        var buttons = (mouse.LeftButton == ButtonState.Pressed ? MouseButtons.Left : MouseButtons.None)
                      | (mouse.RightButton == ButtonState.Pressed ? MouseButtons.Right : MouseButtons.None)
                      | (mouse.MiddleButton == ButtonState.Pressed ? MouseButtons.Middle : MouseButtons.None)
                      | (mouse.XButton1 == ButtonState.Pressed ? MouseButtons.X1 : MouseButtons.None)
                      | (mouse.XButton2 == ButtonState.Pressed ? MouseButtons.X2 : MouseButtons.None);
        return new InputLine(1, keys, buttons, CursorTarget.Screen(mouse.X, mouse.Y), notches);
    }

    /// <summary>
    /// Drops the wheel turns not read yet. Call when a frame runs without reading the real devices, so that scrolling
    /// during playback does not land on the next recorded frame. (Scrolling while paused does: that is how a frame
    /// advance gets a wheel notch.)
    /// </summary>
    public void ForgetRealWheel()
    {
        _realWheel = ReadReal(() => Mouse.GetState()).ScrollWheelValue;
        _realWheelRemainder = 0;
    }

    /// <summary>
    /// Puts the cursor at <paramref name="position"/> (window pixels), for the game too: Terraria only updates its
    /// cursor position when the mouse moves, so it would otherwise keep whatever it picked up while loading.
    /// </summary>
    public void PlaceCursor(Point position)
    {
        _cursor = position;
        _mouse = new MouseState(position.X, position.Y, _wheel,
            ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        PlayerInput.MouseInfo = PlayerInput.MouseInfoOld = _mouse;
        PlayerInput.MouseX = Main.mouseX = Main.lastMouseX = position.X;
        PlayerInput.MouseY = Main.mouseY = Main.lastMouseY = position.Y;
    }

    /// <summary>Savestate support: the cursor and wheel position persist across frames.</summary>
    public object CaptureState() => Tuple.Create(_cursor, _wheel);

    public void RestoreState(object? state)
    {
        var saved = (Tuple<Point, int>)state!;
        _cursor = saved.Item1;
        _wheel = saved.Item2;
    }

    private static ButtonState Button(InputLine line, MouseButtons button) =>
        (line.Buttons & button) != 0 ? ButtonState.Pressed : ButtonState.Released;

    /// <summary>Reads a real device, whatever the movie feeds the game.</summary>
    private T ReadReal<T>(Func<T> read)
    {
        var overriding = _overriding;
        _overriding = false;
        try
        {
            return read();
        }
        finally
        {
            _overriding = overriding;
        }
    }

    private int PendingNotches(int scrollWheelValue) =>
        _realWheel is { } last ? (scrollWheelValue - last + _realWheelRemainder) / WheelUnitsPerNotch : 0;

    /// <summary>Whole notches turned since the last read; partial turns (touchpads) carry over.</summary>
    private int WheelNotches(int scrollWheelValue)
    {
        if (_realWheel is not { } last)
        {
            _realWheel = scrollWheelValue;
            return 0;
        }

        var units = scrollWheelValue - last + _realWheelRemainder;
        _realWheel = scrollWheelValue;
        var notches = units / WheelUnitsPerNotch;
        _realWheelRemainder = units - notches * WheelUnitsPerNotch;
        return notches;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private KeyboardState WithoutHiddenKeys(KeyboardState real) =>
        _hiddenKeys.Any(real.IsKeyDown) ? new KeyboardState(real.GetPressedKeys().Except(_hiddenKeys).ToArray()) : real;

    [HarmonyPatch]
    private static class Hooks
    {
        [HarmonyPatch(typeof(Keyboard), nameof(Keyboard.GetState), new Type[0]), HarmonyPrefix]
        private static bool KeyboardPrefix(ref KeyboardState __result) => Override(ref __result, _active!._keyboard);

        [HarmonyPatch(typeof(Keyboard), nameof(Keyboard.GetState), new Type[0]), HarmonyPostfix]
        private static void KeyboardPostfix(ref KeyboardState __result)
        {
            if (_active is { _overriding: false } input)
                __result = input.WithoutHiddenKeys(__result);
        }

        [HarmonyPatch(typeof(Mouse), nameof(Mouse.GetState), new Type[0]), HarmonyPrefix]
        private static bool MousePrefix(ref MouseState __result) => Override(ref __result, _active!._mouse);

        [HarmonyPatch(typeof(GamePad), nameof(GamePad.GetState), typeof(PlayerIndex)), HarmonyPrefix]
        private static bool GamePadPrefix(ref GamePadState __result) => Override(ref __result, default);

        [HarmonyPatch(typeof(GamePad), nameof(GamePad.GetState), typeof(PlayerIndex), typeof(GamePadDeadZone)), HarmonyPrefix]
        private static bool GamePadDeadZonePrefix(ref GamePadState __result) => Override(ref __result, default);

        private static bool Override<T>(ref T result, T value)
        {
            if (_active is not { _overriding: true })
                return true;

            result = value;
            return false;
        }
    }
}
