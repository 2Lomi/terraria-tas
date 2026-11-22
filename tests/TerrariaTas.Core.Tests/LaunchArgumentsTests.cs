using System;
using TerrariaTas.Core.Launch;
using Xunit;

namespace TerrariaTas.Core.Tests;

public sealed class LaunchArgumentsTests
{
    private static LaunchArguments Parse(params string[] args) => LaunchArguments.Parse(args);

    private static string Error(params string[] args) => Assert.Throws<ArgumentException>(() => LaunchArguments.Parse(args)).Message;

    [Fact]
    public void AMovieAlonePlaysAndNothingIsFreePlay()
    {
        var play = Parse("my-run");
        Assert.Equal(LaunchMode.Play, play.Mode);
        Assert.Equal("my-run", play.Movie);

        Assert.Equal(LaunchMode.FreePlay, Parse().Mode);
        Assert.Null(Parse().Movie);
    }

    [Theory]
    [InlineData("--edit", LaunchMode.Edit)]
    [InlineData("--verify", LaunchMode.Verify)]
    public void ModesTakeTheMovieOnEitherSide(string flag, LaunchMode mode)
    {
        Assert.Equal(mode, Parse("my-run", flag).Mode);
        Assert.Equal(mode, Parse(flag, "my-run").Mode);
        Assert.Equal("my-run", Parse(flag, "my-run").Movie);
    }

    [Fact]
    public void EditNeedsAMovie()
    {
        Assert.Contains("needs the movie", Error("--edit"));
        Assert.Contains("needs the movie", Error("--verify"));
    }

    [Fact]
    public void NewDefaultsToASmallCorruptionWorldInNormalDifficulty()
    {
        var arguments = Parse("--new");
        Assert.Equal(LaunchMode.New, arguments.Mode);
        Assert.Null(arguments.Movie);
        Assert.Equal(WorldSize.Small, arguments.NewMovie.Size);
        Assert.Equal(WorldEvil.Corruption, arguments.NewMovie.Evil);
        Assert.Equal(WorldDifficulty.Normal, arguments.NewMovie.Difficulty);
        Assert.Null(arguments.NewMovie.Seed);
        Assert.Equal("small corruption world, normal difficulty", arguments.NewMovie.DescribeWorld());
    }

    [Fact]
    public void NewTakesItsNameAfterItOrBeforeIt()
    {
        Assert.Equal("speedy", Parse("--new", "speedy").Movie);
        Assert.Equal("speedy", Parse("speedy", "--new").Movie);
        Assert.Equal("speedy", Parse("--new", "speedy", "--crimson").Movie);
        Assert.Null(Parse("--new", "--crimson").Movie);
        Assert.Contains("Two movies", Error("one", "--new", "two"));
    }

    [Fact]
    public void ShortFlagsAndLongOptionsSayTheSame()
    {
        var shortFlags = Parse("--new", "--crimson", "--master", "--large", "--seed", "42").NewMovie;
        var longOptions = Parse("--new", "--evil", "crimson", "--difficulty", "Master", "--size", "LARGE", "--seed", "42").NewMovie;
        foreach (var options in new[] { shortFlags, longOptions })
        {
            Assert.Equal(WorldEvil.Crimson, options.Evil);
            Assert.Equal(WorldDifficulty.Master, options.Difficulty);
            Assert.Equal(WorldSize.Large, options.Size);
            Assert.Equal(42, options.Seed);
        }

        Assert.Equal(WorldEvil.Crimson, Parse("--new", "--type", "crimson").NewMovie.Evil);
        Assert.Equal(WorldDifficulty.Normal, Parse("--new", "--classic").NewMovie.Difficulty);
        Assert.Equal(WorldDifficulty.Journey, Parse("--new", "--journey").NewMovie.Difficulty);
    }

