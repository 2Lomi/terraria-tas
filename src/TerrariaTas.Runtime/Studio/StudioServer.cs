using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using TerrariaTas.Core.Studio;

namespace TerrariaTas.Runtime.Studio;

/// <summary>
/// Hosts the named pipe TAS Studio connects to, one client at a time. Pipe I/O runs on background
/// threads: the game's main thread only queues outgoing messages and drains incoming ones, so a slow
/// or closed Studio can never stall a frame.
/// </summary>
internal sealed class StudioServer
{
    // Paused, the game sends a handful of messages per frame advance. This many unread means Studio is stuck.
    private const int MaxQueuedMessages = 1000;

    private readonly object _gate = new();
    private readonly ConcurrentQueue<StudioMessage> _received = new();
    private BlockingCollection<byte[]>? _outbox;
    private bool _newConnection;
    private bool _warnedFull;

    private StudioServer()
    {
    }

    public bool IsConnected
    {
        get
        {
            lock (_gate)
                return _outbox != null;
        }
    }

    /// <summary>Starts listening, or returns null (with a warning) when another TAS instance owns the pipe.</summary>
    public static StudioServer? TryStart()
    {
        NamedPipeServerStream pipe;
        try
        {
            pipe = CreatePipe();
        }
        catch (IOException e)
        {
            Log.Warn("TAS Studio link unavailable (is another TerrariaTas running?): " + e.Message);
            return null;
        }

        var server = new StudioServer();
        new Thread(() => server.Serve(pipe)) { IsBackground = true, Name = "TAS Studio link" }.Start();
        return server;
    }

    /// <summary>True once after each new connection, so that the caller sends the initial state.</summary>
    public bool TakeNewConnection()
    {
        lock (_gate)
        {
            var isNew = _newConnection;
            _newConnection = false;
            return isNew;
        }
    }

    public bool TryReceive(out StudioMessage? message) => _received.TryDequeue(out message);

    public void Send(StudioMessage message)
    {
        var frame = StudioCodec.Encode(message);
        lock (_gate)
        {
            if (_outbox == null || _outbox.TryAdd(frame) || _warnedFull)
                return;
            _warnedFull = true;
        }

        Log.Warn("TAS Studio is not reading its messages: dropping them until it catches up.");
    }

    private void Serve(NamedPipeServerStream pipe)
    {
        while (true)
        {
            try
            {
                pipe.WaitForConnection();
                RunConnection(pipe);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or ObjectDisposedException)
            {
                Log.Warn("TAS Studio link: " + e.Message);
            }
            finally
            {
                Disconnect();
                pipe.Dispose();
            }

            try
            {
                pipe = CreatePipe();
            }
            catch (IOException e)
            {
                Log.Warn("TAS Studio link stopped: " + e.Message);
                return;
            }
        }
    }

    private void RunConnection(NamedPipeServerStream pipe)
    {
        var outbox = new BlockingCollection<byte[]>(MaxQueuedMessages);
        lock (_gate)
        {
            _outbox = outbox;
            _newConnection = true;
            _warnedFull = false;
        }

        new Thread(() => WriteAll(pipe, outbox)) { IsBackground = true, Name = "TAS Studio writer" }.Start();
        Log.Info("TAS Studio connected");
        while (StudioCodec.ReadFrame(pipe) is { } frame)
            _received.Enqueue(StudioCodec.Decode(frame));
        Log.Info("TAS Studio disconnected");
    }

    private void Disconnect()
    {
        lock (_gate)
        {
            _outbox?.CompleteAdding();
            _outbox = null;
        }
    }

    private static void WriteAll(Stream pipe, BlockingCollection<byte[]> outbox)
    {
        try
        {
            foreach (var frame in outbox.GetConsumingEnumerable())
                StudioCodec.WriteFrame(pipe, frame);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // The reading side sees the broken pipe as well and ends the connection.
        }
    }

    // Asynchronous (overlapped) mode is required: on a synchronous pipe handle, Windows serializes I/O,
    // so a pending read would block every write until Studio sends something.
    private static NamedPipeServerStream CreatePipe() =>
        new(StudioProtocol.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
}
