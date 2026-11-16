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
