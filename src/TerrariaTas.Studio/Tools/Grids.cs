using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace TerrariaTas.Studio.Tools;

/// <summary>Read-only grids with a consistent look.</summary>
internal static class Grids
{
    public static readonly Color Highlight = Color.FromArgb(254, 240, 138);
    public static readonly Color SoftHighlight = Color.FromArgb(254, 249, 195);
    public static readonly Color Match = Color.FromArgb(187, 247, 208);
    public static readonly Color Savestate = Color.FromArgb(220, 252, 231);

    public static DataGridView Create()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(191, 219, 254);
        grid.DefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);

        // Grids refresh on every frame advance; without double buffering they flicker.
        typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(grid, true);
        return grid;
    }

    public static DataGridViewTextBoxColumn Column(string header, float weight = 100, DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft) => new()
    {
        HeaderText = header,
        FillWeight = weight,
        SortMode = DataGridViewColumnSortMode.Automatic,
        DefaultCellStyle = { Alignment = alignment },
    };

    public static DataGridViewTextBoxColumn Number(string header, float weight = 50, string format = "") =>
        new()
        {
            HeaderText = header,
            FillWeight = weight,
            SortMode = DataGridViewColumnSortMode.Automatic,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight, Format = format },
        };

    /// <summary>
    /// Replaces every row, keeping the sort order, the selected row (identified by its tag) and the scroll
    /// position. Rows stay in the given order until the user sorts, unless a default sort column is given.
    /// </summary>
    public static void Refill(DataGridView grid, IEnumerable<(object Tag, object[] Values)> rows, DataGridViewColumn? defaultSort)
    {
        var selected = grid.CurrentRow?.Tag;
        var sortColumn = grid.SortedColumn ?? defaultSort;
        var direction = grid.SortOrder == SortOrder.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        var scroll = grid.FirstDisplayedScrollingRowIndex;

        grid.Rows.Clear();
        foreach (var (tag, values) in rows)
            grid.Rows[grid.Rows.Add(values)].Tag = tag;
        if (sortColumn != null)
            grid.Sort(sortColumn, direction);

        if (scroll >= 0 && scroll < grid.RowCount)
            grid.FirstDisplayedScrollingRowIndex = scroll;
        grid.ClearSelection();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (!Equals(row.Tag, selected))
                continue;
            row.Selected = true;
            grid.CurrentCell = row.Cells[0];
            break;
        }
    }
}
