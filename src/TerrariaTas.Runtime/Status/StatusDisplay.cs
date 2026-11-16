using System.Diagnostics;
using Terraria;

namespace TerrariaTas.Runtime.Status;

/// <summary>Shows the TAS state in the game window title (outside the game's own rendering).</summary>
internal sealed class StatusDisplay
{
    private const long RefreshMilliseconds = 100;

    private readonly Stopwatch _sinceRefresh = Stopwatch.StartNew();
    private string _shown = "";

    /// <param name="urgent">Show immediately instead of waiting for the next refresh slot.</param>
    public void Show(string text, bool urgent = false)
    {
        if (text == _shown || (!urgent && _sinceRefresh.ElapsedMilliseconds < RefreshMilliseconds))
            return;

        Main.instance.Window.Title = text;
        _shown = text;
        _sinceRefresh.Restart();
    }
}
