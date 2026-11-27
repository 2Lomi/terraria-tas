using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TerrariaTas.Runtime.GameAccess;

namespace TerrariaTas.Runtime.SaveStates;

/// <summary>
/// Saves and restores every plain static value of the global-state classes (<see cref="GlobalState"/>) rather than
/// a hand-picked list, so counters nobody listed come back too: weather and tile counting, input targets, and the
/// draw-time state that decides how often drawing code draws from the shared RNG. The --verify audit checks that
/// every value comes back.
/// </summary>
internal sealed class StaticFieldState
{
    private const int MaxArrayLength = 1 << 20;

    // Volumes stay as the user sets them; the sound volume the game logic sees is pinned elsewhere.
    // The sizes of the background textures loaded so far are not game state either: Main.LoadBackground sets one when it
    // loads that texture, the first time a background needs it, and the texture stays loaded. Restoring the sizes of a
    // state saved before then would put that size back to 0 for good, and drawing a background of size 0 stops early,
    // before some of its draws from the shared RNG (the mushroom background desynced after a teleport this way).
    private static readonly HashSet<string> Excluded = new()
    {
        "Main.musicVolume", "Main.soundVolume", "Main.ambientVolume",
        "Main.backgroundWidth", "Main.backgroundHeight",
    };

    private static readonly object NotCaptured = new();

    private readonly FieldInfo[] _fields = GlobalState.StaticFields()
        .Where(field => !Excluded.Contains(GlobalState.NameOf(field)))
        .Where(field => GlobalState.IsPlainArray(field.FieldType)
                        || (!field.IsInitOnly && (GlobalState.IsPlainValue(field.FieldType) || field.FieldType == typeof(string))))
        .ToArray();

    public int Count => _fields.Length;

    /// <summary>Values (arrays copied) in field order. The result is never modified afterwards.</summary>
    public object?[] Capture()
    {
        var values = new object?[_fields.Length];
        for (var i = 0; i < _fields.Length; i++)
        {
            var value = _fields[i].GetValue(null);
            values[i] = value is Array array ? array.Length <= MaxArrayLength ? array.Clone() : NotCaptured : value;
        }

        return values;
    }

    public void Restore(object?[] values)
    {
        for (var i = 0; i < _fields.Length; i++)
        {
            var field = _fields[i];
            var saved = values[i];
            if (ReferenceEquals(saved, NotCaptured))
                continue;

            if (saved is Array copy)
            {
                // Copy into the live array when possible: other objects may hold a reference to it.
                if (field.GetValue(null) is Array live && live.GetType() == copy.GetType() && live.Length == copy.Length)
                    Array.Copy(copy, live, copy.Length);
                else if (!field.IsInitOnly)
                    field.SetValue(null, copy.Clone());
            }
            else if (!field.IsInitOnly)
            {
                field.SetValue(null, saved);
            }
        }
    }
}
