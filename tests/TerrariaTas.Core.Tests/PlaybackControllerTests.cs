using TerrariaTas.Core.Movies;
using TerrariaTas.Core.Playback;
using Xunit;

namespace TerrariaTas.Core.Tests;

public class PlaybackControllerTests
{
    private static PlaybackController Controller(string movie) => new(MovieParser.Parse(movie));

    [Fact]
    public void StartsPausedAndWaits()
    {
        var playback = Controller("10 D");

        Assert.Equal(PlaybackState.Paused, playback.State);
        Assert.False(playback.Next().Run);
        Assert.Equal(0, playback.Frame);
    }

    [Fact]
    public void FrameAdvanceRunsExactlyOneFrame()
    {
        var playback = Controller("10 D");

        playback.AdvanceOneFrame();
        var decision = playback.Next();

        Assert.True(decision.Run);
        Assert.Equal(new[] { Key.D }, decision.Input!.Keys);
        Assert.False(playback.Next().Run);
        Assert.Equal(1, playback.Frame);
    }

    [Fact]
    public void PlayingStopsAtBreakpointsAndResumesPastThem()
    {
        var playback = Controller("3 D\n***\n3 A");
        playback.Play();

        for (var i = 0; i < 3; i++)
            Assert.True(playback.Next().Run);
        Assert.False(playback.Next().Run);
        Assert.Equal(PlaybackState.Paused, playback.State);
        Assert.Equal(3, playback.Frame);

        playback.Play();
        Assert.Equal(new[] { Key.A }, playback.Next().Input!.Keys);
    }

    [Fact]
    public void WatchingPlaysThroughBreakpointsToTheEnd()
    {
        var playback = Controller("3 D\n***\n3 A");
        playback.StopsAtBreakpoints = false;
        playback.Play();

        for (var i = 0; i < 6; i++)
            Assert.True(playback.Next().Run);
        Assert.False(playback.Next().Run);
        Assert.Equal(PlaybackState.Paused, playback.State);
        Assert.Equal(6, playback.Frame);
    }

    [Fact]
    public void StopsAtTheEndOfTheMovieThenContinuesWithoutInput()
    {
        var playback = Controller("2 D");
        playback.Play();

        playback.Next();
        playback.Next();
        Assert.False(playback.Next().Run);

        playback.Play();
        var decision = playback.Next();
        Assert.True(decision.Run);
        Assert.Null(decision.Input);
        Assert.True(playback.IsPastMovieEnd);
    }

    [Fact]
    public void EmptyMovieNeverStops()
    {
        var playback = new PlaybackController(Movie.Empty);
        playback.Play();

        Assert.True(playback.Next().Run);
        Assert.True(playback.Next().Run);
    }

    [Fact]
    public void FastForwardPresentsOnlySomeFramesAndIgnoresRealTime()
    {
        var playback = Controller("100 D");
        playback.SetFastForward(true);

        var presented = 0;
        for (var i = 0; i < PlaybackController.FastForwardPresentInterval * 2; i++)
        {
            var decision = playback.Next();
            Assert.False(decision.RealTime);
            if (decision.Present)
                presented++;
        }

        Assert.Equal(2, presented);
        playback.SetFastForward(false);
        Assert.Equal(PlaybackState.Playing, playback.State);
    }

    [Fact]
    public void RunToPausesBeforeTheTargetFrame()
    {
        var playback = Controller("100 D");

        playback.RunTo(40);
        while (playback.Next().Run)
        {
        }

        Assert.Equal(40, playback.Frame);
        Assert.Equal(PlaybackState.Paused, playback.State);
    }

    private static PlaybackController Recording(string movie, Key live)
    {
        var playback = Controller(movie);
        playback.LiveInput = () => new InputLine(1, new[] { live }, MouseButtons.None, null, 0);
        return playback;
    }

    [Fact]
    public void RecordingWritesTheLiveInputAndNeverStopsAtTheEndOfTheMovie()
    {
        var playback = Recording("", Key.D);

        Assert.True(playback.StartRecording());
        playback.Play();
        for (var i = 0; i < 3; i++)
            Assert.Equal(new[] { Key.D }, playback.Next().Input!.Keys);

        Assert.Equal(3, playback.TotalFrames);
        Assert.Equal(3, playback.Movie.Lines[0].FrameCount);
    }

    [Fact]
    public void RecordingInsideTheMovieOverwritesFromTheCurrentFrame()
    {
        var playback = Recording("10 A\n***\n5 S", Key.D);
        playback.JumpTo(4);
        playback.StartRecording();

        playback.AdvanceOneFrame();
        Assert.Equal(new[] { Key.D }, playback.Next().Input!.Keys);

        Assert.Equal(5, playback.TotalFrames);
        Assert.Empty(playback.Movie.BreakpointFrames);
    }

    [Fact]
    public void RunningToAFrameWhileRecordingReplaysTheMovieAndStopsAtItsEnd()
    {
        var playback = Recording("10 A", Key.D);
        playback.StartRecording();

        playback.RunTo(6);
        while (playback.Next() is { Run: true } decision)
            Assert.Equal(new[] { Key.A }, decision.Input!.Keys);
        Assert.Equal(6, playback.Frame);
        Assert.Equal(10, playback.TotalFrames);

        playback.RunTo(50);
        while (playback.Next().Run)
        {
        }

        Assert.Equal(10, playback.Frame);
        playback.AdvanceOneFrame();
        Assert.Equal(new[] { Key.D }, playback.Next().Input!.Keys);
        Assert.Equal(11, playback.TotalFrames);
    }

    [Fact]
    public void RecordingCannotStartAfterFramesPlayedPastTheEnd()
    {
        var playback = Recording("2 A", Key.D);
        playback.JumpTo(2);
        playback.AdvanceOneFrame();
        playback.Next();

        Assert.False(playback.StartRecording());
        Assert.False(playback.Recording);
    }

    [Fact]
    public void JumpingPastTheEndOfTheMovieStopsRecording()
    {
        var playback = Recording("2 A", Key.D);
        playback.StartRecording();

        playback.JumpTo(5);

        Assert.False(playback.Recording);
    }

    [Fact]
    public void JumpToDoesNotStopImmediatelyOnABreakpoint()
    {
        var playback = Controller("5 D\n***\n5 A");

        playback.JumpTo(5);
        playback.Play();

        Assert.True(playback.Next().Run);
        Assert.Equal(6, playback.Frame);
    }
}
