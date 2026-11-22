using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria;
using Terraria.GameContent.Ambience;
using Terraria.DataStructures;
using Terraria.GameContent.Drawing;
using Terraria.GameContent.UI.Minimap;
using Terraria.Graphics;
using Terraria.GameInput;
using Terraria.Testing;
using Terraria.Testing.Cloning;

namespace TerrariaTas.Runtime.SaveStates;

/// <summary>
/// Captures and restores the game. The bulk of the work is done by Terraria's own snapshot system
/// (<c>Terraria.Testing.StateSnapshot</c>, maintained by Re-Logic for their replay tooling), which
/// covers tiles, entities, chests, tile entities, world flags and the per-phase RNG streams.
/// This class adds what that system leaves out; other features add their own state via <see cref="Register"/>.
/// </summary>
internal sealed class SaveStateStore
{
    private static readonly MethodInfo DeepClone = typeof(DeepCloning).GetMethod(nameof(DeepCloning.Clone))!;

    private readonly List<Extra> _extras = new();
    private readonly StaticFieldState _statics = new();

    public SaveStateStore()
    {
        // Every plain static value of Main and the other global-state classes: weather and wind counters, thunder,
        // cloud counts, tile counting, frame counters AI reads, UI flags, input targets, the draw schedule, tile and
        // critter animations... The entries below cover what holds objects.
        Register("Static values", () => _statics.Capture(), saved => _statics.Restore((object?[])saved!));

        // Base RNG used outside world updates: UI (reforging, crafting prefixes, opening bags), weather, drawing.
        AddStatic(typeof(Main), nameof(Main.rand));

        // Previous-frame device states: "just pressed" logic compares against them. (The Main ones are static values
        // too; listed anyway because input must never go missing.)
        AddStatic(typeof(Main), nameof(Main.keyState), nameof(Main.oldKeyState), nameof(Main.mouseLeftRelease), nameof(Main.mouseRightRelease));
        AddStatic(typeof(PlayerInput), nameof(PlayerInput.MouseInfo), nameof(PlayerInput.MouseInfoOld),
            nameof(PlayerInput.ScrollWheelValue), nameof(PlayerInput.ScrollWheelValueOld));

        // Visual systems whose state decides how many samples they draw from Main.rand, which gameplay shares.
        // Found with the RNG consumer census that --verify prints; add new ones here.
        AddStatic(typeof(Main), nameof(Main.itemAnimations), nameof(Main.cloud), nameof(Main.star));
        Add(typeof(AmbienceServer), () => Main.AmbienceServer, "Main.AmbienceServer", "_updatesUntilNextAttempt", "_forcedSpawns");

        // The lighting engine steps once per frame; one of its steps refreshes biome detection (Main.SceneMetrics).
        var lighting = AccessTools.Field(typeof(Lighting), "NewEngine");
        Add(lighting.FieldType, () => lighting.GetValue(null), "Lighting.NewEngine",
            "_state", "_activeLightMap", "_workingLightMap", "_activeProcessedArea", "_workingProcessedArea");
        AddStatic(typeof(Main), "_cameraSceneMetrics", "_playerSceneMetrics", "_usingSeparateCameraSceneMetrics");

        // The draw schedule: tile drawing emits dust and particles (into pools gameplay also uses) whenever it
        // refreshes a cached layer, which depends on the render counters (static values) and on where each layer
        // was last rendered.
        foreach (var layer in AccessTools.GetDeclaredFields(typeof(Main)).Where(f => f.IsStatic && f.FieldType == typeof(WorldSceneLayerTarget)))
            Add(typeof(WorldSceneLayerTarget), () => layer.GetValue(null), "Main." + layer.Name, "_position");
        // Game settings a movie fixes that live on objects rather than in static fields (GameAccess/GlobalState has those).
        Add(typeof(WaterfallManager), () => Main.instance.waterfallManager, "Main.instance.waterfallManager", "maxWaterfallCount");
        var minimapFrame = AccessTools.Field(typeof(SelectionHolder<MinimapFrame>), "ActiveSelectionConfigKey");
        var setMinimapFrame = AccessTools.Method(typeof(SelectionHolder<MinimapFrame>), "SetActiveFrame", new[] { typeof(string) });
        Register("Minimap frame", () => minimapFrame.GetValue(Main.MinimapFrameManagerInstance),
            saved => setMinimapFrame.Invoke(Main.MinimapFrameManagerInstance, new[] { saved }));

        Add(typeof(TileDrawing), () => Main.instance.TilesRenderer, "TilesRenderer",
            "_rand", "_windGrid", "_isActiveAndNotPaused", "_specialsCount", "_specialPositions", "_specialTilesCount",
            "_specialTileX", "_specialTileY", "_vineRootsPositions", "_reverseVineRootsPositions",
            "_treeWindCounter", "_grassWindCounter", "_sunflowerWindCounter", "_vineWindCounter");
    }

    /// <summary>Adds state owned by another feature. Captured values must not be mutated afterwards.</summary>
    public void Register(string name, Func<object?> capture, Action<object?> restore) =>
        _extras.Add(new Extra(name, capture, restore));

    public SaveState Capture(int frame)
    {
        var gameplay = StateSnapshot.Gameplay.Capture();
        var input = StateSnapshot.Input.Capture();
        return new SaveState(frame, gameplay, input, _extras.Select(e => e.Capture()).ToArray());
    }

    public void Restore(SaveState state)
    {
        state.Gameplay.Restore();
        state.Input.Restore();
        for (var i = 0; i < _extras.Count; i++)
        {
            try
            {
                _extras[i].Restore(state.Extras[i]);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"Restoring '{_extras[i].Name}' failed.", e);
            }
        }
    }

    private void AddStatic(Type type, params string[] members) => Add(type, () => null, type.Name, members);

    /// <summary>Saves fields or properties of <paramref name="owner"/> as deep copies made by Terraria's own cloner.</summary>
    private void Add(Type type, Func<object?> owner, string ownerName, params string[] members)
    {
        foreach (var name in members)
        {
            var (get, set, memberType) = Accessor(type, name);
            var clone = memberType.IsValueType ? null : DeepClone.MakeGenericMethod(memberType);
            object? Copy(object? value) => value == null || clone == null ? value : clone.Invoke(null, new[] { value, null });

            // Restores copy again so that a slot can be loaded any number of times.
            Register($"{ownerName}.{name}", () => Copy(get(owner())), saved => set(owner(), Copy(saved)));
        }
    }

    private static (Func<object?, object?> Get, Action<object?, object?> Set, Type Type) Accessor(Type type, string name)
    {
        if (AccessTools.Field(type, name) is { } field)
            return (field.GetValue, field.SetValue, field.FieldType);

        var property = AccessTools.Property(type, name);
        if (property is not { CanRead: true, CanWrite: true })
            throw new MissingMemberException($"{type.FullName}.{name} is not a field or a read/write property.");
        return (property.GetValue, property.SetValue, property.PropertyType);
    }

    private sealed class Extra
    {
        public Extra(string name, Func<object?> capture, Action<object?> restore)
        {
            Name = name;
            Capture = capture;
            Restore = restore;
        }

        public string Name { get; }

        public Func<object?> Capture { get; }

        public Action<object?> Restore { get; }
    }
}
