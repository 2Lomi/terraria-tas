namespace TerrariaTas.Runtime.Frames;

/// <summary>What the main loop does on one tick, as decided by an <see cref="IFrameDriver"/>.</summary>
internal readonly struct TickPlan
{
    private TickPlan(bool runFrame, bool present, bool realTime)
    {
        RunFrame = runFrame;
        Present = present;
        RealTime = realTime;
    }

    /// <summary>Freeze the game: no update, no draw.</summary>
    public static TickPlan Wait => default;

    public bool RunFrame { get; }

    /// <summary>Show the drawn frame on screen (false while fast-forwarding).</summary>
    public bool Present { get; }

    /// <summary>Pace frames to 60 per second instead of running as fast as possible.</summary>
    public bool RealTime { get; }

    public static TickPlan Run(bool present, bool realTime) => new(true, present, realTime);
}

internal interface IFrameDriver
{
    /// <summary>Called on every main-loop tick at a frame boundary, before Terraria updates. Must not throw.</summary>
    TickPlan PlanTick();
}
