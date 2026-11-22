using System;

namespace TerrariaTas.Core.Sound;

/// <summary>
/// How long a sound plays, counted in game frames. Terraria's sound code asks whether earlier sounds still play
/// (to pick a free thunder sound, to skip an item sound already playing...) and draws random numbers depending on
/// the answer. Answering from the frame count rather than the audio device gives the same answer whether the game
/// runs at full speed, is paused or fast-forwards; at full speed it is what the audio device would say.
/// </summary>
public static class SoundTiming
{
    public const double FramesPerSecond = 60.0;

    /// <summary>Frames a sound lasts. XNA pitch is in octaves: +1 plays twice as fast, so half as long.</summary>
    public static long DurationFrames(TimeSpan duration, float pitch) =>
        Math.Max(1, (long)Math.Ceiling(duration.TotalSeconds * FramesPerSecond / Math.Pow(2, pitch)));

    /// <summary>Whether a sound started on <paramref name="startFrame"/> still plays on <paramref name="frame"/>.</summary>
    public static bool IsPlaying(long startFrame, long durationFrames, bool looped, long frame) =>
        frame >= startFrame && (looped || frame - startFrame < durationFrames);
}
