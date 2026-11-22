using TerrariaTas.Core.Hotkeys;
using TerrariaTas.Core.Movies;
using Xunit;

namespace TerrariaTas.Core.Tests;

public class HotkeyMapTests
{
    [Fact]
    public void TheDefaultsAreTheFunctionKeys()
    {
        var map = HotkeyMap.Default;

        Assert.Equal(Key.F1, map.KeyOf(HotkeyAction.ToggleRecording));
        Assert.Equal(Key.F10, map.KeyOf(HotkeyAction.ReloadMovie));
        Assert.Equal(HotkeyAction.FrameAdvance, map.ActionOf(Key.F3));
        Assert.Null(map.ActionOf(Key.F11));
        Assert.Null(map.KeyOf(HotkeyAction.ToggleOverlay)); // until the user gives it a key
        Assert.StartsWith("F1 record on/off | F2 play/pause | F3 frame advance", map.Help());
        Assert.Contains("ToggleOverlay:", map.Write());
    }

    [Fact]
    public void AFileChangesTheKeysItNamesAndCanUnbind()
    {
        var map = HotkeyMap.Parse("""
            # laptop layout
            frameadvance: v      # lower case works too
            FastForward: B
            ReloadMovie:
            """);

        Assert.Equal(Key.V, map.KeyOf(HotkeyAction.FrameAdvance));
        Assert.Equal(Key.B, map.KeyOf(HotkeyAction.FastForward));
        Assert.Null(map.KeyOf(HotkeyAction.ReloadMovie));
        Assert.Equal(Key.F2, map.KeyOf(HotkeyAction.PlayPause));
        Assert.DoesNotContain(Key.F10, map.BoundKeys);
        Assert.DoesNotContain("reload", map.Help());
    }

    [Fact]
    public void TheWrittenFileReadsBackAsTheSameMap()
    {
        var map = HotkeyMap.Parse("PlayPause: Space\nToggleRecording: F2\nRestart:\n");

        var reread = HotkeyMap.Parse(map.Write());

        Assert.Equal(map.Keys, reread.Keys);
        Assert.Contains("# Terraria TAS hotkeys", map.Write());
    }

    [Theory]
    [InlineData("Jump: F1", 1, "Actions:")]
    [InlineData("\nPlayPause F2", 2, "<action>: <key>")]
    [InlineData("PlayPause: LMB", 1, "not a key name")]
    [InlineData("PlayPause: F1", 0, "F1 is the key of both ToggleRecording and PlayPause")]
    [InlineData("3: F1", 1, "Actions:")]
    public void MistakesAreReported(string text, int line, string message)
    {
        var error = Assert.Throws<MovieFormatException>(() => HotkeyMap.Parse(text));

        Assert.Equal(line, error.LineNumber);
        Assert.Contains(message, error.Message);
    }
}
