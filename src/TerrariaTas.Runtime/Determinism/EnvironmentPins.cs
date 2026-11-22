using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using HarmonyLib;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.Ambience;
using Terraria.GameContent.Creative;
using Terraria.GameContent.Drawing;
using Terraria.Graphics.Light;
using Terraria.IO;
using Terraria.Map;
using Terraria.Social;
using Terraria.Utilities;

namespace TerrariaTas.Runtime.Determinism;

/// <summary>
/// Removes sources of non-determinism that are not part of the movie: Steam, window focus,
/// the real calendar, FPS-adaptive quality settings, autosaves and file writes, RNGs seeded
/// from the system clock or a GUID, and everything booting leaves behind that depends on how
/// long it took. See docs/research.md for why each pin matters.
/// </summary>
internal sealed class EnvironmentPins
{
    // Critter cage, fish bowl and jar animations ("birdCageFrame", "fishBowlFrameMode", "squirrelCageFrameCounterOrange"...).
    private static readonly Regex CritterAnimation = new("^[A-Za-z0-9]+(Cage|Bowl|Jar)(Frame|FrameCounter|FrameMode|Mode)(Orange)?$");

    // Other values that advance on every frame from boot, title screen included: animations, color pulses, and the
    // visual-effects timer some NPC and projectile AI reads.
    private static readonly string[] AdvancedSinceBoot =
    {
        nameof(Main.tileFrame), nameof(Main.tileFrameCounter), nameof(Main.wallFrame), nameof(Main.wallFrameCounter),
        nameof(Main.bgFrame), nameof(Main.bgFrameCounter), nameof(Main.wFrame), nameof(Main.wFrCounter),
        nameof(Main.weatherVaneBobframe), nameof(Main.critterCage),
        nameof(Main.timeForVisualEffects), nameof(Main.GlobalTimeWrappedHourly),
        nameof(Main.DiscoR), nameof(Main.DiscoG), nameof(Main.DiscoB),
        nameof(Main.cursorColor), nameof(Main.cursorColorDirection), nameof(Main.cursorAlpha), nameof(Main.cursorScale),
        nameof(Main.GamepadCursorAlpha), nameof(Main.mouseTextColor), nameof(Main.mouseTextColorChange),
        nameof(Main.demonTorch), nameof(Main.demonTorchDir), nameof(Main.martianLight), nameof(Main.martianLightDir),
        nameof(Main.gFade), nameof(Main.gFadeDir), nameof(Main.gFader), nameof(Main.masterColor), nameof(Main.masterColorDir),
        nameof(Main.sunCircle), nameof(Main.fadeCounter), nameof(Main.LastCelestialBodyPosition),
    };

    private static EnvironmentPins? _active;

    private DateTime _date = new(2026, 6, 15);
    private int _unseededRandoms;
    private int _unseededFastRandoms;
    private (FieldInfo Field, object? Value)[] _bootAnimations = Array.Empty<(FieldInfo, object?)>();

    public void Install(Harmony harmony)
    {
        _active = this;
        harmony.CreateClassProcessor(typeof(Hooks)).Patch();
    }

    /// <summary>Calendar date the game believes it is (drives Halloween and Christmas content).</summary>
    public void SetDate(DateTime date) => _date = date;

    /// <summary>
    /// Set when a movie is loaded: its settings and key bindings are applied to the game, and the game must not save
    /// them into config.json and input profiles.json as if they were the player's (it saves both when quitting).
    /// </summary>
    public bool KeepConfigurationFiles { get; set; }

    /// <summary>Set by the creation launch of --new only, which saves the player and world it creates. TAS sessions never write either.</summary>
    public bool SavesCreatedFiles { get; set; }

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
    /// Remembers what advances on every frame from boot (animations, color pulses, the visual-effects timer) before the
    /// first frame runs. Several of these draw from the shared RNG on their own schedule (critter cages) or feed AI:
    /// the movie start puts them back, so how long the menus stayed up changes nothing. Call before
    /// the first update.
    /// </summary>
    public void CaptureBootState()
    {
        _bootAnimations = AccessTools.GetDeclaredFields(typeof(Main))
            .Where(field => field.IsStatic && !field.IsLiteral && (AdvancedSinceBoot.Contains(field.Name) || CritterAnimation.IsMatch(field.Name)))
            .Select(field => (field, Copy(field.GetValue(null))))
            .ToArray();
    }

