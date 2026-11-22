using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TerrariaTas.Core.Movies;
using TerrariaTas.Core.Studio;
using TerrariaTas.Studio.Connection;
using MovieButtons = TerrariaTas.Core.Movies.MouseButtons;

namespace TerrariaTas.Studio.Tools;

/// <summary>
/// The movie frame by frame around the current frame, with its savestates: load a slot from the frame it
/// was taken at, save the current frame, or run to any frame.
/// </summary>
internal sealed class TimelineForm : ToolWindow
{
    private const int FramesShownBefore = 5;
    private const int MarkerColumn = 0;
    private const int FrameColumn = 1;
    private const int SavestateColumn = 6;

    private readonly DataGridView _grid = Grids.Create();
    private readonly Label _summary = new() { AutoSize = true, Margin = new Padding(0, 6, 16, 0) };
    private readonly CheckBox _follow = new() { Text = "Follow the current frame", Checked = true, AutoSize = true };
    private readonly Label[] _slotNames = new Label[StudioProtocol.SlotCount];
    private readonly Label[] _slotFrames = new Label[StudioProtocol.SlotCount];
    private readonly Button[] _loadButtons = new Button[StudioProtocol.SlotCount];
    private readonly Button[] _saveButtons = new Button[StudioProtocol.SlotCount];
    private readonly Font _currentSlotFont;
    private HashSet<int> _breakpoints = new();

    public TimelineForm(StudioState state)
        : base(state, "timeline", "Timeline & savestates")
    {
        ClientSize = new Size(980, 440);
        MinimumSize = new Size(640, 260);
        _currentSlotFont = new Font(Font, FontStyle.Bold);

        _grid.VirtualMode = true;
        _grid.Columns.AddRange(
            Grids.Column("", 10, DataGridViewContentAlignment.MiddleCenter),
            Grids.Number("Frame", 35),
            Grids.Number("Line", 25),
            Grids.Column("Keys", 120),
            Grids.Column("Mouse", 45),
            Grids.Column("Cursor", 55),
            new DataGridViewLinkColumn { HeaderText = "Savestate", FillWeight = 60, TrackVisitedState = false });
        foreach (DataGridViewColumn column in _grid.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        _grid.CellValueNeeded += (_, e) => e.Value = CellText(e.RowIndex, e.ColumnIndex);
        _grid.CellFormatting += OnCellFormatting;
        _grid.CellContentClick += OnCellContentClick;
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
                State.Run(CommandKind.RunToFrame, e.RowIndex);
        };
        _grid.CellMouseDown += OnCellMouseDown;
        _grid.ContextMenuStrip = new ContextMenuStrip();
        _grid.ContextMenuStrip.Opening += OnRowMenuOpening;

        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        header.Controls.Add(_summary);
        header.Controls.Add(_follow);
        header.Controls.Add(new Label { Text = "Double-click a frame to run to it.", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(16, 6, 0, 0) });
        _follow.CheckedChanged += (_, _) => FollowCurrentFrame();

        Controls.Add(_grid);
        Controls.Add(SlotPanel());
        Controls.Add(header);
    }

    protected override void OnMovieChanged()
    {
        _breakpoints = State.Movie.BreakpointFrames.ToHashSet();
        Refresh(scroll: true);
    }

    protected override void OnStatusChanged() => Refresh(scroll: true);

    protected override void OnConnectionChanged() => Refresh(scroll: false);

    private int CurrentFrame => State.Status?.Frame ?? 0;

    private void Refresh(bool scroll)
    {
        var movie = State.Movie;
        var rows = Math.Max(movie.TotalFrames, CurrentFrame) + 1;
        if (_grid.RowCount != rows)
            _grid.RowCount = rows;

        _summary.Text = State.Status is { } status
            ? $"{State.Hello?.MovieName ?? "movie"} · {status.TotalFrames} frames · {(status.Recording ? "recording, " : "")}{status.State.ToString().ToLowerInvariant()} at frame {status.Frame}"
            : "Waiting for the game…";
        RefreshSlots();
        if (scroll)
            FollowCurrentFrame();
        _grid.Invalidate();
    }

    private void FollowCurrentFrame()
    {
        if (!_follow.Checked || _grid.RowCount == 0)
            return;

        var first = Math.Max(0, Math.Min(CurrentFrame - FramesShownBefore, _grid.RowCount - 1));
        if (_grid.FirstDisplayedScrollingRowIndex != first)
            _grid.FirstDisplayedScrollingRowIndex = first;
    }

    private object CellText(int frame, int column)
    {
        var movie = State.Movie;
        var line = frame < movie.TotalFrames ? movie.LineAt(frame) : null;
        return column switch
        {
            MarkerColumn => frame == CurrentFrame ? "▶" : _breakpoints.Contains(frame) ? "●" : "",
            FrameColumn => frame,
            2 => line is { SourceLine: > 0 } ? (object)line.SourceLine : "",
            3 => line == null ? (frame == movie.TotalFrames ? EndOfMovie : "") : string.Join(" ", line.Keys),
            4 => line == null ? "" : ButtonsText(line),
            5 => line?.Cursor?.ToString() ?? "",
            SavestateColumn => SavestateLink(frame),
            _ => "",
        };
    }

    private string EndOfMovie => State.Status?.Recording == true ? "end of movie (recording)" : "end of movie (real input after this, not recorded)";

