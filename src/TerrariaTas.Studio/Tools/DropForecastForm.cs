using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using TerrariaTas.Core.Studio;
using TerrariaTas.Studio.Connection;

namespace TerrariaTas.Studio.Tools;

/// <summary>
/// What an NPC drops depending on when its loot is rolled. Row k of a column is the loot if the roll
/// starts after k more draws on that stream, counted from the frame boundary the game is paused at.
/// The kill log underneath shows at which offset each real kill rolled, and whether the forecast was right.
/// </summary>
internal sealed class DropForecastForm : ToolWindow
{
    private const int RememberedForecasts = 30;
    private const int KillDropsColumn = 4;
    private static readonly int[] RowChoices = { 200, 500, 1000, 2000, 5000, 10000 };
    private static readonly int[] ShadeChoices = { 4, 5, 6, 8, 10, 12 };
    private static readonly Color NextDraw = Color.FromArgb(191, 219, 254);
    private static readonly Color SelectionOutline = Color.FromArgb(37, 99, 235);

    private static readonly Dictionary<string, string> KillKinds = new()
    {
        ["UpdatePlayers"] = "Swing kill",
        ["UpdateProjectiles"] = "Projectile kill",
        ["UpdateNPCs"] = "NPC-update kill",
    };

    private readonly StudioSettings _settings;
    private readonly Label _title = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 13f), Margin = new Padding(0, 0, 16, 0) };
    private readonly Label _details = new() { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
    private readonly Label _notes = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 2, 8, 2), ForeColor = Color.FromArgb(146, 64, 14) };
    private readonly Label _notice = NoticeLabel();
    private readonly Label _matches = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 2, 8, 4) };
    private readonly ComboBox _monster = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private readonly ComboBox _target = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox _rows = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly ComboBox _shades = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 50 };
    private readonly CheckBox _autoRefresh = new() { Text = "Refresh on every paused frame", Checked = true, AutoSize = true, Margin = new Padding(12, 6, 0, 0) };
    private readonly DataGridView _grid = Grids.Create();
    private readonly DataGridView _kills = Grids.Create();
    private readonly List<DropForecast> _recent = new();
    private readonly Font _matchFont;

    private Color[] _shadeColors;
    private bool _stale;
    private bool _fillingMonsters;
    private string _monsterListKey = "";
    private int _npcSlot = -1;
    private int _npcNetId;
    private int _lastRequest;
    private int _awaitedRequest = -1;
    private DropForecast? _forecast;

    public DropForecastForm(StudioState state, StudioSettings settings)
        : base(state, "forecast", "Drop forecast")
    {
        _settings = settings;
        ClientSize = new Size(1000, 720);
        _matchFont = new Font(Font, FontStyle.Bold);

        _rows.Items.AddRange(RowChoices.Select(rows => (object)rows).ToArray());
        _rows.SelectedItem = RowChoices.Contains(settings.ForecastRows) ? settings.ForecastRows : 1000;
        _rows.SelectedIndexChanged += (_, _) =>
        {
            _settings.ForecastRows = SelectedRows;
            RequestForecast();
        };
        _shades.Items.AddRange(ShadeChoices.Select(shades => (object)shades).ToArray());
        _shades.SelectedItem = ShadeChoices.Contains(settings.ForecastShades) ? settings.ForecastShades : 6;
        _shadeColors = Shades(SelectedShades);
        _shades.SelectedIndexChanged += (_, _) =>
        {
            _settings.ForecastShades = SelectedShades;
            _shadeColors = Shades(SelectedShades);
            _grid.Invalidate();
        };
        _target.SelectedIndexChanged += (_, _) => ShowMatches();
        _monster.SelectedIndexChanged += (_, _) =>
        {
            if (!_fillingMonsters && _monster.SelectedItem is MonsterChoice choice && (choice.Slot != _npcSlot || choice.NetId != _npcNetId))
                Target(choice.Slot);
        };

        _grid.VirtualMode = true;
        _grid.CellValueNeeded += (_, e) => e.Value = ForecastCell(e.RowIndex, e.ColumnIndex);
        _grid.CellPainting += PaintForecastCell;
        _grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex > 0 && OutcomeAt(e.RowIndex, e.ColumnIndex - 1) is { } outcome)
                e.ToolTipText = outcome.Describe();
        };
        _kills.CellPainting += PaintKillCell;

        _kills.Columns.AddRange(
            Grids.Number("Frame", 35),
            Grids.Column("NPC", 70),
            Grids.Column("Stream", 60),
            Grids.Number("Loot at offset", 45),
            Grids.Column("Drops", 140),
            Grids.Column("Forecast", 110));
        foreach (DataGridViewColumn column in _kills.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable; // newest first; offsets mix numbers and "?"

        var refresh = new Button { Text = "Refresh", AutoSize = true };
        refresh.Click += (_, _) => RequestForecast();
        var nextMatch = new Button { Text = "Next match ↓", AutoSize = true };
        nextMatch.Click += (_, _) => SelectNextMatch();

        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 0) };
        header.Controls.Add(_title);
        header.Controls.Add(_details);

        var controls = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 0) };
        controls.Controls.Add(Labeled("Monster", _monster, 0));
        controls.Controls.Add(Labeled("Item to find", _target, 16));
        controls.Controls.Add(nextMatch);
        controls.Controls.Add(Labeled("Rows", _rows, 16));
        controls.Controls.Add(Labeled("Shades", _shades, 16));
        controls.Controls.Add(refresh);
        controls.Controls.Add(_autoRefresh);

        var help = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8, 2, 8, 4),
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(980, 0),
            Text = "Row k = the loot if its roll starts k draws after this frame boundary, on the stream of the column. "
                   + "A kill spends draws before its loot (crit, damage, sounds, blood particles): the kill log shows the offset each real kill rolled at. "
                   + "Blue is the next draw. The grays follow the stream position: a roll keeps its shade from frame to frame, "
                   + "so the shades move up by the draws each frame made (the column titles give the exact count).",
        };

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 430 };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_kills);
        split.Panel2.Controls.Add(new Label { Text = "Kill log", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 2), Font = new Font(Font, FontStyle.Bold) });

        Controls.Add(split);
        Controls.Add(_matches);
        Controls.Add(help);
        Controls.Add(_notes);
        Controls.Add(_notice);
        Controls.Add(controls);
        Controls.Add(header);
        ShowHeader();
    }

    /// <summary>A label and its control, kept on the same line when the toolbar wraps.</summary>
    private static Control Labeled(string text, Control control, int leftGap)
    {
        var pair = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(leftGap, 0, 0, 0) };
        pair.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        pair.Controls.Add(control);
        return pair;
    }

    private int SelectedRows => _rows.SelectedItem as int? ?? 1000;

    private int SelectedShades => _shades.SelectedItem as int? ?? 6;

    private int? TargetItem => (_target.SelectedItem as TargetChoice)?.ItemId;

    /// <summary>Shows the forecast of the NPC in <paramref name="npcSlot"/>.</summary>
    public void Target(int npcSlot)
    {
        _npcSlot = npcSlot;
        _npcNetId = State.World?.Npcs.FirstOrDefault(npc => npc.Slot == npcSlot)?.NetId ?? 0;
        _forecast = null;
        _grid.RowCount = 0;
        _grid.Columns.Clear();
        SelectMonsterChoice();
        ShowHeader();
        RequestForecast();
    }

    protected override void OnConnectionChanged() => UpdateNotice();

    protected override void OnStatusChanged() => UpdateNotice();

    protected override void OnIconsChanged()
    {
        _grid.Invalidate();
        _kills.Invalidate();
    }

    protected override void OnWorldChanged()
    {
        FillMonsters();
        UpdateNotice();
        ShowHeader();
        UpdateColumnTitles();
        if (_autoRefresh.Checked && State.WorldIsCurrent && NpcIsAlive())
            RequestForecast();
    }

    /// <summary>Lists the NPCs, enemies nearest first. Without a target yet, the nearest enemy becomes the target.</summary>
    private void FillMonsters()
    {
        if (State.World is not { } world)
            return;

        // Rebuilding only when NPCs come or go keeps an open dropdown usable while frames advance.
        var key = string.Join(",", world.Npcs.Select(npc => $"{npc.Slot}:{npc.NetId}"));
        if (key != _monsterListKey)
        {
            _monsterListKey = key;
            var choices = world.Npcs
                .OrderBy(npc => npc.Category is NpcCategory.Enemy or NpcCategory.Boss ? 0 : 1)
                .ThenBy(npc => npc.Distance)
                .Select(npc => new MonsterChoice(npc.Slot, npc.NetId, $"{npc.Name} (slot {npc.Slot})"))
                .ToArray();
            _fillingMonsters = true;
            _monster.BeginUpdate();
            _monster.Items.Clear();
            _monster.Items.AddRange(choices);
            _monster.EndUpdate();
            _fillingMonsters = false;
            SelectMonsterChoice();
        }

        if (_npcSlot < 0 && world.Npcs.Where(npc => npc.Category is NpcCategory.Enemy or NpcCategory.Boss).MinBy(npc => npc.Distance) is { } nearest)
            Target(nearest.Slot);
    }

    private void SelectMonsterChoice()
    {
        _fillingMonsters = true;
        _monster.SelectedItem = _monster.Items.Cast<MonsterChoice>().FirstOrDefault(choice => choice.Slot == _npcSlot && choice.NetId == _npcNetId);
        _fillingMonsters = false;
    }

    protected override void OnForecastReceived(DropForecast forecast)
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

        BuildColumns(forecast);
        FillTargets(forecast);
        _grid.RowCount = forecast.Columns.Max(column => column.Cells.Length);
        _grid.Invalidate();
        _notes.Text = forecast.Notes;
        _notes.Visible = forecast.Notes.Length > 0;
        UpdateNotice();
        ShowHeader();
        ShowMatches();
    }

    protected override void OnKillRecorded(KillRecord kill)
    {
        var row = new object[]
        {
            kill.Frame,
            $"{kill.NpcName} (slot {kill.NpcSlot})",
            kill.Stream,
            kill.Offset >= 0 ? (object)kill.Offset : "?",
            kill.Drops.Describe(),
            CheckAgainstForecast(kill),
        };
        _kills.Rows.Insert(0, row);
        _kills.Rows[0].Tag = kill;
        _kills.Rows[0].Cells[KillDropsColumn].ToolTipText = kill.Drops.Describe();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        foreach (var kill in State.Kills)
            OnKillRecorded(kill);
    }

    private void RequestForecast()
    {
        if (_npcSlot < 0 || !State.IsConnected)
            return;

        _awaitedRequest = ++_lastRequest;
        State.Send(new ForecastRequest { RequestId = _awaitedRequest, NpcSlot = _npcSlot, Rows = SelectedRows });
        ShowHeader();
    }

    private bool NpcIsAlive() => State.World?.Npcs.Any(npc => npc.Slot == _npcSlot && npc.NetId == _npcNetId) == true;

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
        if (_npcSlot < 0)
            return "Pick a monster above, or double-click one in the Monsters & bosses window.";
        if (State.WorldIsCurrent && !NpcIsAlive())
            return $"The NPC that was in slot {_npcSlot} is gone (killed or despawned).";
        if (_forecast != null && (!State.IsPaused || State.Status!.Frame != _forecast.Frame))
            return $"Forecast taken at frame {_forecast.Frame}; the game is at frame {State.Status?.Frame}. It refreshes when the game pauses.";
        return null;
    }

    private void ShowHeader()
    {
        var npc = State.World?.Npcs.FirstOrDefault(n => n.Slot == _npcSlot && n.NetId == _npcNetId);
        _title.Text = _forecast?.NpcName ?? npc?.Name ?? "No monster selected";
        var parts = new List<string>();
        if (_npcSlot >= 0)
            parts.Add($"slot {_npcSlot}");
        if (npc != null)
            parts.Add($"HP {npc.Life}/{npc.LifeMax}");
        if (_forecast != null)
            parts.Add($"luck {_forecast.Luck.ToString("0.###", CultureInfo.InvariantCulture)}");
        if (_forecast != null)
            parts.Add($"frame {_forecast.Frame}");
        if (_awaitedRequest >= 0)
            parts.Add("computing…");
        _details.Text = string.Join(" · ", parts);
    }

    private void BuildColumns(DropForecast forecast)
    {
        if (_grid.Columns.Count != forecast.Columns.Count + 1)
        {
            _grid.Columns.Clear();
            _grid.Columns.Add(Grids.Number("Offset", 25));
            foreach (var _ in forecast.Columns)
                _grid.Columns.Add(Grids.Column("", 100));
            foreach (DataGridViewColumn column in _grid.Columns)
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
        }

        UpdateColumnTitles();
    }

    /// <summary>Kill kind, stream, its position and, when the world snapshot is of the same frame, the draws of the last frame.</summary>
    private void UpdateColumnTitles()
    {
        if (_forecast == null || _grid.ColumnCount != _forecast.Columns.Count + 1)
            return;

        var streams = State.World is { } world && world.Frame == _forecast.Frame ? world.Streams : null;
        for (var i = 0; i < _forecast.Columns.Count; i++)
        {
            var column = _forecast.Columns[i];
            var kind = KillKinds.GetValueOrDefault(column.Stream, column.Stream);
            var position = column.StartPosition >= 0 ? $" @ {column.StartPosition:N0}" : "";
            var lastFrame = streams?.FirstOrDefault(stream => stream.Name == column.Stream)?.LastFrameDraws ?? -1;
            var advance = lastFrame >= 0 ? $" (+{lastFrame:N0} last frame)" : "";
            _grid.Columns[i + 1].HeaderText = $"{kind} — {column.Stream}{position}{advance}";
        }
    }

    private void FillTargets(DropForecast forecast)
    {
        var previous = TargetItem;
        _target.BeginUpdate();
        _target.Items.Clear();
        _target.Items.Add(new TargetChoice(null, "(nothing: just browse)"));
        foreach (var drop in forecast.PossibleDrops)
            _target.Items.Add(new TargetChoice(drop.ItemId, $"{drop.Name} — {FormatChance(drop.Chance)}{FormatStack(drop)}"));
        _target.EndUpdate();
        _target.SelectedItem = _target.Items.Cast<TargetChoice>().FirstOrDefault(choice => choice.ItemId == previous) ?? _target.Items[0];
    }

    private object ForecastCell(int row, int column)
    {
        if (_forecast == null)
            return "";
        if (column == 0)
            return $"+{row}";

        var cells = _forecast.Columns[column - 1].Cells;
        if (cells.Length == 0)
            return row == 0 ? "the game has not created this stream yet" : "";
        return row < cells.Length ? _forecast.Outcomes[cells[row]].Describe() : "";
    }

    private void PaintForecastCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics is not { } graphics)
            return; // headers keep the default look

        var outcome = e.ColumnIndex > 0 ? OutcomeAt(e.RowIndex, e.ColumnIndex - 1) : null;
        var match = outcome != null && TargetItem is { } item && outcome.Contains(item);
        using (var background = new SolidBrush(match ? Grids.Match : CellColor(e.RowIndex, e.ColumnIndex)))
            graphics.FillRectangle(background, e.CellBounds);
        e.Paint(e.CellBounds, DataGridViewPaintParts.Border);

        var font = match ? _matchFont : _grid.Font;
        var textColor = _stale ? SystemColors.GrayText : SystemColors.ControlText;
        if (outcome != null)
        {
            LootPainter.Paint(graphics, e.CellBounds, outcome, State.Icons, font, textColor, faded: _stale);
        }
        else
        {
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                        | (e.ColumnIndex == 0 ? TextFormatFlags.Right : TextFormatFlags.Left);
            var text = Rectangle.Inflate(e.CellBounds, -4, 0);
            TextRenderer.DrawText(graphics, e.FormattedValue as string ?? "", font, text, textColor, flags);
        }

        if ((e.State & DataGridViewElementStates.Selected) != 0)
            Outline(graphics, e.CellBounds, e.ColumnIndex);
        e.Handled = true;
    }

    /// <summary>
    /// Row +0 (the next draw) is blue. Below, a stream cell takes the shade of its stream position: a roll keeps
    /// its shade from frame to frame, and the shades move up by as many rows as the frame made draws.
    /// </summary>
    private Color CellColor(int row, int column)
    {
        if (row == 0)
            return NextDraw;
        if (column == 0 || _forecast == null || column > _forecast.Columns.Count)
            return SystemColors.Window;

        var start = _forecast.Columns[column - 1].StartPosition;
        return start < 0 ? SystemColors.Window : _shadeColors[(int)((start + row) % _shadeColors.Length)];
    }

    // Light to darker gray: the jump back from the darkest to the lightest marks each full cycle.
    private static Color[] Shades(int count) =>
        Enumerable.Range(0, count)
            .Select(i => 252 - i * 48 / (count - 1))
            .Select(level => Color.FromArgb(level, level, level))
            .ToArray();

    /// <summary>The selected row gets a frame instead of a fill, so that its colors stay readable.</summary>
    private void Outline(Graphics graphics, Rectangle cell, int column)
    {
        using var brush = new SolidBrush(SelectionOutline);
        graphics.FillRectangle(brush, cell.Left, cell.Top, cell.Width, 2);
        graphics.FillRectangle(brush, cell.Left, cell.Bottom - 3, cell.Width, 2);
        if (column == 0)
            graphics.FillRectangle(brush, cell.Left, cell.Top, 2, cell.Height - 1);
        if (column == _grid.ColumnCount - 1)
            graphics.FillRectangle(brush, cell.Right - 2, cell.Top, 2, cell.Height - 1);
    }

    private void PaintKillCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != KillDropsColumn || e.Graphics is not { } graphics || e.CellStyle is not { } style
            || _kills.Rows[e.RowIndex].Tag is not KillRecord kill)
            return;

        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        e.PaintBackground(e.CellBounds, selected);
        LootPainter.Paint(graphics, e.CellBounds, kill.Drops, State.Icons, style.Font, selected ? style.SelectionForeColor : style.ForeColor, faded: false);
        e.Handled = true;
    }

    private DropOutcome? OutcomeAt(int row, int column)
    {
        if (_forecast == null || column >= _forecast.Columns.Count)
            return null;
        var cells = _forecast.Columns[column].Cells;
        return row < cells.Length ? _forecast.Outcomes[cells[row]] : null;
    }

    private void ShowMatches()
    {
        _grid.Invalidate();
        if (_forecast == null || TargetItem is not { } item)
        {
            _matches.Text = "";
            return;
        }

        var lines = _forecast.Columns.Select(column =>
        {
            var offsets = column.Cells.Select((outcome, offset) => (outcome, offset))
                .Where(cell => _forecast.Outcomes[cell.outcome].Contains(item))
                .Select(cell => cell.offset)
                .ToList();
            var kind = KillKinds.GetValueOrDefault(column.Stream, column.Stream);
            var first = string.Join(", ", offsets.Take(8).Select(offset => "+" + offset));
            return offsets.Count == 0
                ? $"{kind}: not in the next {column.Cells.Length} draws"
                : $"{kind}: {first}{(offsets.Count > 8 ? " …" : "")}  ({offsets.Count} of {column.Cells.Length})";
        });
        _matches.Text = string.Join("     ", lines);
    }

    private void SelectNextMatch()
    {
        if (_forecast == null || TargetItem is not { } item)
            return;

        var start = (_grid.CurrentCell?.RowIndex ?? -1) + 1;
        for (var row = start; row < _grid.RowCount; row++)
        {
            for (var column = 0; column < _forecast.Columns.Count; column++)
            {
                if (OutcomeAt(row, column)?.Contains(item) != true)
                    continue;
                _grid.CurrentCell = _grid.Rows[row].Cells[column + 1];
                return;
            }
        }
    }

    /// <summary>Compares a real kill with the forecast taken at the start of its frame, when there is one.</summary>
    private string CheckAgainstForecast(KillRecord kill)
    {
        var forecast = _recent.LastOrDefault(f => f.Frame == kill.Frame && f.NpcSlot == kill.NpcSlot && f.NetId == kill.NetId);
        var column = forecast?.Columns.FirstOrDefault(c => c.Stream == kill.Stream);
        if (forecast == null || column == null || kill.Offset < 0)
            return "";
        if (kill.Offset >= column.Cells.Length)
            return $"offset beyond the {column.Cells.Length} rows";

        var predicted = forecast.Outcomes[column.Cells[(int)kill.Offset]];
        return SameLoot(predicted, kill.Drops) ? "✓ as forecast" : "✗ forecast said " + predicted.Describe();
    }

    private static bool SameLoot(DropOutcome a, DropOutcome b) =>
        a.Copper == b.Copper && a.Items.Select(i => (i.ItemId, i.Prefix, i.Stack)).SequenceEqual(b.Items.Select(i => (i.ItemId, i.Prefix, i.Stack)));

    private static string FormatChance(float chance) =>
        chance >= 0.995f ? "100%" : chance >= 0.01f ? $"{chance * 100:0.#}%" : $"1 in {Math.Round(1 / chance):N0}";

    private static string FormatStack(PossibleDrop drop) =>
        drop.StackMax <= 1 ? "" : drop.StackMin == drop.StackMax ? $" ×{drop.StackMin}" : $" ×{drop.StackMin}-{drop.StackMax}";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _matchFont.Dispose();
        base.Dispose(disposing);
    }

    private sealed record TargetChoice(int? ItemId, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed record MonsterChoice(int Slot, int NetId, string Text)
    {
        public override string ToString() => Text;
    }
}
