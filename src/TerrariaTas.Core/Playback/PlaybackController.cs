using System.Collections.Generic;
using TerrariaTas.Core.Movies;

namespace TerrariaTas.Core.Playback;

public enum PlaybackState
{
    Paused,
    Playing,
    FastForward,
}

/// <summary>
/// Decides, tick by tick, whether the game advances one frame and with which movie input.
/// Knows nothing about the game: the runtime calls <see cref="Next"/> once per main-loop tick.
/// </summary>
public sealed class PlaybackController
{
    /// <summary>While fast-forwarding, only one frame in this many is shown on screen.</summary>
    public const int FastForwardPresentInterval = 15;

    private HashSet<int> _breakpoints = new();
    private bool _advanceRequested;
    private int? _runToFrame;
    private int _alreadyStoppedAt = -1;

    public PlaybackController(Movie movie)
    {
        Movie = movie;
        ReplaceMovie(movie);
    }

    public Movie Movie { get; private set; }

    /// <summary>Index of the next frame to run, which is also the number of frames already played.</summary>
    public int Frame { get; private set; }

    public PlaybackState State { get; private set; } = PlaybackState.Paused;

    public bool IsPastMovieEnd => Frame >= Movie.TotalFrames;

    public void Play() => State = PlaybackState.Playing;

    public void Pause()
    {
        State = PlaybackState.Paused;
        _runToFrame = null;
    }

    public void TogglePause()
    {
        if (State == PlaybackState.Paused)
            Play();
        else
            Pause();
    }

    /// <summary>Fast-forwards while enabled, then falls back to normal playing.</summary>
    public void SetFastForward(bool enabled)
    {
        if (enabled)
            State = PlaybackState.FastForward;
        else if (State == PlaybackState.FastForward && _runToFrame == null)
            State = PlaybackState.Playing;
    }

    /// <summary>Pauses and runs exactly one frame on the next tick.</summary>
    public void AdvanceOneFrame()
    {
        Pause();
        _advanceRequested = true;
    }

    /// <summary>Fast-forwards up to <paramref name="frame"/> and pauses before running it.</summary>
    public void RunTo(int frame)
    {
        _runToFrame = frame;
        State = frame > Frame ? PlaybackState.FastForward : PlaybackState.Paused;
    }

    /// <summary>Moves the timeline, typically after a savestate was loaded. Playback pauses there.</summary>
    public void JumpTo(int frame)
    {
        Frame = frame;
        Pause();
        _advanceRequested = false;
        _alreadyStoppedAt = frame;
    }

    public void ReplaceMovie(Movie movie)
    {
        Movie = movie;
        _breakpoints = new HashSet<int>(movie.BreakpointFrames);
    }

    public FrameDecision Next()
    {
        if (State == PlaybackState.Paused)
        {
            if (!_advanceRequested)
                return FrameDecision.Wait;

            _advanceRequested = false;
            return RunFrame(present: true, realTime: true);
        }

        if (ShouldStopBefore(Frame))
        {
            _alreadyStoppedAt = Frame;
            Pause();
            return FrameDecision.Wait;
        }

        var fastForward = State == PlaybackState.FastForward;
        return RunFrame(present: !fastForward || Frame % FastForwardPresentInterval == 0, realTime: !fastForward);
    }

    private bool ShouldStopBefore(int frame)
    {
        if (frame == _alreadyStoppedAt)
            return false;

        var isMovieEnd = Movie.TotalFrames > 0 && frame == Movie.TotalFrames;
        return frame == _runToFrame || isMovieEnd || _breakpoints.Contains(frame);
    }

    private FrameDecision RunFrame(bool present, bool realTime)
    {
        var input = Frame < Movie.TotalFrames ? Movie.LineAt(Frame) : null;
        Frame++;
        return FrameDecision.RunFrame(input, present, realTime);
    }
}
