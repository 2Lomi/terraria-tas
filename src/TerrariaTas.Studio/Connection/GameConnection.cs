using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using TerrariaTas.Core.Studio;

namespace TerrariaTas.Studio.Connection;

/// <summary>
/// Client side of the Studio link: keeps trying to reach the pipe of a running TerrariaTas.exe, reads its
/// messages on a background thread and raises the events on the UI thread.
/// </summary>
internal sealed class GameConnection : IDisposable
{
    private const int ConnectTimeoutMilliseconds = 500;

    private const string NoLinkHint =
        "TerrariaTas.exe is running but does not answer. A TerrariaTas.exe built before TAS Studio has no link: " +
        "rebuild it (run.ps1 does). --verify runs have no link either, and only one Studio can connect at a time.";

    private const string NoAccessHint =
        "TerrariaTas.exe runs with more rights than Studio (as administrator?): start both the same way.";

    // The game opens its end of the link within seconds of starting. Still none after this long, it never will.
    private static readonly TimeSpan StartupGrace = TimeSpan.FromSeconds(10);

    private readonly object _writeGate = new();
    private volatile bool _stopped;
    private SynchronizationContext? _ui;
    private NamedPipeClientStream? _pipe;
    private string _hint = "";

    public event Action? Connected;

    /// <summary>Raised with the reason when a connection ends. Studio keeps trying to reconnect.</summary>
    public event Action<string>? Disconnected;

    /// <summary>Raised with what keeps a running game from answering, and with "" once that is over.</summary>
    public event Action<string>? HintChanged;

    public event Action<StudioMessage>? MessageReceived;

    /// <summary>Starts connecting. Call from the UI thread: the events are raised on it.</summary>
    public void Start()
    {
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Start the game connection from the UI thread.");
        new Thread(Run) { IsBackground = true, Name = "Game connection" }.Start();
    }

    /// <summary>Sends a message if connected; otherwise it is dropped (the windows show the disconnection).</summary>
    public void Send(StudioMessage message)
    {
        var frame = StudioCodec.Encode(message);
        lock (_writeGate)
        {
            if (_pipe == null)
                return;
            try
            {
                StudioCodec.WriteFrame(_pipe, frame);
            }
            catch (IOException)
            {
                // The reading thread notices the broken pipe and reconnects.
            }
        }
    }

    public void Dispose()
    {
        _stopped = true;
        lock (_writeGate)
            _pipe?.Dispose();
    }

    private void Run()
    {
        while (!_stopped)
        {
            // Asynchronous mode lets this thread wait on a read while the UI thread writes.
            var pipe = new NamedPipeClientStream(".", StudioProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                pipe.Connect(ConnectTimeoutMilliseconds);
            }
            catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
            {
                pipe.Dispose();
                SetHint(e is UnauthorizedAccessException ? NoAccessHint : GameRunsWithoutLink() ? NoLinkHint : "");
                Thread.Sleep(ConnectTimeoutMilliseconds);
                continue;
            }

            SetHint("");
            lock (_writeGate)
                _pipe = pipe;
            Post(() => Connected?.Invoke());

            var reason = ReadUntilClosed(pipe);

            lock (_writeGate)
                _pipe = null;
            pipe.Dispose();
            Post(() => Disconnected?.Invoke(reason));
        }
    }

    private string ReadUntilClosed(Stream pipe)
    {
        try
        {
            while (StudioCodec.ReadFrame(pipe) is { } frame)
            {
                var message = StudioCodec.Decode(frame);
                Post(() => MessageReceived?.Invoke(message));
            }

            return "The game closed the link.";
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ObjectDisposedException)
        {
            return e.Message;
        }
    }

    private void SetHint(string hint)
    {
        if (hint == _hint)
            return;
        _hint = hint;
        Post(() => HintChanged?.Invoke(hint));
    }

    private static bool GameRunsWithoutLink()
    {
        var games = Process.GetProcessesByName("TerrariaTas");
        try
        {
            return games.Any(StartedLongAgo);
        }
        finally
        {
            foreach (var game in games)
                game.Dispose();
        }
    }

    private static bool StartedLongAgo(Process game)
    {
        try
        {
            return DateTime.Now - game.StartTime > StartupGrace;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return false; // exited meanwhile, or not ours to inspect
        }
    }

    private void Post(Action action)
    {
        // After Studio closed, the UI thread is gone: nothing is left to notify.
        if (_stopped)
            return;

        try
        {
            _ui!.Post(_ => action(), null);
        }
        catch (InvalidOperationException)
        {
            // The windows are being torn down.
        }
    }
}
