using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TerrariaTas.Core.Studio;
using TerrariaTas.Core.World;
using TerrariaTas.Studio.Connection;

namespace TerrariaTas.Studio.Tools;

/// <summary>
/// The world map, to move around with the mouse: drag to pan, wheel to zoom. The pointer is a crosshair of white lines
/// across the whole view, with the tile under it and what the compass and depth meter would read there. Marks the
/// player, the spawn point, upcoming teleport destinations and the place teleports are looked for.
/// </summary>
internal sealed class MapView : Control
{
    // Screen pixels per tile.
    private const float MinScale = 1f / 16f;
    private const float MaxScale = 32f;
    private const int DragThreshold = 4;

    private static readonly Color Background = Color.FromArgb(24, 24, 24);
    private static readonly Color PlayerColor = Color.FromArgb(34, 211, 238);
    private static readonly Color DestinationColor = Color.FromArgb(250, 204, 21);
    private static readonly Color SelectedColor = Color.FromArgb(236, 72, 153);
    private static readonly Color TargetColor = Color.FromArgb(249, 115, 22);

    private readonly Cursor _hiddenPointer;
    private readonly IntPtr _hiddenPointerIcon;
    private WorldMapImage? _map;
    private Bitmap? _bitmap;
    private int _rowsCopied;
    private Bitmap? _view;
    private bool _viewDirty = true;
    private float _scale = 1f;
    private float _originX;
    private float _originY;
    private bool _placed;
    private Point? _pointer;
    private Point _dragStart;
    private PointF _dragOrigin;
    private MouseButtons _dragButton;
    private bool _dragging;
    private PlayerView? _player;
    private IReadOnlyList<(int Offset, TeleportDestination Destination)> _destinations = Array.Empty<(int, TeleportDestination)>();
    private int _selectedOffset = -1;
    private TeleportTarget? _target;

    // The labels of the paint in progress, drawn after every marker so that the important ones get the room.
    private readonly List<MapLabel> _labels = new();

    public MapView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Background;
        ForeColor = Color.White;

