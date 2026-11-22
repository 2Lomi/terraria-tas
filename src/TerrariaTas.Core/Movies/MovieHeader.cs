using System;
using System.Collections.Generic;

namespace TerrariaTas.Core.Movies;

/// <summary>Everything besides inputs that is needed to replay a movie identically.</summary>
public sealed class MovieHeader
{
    // Terraria's window size limits (Main.minScreenW... with 4K support off, which the TAS pins). It also rounds odd sizes
    // down to even ones.
    public const int MinScreenWidth = 800;
    public const int MinScreenHeight = 600;
    public const int MaxScreenWidth = 1920;
    public const int MaxScreenHeight = 1200;

    /// <summary>Terraria build the movie was made on, e.g. "1.4.5.8". Null when not specified.</summary>
    public string? TerrariaVersion { get; set; }

    /// <summary>Player file name inside the TAS data folder's Players directory.</summary>
    public string? Player { get; set; }

    /// <summary>SHA-256 of the player file the movie was made with (<see cref="FileChecksum"/>), or null when not pinned.</summary>
    public string? PlayerChecksum { get; set; }

    /// <summary>World file name inside the TAS data folder's Worlds directory.</summary>
    public string? World { get; set; }

    /// <summary>SHA-256 of the world file the movie was made with (<see cref="FileChecksum"/>), or null when not pinned.</summary>
    public string? WorldChecksum { get; set; }

    /// <summary>Seed of the main-thread RNG (Main.rand) when the movie starts.</summary>
    public int Seed { get; set; }

    /// <summary>Calendar date seen by the game (Halloween and Christmas depend on it).</summary>
    public DateTime Date { get; set; } = new(2026, 6, 15);

    public int ScreenWidth { get; set; } = 1280;

    public int ScreenHeight { get; set; } = 720;

    /// <summary>
    /// Game settings the movie states, by <see cref="GameSetting.Name"/>. The TAS applies them when the movie loads;
    /// a setting the movie does not state takes its default, never the local configuration.
    /// </summary>
    public Dictionary<string, string> Settings { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Key bindings the movie states, by <see cref="KeyBinding.Name"/>: keys and mouse buttons as input lines write
    /// them (an empty list unbinds the action). An action the movie does not bind keeps a fresh install's keys.
    /// </summary>
    public Dictionary<string, IReadOnlyList<string>> Bindings { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool StartsInWorld => Player != null && World != null;

    /// <summary>The value of <paramref name="setting"/> in this movie.</summary>
    public string ValueOf(GameSetting setting) => Settings.TryGetValue(setting.Name, out var value) ? value : setting.Default;

    /// <summary>The keys and mouse buttons bound to <paramref name="binding"/> in this movie.</summary>
    public IReadOnlyList<string> InputsOf(KeyBinding binding) => Bindings.TryGetValue(binding.Name, out var inputs) ? inputs : binding.Default;

    /// <summary>
    /// The properties whose values differ in <paramref name="other"/>, as the header writes them ("Seed", "Zoom",
    /// "Bind Jump"). A setting or binding left out counts as its default, which it is.
    /// </summary>
    public IReadOnlyList<string> DifferencesFrom(MovieHeader other)
    {
        var names = new List<string>();
        Compare("Terraria", TerrariaVersion == other.TerrariaVersion);
        Compare("Player", Player == other.Player);
        Compare("PlayerChecksum", PlayerChecksum == other.PlayerChecksum);
        Compare("World", World == other.World);
        Compare("WorldChecksum", WorldChecksum == other.WorldChecksum);
        Compare("Seed", Seed == other.Seed);
        Compare("Date", Date == other.Date);
        Compare("Resolution", ScreenWidth == other.ScreenWidth && ScreenHeight == other.ScreenHeight);
        foreach (var setting in GameSettings.All)
            Compare(setting.Name, ValueOf(setting) == other.ValueOf(setting));
        foreach (var binding in KeyBindings.All)
            Compare("Bind " + binding.Name, new HashSet<string>(InputsOf(binding)).SetEquals(other.InputsOf(binding)));
        return names;

        void Compare(string name, bool same)
        {
            if (!same)
                names.Add(name);
        }
    }
}
