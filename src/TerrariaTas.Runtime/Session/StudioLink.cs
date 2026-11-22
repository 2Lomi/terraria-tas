using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TerrariaTas.Core.Hotkeys;
using TerrariaTas.Core.Movies;
using TerrariaTas.Core.Playback;
using TerrariaTas.Core.Studio;
using TerrariaTas.Runtime.Inspection;
using TerrariaTas.Runtime.RngTools;
using TerrariaTas.Runtime.Studio;

namespace TerrariaTas.Runtime.Session;

/// <summary>What the session tells TAS Studio on one tick.</summary>
internal sealed class SessionView
{
    public SessionPhase Phase { get; set; }

    public int Frame { get; set; }

    public PlaybackState State { get; set; }

    public int Slot { get; set; }

    public int[] SlotFrames { get; set; } = Array.Empty<int>();

    public string Notice { get; set; } = "";

    public bool Recording { get; set; }

    /// <summary>While recording: what the next frame would record right now.</summary>
    public InputView? NextInput { get; set; }

    public int TotalFrames { get; set; }

    /// <summary>Changes whenever the movie changes.</summary>
    public long MovieVersion { get; set; }

    /// <summary>The movie as it is now; called only when it must be sent, as recording changes it every frame.</summary>
    public Func<Movie> ReadMovie { get; set; } = () => Movie.Empty;

    public bool InWorld { get; set; }

    /// <summary>Paused at a frame boundary inside a world: the only time the world is inspected.</summary>
    public bool CanInspect { get; set; }

    /// <summary>Changes whenever the game state may have changed: a frame completed or a savestate was loaded.</summary>
    public long StateVersion { get; set; }

    /// <summary>The overlay shows over the game.</summary>
    public bool Overlay { get; set; }
}

/// <summary>
/// Feeds TAS Studio: the session status, world snapshots and drop forecasts at paused frame boundaries,
/// kill records as they happen. Commands from Studio go back to the session, which runs them like hotkeys.
/// Lives in Session/ because it gathers data from several features.
/// </summary>
internal sealed class StudioLink
{
    // About one status per displayed frame while the game runs, and a few movie updates per second while recording.
    // While paused, every change goes out at once.
    private static readonly TimeSpan StatusInterval = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan MovieInterval = TimeSpan.FromMilliseconds(250);
    private const int KillHistory = 200;

    private readonly StudioServer _server;
    private readonly WorldInspector _inspector;
    private readonly RngTracker _rng;
    private readonly DropForecaster _forecaster;
    private readonly Hello _hello;
    private readonly HotkeyBindings _hotkeys;
    private readonly ItemIconReader _iconReader = new();
    private readonly HashSet<int> _sentIcons = new();
    private readonly Queue<KillRecord> _kills = new();
    private readonly Stopwatch _sinceStatus = Stopwatch.StartNew();
    private readonly Stopwatch _sinceMovie = Stopwatch.StartNew();
    private readonly Watches _watches;
    private ForecastRequest? _pendingForecast;
    private string? _notice;
    private bool _wasConnected;
    private IReadOnlyList<InputLine> _sentLines = Array.Empty<InputLine>();
    private long _sentMovieVersion = -1;
    private string _sentStatus = "";
    private string _sentLiveInput = "";
    private long _sentWorldVersion = -1;

    private StudioLink(StudioServer server, WorldInspector inspector, RngTracker rng, Hello hello, HotkeyMap hotkeys, string watchLogPath)
    {
        _server = server;
        _inspector = inspector;
        _rng = rng;
        _forecaster = new DropForecaster(rng);
        _hello = hello;
        _hotkeys = new HotkeyBindings { Map = hotkeys };
        _watches = new Watches(watchLogPath);
    }

