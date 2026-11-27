using TerrariaTas.Core.World;
using Xunit;

namespace TerrariaTas.Core.Tests;

public class WorldCoordinatesTests
{
    // A small world: 4200 × 1200 tiles, surface line at row 300, caverns from row 400.
    private static readonly WorldGeometry Small = new() { Width = 4200, Height = 1200, WorldSurface = 300, RockLayer = 400 };

    private const float CenterX = 4200 * 16 / 2;

    [Theory]
    [InlineData(CenterX, "Center")]
    [InlineData(CenterX + 80, "10' East")] // 5 tiles east: two feet per tile
    [InlineData(CenterX - 8, "1' West")] // half a tile
    [InlineData(CenterX + 7, "Center")] // the game truncates toward zero
    [InlineData(CenterX - 7, "Center")]
    public void CompassCountsTwoFeetPerTileFromTheCenter(float centerX, string expected) =>
        Assert.Equal(expected, WorldCoordinates.Compass(centerX, Small));

    [Theory]
    [InlineData(300 * 16, "Level Surface")]
    [InlineData(5000, "25' Underground")]
    [InlineData(6500, "212' Caverns")]
    [InlineData(16000, "1400' Underworld")] // below row 1200 - 204
    [InlineData(1000, "475' Space")] // high above the surface line
    public void DepthMeterReadsLikeTheGame(float bottomY, string expected) =>
        Assert.Equal(expected, WorldCoordinates.Depth(bottomY, bottomY - WorldCoordinates.PlayerHeight / 2, Small));

    [Fact]
    public void SpaceStartsHigherInLargerWorlds()
    {
        var large = new WorldGeometry { Width = 8400, Height = 2400, WorldSurface = 300, RockLayer = 400 };

        // Center on row 145: (145 - 75) / (300 / 5) = 1.17 in a small world, (145 - 105) / 60 = 0.67 in a large one.
        Assert.Equal("Surface", WorldCoordinates.Layer(145 * 16 + 21, 145 * 16, Small));
        Assert.Equal("Space", WorldCoordinates.Layer(145 * 16 + 21, 145 * 16, large));
    }

    [Fact]
    public void ReadingsAtAPositionUseThePlayersCenterAndFeet()
    {
        // Top-left corner 10 px left of the center line, feet on the surface line.
        Assert.Equal("Center, Level Surface", WorldCoordinates.AtPosition(CenterX - 10, 300 * 16 - 42, Small));
        Assert.Equal("Center, Level Surface", WorldCoordinates.AtFeet(CenterX, 300 * 16, Small));
    }
}
