using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using TerrariaTas.Core.Movies;
using MovieButtons = TerrariaTas.Core.Movies.MouseButtons;

namespace TerrariaTas.Studio.Tools;

/// <summary>Colors shared by the keyboard and mouse drawings.</summary>
internal static class InputPalette
{
    public static readonly Color Cap = Color.FromArgb(241, 245, 249);
    public static readonly Color CapBorder = Color.FromArgb(148, 163, 184);
    public static readonly Color Label = Color.FromArgb(51, 65, 85);
    public static readonly Color Pressed = Color.FromArgb(37, 99, 235);
    public static readonly Color PressedLabel = Color.White;
    public static readonly Color Next = Color.FromArgb(234, 88, 12);

    /// <summary>Draws one key or button: filled when pressed on the last frame, outlined when pressed on the next.</summary>
    public static void DrawCap(Graphics graphics, RectangleF bounds, string label, bool pressed, bool next, Font font, float radius)
    {
        using var path = Rounded(bounds, radius);
        using (var fill = new SolidBrush(pressed ? Pressed : Cap))
            graphics.FillPath(fill, path);
        using (var border = new Pen(next ? Next : CapBorder, next ? Math.Max(2.5f, radius * 0.6f) : 1f))
            graphics.DrawPath(border, path);

        using var text = new SolidBrush(pressed ? PressedLabel : Label);
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(label, font, text, bounds, centered);
    }

    private static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>A keyboard (main block and navigation keys) that scales with the window.</summary>
internal sealed class KeyboardView : Control
{
    private const float LayoutWidth = 18.5f;
    private const float LayoutHeight = 6.25f;

    private static readonly KeyCap[] Caps = BuildLayout();
    private static readonly HashSet<Key> DrawnKeys = Caps.Select(cap => cap.Key).ToHashSet();

    private HashSet<Key> _pressed = new();
    private HashSet<Key> _next = new();

    public KeyboardView()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    /// <summary>Keys that have a place on the drawing; the others (numpad...) are listed in text instead.</summary>
    public static bool IsDrawn(Key key) => DrawnKeys.Contains(key);

    public void ShowKeys(IEnumerable<Key> pressed, IEnumerable<Key> next)
    {
        _pressed = pressed.ToHashSet();
        _next = next.ToHashSet();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var unit = Math.Min(ClientSize.Width / LayoutWidth, ClientSize.Height / LayoutHeight);
        if (unit < 4)
            return;

        var left = (ClientSize.Width - unit * LayoutWidth) / 2;
        var top = (ClientSize.Height - unit * LayoutHeight) / 2;
        var gap = unit * 0.08f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var font = new Font(Font.FontFamily, Math.Max(7f, unit * 0.24f), GraphicsUnit.Pixel);
        foreach (var cap in Caps)
        {
            var bounds = new RectangleF(left + cap.X * unit + gap / 2, top + cap.Y * unit + gap / 2, cap.Width * unit - gap, unit - gap);
            InputPalette.DrawCap(e.Graphics, bounds, cap.Label, _pressed.Contains(cap.Key), _next.Contains(cap.Key), font, unit * 0.12f);
        }
    }

