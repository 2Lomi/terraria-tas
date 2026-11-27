using System;
using System.IO;
using System.Linq;
using TerrariaTas.Core.Studio;
using Xunit;

namespace TerrariaTas.Core.Tests;

public class MapRowsTests
{
    [Fact]
    public void RowsRoundTripIntoAWorldSizedMap()
    {
        const int width = 4;
        var colors = new[]
        {
            0x112233, 0x112233, 0x112233, 0x445566,
            0x445566, 0x445566, 0x112233, 0x778899,
        };
        var builder = new MapRowsBuilder(width);
        foreach (var color in colors)
            builder.Add(color);

        var rows = Codec.RoundTrip(builder.Build(requestId: 7, frame: 120, firstRow: 1));
        var map = new int[width * 3];
        rows.CopyTo(map, width);

        Assert.Equal(7, rows.RequestId);
        Assert.Equal(120, rows.Frame);
        Assert.Equal(2, rows.RowCount);
        Assert.Equal(new int[width], map.Take(width)); // row 0 untouched
        Assert.Equal(colors, map.Skip(width));
    }

    [Fact]
    public void RunsContinueAcrossRowsAndColorsAreListedOnce()
    {
        var builder = new MapRowsBuilder(3);
        foreach (var color in new[] { 1, 1, 1, 1, 2, 1 })
            builder.Add(color);

        var rows = builder.Build(0, 0, 0);

        Assert.Equal(new[] { 1, 2 }, rows.Palette);
        Assert.Equal(new[] { 4, 1, 1 }, rows.RunLengths);
        Assert.Equal(new[] { 0, 1, 0 }, rows.RunColors);
    }

    [Fact]
    public void OnlyTheColorBitsCount()
    {
        var builder = new MapRowsBuilder(2);
        builder.Add(unchecked((int)0xFF123456));
        builder.Add(0x123456);

        Assert.Equal(new[] { 0x123456 }, builder.Build(0, 0, 0).Palette);
    }

    [Fact]
    public void PartialRowsAreRefused()
    {
        var builder = new MapRowsBuilder(4);
        builder.Add(1);

        Assert.Throws<InvalidOperationException>(() => builder.Build(0, 0, 0));
    }

    [Fact]
    public void RunsThatDoNotCoverTheRowsAreRejected()
    {
        var shortRuns = new MapRows { FirstRow = 0, RowCount = 1, Palette = new[] { 5 }, RunColors = new[] { 0 }, RunLengths = new[] { 3 } };
        var badColor = new MapRows { FirstRow = 0, RowCount = 1, Palette = new[] { 5 }, RunColors = new[] { 1 }, RunLengths = new[] { 4 } };
        var outside = new MapRows { FirstRow = 2, RowCount = 1, Palette = new[] { 5 }, RunColors = new[] { 0 }, RunLengths = new[] { 4 } };

        Assert.Throws<InvalidDataException>(() => shortRuns.CopyTo(new int[8], 4));
        Assert.Throws<InvalidDataException>(() => badColor.CopyTo(new int[8], 4));
        Assert.Throws<InvalidDataException>(() => outside.CopyTo(new int[8], 4));
    }
}
