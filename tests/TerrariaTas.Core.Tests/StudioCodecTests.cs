using System.Collections.Generic;
using System.IO;
using System.Linq;
using TerrariaTas.Core.Movies;
using TerrariaTas.Core.Playback;
using TerrariaTas.Core.Studio;
using Xunit;

namespace TerrariaTas.Core.Tests;

public class StudioCodecTests
{
    [Fact]
    public void HelloRoundTrips()
    {
        var hello = RoundTrip(new Hello { TasVersion = "1.4.5.8-r0", TerrariaVersion = "1.4.5.8", MovieName = "run.tas" });

        Assert.Equal(StudioProtocol.Version, hello.ProtocolVersion);
        Assert.Equal("1.4.5.8-r0", hello.TasVersion);
        Assert.Equal("1.4.5.8", hello.TerrariaVersion);
        Assert.Equal("run.tas", hello.MovieName);
    }

    [Fact]
    public void MovieDataRoundTripsInputLines()
    {
        var lines = new List<InputLine>
        {
            new(30, new[] { Key.D, Key.Space }, MouseButtons.Left, CursorTarget.Tile(1520, 310), wheel: -2, sourceLine: 12),
            new(1, new Key[0], MouseButtons.None, null, wheel: 0, sourceLine: 13),
        };

        var data = RoundTrip(new MovieData { Lines = lines, BreakpointFrames = new[] { 30 } });

        Assert.Equal(2, data.Lines.Count);
        Assert.True(lines[0].HasSameInputs(data.Lines[0]));
        Assert.Equal(30, data.Lines[0].FrameCount);
        Assert.Equal(12, data.Lines[0].SourceLine);
        Assert.True(lines[1].HasSameInputs(data.Lines[1]));
        Assert.Equal(new[] { 30 }, data.BreakpointFrames);
        Assert.Equal(31, data.ApplyTo(Movie.Empty).TotalFrames);
    }

    [Fact]
    public void FrameStatusCarriesTheNextInputWhileRecording()
    {
        var status = RoundTrip(new FrameStatus { Recording = true, NextInput = new InputView { Keys = new[] { Key.D }, CursorX = 812, CursorY = 344, CursorTileX = 1520, CursorTileY = 310 } });

        Assert.Equal(new[] { Key.D }, status.NextInput!.Keys);
        Assert.Equal(812, status.NextInput.CursorX);
        Assert.Equal(310, status.NextInput.CursorTileY);
    }

    [Fact]
    public void WatchListsAndTheirValuesRoundTrip()
    {
        var list = RoundTrip(new WatchList { Version = 3, Paths = { "player.velocity.X", "Main.windSpeedCurrent" }, LogToFile = true });
        var status = RoundTrip(new FrameStatus { WatchVersion = 3, WatchValues = new[] { "3.18554688", "! nope" } });

        Assert.Equal(3, list.Version);
        Assert.Equal(new[] { "player.velocity.X", "Main.windSpeedCurrent" }, list.Paths);
        Assert.True(list.LogToFile);
        Assert.Equal(3, status.WatchVersion);
        Assert.Equal(new[] { "3.18554688", "! nope" }, status.WatchValues);
    }

    [Fact]
    public void HotkeyBindingsRoundTrip()
    {
        var map = TerrariaTas.Core.Hotkeys.HotkeyMap.Parse("FrameAdvance: V\nReloadMovie:\n");

        var bindings = RoundTrip(new HotkeyBindings { Map = map });

        Assert.Equal(map.Keys, bindings.Map.Keys);
        Assert.Equal(Key.V, bindings.Map.KeyOf(TerrariaTas.Core.Hotkeys.HotkeyAction.FrameAdvance));
        Assert.Null(bindings.Map.KeyOf(TerrariaTas.Core.Hotkeys.HotkeyAction.ReloadMovie));
    }

