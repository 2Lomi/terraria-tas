using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TerrariaTas.Core.Studio;
using TerrariaTas.Studio.Connection;

namespace TerrariaTas.Studio.Tools;

/// <summary>
/// The whole world as the game's map shows it, fully revealed, to pan and zoom: where you are, where the spawn is,
/// and where the next Teleportation Potions would land. A right click picks the place the teleport forecast looks for.
/// </summary>
internal sealed class MapForm : ToolWindow
{
    private const string Help = "Drag to move, wheel to zoom, right-click to look for teleports landing there.";
    private static readonly int[] DestinationChoices = { 0, 5, 10, 20, 50, 100 };

    private readonly StudioSettings _settings;
    private readonly MapView _view = new() { Dock = DockStyle.Fill };
    private readonly Label _info = new() { AutoSize = true, Margin = new Padding(16, 7, 0, 0), UseMnemonic = false };
    private readonly Label _readout = new() { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8, 4, 8, 4), UseMnemonic = false, Text = Help };
    private readonly Label _notice = NoticeLabel();
    private readonly ComboBox _destinations = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };

    public MapForm(StudioState state, StudioSettings settings)
        : base(state, "map", "World map")
    {
        _settings = settings;
        ClientSize = new Size(1100, 680);
        MinimumSize = new Size(480, 320);

        _destinations.Items.AddRange(DestinationChoices.Select(count => (object)count).ToArray());
        _destinations.SelectedItem = DestinationChoices.Contains(settings.MapDestinations) ? settings.MapDestinations : 20;
        _destinations.SelectedIndexChanged += (_, _) =>
        {
            _settings.MapDestinations = _destinations.SelectedItem as int? ?? 20;
            ShowTeleports();
        };

        var refresh = new Button { Text = "Read the map again", AutoSize = true };
        refresh.Click += (_, _) => State.RequestMap();
        var fit = new Button { Text = "Whole world", AutoSize = true };
        fit.Click += (_, _) => _view.Fit();
        var player = new Button { Text = "Center on you", AutoSize = true };
        player.Click += (_, _) => CenterOnPlayer();
        var clearTarget = new Button { Text = "Clear target", AutoSize = true };
        clearTarget.Click += (_, _) => State.SetTeleportTarget(null);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        toolbar.Controls.Add(refresh);
        toolbar.Controls.Add(fit);
        toolbar.Controls.Add(player);
        toolbar.Controls.Add(new Label { Text = "Teleport destinations", AutoSize = true, Margin = new Padding(16, 7, 4, 0) });
        toolbar.Controls.Add(_destinations);
        toolbar.Controls.Add(clearTarget);
        toolbar.Controls.Add(_info);

        _view.PointerMoved += () => _readout.Text = _view.PointerReadout ?? Help;
        _view.TileRightClicked += (x, y) => State.SetTeleportTarget(new TeleportTarget(x, y, _settings.TeleportRadius));

        Controls.Add(_view);
        Controls.Add(_readout);
        Controls.Add(_notice);
        Controls.Add(toolbar);
    }

    /// <summary>Puts a tile in the middle of the view, close enough to see the tiles around it.</summary>
    public void CenterOn(int tileX, int tileY) => _view.CenterOn(tileX + 0.5f, tileY + 0.5f, minScale: 4f);

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (State.Map == null && !State.MapRequested)
            State.RequestMap();
        OnMapChanged(); // a map read while this window was closed
        ShowTeleports();
        OnTeleportFocusChanged();
    }

    protected override void OnConnectionChanged()
    {
        if (State.IsConnected && State.Map == null && !State.MapRequested)
            State.RequestMap();
        ShowState();
    }

    protected override void OnStatusChanged()
    {
        _view.Player = State.Status?.Player;
        ShowState();
    }

    protected override void OnMapChanged()
    {
        _view.ShowMap(State.Map);
        ShowState();
    }

    protected override void OnTeleportForecastReceived(TeleportForecast forecast) => ShowTeleports();

    protected override void OnTeleportFocusChanged()
    {
        _view.Target = State.TeleportTarget;
        _view.SelectedOffset = State.SelectedTeleport;
        ShowTeleports();
    }

    private void CenterOnPlayer()
    {
        if (State.Status?.Player is { } player)
            _view.CenterOn(player.X / 16f, player.Y / 16f, minScale: 2f);
    }

    /// <summary>The first destinations of the latest teleport forecast, plus the one selected in the teleport window.</summary>
    private void ShowTeleports()
    {
        var count = _destinations.SelectedItem as int? ?? 20;
        var column = State.LatestTeleports?.Columns.FirstOrDefault();
        if (column == null)
        {
            _view.Destinations = Array.Empty<(int, TeleportDestination)>();
            return;
        }

        var shown = column.Destinations.Select((destination, offset) => (Offset: offset, Destination: destination))
            .Where(entry => entry.Offset < count || entry.Offset == State.SelectedTeleport)
            .ToList();
        _view.Destinations = shown;
    }

    private void ShowState()
    {
        var map = State.Map;
        if (map == null)
        {
            _info.Text = "";
            ShowNotice(_notice, !State.IsConnected ? "Not connected to the game."
                : State.MapRequested ? "Waiting for the game to pause inside a world to read the map…"
                : "No map yet: press \"Read the map again\".");
            return;
        }

        var frames = map.FirstFrame == map.LastFrame ? $"frame {map.FirstFrame}" : $"frames {map.FirstFrame} to {map.LastFrame}";
        var progress = map.Complete ? "" : $" · reading rows {map.RowsReceived} / {map.World.Height}…";
        _info.Text = $"{map.WorldName} · {map.World.Width} × {map.World.Height} tiles · tiles as of {frames}{progress}";

        string? notice = null;
        if (map.Error.Length > 0)
            notice = map.Error;
        else if (State.MapRequested)
            notice = "Reading the map again when the game pauses inside a world…";
        ShowNotice(_notice, notice);
    }
}