    /// <summary>Starts listening for Studio, or returns null when the link cannot be opened (already logged).</summary>
    /// <param name="watchLogPath">Where the memory watch log goes when Studio asks for one.</param>
    public static StudioLink? TryStart(WorldInspector inspector, RngTracker rng, Hello hello, HotkeyMap hotkeys, string watchLogPath)
    {
        var server = StudioServer.TryStart();
        return server == null ? null : new StudioLink(server, inspector, rng, hello, hotkeys, watchLogPath);
    }

    /// <summary>Something the user should know about a Studio request (the memory watch log), once; or null.</summary>
    public string? TakeNotice()
    {
        var notice = _notice;
        _notice = null;
        return notice;
    }

    /// <summary>Call at the boundary after each frame: the memory watch log gets a row.</summary>
    public void FrameCompleted(int nextFrame) => _watches.LogFrame(nextFrame);

    /// <summary>Closes the memory watch log, if any. The game is closing.</summary>
    public void Close() => _watches.StopLog();

    /// <summary>Commands received since the last call. Forecast requests are kept until the game can answer them.</summary>
    public List<Command> TakeCommands()
    {
        var commands = new List<Command>();
        while (_server.TryReceive(out var message))
        {
            if (message is Command command)
                commands.Add(command);
            else if (message is ForecastRequest request)
                _pendingForecast = request; // a newer request makes the older ones moot
            else if (message is WatchList list)
                _notice = _watches.Set(list) ?? _notice;
        }

        return commands;
    }

    public void AddKills(IEnumerable<KillRecord> kills)
    {
        foreach (var kill in kills)
        {
            _kills.Enqueue(kill);
            if (_kills.Count > KillHistory)
                _kills.Dequeue();
            SendIcons(new[] { kill.Drops });
            _server.Send(kill);
        }
    }

    public void Publish(SessionView view)
    {
        if (_server.TakeNewConnection())
            Greet();
        if (!_server.IsConnected)
        {
            // Nobody reads the watch any more: stop reading and logging it. A new Studio sends its own list.
            if (_wasConnected && !_watches.IsEmpty)
                _notice = _watches.Set(new WatchList()) ?? _notice;
            _wasConnected = false;
            return;
        }

        _wasConnected = true;

        PublishMovie(view);
        PublishStatus(view);
        if (view.CanInspect)
            PublishWorld(view);
        AnswerForecast(view);
    }

    private void Greet()
    {
        _server.Send(_hello);
        _server.Send(_hotkeys);
        _sentLines = Array.Empty<InputLine>();
        _sentMovieVersion = -1;
        _sentStatus = "";
        _sentWorldVersion = -1;
        _sentIcons.Clear();
        foreach (var kill in _kills)
        {
            SendIcons(new[] { kill.Drops });
            _server.Send(kill);
        }
    }

    /// <summary>
    /// Sends the pictures of the items Studio has not received on this connection yet, ahead of the message
    /// that names them. Main thread only (see <see cref="ItemIconReader"/>).
    /// </summary>
    private void SendIcons(IEnumerable<DropOutcome> outcomes)
    {
        if (!_server.IsConnected)
            return;

        var message = new ItemIcons();
        foreach (var item in outcomes.SelectMany(outcome => outcome.Items))
        {
            if (_sentIcons.Add(item.ItemId))
                message.Icons.Add(_iconReader.Read(item.ItemId));
        }

        if (message.Icons.Count > 0)
            _server.Send(message);
    }

    /// <summary>Sends the movie lines that changed since the last update: recording only changes the end.</summary>
    private void PublishMovie(SessionView view)
    {
        var paused = view.Phase == SessionPhase.Running && view.State == PlaybackState.Paused;
        if (view.MovieVersion == _sentMovieVersion || (!paused && _sinceMovie.Elapsed < MovieInterval))
            return;

        var movie = view.ReadMovie();
        var lines = movie.Lines;
        var first = 0;
        while (first < lines.Count && first < _sentLines.Count && SameLine(lines[first], _sentLines[first]))
            first++;

        _server.Send(new MovieData { FirstLine = first, Lines = lines.Skip(first).ToList(), BreakpointFrames = movie.BreakpointFrames.ToArray() });
        _sentLines = lines;
        _sentMovieVersion = view.MovieVersion;
        _sinceMovie.Restart();
    }

