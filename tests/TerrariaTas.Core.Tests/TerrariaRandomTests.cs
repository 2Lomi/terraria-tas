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
    public void PeekDoesNotAdvance()
    {
        var random = new TerrariaRandom(5);
        var clone = random.Clone();

        random.Peek();

        Assert.Equal(clone.Next(), random.Next());
    }
}