    /// <summary>
    /// World-session values that booting and world loading roll with generators seeded from the clock or a GUID,
    /// or with the shared RNG at a point that depends on how long loading took: wind and weather counters, the cloud
    /// layer, critter spawn chances, the traveling merchant's shop, stars, clouds and the seed some AI reads. Rolled
    /// again from the movie seed, on a generator of their own so that the movie's shared RNG sequence stays put.
    /// Call after the RNGs were reset for the movie.
    /// </summary>
    public void ResetWorldSession(int seed)
    {
        _unseededRandoms = 0;
        _unseededFastRandoms = 0;
        var movieRandom = Main.rand;
        Main.rand = new UnifiedRandom(unchecked(seed * 1103515245 + 12345));
        try
        {
            Main.ResetWindCounter(resetExtreme: true);
            Main.weatherCounter = Main.rand.Next(3600, 10800); // as UpdateWeather does when it runs out
            Main.cloudBGActive = 0f;
            Main.cloudBGAlpha = 0f;
            Main.numCloudsTemp = Main.numClouds;
            Main.SmartCursorWanted_Mouse = false; // as a fresh game process has it: no file keeps it
            NPC.setFireFlyChance();
            Chest.SetupTravelShop();
            Star.SpawnStars();
            Cloud.resetClouds();
            AccessTools.Property(typeof(Main), nameof(Main.UnpausedUpdateSeed)).SetValue(null, unchecked((ulong)Main.rand.Next() << 31 ^ (ulong)Main.rand.Next()));
        }
        finally
        {
            Main.rand = movieRandom;
        }
    }

    /// <summary>
    /// Visual schedules that consume the shared RNG (layer refreshes emit dust and particles, ambient
    /// critters spawn on a timer, animations roll their next frame) start in a state that depends on how
    /// long booting took. Reset them so every launch starts the movie identically. Call after
    /// <see cref="ResetWorldSession"/>.
    /// </summary>
    public void ResetVisualSchedules(int seed)
    {
        Main.renderNow = true; // refresh every cached layer on the first frame
        Main.renderCount = 0;
        AccessTools.Field(typeof(Main), "_drawCycleCounter").SetValue(null, 0UL);
        Main.TileFrameSeed = unchecked((ulong)seed);
        ((UnifiedRandom)AccessTools.Field(typeof(TileDrawing), "_rand").GetValue(Main.instance.TilesRenderer)).SetSeed(seed);
        Main.AmbienceServer = new AmbienceServer();

        foreach (var (field, value) in _bootAnimations)
        {
            if (value is Array saved && field.GetValue(null) is Array live && live.Length == saved.Length)
                Array.Copy(saved, live, saved.Length);
            else
                field.SetValue(null, Copy(value));
        }

        // Item animations only exist once the splash screen ends, after the boot values above were taken: rebuild
        // them with the game's own setup, which gives fresh ones every launch (they draw from Main.rand as they run).
        Main.InitializeItemAnimations();

        // Animated forest backgrounds (some worlds) take their textures from bgFrame, just put back: let the game's own
        // animation step set them. It counts up before it advances a frame, so with every counter one lower it only
        // sets the textures; the counters are then put back as well.
        var counters = (int[])Main.bgFrameCounter.Clone();
        for (var i = 0; i < counters.Length; i++)
            Main.bgFrameCounter[i]--;
        AccessTools.Method(typeof(Main), "DoUpdate_AnimateBackgrounds").Invoke(null, null);
        Array.Copy(counters, Main.bgFrameCounter, counters.Length);

        // The lighting engine advances one step per frame (minimap, scene metrics, scan, blur) and drives
        // both the layer refreshes and biome detection: start its cycle at the first step.
        var engine = AccessTools.Field(typeof(Lighting), "NewEngine").GetValue(null);
        var state = AccessTools.Field(engine.GetType(), "_state");
        state.SetValue(engine, Enum.ToObject(state.FieldType, 0));

        // Light flicker comes from generators the engine seeded from a GUID when the game started.
        var scanner = AccessTools.Field(typeof(LightingEngine), "_tileScanner").GetValue(engine);
        AccessTools.Field(typeof(TileLightScanner), "_random").SetValue(scanner, new FastRandom(unchecked((ulong)seed * 3)));
        AccessTools.Field(typeof(LightMap), "_random").SetValue(AccessTools.Field(typeof(LightingEngine), "_activeLightMap").GetValue(engine), new FastRandom(unchecked((ulong)seed * 5)));
        AccessTools.Field(typeof(LightMap), "_random").SetValue(AccessTools.Field(typeof(LightingEngine), "_workingLightMap").GetValue(engine), new FastRandom(unchecked((ulong)seed * 7)));
    }