    [Fact]
    public void ContradictoryChoicesAreRefusedButRepeatsAreFine()
    {
        Assert.Contains("choose one world evil", Error("--new", "--crimson", "--corruption"));
        Assert.Contains("choose one difficulty", Error("--new", "--expert", "--difficulty", "master"));
        Assert.Contains("choose one world size", Error("--new", "--small", "--large"));
        Assert.Equal(WorldEvil.Crimson, Parse("--new", "--crimson", "--evil", "crimson").NewMovie.Evil);
    }

    [Theory]
    [InlineData("--evil", "hallow", "corruption or crimson")]
    [InlineData("--difficulty", "hard", "journey, normal, expert or master")]
    [InlineData("--size", "huge", "small, medium or large")]
    [InlineData("--size", "1", "small, medium or large")]
    [InlineData("--seed", "-5", "whole number")]
    [InlineData("--seed", "abc", "whole number")]
    public void BadValuesSayWhatIsExpected(string option, string value, string expected)
    {
        Assert.Contains(expected, Error("--new", option, value));
    }

    [Theory]
    [InlineData("--crimson")]
    [InlineData("--master")]
    [InlineData("--large")]
    [InlineData("--seed", "1")]
    [InlineData("--player", "a.plr")]
    [InlineData("--world", "a.wld")]
    public void CreationChoicesOnlyGoWithNew(params string[] option)
    {
        var args = new string[option.Length + 1];
        args[0] = "my-run";
        option.CopyTo(args, 1);
        Assert.Contains("only goes with --new", Error(args));
    }

    [Fact]
    public void AnExistingWorldKeepsItsOwnSettings()
    {
        var arguments = Parse("--new", "--world", "Forest.wld", "--player", "Runner.plr");
        Assert.Equal("Forest.wld", arguments.NewMovie.World);
        Assert.Equal("Runner.plr", arguments.NewMovie.Player);

        Assert.Contains("--crimson does not apply", Error("--new", "--world", "Forest.wld", "--crimson"));
        Assert.Contains("--seed does not apply", Error("--new", "--world", "Forest.wld", "--seed", "3"));
        Assert.Contains("not a path", Error("--new", "--player", @"C:\saves\Runner.plr"));
    }

    [Fact]
    public void ModesDoNotMix()
    {
        Assert.Contains("cannot go together", Error("my-run", "--edit", "--verify"));
        Assert.Contains("cannot go together", Error("--new", "--list"));
        Assert.Contains("lists every movie", Error("--list", "my-run"));
        Assert.Equal(LaunchMode.Edit, Parse("my-run", "--edit", "--edit").Mode);
    }

    [Fact]
    public void FoldersAndHelp()
    {
        var arguments = Parse("--list", "--movies", "D:/tas", "--data", "D:/data", "--terraria", "D:/game", "--ignore-version");
        Assert.Equal(LaunchMode.List, arguments.Mode);
        Assert.Equal("D:/tas", arguments.MoviesDir);
        Assert.Equal("D:/data", arguments.DataDir);
        Assert.Equal("D:/game", arguments.TerrariaDir);
        Assert.True(arguments.IgnoreVersion);

        Assert.True(Parse("--help").Help);
        Assert.True(Parse("my-run", "-h", "--bogus").Help);
        Assert.Contains("needs a value", Error("--data"));
        Assert.Contains("needs a value", Error("--data", "--list"));
    }

    [Fact]
    public void UnknownAndRetiredFlags()
    {
        Assert.Contains("Unexpected argument '--fast'", Error("--fast"));
        Assert.Contains("a movie given alone plays", Error("my-run", "--play"));
    }

    [Fact]
    public void TheCreationLaunchTakesAPath()
    {
        var arguments = Parse("--create", @"C:\movies\run.tas", "--size", "medium", "--evil", "crimson", "--difficulty", "journey", "--seed", "7");
        Assert.Equal(LaunchMode.Create, arguments.Mode);
        Assert.Equal(@"C:\movies\run.tas", arguments.Movie);
        Assert.Equal(WorldSize.Medium, arguments.NewMovie.Size);
        Assert.Equal(WorldDifficulty.Journey, arguments.NewMovie.Difficulty);
        Assert.Equal(7, arguments.NewMovie.Seed);
    }
}