    private static string ButtonsText(InputLine line)
    {
        var parts = new List<string>();
        foreach (var (button, name) in new[] { (MovieButtons.Left, "LMB"), (MovieButtons.Right, "RMB"), (MovieButtons.Middle, "MMB"), (MovieButtons.X1, "XB1"), (MovieButtons.X2, "XB2") })
        {
            if ((line.Buttons & button) != 0)
                parts.Add(name);
        }

        if (line.Wheel != 0)
            parts.Add(line.Wheel > 0 ? $"Wheel+{line.Wheel}" : $"Wheel{line.Wheel}");
        return string.Join(" ", parts);
    }

    private string SavestateLink(int frame)
    {
        if (State.Status is not { } status)
            return "";
        if (frame == status.Frame && status.Phase == SessionPhase.Running)
            return Hotkeys.Label($"Save to slot {status.Slot}", CommandKind.SaveSlot);

        var slots = SlotsAt(frame);
        return slots.Count switch
        {
            0 => "",
            1 => $"Load slot {SlotName(slots[0])}",
            _ => $"Load slot {SlotName(slots[0])} (also {string.Join(", ", slots.Skip(1).Select(SlotName))})",
        };
    }

    private List<int> SlotsAt(int frame) =>
        State.Status?.SlotFrames.Select((slotFrame, slot) => (slotFrame, slot)).Where(s => s.slotFrame == frame).Select(s => s.slot).ToList()
        ?? new List<int>();

    private static string SlotName(int slot) => slot == 0 ? "0 (start)" : slot.ToString();

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        var frame = e.RowIndex;
        if (frame == CurrentFrame)
            e.CellStyle!.BackColor = Grids.Highlight;
        else if (frame == CurrentFrame - 1)
            e.CellStyle!.BackColor = Grids.SoftHighlight;
        else if (e.ColumnIndex == SavestateColumn && SlotsAt(frame).Count > 0)
            e.CellStyle!.BackColor = Grids.Savestate;

        if (e.ColumnIndex == MarkerColumn && _breakpoints.Contains(frame) && frame != CurrentFrame)
            e.CellStyle!.ForeColor = Color.Firebrick;
    }

    private void OnCellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != SavestateColumn || State.Status is not { } status)
            return;

        if (e.RowIndex == status.Frame)
            State.Run(CommandKind.SaveSlot, status.Slot);
        else if (SlotsAt(e.RowIndex) is { Count: > 0 } slots)
            State.Run(CommandKind.LoadSlot, slots[0]);
    }

    private void OnCellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        // Right-click selects the row first, so the menu acts on the frame under the cursor.
        if (e.Button == System.Windows.Forms.MouseButtons.Right && e.RowIndex >= 0)
            _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[FrameColumn];
    }

    private void OnRowMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var menu = _grid.ContextMenuStrip!;
        menu.Items.Clear();
        if (_grid.CurrentCell == null || State.Status == null)
        {
            e.Cancel = true;
            return;
        }

        var frame = _grid.CurrentCell.RowIndex;
        menu.Items.Add($"Run to frame {frame}", null, (_, _) => State.Run(CommandKind.RunToFrame, frame));
        foreach (var slot in SlotsAt(frame))
            menu.Items.Add($"Load slot {SlotName(slot)}", null, (_, _) => State.Run(CommandKind.LoadSlot, slot));
    }

    private Control SlotPanel()
    {
        // One auto-sized row per slot, then a filler row that keeps them packed at the top.
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 300,
            Padding = new Padding(8, 4, 8, 4),
            ColumnCount = 4,
            RowCount = StudioProtocol.SlotCount + 1,
            AutoScroll = true,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var row = 0; row < StudioProtocol.SlotCount; row++)
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        for (var slot = 0; slot < StudioProtocol.SlotCount; slot++)
        {
            var target = slot;
            _slotNames[slot] = new Label { Text = slot == 0 ? "Start" : $"Slot {slot}", AutoSize = true, Anchor = AnchorStyles.Left };
            _slotFrames[slot] = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText };
            _loadButtons[slot] = new Button { Text = "Load", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            _loadButtons[slot].Click += (_, _) => State.Run(CommandKind.LoadSlot, target);
            _saveButtons[slot] = new Button { Text = "Save here", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Visible = slot > 0 };
            _saveButtons[slot].Click += (_, _) => State.Run(CommandKind.SaveSlot, target);

            table.Controls.Add(_slotNames[slot], 0, slot);
            table.Controls.Add(_slotFrames[slot], 1, slot);
            table.Controls.Add(_loadButtons[slot], 2, slot);
            table.Controls.Add(_saveButtons[slot], 3, slot);
        }

        return table;
    }

    private void RefreshSlots()
    {
        var status = State.Status;
        var canAct = State.IsConnected && status?.Phase == SessionPhase.Running;
        for (var slot = 0; slot < StudioProtocol.SlotCount; slot++)
        {
            var frame = status != null && slot < status.SlotFrames.Length ? status.SlotFrames[slot] : -1;
            _slotFrames[slot].Text = frame >= 0 ? $"frame {frame}" : "empty";
            _loadButtons[slot].Enabled = canAct && frame >= 0;
            _saveButtons[slot].Enabled = canAct;
            var current = status?.Slot == slot;
            _slotNames[slot].Font = current ? _currentSlotFont : Font;
            _slotNames[slot].Text = (slot == 0 ? "Start" : $"Slot {slot}") + (current ? " ◀" : "");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _currentSlotFont.Dispose();
        base.Dispose(disposing);
    }
}
