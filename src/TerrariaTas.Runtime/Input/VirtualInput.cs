using System;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using TerrariaTas.Core.Movies;
using XnaKeys = Microsoft.Xna.Framework.Input.Keys;

namespace TerrariaTas.Runtime.Input;

/// <summary>
/// Replaces XNA's keyboard, mouse and gamepad with virtual devices. Terraria reads input through
/// these three calls only, so menus, UI and gameplay all see the movie's input. When not overriding,
/// the real devices pass through untouched.
/// </summary>
internal sealed class VirtualInput
{
    private const int WheelUnitsPerNotch = 120;

    private static VirtualInput? _active;

    private bool _overriding;
    private KeyboardState _keyboard;
    private MouseState _mouse;
    private Point _cursor;
    private int _wheel;

    public void Install(Harmony harmony)
    {
        _active = this;
        harmony.CreateClassProcessor(typeof(Hooks)).Patch();
    }

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
    }

    /// <summary>Nothing pressed, cursor unchanged. Keeps stray real input out while the TAS sets things up.</summary>
    public void ApplyNeutral()
    {
        _keyboard = default;
        _mouse = new MouseState(_cursor.X, _cursor.Y, _wheel,
            ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        _overriding = true;
    }

    /// <summary>Lets the real keyboard, mouse and gamepad through.</summary>
    public void PassThrough() => _overriding = false;

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

    [HarmonyPatch]
    private static class Hooks
    {
        [HarmonyPatch(typeof(Keyboard), nameof(Keyboard.GetState), new Type[0]), HarmonyPrefix]
        private static bool KeyboardPrefix(ref KeyboardState __result) => Override(ref __result, _active!._keyboard);

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
