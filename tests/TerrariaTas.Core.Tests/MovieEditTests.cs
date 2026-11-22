using TerrariaTas.Core.Movies;
using TerrariaTas.Core.Playback;
using Xunit;

namespace TerrariaTas.Core.Tests;

public class MovieEditTests
{
    private static InputHistory Input(string movie, int frames) => InputHistory.Of(MovieParser.Parse(movie).Lines, frames);

    private static MovieEdit Edit(string before, string after, InputHistory played) =>
        new(MovieParser.Parse(before), MovieParser.Parse(after), played);

    [Fact]
    public void HistoriesDifferAtTheirFirstDifferentFrameOrWhereTheShorterEnds()
    {
        Assert.Null(InputHistory.FirstDifference(Input("4 D\n6 D", 10), Input("10 D", 10)));
        Assert.Equal(7, InputHistory.FirstDifference(Input("10 D", 10), Input("7 D\n3 A", 10)));
        Assert.Equal(6, InputHistory.FirstDifference(Input("10 D", 10), Input("10 D", 6)));
        Assert.Equal(0, InputHistory.FirstDifference(InputHistory.Empty, Input("1 D", 1)));
    }

    [Fact]
    public void AnEditAheadOfTheGameNeedsNoReplay()
    {
        var edit = Edit("10 D\n10 A", "10 D\n5 A\n5 S", Input("10 D\n10 A", 12));

        Assert.Equal(15, edit.FirstChangedFrame);
        Assert.Equal(12, edit.FollowedUpTo);
        Assert.False(edit.NeedsReplay);
    }

    [Fact]
    public void AnEditTheGamePlayedThroughNeedsAReplay()
    {
        var edit = Edit("10 D\n10 A", "5 D Space\n5 D\n10 A", Input("10 D\n10 A", 12));

        Assert.Equal(0, edit.FirstChangedFrame);
        Assert.True(edit.NeedsReplay);
    }

    [Fact]
    public void CommentsBreakpointsAndLineSplitsChangeNoInput()
    {
        var edit = Edit("10 D", "# walk\n5 D\n***\n5 D   # still walking", Input("10 D", 10));

        Assert.Null(edit.FirstChangedFrame);
        Assert.False(edit.NeedsReplay);
    }

    [Fact]
    public void LinesAddedAtTheEndChangeTheMovieFromItsOldEnd()
    {
        var edit = Edit("10 D", "10 D\n5 A", Input("10 D", 10));

        Assert.Equal(10, edit.FirstChangedFrame);
        Assert.False(edit.NeedsReplay); // the game stands at frame 10 and has not played it yet
    }

    [Fact]
    public void AMovieCutBeforeTheGameNeedsAReplay()
    {
        var edit = Edit("10 D\n10 A", "10 D", Input("10 D\n10 A", 15));

        Assert.Equal(10, edit.FirstChangedFrame);
        Assert.True(edit.NeedsReplay);
    }

    [Fact]
    public void AGameThatLeftTheMovieBeforeTheChangeIsLeftAsItIs()
    {
        // A savestate of another version of the movie, whose input differs from frame 3.
        var edit = Edit("20 D", "8 D\n12 A", Input("3 D\n9 S", 12));

        Assert.Equal(8, edit.FirstChangedFrame);
        Assert.Equal(3, edit.FollowedUpTo);
        Assert.False(edit.NeedsReplay);
    }

    [Fact]
    public void FramesPlayedPastTheEndAreNotTheMovies()
    {
        // The game played 10 frames of a 10-frame movie, then more without a movie line: its input history stops at 10.
        var edit = Edit("10 D", "10 D\n10 A", Input("10 D", 10));

        Assert.Equal(10, edit.FollowedUpTo);
        Assert.False(edit.NeedsReplay);
    }

    [Fact]
    public void HeaderDifferencesAreListedAndDefaultsWrittenOutAreNone()
    {
        var before = MovieParser.Parse("Seed: 1\nZoom: 1.5\nBind Jump: Space W\n").Header;
        var after = MovieParser.Parse("Seed: 2\nZoom: 1.5\nBind Jump: W Space\nBind Grapple: F\nUIScale: auto\nMap: on\n").Header;

        Assert.Equal(new[] { "Seed", "Bind Grapple" }, before.DifferencesFrom(after));
        Assert.Empty(before.DifferencesFrom(MovieParser.Parse("Seed: 1\nZoom: 1.5\nBind Jump: W Space\n").Header));
    }
}
