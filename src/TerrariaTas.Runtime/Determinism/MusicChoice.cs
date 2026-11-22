using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;
using Terraria.Audio;

namespace TerrariaTas.Runtime.Determinism;

/// <summary>
/// Makes the music the game picks independent of the audio device and of the music volume. Choosing between two
/// versions of the day and underground tracks draws from <c>Main.rand</c> on every update where the audio engine
/// reports neither as playing, which depends on how fast the real track starts (and so on fast-forward and on the
/// machine); with the music volume at 0 the game skipped the choice entirely, which also stops music boxes from
/// recording. A track now counts as playing when it was the music of the previous update, as if audio started
/// instantly, and the volume only changes what you hear.
/// </summary>
internal sealed class MusicChoice
{
    public void Install(Harmony harmony) => harmony.CreateClassProcessor(typeof(Hooks)).Patch();

    // The music logic returns early when the music volume is 0; this value replaces the volume in those tests.
    private static float VolumeForShortcut() => 1f;

    [HarmonyPatch]
    private static class Hooks
    {
        private static readonly FieldInfo MusicVolume = AccessTools.Field(typeof(Main), nameof(Main.musicVolume));

        [HarmonyPatch(typeof(LegacyAudioSystem), nameof(LegacyAudioSystem.IsTrackPlaying)), HarmonyPrefix]
        private static bool TrackPlaying(int trackIndex, ref bool __result)
        {
            __result = Main.curMusic == trackIndex;
            return false;
        }

        [HarmonyPatch(typeof(Main), "UpdateAudio"), HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> UpdateAudio(IEnumerable<CodeInstruction> instructions) =>
            IgnoreMutedMusic(instructions, "Main.UpdateAudio");

        [HarmonyPatch(typeof(Main), "UpdateAudio_DecideOnNewMusic"), HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> DecideOnNewMusic(IEnumerable<CodeInstruction> instructions) =>
            IgnoreMutedMusic(instructions, "Main.UpdateAudio_DecideOnNewMusic");

        [HarmonyPatch(typeof(Main), "UpdateAudio_DecideOnTOWMusic"), HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> DecideOnOtherworldMusic(IEnumerable<CodeInstruction> instructions) =>
            IgnoreMutedMusic(instructions, "Main.UpdateAudio_DecideOnTOWMusic");

        /// <summary>Every <c>musicVolume == 0</c> test reads 1 instead; the volume the music plays at is untouched.</summary>
        private static List<CodeInstruction> IgnoreMutedMusic(IEnumerable<CodeInstruction> instructions, string method)
        {
            var code = instructions.ToList();
            var replaced = 0;
            for (var i = 0; i + 1 < code.Count; i++)
            {
                if (!code[i].LoadsField(MusicVolume) || code[i + 1].opcode != OpCodes.Ldc_R4 || (float)code[i + 1].operand != 0f)
                    continue;

                code[i].opcode = OpCodes.Call;
                code[i].operand = AccessTools.Method(typeof(MusicChoice), nameof(VolumeForShortcut));
                replaced++;
            }

            if (replaced == 0)
                throw new InvalidOperationException($"{method} no longer tests for a music volume of 0: review MusicChoice.");
            return code;
        }
    }
}
