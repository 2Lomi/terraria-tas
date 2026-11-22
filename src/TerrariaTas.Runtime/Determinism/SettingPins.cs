using System;
using System.Collections.Generic;
using System.Linq;
using TerrariaTas.Core.Movies;

namespace TerrariaTas.Runtime.Determinism;

/// <summary>
/// Puts the game in the settings a movie states (<see cref="GameSettings"/>) when it loads. From there they are game
/// state like any other: a change during the movie (the settings menu, the zoom keys) is input the movie replays, and
/// savestates keep them (the classes that hold them are in <c>GameAccess/GlobalState</c>). Free play keeps the local
/// configuration.
/// </summary>
internal sealed class SettingPins
{
    private readonly Dictionary<string, SettingBinding> _bindings;

    public SettingPins()
    {
        _bindings = SettingBinding.All.ToDictionary(binding => binding.Name, StringComparer.OrdinalIgnoreCase);
        var unbound = GameSettings.All.Where(setting => !_bindings.ContainsKey(setting.Name)).Select(setting => setting.Name).ToList();
        if (unbound.Count > 0 || _bindings.Count != GameSettings.All.Count)
            throw new InvalidOperationException("Movie settings and their game bindings do not match: " + string.Join(", ", unbound));
    }

    public void Apply(MovieHeader header)
    {
        foreach (var setting in GameSettings.All)
        {
            var binding = _bindings[setting.Name];
            var value = binding.Resolve(header.ValueOf(setting));
            if (binding.Read() != value)
                binding.Apply(value);
        }
    }

    /// <summary>The settings the game loaded from its configuration that differ from a fresh install, in header form.</summary>
    public Dictionary<string, string> ReadChangedFromDefaults()
    {
        var changed = new Dictionary<string, string>();
        foreach (var setting in GameSettings.All)
        {
            var binding = _bindings[setting.Name];
            var value = setting.Normalize(binding.Read()) ?? setting.Default;
            if (value != binding.Resolve(setting.Default))
                changed[setting.Name] = value;
        }

        return changed;
    }
}
