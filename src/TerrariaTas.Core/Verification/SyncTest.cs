using System;
using System.Collections.Generic;
using System.Linq;
using TerrariaTas.Core.Playback;

namespace TerrariaTas.Core.Verification;

/// <summary>
/// Determinism self-test. Plays the movie once while fingerprinting every frame boundary, then replays
/// it from a savestate taken halfway and from the start state, comparing fingerprints and RNG calls.
/// A mismatch means the game is not deterministic yet, or a savestate misses some state.
/// </summary>
public sealed class SyncTest
{
    private readonly PlaybackController _playback;
    private readonly ISyncTarget _target;
    private readonly object _startState;
    private readonly int _end;
    private readonly int _middle;
    private readonly Fingerprint[] _fingerprints;
    private readonly IReadOnlyList<string>[] _rngCalls;
    private readonly List<string> _results = new();
    private Pass _pass = Pass.Reference;
    private object? _middleState;
    private ulong _fullHash;
    private int? _lastCheckedFrame;
    private string? _desync;
    private string? _rngDivergence;

    /// <param name="startState">State captured at frame 0, where <paramref name="playback"/> must stand.</param>
    public SyncTest(PlaybackController playback, ISyncTarget target, object startState)
    {
        if (playback.Movie.TotalFrames == 0 || playback.Frame != 0)
            throw new ArgumentException("The self-test needs a movie with frames, starting at frame 0.");

        _playback = playback;
        _target = target;
        _startState = startState;
        _end = playback.Movie.TotalFrames;
        _middle = _end / 2;
        _fingerprints = new Fingerprint[_end + 1];
        _rngCalls = new IReadOnlyList<string>[_end + 1];
        playback.RunTo(_end);
    }

    private enum Pass
    {
        Reference,
        FromMiddle,
        FromStart,
    }

    public bool Finished { get; private set; }

    public bool Passed { get; private set; } = true;

    public IReadOnlyList<string> Results => _results;

    /// <summary>Fingerprint of every frame boundary of the reference run (frames 0 to the movie length).</summary>
    public IReadOnlyList<Fingerprint> ReferenceFingerprints => _fingerprints;

    /// <summary>Call on every tick, before the playback controller decides. Acts once per frame boundary.</summary>
    public void Update()
    {
        if (!Finished && _playback.Frame != _lastCheckedFrame)
            Check(_playback.Frame);
    }

    private void Check(int frame)
    {
        // RNG calls since the previous boundary belong to the frame that just ran; after a restore, no frame ran.
        var calls = _target.TakeRngCalls();
        if (_lastCheckedFrame == frame - 1)
            CheckRngCalls(frame - 1, calls);
        _lastCheckedFrame = frame;

        var fingerprint = _target.Fingerprint();
        if (_pass == Pass.Reference)
        {
            _fingerprints[frame] = fingerprint;
            if (frame == _middle)
                _middleState = _target.SaveState(frame);
        }
        else if (_desync == null && fingerprint.DifferencesFrom(_fingerprints[frame]) is { Count: > 0 } differences)
        {
            _desync = $"frame {frame} differs in {string.Join(", ", differences)}";
        }

        if (frame == _end)
            EndPass();
    }

    private void CheckRngCalls(int frame, IReadOnlyList<string> calls)
    {
        if (_pass == Pass.Reference)
        {
            _rngCalls[frame] = calls;
            return;
        }

        var reference = _rngCalls[frame];
        var index = 0;
        while (index < calls.Count && index < reference.Count && calls[index] == reference[index])
            index++;

        if (_rngDivergence != null || (index == calls.Count && index == reference.Count))
            return;

        string At(IReadOnlyList<string> list) => index < list.Count ? list[index] : "(no call)";
        _rngDivergence = $"RNG diverged during frame {frame} at call #{index}: "
                         + $"reference {At(reference)}, replay {At(calls)} ({reference.Count} vs {calls.Count} calls)";
    }

    private void EndPass()
    {
        var fullHash = _target.FullHash();
        switch (_pass)
        {
            case Pass.Reference:
                _fullHash = fullHash;
                _results.Add($"Reference run: {_end} frames fingerprinted");
                ReportRngConsumers();
                RestartFrom(_middleState!, _middle, Pass.FromMiddle);
                break;
            case Pass.FromMiddle:
                Record($"Replay from a savestate at frame {_middle}", fullHash);
                RestartFrom(_startState, 0, Pass.FromStart);
                break;
            default:
                Record("Replay from the movie start state", fullHash);
                Finished = true;
                break;
        }
    }

    /// <summary>Every method that drew from the traced RNG: each one's state must be restorable by savestates.</summary>
    private void ReportRngConsumers()
    {
        var consumers = _rngCalls.Where(calls => calls != null).SelectMany(calls => calls)
            .GroupBy(caller => caller)
            .OrderByDescending(group => group.Count());
        foreach (var consumer in consumers)
            _results.Add($"  RNG consumer: {consumer.Key} ({consumer.Count()} calls)");
    }

    private void Record(string pass, ulong fullHash)
    {
        var desync = _desync ?? (fullHash != _fullHash ? "the full state differs at the end" : null);
        Passed &= desync == null;
        _results.Add($"{pass}: {(desync == null ? "in sync" : "DESYNC, " + desync)}");
        if (_rngDivergence != null)
            _results.Add("  " + _rngDivergence);
    }

    private void RestartFrom(object state, int frame, Pass pass)
    {
        _pass = pass;
        _desync = null;
        _rngDivergence = null;
        _target.LoadState(state);
        _playback.JumpTo(frame);
        _playback.RunTo(_end);
        Check(frame); // the restored state itself must match the reference
    }
}
