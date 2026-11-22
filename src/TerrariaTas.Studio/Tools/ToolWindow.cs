using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using TerrariaTas.Core.Hotkeys;
using TerrariaTas.Core.Studio;
using TerrariaTas.Studio.Connection;

namespace TerrariaTas.Studio.Tools;

/// <summary>
/// Base of every Studio window: follows the shared state through overridable callbacks, forwards the
/// TAS hotkeys to the game and remembers where it was placed.
/// </summary>
internal class ToolWindow : Form
{
    private readonly HashSet<Keys> _heldKeys = new();

    protected ToolWindow(StudioState state, string key, string title)
    {
        State = state;
        Key = key;
        Text = title;
        KeyPreview = true;
        ShowInTaskbar = false;
        // Sizes in this code are for 96 DPI; Windows scales them on high-DPI screens.
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;
    }

    /// <summary>Identifies the tool in the settings file.</summary>
    public string Key { get; }

    protected StudioState State { get; }

    protected virtual void OnConnectionChanged()
    {
    }

    protected virtual void OnMovieChanged()
    {
    }

    protected virtual void OnStatusChanged()
    {
    }

    protected virtual void OnWorldChanged()
    {
    }

    protected virtual void OnKillRecorded(KillRecord kill)
    {
    }

    protected virtual void OnForecastReceived(DropForecast forecast)
    {
    }

    protected virtual void OnIconsChanged()
    {
    }

    protected virtual void OnHotkeysChanged()
    {
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        State.ConnectionChanged += OnConnectionChanged;
        State.MovieChanged += OnMovieChanged;
        State.StatusChanged += OnStatusChanged;
        State.WorldChanged += OnWorldChanged;
        State.KillRecorded += OnKillRecorded;
        State.ForecastReceived += OnForecastReceived;
        State.IconsChanged += OnIconsChanged;
        State.HotkeysChanged += OnHotkeysChanged;

        // Show whatever is already known.
        OnConnectionChanged();
        OnMovieChanged();
        OnStatusChanged();
        OnWorldChanged();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        State.ConnectionChanged -= OnConnectionChanged;
        State.MovieChanged -= OnMovieChanged;
        State.StatusChanged -= OnStatusChanged;
        State.WorldChanged -= OnWorldChanged;
        State.KillRecorded -= OnKillRecorded;
        State.ForecastReceived -= OnForecastReceived;
        State.IconsChanged -= OnIconsChanged;
        State.HotkeysChanged -= OnHotkeysChanged;
        base.OnFormClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // While typing into a text field, only keys that type nothing (F1-F24) can be hotkeys: a hotkey moved to a letter
        // must still type that letter there.
        var typing = ActiveControl is TextBoxBase or ComboBox && e.KeyCode is < Keys.F1 or > Keys.F24;
        var repeat = !_heldKeys.Add(e.KeyCode);
        if (!typing && Hotkeys.Press(State, e.KeyCode, repeat))
            e.Handled = e.SuppressKeyPress = true;
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _heldKeys.Remove(e.KeyCode);
        if (Hotkeys.Release(State, e.KeyCode))
            e.Handled = true;
        base.OnKeyUp(e);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        // Key-up events go to whichever window has focus; never leave fast-forward stuck on.
        if (Hotkeys.KeyOf(HotkeyAction.FastForward) is { } fastForward && _heldKeys.Remove(fastForward))
            Hotkeys.Release(State, fastForward);
        _heldKeys.Clear();
        base.OnDeactivate(e);
    }

    /// <summary>A label that explains why the data below may not match the game right now.</summary>
    protected static Label NoticeLabel() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        UseMnemonic = false,
        Padding = new Padding(6, 4, 6, 4),
        ForeColor = Color.FromArgb(146, 64, 14),
        BackColor = Color.FromArgb(254, 243, 199),
        Visible = false,
    };

    /// <summary>Shows the reason the data is stale or missing, or hides the label when it is current.</summary>
    protected string? StaleReason()
    {
        if (!State.IsConnected)
            return "Not connected to the game.";
        if (State.World == null)
            return "No world data yet: enter a world and pause.";
        if (!State.IsPaused)
            return $"Showing frame {State.World.Frame}. The game is running: data refreshes when it pauses.";
        if (!State.WorldIsCurrent)
            return $"Showing frame {State.World.Frame}, waiting for frame {State.Status!.Frame}.";
        return null;
    }

    protected static void ShowNotice(Label label, string? text)
    {
        label.Text = text ?? "";
        label.Visible = text != null;
    }
}
