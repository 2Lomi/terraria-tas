using System;
using TerrariaTas.Core.Movies;

namespace TerrariaTas.Core.Playback;

public enum PlaybackState
{
    Paused,
    Playing,
    FastForward,
}

/// <summary>
/// Decides, tick by tick, whether the game advances one frame and with which input: the movie's, or while
/// recording, the player's, which then becomes the movie's. Knows nothing about the game: the runtime calls
/// <see cref="Next"/> once per main-loop tick.
/// </summary>
public sealed class PlaybackController
{
    /// <summary>While fast-forwarding, only one frame in this many is shown on screen.</summary>
    public const int FastForwardPresentInterval = 15;

    private bool _advanceRequested;
    private int? _runToFrame;
    private int _alreadyStoppedAt = -1;

    public PlaybackController(Movie movie)
        : this(new MovieRecorder(movie))
    {
    }

    public PlaybackController(MovieRecorder recorder)
    {
        Recorder = recorder;
    }

    /// <summary>The movie and its edits.</summary>
    public MovieRecorder Recorder { get; }

    public Movie Movie => Recorder.Movie;

    public int TotalFrames => Recorder.TotalFrames;

    /// <summary>Index of the next frame to run, which is also the number of frames already played.</summary>
    public int Frame { get; private set; }

    public PlaybackState State { get; private set; } = PlaybackState.Paused;

    /// <summary>
    /// While on, the frames played take <see cref="LiveInput"/> and write it into the movie at their frame, which
    /// ends the movie there. Running to a frame still replays the movie.
    /// </summary>
    public bool Recording { get; private set; }

    /// <summary>The input of a recorded frame: what the player does right now.</summary>
    public Func<InputLine>? LiveInput { get; set; }

    /// <summary>Playback pauses at the movie's breakpoints (<c>***</c>). Off for watching, which only pauses at the end.</summary>
    public bool StopsAtBreakpoints { get; set; } = true;

    public bool IsPastMovieEnd => Frame >= TotalFrames;

    /// <summary>
    /// Records from the current frame on. Impossible past the end of the movie: the frames in between were
    /// played without being recorded.
    /// </summary>
    public bool StartRecording()
    {
        if (Frame > TotalFrames || LiveInput == null)
            return false;

        Recording = true;
        return true;
    }

    public void StopRecording() => Recording = false;

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

    /// <summary>
    /// Fast-forwards up to <paramref name="frame"/> and pauses before running it. The frames on the way replay the
    /// movie, also while recording, so then the run stops at the end of the movie at the latest.
    /// </summary>
    public void RunTo(int frame)
    {
        if (Recording)
            frame = Math.Min(frame, TotalFrames);

        if (frame <= Frame)
        {
            Pause();
            return;
        }

        _runToFrame = frame;
        State = PlaybackState.FastForward;
    }

    /// <summary>
    /// Moves the timeline, typically after a savestate was loaded. Playback pauses there. Recording stops when the
    /// frame is past the end of the movie.
    /// </summary>
    public void JumpTo(int frame)
    {
        Frame = frame;
        Pause();
        _advanceRequested = false;
        _alreadyStoppedAt = frame;
        if (frame > TotalFrames)
            Recording = false;
    }

    /// <summary>Replaces the movie with <paramref name="text"/>. Recording stops when the movie now ends before the current frame.</summary>
    /// <exception cref="MovieFormatException">The text is not a valid movie. Nothing changes then.</exception>
    public void Reload(string text)
    {
        Recorder.Load(text);
        if (Frame > TotalFrames)
            Recording = false;
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

    /// <summary>The next frame takes live input: recording, and not running to a frame.</summary>
    private bool RecordsNextFrame => Recording && _runToFrame == null && Frame <= TotalFrames;

    private bool ShouldStopBefore(int frame)
    {
        if (frame == _alreadyStoppedAt)
            return false;
        if (frame == _runToFrame)
            return true;

        // Recording, the movie ends at the current frame and the breakpoints after it are gone.
        if (RecordsNextFrame)
            return false;

        var isMovieEnd = TotalFrames > 0 && frame == TotalFrames;
        return isMovieEnd || (StopsAtBreakpoints && Recorder.IsBreakpoint(frame));
    }

    private FrameDecision RunFrame(bool present, bool realTime)
    {
        var recorded = RecordsNextFrame;
        InputLine? input;
        if (recorded)
        {
            input = LiveInput!();
            Recorder.Record(Frame, input);
        }
        else
        {
            input = Frame < TotalFrames ? Recorder.LineAt(Frame) : null;
        }

        Frame++;
        return FrameDecision.RunFrame(input, present, realTime, recorded);
    }
}
