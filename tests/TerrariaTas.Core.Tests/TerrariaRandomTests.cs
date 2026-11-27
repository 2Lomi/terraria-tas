using System;
using TerrariaTas.Core.Rng;
using Xunit;

namespace TerrariaTas.Core.Tests;

/// <summary>
/// Terraria's UnifiedRandom uses the same algorithm as seeded System.Random, so .NET's
/// implementation is a reference that does not require the game.
/// </summary>
public class TerrariaRandomTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-123456)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void MatchesSeededSystemRandom(int seed)
    {
        var expected = new Random(seed);
        var actual = new TerrariaRandom(seed);

        for (var i = 0; i < 2000; i++)
        {
            switch (i % 5)
            {
                case 0: Assert.Equal(expected.Next(), actual.Next()); break;
                case 1: Assert.Equal(expected.Next(100), actual.Next(100)); break;
                case 2: Assert.Equal(expected.Next(-15, 16), actual.Next(-15, 16)); break;
                case 3: Assert.Equal(expected.Next(int.MinValue, int.MaxValue), actual.Next(int.MinValue, int.MaxValue)); break;
                default: Assert.Equal(expected.NextDouble(), actual.NextDouble()); break;
            }
        }
    }

    [Fact]
    public void ResumesExactlyFromCopiedState()
    {
        var original = new TerrariaRandom(99);
        for (var i = 0; i < 77; i++)
            original.Next();

        var copy = TerrariaRandom.FromState(original.CopyState(), original.Index);

        for (var i = 0; i < 500; i++)
            Assert.Equal(original.Next(1000), copy.Next(1000));
    }

    [Fact]
    public void DrawsUntilCountsRawSamples()
    {
        var start = new TerrariaRandom(1234);
        var target = start.Clone();
        for (var i = 0; i < 1000; i++)
            target.Next(100);
        for (var i = 0; i < 10; i++)
            target.Next(int.MinValue, int.MaxValue); // a range wider than int.MaxValue takes two samples

        Assert.Equal(1020, start.DrawsUntil(target, maxDraws: 100_000));
        Assert.Equal(0, target.DrawsUntil(target, maxDraws: 0));
    }

    [Fact]
    public void DrawsUntilGivesUpAfterTheLimitAndKeepsTheGenerator()
    {
        var start = new TerrariaRandom(7);
        var target = start.Clone();
        for (var i = 0; i < 500; i++)
            target.Next();
        var untouched = start.Clone();

        Assert.Equal(-1, start.DrawsUntil(target, maxDraws: 499));
        Assert.Equal(500, start.DrawsUntil(target, maxDraws: 500));
        Assert.Equal(untouched.Next(), start.Next());
    }

    [Fact]
    public void DrawsUntilFindsAPositionFromTheSeed()
    {
        // How the runtime finds a stream's position: every stream starts from a seed it knows.
        var stream = new TerrariaRandom(-987654);
        for (var i = 0; i < 123_456; i++)
            stream.NextDouble();

        Assert.Equal(123_456, new TerrariaRandom(-987654).DrawsUntil(stream.CopyState(), stream.Index, maxDraws: 1_000_000));
    }

    [Fact]
    public void PeekDoesNotAdvance()
    {
        var random = new TerrariaRandom(5);
        var clone = random.Clone();

        random.Peek();

        Assert.Equal(clone.Next(), random.Next());
    }
}