    private static bool SameLine(InputLine a, InputLine b) =>
        ReferenceEquals(a, b) || (a.FrameCount == b.FrameCount && a.SourceLine == b.SourceLine && a.HasSameInputs(b));

    private void PublishStatus(SessionView view)
    {
        // Paused, a change of the session goes out at once (ahead of the world snapshot of the same frame), and a
        // change of the player's live input while recording at most at the running rate. Running, the input changes
        // on every frame: sent at a steady rate.
        var session = $"{view.Phase}|{view.Frame}|{view.TotalFrames}|{view.State}|{view.Recording}|{view.Slot}|{string.Join(",", view.SlotFrames)}|{view.Notice}|{view.StateVersion}|{_watches.Version}|{view.Overlay}";
        var live = view.NextInput is { } input ? $"{string.Join(",", input.Keys)}|{input.Buttons}|{input.CursorX},{input.CursorY}|{input.CursorTileX},{input.CursorTileY}" : "";
        var paused = view.Phase == SessionPhase.Running && view.State == PlaybackState.Paused;
        var due = !paused ? _sinceStatus.Elapsed >= StatusInterval
            : session != _sentStatus || (live != _sentLiveInput && _sinceStatus.Elapsed >= StatusInterval);
        if (!due)
            return;

        _sentStatus = session;
        _sentLiveInput = live;
        _sinceStatus.Restart();
        _server.Send(new FrameStatus
        {
            Phase = view.Phase,
            Frame = view.Frame,
            TotalFrames = view.TotalFrames,
            State = view.State,
            Recording = view.Recording,
            NextInput = view.NextInput,
            Slot = view.Slot,
            SlotFrames = view.SlotFrames,
            Notice = view.Notice,
            Input = _inspector.ReadInput(),
            Player = view.InWorld ? _inspector.ReadPlayer() : null,
            Overlay = view.Overlay,
            WatchVersion = _watches.Version,
            WatchValues = view.InWorld && !_watches.IsEmpty ? _watches.Read() : Array.Empty<string>(),
        });
    }

    private void PublishWorld(SessionView view)
    {
        if (view.StateVersion == _sentWorldVersion)
            return;

        _sentWorldVersion = view.StateVersion;
        var snapshot = _inspector.Capture(view.Frame);
        snapshot.Streams = _rng.Views();
        _server.Send(snapshot);
    }

    private void AnswerForecast(SessionView view)
    {
        if (_pendingForecast is not { } request)
            return;

        if (view.CanInspect)
        {
            _pendingForecast = null;
            var forecast = Forecast(request, view.Frame);
            SendIcons(forecast.Outcomes);
            _server.Send(forecast);
        }
        else if (view.State != PlaybackState.Paused || !view.InWorld)
        {
            _pendingForecast = null;
            _server.Send(new DropForecast
            {
                RequestId = request.RequestId,
                Frame = view.Frame,
                NpcSlot = request.NpcSlot,
                Error = "Pause the game inside a world to forecast drops.",
            });
        }

        // Otherwise a frame advance is running this tick: answer at the next boundary.
    }

    /// <summary>
    /// A failing forecast is reported to Studio instead of ending the TAS session (and its savestates):
    /// every roll restores the game in a finally block. Only a detected leak stops the session.
    /// </summary>
    private DropForecast Forecast(ForecastRequest request, int frame)
    {
        try
        {
            return _forecaster.Forecast(request, frame);
        }
        catch (Exception e) when (e is not GameStateChangedException)
        {
            Log.Error("Drop forecast failed: " + e);
            return new DropForecast { RequestId = request.RequestId, Frame = frame, NpcSlot = request.NpcSlot, Error = "The forecast failed: " + e.Message };
        }
    }
}
