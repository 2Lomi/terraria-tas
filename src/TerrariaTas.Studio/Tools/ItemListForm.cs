using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TerrariaTas.Core.Studio;
using TerrariaTas.Studio.Connection;

namespace TerrariaTas.Studio.Tools;

/// <summary>Items lying in the world, ready to be picked up.</summary>
internal sealed class ItemListForm : ToolWindow
{
    private const float FramesPerSecond = 60f;

    private readonly DataGridView _grid = Grids.Create();
    private readonly DataGridViewColumn _distance;
    private readonly Label _notice = NoticeLabel();
    private readonly Label _summary = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 4) };

    public ItemListForm(StudioState state)
        : base(state, "items", "Items on the ground")
    {
        ClientSize = new Size(640, 340);

        _distance = Grids.Number("Distance", 45, "0.0");
        _grid.Columns.AddRange(
            Grids.Number("Slot", 30),
            Grids.Column("Item", 130),
            Grids.Number("Stack", 35),
            Grids.Number("Tile X", 40),
            Grids.Number("Tile Y", 40),
            _distance,
            Grids.Number("Age (s)", 40, "0.0"));

        Controls.Add(_grid);
        Controls.Add(_notice);
        Controls.Add(_summary);
    }

    protected override void OnStatusChanged() => ShowNotice(_notice, StaleReason());

    protected override void OnConnectionChanged() => ShowNotice(_notice, StaleReason());

    protected override void OnWorldChanged()
    {
        ShowNotice(_notice, StaleReason());
        if (State.World is not { } world)
            return;

        _summary.Text = $"{world.Items.Count} items at frame {world.Frame}";
        Grids.Refill(_grid, world.Items.Select(item => ((object)item.Slot, Row(item))), _distance);
    }

    private static object[] Row(ItemView item) => new object[]
    {
        item.Slot,
        item.Name,
        item.Stack,
        (int)Math.Floor(item.X / 16),
        (int)Math.Floor(item.Y / 16),
        item.Distance,
        item.Age / FramesPerSecond,
    };
}
