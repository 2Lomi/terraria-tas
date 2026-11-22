using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameContent.UI.Minimap;
using Terraria.GameInput;
using Terraria.UI;
using TerrariaTas.Core.Movies;
using XnaKeys = Microsoft.Xna.Framework.Input.Keys;

namespace TerrariaTas.Runtime.Determinism;

/// <summary>Where one movie setting (<see cref="TerrariaTas.Core.Movies.GameSettings"/>) lives in the game.</summary>
internal sealed class SettingBinding
{
    private static readonly FieldInfo MinimapFrameKey = AccessTools.Field(typeof(SelectionHolder<MinimapFrame>), "ActiveSelectionConfigKey");
    private static readonly MethodInfo SetMinimapFrame = AccessTools.Method(typeof(SelectionHolder<MinimapFrame>), "SetActiveFrame", new[] { typeof(string) });

    private readonly Func<string> _read;
    private readonly Action<string> _apply;
    private readonly Func<string>? _auto;

    private SettingBinding(string name, Func<string> read, Action<string> apply, Func<string>? auto = null)
    {
        Name = name;
        _read = read;
        _apply = apply;
        _auto = auto;
    }

    public static IReadOnlyList<SettingBinding> All { get; } = new[]
    {
        Fraction("Zoom", () => Main.GameZoomTarget, value => Main.GameZoomTarget = value),

        // A fresh install picks the interface size from the window height (Main.TryPickingDefaultUIScale).
        Fraction("UIScale", () => Main.UIScaleWanted, value => Main.UIScale = value, () => Main.screenHeight / 1080f * 1.1f),
        Switch("Background", () => Main.BackgroundEnabled, on => Main.BackgroundEnabled = on),
        Switch("FlashyEffects", () => Main.FlashyEffectsWorld, on => Main.FlashyEffectsWorld = on),
        Switch("SwayInWind", () => Main.SettingsEnabled_TilesSwayInWind, on => Main.SettingsEnabled_TilesSwayInWind = on),
        Switch("BloodAndGore", () => ChildSafety.Disabled, on => ChildSafety.Disabled = on),
        Switch("PickupText", () => Main.showItemText, on => Main.showItemText = on),
        Number("WaterfallLimit", () => Main.instance.waterfallManager.maxWaterfallCount, value => Main.instance.waterfallManager.maxWaterfallCount = value),
        Switch("ScreenShake", () => Main.UseScreenShake, on => Main.UseScreenShake = on),
        Switch("StormEffects", () => Main.UseStormEffects, on => Main.UseStormEffects = on),
        Switch("Map", () => Main.mapEnabled, on => Main.mapEnabled = on),
        Fraction("MapScale", () => Main.MapScale, value => Main.MapScale = value),
        new SettingBinding("MinimapFrame",
            () => (string)MinimapFrameKey.GetValue(Main.MinimapFrameManagerInstance),
            name => SetMinimapFrame.Invoke(Main.MinimapFrameManagerInstance, new object[] { name })),
        Choice("SmartCursor", () => Main.cSmartCursorModeIsToggleAndNotHold, toggle => Main.cSmartCursorModeIsToggleAndNotHold = toggle, (true, "toggle"), (false, "hold")),
        Switch("SmartCursorReleaseMidUse", () => Player.SmartCursorSettings.SmartCursorHoldCanReleaseMidUse, on => Player.SmartCursorSettings.SmartCursorHoldCanReleaseMidUse = on),
        Switch("SmartCursorBlocks", () => Player.SmartCursorSettings.SmartBlocksEnabled, on => Player.SmartCursorSettings.SmartBlocksEnabled = on),
        Switch("SmartAxeAfterPickaxe", () => Player.SmartCursorSettings.SmartAxeAfterPickaxe, on => Player.SmartCursorSettings.SmartAxeAfterPickaxe = on),
        Switch("ShiftClickTrash", () => !ItemSlot.Options.DisableLeftShiftTrashCan, on => ItemSlot.Options.DisableLeftShiftTrashCan = !on),
        Switch("QuickTrash", () => !ItemSlot.Options.DisableQuickTrash, on => ItemSlot.Options.DisableQuickTrash = !on),
        Choice("ArmorSetBonusKey", () => Main.ReversedUpDownArmorSetBonuses, up => Main.ReversedUpDownArmorSetBonuses = up, (false, "down"), (true, "up")),
        new SettingBinding("FavoriteKey", () => Main.FavoriteKey.ToString(), name =>
        {
            Main.cFavoriteKey = name;
            Main.FavoriteKey = (XnaKeys)Enum.Parse(typeof(XnaKeys), name);
        }),
        Switch("Autoswing", () => Main.SettingsEnabled_AutoReuseAllItems, on => Main.SettingsEnabled_AutoReuseAllItems = on),
        Choice("AutoDoors", () => DoorOpeningHelper.PreferenceSettings, mode => DoorOpeningHelper.PreferenceSettings = mode,
            (DoorOpeningHelper.DoorAutoOpeningPreference.Disabled, "off"),
            (DoorOpeningHelper.DoorAutoOpeningPreference.EnabledForGamepadOnly, "gamepad"),
            (DoorOpeningHelper.DoorAutoOpeningPreference.EnabledForEverything, "everything")),
        Choice("HoverControl", () => Player.Settings.HoverControl, mode => Player.Settings.HoverControl = mode,
            (ButtonControlMode.Hold, "hold"), (ButtonControlMode.Click, "click")),
        Choice("DoubleTapDash", () => Player.Settings.DashControl, mode => Player.Settings.DashControl = mode,
            (Player.Settings.DashPreference.AllowDoubleTap, "on"), (Player.Settings.DashPreference.OnlyThroughHotkeys, "off")),
        Choice("Crafting", () => Player.Settings.CraftingGridControl, mode => Player.Settings.CraftingGridControl = mode,
            (Player.Settings.CraftingGridMode.Modern, "modern"), (Player.Settings.CraftingGridMode.Classic, "classic")),
        Choice("QuickStack", () => Player.Settings.StackToChestsPreferredMode, mode => Player.Settings.StackToChestsPreferredMode = mode,
            (Player.Settings.StackToNearbyChestsMode.QuickStackToNearbyChests, "quick"), (Player.Settings.StackToNearbyChestsMode.SmartStackToNearbyChests, "smart")),
        Switch("CraftFromChests", () => Player.Settings.CraftFromNearbyChests, on => Player.Settings.CraftFromNearbyChests = on),

        // Held by the control profile, like the key bindings: savestates keep it with them (KeyBindingPins).
        Choice("ForwardZoom", () => PlayerInput.CurrentProfile.PanControl_Keyboard, mode => PlayerInput.CurrentProfile.PanControl_Keyboard = mode,
            (ButtonControlMode.Hold, "hold"), (ButtonControlMode.Click, "click"), (ButtonControlMode.OnAlways, "always"), (ButtonControlMode.OffAlways, "off")),
    };

