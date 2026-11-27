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
    private const int HitHistory = 500;
    private const int TeleportHistory = 200;

    private readonly GameConnection _connection;
    private readonly List<KillRecord> _kills = new();
    private readonly List<HitRecord> _hits = new();
    private readonly List<TeleportRecord> _teleports = new();
    private int _lastMapRequest;

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

    public event Action<DamageForecast>? DamageForecastReceived;

    public event Action<HitRecord>? HitRecorded;

    public event Action<TeleportForecast>? TeleportForecastReceived;

    public event Action<TeleportRecord>? TeleportRecorded;

    /// <summary>The world map started, grew by some rows, or stopped with an error.</summary>
    public event Action? MapChanged;

    /// <summary>The place teleports are looked for, or the destination selected in the teleport forecast, changed.</summary>
    public event Action? TeleportFocusChanged;

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

    public IReadOnlyList<HitRecord> Hits => _hits;

    public IReadOnlyList<TeleportRecord> Teleports => _teleports;

    /// <summary>The latest teleport forecast that has destinations, whichever window asked for it; the map draws it.</summary>
    public TeleportForecast? LatestTeleports { get; private set; }

    /// <summary>Where teleports are looked for (set from the map or the teleport forecast), or null.</summary>
    public TeleportTarget? TeleportTarget { get; private set; }

    /// <summary>The offset selected in the teleport forecast, or -1.</summary>
    public int SelectedTeleport { get; private set; } = -1;

    /// <summary>The world map being received or received, or null before one is asked for.</summary>
    public WorldMapImage? Map { get; private set; }

    /// <summary>True between asking for a map and its header arriving.</summary>
    public bool MapRequested { get; private set; }

    public ItemIconCache Icons { get; } = new();

    public bool IsPaused => Status is { Phase: SessionPhase.Running, State: PlaybackState.Paused };

    /// <summary>True when the world snapshot shows the frame the game is paused at.</summary>
    public bool WorldIsCurrent => IsPaused && World != null && World.Frame == Status!.Frame;

    public void Start() => _connection.Start();

    public void Send(StudioMessage message) => _connection.Send(message);

    public void Run(CommandKind kind, int argument = 0) => Send(new Command(kind, argument));

    /// <summary>Asks the game for the world map; it comes at the next paused frame inside a world.</summary>
    public void RequestMap()
    {
        if (!IsConnected)
            return;

        MapRequested = true;
        Send(new MapRequest { RequestId = ++_lastMapRequest });
        MapChanged?.Invoke();
    }

    public void SetTeleportTarget(TeleportTarget? target)
    {
        if (target == TeleportTarget)
            return;
        TeleportTarget = target;
        TeleportFocusChanged?.Invoke();
    }

    public void SelectTeleport(int offset)
    {
        if (offset == SelectedTeleport)
            return;
        SelectedTeleport = offset;
        TeleportFocusChanged?.Invoke();
    }

    private void OnConnected()
    {
        IsConnected = true;
        Problem = "";
        Hello = null;
        Movie = Movie.Empty; // the game sends the whole movie first
        _kills.Clear(); // the game sends its kill, hit and teleport histories again
        _hits.Clear();
        _teleports.Clear();
        MapRequested = false; // a map on its way stopped with the previous connection
        Map = null; // the game may have opened another world since
        LatestTeleports = null;
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
            case DamageForecast damage:
                DamageForecastReceived?.Invoke(damage);
                break;
            case HitRecord hit:
                Remember(_hits, hit, HitHistory);
                HitRecorded?.Invoke(hit);
                break;
            case TeleportForecast teleports:
                if (teleports.Error.Length == 0)
                    LatestTeleports = teleports;
                TeleportForecastReceived?.Invoke(teleports);
                break;
            case TeleportRecord teleport:
                Remember(_teleports, teleport, TeleportHistory);
                TeleportRecorded?.Invoke(teleport);
                break;
            case MapHeader header:
                OnMapHeader(header);
                break;
            case MapRows rows:
                if (Map != null && rows.RequestId == Map.RequestId)
                {
                    Map.Add(rows);
                    MapChanged?.Invoke();
                }

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

    private void OnMapHeader(MapHeader header)
    {
        if (header.RequestId != _lastMapRequest)
            return; // an answer to an older request

        // A header with an error comes after the first one of the same request: reading stopped midway.
        if (header.Error.Length == 0)
            Map = new WorldMapImage(header);
        else if (Map?.RequestId == header.RequestId)
            Map.Fail(header.Error);
        MapRequested = false;
        MapChanged?.Invoke();
    }

    private static void Remember<T>(List<T> history, T record, int length)
    {
        history.Add(record);
        if (history.Count > length)
            history.RemoveAt(0);
    }
}
