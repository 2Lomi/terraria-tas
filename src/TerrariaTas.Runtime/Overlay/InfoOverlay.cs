using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace TerrariaTas.Runtime.Overlay;

/// <summary>
/// Draws a few lines of TAS information over the game (frame, position and velocity) once the game's own draw is
/// done, with its font. It only draws: nothing it touches is read by the simulation, so movies replay the same with it
/// on (it does show in screenshots and recordings of the window). Off until turned on. A failure turns it off rather
/// than crashing the game.
/// </summary>
internal sealed class InfoOverlay
{
    private const float Scale = 0.85f;
    private const int Margin = 12;
    private const int LineHeight = 20;

    private static InfoOverlay? _active;

    public bool Enabled { get; set; }

    /// <summary>The lines to show, asked for while the frame draws (after its update).</summary>
    public Func<string[]>? Lines { get; set; }

    public void Install(Harmony harmony)
    {
        _active = this;
        harmony.CreateClassProcessor(typeof(Hooks)).Patch();
    }

    private void Draw()
    {
        if (!Enabled || Lines == null || Main.gameMenu)
            return;

        var batch = Main.spriteBatch;
        try
        {
            batch.Begin();
        }
        catch (InvalidOperationException e)
        {
            TurnOff(e); // the game left its sprite batch open: drawing now would break its next draw
            return;
        }

        try
        {
            // Bottom right, right-aligned: the bottom left is where the game prints chat and achievement messages.
            var lines = Lines();
            var y = Main.screenHeight - Margin - LineHeight * lines.Length;
            foreach (var line in lines)
            {
                // The game's text drawer reads ':' as a color code and drops it: the lines are written without one.
                Utils.DrawBorderString(batch, line, new Vector2(Main.screenWidth - Margin, y), Color.White, Scale, anchorx: 1f);
                y += LineHeight;
            }
        }
        catch (Exception e)
        {
            TurnOff(e);
        }
        finally
        {
            batch.End();
        }
    }

    private void TurnOff(Exception reason)
    {
        Enabled = false;
        Log.Error("The overlay failed and is off now: " + reason);
    }

    [HarmonyPatch]
    private static class Hooks
    {
        [HarmonyPatch(typeof(Main), "Draw", typeof(GameTime)), HarmonyPostfix]
        private static void AfterDraw() => _active?.Draw();
    }
}
