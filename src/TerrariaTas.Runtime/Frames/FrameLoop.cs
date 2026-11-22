using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Enums;

namespace TerrariaTas.Runtime.Frames;

/// <summary>
/// Owns Terraria's main loop. A game frame is exactly one Update followed by exactly one Draw,
/// with a GameTime that only depends on the frame count. Draws outside of that (window repaints)
/// are refused because Terraria runs UI logic and RNG inside Draw. Whether a frame runs at all is
/// decided by the <see cref="IFrameDriver"/>.
/// </summary>
internal sealed class FrameLoop
{
    /// <summary>1/60 s, rounded up so Terraria's own update gate always lets the frame through.</summary>
    private static readonly TimeSpan FrameDuration = TimeSpan.FromTicks(166667);

    private static FrameLoop? _active;

    private readonly IFrameDriver _driver;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _nextFrameDue;
    private bool _paced;
    private bool _updateRunning;
    private bool _drawPending;
    private bool _presentPending;
    private long _frameCount;
    private GameTime _gameTime = new(TimeSpan.Zero, TimeSpan.Zero);

    public FrameLoop(IFrameDriver driver)
    {
        _driver = driver;
    }

    /// <summary>Frames run since boot. The game clock derives from it, so savestates must restore it.</summary>
    public long ElapsedFrames
    {
        get => _frameCount;
        set => _frameCount = value;
    }

    public void Install(Harmony harmony)
    {
        _active = this;
        TimeBeginPeriod(1); // makes Thread.Sleep(1) last ~1 ms, needed for 60 fps pacing
        harmony.CreateClassProcessor(typeof(Hooks)).Patch();
    }

    private bool BeforeUpdate(ref GameTime gameTime)
    {
        if (_paced && _clock.Elapsed < _nextFrameDue)
            return Idle();

        var plan = _driver.PlanTick();
        if (!plan.RunFrame)
            return Idle();

        if (plan.RealTime)
        {
            var now = _clock.Elapsed;
            var catchUpLimit = TimeSpan.FromTicks(FrameDuration.Ticks * 4);
            _nextFrameDue = (now - _nextFrameDue > catchUpLimit ? now : _nextFrameDue) + FrameDuration;
        }

        _paced = plan.RealTime;
        _frameCount++;
        _gameTime = new GameTime(TimeSpan.FromTicks(FrameDuration.Ticks * _frameCount), FrameDuration);
        gameTime = _gameTime;

        // Terraria decides on its own whether an Update really runs (frame-skip modes, time accumulator).
        Main.FrameSkipMode = FrameSkipMode.Off;
        Main.UpdateTimeAccumulator = 0;
        Main.ThrottleWhenInactive = false;

        _updateRunning = true;
        _drawPending = true;
        _presentPending = plan.Present;
        return true;
    }

    private void AfterUpdate()
    {
        if (!_updateRunning)
            return;

        _updateRunning = false;
        // Terraria re-enables fixed time steps when its window is inactive; XNA would then batch updates.
        Main.instance.IsFixedTimeStep = false;
        Main.instance.InactiveSleepTime = TimeSpan.Zero;
    }

    private bool BeginDraw(ref bool result)
    {
        if (_drawPending)
        {
            _drawPending = false;
            return true;
        }

        result = false;
        return false;
    }

    private static bool Idle()
    {
        Main.instance.SuppressDraw();
        Thread.Sleep(1);
        return false;
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [HarmonyPatch]
    private static class Hooks
    {
        [HarmonyPatch(typeof(Main), "Update", typeof(GameTime)), HarmonyPrefix]
        private static bool UpdatePrefix(ref GameTime gameTime) => _active!.BeforeUpdate(ref gameTime);

        [HarmonyPatch(typeof(Main), "Update", typeof(GameTime)), HarmonyPostfix]
        private static void UpdatePostfix() => _active!.AfterUpdate();

        [HarmonyPatch(typeof(Main), "Update", typeof(GameTime)), HarmonyFinalizer]
        private static Exception? UpdateFinalizer(Exception? __exception) => Crashed(__exception);

        [HarmonyPatch(typeof(Main), "Draw", typeof(GameTime)), HarmonyFinalizer]
        private static Exception? DrawFinalizer(Exception? __exception) => Crashed(__exception);

        /// <summary>Lets the driver save its work, then lets the exception go on to Terraria's crash handling.</summary>
        private static Exception? Crashed(Exception? exception)
        {
            if (exception != null)
                _active!._driver.GameCrashed(exception);
            return exception;
        }

        [HarmonyPatch(typeof(Main), "Draw", typeof(GameTime)), HarmonyPrefix]
        private static void DrawPrefix(ref GameTime gameTime) => gameTime = _active!._gameTime;

        [HarmonyPatch(typeof(Microsoft.Xna.Framework.Game), "BeginDraw"), HarmonyPrefix]
        private static bool BeginDrawPrefix(ref bool __result) => _active!.BeginDraw(ref __result);

        [HarmonyPatch(typeof(Microsoft.Xna.Framework.Game), "EndDraw"), HarmonyPrefix]
        private static bool PresentPrefix() => _active!._presentPending;
    }
}
