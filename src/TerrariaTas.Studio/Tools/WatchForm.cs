using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TerrariaTas.Core.Studio;
using TerrariaTas.Core.Watch;
using TerrariaTas.Studio.Connection;

namespace TerrariaTas.Studio.Tools;

/// <summary>
/// The memory watch: paths into the game's objects with their values, live while the game plays. Fields and array
/// elements are read as they are; a property only when written <c>Name()</c>, since its getter runs game code. Studio
/// remembers the paths; "Log every frame" makes the game write the values of every frame to a CSV file.
/// </summary>
internal sealed class WatchForm : ToolWindow
{
    private static readonly Color ErrorColor = Color.FromArgb(185, 28, 28);

    private readonly StudioSettings _settings;
    private readonly DataGridView _grid = Grids.Create();
    private readonly TextBox _path = new() { Width = 320, Margin = new Padding(0, 3, 6, 0) };
    private readonly CheckBox _log = new() { Text = "Log every frame", AutoSize = true, Margin = new Padding(10, 6, 0, 0) };
    private readonly Label _notice = NoticeLabel();
    private long _version;

    public WatchForm(StudioState state, StudioSettings settings)
        : base(state, "watch", "Memory watch")
    {
        _settings = settings;
        ClientSize = new Size(620, 400);
        MinimumSize = new Size(420, 240);

        _grid.Columns.AddRange(Grids.Column("Path", 50), Grids.Column("Value", 50));
        foreach (var column in _grid.Columns.Cast<DataGridViewColumn>())
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        _grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
                RemoveSelected();
        };

        var add = new Button { Text = "Add", AutoSize = true };
        add.Click += (_, _) => AddPath();
        var remove = new Button { Text = "Remove", AutoSize = true };
        remove.Click += (_, _) => RemoveSelected();
        _path.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;
            AddPath();
            e.Handled = e.SuppressKeyPress = true;
        };
        _log.CheckedChanged += (_, _) => SendList();

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 2), WrapContents = false };
        toolbar.Controls.AddRange(new Control[] { _path, add, remove, _log });
        var help = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8, 2, 8, 6),
            ForeColor = SystemColors.GrayText,
            Text = "Start from player or a game class (Main, NPC, WorldGen...): player.velocity.X, Main.npc[3].ai[0], Main.windSpeedCurrent.\n"
                   + "A property is read only when written Name(): its getter runs game code. Delete removes the selected path.",
        };

        Controls.Add(_grid);
        Controls.Add(_notice);
        Controls.Add(help);
        Controls.Add(toolbar);

        foreach (var path in settings.Watches)
            _grid.Rows.Add(path, "");
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        SendList();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _grid.ClearSelection(); // nothing for Delete to remove until the user picks a path
        _path.Focus();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // Closing the window stops the watch (and its log) in the game.
        if (State.IsConnected)
            State.Send(new WatchList { Version = ++_version });
        base.OnFormClosed(e);
    }

    protected override void OnConnectionChanged()
    {
        // A new game knows nothing of this list yet.
        if (State.IsConnected)
            SendList();
        ShowValues();
    }

    protected override void OnStatusChanged() => ShowValues();

    private void AddPath()
    {
        var text = _path.Text.Trim();
        if (text.Length == 0)
            return;

        try
        {
            text = WatchPath.Parse(text).ToString();
        }
        catch (FormatException error)
        {
            ShowNotice(_notice, error.Message);
            return;
        }

        ShowNotice(_notice, null);
        _grid.Rows.Add(text, "");
        _path.Clear();
        SaveAndSend();
    }

    private void RemoveSelected()
    {
        foreach (var row in _grid.SelectedRows.Cast<DataGridViewRow>().ToList())
            _grid.Rows.Remove(row);
        SaveAndSend();
    }

    private void SaveAndSend()
    {
        _settings.Watches = Paths();
        SendList();
    }

    private void SendList()
    {
        if (!State.IsConnected)
            return;
        State.Send(new WatchList { Version = ++_version, Paths = Paths(), LogToFile = _log.Checked });
    }

    private System.Collections.Generic.List<string> Paths() =>
        _grid.Rows.Cast<DataGridViewRow>().Select(row => (string)row.Cells[0].Value).ToList();

    private void ShowValues()
    {
        var status = State.IsConnected ? State.Status : null;
        var values = status != null && status.WatchVersion == _version ? status.WatchValues : null;
        for (var i = 0; i < _grid.Rows.Count; i++)
        {
            var value = values != null && i < values.Length ? values[i]
                : !State.IsConnected ? "(not connected)"
                : status?.Player == null ? "(not in a world)"
                : "…";
            var cell = _grid.Rows[i].Cells[1];
            if (!Equals(cell.Value, value))
            {
                cell.Value = value;
                cell.Style.ForeColor = value.StartsWith("! ", StringComparison.Ordinal) ? ErrorColor : SystemColors.ControlText;
            }
        }
    }
}