    [Fact]
    public void FrameStatusCarriesThePlayerInAWorldOnly()
    {
        var player = new PlayerView
        {
            PositionX = 25041.685546875f, PositionY = 4313.0957f, OldPositionX = 25038.5f, VelocityX = 3.1855469f, VelocityY = -5.0999985f,
            Direction = -1, GravityDirection = 1, Jump = 12, WingTime = 37.5f, DashDelay = -1, ImmuneTime = 40, FallStart = 270,
            SelectedItem = 3, ItemAnimation = 14, ItemTime = 13, Life = 380, Defense = 21, Breath = 200, Dead = true, RespawnTimer = 540,
            HeldItem = "Legendary Night's Edge",
            Buffs = { new BuffView { Type = 1, Name = "Obsidian Skin", Time = 14400 }, new BuffView { Type = 24, Name = "On Fire!", Time = 180 } },
        };

        var status = RoundTrip(new FrameStatus { Frame = 7, Player = player, Overlay = true });
        var inWorld = status.Player!;
        Assert.True(status.Overlay);
        var inMenus = RoundTrip(new FrameStatus { Frame = 8 });

        Assert.Equal(25041.685546875f, inWorld.PositionX);
        Assert.Equal(25038.5f, inWorld.OldPositionX);
        Assert.Equal(-5.0999985f, inWorld.VelocityY);
        Assert.Equal(-1, inWorld.Direction);
        Assert.Equal(37.5f, inWorld.WingTime);
        Assert.Equal(-1, inWorld.DashDelay);
        Assert.Equal(270, inWorld.FallStart);
        Assert.Equal(13, inWorld.ItemTime);
        Assert.True(inWorld.Dead);
        Assert.Equal(540, inWorld.RespawnTimer);
        Assert.Equal("Legendary Night's Edge", inWorld.HeldItem);
        Assert.Equal(new[] { "Obsidian Skin", "On Fire!" }, inWorld.Buffs.Select(buff => buff.Name));
        Assert.Equal(180, inWorld.Buffs[1].Time);
        Assert.Null(inMenus.Player);
    }

    [Fact]
    public void MovieDataReplacesTheLinesFromItsFirstLine()
    {
        var movie = MovieParser.Parse("10 D\n5 A\n5 S");
        var update = RoundTrip(new MovieData { FirstLine = 1, Lines = { new InputLine(3, new[] { Key.W }, MouseButtons.None, null, 0) } });

        var updated = update.ApplyTo(movie);

        Assert.Equal(13, updated.TotalFrames);
        Assert.Equal(new[] { Key.W }, updated.LineAt(10).Keys);
    }

    [Fact]
    public void FrameStatusRoundTrips()
    {
        var status = RoundTrip(new FrameStatus
        {
            Phase = SessionPhase.Running,
            Frame = 377,
            TotalFrames = 512,
            State = PlaybackState.FastForward,
            Recording = true,
            Slot = 3,
            SlotFrames = new[] { 0, -1, -1, 300, -1, -1, -1, -1, -1, -1 },
            Notice = "Saved slot 3 at frame 300",
            Input = new InputView
            {
                Keys = new[] { Key.A, Key.LeftShift },
                Buttons = MouseButtons.Right | MouseButtons.X2,
                CursorX = 640,
                CursorY = 360,
                CursorTileX = 2100,
                CursorTileY = 380,
            },
        });

        Assert.Equal(SessionPhase.Running, status.Phase);
        Assert.Equal(377, status.Frame);
        Assert.Equal(512, status.TotalFrames);
        Assert.Equal(PlaybackState.FastForward, status.State);
        Assert.True(status.Recording);
        Assert.Null(status.NextInput);
        Assert.Equal(3, status.Slot);
        Assert.Equal(300, status.SlotFrames[3]);
        Assert.Equal("Saved slot 3 at frame 300", status.Notice);
        Assert.Equal(new[] { Key.A, Key.LeftShift }, status.Input.Keys);
        Assert.Equal(MouseButtons.Right | MouseButtons.X2, status.Input.Buttons);
        Assert.Equal(2100, status.Input.CursorTileX);
        Assert.Equal(380, status.Input.CursorTileY);
    }

