using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Terraria;
using Terraria.GameInput;
using TerrariaTas.Core.Movies;

namespace TerrariaTas.Runtime.Determinism;

/// <summary>
/// Puts a movie's key bindings (<see cref="KeyBindings"/>) into the game's keyboard controls when it loads, so the
/// keys a movie presses do the same thing on every machine, whatever <c>input profiles.json</c> says. From there they
/// are game state like the settings: rebinding in the controls menu is input the movie replays, and savestates keep
/// the bindings. TAS sessions with a movie never save the configuration (<see cref="EnvironmentPins"/>), so the file
/// keeps the player's own bindings; switching control profiles during a movie does nothing.
/// </summary>
internal sealed class KeyBindingPins
{
    private static readonly Dictionary<string, string> MouseButtonsInGame = new()
    {
        ["LMB"] = "Mouse1", ["RMB"] = "Mouse2", ["MMB"] = "Mouse3", ["XB1"] = "Mouse4", ["XB2"] = "Mouse5",
    };

    private static KeyBindingPins? _active;
    private bool _checked;
    private bool _movieOwnsBindings;

    public void Install(Harmony harmony)
    {
        _active = this;
        harmony.CreateClassProcessor(typeof(Hooks)).Patch();
    }

    /// <summary>The movie's bindings, a fresh install's for the actions it does not bind.</summary>
    public void Apply(MovieHeader header)
    {
        var keyboard = Keyboard();
        foreach (var binding in KeyBindings.All)
            keyboard[binding.Name] = header.InputsOf(binding).Select(ToGame).ToList();
        _movieOwnsBindings = true;
    }

    /// <summary>The bindings the game loaded from the input profiles that differ from a fresh install, in header form.</summary>
    public Dictionary<string, IReadOnlyList<string>> ReadChangedFromDefaults()
    {
        var changed = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var entry in Keyboard())
        {
            var binding = KeyBindings.Find(entry.Key)!;
            var inputs = new List<string>();
            foreach (var name in entry.Value)
            {
                if (FromGame(name) is not { } input)
                    Log.Warn($"Key binding {binding.Name}: '{name}' is not a key or mouse button movies know, left out of the movie.");
                else if (!inputs.Contains(input))
                    inputs.Add(input);
            }

            if (inputs.Count != binding.Default.Count || inputs.Except(binding.Default).Any())
                changed[binding.Name] = inputs;
        }

        return changed;
    }

    /// <summary>Savestate support: the keyboard bindings and the forward zoom mode, both held by the control profile.</summary>
    public object Capture() =>
        (Keyboard().ToDictionary(entry => entry.Key, entry => entry.Value.ToArray()), PlayerInput.CurrentProfile.PanControl_Keyboard);

    public void Restore(object? saved)
    {
        var (bindings, forwardZoom) = ((Dictionary<string, string[]>, ButtonControlMode))saved!;
        var keyboard = Keyboard();
        foreach (var entry in bindings)
            keyboard[entry.Key] = entry.Value.ToList();
        PlayerInput.CurrentProfile.PanControl_Keyboard = forwardZoom;
    }

    private Dictionary<string, List<string>> Keyboard()
    {
        var keyboard = PlayerInput.CurrentProfile.InputModes[InputMode.Keyboard].KeyStatus;
        if (!_checked)
        {
            var known = KeyBindings.All.Select(binding => binding.Name).ToList();
            if (!PlayerInput.KnownTriggers.SequenceEqual(known) || !keyboard.Keys.OrderBy(name => name).SequenceEqual(known.OrderBy(name => name)))
            {
                throw new InvalidOperationException("The game's control actions no longer match TerrariaTas.Core.Movies.KeyBindings: "
                                                    + string.Join(", ", PlayerInput.KnownTriggers.Except(known).Concat(known.Except(PlayerInput.KnownTriggers))));
            }

            _checked = true;
        }

        return keyboard;
    }

    private static string ToGame(string input) => MouseButtonsInGame.TryGetValue(input, out var button) ? button : input;

    private static string? FromGame(string name) =>
        MouseButtonsInGame.FirstOrDefault(entry => entry.Value == name).Key ?? KeyBindings.NormalizeInput(name);

    [HarmonyPatch]
    private static class Hooks
    {
        // The controls menu can switch to another profile of the local input profiles: not with a movie loaded.
        [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.SetSelectedProfile)), HarmonyPrefix]
        private static bool KeepMovieBindings() => !_active!._movieOwnsBindings;
    }
}
