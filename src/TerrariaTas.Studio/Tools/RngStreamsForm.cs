using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using TerrariaTas.Core.Rng;
using TerrariaTas.Core.Studio;
using TerrariaTas.Studio.Connection;

namespace TerrariaTas.Studio.Tools;

/// <summary>
/// Every random number generator of the game: how far it is (draws since seeded), how much it moved on the
/// last frame and its upcoming values, computed here from its state with the same algorithm as the game.
/// </summary>
internal sealed class RngStreamsForm : ToolWindow
{
    private const int PositionColumn = 2;
    private const int LastFrameColumn = 3;

    private static readonly Dictionary<string, string> Uses = new()
    {
        ["Main.rand"] = "Shared: UI (reforging, crafting, bags), weather, drawing",
        ["DoUpdateInWorld"] = "World update, between phases",
        ["UpdateParticleSystems_World"] = "Particle systems",
        ["UpdatePlayers"] = "Players: swings and swing kills, footsteps, item use",
        ["SpawnNPCs"] = "Spawn phase",
        ["SpawnNPC"] = "Enemy spawns",
        ["UpdatePressurePlates"] = "Pressure plates",
        ["UpdateNPCs"] = "NPC AI; kills by lava, debuffs or other NPCs",
        ["UpdateGores"] = "Gore",
        ["UpdateProjectiles"] = "Projectiles and projectile kills",
        ["UpdateItems"] = "Items on the ground",
        ["UpdateDust"] = "Dust",
        ["UpdateLeashedEntities"] = "Leashed entities",
        ["UpdateFloatingText"] = "Floating text",
        ["UpdateTime"] = "Dusk/dawn events: Blood Moon, eclipse, invasions, meteors, merchant",
        ["UpdateWorldGen"] = "Random tile updates: grass, trees, Plantera bulbs, life fruit",
        ["UpdateServerOrClient"] = "Network bookkeeping",
        ["UpdateLiquid"] = "Liquids",
    };

    private readonly DataGridView _streams = Grids.Create();
    private readonly DataGridView _values = Grids.Create();
    private readonly NumericUpDown _count = new() { Minimum = 10, Maximum = 100_000, Value = 200, Increment = 100, Width = 80 };
    private readonly NumericUpDown _range = new() { Minimum = 1, Maximum = int.MaxValue, Value = 100, Width = 90 };
    private readonly Label _notice = NoticeLabel();
    private readonly Label _valuesTitle = new() { AutoSize = true, Margin = new Padding(0, 7, 16, 0) };
    private RngStreamView? _selected;
    private double[] _upcoming = Array.Empty<double>();

    public RngStreamsForm(StudioState state)
        : base(state, "rng", "RNG streams")
    {
        ClientSize = new Size(820, 620);

        _streams.Columns.AddRange(
            Grids.Column("Stream", 70),
            Grids.Column("Used for", 140),
            Grids.Number("Position", 50),
            Grids.Number("Last frame", 40),
            Grids.Column("Next values", 90));
        _streams.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex is PositionColumn or LastFrameColumn && e.Value is long value && value < 0)
            {
                e.Value = "?";
                e.FormattingApplied = true;
            }
        };
        _streams.SelectionChanged += (_, _) => SelectStream();

        _values.VirtualMode = true;
        _values.Columns.AddRange(
            Grids.Number("Offset", 30),
            Grids.Number("Position", 50),
            Grids.Number("Value", 50),
            Grids.Number("Next(n)", 40));
        foreach (DataGridViewColumn column in _values.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        _values.CellValueNeeded += (_, e) => e.Value = ValueCell(e.RowIndex, e.ColumnIndex);
        _count.ValueChanged += (_, _) => ComputeUpcoming();
        _range.ValueChanged += (_, _) => _values.Invalidate();

        var valuesBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        valuesBar.Controls.Add(_valuesTitle);
        valuesBar.Controls.Add(new Label { Text = "Count", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        valuesBar.Controls.Add(_count);
        valuesBar.Controls.Add(new Label { Text = "n for Next(n)", AutoSize = true, Margin = new Padding(16, 7, 4, 0) });
        valuesBar.Controls.Add(_range);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 260 };
        split.Panel1.Controls.Add(_streams);
        split.Panel2.Controls.Add(_values);
        split.Panel2.Controls.Add(valuesBar);

        Controls.Add(split);
        Controls.Add(_notice);
        Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8, 6, 8, 4),
            Text = "Every world-update phase draws from its own stream, seeded from the movie's seed and the stream's name. Offset k is the value the (k+1)-th next draw returns; Next(n) is what a roll out of n gives with it.",
            MaximumSize = new Size(800, 0),
        });
    }

    protected override void OnStatusChanged() => ShowNotice(_notice, StaleReason());

    protected override void OnConnectionChanged() => ShowNotice(_notice, StaleReason());

    protected override void OnWorldChanged()
    {
        ShowNotice(_notice, StaleReason());
        if (State.World is not { } world)
            return;

        Grids.Refill(_streams, world.Streams.Select(stream => ((object)stream.Name, Row(stream))), defaultSort: null);
        SelectStream();
    }

    private static object[] Row(RngStreamView stream)
    {
        var model = TerrariaRandom.FromState(stream.State, stream.Index);
        var preview = string.Join("  ", Enumerable.Range(0, 4).Select(_ => model.NextDouble().ToString("0.0000", CultureInfo.InvariantCulture)));
        return new object[] { stream.Name, Uses.GetValueOrDefault(stream.Name, ""), stream.Position, stream.LastFrameDraws, preview };
    }

    private void SelectStream()
    {
        var name = _streams.CurrentRow?.Tag as string;
        _selected = State.World?.Streams.FirstOrDefault(stream => stream.Name == name);
        ComputeUpcoming();
    }

    private void ComputeUpcoming()
    {
        if (_selected == null)
        {
            _upcoming = Array.Empty<double>();
            _valuesTitle.Text = "Select a stream";
        }
        else
        {
            var model = TerrariaRandom.FromState(_selected.State, _selected.Index);
            _upcoming = new double[(int)_count.Value];
            for (var i = 0; i < _upcoming.Length; i++)
                _upcoming[i] = model.NextDouble();
            _valuesTitle.Text = $"Upcoming values of {_selected.Name}";
        }

        _values.RowCount = _upcoming.Length;
        _values.Invalidate();
    }

    private object ValueCell(int offset, int column)
    {
        if (offset >= _upcoming.Length || _selected == null)
            return "";

        return column switch
        {
            0 => $"+{offset}",
            1 => _selected.Position >= 0 ? (object)(_selected.Position + offset) : "?",
            2 => _upcoming[offset].ToString("0.000000", CultureInfo.InvariantCulture),
            _ => (long)(_upcoming[offset] * (double)_range.Value),
        };
    }
}
