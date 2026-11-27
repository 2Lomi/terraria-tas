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
/// The damage rolls of the held weapon depending on when the hit rolls. Row k of a column is the crit and damage of a
/// hit whose first roll starts k draws after the frame boundary the game is paused at, on that kind of hit's stream.
/// With a target, the damage it would lose after its defense, and whether that kills it. The hit log underneath shows
/// at which offset each real hit rolled, and whether the forecast was right.
/// </summary>
internal sealed class DamageRollsForm : ToolWindow
{
    private const int RememberedForecasts = 30;
    private const int LogRows = 500;
    private static readonly int[] RowChoices = { 100, 200, 500, 1000, 2000, 5000 };
    private static readonly Color NextDraw = Color.FromArgb(191, 219, 254);
    private static readonly Color CritColor = Color.FromArgb(254, 243, 199);

    private static readonly Dictionary<HitKind, string> KindNames = new()
    {
        [HitKind.Swing] = "Swing",
        [HitKind.Projectile] = "Projectile",
    };

    private readonly StudioSettings _settings;
    private readonly Label _title = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 13f), Margin = new Padding(0, 0, 16, 0), UseMnemonic = false };
    private readonly Label _details = new() { AutoSize = true, Margin = new Padding(0, 6, 0, 0), UseMnemonic = false };
    private readonly Label _columns = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 2, 8, 2), UseMnemonic = false };
    private readonly Label _notes = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 2, 8, 2), ForeColor = SystemColors.GrayText, UseMnemonic = false };
    private readonly Label _notice = NoticeLabel();
    private readonly Label _matches = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 2, 8, 4), UseMnemonic = false };
    private readonly ComboBox _target = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox _rows = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    private readonly CheckBox _autoRefresh = new() { Text = "Refresh on every paused frame", Checked = true, AutoSize = true, Margin = new Padding(12, 6, 0, 0) };
    private readonly CheckBox _setBase = new() { Text = "Base damage", AutoSize = true, Margin = new Padding(0, 6, 4, 0) };
    private readonly NumericUpDown _base = new() { Maximum = 1_000_000, Width = 70, Enabled = false };
    private readonly CheckBox _setCrit = new() { Text = "Crit %", AutoSize = true, Margin = new Padding(16, 6, 4, 0) };
    private readonly NumericUpDown _crit = new() { Maximum = 100, Width = 50, Enabled = false };
    private readonly ComboBox _highlight = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly NumericUpDown _threshold = new() { Maximum = 1_000_000, Width = 70 };
    private readonly DataGridView _grid = Grids.Create();
    private readonly DataGridView _log = Grids.Create();
    private readonly List<DamageForecast> _recent = new();
    private readonly Font _boldFont;

    private DamageForecast? _forecast;
    private bool _stale;
    private bool _fillingTargets;
    private bool _targetChosen;
    private string _targetListKey = "";
    private int _targetSlot = -1;
    private int _lastFrame;
    private int _lastRequest;
    private int _awaitedRequest = -1;

    public DamageRollsForm(StudioState state, StudioSettings settings)
        : base(state, "damage", "Damage rolls")
    {
        _settings = settings;
        ClientSize = new Size(940, 720);
        _boldFont = new Font(Font, FontStyle.Bold);

        _rows.Items.AddRange(RowChoices.Select(rows => (object)rows).ToArray());
        _rows.SelectedItem = RowChoices.Contains(settings.DamageRows) ? settings.DamageRows : 500;
        _rows.SelectedIndexChanged += (_, _) =>
        {
            _settings.DamageRows = SelectedRows;
            RequestForecast();
        };
        _target.SelectedIndexChanged += (_, _) =>
        {
            if (_fillingTargets || _target.SelectedItem is not TargetChoice choice)
                return;
            _targetChosen = true;
            _targetSlot = choice.Slot;
            RequestForecast();
        };
        _setBase.CheckedChanged += (_, _) =>
        {
            _base.Enabled = _setBase.Checked;
            RequestForecast();
        };
        _setCrit.CheckedChanged += (_, _) =>
        {
            _crit.Enabled = _setCrit.Checked;
            RequestForecast();
        };
        _base.ValueChanged += (_, _) =>
        {
            if (_setBase.Checked)
                RequestForecast();
        };
        _crit.ValueChanged += (_, _) =>
        {
            if (_setCrit.Checked)
                RequestForecast();
        };
        _highlight.Items.AddRange(Enum.GetValues<Highlight>().Select(h => (object)new HighlightChoice(h)).ToArray());
        _highlight.SelectedIndex = 0;
        _threshold.Visible = false;
        _highlight.SelectedIndexChanged += (_, _) => ShowMatches();
        _threshold.ValueChanged += (_, _) => ShowMatches();

        _grid.VirtualMode = true;
        _grid.CellValueNeeded += (_, e) => e.Value = Cell(e.RowIndex, e.ColumnIndex);
        _grid.CellFormatting += FormatCell;

        _log.Columns.AddRange(
            Grids.Number("Frame", 25),
            Grids.Column("NPC", 60),
            Grids.Column("Hit", 60),
            Grids.Number("Rolled at offset", 30),
            Grids.Column("Crit", 18),
            Grids.Number("Damage", 25),
            Grids.Number("Dealt", 22),
            Grids.Column("Forecast", 80));
        foreach (DataGridViewColumn column in _log.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable; // newest first

        var refresh = new Button { Text = "Refresh", AutoSize = true };
        refresh.Click += (_, _) => RequestForecast();
        var nextMatch = new Button { Text = "Next match ↓", AutoSize = true };
        nextMatch.Click += (_, _) => SelectNextMatch();

        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 0) };
        header.Controls.Add(_title);
        header.Controls.Add(_details);

        var controls = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 0) };
        controls.Controls.Add(Labeled("Target", _target, 0));
        controls.Controls.Add(Labeled("Rows", _rows, 16));
        controls.Controls.Add(refresh);
        controls.Controls.Add(_autoRefresh);

        var options = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 2, 8, 0) };
        options.Controls.Add(_setBase);
        options.Controls.Add(_base);
        options.Controls.Add(_setCrit);
        options.Controls.Add(_crit);
        options.Controls.Add(Labeled("Highlight", _highlight, 24));
        options.Controls.Add(_threshold);
        options.Controls.Add(nextMatch);

        var help = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8, 2, 8, 4),
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(920, 0),
            Text = "Row k = the hit's crit and damage if its first roll starts k draws after this frame boundary, on the column's stream: "
                   + "swings roll during the players' update, projectiles during the projectiles' update. \"→\" is what the target loses after its defense. "
                   + "Base damage and Crit % replace the game's, for example with a projectile's own damage.",
        };

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 400 };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_log);
        split.Panel2.Controls.Add(new Label { Text = "Hit log", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 2), Font = new Font(Font, FontStyle.Bold) });

        Controls.Add(split);
        Controls.Add(_matches);
        Controls.Add(help);
        Controls.Add(_notes);
        Controls.Add(_columns);
        Controls.Add(_notice);
        Controls.Add(options);
        Controls.Add(controls);
        Controls.Add(header);
    }

    private enum Highlight
    {
        Nothing,
        Crits,
        Kills,
        DamageAtLeast,
        DealtAtLeast,
    }

    private int SelectedRows => _rows.SelectedItem as int? ?? 500;

    private Highlight SelectedHighlight => (_highlight.SelectedItem as HighlightChoice)?.Value ?? Highlight.Nothing;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        foreach (var hit in State.Hits)
            OnHitRecorded(hit);
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
        FillTargets();
        UpdateNotice();

        // A new world snapshot means a new game state: a frame ran or a savestate was loaded.
        if (_autoRefresh.Checked && State.WorldIsCurrent)
            RequestForecast();
    }

    protected override void OnDamageForecastReceived(DamageForecast forecast)
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
        _grid.RowCount = forecast.Columns.Count == 0 ? 0 : forecast.Columns.Max(column => column.Rolls.Count);
        _grid.Invalidate();
        _notes.Text = forecast.Notes;
        _notes.Visible = forecast.Notes.Length > 0;
        if (!_setBase.Checked && forecast.Columns.FirstOrDefault() is { } first)
            _base.Value = Math.Min(_base.Maximum, first.BaseDamage); // the game's, as a starting point for typing another one
        if (!_setCrit.Checked && forecast.Columns.FirstOrDefault()?.CritChances.FirstOrDefault() is { } critChance)
            _crit.Value = Math.Max(_crit.Minimum, Math.Min(_crit.Maximum, critChance));
        UpdateNotice();
        ShowHeader();
        ShowMatches();
    }

    protected override void OnHitRecorded(HitRecord hit)
    {
        var row = new object[]
        {
            hit.Frame,
            $"{hit.NpcName} (slot {hit.NpcSlot})",
            $"{KindNames[hit.Kind]}: {hit.Source}",
            hit.Offset >= 0 ? (object)hit.Offset : "?",
            hit.Crit ? "crit" : "",
            hit.Damage >= 0 ? (object)hit.Damage : "?",
            hit.Dealt,
            CheckAgainstForecast(hit),
        };
        _log.Rows.Insert(0, row);
        _log.Rows[0].Tag = hit;
        if (_log.Rows.Count > LogRows)
            _log.Rows.RemoveAt(_log.Rows.Count - 1);
    }

    private void RequestForecast()
    {
        if (!State.IsConnected)
            return;

        _awaitedRequest = ++_lastRequest;
        State.Send(new DamageRequest
        {
            RequestId = _awaitedRequest,
            Rows = SelectedRows,
            TargetSlot = _targetSlot,
            BaseDamage = _setBase.Checked ? (int)_base.Value : -1,
            CritChance = _setCrit.Checked ? (int)_crit.Value : -1,
        });
        ShowHeader();
    }

    /// <summary>Lists the NPCs, enemies nearest first. Until one is chosen, the nearest enemy is the target.</summary>
    private void FillTargets()
    {
        if (State.World is not { } world)
            return;

        // Rebuilding only when NPCs come or go keeps an open dropdown usable while frames advance.
        var key = string.Join(",", world.Npcs.Select(npc => $"{npc.Slot}:{npc.NetId}"));
        if (key == _targetListKey)
            return;

        _targetListKey = key;
        var enemies = world.Npcs
            .OrderBy(npc => npc.Category is NpcCategory.Enemy or NpcCategory.Boss ? 0 : 1)
            .ThenBy(npc => npc.Distance)
            .ToList();
        if (!_targetChosen)
            _targetSlot = enemies.FirstOrDefault(npc => npc.Category is NpcCategory.Enemy or NpcCategory.Boss)?.Slot ?? -1;

        _fillingTargets = true;
        _target.BeginUpdate();
        _target.Items.Clear();
        _target.Items.Add(new TargetChoice(-1, "(no target: rolls only)"));
        _target.Items.AddRange(enemies.Select(npc => (object)new TargetChoice(npc.Slot, $"{npc.Name} (slot {npc.Slot})")).ToArray());
        _target.EndUpdate();
        var selected = _target.Items.Cast<TargetChoice>().FirstOrDefault(choice => choice.Slot == _targetSlot) ?? (TargetChoice)_target.Items[0]!;
        _target.SelectedItem = selected;
        _targetSlot = selected.Slot; // a chosen NPC that is gone leaves no target
        _fillingTargets = false;
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
        _title.Text = _forecast is { ItemName.Length: > 0 } ? _forecast.ItemName : "Held weapon";
        var parts = new List<string>();
        if (_forecast != null)
        {
            parts.Add($"luck {_forecast.Luck.ToString("0.###", CultureInfo.InvariantCulture)}");
            if (_forecast.TargetSlot >= 0)
                parts.Add($"target {_forecast.TargetName}: {_forecast.TargetLife} life, {_forecast.TargetDefense} defense");
            parts.Add($"frame {_forecast.Frame}");
        }

        if (_awaitedRequest >= 0)
            parts.Add("computing…");
        _details.Text = string.Join(" · ", parts);
    }

    private void BuildColumns(DamageForecast forecast)
    {
        if (_grid.Columns.Count != forecast.Columns.Count + 1)
        {
            _grid.Columns.Clear();
            _grid.Columns.Add(Grids.Number("Offset", 20));
            foreach (var _ in forecast.Columns)
                _grid.Columns.Add(Grids.Column("", 100));
            foreach (DataGridViewColumn column in _grid.Columns)
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
        }

        var streams = State.World is { } world && world.Frame == forecast.Frame ? world.Streams : null;
        var lines = new List<string>();
        for (var i = 0; i < forecast.Columns.Count; i++)
        {
            var column = forecast.Columns[i];
            var position = column.StartPosition >= 0 ? $" @ {column.StartPosition:N0}" : "";
            var lastFrame = streams?.FirstOrDefault(stream => stream.Name == column.Stream)?.LastFrameDraws ?? -1;
            _grid.Columns[i + 1].HeaderText = $"{KindNames[column.Kind]} — {column.Stream}{position}{(lastFrame >= 0 ? $" (+{lastFrame:N0} last frame)" : "")}";
            var crits = column.CritChances.Length == 0 ? "cannot crit" : "crit " + string.Join(" + ", column.CritChances.Select(chance => chance + "%"));
            lines.Add($"{KindNames[column.Kind]}: {column.Source} · {column.BaseDamage} damage ±15% · {crits}");
        }

        _columns.Text = string.Join(Environment.NewLine, lines);
    }

    private DamageRoll? RollAt(int row, int column)
    {
        if (_forecast == null || column < 1 || column > _forecast.Columns.Count)
            return null;
        var rolls = _forecast.Columns[column - 1].Rolls;
        return row < rolls.Count ? rolls[row] : null;
    }

    private object Cell(int row, int column)
    {
        if (_forecast == null)
            return "";
        if (column == 0)
            return $"+{row}";
        if (RollAt(row, column) is not { } roll)
            return "";

        var text = (roll.Crit ? "CRIT " : "") + roll.Damage;
        if (roll.Dealt >= 0)
            text += $" → {roll.Dealt}{(Kills(roll) ? " KILL" : "")}";
        return text;
    }

    private bool Kills(DamageRoll roll) => _forecast is { TargetSlot: >= 0 } forecast && roll.Dealt >= forecast.TargetLife;

    private bool Matches(DamageRoll roll) => SelectedHighlight switch
    {
        Highlight.Crits => roll.Crit,
        Highlight.Kills => Kills(roll),
        Highlight.DamageAtLeast => roll.Damage >= _threshold.Value,
        Highlight.DealtAtLeast => roll.Dealt >= 0 && roll.Dealt >= _threshold.Value,
        _ => false,
    };

    private void FormatCell(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.CellStyle is not { } style)
            return;

        var roll = RollAt(e.RowIndex, e.ColumnIndex);
        if (roll != null && Matches(roll))
            style.BackColor = Grids.Match;
        else if (e.RowIndex == 0)
            style.BackColor = NextDraw;
        else if (roll is { Crit: true })
            style.BackColor = CritColor;
        if (roll != null && (roll.Crit || Kills(roll)))
            style.Font = _boldFont;
        if (_stale)
            style.ForeColor = SystemColors.GrayText;
    }

    private void ShowMatches()
    {
        _threshold.Visible = SelectedHighlight is Highlight.DamageAtLeast or Highlight.DealtAtLeast;
        _grid.Invalidate();
        if (_forecast == null || SelectedHighlight == Highlight.Nothing)
        {
            _matches.Text = "";
            return;
        }

        if (SelectedHighlight is Highlight.Kills or Highlight.DealtAtLeast && _forecast.TargetSlot < 0)
        {
            _matches.Text = "Pick a target to see what it loses.";
            return;
        }

        var lines = _forecast.Columns.Select(column =>
        {
            var offsets = column.Rolls.Select((roll, offset) => (roll, offset)).Where(entry => Matches(entry.roll)).Select(entry => entry.offset).ToList();
            var first = string.Join(", ", offsets.Take(8).Select(offset => "+" + offset));
            return offsets.Count == 0
                ? $"{KindNames[column.Kind]}: none in the next {column.Rolls.Count} draws"
                : $"{KindNames[column.Kind]}: {first}{(offsets.Count > 8 ? " …" : "")}  ({offsets.Count} of {column.Rolls.Count})";
        });
        _matches.Text = string.Join("     ", lines);
    }

    private void SelectNextMatch()
    {
        if (_forecast == null)
            return;

        var start = (_grid.CurrentCell?.RowIndex ?? -1) + 1;
        for (var row = start; row < _grid.RowCount; row++)
        {
            for (var column = 1; column <= _forecast.Columns.Count; column++)
            {
                if (RollAt(row, column) is not { } roll || !Matches(roll))
                    continue;
                _grid.CurrentCell = _grid.Rows[row].Cells[column];
                return;
            }
        }
    }

    /// <summary>Compares a real hit with the forecast taken at the start of its frame, when there is one.</summary>
    private string CheckAgainstForecast(HitRecord hit)
    {
        var forecast = _recent.LastOrDefault(f => f.Frame == hit.Frame);
        var column = forecast?.Columns.FirstOrDefault(c => c.Kind == hit.Kind && c.Stream == hit.Stream && c.SourceId == hit.SourceId);
        if (column == null || hit.Offset < 0 || hit.Damage < 0)
            return "";
        if (column.Overridden)
            return "not compared: Base damage or Crit % was set";
        if (hit.Offset >= column.Rolls.Count)
            return $"offset beyond the {column.Rolls.Count} rows";

        var predicted = column.Rolls[(int)hit.Offset];
        return predicted.Crit == hit.Crit && predicted.Damage == hit.Damage
            ? "✓ as forecast"
            : $"✗ forecast said {(predicted.Crit ? "crit " : "")}{predicted.Damage}";
    }

    /// <summary>A label and its control, kept on the same line when the toolbar wraps.</summary>
    private static Control Labeled(string text, Control control, int leftGap)
    {
        var pair = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(leftGap, 0, 0, 0) };
        pair.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        pair.Controls.Add(control);
        return pair;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _boldFont.Dispose();
        base.Dispose(disposing);
    }

    private sealed record TargetChoice(int Slot, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed record HighlightChoice(Highlight Value)
    {
        public override string ToString() => Value switch
        {
            Highlight.Crits => "crits",
            Highlight.Kills => "kills",
            Highlight.DamageAtLeast => "damage at least",
            Highlight.DealtAtLeast => "dealt at least",
            _ => "nothing",
        };
    }
}
