using System;
using System.IO;
using TerrariaTas.Core.Launch;
using TerrariaTas.Core.Movies;
using Xunit;

namespace TerrariaTas.Core.Tests;

/// <summary>The checksums that pin a movie to its player and world files, and the command line of the creation launch.</summary>
public sealed class StartFileChecksumTests
{
    private const string PlayerSum = "3f2a9c0b1d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8";
    private const string WorldSum = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

    [Fact]
    public void ChecksumsAreSha256OfTheFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "abc");
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", FileChecksum.Of(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheHeaderReadsAndWritesChecksums()
    {
        var movie = MovieParser.Parse($"Player: a.plr\nPlayerChecksum: {PlayerSum.ToUpperInvariant()}\nWorld: b.wld\nWorldChecksum: {WorldSum}\n\n5 D\n");
        Assert.Equal(PlayerSum, movie.Header.PlayerChecksum);
        Assert.Equal(WorldSum, movie.Header.WorldChecksum);

        var text = MovieWriter.Write(movie);
        Assert.Contains($"Player: a.plr\nPlayerChecksum: {PlayerSum}\nWorld: b.wld\nWorldChecksum: {WorldSum}\n", text);
        Assert.Equal(PlayerSum, MovieParser.Parse(text).Header.PlayerChecksum);

        var other = MovieParser.Parse($"Player: a.plr\nWorld: b.wld\nWorldChecksum: {WorldSum}\n");
        Assert.Equal(new[] { "PlayerChecksum" }, movie.Header.DifferencesFrom(other.Header));
    }

    [Theory]
    [InlineData("PlayerChecksum: 1234")]
    [InlineData("WorldChecksum: zz112233445566778899aabbccddeeff00112233445566778899aabbccddeeff")]
    public void MalformedChecksumsAreRefused(string line)
    {
        var error = Assert.Throws<MovieFormatException>(() => MovieParser.Parse(line));
        Assert.Contains("64 hexadecimal digits of a SHA-256", error.Reason);
    }

    [Fact]
    public void ChecksumsGoUnderTheirLinesAndTheRestStaysAsItWas()
    {
        const string text = "# My run\nPlayer: a.plr   # the hero\nWorld: b.wld\nSeed: 3\n\n  10 D   # walk\n***\n";
        var pinned = MovieHeaderText.AddChecksums(text, PlayerSum, WorldSum);
        Assert.Equal($"# My run\nPlayer: a.plr   # the hero\nPlayerChecksum: {PlayerSum}\nWorld: b.wld\nWorldChecksum: {WorldSum}\nSeed: 3\n\n  10 D   # walk\n***\n", pinned);

        var movie = MovieParser.Parse(pinned);
        Assert.Equal(PlayerSum, movie.Header.PlayerChecksum);
        Assert.Equal(10, movie.TotalFrames);
    }

    [Fact]
    public void OnlyMissingChecksumsAreAdded()
    {
        var text = $"Player: a.plr\nPlayerChecksum: {PlayerSum}\nWorld: b.wld\n";
        Assert.Equal($"Player: a.plr\nPlayerChecksum: {PlayerSum}\nWorld: b.wld\nWorldChecksum: {WorldSum}\n",
            MovieHeaderText.AddChecksums(text, WorldSum, WorldSum));
        Assert.Equal(text, MovieHeaderText.AddChecksums(text, PlayerSum, null));
    }

    [Fact]
    public void WindowsLineEndingsAndAHeaderEndingTheFileAreKept()
    {
        Assert.Equal($"Player: a.plr\r\nPlayerChecksum: {PlayerSum}\r\nWorld: b.wld\r\nWorldChecksum: {WorldSum}",
            MovieHeaderText.AddChecksums("Player: a.plr\r\nWorld: b.wld", PlayerSum, WorldSum));
        Assert.Equal($"World: b.wld\nWorldChecksum: {WorldSum}", MovieHeaderText.AddChecksums("World: b.wld", null, WorldSum));
    }

    [Fact]
    public void InputLinesAreNotTheHeader()
    {
        const string text = "Seed: 1\n\n10 D\n# Player: x.plr\n";
        Assert.Equal(text, MovieHeaderText.AddChecksums(text, PlayerSum, WorldSum));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData(@"C:\My Games\Terraria TAS", "\"C:\\My Games\\Terraria TAS\"")]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("", "\"\"")]
    public void ArgumentsAreQuotedTheWayWindowsSplitsThem(string argument, string quoted)
    {
        Assert.Equal(quoted, CommandLineText.Quote(argument));
    }

    [Fact]
    public void ACommandLineJoinsQuotedArguments()
    {
        var line = CommandLineText.Join(new[] { "--create", @"C:\movies\my run.tas", "--seed", "7" });
        Assert.Equal("--create \"C:\\movies\\my run.tas\" --seed 7", line);
    }
}
