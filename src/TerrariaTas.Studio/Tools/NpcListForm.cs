using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using TerrariaTas.Core.Studio;
using TerrariaTas.Studio.Connection;

namespace TerrariaTas.Studio.Tools;

/// <summary>NPCs alive in the world, filtered by category. Double-click one to open its drop forecast.</summary>
internal sealed class NpcListForm : ToolWindow
{
    private const int ValueColumn = 7;

    private readonly HashSet<NpcCategory> _categories;
    private readonly Action<int> _openForecast;
    private readonly DataGridView _grid = Grids.Create();
    private readonly DataGridViewColumn _distance;
    private readonly Label _notice = NoticeLabel();
    private readonly Label _summary = new() { AutoSize = true, Margin = new Padding(0, 7, 16, 0) };

    private NpcListForm(StudioState state, string key, string title, Action<int> openForecast, params NpcCategory[] categories)
        : base(state, key, title)
    {
        _categories = categories.ToHashSet();
        _openForecast = openForecast;
        ClientSize = new Size(900, 360);

        _distance = Grids.Number("Distance", 45, "0.0");
        _grid.Columns.AddRange(
            Grids.Number("Slot", 30),
            Grids.Column("Name", 110),
            Grids.Number("HP", 35),
            Grids.Number("Max HP", 35),
            Grids.Number("Tile X", 40),
            Grids.Number("Tile Y", 40),
            _distance,
            Grids.Number("Value", 45),
            Grids.Column("Velocity", 60),
            Grids.Column("Notes", 50),
            Grids.Column("AI", 110));
        _grid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex == ValueColumn && e.Value is long copper)
            {
                e.Value = Coins.Format(copper);
                e.FormattingApplied = true;
            }
        };
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
                OpenForecastForSelection();
        };

        var forecast = new Button { Text = "Drop forecast…", AutoSize = true };
        forecast.Click += (_, _) => OpenForecastForSelection();
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        toolbar.Controls.Add(_summary);
        toolbar.Controls.Add(forecast);
        toolbar.Controls.Add(new Label { Text = "or double-click a row.", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(4, 7, 0, 0) });

        Controls.Add(_grid);
        Controls.Add(_notice);
        Controls.Add(toolbar);
    }

    public static NpcListForm Monsters(StudioState state, Action<int> openForecast) =>
        new(state, "monsters", "Monsters & bosses", openForecast, NpcCategory.Enemy, NpcCategory.Boss);

    public static NpcListForm TownAndCritters(StudioState state, Action<int> openForecast) =>
        new(state, "town", "Town NPCs & critters", openForecast, NpcCategory.TownNpc, NpcCategory.Critter, NpcCategory.Friendly);

    protected override void OnStatusChanged() => ShowNotice(_notice, StaleReason());

    protected override void OnConnectionChanged() => ShowNotice(_notice, StaleReason());

    protected override void OnWorldChanged()
    {
        ShowNotice(_notice, StaleReason());
        if (State.World is not { } world)
            return;

        var npcs = world.Npcs.Where(npc => _categories.Contains(npc.Category)).ToList();
        _summary.Text = $"{npcs.Count} at frame {world.Frame}";
        Grids.Refill(_grid, npcs.Select(npc => ((object)npc.Slot, Row(npc))), _distance);
    }

    private static object[] Row(NpcView npc) => new object[]
    {
        npc.Slot,
        npc.Name,
        npc.Life,
        npc.LifeMax,
        (int)Math.Floor(npc.X / 16),
        (int)Math.Floor(npc.Y / 16),
        npc.Distance,
        npc.Copper,
        $"{npc.VelocityX:0.00}, {npc.VelocityY:0.00}",
        Notes(npc),
        string.Join("  ", npc.Ai.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture))),
    };

    private static string Notes(NpcView npc)
    {
        var notes = new List<string>();
        if (npc.Category == NpcCategory.Boss)
            notes.Add("boss");
        if (npc.FromStatue)
            notes.Add("statue");
        if (npc.Category is NpcCategory.Critter or NpcCategory.TownNpc or NpcCategory.Friendly)
            notes.Add(npc.Category == NpcCategory.TownNpc ? "town" : npc.Category.ToString().ToLowerInvariant());
        return string.Join(", ", notes);
    }

    private void OpenForecastForSelection()
    {
        if (_grid.CurrentRow?.Tag is int slot)
            _openForecast(slot);
    }
}
