using System;
using System.Collections.Generic;
using TerrariaTas.Core.Movies;
using TerrariaTas.Core.Playback;
using TerrariaTas.Core.Studio;

namespace TerrariaTas.Studio.Connection;

/// <summary>
/// The latest of everything the game sent, with change events for the tool windows.
/// Only used from the UI thread.
/// </summary>
internal sealed class StudioState
{
    private const int KillHistory = 500;

    private readonly GameConnection _connection;
    private readonly List<KillRecord> _kills = new();

    public StudioState(GameConnection connection)
    {
        _connection = connection;
        connection.Connected += OnConnected;
        connection.Disconnected += OnDisconnected;
        connection.HintChanged += OnHintChanged;
        connection.MessageReceived += OnMessage;
    }

    public event Action? ConnectionChanged;

    public event Action? MovieChanged;

    public event Action? StatusChanged;

    public event Action? WorldChanged;

    public event Action<KillRecord>? KillRecorded;

    public event Action<DropForecast>? ForecastReceived;

    public event Action? IconsChanged;

    /// <summary>The game sent its hotkeys (<see cref="Tools.Hotkeys.Map"/>).</summary>
    public event Action? HotkeysChanged;

    public bool IsConnected { get; private set; }

    /// <summary>Why the last connection ended or was refused; empty while things are fine.</summary>
    public string Problem { get; private set; } = "";

    /// <summary>While disconnected: what seems to keep a running game from answering, or "".</summary>
    public string WaitingHint { get; private set; } = "";

    public Hello? Hello { get; private set; }

    public Movie Movie { get; private set; } = Movie.Empty;

    public FrameStatus? Status { get; private set; }

    public WorldSnapshot? World { get; private set; }

    public IReadOnlyList<KillRecord> Kills => _kills;

    public ItemIconCache Icons { get; } = new();

    public bool IsPaused => Status is { Phase: SessionPhase.Running, State: PlaybackState.Paused };

    /// <summary>True when the world snapshot shows the frame the game is paused at.</summary>
    public bool WorldIsCurrent => IsPaused && World != null && World.Frame == Status!.Frame;

    public void Start() => _connection.Start();

    public void Send(StudioMessage message) => _connection.Send(message);

    public void Run(CommandKind kind, int argument = 0) => Send(new Command(kind, argument));

    private void OnConnected()
    {
        IsConnected = true;
        Problem = "";
        Hello = null;
        Movie = Movie.Empty; // the game sends the whole movie first
        _kills.Clear(); // the game sends its kill history again
        ConnectionChanged?.Invoke();
    }

    private void OnDisconnected(string reason)
    {
        IsConnected = false;
        if (Problem.Length == 0)
            Problem = reason;
        ConnectionChanged?.Invoke();
    }

    private void OnHintChanged(string hint)
    {
        WaitingHint = hint;
        ConnectionChanged?.Invoke();
    }

    private void OnMessage(StudioMessage message)
    {
        switch (message)
        {
            case Hello hello:
                Hello = hello;
                if (hello.ProtocolVersion != StudioProtocol.Version)
                    Problem = $"This Studio speaks link version {StudioProtocol.Version}, the game speaks {hello.ProtocolVersion}: use the Studio built with that TerrariaTas.";
                ConnectionChanged?.Invoke();
                break;
            case MovieData movie:
                Movie = movie.ApplyTo(Movie);
                MovieChanged?.Invoke();
                break;
            case FrameStatus status:
                Status = status;
                StatusChanged?.Invoke();
                break;
            case WorldSnapshot world:
                World = world;
                WorldChanged?.Invoke();
                break;
            case KillRecord kill:
                _kills.Add(kill);
                if (_kills.Count > KillHistory)
                    _kills.RemoveAt(0);
                KillRecorded?.Invoke(kill);
                break;
            case DropForecast forecast:
                ForecastReceived?.Invoke(forecast);
                break;
            case HotkeyBindings bindings:
                Tools.Hotkeys.Use(bindings.Map);
                HotkeysChanged?.Invoke();
                break;
            case ItemIcons icons:
                foreach (var icon in icons.Icons)
                    Icons.Add(icon);
                IconsChanged?.Invoke();
                break;
        }
    }
}
