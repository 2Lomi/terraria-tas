using System.Linq;
using TerrariaTas.Core.Movies;
using Xunit;

namespace TerrariaTas.Core.Tests;

public class KeyBindingsTests
{
    private static KeyBinding Action(string name) => KeyBindings.Find(name)!;

    [Fact]
    public void TheHeaderReadsBindingsInCanonicalForm()
    {
        var header = MovieParser.Parse("bind jump: space w space\nBind MouseLeft: lmb\nBind  Throw :\n\n10 D").Header;

        Assert.Equal(new[] { "Space", "W" }, header.InputsOf(Action("Jump")));
        Assert.Equal(new[] { "LMB" }, header.InputsOf(Action("MouseLeft")));
        Assert.Empty(header.InputsOf(Action("Throw")));
    }

    [Fact]
    public void AnActionTheMovieDoesNotBindKeepsAFreshInstallsKeys()
    {
        var header = MovieParser.Parse("Bind Jump: W\n10 D").Header;

        Assert.Equal(new[] { "E" }, header.InputsOf(Action("Grapple")));
        Assert.Equal(new[] { "D0" }, header.InputsOf(Action("Hotbar10")));
        Assert.Equal(new[] { "RMB" }, header.InputsOf(Action("MouseRight")));
        Assert.Empty(header.InputsOf(Action("Dash")));
    }

    [Fact]
    public void UnknownActionsAndKeysAreErrors()
    {
        var action = Assert.Throws<MovieFormatException>(() => MovieParser.Parse("Bind Jmup: Space\n10 D"));
        Assert.Equal(1, action.LineNumber);
        Assert.Contains("Grapple", action.Reason);

        var key = Assert.Throws<MovieFormatException>(() => MovieParser.Parse("Seed: 1\nBind Jump: Spcae\n10 D"));
        Assert.Equal(2, key.LineNumber);
        Assert.Contains("Spcae", key.Reason);

        Assert.Throws<MovieFormatException>(() => MovieParser.Parse("Bind: Space\n10 D"));
        Assert.Throws<MovieFormatException>(() => MovieParser.Parse("Bind Jump Now: Space\n10 D"));
        Assert.Throws<MovieFormatException>(() => MovieParser.Parse("Bind Jump: WheelUp\n10 D"));
    }

    [Fact]
    public void BindingsSurviveWritingAndReading()
    {
        var movie = MovieParser.Parse("Bind Throw:\nAutoswing: on\nBind Jump: space W\n\n10 D");

        var text = MovieWriter.Write(movie);
        var reread = MovieParser.Parse(text).Header;

        Assert.Contains("Autoswing: on\n", text);
        Assert.Contains("Bind Jump: Space W\nBind Throw:\n", text); // in the game's order, after the settings
        Assert.True(text.IndexOf("Autoswing", System.StringComparison.Ordinal) < text.IndexOf("Bind Jump", System.StringComparison.Ordinal));
        Assert.Equal(new[] { "Space", "W" }, reread.InputsOf(Action("Jump")));
        Assert.Empty(reread.InputsOf(Action("Throw")));
        Assert.Equal(2, reread.Bindings.Count);
    }

    [Fact]
    public void EveryDefaultIsWrittenAsMoviesWriteInputs()
    {
        foreach (var binding in KeyBindings.All)
        {
            foreach (var input in binding.Default)
                Assert.Equal(input, KeyBindings.NormalizeInput(input));
        }

        Assert.Equal(60, KeyBindings.All.Count);
        Assert.Equal(KeyBindings.All.Count, KeyBindings.All.Select(b => b.Name.ToLowerInvariant()).Distinct().Count());
    }
}
