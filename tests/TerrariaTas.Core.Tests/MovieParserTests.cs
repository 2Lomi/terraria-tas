using System;
using TerrariaTas.Core.Movies;
using Xunit;

namespace TerrariaTas.Core.Tests;

public class MovieParserTests
{
    [Fact]
    public void ParsesHeader()
    {
        var movie = MovieParser.Parse("""
            Terraria: 1.4.5.8
            Player: Runner.plr
            World: Forest.wld
            Seed: -42
            Date: 2026-10-31
            Resolution: 1920x1080
            """);

        var header = movie.Header;
        Assert.Equal("1.4.5.8", header.TerrariaVersion);
        Assert.Equal("Runner.plr", header.Player);
        Assert.Equal("Forest.wld", header.World);
        Assert.Equal(-42, header.Seed);
        Assert.Equal(new DateTime(2026, 10, 31), header.Date);
        Assert.Equal(1920, header.ScreenWidth);
        Assert.Equal(1080, header.ScreenHeight);
        Assert.True(header.StartsInWorld);
    }

    [Fact]
    public void ParsesInputLinesOnATimeline()
    {
        var movie = MovieParser.Parse("""
            10 D Space
             5 a lmb rmb WheelUp WheelUp
             1
            """);

        Assert.Equal(16, movie.TotalFrames);
        Assert.Equal(new[] { Key.Space, Key.D }, movie.LineAt(0).Keys);
        Assert.Same(movie.Lines[0], movie.LineAt(9));
        Assert.Same(movie.Lines[1], movie.LineAt(10));
        Assert.Equal(MouseButtons.Left | MouseButtons.Right, movie.LineAt(12).Buttons);
        Assert.Equal(2, movie.LineAt(12).Wheel);
        Assert.Empty(movie.LineAt(15).Keys);
    }

    [Fact]
    public void CursorCarriesOverUntilChanged()
    {
        var movie = MovieParser.Parse("""
            1
            2 LMB @640,360
            3
            4 @t100,250
            """);

        Assert.Null(movie.Lines[0].Cursor);
        Assert.Equal(CursorTarget.Screen(640, 360), movie.Lines[1].Cursor);
        Assert.Equal(CursorTarget.Screen(640, 360), movie.Lines[2].Cursor);
        Assert.Equal(CursorTarget.Tile(100, 250), movie.Lines[3].Cursor);
    }

    [Fact]
    public void BreakpointsMarkTheFrameOfTheNextLine()
    {
        var movie = MovieParser.Parse("""
            10 D
            ***
            5 A
            ***
            """);

        Assert.Equal(new[] { 10, 15 }, movie.BreakpointFrames);
    }

    [Fact]
    public void IgnoresCommentsAndBlankLines()
    {
        var movie = MovieParser.Parse("# intro\r\n\r\n  3 D   # walk right\r\n");

        Assert.Equal(3, movie.TotalFrames);
        Assert.Equal(3, movie.Lines[0].SourceLine);
    }

    [Theory]
    [InlineData("0 D", 1, "frame count")]
    [InlineData("5 Jmp", 1, "Unknown input 'Jmp'")]
    [InlineData("5 A,D", 1, "Unknown input 'A,D'")]
    [InlineData("5 @12", 1, "Invalid cursor")]
    [InlineData("1 D\nSeed: 3", 2, "before the first input")]
    [InlineData("Speed: 3", 1, "Unknown header property")]
    [InlineData("hello", 1, "Cannot understand")]
    public void ReportsErrorsWithLineNumbers(string text, int line, string message)
    {
        var error = Assert.Throws<MovieFormatException>(() => MovieParser.Parse(text));

        Assert.Equal(line, error.LineNumber);
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void WriterOutputParsesBackToTheSameMovie()
    {
        var original = MovieParser.Parse("""
            Player: Runner.plr
            World: Forest.wld
            Seed: 7

            30 D
            ***
             2 D Space LMB @t10,20 WheelDown
             8 A @300,200
            ***
            """);

        var reparsed = MovieParser.Parse(MovieWriter.Write(original));

        Assert.Equal(original.TotalFrames, reparsed.TotalFrames);
        Assert.Equal(original.BreakpointFrames, reparsed.BreakpointFrames);
        Assert.Equal(original.Header.Seed, reparsed.Header.Seed);
        for (var i = 0; i < original.Lines.Count; i++)
        {
            Assert.True(original.Lines[i].HasSameInputs(reparsed.Lines[i]), $"line {i} differs");
            Assert.Equal(original.Lines[i].FrameCount, reparsed.Lines[i].FrameCount);
        }
    }
}
