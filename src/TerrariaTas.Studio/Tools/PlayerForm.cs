using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using TerrariaTas.Core.Studio;
using TerrariaTas.Studio.Connection;

namespace TerrariaTas.Studio.Tools;

/// <summary>
/// The player as the last frame left it: position and velocity exactly as the game holds them (sub-pixels included),
/// what the last frame moved, jump, wing and dash timers, item use, health and buffs. Live while the game plays.
/// </summary>
internal sealed class PlayerForm : ToolWindow
{
    private const float TileSize = 16f;
    private const float FramesPerSecond = 60f;

    private static readonly string[] Rows =
    {
        "Position X", "Position Y", "Moved last frame", "Velocity", "Facing", "Jump", "Wings / rockets", "Dash", "Fall",
        "Held item", "Item use", "Life", "Mana", "Defense", "Breath", "Immunity", "Luck", "Death",
    };

    private readonly Label _summary = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 4) };

    // Shows the game's state; a click asks the game to switch (the box never switches by itself).
    private readonly CheckBox _overlay = new() { Text = "Show frame, position and velocity over the game", AutoSize = true, AutoCheck = false, Dock = DockStyle.Bottom, Padding = new Padding(8, 4, 8, 6) };
    private readonly DataGridView _values = Grids.Create();
    private readonly DataGridView _buffs = Grids.Create();

    public PlayerForm(StudioState state)
        : base(state, "player", "Player")
    {
        ClientSize = new Size(470, 620);
        MinimumSize = new Size(340, 360);

        _values.Columns.AddRange(Grids.Column("", 34), Grids.Column("Value", 100));
        _values.ColumnHeadersVisible = false;
        foreach (var column in _values.Columns.Cast<DataGridViewColumn>())
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        foreach (var row in Rows)
            _values.Rows.Add(row, "");
        _values.Dock = DockStyle.Top;
        _values.Height = _values.RowTemplate.Height * Rows.Length + 4;
        _values.ScrollBars = ScrollBars.None;

        _buffs.Columns.AddRange(Grids.Column("Buff", 100), Grids.Number("Frames left", 40), Grids.Number("Seconds", 35, "0.0"));

        var buffTitle = new Label { Text = "Buffs", Dock = DockStyle.Top, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(8, 8, 8, 4) };
        _overlay.Click += (_, _) => State.Run(CommandKind.ToggleOverlay);
        Controls.Add(_buffs);
        Controls.Add(_overlay);
        Controls.Add(buffTitle);
        Controls.Add(_values);
        Controls.Add(_summary);

        // Nothing is selected until the user clicks a row (to copy it with Ctrl+C): a highlighted row would read as a warning.
        _values.TabStop = _buffs.TabStop = false;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _values.ClearSelection();
        _buffs.ClearSelection();
    }

    protected override void OnConnectionChanged() => ShowPlayer();

    protected override void OnStatusChanged() => ShowPlayer();

    private void ShowPlayer()
    {
        var status = State.IsConnected ? State.Status : null;
        _overlay.Enabled = status != null;
        _overlay.Checked = status?.Overlay == true;
        _overlay.Text = Hotkeys.Label("Show frame, position and velocity over the game", CommandKind.ToggleOverlay);
        if (status?.Player is not { } player)
        {
            _summary.Text = State.IsConnected ? "No player yet: the game is not in a world." : "Not connected to the game.";
            return;
        }

        _summary.Text = $"Before frame {status.Frame} · {status.State.ToString().ToLowerInvariant()}";
        var movedX = (double)player.PositionX - player.OldPositionX;
        var movedY = (double)player.PositionY - player.OldPositionY;
        var fallen = (int)(player.PositionY / TileSize) - player.FallStart;
        string[] values =
        {
            Position(player.PositionX),
            Position(player.PositionY),
            $"{Signed(movedX)}, {Signed(movedY)}",
            $"{Exact(player.VelocityX)}, {Exact(player.VelocityY)}",
            (player.Direction < 0 ? "left" : "right") + (player.GravityDirection < 0 ? " · gravity flipped" : ""),
            player.Jump > 0 ? $"{player.Jump} frames of jump left" : "no jump going on",
            $"wing time {Exact(player.WingTime)} · rocket time {player.RocketTime}",
            player.DashDelay < 0 ? "dashing" : player.DashDelay > 0 ? $"next dash in {player.DashDelay} frames" : "ready",
            $"started at tile row {player.FallStart} ({fallen * (player.GravityDirection < 0 ? -1 : 1)} tiles fallen)",
            $"slot {player.SelectedItem + 1}: {(player.HeldItem.Length > 0 ? player.HeldItem : "empty")}",
            $"animation {player.ItemAnimation} · use time {player.ItemTime}",
            $"{player.Life} / {player.LifeMax}",
            $"{player.Mana} / {player.ManaMax}",
            player.Defense.ToString(CultureInfo.InvariantCulture),
            $"{player.Breath} / {player.BreathMax}",
            player.ImmuneTime > 0 ? $"{player.ImmuneTime} frames" : "none",
            Exact(player.Luck),
            player.Dead ? $"dead, respawns in {player.RespawnTimer} frames" : "alive",
        };

        for (var i = 0; i < values.Length; i++)
        {
            if (!Equals(_values.Rows[i].Cells[1].Value, values[i]))
                _values.Rows[i].Cells[1].Value = values[i];
        }

        Grids.Refill(_buffs, player.Buffs.Select(buff => ((object)buff.Type, new object[] { buff.Name, buff.Time, buff.Time / FramesPerSecond })), null);
        if (!_buffs.Focused)
            _buffs.ClearSelection();
    }

    /// <summary>Pixels exactly as the game holds them, then the tile and the position inside it.</summary>
    private static string Position(float pixels)
    {
        var tile = Math.Floor(pixels / TileSize);
        return $"{Exact(pixels)} px · tile {tile.ToString(CultureInfo.InvariantCulture)} + {Exact((float)(pixels - tile * TileSize))} px";
    }

    /// <summary>A float with every digit it holds: two different values never show the same.</summary>
    private static string Exact(float value) => value.ToString("G9", CultureInfo.InvariantCulture);

    private static string Signed(double value) => (value > 0 ? "+" : "") + value.ToString("0.#######", CultureInfo.InvariantCulture);
}
