using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace TerrariaTas.Studio;

/// <summary>What Studio remembers between sessions: open tools, window placement and a few options.</summary>
internal sealed class StudioSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TerrariaTas", "studio.json");

    public bool ToolsOnTop { get; set; } = true;

    public int ForecastRows { get; set; } = 1000;

    /// <summary>How many gray shades the drop forecast cycles through, by stream position.</summary>
    public int ForecastShades { get; set; } = 6;

    public int DamageRows { get; set; } = 500;

    // Each row runs the game's search for a landing spot, up to 1000 tries: the game waits for them on every paused frame.
    public int TeleportRows { get; set; } = 500;

    /// <summary>How close to the target a teleport destination must land to count as a match, in tiles.</summary>
    public int TeleportRadius { get; set; } = 25;

    /// <summary>How many upcoming teleport destinations the world map marks.</summary>
    public int MapDestinations { get; set; } = 20;

    public List<string> OpenTools { get; set; } = new();

    /// <summary>The paths of the memory watch.</summary>
    public List<string> Watches { get; set; } = new();

    public Dictionary<string, WindowBounds> Windows { get; set; } = new();

    public static StudioSettings Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<StudioSettings>(File.ReadAllText(FilePath)) ?? new() : new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new StudioSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Losing the window layout is not worth bothering the user.
        }
    }

    /// <summary>Puts a window back where it was, unless that place is no longer on any screen.</summary>
    public void RestorePlacement(Form form, string key)
    {
        if (!Windows.TryGetValue(key, out var saved))
            return;

        var bounds = new Rectangle(saved.X, saved.Y, saved.Width, saved.Height);
        if (!Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds)))
            return;

        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = bounds;
    }

    public void RememberPlacement(Form form, string key)
    {
        if (form.WindowState == FormWindowState.Normal)
            Windows[key] = new WindowBounds { X = form.Left, Y = form.Top, Width = form.Width, Height = form.Height };
    }
}

internal sealed class WindowBounds
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }
}
