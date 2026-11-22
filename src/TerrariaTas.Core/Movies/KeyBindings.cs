using System;
using System.Collections.Generic;
using System.Linq;

namespace TerrariaTas.Core.Movies;

/// <summary>
/// One action of Terraria's keyboard and mouse controls (Jump, Grapple, Hotbar1...) and the inputs a fresh install
/// binds to it, written as in input lines: key names, and LMB/RMB/MMB/XB1/XB2 for mouse buttons.
/// </summary>
public sealed class KeyBinding
{
    internal KeyBinding(string name, string[] defaultInputs)
    {
        Name = name;
        Default = defaultInputs;
    }

    /// <summary>The game's name for the action ("trigger"), as in <c>input profiles.json</c>.</summary>
    public string Name { get; }

    public IReadOnlyList<string> Default { get; }
}

/// <summary>
/// The key bindings a movie fixes: what each key and mouse button does in a world. The movie's input lines say which
/// keys are down; these decide what they mean. An action the movie does not bind keeps a fresh install's keys
/// ("Redigit's Pick"), never the local input profiles. Menus outside a world have bindings of their own, which movies
/// do not use.
/// </summary>
public static class KeyBindings
{
    /// <summary>Every action, in the game's order (<c>PlayerInput.KnownTriggers</c>).</summary>
    public static IReadOnlyList<KeyBinding> All { get; } = new[]
    {
        Bind("MouseLeft", "LMB"), Bind("MouseRight", "RMB"),
        Bind("Up", "W"), Bind("Down", "S"), Bind("Left", "A"), Bind("Right", "D"),
        Bind("Jump", "Space"), Bind("Throw"), Bind("Inventory", "Escape"), Bind("Grapple", "E"),
        Bind("SmartSelect", "LeftShift"), Bind("SmartCursor", "LeftControl"), Bind("QuickMount", "R"),
        Bind("QuickHeal", "H"), Bind("QuickMana", "J"), Bind("QuickBuff", "B"),
        Bind("MapZoomIn", "Add"), Bind("MapZoomOut", "Subtract"), Bind("MapAlphaUp", "PageUp"), Bind("MapAlphaDown", "PageDown"),
        Bind("MapFull", "M"), Bind("MapStyle", "Tab"),
        Bind("Hotbar1", "D1"), Bind("Hotbar2", "D2"), Bind("Hotbar3", "D3"), Bind("Hotbar4", "D4"), Bind("Hotbar5", "D5"),
        Bind("Hotbar6", "D6"), Bind("Hotbar7", "D7"), Bind("Hotbar8", "D8"), Bind("Hotbar9", "D9"), Bind("Hotbar10", "D0"),
        Bind("HotbarMinus"), Bind("HotbarPlus"),
        Bind("DpadRadial1"), Bind("DpadRadial2"), Bind("DpadRadial3"), Bind("DpadRadial4"), Bind("RadialHotbar"), Bind("RadialQuickbar"),
        Bind("DpadSnap1"), Bind("DpadSnap2"), Bind("DpadSnap3"), Bind("DpadSnap4"),
        Bind("MenuUp"), Bind("MenuDown"), Bind("MenuLeft"), Bind("MenuRight"), Bind("LockOn"),
        Bind("ViewZoomIn", "OemPlus"), Bind("ViewZoomOut", "OemMinus"),
        Bind("Loadout1", "F1"), Bind("Loadout2", "F2"), Bind("Loadout3", "F3"), Bind("PreviousLoadout"), Bind("NextLoadout"),
        Bind("ToggleCameraMode", "F4"), Bind("ToggleCreativeMenu", "C"), Bind("ArmorSetAbility"), Bind("Dash"),
    };

    public static KeyBinding? Find(string name) =>
        All.FirstOrDefault(binding => binding.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A key or mouse button as movies write it ("space" reads "Space", "lmb" reads "LMB"), or null for anything else.</summary>
    public static string? NormalizeInput(string token)
    {
        if (MovieParser.ButtonTokens.Keys.FirstOrDefault(name => name.Equals(token, StringComparison.OrdinalIgnoreCase)) is { } button)
            return button;
        return MovieParser.TryParseKey(token, out var key) ? key.ToString() : null;
    }

    private static KeyBinding Bind(string name, params string[] defaultInputs) => new(name, defaultInputs);
}
