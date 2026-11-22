using System;
using TerrariaTas.Core.Sound;
using Xunit;

namespace TerrariaTas.Core.Tests;

public class SoundTimingTests
{
    [Theory]
    [InlineData(1000, 0f, 60)]   // one second at normal pitch
    [InlineData(1000, 1f, 30)]   // an octave up plays twice as fast
    [InlineData(1000, -1f, 120)] // an octave down, twice as slow
    [InlineData(10, 0f, 1)]      // shorter than a frame still lasts one frame
    [InlineData(1010, 0f, 61)]   // partial frames round up
    public void DurationFollowsPitch(int milliseconds, float pitch, long expectedFrames) =>
        Assert.Equal(expectedFrames, SoundTiming.DurationFrames(TimeSpan.FromMilliseconds(milliseconds), pitch));

    [Fact]
    public void SoundPlaysForItsDurationFromItsStartFrame()
    {
        Assert.False(SoundTiming.IsPlaying(startFrame: 100, durationFrames: 30, looped: false, frame: 99));
        Assert.True(SoundTiming.IsPlaying(100, 30, false, 100));
        Assert.True(SoundTiming.IsPlaying(100, 30, false, 129));
        Assert.False(SoundTiming.IsPlaying(100, 30, false, 130));
    }

    [Fact]
    public void LoopedSoundPlaysUntilStopped() => Assert.True(SoundTiming.IsPlaying(100, 30, looped: true, frame: 100_000));
}