    [Fact]
    public void WorldSnapshotRoundTrips()
    {
        var snapshot = RoundTrip(new WorldSnapshot
        {
            Frame = 42,
            WorldName = "Forest",
            WorldSeed = -123,
            Player = new PlayerView { X = 100.5f, Y = 200, Life = 80, LifeMax = 100, Luck = 0.2f, HeldItem = "Copper Shortsword" },
            Npcs =
            {
                new NpcView
                {
                    Slot = 7, NetId = -3, TypeId = 1, Name = "Green Slime", Category = NpcCategory.Enemy,
                    Life = 14, LifeMax = 14, Distance = 12.5f, Copper = 25, FromStatue = true, Ai = new[] { 1f, 2f, 3f, 4f },
                },
            },
            Items = { new ItemView { Slot = 3, ItemId = 23, Name = "Gel", Stack = 2, Age = 90 } },
            Streams = { new RngStreamView { Name = "UpdateNPCs", Position = 123456, LastFrameDraws = 12, State = Enumerable.Range(0, 56).ToArray(), Index = 21 } },
        });

        Assert.Equal(42, snapshot.Frame);
        Assert.Equal(-123, snapshot.WorldSeed);
        Assert.Equal(0.2f, snapshot.Player.Luck);
        Assert.Equal("Copper Shortsword", snapshot.Player.HeldItem);
        var npc = Assert.Single(snapshot.Npcs);
        Assert.Equal(-3, npc.NetId);
        Assert.Equal("Green Slime", npc.Name);
        Assert.True(npc.FromStatue);
        Assert.Equal(new[] { 1f, 2f, 3f, 4f }, npc.Ai);
        Assert.Equal(2, Assert.Single(snapshot.Items).Stack);
        var stream = Assert.Single(snapshot.Streams);
        Assert.Equal(123456, stream.Position);
        Assert.Equal(21u, stream.Index);
        Assert.Equal(55, stream.State[55]);
    }

    [Fact]
    public void DropForecastRoundTrips()
    {
        var gel = new DropOutcome { Items = { new DroppedItem { ItemId = 23, Name = "Gel", Stack = 2 } }, Copper = 31 };
        var forecast = RoundTrip(new DropForecast
        {
            RequestId = 9,
            Frame = 100,
            NpcSlot = 7,
            NetId = -3,
            NpcName = "Green Slime",
            Luck = -0.1f,
            Notes = "boss",
            PossibleDrops = { new PossibleDrop { ItemId = 23, Name = "Gel", Chance = 1, StackMin = 1, StackMax = 2 } },
            Outcomes = { gel, new DropOutcome { NoLootReason = "statue" } },
            Columns = { new ForecastColumn { Stream = "UpdatePlayers", StartPosition = 5000, Cells = new[] { 0, 1, 0 } } },
        });

        Assert.Equal(9, forecast.RequestId);
        Assert.Equal(-0.1f, forecast.Luck);
        Assert.Equal("", forecast.Error);
        Assert.Equal("boss", forecast.Notes);
        Assert.Equal(2, Assert.Single(forecast.PossibleDrops).StackMax);
        Assert.Equal(gel.Key, forecast.Outcomes[0].Key);
        Assert.Equal("statue", forecast.Outcomes[1].NoLootReason);
        var column = Assert.Single(forecast.Columns);
        Assert.Equal(5000, column.StartPosition);
        Assert.Equal(new[] { 0, 1, 0 }, column.Cells);
    }

    [Fact]
    public void KillRecordAndRequestsRoundTrip()
    {
        var kill = RoundTrip(new KillRecord
        {
            Frame = 55, NpcSlot = 2, NetId = 1, NpcName = "Blue Slime", Stream = "UpdatePlayers", Offset = 317, Position = 9000,
            Drops = new DropOutcome { Items = { new DroppedItem { ItemId = 23, Name = "Gel", Stack = 1 } } },
        });
        var request = RoundTrip(new ForecastRequest { RequestId = 4, NpcSlot = 2, Rows = 1000, Streams = { "UpdateNPCs" } });
        var command = RoundTrip(new Command(CommandKind.RunToFrame, 250));

        Assert.Equal(317, kill.Offset);
        Assert.Equal("Gel", kill.Drops.Describe());
        Assert.Equal(1000, request.Rows);
        Assert.Equal("UpdateNPCs", Assert.Single(request.Streams));
        Assert.Equal(CommandKind.RunToFrame, command.Kind);
        Assert.Equal(250, command.Argument);
    }

