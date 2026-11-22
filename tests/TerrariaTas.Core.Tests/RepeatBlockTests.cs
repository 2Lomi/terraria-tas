using System.Linq;
using TerrariaTas.Core.Movies;
using Xunit;

namespace TerrariaTas.Core.Tests;

public class RepeatBlockTests
{
    private static string Lines(Movie movie) =>
        string.Join(" | ", movie.Lines.Select(line => $"{line.FrameCount} {string.Join(" ", line.Keys)}".TrimEnd()));

    [Fact]
    public void ABlockPlaysItsLinesOncePerRound()
    {
        var movie = MovieParser.Parse("""
            10 D
            Repeat 3     # mash
              1 LeftControl
              2
            EndRepeat
            5 A
            """);

        Assert.Equal("10 D | 1 LeftControl | 2 | 1 LeftControl | 2 | 1 LeftControl | 2 | 5 A", Lines(movie));
        Assert.Equal(24, movie.TotalFrames);
        Assert.Equal(new[] { 0, 2, 2, 2, 2, 2, 2, 0 }, movie.Lines.Select(line => line.RepeatLine));
        Assert.Equal(new[] { 1, 3, 4, 3, 4, 3, 4, 6 }, movie.Lines.Select(line => line.SourceLine));
    }

    [Fact]
    public void TheCursorCarriesOverFromRoundToRound()
    {
        var movie = MovieParser.Parse("""
            1 @10,10
            repeat 2
            1 D
            1 A @20,20
            endrepeat
            """);

        Assert.Equal(CursorTarget.Screen(10, 10), movie.Lines[1].Cursor);
        Assert.Equal(CursorTarget.Screen(20, 20), movie.Lines[2].Cursor);
        Assert.Equal(CursorTarget.Screen(20, 20), movie.Lines[3].Cursor); // the first line of round 2 keeps round 1's last cursor
    }

    [Theory]
    [InlineData("Repeat 2\n1 D\nRepeat 2\n1 A\nEndRepeat\nEndRepeat", 3, "cannot be nested")]
    [InlineData("Repeat 2\n1 D\n", 1, "no EndRepeat")]
    [InlineData("1 D\nEndRepeat", 2, "without a Repeat")]
    [InlineData("Repeat 0\n1 D\nEndRepeat", 1, "number of rounds")]
    [InlineData("Repeat\n1 D\nEndRepeat", 1, "number of rounds")]
    [InlineData("Repeat 2\n1 D\n***\n1 A\nEndRepeat", 3, "breakpoint cannot be inside")]
    [InlineData("Repeat 2\n# nothing\nEndRepeat", 1, "no input line")]
    [InlineData("Repeat 2\n1 Jmp\nEndRepeat", 2, "Unknown input 'Jmp'")]
    [InlineData("Repeat 100000\n1 D\n1 A\nEndRepeat", 1, "more than the 100000")]
    [InlineData("Repeat 2\nSeed: 3\n1 D\nEndRepeat", 2, "Header properties")]
    [InlineData("Repeat 2\n1 D\nEndRepeat now", 3, "nothing after it")]
    public void MistakesAreReportedWithTheirLine(string text, int line, string message)
    {
        var error = Assert.Throws<MovieFormatException>(() => MovieParser.Parse(text));

        Assert.Equal(line, error.LineNumber);
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void RecordingAfterABlockKeepsTheBlockAsWritten()
    {
        const string file = "Repeat 2\n  1 D\n  1\nEndRepeat\n   3 A\n";
        var recorder = new MovieRecorder(file);

        recorder.Record(6, new InputLine(1, new[] { Key.S }, MouseButtons.None, null, 0));

        Assert.Equal("Repeat 2\n  1 D\n  1\nEndRepeat\n     2 A\n     1 S\n", recorder.Text());
    }

    [Fact]
    public void RecordingInsideABlockWritesItOutRoundByRound()
    {
        const string file = "# start\n   5 W\nRepeat 3   # mash\n  1 D\n  1\nEndRepeat\n   3 A\n";
        var recorder = new MovieRecorder(file);

        recorder.Record(8, new InputLine(1, new[] { Key.S }, MouseButtons.None, null, 0)); // the first frame of round 2's second line

        Assert.Equal("# start\n   5 W\n     1 D\n     1\n     1 D\n     1 S\n", recorder.Text());
        Assert.Equal("5 W | 1 D | 1 | 1 D | 1 S", Lines(new MovieRecorder(recorder.Text()).Movie));
    }
}
