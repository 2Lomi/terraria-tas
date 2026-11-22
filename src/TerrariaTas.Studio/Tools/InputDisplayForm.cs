using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TerrariaTas.Core.Movies;
using TerrariaTas.Studio.Connection;
using MovieButtons = TerrariaTas.Core.Movies.MouseButtons;

namespace TerrariaTas.Studio.Tools;

/// <summary>
/// What is pressed: filled keys are the input the game read on the last frame, outlined keys are the input of the
/// next frame: the movie's, or while recording, the player's right now. Live while the game plays.
/// </summary>
internal sealed class InputDisplayForm : ToolWindow
{
    private readonly Label _frames = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 2) };
    private readonly KeyboardView _keyboard = new() { Dock = DockStyle.Fill };
    private readonly MouseView _mouse = new() { Dock = DockStyle.Right, Width = 120 };
    private readonly Label _cursor = new() { AutoSize = true, Margin = new Padding(8, 2, 8, 2) };
    private readonly Label _otherKeys = new() { AutoSize = true, Margin = new Padding(8, 2, 8, 2) };

    public InputDisplayForm(StudioState state)
        : base(state, "input", "Input display")
    {
        ClientSize = new Size(860, 330);
        MinimumSize = new Size(420, 220);

        var drawing = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        drawing.Controls.Add(_keyboard);
        drawing.Controls.Add(_mouse);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        footer.Controls.Add(Legend());
        footer.Controls.Add(_cursor);
        footer.Controls.Add(_otherKeys);

        Controls.Add(drawing);
        Controls.Add(footer);
        Controls.Add(_frames);
    }

    protected override void OnStatusChanged() => ShowInput();

    protected override void OnMovieChanged() => ShowInput();

    private void ShowInput()
    {
        if (State.Status is not { } status)
        {
            _frames.Text = "Waiting for the game…";
            return;
        }

        var last = status.Input;
        var live = status.Recording ? status.NextInput : null;
        var next = live == null && status.Frame < State.Movie.TotalFrames ? State.Movie.LineAt(status.Frame) : null;
        var nextKeys = live?.Keys ?? next?.Keys.ToArray() ?? Array.Empty<Key>();
        _keyboard.ShowKeys(last.Keys, nextKeys);
        _mouse.ShowButtons(last.Buttons, live?.Buttons ?? next?.Buttons ?? MovieButtons.None);

        _frames.Text = $"Filled: frame {status.Frame - 1}, as the game read it.   Outlined: frame {status.Frame}, "
                       + (live != null ? "your keyboard and mouse right now (recording)."
                          : next != null ? $"from movie line {next.SourceLine}."
                          : "past the end of the movie (real input).");
        _cursor.Text = $"Cursor {last.CursorX}, {last.CursorY} px · tile {last.CursorTileX}, {last.CursorTileY}"
                       + (live != null ? $"   ·   next frame aims at @{live.CursorX},{live.CursorY} · tile {live.CursorTileX}, {live.CursorTileY}"
                          : next?.Cursor is { } target ? $"   ·   next frame aims at {target}" : "");

        var others = last.Keys.Where(key => !KeyboardView.IsDrawn(key)).Select(key => key.ToString())
            .Concat(nextKeys.Where(key => !KeyboardView.IsDrawn(key)).Select(key => key + " (next)"))
            .ToList();
        _otherKeys.Text = others.Count > 0 ? "Not drawn: " + string.Join(", ", others) : "";
    }

    private static FlowLayoutPanel Legend()
    {
        var legend = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(8, 2, 8, 2) };
        legend.Controls.Add(Swatch(InputPalette.Pressed, filled: true));
        legend.Controls.Add(new Label { Text = "pressed on the last frame", AutoSize = true, Margin = new Padding(2, 3, 16, 0) });
        legend.Controls.Add(Swatch(InputPalette.Next, filled: false));
        legend.Controls.Add(new Label { Text = "pressed on the next frame", AutoSize = true, Margin = new Padding(2, 3, 16, 0) });
        return legend;
    }

    private static Control Swatch(Color color, bool filled)
    {
        var swatch = new Panel { Size = new Size(18, 18), Margin = new Padding(0, 2, 0, 0) };
        swatch.Paint += (_, e) =>
        {
            var bounds = new Rectangle(1, 1, swatch.Width - 3, swatch.Height - 3);
            if (filled)
            {
                using var brush = new SolidBrush(color);
                e.Graphics.FillRectangle(brush, bounds);
            }

            using var pen = new Pen(color, filled ? 1 : 3);
            e.Graphics.DrawRectangle(pen, bounds);
        };
        return swatch;
    }
}