    /// <summary>The counters behind the seeds of generators created without one: part of savestates.</summary>
    public object CaptureSeedCounters() => new[] { _unseededRandoms, _unseededFastRandoms };

    public void RestoreSeedCounters(object? saved)
    {
        var counters = (int[])saved!;
        _unseededRandoms = counters[0];
        _unseededFastRandoms = counters[1];
    }

    private static object? Copy(object? value) => value is Array array ? array.Clone() : value;

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
        private static bool NoWorldSave() => _active!.SavesCreatedFiles;

        [HarmonyPatch(typeof(Player), nameof(Player.SavePlayer)), HarmonyPrefix]
        private static bool NoPlayerSave() => _active!.SavesCreatedFiles;

        [HarmonyPatch(typeof(MapHelper), nameof(MapHelper.SaveMap)), HarmonyPrefix]
        private static bool NoMapSave() => false;

        [HarmonyPatch(typeof(Main), "DoUpdate_AutoSave"), HarmonyPrefix]
        private static bool NoAutoSave() => false;

        [HarmonyPatch(typeof(Main), nameof(Main.SaveSettings)), HarmonyPrefix]
        private static bool KeepConfiguration(ref bool __result)
        {
            if (!_active!.KeepConfigurationFiles)
                return true;
            __result = true;
            return false;
        }

        // Resource packs can replace sounds (whose length decides random rolls, see SoundClock), fonts and texts (which
        // place buttons); the game already rejects textures of another size. TAS sessions use the game's own content;
        // the packs stay selected in the configuration.
        [HarmonyPatch(typeof(AssetSourceController), nameof(AssetSourceController.UseResourcePacks)), HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> VanillaContent(IEnumerable<CodeInstruction> instructions)
        {
            var enabledPacks = AccessTools.PropertyGetter(typeof(ResourcePackList), nameof(ResourcePackList.EnabledPacks));
            var code = instructions.ToList();
            var replaced = 0;
            foreach (var instruction in code.Where(instruction => instruction.Calls(enabledPacks)))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(Hooks), nameof(NoPacks));
                replaced++;
            }

            if (replaced == 0)
                throw new InvalidOperationException("AssetSourceController.UseResourcePacks no longer reads the enabled packs: review EnvironmentPins.");
            return code;
        }

        private static IEnumerable<ResourcePack> NoPacks(ResourcePackList packs)
        {
            var enabled = packs.EnabledPacks.Select(pack => pack.Name).ToList();
            if (enabled.Count > 0)
                Log.Info($"Resource packs are not used in TAS sessions (they can change sounds and fonts the simulation depends on): {string.Join(", ", enabled)}");
            return Enumerable.Empty<ResourcePack>();
        }

        // Journey mode sliders commit a change once the real clock reads at least the time of the last commit: always,
        // unless Windows sets its clock back. Now always.
        [HarmonyPatch(typeof(CreativePowers.APerPlayerSliderPower), "AttemptPushingChange"), HarmonyPrefix]
        private static void PlayerSliderCommits(ref DateTime ____nextTimeWeCanPush) => ____nextTimeWeCanPush = DateTime.MinValue;

        [HarmonyPatch(typeof(CreativePowers.ASharedSliderPower), "AttemptPushingChange"), HarmonyPrefix]
        private static void WorldSliderCommits(ref DateTime ____nextTimeWeCanPush) => ____nextTimeWeCanPush = DateTime.MinValue;

        // A player saved while dead enters the world with the real time since saving taken off the respawn timer:
        // movies start with the player as the file has it.
        [HarmonyPatch(typeof(Player), "AdjustRespawnTimerForWorldJoining"), HarmonyPrefix]
        private static bool RespawnTimerAsSaved() => false;

        // new UnifiedRandom() seeds from Environment.TickCount; hand out a fixed sequence instead.
        [HarmonyPatch(typeof(UnifiedRandom), MethodType.Constructor, new Type[0]), HarmonyPostfix]
        private static void SeedFromCounter(UnifiedRandom __instance) =>
            __instance.SetSeed(unchecked(++_active!._unseededRandoms * 7919));

        // FastRandom.CreateWithRandomSeed() seeds from a GUID: wind gust timing (during play), stars, light flicker.
        [HarmonyPatch(typeof(FastRandom), nameof(FastRandom.CreateWithRandomSeed)), HarmonyPrefix]
        private static bool FastRandomFromCounter(ref FastRandom __result)
        {
            __result = new FastRandom(unchecked((ulong)++_active!._unseededFastRandoms * 0x9E3779B97F4A7C15UL));
            return false;
        }
    }
}