    public string Name { get; }

    /// <summary>The game's value, written as in movie headers (a value the game holds but movies do not allow reads as is).</summary>
    public string Read() => _read();

    /// <summary>What "auto" stands for right now (see <see cref="TerrariaTas.Core.Movies.GameSetting.Auto"/>).</summary>
    public string Resolve(string value) => value == GameSetting.Auto && _auto != null ? _auto() : value;

    public void Apply(string value) => _apply(Resolve(value));

    private static SettingBinding Switch(string name, Func<bool> read, Action<bool> apply) =>
        new(name, () => read() ? "on" : "off", value => apply(value == "on"));

    private static SettingBinding Number(string name, Func<int> read, Action<int> apply) =>
        new(name, () => read().ToString(CultureInfo.InvariantCulture), value => apply(int.Parse(value, CultureInfo.InvariantCulture)));

    /// <summary>A float, written exactly; <paramref name="auto"/> computes the value a fresh install picks.</summary>
    private static SettingBinding Fraction(string name, Func<float> read, Action<float> apply, Func<float>? auto = null) =>
        new(name, () => GameSetting.Format(read()), value => apply(float.Parse(value, CultureInfo.InvariantCulture)),
            auto == null ? null : () => GameSetting.Format(auto()));

    private static SettingBinding Choice<T>(string name, Func<T> read, Action<T> apply, params (T Value, string Name)[] names) =>
        new(name,
            () => names.FirstOrDefault(entry => Equals(entry.Value, read())).Name ?? read()!.ToString(),
            value => apply(names.First(entry => entry.Name == value).Value));
}
