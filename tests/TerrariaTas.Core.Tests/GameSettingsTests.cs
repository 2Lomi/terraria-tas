using System.Linq;
using TerrariaTas.Core.Movies;
using Xunit;

namespace TerrariaTas.Core.Tests;

public class GameSettingsTests
{
    private static GameSetting Setting(string name) => GameSettings.Find(name)!;

    [Fact]
    public void TheHeaderReadsSettingsAndWritesThemInCanonicalForm()
    {
        var movie = MovieParser.Parse("Zoom: 1.50\nbackground: Off\nfavoritekey: leftcontrol\nUIScale: AUTO\n\n10 D");

        Assert.Equal("1.5", movie.Header.ValueOf(Setting("Zoom")));
        Assert.Equal(GameSetting.Auto, movie.Header.ValueOf(Setting("UIScale")));
        Assert.Equal("off", movie.Header.ValueOf(Setting("Background")));
        Assert.Equal("LeftControl", movie.Header.ValueOf(Setting("FavoriteKey")));
    }

    [Fact]
    public void ASettingTheMovieDoesNotStateIsAFreshInstallsValue()
    {
        var header = MovieParser.Parse("10 D").Header;

        Assert.Equal(GameSetting.Auto, header.ValueOf(Setting("UIScale")));
        Assert.Equal("1", header.ValueOf(Setting("Zoom")));
        Assert.Equal("on", header.ValueOf(Setting("BloodAndGore")));
        Assert.Equal("off", header.ValueOf(Setting("Autoswing")));
    }

    [Fact]
    public void AValueOutsideTheAllowedOnesIsAnError()
    {
        var error = Assert.Throws<MovieFormatException>(() => MovieParser.Parse("Zoom: 3\n10 D"));

        Assert.Equal(1, error.LineNumber);
        Assert.Contains("1 to 2", error.Reason);
        Assert.Throws<MovieFormatException>(() => MovieParser.Parse("Zoom: auto\n10 D"));
        Assert.Throws<MovieFormatException>(() => MovieParser.Parse("Crafting: fancy\n10 D"));
    }

    [Fact]
    public void AnUnknownPropertyListsTheSettingsToo()
    {
        var error = Assert.Throws<MovieFormatException>(() => MovieParser.Parse("Zoooom: 120\n10 D"));

        Assert.Contains("SwayInWind", error.Reason);
    }

    [Fact]
    public void SettingsSurviveWritingAndReading()
    {
        var movie = MovieParser.Parse("Seed: 4\nAutoswing: on\nUIScale: 0.733333349\n\n10 D");

        var text = MovieWriter.Write(movie);
        var reread = MovieParser.Parse(text);

        Assert.Contains("UIScale: 0.733333349\nAutoswing: on\n", text); // in the table's order, after the other header lines
        Assert.Equal(720f / 1080f * 1.1f, float.Parse(reread.Header.ValueOf(Setting("UIScale")), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(movie.Header.Settings.OrderBy(s => s.Key), reread.Header.Settings.OrderBy(s => s.Key));
    }

    [Fact]
    public void EveryDefaultIsAnAllowedValue()
    {
        foreach (var setting in GameSettings.All)
            Assert.Equal(setting.Default, setting.Normalize(setting.Default));

        Assert.Equal(GameSettings.All.Count, GameSettings.All.Select(s => s.Name.ToLowerInvariant()).Distinct().Count());
    }
}
