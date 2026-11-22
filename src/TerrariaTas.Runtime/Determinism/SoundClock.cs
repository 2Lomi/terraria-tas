using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.Audio;
using TerrariaTas.Core.Sound;

namespace TerrariaTas.Runtime.Determinism;

/// <summary>
/// Makes Terraria's legacy sound player ask a game-frame clock, not the audio device, whether a sound still plays.
/// Its PlaySound draws random numbers depending on that answer (which thunder sound to use; whether an item, NPC hit,
/// NPC death, roar or splash sound plays at all, and so rolls its pitch), on the stream of the update phase that
/// plays the sound: with the real audio state, pausing or fast-forwarding changed drops. PlaySound also skipped all
/// of that when the sound or ambient volume was 0; the volumes now only change what you hear.
/// </summary>
internal sealed class SoundClock
{
    private static SoundClock? _active;

    private readonly ConditionalWeakTable<SoundEffectInstance, Playback> _playbacks = new();
    private FieldInfo[]? _instanceFields;

    public void Install(Harmony harmony)
    {
        _active = this;
        harmony.CreateClassProcessor(typeof(Hooks)).Patch();
    }

    /// <summary>
    /// Savestate support: the sound instances the legacy player holds (its checks read them) and the playback of each.
    /// The audio itself keeps playing whatever it plays.
    /// </summary>
    public object? CaptureState()
    {
        if (SoundEngine.LegacySoundPlayer is not { } player)
            return null;

        var holders = new List<(FieldInfo Field, object? Value)>();
        var playbacks = new List<(SoundEffectInstance Instance, Playback Playback)>();
        foreach (var field in InstanceFields())
        {
            var value = field.GetValue(player);
            holders.Add((field, value is SoundEffectInstance[] array ? array.Clone() : value));
            foreach (var instance in Instances(value))
            {
                if (_playbacks.TryGetValue(instance, out var playback))
                    playbacks.Add((instance, playback.Copy()));
            }
        }

        return (holders, playbacks);
    }

    public void RestoreState(object? state)
    {
        if (state == null || SoundEngine.LegacySoundPlayer is not { } player)
            return;

        var (holders, playbacks) = ((List<(FieldInfo Field, object? Value)>, List<(SoundEffectInstance Instance, Playback Playback)>))state;
        foreach (var (field, value) in holders)
        {
            if (value is SoundEffectInstance[] saved && field.GetValue(player) is SoundEffectInstance[] live && live.Length == saved.Length)
            {
                for (var i = 0; i < saved.Length; i++)
                {
                    if (saved[i] is not { IsDisposed: true })
                        live[i] = saved[i];
                }
            }
            else if (value is not SoundEffectInstance { IsDisposed: true })
            {
                field.SetValue(player, value);
            }
        }

        foreach (var (instance, playback) in playbacks)
        {
            _playbacks.Remove(instance);
            _playbacks.Add(instance, playback.Copy());
        }
    }

