using System;
using System.IO;
using System.Linq;
using TerrariaTas.Core.Movies;
using Xunit;

namespace TerrariaTas.Core.Tests;

public class MovieRecorderTests
{
    private static InputLine Press(params Key[] keys) => new(1, keys, MouseButtons.None, null, 0);

    private static string Lines(MovieRecorder recorder) =>
        string.Join(" | ", recorder.Movie.Lines.Select(line => $"{line.FrameCount} {string.Join(" ", line.Keys)}".TrimEnd()));

    [Fact]
    public void RecordingAtTheEndExtendsTheLastLineWhenTheInputIsTheSame()
    {
        var recorder = new MovieRecorder("3 D\n");

        recorder.Record(3, Press(Key.D));
        recorder.Record(4, Press(Key.A));
        recorder.Record(5, Press(Key.A));

        Assert.Equal("4 D | 2 A", Lines(recorder));
        Assert.Equal(6, recorder.TotalFrames);
        Assert.True(recorder.HasUnsavedChanges);
    }

    [Fact]
    public void RecordingInsideTheMovieReplacesEverythingAfter()
    {
        var recorder = new MovieRecorder("10 D\n5 A\n5 S\n");

        recorder.Record(4, Press(Key.Space));

        Assert.Equal("4 D | 1 Space", Lines(recorder));
        Assert.Equal(5, recorder.TotalFrames);
    }

    [Fact]
    public void RecordingOnTheFirstFrameOfALineReplacesThatLine()
    {
        var recorder = new MovieRecorder("10 D\n5 A\n5 S\n");

        recorder.Record(10, Press(Key.S));

        Assert.Equal("10 D | 1 S", Lines(recorder));
    }

    [Fact]
    public void BreakpointsAfterTheRecordedFrameGoAndABreakpointKeepsLinesApart()
    {
        var recorder = new MovieRecorder("5 D\n***\n5 A\n***\n5 S\n");

        recorder.Record(5, Press(Key.D));

        Assert.Equal("5 D | 1 D", Lines(recorder));
        Assert.Equal(new[] { 5 }, recorder.Movie.BreakpointFrames);
    }

    [Fact]
    public void TheTextKeepsTheFileUpToTheFirstChangedLine()
    {
        const string file =
            "# My run\n" +
            "Seed: 7\n" +
            "\n" +
            "   10 D   # walk\n" +
            "# jump now\n" +
            "    5 D Space @100,200\n" +
            "    5 A\n";
        var recorder = new MovieRecorder(file);

        recorder.Record(12, new InputLine(1, new[] { Key.A }, MouseButtons.Left, CursorTarget.Screen(100, 200), 0));

        Assert.Equal(
            "# My run\n" +
            "Seed: 7\n" +
            "\n" +
            "   10 D   # walk\n" +
            "# jump now\n" +
            "     2 Space D @100,200\n" +
            "     1 A LMB\n",
            recorder.Text());
    }

    [Fact]
    public void TheTextKeepsWindowsLineEndingsAndAppendsAfterTrailingComments()
    {
        var recorder = new MovieRecorder("Seed: 7\r\n\r\n   10 D\r\n# the end");

        recorder.Record(10, Press(Key.A));

        Assert.Equal("Seed: 7\r\n\r\n   10 D\r\n# the end\r\n     1 A\r\n", recorder.Text());
    }

    [Fact]
    public void TheTextReadsBackAsTheSameMovie()
    {
        var recorder = new MovieRecorder("Seed: 3\n\n4 D @t10,20\n***\n4 A\n");
        recorder.Record(6, new InputLine(1, new[] { Key.S }, MouseButtons.None, CursorTarget.Screen(5, 5), 1));
        recorder.Record(7, new InputLine(1, new[] { Key.S }, MouseButtons.None, CursorTarget.Screen(5, 5), 1));

        var text = recorder.Text();
        var reread = new MovieRecorder(text);

        Assert.Equal(Lines(recorder), Lines(reread));
        Assert.Equal(recorder.Movie.BreakpointFrames, reread.Movie.BreakpointFrames);
        Assert.Equal(CursorTarget.Tile(10, 20), reread.Movie.LineAt(5).Cursor);
        Assert.Equal(CursorTarget.Screen(5, 5), reread.Movie.LineAt(7).Cursor);
        Assert.Equal(1, reread.Movie.LineAt(7).Wheel);
    }

    [Fact]
    public void LoadingTheSavedTextClearsTheChanges()
    {
        var recorder = new MovieRecorder("3 D\n");
        recorder.Record(3, Press(Key.A));

        recorder.Load(recorder.Text());

        Assert.False(recorder.HasUnsavedChanges);
        Assert.Equal("3 D | 1 A", Lines(recorder));
        Assert.Equal(2, recorder.Movie.LineAt(3).SourceLine);
    }

    [Fact]
    public void AHistoryTellsWhereTheMovieLeftItAndAdoptingItPutsTheMovieBack()
    {
        var recorder = new MovieRecorder("10 D\n10 A\n");
        var history = recorder.HistoryUpTo(15);
        Assert.Equal(15, history.Frames);
        Assert.Null(recorder.FirstDifferenceFrom(history));

        recorder.Record(12, Press(Key.S));
        Assert.Equal(12, recorder.FirstDifferenceFrom(history));

        Assert.Equal(12, recorder.Adopt(history));
        Assert.Equal("10 D | 5 A", Lines(recorder));
        Assert.Null(recorder.Adopt(history));
    }

    [Fact]
    public void AShorterMovieDiffersWhereItEnds()
    {
        var recorder = new MovieRecorder("5 D\n");
        var longer = new MovieRecorder("8 D\n").HistoryUpTo(8);

        Assert.Equal(5, recorder.FirstDifferenceFrom(longer));
        recorder.Adopt(longer);
        Assert.Equal("8 D", Lines(recorder));
    }

    [Fact]
    public void SplicingTakesTheFirstHistoryThenTheSecond()
    {
        var first = new MovieRecorder("10 D\n").HistoryUpTo(10);
        var second = new MovieRecorder("3 A\n7 S\n").HistoryUpTo(10);

        var spliced = InputHistory.Splice(first, 5, second);
        var recorder = new MovieRecorder("");
        recorder.Adopt(spliced);

        Assert.Equal(10, spliced.Frames);
        Assert.Equal("5 D | 5 S", Lines(recorder));
        Assert.Same(first, InputHistory.Splice(first, 12, second));
    }

    [Fact]
    public void RecordingPastTheEndIsAnError()
    {
        var recorder = new MovieRecorder("3 D\n");

        Assert.Throws<ArgumentOutOfRangeException>(() => recorder.Record(4, Press(Key.D)));
    }

    [Fact]
    public void SavingKeepsTheOriginalFileAsABackupOnce()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tas-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "run.tas");
            File.WriteAllText(path, "original");
            var file = new MovieFile(path);

            file.Save("first");
            file.Save("second");

            Assert.Equal("second", File.ReadAllText(path));
            Assert.Equal("original", File.ReadAllText(file.BackupPath));
            Assert.False(File.Exists(path + ".tmp"));

            var fresh = new MovieFile(Path.Combine(folder, "new.tas"));
            fresh.Save("new");
            Assert.False(File.Exists(fresh.BackupPath));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