    [Fact]
    public void PrefixNameRoundTrips()
    {
        var sword = new DroppedItem { ItemId = 1166, Name = "Keen Bone Sword", Stack = 1, Prefix = 14, PrefixName = "Keen" };

        var kill = RoundTrip(new KillRecord { Drops = new DropOutcome { Items = { sword } } });

        var item = Assert.Single(kill.Drops.Items);
        Assert.Equal(14, item.Prefix);
        Assert.Equal("Keen", item.PrefixName);
    }

    [Fact]
    public void ItemIconsRoundTrip()
    {
        var pixels = Enumerable.Range(0, 2 * 3 * 4).Select(i => (byte)i).ToArray();

        var icons = RoundTrip(new ItemIcons
        {
            Icons = { new ItemIcon { ItemId = 23, Width = 2, Height = 3, Rgba = pixels }, new ItemIcon { ItemId = 9999 } },
        });

        Assert.Equal(2, icons.Icons.Count);
        Assert.Equal(23, icons.Icons[0].ItemId);
        Assert.Equal(3, icons.Icons[0].Height);
        Assert.Equal(pixels, icons.Icons[0].Rgba);
        Assert.Equal(0, icons.Icons[1].Width);
        Assert.Empty(icons.Icons[1].Rgba);
    }

    [Fact]
    public void IconWithTheWrongPixelCountIsAnError()
    {
        var payload = StudioCodec.Encode(new ItemIcons { Icons = { new ItemIcon { ItemId = 23, Width = 2, Height = 2, Rgba = new byte[4] } } });

        Assert.Throws<InvalidDataException>(() => StudioCodec.Decode(payload));
    }

    [Fact]
    public void FramesSurviveAStream()
    {
        using var pipe = new MemoryStream();
        StudioCodec.WriteFrame(pipe, StudioCodec.Encode(new Command(CommandKind.SaveSlot, 4)));
        StudioCodec.WriteFrame(pipe, StudioCodec.Encode(new Hello { MovieName = "a.tas" }));
        pipe.Position = 0;

        var first = Assert.IsType<Command>(StudioCodec.Decode(StudioCodec.ReadFrame(pipe)!));
        var second = Assert.IsType<Hello>(StudioCodec.Decode(StudioCodec.ReadFrame(pipe)!));

        Assert.Equal(4, first.Argument);
        Assert.Equal("a.tas", second.MovieName);
        Assert.Null(StudioCodec.ReadFrame(pipe));
    }

    [Fact]
    public void TruncatedFrameIsAnError()
    {
        using var pipe = new MemoryStream();
        StudioCodec.WriteFrame(pipe, StudioCodec.Encode(new Hello()));
        var truncated = new MemoryStream(pipe.ToArray().Take((int)pipe.Length - 1).ToArray());

        Assert.Throws<EndOfStreamException>(() => StudioCodec.ReadFrame(truncated));
    }

    [Theory]
    [InlineData(0, "0c")]
    [InlineData(25, "25c")]
    [InlineData(150, "1s 50c")]
    [InlineData(1_000_000, "1p")]
    [InlineData(1_020_304, "1p 2g 3s 4c")]
    public void CoinsAreFormattedByDenomination(long copper, string expected) => Assert.Equal(expected, Coins.Format(copper));

    [Fact]
    public void OutcomeDescriptionListsItemsThenCoins()
    {
        var outcome = new DropOutcome
        {
            Items = { new DroppedItem { ItemId = 23, Name = "Gel", Stack = 2 }, new DroppedItem { ItemId = 1, Name = "Keen Bone Sword", Stack = 1 } },
            Copper = 130,
        };

        Assert.Equal("Gel ×2, Keen Bone Sword, 1s 30c", outcome.Describe());
        Assert.Equal("nothing", new DropOutcome().Describe());
    }

    private static T RoundTrip<T>(T message) where T : StudioMessage =>
        Assert.IsType<T>(StudioCodec.Decode(StudioCodec.Encode(message)));
}
