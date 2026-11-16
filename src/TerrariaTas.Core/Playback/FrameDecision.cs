using TerrariaTas.Core.Movies;

namespace TerrariaTas.Core.Playback;

/// <summary>What the game loop should do on one tick.</summary>
public readonly struct FrameDecision
{
    private FrameDecision(bool run, InputLine? input, bool present, bool realTime)
    {
        Run = run;
        Input = input;
        Present = present;
        RealTime = realTime;
    }

    /// <summary>Do not advance the game this tick.</summary>
    public static FrameDecision Wait => default;

    public bool Run { get; }

    /// <summary>Movie input for the frame, or null past the end of the movie (real input is used).</summary>
    public InputLine? Input { get; }

    /// <summary>False while fast-forwarding: the frame is simulated and drawn but not shown.</summary>
    public bool Present { get; }

    /// <summary>True when the frame should be paced to 60 frames per second.</summary>
    public bool RealTime { get; }

    public static FrameDecision RunFrame(InputLine? input, bool present, bool realTime) => new(true, input, present, realTime);
}
