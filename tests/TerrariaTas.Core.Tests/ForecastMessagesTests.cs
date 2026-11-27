using TerrariaTas.Core.Studio;
using TerrariaTas.Core.World;
using Xunit;

namespace TerrariaTas.Core.Tests;

public class ForecastMessagesTests
{
    private static readonly WorldGeometry Small = new() { Width = 4200, Height = 1200, WorldSurface = 300.5, RockLayer = 410.25, SpawnX = 2100, SpawnY = 280, BorderTiles = 40 };

    [Fact]
    public void TeleportRequestsAndForecastsRoundTrip()
    {
        var request = Codec.RoundTrip(new TeleportRequest { RequestId = 3, Kind = TeleportKind.Potion, Rows = 1000, Streams = { "UpdatePlayers" } });
        var forecast = Codec.RoundTrip(new TeleportForecast
        {
            RequestId = 3,
            Frame = 812,
            World = Small,
            PlayerX = 33590.25f,
            PlayerY = 4758f,
            RollFrame = 820,
            Milliseconds = 85,
            Notes = "notes",
            Columns =
            {
                new TeleportColumn
                {
                    Stream = "UpdatePlayers",
                    StartPosition = 123_456,
                    Destinations = { new TeleportDestination { X = 1598, Y = 6358, Found = true, Draws = 6 }, new TeleportDestination { Draws = 2000 } },
                },
            },
        });

        Assert.Equal(1000, request.Rows);
        Assert.Equal(new[] { "UpdatePlayers" }, request.Streams);
        Assert.Equal(820, forecast.RollFrame);
        Assert.Equal(85, forecast.Milliseconds);
        Assert.Equal(33590.25f, forecast.PlayerX);
        Assert.Equal(410.25, forecast.World.RockLayer);
        Assert.Equal(40, forecast.World.BorderTiles);
        var column = Assert.Single(forecast.Columns);
        Assert.Equal(123_456, column.StartPosition);
        Assert.Equal(2, column.Destinations.Count);
        Assert.True(column.Destinations[0].Found);
        Assert.Equal(6, column.Destinations[0].Draws);
        Assert.False(column.Destinations[1].Found);
    }

    [Fact]
    public void ADestinationNamesTheTileThePlayerStandsOn()
    {
        // As the game places a 20 × 42 player on tile (100, 400): x = 100 * 16 - 10 + 8, feet on row 400's top edge.
        var destination = new TeleportDestination { X = 100 * 16 - 2, Y = 400 * 16 - 42, Found = true };

        Assert.Equal(100, destination.TileX);
        Assert.Equal(400, destination.TileY);
    }

    [Fact]
    public void TeleportRecordsRoundTrip()
    {
        var record = Codec.RoundTrip(new TeleportRecord { Frame = 820, Stream = "UpdatePlayers", Offset = 14, Position = 123_470, FromX = 1, FromY = 2, ToX = 1598, ToY = 6358 });

        Assert.Equal(14, record.Offset);
        Assert.Equal(123_470, record.Position);
        Assert.Equal(1598, record.ToX);
        Assert.Equal(6358, record.ToY);
    }

    [Fact]
    public void DamageRequestsAndForecastsRoundTrip()
    {
        var request = Codec.RoundTrip(new DamageRequest { RequestId = 9, Rows = 500, TargetSlot = 4, BaseDamage = -1, CritChance = 50 });
        var forecast = Codec.RoundTrip(new DamageForecast
        {
            RequestId = 9,
            Frame = 300,
            ItemId = 3507,
            ItemName = "Legendary Copper Shortsword",
            Luck = 0.1f,
            TargetSlot = 4,
            TargetNetId = 1,
            TargetName = "Blue Slime",
            TargetLife = 25,
            TargetDefense = 4,
            Columns =
            {
                new DamageColumn
                {
                    Kind = HitKind.Swing,
                    Stream = "UpdatePlayers",
                    StartPosition = 77,
                    Source = "Legendary Copper Shortsword",
                    SourceId = 3507,
                    Overridden = true,
                    BaseDamage = 7,
                    CritChances = new[] { 4 },
                    Rolls = { new DamageRoll { Crit = true, Damage = 8, Dealt = 12, Draws = 3 }, new DamageRoll { Damage = 6 } },
                },
            },
        });

        Assert.Equal(4, request.TargetSlot);
        Assert.Equal(-1, request.BaseDamage);
        Assert.Equal(50, request.CritChance);
        Assert.Equal("Blue Slime", forecast.TargetName);
        Assert.Equal(25, forecast.TargetLife);
        var column = Assert.Single(forecast.Columns);
        Assert.Equal(HitKind.Swing, column.Kind);
        Assert.Equal(3507, column.SourceId);
        Assert.True(column.Overridden);
        Assert.Equal(new[] { 4 }, column.CritChances);
        Assert.True(column.Rolls[0].Crit);
        Assert.Equal(12, column.Rolls[0].Dealt);
        Assert.Equal(-1, column.Rolls[1].Dealt);
    }

    [Fact]
    public void HitRecordsRoundTrip()
    {
        var hit = Codec.RoundTrip(new HitRecord
        {
            Frame = 301,
            NpcSlot = 4,
            NetId = 1,
            NpcName = "Blue Slime",
            Kind = HitKind.Projectile,
            Source = "Wooden Arrow",
            SourceId = 1,
            Stream = "UpdateProjectiles",
            Offset = 40,
            Crit = true,
            Damage = 9,
            Dealt = 14,
        });

        Assert.Equal(HitKind.Projectile, hit.Kind);
        Assert.Equal("Wooden Arrow", hit.Source);
        Assert.Equal(1, hit.SourceId);
        Assert.Equal(40, hit.Offset);
        Assert.True(hit.Crit);
        Assert.Equal(14, hit.Dealt);
    }

    [Fact]
    public void MapRequestsAndHeadersRoundTrip()
    {
        var request = Codec.RoundTrip(new MapRequest { RequestId = 5 });
        var header = Codec.RoundTrip(new MapHeader { RequestId = 5, Frame = 60, WorldName = "Smelly Afterworld", World = Small, Error = "" });
        var failed = Codec.RoundTrip(new MapHeader { RequestId = 5, Error = "Reading the map failed: boom" });

        Assert.Equal(5, request.RequestId);
        Assert.Equal("Smelly Afterworld", header.WorldName);
        Assert.Equal(4200, header.World.Width);
        Assert.Equal(1000, header.World.UnderworldLayer);
        Assert.Equal(2100, header.World.SpawnX);
        Assert.Equal("Reading the map failed: boom", failed.Error);
    }
}