    private static KeyCap[] BuildLayout()
    {
        var caps = new List<KeyCap>();

        void Row(float y, float x, params (Key Key, string Label, float Width)[] keys)
        {
            foreach (var (key, label, width) in keys)
            {
                caps.Add(new KeyCap(key, label, x, y, width));
                x += width;
            }
        }

        static (Key, string, float) K(Key key, string label, float width = 1) => (key, label, width);

        static (Key, string, float)[] Letters(string letters) =>
            letters.Select(letter => K(Enum.Parse<Key>(letter.ToString()), letter.ToString())).ToArray();

        Row(0, 0, K(Key.Escape, "Esc"));
        Row(0, 2, K(Key.F1, "F1"), K(Key.F2, "F2"), K(Key.F3, "F3"), K(Key.F4, "F4"));
        Row(0, 6.5f, K(Key.F5, "F5"), K(Key.F6, "F6"), K(Key.F7, "F7"), K(Key.F8, "F8"));
        Row(0, 11, K(Key.F9, "F9"), K(Key.F10, "F10"), K(Key.F11, "F11"), K(Key.F12, "F12"));

        Row(1.25f, 0, new[] { K(Key.OemTilde, "`") }
            .Concat("1234567890".Select(digit => K(Enum.Parse<Key>("D" + digit), digit.ToString())))
            .Concat(new[] { K(Key.OemMinus, "-"), K(Key.OemPlus, "="), K(Key.Back, "Back", 2) }).ToArray());
        Row(2.25f, 0, new[] { K(Key.Tab, "Tab", 1.5f) }.Concat(Letters("QWERTYUIOP"))
            .Concat(new[] { K(Key.OemOpenBrackets, "["), K(Key.OemCloseBrackets, "]"), K(Key.OemPipe, "\\", 1.5f) }).ToArray());
        Row(3.25f, 0, new[] { K(Key.CapsLock, "Caps", 1.75f) }.Concat(Letters("ASDFGHJKL"))
            .Concat(new[] { K(Key.OemSemicolon, ";"), K(Key.OemQuotes, "'"), K(Key.Enter, "Enter", 2.25f) }).ToArray());
        Row(4.25f, 0, new[] { K(Key.LeftShift, "Shift", 2.25f) }.Concat(Letters("ZXCVBNM"))
            .Concat(new[] { K(Key.OemComma, ","), K(Key.OemPeriod, "."), K(Key.OemQuestion, "/"), K(Key.RightShift, "Shift", 2.75f) }).ToArray());
        Row(5.25f, 0, K(Key.LeftControl, "Ctrl", 1.5f));
        Row(5.25f, 2.5f, K(Key.LeftAlt, "Alt", 1.5f), K(Key.Space, "Space", 6), K(Key.RightAlt, "Alt", 1.5f));
        Row(5.25f, 13.5f, K(Key.RightControl, "Ctrl", 1.5f));

        Row(1.25f, 15.5f, K(Key.Insert, "Ins"), K(Key.Home, "Home"), K(Key.PageUp, "PgUp"));
        Row(2.25f, 15.5f, K(Key.Delete, "Del"), K(Key.End, "End"), K(Key.PageDown, "PgDn"));
        Row(4.25f, 16.5f, K(Key.Up, "↑"));
        Row(5.25f, 15.5f, K(Key.Left, "←"), K(Key.Down, "↓"), K(Key.Right, "→"));
        return caps.ToArray();
    }

    private sealed record KeyCap(Key Key, string Label, float X, float Y, float Width);
}

/// <summary>A mouse seen from above: left, middle and right buttons, and the two side buttons.</summary>
internal sealed class MouseView : Control
{
    private MovieButtons _pressed;
    private MovieButtons _next;

    public MouseView()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    public void ShowButtons(MovieButtons pressed, MovieButtons next)
    {
        _pressed = pressed;
        _next = next;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var unit = Math.Min(ClientSize.Width / 4f, ClientSize.Height / 6f);
        if (unit < 4)
            return;

        var left = (ClientSize.Width - unit * 4) / 2;
        var top = (ClientSize.Height - unit * 6) / 2;
        var gap = unit * 0.1f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var font = new Font(Font.FontFamily, Math.Max(7f, unit * 0.32f), GraphicsUnit.Pixel);

        void Button(MovieButtons button, string label, float x, float y, float width, float height) =>
            InputPalette.DrawCap(e.Graphics, new RectangleF(left + x * unit + gap, top + y * unit + gap, width * unit - 2 * gap, height * unit - 2 * gap),
                label, (_pressed & button) != 0, (_next & button) != 0, font, unit * 0.3f);

        Button(MovieButtons.Left, "L", 1, 0, 1.25f, 2.5f);
        Button(MovieButtons.Middle, "M", 2.25f, 0.4f, 0.5f, 1.7f);
        Button(MovieButtons.Right, "R", 2.75f, 0, 1.25f, 2.5f);
        Button(MovieButtons.X2, "X2", 0, 2.6f, 1, 1);
        Button(MovieButtons.X1, "X1", 0, 3.6f, 1, 1);
        InputPalette.DrawCap(e.Graphics, new RectangleF(left + unit + gap, top + 2.5f * unit + gap, 3 * unit - 2 * gap, 3.5f * unit - 2 * gap),
            "", false, false, font, unit * 0.6f);
    }
}
