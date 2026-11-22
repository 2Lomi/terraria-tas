using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace TerrariaTas.Runtime.Determinism;

/// <summary>
/// The window size is part of the movie (Resolution): Terraria simulates with the size of its back buffer, which follows
/// the window (mouse coordinates, lighting range, what is on screen, dust spawned near it). The size goes into
/// config.json before launch, the window cannot be resized or maximized during a movie, and no frame may run while it
/// is minimized: XNA does not draw a minimized window (<c>Game.DrawFrame</c>), and Terraria runs interface logic and
/// random rolls in its draw. Drawing anyway is no way out: Terraria would then read the empty client area and shrink its
/// screen to the minimum size.
/// </summary>
internal sealed class WindowPins
{
    /// <summary>The size the game simulates with.</summary>
    public static Point ScreenSize => new(Main.screenWidth, Main.screenHeight);

    /// <summary>The window shows no client area: XNA skips the draw of every frame that runs now.</summary>
    public static bool IsMinimized
    {
        get
        {
            // Mirrors XNA's own test (WindowsGameForm.IsMinimized), which decides whether a frame is drawn.
            var bounds = Main.instance.Window.ClientBounds;
            return bounds.Width == 0 || bounds.Height == 0;
        }
    }

    /// <summary>
    /// Writes the movie's window size to the TAS data folder's config.json before launch: Terraria applies it while
    /// creating its window.
    /// </summary>
    public void Configure(int width, int height)
    {
        var config = Main.Configuration;
        config.Load();
        config.Put("LastLaunchedVersion", Main.curRelease); // older configs have their display settings ignored
        config.Put("Fullscreen", false);
        config.Put("WindowMaximized", false);
        config.Put("WindowBorderless", false);
        config.Put("DisplayWidth", width);
        config.Put("DisplayHeight", height);

        // With 4K support on, the graphics profile depends on the desktop size and the GPU, and with it the screen
        // size limit that the snow budget uses: off, every machine runs the same profile (movies up to 1920x1200).
        config.Put("Support4K", false);

        // A configuration without these is a first launch: the game asks for a language (on screens of their own) and
        // makes a new client id. TAS sessions with a movie never save the configuration, so a new data folder would
        // stay a first launch on every launch. Written once, the language sessions use anyway.
        if (!config.Contains("Language"))
            config.Put("Language", "en-US");
        if (!config.Contains("ClientUUID"))
            config.Put("ClientUUID", Guid.NewGuid().ToString());
        config.Save();
    }

    /// <summary>
    /// From now on the window keeps its size: no resizing border, no maximize button. It can still be moved and
    /// minimized. Call once the game has set up its window (after the splash screen), which allows resizing.
    /// </summary>
    public void Lock() => Main.instance.Window.AllowUserResizing = false;
}
