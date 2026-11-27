using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TerrariaTas.Core.Studio;
using TerrariaTas.Core.World;
using TerrariaTas.Studio.Connection;

namespace TerrariaTas.Studio.Tools;

/// <summary>
/// Where a Teleportation Potion would land depending on when its destination is rolled. Row k is the landing spot if
/// the roll starts k draws after the frame boundary the game is paused at, on the players' stream. A target (typed
/// here, or a right click on the world map) highlights the rows landing near it. The teleport log underneath shows at
/// which offset each real teleport rolled, and whether the forecast was right.
/// </summary>
internal sealed class TeleportForecastForm : ToolWindow
{
    private const int RememberedForecasts = 30;
    private const int LogRows = 200;
    private const int TileColumn = 1;
    private const int DistanceColumn = 4;
    private static readonly int[] RowChoices = { 200, 500, 1000, 2000, 5000, 10000 };
    private static readonly Color NextDraw = Color.FromArgb(191, 219, 254);

    private readonly StudioSettings _settings;
    private readonly Action<int, int> _showOnMap;
    private readonly Label _title = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 13f), Margin = new Padding(0, 0, 16, 0), Text = "Teleportation Potion" };
    private readonly Label _details = new() { AutoSize = true, Margin = new Padding(0, 6, 0, 0), UseMnemonic = false };
    private readonly Label _roll = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 2, 8, 2), UseMnemonic = false };
    private readonly Label _notice = NoticeLabel();
    private readonly Label _matches = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 2, 8, 4), UseMnemonic = false };
    private readonly ComboBox _rows = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly CheckBox _autoRefresh = new() { Text = "Refresh on every paused frame", Checked = true, AutoSize = true, Margin = new Padding(12, 6, 0, 0) };
    private readonly NumericUpDown _targetX = new() { Maximum = 100_000, Width = 70 };
    private readonly NumericUpDown _targetY = new() { Maximum = 100_000, Width = 70 };
    private readonly NumericUpDown _radius = new() { Minimum = 1, Maximum = 10_000, Width = 60 };
    private readonly DataGridView _grid = Grids.Create();
    private readonly DataGridView _log = Grids.Create();
    private readonly List<TeleportForecast> _recent = new();

    private TeleportForecast? _forecast;
    private bool _stale;
    private bool _showingTarget;
    private int _lastFrame;
    private int _lastRequest;
    private int _awaitedRequest = -1;

    /// <param name="showOnMap">Opens the world map centered on a tile.</param>
    public TeleportForecastForm(StudioState state, StudioSettings settings, Action<int, int> showOnMap)
        : base(state, "teleport", "Teleport forecast")
    {
        _settings = settings;
        _showOnMap = showOnMap;
        ClientSize = new Size(900, 720);

        _rows.Items.AddRange(RowChoices.Select(rows => (object)rows).ToArray());
        _rows.SelectedItem = RowChoices.Contains(settings.TeleportRows) ? settings.TeleportRows : 500;
        _rows.SelectedIndexChanged += (_, _) =>
        {
            _settings.TeleportRows = SelectedRows;
            RequestForecast();
        };
        _radius.Value = Math.Max(1, Math.Min(10_000, settings.TeleportRadius));
        _radius.ValueChanged += (_, _) =>
        {
            _settings.TeleportRadius = (int)_radius.Value;
            if (!_showingTarget && State.TeleportTarget is { } target)
                State.SetTeleportTarget(target with { Radius = (int)_radius.Value });
        };

        _grid.VirtualMode = true;
        _grid.Columns.AddRange(
            Grids.Number("Offset", 20),
            Grids.Column("Lands on tile", 30),
            Grids.Column("Compass", 35),
            Grids.Column("Depth meter", 45),
            Grids.Number("From target", 25),
            Grids.Number("Draws", 18));
        foreach (DataGridViewColumn column in _grid.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        _grid.CellValueNeeded += (_, e) => e.Value = Cell(e.RowIndex, e.ColumnIndex);
        _grid.CellFormatting += FormatCell;
        _grid.SelectionChanged += (_, _) => State.SelectTeleport(_grid.CurrentCell?.RowIndex ?? -1);
        _grid.CellDoubleClick += (_, e) => ShowRowOnMap(e.RowIndex);

        _log.Columns.AddRange(
            Grids.Number("Frame", 25),
            Grids.Column("Stream", 35),
            Grids.Number("Rolled at offset", 30),
            Grids.Column("Landed on tile", 35),
            Grids.Column("Compass, depth", 70),
            Grids.Column("Forecast", 70));
        foreach (DataGridViewColumn column in _log.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable; // newest first

        var refresh = new Button { Text = "Refresh", AutoSize = true };
        refresh.Click += (_, _) => RequestForecast();
        var lookThere = new Button { Text = "Look there", AutoSize = true };
        lookThere.Click += (_, _) => State.SetTeleportTarget(new TeleportTarget((int)_targetX.Value, (int)_targetY.Value, (int)_radius.Value));
        var clearTarget = new Button { Text = "Clear", AutoSize = true };
        clearTarget.Click += (_, _) => State.SetTeleportTarget(null);
        var nextMatch = new Button { Text = "Next match ↓", AutoSize = true };
        nextMatch.Click += (_, _) => SelectNextMatch();
        var showOnMapButton = new Button { Text = "Show on map", AutoSize = true };
        showOnMapButton.Click += (_, _) => ShowRowOnMap(_grid.CurrentCell?.RowIndex ?? -1);

        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 0) };
        header.Controls.Add(_title);
        header.Controls.Add(_details);

        var controls = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 0) };
        controls.Controls.Add(Labeled("Rows", _rows, 0));
        controls.Controls.Add(refresh);
        controls.Controls.Add(_autoRefresh);
        controls.Controls.Add(showOnMapButton);

        var target = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 2, 8, 0) };
        target.Controls.Add(Labeled("Look for landings near tile X", _targetX, 0));
        target.Controls.Add(Labeled("Y", _targetY, 6));
        target.Controls.Add(Labeled("within", _radius, 6));
        target.Controls.Add(new Label { Text = "tiles", AutoSize = true, Margin = new Padding(4, 6, 8, 0) });
        target.Controls.Add(lookThere);
        target.Controls.Add(clearTarget);
        target.Controls.Add(nextMatch);

        var help = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8, 2, 8, 4),
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(880, 0),
            Text = "Row k = where you land if the potion's destination roll starts k draws after this frame boundary, on the players' stream (UpdatePlayers). "
                   + "What the players' update draws before the roll in that frame decides the row: the teleport log shows the offset of each real teleport. "
                   + "Right-click the world map to look for landings near a place. Double-click a row to see it on the map.",
        };

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 430 };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_log);
        split.Panel2.Controls.Add(new Label { Text = "Teleport log", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 2), Font = new Font(Font, FontStyle.Bold) });

        Controls.Add(split);
        Controls.Add(_matches);
        Controls.Add(help);
        Controls.Add(_roll);
        Controls.Add(_notice);
        Controls.Add(target);
        Controls.Add(controls);
        Controls.Add(header);
    }

    private int SelectedRows => _rows.SelectedItem as int? ?? 500;

    private TeleportColumn? Column => _forecast?.Columns.FirstOrDefault();

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        foreach (var teleport in State.Teleports)
            OnTeleportRecorded(teleport);
        OnTeleportFocusChanged();
        if (_awaitedRequest < 0)
            RequestForecast(); // otherwise the world snapshot already asked
    }

    protected override void OnConnectionChanged() => UpdateNotice();

    protected override void OnStatusChanged()
    {
        // Back to an earlier frame (a savestate, a restart): forecasts of later frames belong to another timeline.
        if (State.Status is { } status)
        {
            if (status.Frame < _lastFrame)
                _recent.Clear();
            _lastFrame = status.Frame;
        }

        UpdateNotice();
    }

    protected override void OnWorldChanged()
    {
        UpdateNotice();
        // A new world snapshot means a new game state: a frame ran or a savestate was loaded.
        if (_autoRefresh.Checked && State.WorldIsCurrent)
            RequestForecast();
    }

    protected override void OnTeleportForecastReceived(TeleportForecast forecast)
    {
        if (forecast.RequestId != _awaitedRequest)
            return;

        _awaitedRequest = -1;
        if (forecast.Error.Length > 0)
        {
            ShowNotice(_notice, forecast.Error);
            _stale = true;
            _grid.Invalidate();
            ShowHeader();
            return;
        }

        _forecast = forecast;
        _recent.Add(forecast);
        if (_recent.Count > RememberedForecasts)
            _recent.RemoveAt(0);

        _grid.RowCount = Column?.Destinations.Count ?? 0;
        _grid.Invalidate();
        UpdateNotice();
        ShowHeader();
        ShowMatches();
    }

    protected override void OnTeleportRecorded(TeleportRecord teleport)
    {
        var world = _forecast?.World ?? State.Map?.World;
        var landed = new TeleportDestination { X = teleport.ToX, Y = teleport.ToY, Found = true };
        var row = new object[]
        {
            teleport.Frame,
            teleport.Stream,
            teleport.Offset >= 0 ? (object)teleport.Offset : "?",
            $"{landed.TileX}, {landed.TileY}",
            world == null ? "" : WorldCoordinates.AtPosition(teleport.ToX, teleport.ToY, world),
            CheckAgainstForecast(teleport),
        };
        _log.Rows.Insert(0, row);
        _log.Rows[0].Tag = teleport;
        if (_log.Rows.Count > LogRows)
            _log.Rows.RemoveAt(_log.Rows.Count - 1);
    }

    protected override void OnTeleportFocusChanged()
    {
        var target = State.TeleportTarget;
        _showingTarget = true;
        if (target != null)
        {
            _targetX.Value = Math.Min(_targetX.Maximum, target.TileX);
            _targetY.Value = Math.Min(_targetY.Maximum, target.TileY);
            _radius.Value = Math.Max(_radius.Minimum, Math.Min(_radius.Maximum, target.Radius));
        }

        _showingTarget = false;
        _grid.Invalidate();
        ShowMatches();
    }

    private void RequestForecast()
    {
        if (!State.IsConnected)
            return;

        _awaitedRequest = ++_lastRequest;
        State.Send(new TeleportRequest { RequestId = _awaitedRequest, Kind = TeleportKind.Potion, Rows = SelectedRows });
        ShowHeader();
    }

    private void UpdateNotice()
    {
        var problem = Problem();
        ShowNotice(_notice, problem);
        if (_stale == (problem != null))
            return;

        _stale = problem != null;
        _grid.Invalidate();
    }

    private string? Problem()
    {
        if (!State.IsConnected)
            return "Not connected to the game.";
        if (_forecast != null && (!State.IsPaused || State.Status!.Frame != _forecast.Frame))
            return $"Forecast taken at frame {_forecast.Frame}; the game is at frame {State.Status?.Frame}. It refreshes when the game pauses.";
        return null;
    }

    private void ShowHeader()
    {
        var parts = new List<string>();
        if (_forecast is { } forecast)
        {
            parts.Add($"frame {forecast.Frame}, computed in {forecast.Milliseconds} ms");
            parts.Add($"you: {WorldCoordinates.AtPosition(forecast.PlayerX, forecast.PlayerY, forecast.World)}");
            if (Column is { } column)
            {
                var lastFrame = State.World is { } world && world.Frame == forecast.Frame
                    ? world.Streams.FirstOrDefault(stream => stream.Name == column.Stream)?.LastFrameDraws ?? -1
                    : -1;
                parts.Add($"{column.Stream}{(column.StartPosition >= 0 ? $" @ {column.StartPosition:N0}" : "")}{(lastFrame >= 0 ? $" (+{lastFrame:N0} last frame)" : "")}");
            }
        }

        if (_awaitedRequest >= 0)
            parts.Add("computing…");
        _details.Text = string.Join(" · ", parts);
        _roll.Text = _forecast == null ? ""
            : _forecast.RollFrame < 0 ? "Not drinking one now. A potion rolls its destination on the 16th frame of its use, when its use timer is down to 2."
            : _forecast.RollFrame == _forecast.Frame ? $"Drinking one: the destination rolls during the next frame ({_forecast.RollFrame})."
            : $"Drinking one: the destination rolls during frame {_forecast.RollFrame}, {_forecast.RollFrame - _forecast.Frame} frames after the next one.";
    }

    private object Cell(int row, int column)
    {
        if (Column is not { } forecastColumn || row >= forecastColumn.Destinations.Count)
            return "";

        var destination = forecastColumn.Destinations[row];
        if (column == 0)
            return $"+{row}";
        if (column == 5)
            return destination.Draws;
        if (!destination.Found)
            return column == TileColumn ? "no spot found: you stay" : "";

        var world = _forecast!.World;
        var centerX = destination.X + WorldCoordinates.PlayerWidth / 2;
        var bottom = destination.Y + WorldCoordinates.PlayerHeight;
        return column switch
        {
            TileColumn => $"{destination.TileX}, {destination.TileY}",
            2 => WorldCoordinates.Compass(centerX, world),
            3 => WorldCoordinates.Depth(bottom, destination.Y + WorldCoordinates.PlayerHeight / 2, world),
            DistanceColumn => State.TeleportTarget is { } target ? (object)Math.Round(target.DistanceTo(destination)) : "",
            _ => "",
        };
    }

    private void FormatCell(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.CellStyle is not { } style || Column is not { } column || e.RowIndex >= column.Destinations.Count)
            return;

        var destination = column.Destinations[e.RowIndex];
        if (State.TeleportTarget is { } target && target.Contains(destination))
            style.BackColor = Grids.Match;
        else if (e.RowIndex == 0)
            style.BackColor = NextDraw;
        if (_stale || !destination.Found)
            style.ForeColor = SystemColors.GrayText;
    }

    private void ShowMatches()
    {
        _grid.Invalidate();
        if (State.TeleportTarget is not { } target)
        {
            _matches.Text = "No target: right-click the world map, or type a tile above and press Look there.";
            return;
        }

        if (Column is not { } column)
        {
            _matches.Text = "";
            return;
        }

        var offsets = column.Destinations.Select((destination, offset) => (destination, offset))
            .Where(entry => target.Contains(entry.destination))
            .Select(entry => entry.offset)
            .ToList();
        var first = string.Join(", ", offsets.Take(10).Select(offset => "+" + offset));
        _matches.Text = offsets.Count == 0
            ? $"Nothing lands within {target.Radius} tiles of {target.TileX}, {target.TileY} in the next {column.Destinations.Count} draws."
            : $"Within {target.Radius} tiles of {target.TileX}, {target.TileY}: {first}{(offsets.Count > 10 ? " …" : "")}  ({offsets.Count} of {column.Destinations.Count})";
    }

    private void SelectNextMatch()
    {
        if (State.TeleportTarget is not { } target || Column is not { } column)
            return;

        var start = (_grid.CurrentCell?.RowIndex ?? -1) + 1;
        for (var row = start; row < column.Destinations.Count; row++)
        {
            if (!target.Contains(column.Destinations[row]))
                continue;
            _grid.CurrentCell = _grid.Rows[row].Cells[0];
            return;
        }
    }

    private void ShowRowOnMap(int row)
    {
        if (Column is { } column && row >= 0 && row < column.Destinations.Count && column.Destinations[row].Found)
            _showOnMap(column.Destinations[row].TileX, column.Destinations[row].TileY);
        else if (State.TeleportTarget is { } target)
            _showOnMap(target.TileX, target.TileY);
    }

    /// <summary>Compares a real teleport with the forecast taken at the start of its frame, when there is one.</summary>
    private string CheckAgainstForecast(TeleportRecord teleport)
    {
        var forecast = _recent.LastOrDefault(f => f.Frame == teleport.Frame);
        var column = forecast?.Columns.FirstOrDefault(c => c.Stream == teleport.Stream);
        if (column == null || teleport.Offset < 0)
            return "";
        if (teleport.Offset >= column.Destinations.Count)
            return $"offset beyond the {column.Destinations.Count} rows";

        var predicted = column.Destinations[(int)teleport.Offset];
        var predictedX = predicted.Found ? predicted.X : teleport.FromX;
        var predictedY = predicted.Found ? predicted.Y : teleport.FromY;
        return predictedX == teleport.ToX && predictedY == teleport.ToY
            ? "✓ as forecast"
            : $"✗ forecast said {(predicted.Found ? $"tile {predicted.TileX}, {predicted.TileY}" : "no spot found")}";
    }

    /// <summary>A label and its control, kept on the same line when the toolbar wraps.</summary>
    private static Control Labeled(string text, Control control, int leftGap)
    {
        var pair = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(leftGap, 0, 0, 0) };
        pair.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        pair.Controls.Add(control);
        return pair;
    }
}