        // The crosshair is the pointer: Windows' own arrow is hidden over the map.
        using var empty = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        _hiddenPointerIcon = empty.GetHicon();
        _hiddenPointer = new Cursor(_hiddenPointerIcon);
        Cursor = _hiddenPointer;
    }

    /// <summary>A right click on the map, with the tile under the pointer.</summary>
    public event Action<int, int>? TileRightClicked;

    /// <summary>The pointer moved, entered or left the map: <see cref="PointerReadout"/> changed.</summary>
    public event Action? PointerMoved;

    /// <summary>"Tile 1234, 567 · 123' East, 45' Caverns" for the pointer; null when it is not over the world.</summary>
    public string? PointerReadout { get; private set; }

    public PlayerView? Player
    {
        get => _player;
        set
        {
            _player = value;
            Invalidate();
        }
    }

    /// <summary>Destinations to mark, with their offsets.</summary>
    public IReadOnlyList<(int Offset, TeleportDestination Destination)> Destinations
    {
        get => _destinations;
        set
        {
            _destinations = value;
            Invalidate();
        }
    }

    public int SelectedOffset
    {
        get => _selectedOffset;
        set
        {
            _selectedOffset = value;
            Invalidate();
        }
    }

    public TeleportTarget? Target
    {
        get => _target;
        set
        {
            _target = value;
            Invalidate();
        }
    }

    /// <summary>Shows a map, or copies the rows that arrived since the last call when it is the same one.</summary>
    public void ShowMap(WorldMapImage? map)
    {
        if (map != _map)
        {
            _map = map;
            _bitmap?.Dispose();
            _bitmap = map == null ? null : new Bitmap(map.World.Width, map.World.Height, PixelFormat.Format32bppRgb);
            _rowsCopied = 0;
            if (!_placed)
                Fit(); // the first map shows whole, unless a place was asked for already
        }

        CopyNewRows();
        _viewDirty = true;
        Invalidate();
    }

    /// <summary>The whole world in view.</summary>
    public void Fit()
    {
        if (_map == null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return;

        var world = _map.World;
        _scale = Clamp(Math.Min((float)ClientSize.Width / world.Width, (float)ClientSize.Height / world.Height));
        CenterOn(world.Width / 2f, world.Height / 2f);
    }

    /// <summary>Puts a tile in the middle of the view, zooming in to at least <paramref name="minScale"/> pixels per tile.</summary>
    public void CenterOn(float tileX, float tileY, float minScale = 0f)
    {
        _placed = true;
        _scale = Clamp(Math.Max(_scale, minScale));
        _originX = tileX - ClientSize.Width / 2f / _scale;
        _originY = tileY - ClientSize.Height / 2f / _scale;
        ViewChanged();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        if (_bitmap == null || _map == null)
        {
            graphics.Clear(Background);
            DrawPointer(graphics); // the crosshair is the only pointer over the map
            return;
        }

        if (_viewDirty || _view == null || _view.Size != ClientSize)
            RenderView();
        graphics.DrawImageUnscaled(_view!, 0, 0);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        _labels.Clear();
        DrawSpawn(graphics, _map.World);
        DrawTarget(graphics);
        DrawDestinations(graphics);
        DrawPlayer(graphics);
        DrawLabels(graphics);
        graphics.SmoothingMode = SmoothingMode.None;
        DrawPointer(graphics);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _viewDirty = true;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (FindForm() is { } form && form == Form.ActiveForm)
            Focus(); // the wheel goes to the focused control
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        _dragStart = e.Location;
        _dragOrigin = new PointF(_originX, _originY);
        _dragButton = e.Button;
        _dragging = false;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if ((_dragButton is MouseButtons.Left or MouseButtons.Middle) && (e.Button & _dragButton) != 0)
        {
            var dx = e.X - _dragStart.X;
            var dy = e.Y - _dragStart.Y;
            _dragging |= Math.Abs(dx) > DragThreshold || Math.Abs(dy) > DragThreshold;
            if (_dragging)
            {
                _originX = _dragOrigin.X - dx / _scale;
                _originY = _dragOrigin.Y - dy / _scale;
                _viewDirty = true;
            }
        }

        MovePointer(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right && !_dragging && TileAt(e.Location) is { } tile)
            TileRightClicked?.Invoke(tile.X, tile.Y);
        _dragButton = MouseButtons.None;
        _dragging = false;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        MovePointer(null);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var anchor = ToTile(e.Location);
        _scale = Clamp(_scale * (float)Math.Pow(1.25, e.Delta / 120.0));
        _originX = anchor.X - e.X / _scale;
        _originY = anchor.Y - e.Y / _scale;
        ViewChanged();
        MovePointer(e.Location);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bitmap?.Dispose();
            _view?.Dispose();
            _hiddenPointer.Dispose();
            DestroyIcon(_hiddenPointerIcon);
        }

        base.Dispose(disposing);
    }

    private static float Clamp(float scale) => Math.Max(MinScale, Math.Min(MaxScale, scale));

    private float ScreenX(float tileX) => (tileX - _originX) * _scale;

    private float ScreenY(float tileY) => (tileY - _originY) * _scale;

    private PointF ToTile(Point screen) => new(_originX + screen.X / _scale, _originY + screen.Y / _scale);

    private Point? TileAt(Point screen)
    {
        if (_map == null)
            return null;
        var tile = ToTile(screen);
        var x = (int)Math.Floor(tile.X);
        var y = (int)Math.Floor(tile.Y);
        return x >= 0 && y >= 0 && x < _map.World.Width && y < _map.World.Height ? new Point(x, y) : null;
    }

    private void ViewChanged()
    {
        _viewDirty = true;
        Invalidate();
    }

    private void MovePointer(Point? location)
    {
        _pointer = location;
        PointerReadout = null;
        if (location is { } screen && _map != null && TileAt(screen) is { } tile)
        {
            // The readings of a player standing with its feet on the pointer.
            var world = ToTile(screen);
            var feet = WorldCoordinates.AtFeet(world.X * WorldGeometry.TileSize, world.Y * WorldGeometry.TileSize, _map.World);
            PointerReadout = $"Tile {tile.X}, {tile.Y} · {feet}";
        }

        PointerMoved?.Invoke();
        Invalidate();
    }

    /// <summary>Copies the rows received since the last call into the bitmap.</summary>
    private void CopyNewRows()
    {
        if (_map == null || _bitmap == null || _map.RowsReceived <= _rowsCopied)
            return;

        var width = _map.World.Width;
        var rows = new Rectangle(0, _rowsCopied, width, _map.RowsReceived - _rowsCopied);
        var data = _bitmap.LockBits(rows, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            for (var y = 0; y < rows.Height; y++)
                Marshal.Copy(_map.Pixels, (rows.Y + y) * width, data.Scan0 + y * data.Stride, width);
        }
        finally
        {
            _bitmap.UnlockBits(data);
        }

        _rowsCopied = _map.RowsReceived;
    }

    /// <summary>The map at the current pan and zoom, kept until they change: moving the pointer only redraws the marks.</summary>
    private void RenderView()
    {
        _view?.Dispose();
        _view = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height), PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(_view);
        graphics.Clear(Background);
        _viewDirty = false;
        if (_bitmap == null)
            return;

        // Only the part of the world in view: GDI+ smears the edge pixels of a source rectangle that leaves the image.
        var left = Math.Max(0f, _originX);
        var top = Math.Max(0f, _originY);
        var right = Math.Min(_bitmap.Width, _originX + ClientSize.Width / _scale);
        var bottom = Math.Min(_bitmap.Height, _originY + ClientSize.Height / _scale);
        if (right <= left || bottom <= top)
            return;

        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        var corners = new[]
        {
            new PointF(ScreenX(left), ScreenY(top)),
            new PointF(ScreenX(right), ScreenY(top)),
            new PointF(ScreenX(left), ScreenY(bottom)),
        };
        graphics.DrawImage(_bitmap, corners, RectangleF.FromLTRB(left, top, right, bottom), GraphicsUnit.Pixel, attributes);
    }

    /// <summary>A player-sized box whose top-left corner is the world pixel (x, y): at least a few pixels when zoomed out.</summary>
    private RectangleF PlayerBox(float x, float y)
    {
        var width = Math.Max(4f, WorldCoordinates.PlayerWidth / (float)WorldGeometry.TileSize * _scale);
        var height = Math.Max(7f, WorldCoordinates.PlayerHeight / (float)WorldGeometry.TileSize * _scale);
        var centerX = ScreenX((x + WorldCoordinates.PlayerWidth / 2f) / WorldGeometry.TileSize);
        var bottom = ScreenY((y + WorldCoordinates.PlayerHeight) / WorldGeometry.TileSize);
        return new RectangleF(centerX - width / 2, bottom - height, width, height);
    }

    private void DrawPlayer(Graphics graphics)
    {
        if (_player == null)
            return;
        var box = PlayerBox(_player.PositionX, _player.PositionY);
        using var pen = new Pen(PlayerColor, 2f);
        graphics.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
        AddLabel(LabelPriority.Player, "You", box.Right + 3, box.Top, PlayerColor);
    }

    private void DrawSpawn(Graphics graphics, WorldGeometry world)
    {
        var x = ScreenX(world.SpawnX + 0.5f);
        var y = ScreenY(world.SpawnY);
        using var pen = new Pen(Color.White, 1.5f);
        graphics.DrawEllipse(pen, x - 4, y - 4, 8, 8);
        AddLabel(LabelPriority.Spawn, "Spawn", x + 6, y - 8, Color.White);
    }

    /// <summary>
    /// One marker per landing spot: rolls often land on the same one (the last tries of a roll are the first tries of a
    /// roll a few draws later), and its label lists the first offsets that land there.
    /// </summary>
    private void DrawDestinations(Graphics graphics)
    {
        using var pen = new Pen(DestinationColor, 1.5f);
        using var selectedPen = new Pen(SelectedColor, 3f);
        foreach (var spot in _destinations.Where(d => d.Destination.Found).GroupBy(d => (d.Destination.X, d.Destination.Y)))
        {
            var offsets = spot.Select(d => d.Offset).OrderBy(offset => offset).ToList();
            var selected = offsets.Contains(_selectedOffset);
            var box = PlayerBox(spot.Key.X, spot.Key.Y);
            graphics.DrawRectangle(selected ? selectedPen : pen, box.X, box.Y, box.Width, box.Height);
            AddLabel(selected ? LabelPriority.Selected : LabelPriority.Destination, OffsetsLabel(offsets), box.Right + 3, box.Top,
                selected ? SelectedColor : DestinationColor);
        }
    }

    /// <summary>"+0 +2 +4 … (8 rolls)" for the offsets landing on one spot, in order.</summary>
    private static string OffsetsLabel(IReadOnlyList<int> offsets)
    {
        const int Shown = 3;
        var text = string.Join(" ", offsets.Take(Shown).Select(offset => "+" + offset));
        return offsets.Count > Shown ? $"{text} … ({offsets.Count} rolls)" : text;
    }

    private void DrawTarget(Graphics graphics)
    {
        if (_target == null)
            return;
        var x = ScreenX(_target.TileX + 0.5f);
        var y = ScreenY(_target.TileY + 0.5f);
        var radius = Math.Max(3f, _target.Radius * _scale);
        using var pen = new Pen(TargetColor, 2f) { DashStyle = DashStyle.Dash };
        graphics.DrawEllipse(pen, x - radius, y - radius, radius * 2, radius * 2);
        using var solid = new Pen(TargetColor, 2f);
        graphics.DrawLine(solid, x - 6, y, x + 6, y);
        graphics.DrawLine(solid, x, y - 6, x, y + 6);
    }

    private void DrawPointer(Graphics graphics)
    {
        if (_pointer is not { } pointer)
            return;

        using var shadow = new Pen(Color.FromArgb(150, 0, 0, 0));
        graphics.DrawLine(shadow, 0, pointer.Y + 1, Width, pointer.Y + 1);
        graphics.DrawLine(shadow, pointer.X + 1, 0, pointer.X + 1, Height);
        graphics.DrawLine(Pens.White, 0, pointer.Y, Width, pointer.Y);
        graphics.DrawLine(Pens.White, pointer.X, 0, pointer.X, Height);

        // Zoomed in, the tile under the pointer gets a frame.
        if (_scale >= 6f && TileAt(pointer) is { } tile)
            graphics.DrawRectangle(Pens.White, ScreenX(tile.X), ScreenY(tile.Y), _scale, _scale);

        if (PointerReadout == null)
            return;

        var size = TextRenderer.MeasureText(PointerReadout, Font);
        var box = new Rectangle(pointer.X + 14, pointer.Y + 14, size.Width + 8, size.Height + 4);
        if (box.Right > Width)
            box.X = pointer.X - 14 - box.Width;
        if (box.Bottom > Height)
            box.Y = pointer.Y - 14 - box.Height;
        using var fill = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
        graphics.FillRectangle(fill, box);
        TextRenderer.DrawText(graphics, PointerReadout, Font, box, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void AddLabel(LabelPriority priority, string text, float x, float y, Color color) =>
        _labels.Add(new MapLabel(priority, text, new Point((int)x, (int)y), color));

    /// <summary>
    /// Draws the labels of this paint, the most important first, so that none covers another: a label without room next
    /// to its marker goes under it, else above it. A destination's label without room anywhere is left out (its marker
    /// stays); the others are drawn anyway.
    /// </summary>
    private void DrawLabels(Graphics graphics)
    {
        var placed = new List<Rectangle>();
        foreach (var label in _labels.OrderBy(label => label.Priority)) // a stable sort: destinations keep their offset order
        {
            var size = TextRenderer.MeasureText(graphics, label.Text, Font, Size.Empty, TextFormatFlags.NoPrefix);
            Rectangle? free = null;
            foreach (var shift in new[] { 0, size.Height, -size.Height })
            {
                var box = new Rectangle(label.At.X, label.At.Y + shift, size.Width, size.Height);
                if (!placed.Any(other => other.IntersectsWith(box)))
                {
                    free = box;
                    break;
                }
            }

            if (free == null && label.Priority == LabelPriority.Destination)
                continue;
            var spot = free ?? new Rectangle(label.At, size);
            placed.Add(spot);
            TextRenderer.DrawText(graphics, label.Text, Font, new Point(spot.X + 1, spot.Y + 1), Color.Black, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(graphics, label.Text, Font, spot.Location, label.Color, TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Which labels win the room on the map, first to last.</summary>
    private enum LabelPriority
    {
        Player,
        Selected,
        Spawn,
        Destination,
    }

    private sealed record MapLabel(LabelPriority Priority, string Text, Point At, Color Color);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
