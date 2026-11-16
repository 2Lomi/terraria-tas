using System;
using HarmonyLib;
using Terraria;
using Terraria.GameContent.Ambience;
using Terraria.GameContent.Drawing;
using Terraria.Graphics.Light;
using Terraria.IO;
using Terraria.Map;
using Terraria.Social;
using Terraria.Utilities;

namespace TerrariaTas.Runtime.Determinism;

/// <summary>
/// Removes sources of non-determinism that are not part of the movie: Steam, window focus,
/// the real calendar, FPS-adaptive quality settings, autosaves and file writes, and RNGs
/// seeded from the system clock. See docs/research.md for why each pin matters.
/// </summary>
internal sealed class EnvironmentPins
{
    private static EnvironmentPins? _active;

    private DateTime _date = new(2026, 6, 15);
    private int _unseededRandoms;

    public void Install(Harmony harmony)
    {
        _active = this;
        harmony.CreateClassProcessor(typeof(Hooks)).Patch();
    }

    /// <summary>Calendar date the game believes it is (drives Halloween and Christmas content).</summary>
    public void SetDate(DateTime date) => _date = date;

    /// <summary>
    /// Screen size changes mouse coordinates, lighting range and what is on screen, so it is part of the movie.
    /// Written to the TAS data folder's config.json before launch: Terraria applies it while creating its window.
    /// </summary>
    public void ConfigureWindow(int width, int height)
    {
        var config = Main.Configuration;
        config.Load();
        config.Put("LastLaunchedVersion", Main.curRelease); // older configs have their display settings ignored
        config.Put("Fullscreen", false);
        config.Put("WindowMaximized", false);
        config.Put("WindowBorderless", false);
        config.Put("DisplayWidth", width);
        config.Put("DisplayHeight", height);
        config.Save();
    }

    /// <summary>Settings that change simulation results. Re-applied every frame because menus can change them.</summary>
    public void ApplyGameplaySettings()
    {
        Main.qaStyle = 1; // "High" instead of "Auto": Auto tracks real FPS and changes liquid physics and dust limits
        Main.Setting_UseReducedMaxLiquids = false;
        Main.SkipAssemblyLoad = true; // no background thread running static constructors at boot
        Main.autoPause = false;
        if (Lighting.Mode != LightMode.Color)
            Lighting.Mode = LightMode.Color; // the setter also resets the render schedule, so only on change
    }

    /// <summary>
    /// Visual schedules that consume the shared RNG (layer refreshes emit dust and particles, ambient
    /// critters spawn on a timer) start in a state that depends on how long booting took. Reset them so
    /// every launch starts the movie identically. Call after the RNGs were reset.
    /// </summary>
    public void ResetVisualSchedules(int seed)
    {
        Main.renderNow = true; // refresh every cached layer on the first frame
        Main.renderCount = 0;
        AccessTools.Field(typeof(Main), "_drawCycleCounter").SetValue(null, 0UL);
        Main.TileFrameSeed = unchecked((ulong)seed);
        ((UnifiedRandom)AccessTools.Field(typeof(TileDrawing), "_rand").GetValue(Main.instance.TilesRenderer)).SetSeed(seed);
        Main.AmbienceServer = new AmbienceServer();

        // The lighting engine advances one step per frame (minimap, scene metrics, scan, blur) and drives
        // both the layer refreshes and biome detection: start its cycle at the first step.
        var engine = AccessTools.Field(typeof(Lighting), "NewEngine").GetValue(null);
        var state = AccessTools.Field(engine.GetType(), "_state");
        state.SetValue(engine, Enum.ToObject(state.FieldType, 0));
    }

    [HarmonyPatch]
    private static class Hooks
    {
        // Offline like the DRM-free build: no Steam threads, achievements, cloud saves or workshop.
        [HarmonyPatch(typeof(SocialAPI), nameof(SocialAPI.Initialize)), HarmonyPrefix]
        private static void Offline(ref SocialMode? mode) => mode = SocialMode.None;

        // Losing window focus must neither pause the game nor drop input.
        [HarmonyPatch(typeof(FocusHelper), nameof(FocusHelper.UpdateFocus)), HarmonyPrefix]
        private static bool AlwaysFocused(out bool wantsToPause)
        {
            wantsToPause = false;
            FocusHelper.IsSelectedApplication = true;
            return false;
        }

        [HarmonyPatch(typeof(Main), nameof(Main.checkXMas)), HarmonyPrefix]
        private static bool PinnedChristmas()
        {
            var date = _active!._date;
            Main.xMas = (date.Month == 12 && date.Day >= 15) || Main.forceXMasForToday || Main.forceXMasForever;
            return false;
        }

        [HarmonyPatch(typeof(Main), nameof(Main.isHalloweenDateNow)), HarmonyPrefix]
        private static bool PinnedHalloween(ref bool __result)
        {
            var date = _active!._date;
            __result = (date.Month == 10 && date.Day >= 10) || (date.Month == 11 && date.Day <= 1);
            return false;
        }

        // A movie must start from the same files every time, so TAS sessions never write them.
        [HarmonyPatch(typeof(WorldFile), nameof(WorldFile.SaveWorld), typeof(bool), typeof(bool), typeof(bool)), HarmonyPrefix]
        private static bool NoWorldSave() => false;

        [HarmonyPatch(typeof(Player), nameof(Player.SavePlayer)), HarmonyPrefix]
        private static bool NoPlayerSave() => false;

        [HarmonyPatch(typeof(MapHelper), nameof(MapHelper.SaveMap)), HarmonyPrefix]
        private static bool NoMapSave() => false;

        [HarmonyPatch(typeof(Main), "DoUpdate_AutoSave"), HarmonyPrefix]
        private static bool NoAutoSave() => false;

        // new UnifiedRandom() seeds from Environment.TickCount; hand out a fixed sequence instead.
        [HarmonyPatch(typeof(UnifiedRandom), MethodType.Constructor, new Type[0]), HarmonyPostfix]
        private static void SeedFromCounter(UnifiedRandom __instance) =>
            __instance.SetSeed(unchecked(++_active!._unseededRandoms * 7919));
    }
}