    private FieldInfo[] InstanceFields() =>
        _instanceFields ??= typeof(LegacySoundPlayer).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(SoundEffectInstance) || field.FieldType == typeof(SoundEffectInstance[]))
            .ToArray();

    private static IEnumerable<SoundEffectInstance> Instances(object? value) => value switch
    {
        SoundEffectInstance instance => new[] { instance },
        SoundEffectInstance[] array => array.Where(instance => instance != null),
        _ => Enumerable.Empty<SoundEffectInstance>(),
    };

    // The clock: world updates since the world was loaded, part of every savestate.
    private static long Now => Main.GameUpdateCount;

    private static SoundEffectInstance CreateInstance(SoundEffect effect)
    {
        var instance = effect.CreateInstance();
        _active!._playbacks.Remove(instance);
        _active._playbacks.Add(instance, new Playback { Duration = effect.Duration });
        return instance;
    }

    private static void Play(SoundEffectInstance instance)
    {
        instance.Play();
        if (!_active!._playbacks.TryGetValue(instance, out var playback))
            return;
        playback.StartFrame = Now;
        playback.DurationFrames = SoundTiming.DurationFrames(playback.Duration, instance.Pitch);
        playback.Looped = instance.IsLooped;
        playback.Stopped = false;
    }

    private static void Stop(SoundEffectInstance instance)
    {
        instance.Stop();
        MarkStopped(instance);
    }

    private static void StopImmediately(SoundEffectInstance instance, bool immediate)
    {
        instance.Stop(immediate);
        MarkStopped(instance);
    }

    private static void MarkStopped(SoundEffectInstance instance)
    {
        if (_active!._playbacks.TryGetValue(instance, out var playback))
            playback.Stopped = true;
    }

    /// <summary>What PlaySound sees instead of the audio device's answer. Instances it never saw play read as stopped.</summary>
    private static SoundState StateOf(SoundEffectInstance instance) =>
        _active!._playbacks.TryGetValue(instance, out var playback) && playback.StartFrame >= 0 && !playback.Stopped
        && SoundTiming.IsPlaying(playback.StartFrame, playback.DurationFrames, playback.Looped, Now)
            ? SoundState.Playing
            : SoundState.Stopped;

    // PlaySound returns early when the sound volume is 0 (skipping the random rolls); this value replaces the volume there.
    private static float VolumeForShortcut() => 1f;

    private static float NeverSilent(float volume) => volume > 0f ? volume : 1e-4f;

    private sealed class Playback
    {
        public TimeSpan Duration;
        public long StartFrame = -1;
        public long DurationFrames;
        public bool Looped;
        public bool Stopped;

        public Playback Copy() => (Playback)MemberwiseClone();
    }

    [HarmonyPatch]
    private static class Hooks
    {
        private static readonly MethodInfo GetState = AccessTools.PropertyGetter(typeof(SoundEffectInstance), nameof(SoundEffectInstance.State));
        private static readonly MethodInfo PlayInstance = AccessTools.Method(typeof(SoundEffectInstance), nameof(SoundEffectInstance.Play));
        private static readonly MethodInfo StopInstance = AccessTools.Method(typeof(SoundEffectInstance), nameof(SoundEffectInstance.Stop), Type.EmptyTypes);
        private static readonly MethodInfo StopInstanceImmediately = AccessTools.Method(typeof(SoundEffectInstance), nameof(SoundEffectInstance.Stop), new[] { typeof(bool) });
        private static readonly MethodInfo CreateFromEffect = AccessTools.Method(typeof(SoundEffect), nameof(SoundEffect.CreateInstance));
        private static readonly FieldInfo SoundVolume = AccessTools.Field(typeof(Main), nameof(Main.soundVolume));
        private static readonly FieldInfo AmbientVolume = AccessTools.Field(typeof(Main), nameof(Main.ambientVolume));

        [HarmonyPatch(typeof(LegacySoundPlayer), nameof(LegacySoundPlayer.PlaySound)), HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> PlaySound(IEnumerable<CodeInstruction> instructions)
        {
            var code = Redirect(instructions);
            var shortcut = code.FindIndex(instruction => instruction.LoadsField(SoundVolume));
            if (shortcut < 0 || code[shortcut + 1].opcode != OpCodes.Ldc_R4)
                throw new InvalidOperationException("LegacySoundPlayer.PlaySound no longer starts with the sound volume check: review SoundClock.");
            code[shortcut].opcode = OpCodes.Call;
            code[shortcut].operand = AccessTools.Method(typeof(SoundClock), nameof(VolumeForShortcut));

            // Further down, the sound or ambient volume scales the sound, and a sound scaled to 0 returns before its
            // random rolls too: it never reads exactly 0 (a muted sound plays far below hearing instead).
            var audible = 0;
            for (var i = code.Count - 1; i > shortcut; i--)
            {
                if (!code[i].LoadsField(SoundVolume) && !code[i].LoadsField(AmbientVolume))
                    continue;
                code.Insert(i + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SoundClock), nameof(NeverSilent))));
                audible++;
            }

            if (audible == 0)
                throw new InvalidOperationException("LegacySoundPlayer.PlaySound no longer scales sounds by the volume settings: review SoundClock.");
            return code;
        }

        [HarmonyPatch(typeof(LegacySoundPlayer), "CreateInstance"), HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> CreateInstanceHelper(IEnumerable<CodeInstruction> instructions) => Redirect(instructions);

        /// <summary>Routes state checks, instance creation, play and stop through the clock.</summary>
        private static List<CodeInstruction> Redirect(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var redirected = 0;
            foreach (var instruction in code)
            {
                var target = instruction.Calls(GetState) ? nameof(StateOf)
                    : instruction.Calls(PlayInstance) ? nameof(Play)
                    : instruction.Calls(StopInstance) ? nameof(Stop)
                    : instruction.Calls(StopInstanceImmediately) ? nameof(StopImmediately)
                    : instruction.Calls(CreateFromEffect) ? nameof(CreateInstance)
                    : null;
                if (target == null)
                    continue;

                // Same stack in and out: the instance (or effect) becomes the first argument of a static call.
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(SoundClock), target);
                redirected++;
            }

            if (redirected == 0)
                throw new InvalidOperationException("No sound calls found to redirect: the game changed, review SoundClock.");
            return code;
        }
    }
}
