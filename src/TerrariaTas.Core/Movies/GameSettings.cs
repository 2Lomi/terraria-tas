using System;
using System.Collections.Generic;
using System.Linq;

namespace TerrariaTas.Core.Movies;

/// <summary>
/// The game settings a movie fixes: every option that changes what the game simulates, from random draws
/// (graphics options that make drawing take numbers from the shared RNG or spawn dust) to how input is understood
/// (controls, interface size). A movie that does not state one gets the value of a fresh install, never the local
/// configuration. docs/movie-format.md says what each one changes.
/// </summary>
public static class GameSettings
{
    public static IReadOnlyList<GameSetting> All { get; } = new[]
    {
        // What is on screen and how the mouse maps to the world and the interface.
        GameSetting.Fraction("Zoom", "1", 1, 2, "Game zoom: the mouse-to-world mapping, the lit area, snow"),
        GameSetting.Fraction("UIScale", GameSetting.Auto, 0.5f, 2, "Interface size: where inventory, crafting and chest slots are (auto: what a fresh install picks for the window height)"),

        // Graphics options that draw from the shared RNG, spawn dust or gore, or change light.
        GameSetting.Switch("Background", true, "Surface backgrounds: the cloud layer that weather reads, mushroom background rolls"),
        GameSetting.Switch("FlashyEffects", true, "World flashes: lightning rolls and the light of a flash"),
        GameSetting.Switch("SwayInWind", true, "Grass and trees swaying: particles, dust and critter flies from the tiles"),
        GameSetting.Switch("BloodAndGore", true, "Blood and gore: off rolls dust and gore again"),
        GameSetting.Switch("PickupText", true, "Item pickup text: crafting flashes spawn dust"),
        GameSetting.Number("WaterfallLimit", 1000, 0, 100000, "Waterfalls drawn at most: their dust and light"),
        GameSetting.Switch("ScreenShake", true, "Screen shake: moves the camera"),
        GameSetting.Switch("StormEffects", true, "Blizzard and sandstorm effects: hide the aurora, whose light gameplay reads"),

        // The map and its frame (where the minimap buttons are).
        GameSetting.Switch("Map", true, "The map: the map key, pylon teleports, inventory map buttons"),
        GameSetting.Fraction("MapScale", "1", 0.5f, 1, "Minimap size: where its buttons are"),
        GameSetting.Choice("MinimapFrame", "Default", new[] { "Default", "Golden", "Remix", "Sticks", "StoneGold", "TwigLeaf", "Leaf", "Retro", "Valkyrie" },
            "Minimap frame style: where its buttons are"),

        // Controls.
        GameSetting.Choice("SmartCursor", "toggle", new[] { "toggle", "hold" }, "What the smart cursor key does"),
        GameSetting.Switch("SmartCursorReleaseMidUse", false, "Hold mode: releasing the key ends smart cursor during an item use"),
        GameSetting.Switch("SmartCursorBlocks", false, "Smart cursor also aims common blocks"),
        GameSetting.Switch("SmartAxeAfterPickaxe", false, "Smart cursor looks for trees after ore"),
        GameSetting.Switch("ShiftClickTrash", false, "Shift-click sends items to the trash"),
        GameSetting.Switch("QuickTrash", true, "Ctrl-click trashes or sells items"),
        GameSetting.Choice("ArmorSetBonusKey", "down", new[] { "down", "up" }, "Direction that triggers armor set bonuses"),
        GameSetting.Choice("FavoriteKey", "LeftAlt", Enum.GetNames(typeof(Key)).Where(name => name != nameof(Key.None)).ToArray(),
            "Key held to favorite an item by clicking it"),
        GameSetting.Switch("Autoswing", false, "Every damaging item reuses itself while held"),
        GameSetting.Choice("AutoDoors", "everything", new[] { "off", "gamepad", "everything" }, "Doors open by walking into them"),
        GameSetting.Choice("HoverControl", "hold", new[] { "hold", "click" }, "Hovering with wings and mounts"),
        GameSetting.Switch("DoubleTapDash", true, "Double-tapping a direction dashes"),
        GameSetting.Choice("Crafting", "modern", new[] { "modern", "classic" }, "Crafting interface"),
        GameSetting.Choice("QuickStack", "quick", new[] { "quick", "smart" }, "What the nearby-chests stack button does"),
        GameSetting.Switch("CraftFromChests", true, "Recipes use nearby chests"),
        GameSetting.Choice("ForwardZoom", "hold", new[] { "hold", "click", "always", "off" },
            "When a rifle scope, the sniper rifle or binoculars move the camera towards the cursor (an input profile option)"),
    };

    public static GameSetting? Find(string name) =>
        All.FirstOrDefault(setting => setting.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
