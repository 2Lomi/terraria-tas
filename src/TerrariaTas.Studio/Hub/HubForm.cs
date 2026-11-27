using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TerrariaTas.Core.Studio;
using TerrariaTas.Studio.Connection;
using TerrariaTas.Studio.Tools;

namespace TerrariaTas.Studio.Hub;

/// <summary>Studio's main window: the link to the game, playback buttons and the tools to open.</summary>
internal sealed class HubForm : ToolWindow
{
    private static readonly Color RecordingColor = Color.FromArgb(185, 28, 28);

    private readonly StudioSettings _settings;
    private readonly List<ToolEntry> _tools;
    private readonly Dictionary<string, ToolWindow> _open = new();
    private readonly Label _connection = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), Margin = new Padding(0, 0, 0, 2) };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(0, 0, 0, 8) };
    private readonly CheckBox _onTop = new() { Text = "Keep Studio windows on top", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
    private readonly Button _record = new() { Text = RecordText(recording: false), AutoSize = true, Margin = new Padding(0, 0, 6, 0), ForeColor = RecordingColor };
    private readonly Label _help = new() { Text = Hotkeys.Help, AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 0) };

    // The playback buttons and what they say before their key: relabeled when the game sends its hotkeys.
    private readonly List<(Button Button, string Text, CommandKind Command)> _hotkeyButtons = new();

    public HubForm(StudioState state, StudioSettings settings)
        : base(state, "hub", "Terraria TAS Studio")
    {
        _settings = settings;
        ShowInTaskbar = true;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _tools = new List<ToolEntry>
        {
            new("input", "Input display", () => new InputDisplayForm(State)),
            new("player", "Player", () => new PlayerForm(State)),
            new("timeline", "Timeline & savestates", () => new TimelineForm(State)),
            new("monsters", "Monsters & bosses", () => NpcListForm.Monsters(State, OpenForecast)),
            new("town", "Town NPCs & critters", () => NpcListForm.TownAndCritters(State, OpenForecast)),
            new("items", "Items on the ground", () => new ItemListForm(State)),
            new("rng", "RNG streams", () => new RngStreamsForm(State)),
            new("forecast", "Drop forecast", () => new DropForecastForm(State, _settings)),
            new("damage", "Damage rolls", () => new DamageRollsForm(State, _settings)),
            new("teleport", "Teleport forecast", () => new TeleportForecastForm(State, _settings, ShowOnMap)),
            new("map", "World map", () => new MapForm(State, _settings)),
            new("watch", "Memory watch", () => new WatchForm(State, _settings)),
        };

        var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12) };
        layout.Controls.Add(_connection);
        layout.Controls.Add(_status);
        layout.Controls.Add(PlaybackButtons());
        layout.Controls.Add(new Label { Text = "Tools", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 10, 0, 4) });
        layout.Controls.Add(ToolButtons());
        layout.Controls.Add(_onTop);
        layout.Controls.Add(_help);
        Controls.Add(layout);

        _onTop.Checked = settings.ToolsOnTop;
        _onTop.CheckedChanged += (_, _) => ApplyOnTop();
        settings.RestorePlacement(this, Key);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyOnTop();
        State.Start();
        foreach (var key in _settings.OpenTools.ToList())
            Open(key);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _settings.OpenTools = _open.Keys.ToList();
        _settings.RememberPlacement(this, Key);
        foreach (var tool in _open.Values.ToList())
            tool.Close();
        base.OnFormClosing(e);
    }

    protected override void OnConnectionChanged()
    {
        if (State.IsConnected && State.Hello is { } hello)
        {
            _connection.Text = $"● Connected to Terraria {hello.TerrariaVersion}";
            _connection.ForeColor = Color.FromArgb(22, 101, 52);
        }
        else
        {
            _connection.Text = State.IsConnected ? "● Connected" : "○ Waiting for TerrariaTas.exe…";
            _connection.ForeColor = State.IsConnected ? Color.FromArgb(22, 101, 52) : SystemColors.GrayText;
        }

        OnStatusChanged();
    }

    protected override void OnStatusChanged()
    {
        _status.ForeColor = SystemColors.ControlText;
        if (!State.IsConnected && State.WaitingHint.Length > 0)
        {
            _status.Text = State.WaitingHint;
            return;
        }

        var versionMismatch = State.Hello != null && State.Hello.ProtocolVersion != StudioProtocol.Version;
        if ((State.Problem.Length > 0 && !State.IsConnected) || versionMismatch)
        {
            _status.Text = State.Problem;
            return;
        }

        if (!State.IsConnected || State.Status is not { } status)
        {
            _status.Text = "Start TerrariaTas.exe; Studio connects by itself.";
            return;
        }

        var movie = State.Hello?.MovieName is { Length: > 0 } name ? name : "free play";
        _status.Text = status.Phase switch
        {
            SessionPhase.Booting => $"Booting · {movie}",
            SessionPhase.LoadingWorld => $"Loading the world · {movie}",
            _ => $"{(status.Recording ? "● RECORDING · " : "")}{status.State.ToString().ToUpperInvariant()} · frame {status.Frame} / {status.TotalFrames} · slot {status.Slot} · {movie}"
                 + (status.Notice.Length > 0 ? Environment.NewLine + status.Notice : ""),
        };
        if (status.Recording)
            _status.ForeColor = RecordingColor;
        _record.Text = RecordText(status.Recording);
    }

    protected override void OnHotkeysChanged()
    {
        _help.Text = Hotkeys.Help;
        _record.Text = RecordText(State.Status?.Recording == true);
        foreach (var (button, text, command) in _hotkeyButtons)
            button.Text = Hotkeys.Label(text, command);
    }

    private static string RecordText(bool recording) => Hotkeys.Label(recording ? "■ Stop recording" : "● Record", CommandKind.ToggleRecording);

    /// <summary>The playback buttons, each with its hotkey: "Play / pause (F2)".</summary>
    private Control PlaybackButtons()
    {
        var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
        _record.Click += (_, _) => Hotkeys.Run(State, CommandKind.ToggleRecording);
        row.Controls.Add(_record);
        Add("Play / pause", CommandKind.TogglePause);
        Add("Frame +1", CommandKind.FrameAdvance);
        Add("Save slot", CommandKind.SaveSlot);
        Add("Load slot", CommandKind.LoadSlot);
        Add("Restart", CommandKind.Restart);
        return row;

        void Add(string text, CommandKind command)
        {
            var button = new Button { Text = Hotkeys.Label(text, command), AutoSize = true, Margin = new Padding(0, 0, 6, 0) };
            button.Click += (_, _) => Hotkeys.Run(State, command);
            row.Controls.Add(button);
            _hotkeyButtons.Add((button, text, command));
        }
    }

    private Control ToolButtons()
    {
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
        foreach (var tool in _tools)
        {
            var button = new Button { Text = tool.Title, UseMnemonic = false, Width = 210, Height = 34, Margin = new Padding(0, 0, 6, 6) };
            button.Click += (_, _) => Open(tool.Key);
            grid.Controls.Add(button);
        }

        return grid;
    }

    private ToolWindow? Open(string key)
    {
        if (_open.TryGetValue(key, out var existing))
        {
            existing.Activate();
            return existing;
        }

        if (_tools.FirstOrDefault(tool => tool.Key == key) is not { } entry)
            return null;

        var window = entry.Create();
        _settings.RestorePlacement(window, key);
        window.TopMost = _onTop.Checked;
        window.FormClosing += (_, _) => _settings.RememberPlacement(window, key);
        window.FormClosed += (_, _) => _open.Remove(key);
        _open[key] = window;
        window.Show();
        return window;
    }

    private void OpenForecast(int npcSlot)
    {
        if (Open("forecast") is DropForecastForm forecast)
            forecast.Target(npcSlot);
    }

    private void ShowOnMap(int tileX, int tileY)
    {
        if (Open("map") is MapForm map)
            map.CenterOn(tileX, tileY);
    }

    private void ApplyOnTop()
    {
        _settings.ToolsOnTop = _onTop.Checked;
        TopMost = _onTop.Checked;
        foreach (var tool in _open.Values)
            tool.TopMost = _onTop.Checked;
    }

    private sealed record ToolEntry(string Key, string Title, Func<ToolWindow> Create);
}
