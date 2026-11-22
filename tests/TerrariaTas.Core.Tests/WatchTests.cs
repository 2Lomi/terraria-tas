using System;
using System.Collections.Generic;
using TerrariaTas.Core.Watch;
using Xunit;

namespace TerrariaTas.Core.Tests;

public class WatchTests
{
    private static readonly WatchReader Reader = new(
        name => name is "Game" or "My.Full.Game" ? typeof(Game) : null,
        new Dictionary<string, Func<object?>> { ["thing"] = () => Game.things[0] });

    private static string Read(string path) => Reader.Read(WatchPath.Parse(path));

    [Theory]
    [InlineData("Game.wind", "0.100000001")]
    [InlineData("My.Full.Game.wind", "0.100000001")]
    [InlineData("Game.secret", "7")]
    [InlineData("Game.things.Length", "2")]
    [InlineData("Game.things[0].position.X", "1.5")]
    [InlineData("Game.things[0].position", "(1.5, -2)")]
    [InlineData("Game.things[0].box", "{X=1, Y=2, Width=3, Height=4}")]
    [InlineData("Game.things[0].grid[1,2]", "6")]
    [InlineData("Game.things[0].list[1]", "5")]
    [InlineData("Game.things[0].list.Count", "2")]
    [InlineData("Game.things[0].Life()", "99")]
    [InlineData("Game.things[0].name", "\"slime\"")]
    [InlineData("Game.things[0].mode", "Angry")]
    [InlineData("Game.things[0]", "<Thing>")]
    [InlineData("thing.position.Y", "-2")]
    [InlineData("thing.inherited", "3")]
    public void PathsReadTheValuesTheyName(string path, string value) => Assert.Equal(value, Read(path));

    [Theory]
    [InlineData("Game.things[1].position", "Game.things[1] is null")]
    [InlineData("Game.things[5]", "Game.things has 2 elements: no [5]")]
    [InlineData("Game.things[0].grid[1]", "has 2 dimensions: write [i,i]")]
    [InlineData("Game.things[0].Life", "Life is a property: reading it runs game code")]
    [InlineData("Game.things[0].nope", "has no field or property 'nope'")]
    [InlineData("Game.nope", "has no static field or property 'nope'")]
    [InlineData("Nope.x", "'Nope' is not a class watches can start from or thing")]
    [InlineData("Game", "Game is a type: name one of its fields")]
    [InlineData("Game.wind[0]", "(Single) cannot be indexed")]
    public void PathsThatLeadNowhereSayWhere(string path, string message)
    {
        var value = Read(path);

        Assert.StartsWith("! ", value);
        Assert.Contains(message, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Main..x")]
    [InlineData("Main.")]
    [InlineData("[0].x")]
    [InlineData("Main.player[a]")]
    [InlineData("Main.player[0")]
    [InlineData("Main.player[0] + 1")]
    public void MalformedPathsAreRefused(string path) => Assert.Throws<FormatException>(() => WatchPath.Parse(path));

    [Fact]
    public void PathsWriteBackAsTheyRead() =>
        Assert.Equal("Main.player[0].velocity.X", WatchPath.Parse(" Main.player[0].velocity.X ").ToString());

    private enum Mood
    {
        Calm,
        Angry,
    }

    private static class Game
    {
#pragma warning disable CS0414, CS0649 // read through reflection
        public static float wind = 0.1f;
        public static Thing?[] things = { new(), null };
        private static int secret = 7;
#pragma warning restore CS0414, CS0649
    }

    private class Base
    {
        public int inherited = 3;
    }

    private sealed class Thing : Base
    {
        public Vec2 position = new() { X = 1.5f, Y = -2f };
        public Box box = new() { X = 1, Y = 2, Width = 3, Height = 4 };
        public int[,] grid = { { 1, 2, 3 }, { 4, 5, 6 } };
        public List<int> list = new() { 4, 5 };
        public string name = "slime";
        public Mood mode = Mood.Angry;

        public int Life => 99;
    }

    private struct Vec2
    {
        public float X;
        public float Y;
    }

    private struct Box
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }
}
