using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using TerrariaTas.Core.Movies;
using TerrariaTas.Core.Playback;
using TerrariaTas.Core.Verification;
using TerrariaTas.Runtime.Determinism;
using TerrariaTas.Runtime.Frames;
using TerrariaTas.Runtime.GameAccess;
using TerrariaTas.Runtime.Hotkeys;
using TerrariaTas.Runtime.Input;
using TerrariaTas.Runtime.SaveStates;
using TerrariaTas.Runtime.Status;
using TerrariaTas.Runtime.Verification;
using TerrariaTas.Runtime.WorldEntry;

namespace TerrariaTas.Runtime.Session;

/// <summary>
/// Composition root and conductor: the only class that knows every feature. It walks the game
/// from boot to the movie start, turns hotkeys into playback commands, feeds movie input to the
/// virtual devices and manages savestate slots.
/// </summary>
internal sealed class TasSession : IFrameDriver
{
    private const int StartSlot = 0;
    private const int UserSlots = 9;
    private static readonly TimeSpan HoldToRepeat = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan VerifyTimeLimit = TimeSpan.FromMinutes(3);

    private readonly TasOptions _options;
    private readonly PlaybackController _playback;
    private readonly FrameLoop _frames;
    private readonly VirtualInput _input = new();
    private readonly EnvironmentPins _pins = new();
    private readonly SaveStateStore _saveStates = new();
    private readonly WorldLoader _world = new();
    private readonly HotkeyListener _hotkeys = new();
    private readonly StatusDisplay _status = new();
    private readonly SaveState?[] _slots = new SaveState?[UserSlots + 1];
    private Phase _phase = Phase.Booting;
    private SyncTest? _syncTest;
    private RngTracer? _rngTracer;
    private int _slot = 1;
    private string _notice = "";

    private TasSession(TasOptions options, Movie movie)
    {
        _options = options;
        _playback = new PlaybackController(movie);
        _frames = new FrameLoop(this);
    }

    private enum Phase
    {
        Booting,
        EnteringWorld,
        Running,
        Exiting,
    }

    private MovieHeader Header => _playback.Movie.Header;

    private static string Version =>
        typeof(TasSession).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";

    public static void Install(TasOptions options, Harmony harmony)
    {
        var movie = options.MoviePath != null ? MovieParser.Load(options.MoviePath) : Movie.Empty;
        if (options.Verify && (movie.TotalFrames == 0 || !movie.Header.StartsInWorld))
            throw new InvalidOperationException("--verify needs a movie with inputs and with Player and World set.");

        var session = new TasSession(options, movie);
        session._frames.Install(harmony);
        session._input.Install(harmony);
        session._pins.Install(harmony);
        session._world.Install(harmony);
        if (options.Verify)
        {
            session._rngTracer = new RngTracer();
            session._rngTracer.Install(harmony);
            StartWatchdog(VerifyTimeLimit);
        }

        session._pins.SetDate(movie.Header.Date);
        session._pins.ConfigureWindow(movie.Header.ScreenWidth, movie.Header.ScreenHeight);
        session._saveStates.Register("TAS cursor", session._input.CaptureState, session._input.RestoreState);
        session._saveStates.Register("Frame clock", () => session._frames.ElapsedFrames, s => session._frames.ElapsedFrames = (long)s!);

        Log.Info($"Terraria TAS {Version}, movie: {options.MoviePath ?? "none (free play)"} ({movie.TotalFrames} frames)");
        Log.Info(HotkeyListener.Help);
    }

    public TickPlan PlanTick()
    {
        try
        {
            _hotkeys.Poll(Main.instance.Window.Handle);
            return _phase switch
            {
                Phase.Booting => Boot(),
                Phase.EnteringWorld => EnterWorld(),
                Phase.Running => Play(),
                _ => TickPlan.Wait,
            };
        }
        catch (Exception e)
        {
            // A broken TAS must not keep the game running in an unknown state: stop with the reason.
            Abort("Internal error, stopping: " + e);
            return TickPlan.Wait;
        }
    }

    /// <summary>Splash screen: once content is loaded, set up the window and head for the movie's world.</summary>
    private TickPlan Boot()
    {
        HoldInputWhileLoading();
        if (Main.showSplash)
            return TickPlan.Run(present: true, realTime: true);

        LanguageManager.Instance.SetLanguage(GameCulture.FromCultureName(GameCulture.CultureName.English));
        _pins.ApplyGameplaySettings();

        if (Header.StartsInWorld)
            _world.Start(Header.Player!, Header.World!);
        else
            Log.Info("The movie names no Player/World: enter a world from the menus, the TAS starts there.");

        _phase = Phase.EnteringWorld;
        return TickPlan.Run(present: true, realTime: true);
    }

    /// <summary>Loading screen (or menus in free play) until the world is completely loaded.</summary>
    private TickPlan EnterWorld()
    {
        HoldInputWhileLoading();
        if (_world.LoadFailed)
        {
            Abort($"Terraria could not load the world '{Header.World}'. Check the file in {Main.WorldPath}.");
            return TickPlan.Wait;
        }

        if (Main.gameMenu)
            return TickPlan.Run(present: true, realTime: true);
        if (!_world.LoadFinished)
            return TickPlan.Wait;

        StartMovie();
        return Play();
    }

    private void HoldInputWhileLoading()
    {
        if (Header.StartsInWorld)
            _input.ApplyNeutral();
        else
            _input.PassThrough();
    }

    private void StartMovie()
    {
        if (Header.TerrariaVersion != null && Header.TerrariaVersion != GameVersion.Running)
            Log.Warn($"The movie was made on Terraria {Header.TerrariaVersion} but {GameVersion.Running} is running: expect desyncs.");

        GameRng.ResetForMovieStart(Header.Seed);
        _pins.ResetVisualSchedules(Header.Seed);
        _pins.ApplyGameplaySettings();
        _slots[StartSlot] = _saveStates.Capture(0);
        _phase = Phase.Running;

        if (_options.Verify)
        {
            _rngTracer!.Enabled = true;
            _syncTest = new SyncTest(_playback, new GameSyncTarget(_saveStates, _rngTracer), _slots[StartSlot]!);
        }
        else if (_options.AutoPlay || _playback.Movie.TotalFrames == 0)
            _playback.Play();

        Notify("Movie start reached, start state kept in slot 0");
    }

    private TickPlan Play()
    {
        if (_syncTest != null)
        {
            _syncTest.Update();
            if (_syncTest.Finished)
            {
                FinishVerification(_syncTest);
                return TickPlan.Wait;
            }
        }
        else
        {
            HandleHotkeys();
        }

        var decision = _playback.Next();
        ShowStatus();
        if (!decision.Run)
            return TickPlan.Wait;

        if (decision.Input != null)
            _input.Apply(decision.Input, ScreenSize());
        else
            _input.PassThrough();

        _pins.ApplyGameplaySettings();
        return TickPlan.Run(decision.Present, decision.RealTime);
    }

    private void HandleHotkeys()
    {
        if (_hotkeys.WasPressed(HotkeyAction.PlayPause))
            _playback.TogglePause();
        if (_hotkeys.WasPressed(HotkeyAction.FrameAdvance) || _hotkeys.HeldFor(HotkeyAction.FrameAdvance) > HoldToRepeat)
            _playback.AdvanceOneFrame();
        if (_hotkeys.WasPressed(HotkeyAction.FastForward))
            _playback.SetFastForward(true);
        if (_hotkeys.WasReleased(HotkeyAction.FastForward))
            _playback.SetFastForward(false);
        if (_hotkeys.WasPressed(HotkeyAction.NextSlot))
            SelectSlot(_slot % UserSlots + 1);
        if (_hotkeys.WasPressed(HotkeyAction.PreviousSlot))
            SelectSlot((_slot + UserSlots - 2) % UserSlots + 1);
        if (_hotkeys.WasPressed(HotkeyAction.SaveState))
            SaveSlot(_slot);
        if (_hotkeys.WasPressed(HotkeyAction.LoadState))
            LoadSlot(_slot);
        if (_hotkeys.WasPressed(HotkeyAction.Restart))
            LoadSlot(StartSlot);
        if (_hotkeys.WasPressed(HotkeyAction.ReloadMovie))
            ReloadMovie();
    }

    private void SelectSlot(int slot)
    {
        _slot = slot;
        Notify(_slots[slot] is { } state ? $"Slot {slot} (frame {state.Frame})" : $"Slot {slot} (empty)");
    }

    private void SaveSlot(int slot)
    {
        _slots[slot] = _saveStates.Capture(_playback.Frame);
        Notify($"Saved slot {slot} at frame {_playback.Frame}");
    }

    private void LoadSlot(int slot)
    {
        if (_slots[slot] is not { } state)
        {
            Notify($"Slot {slot} is empty");
            return;
        }

        _saveStates.Restore(state);
        _playback.JumpTo(state.Frame);
        // Drawing now would run Terraria's draw-time logic outside a frame, so the picture refreshes on the next frame.
        Notify($"Loaded slot {slot}: frame {state.Frame} (picture updates on the next frame)");
    }

    private void ReloadMovie()
    {
        if (_options.MoviePath == null)
        {
            Notify("No movie file to reload");
            return;
        }

        try
        {
            var movie = MovieParser.Load(_options.MoviePath);
            _playback.ReplaceMovie(movie);
            Notify($"Movie reloaded: {movie.TotalFrames} frames (header changes need a restart)");
        }
        catch (Exception e) when (e is MovieFormatException or IOException)
        {
            Notify("Reload failed: " + e.Message);
        }
    }

    private void FinishVerification(SyncTest test)
    {
        var results = test.Results.ToList();
        var passed = test.Passed;

        // Compare with the previous launch: a movie must also replay identically after restarting the tool.
        var log = new FingerprintLog(BuildAndMovieKey(), test.ReferenceFingerprints);
        var logPath = Path.Combine(_options.DataDir, Path.GetFileNameWithoutExtension(_options.MoviePath) + ".fingerprints");
        var previous = File.Exists(logPath) ? FingerprintLog.Parse(File.ReadAllText(logPath)) : null;
        if (previous?.Key != log.Key)
        {
            results.Add("Against the previous launch: nothing comparable yet, run --verify again");
        }
        else
        {
            var difference = log.FirstDifferenceFrom(previous);
            passed &= difference == null;
            results.Add("Against the previous launch: " + (difference == null ? "in sync" : "DESYNC, " + difference));
        }

        File.WriteAllText(logPath, log.Write());
        foreach (var line in results)
            Log.Info(line);
        WriteVerifyReport(passed ? "PASSED" : "FAILED", results);
        Exit(passed);
    }

    private string BuildAndMovieKey()
    {
        var build = typeof(TasSession).Assembly.ManifestModule.ModuleVersionId;
        using var sha = System.Security.Cryptography.SHA1.Create();
        var movie = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(_options.MoviePath!))).Replace("-", "");
        return $"{build} {movie}";
    }

    private void Abort(string reason)
    {
        Log.Error(reason);
        if (_options.Verify)
            WriteVerifyReport("FAILED", new[] { reason });
        Exit(success: false);
    }

    private void WriteVerifyReport(string verdict, IEnumerable<string> lines)
    {
        var report = Path.Combine(_options.DataDir, "verify-result.txt");
        File.WriteAllLines(report, new[] { verdict }.Concat(lines));
        Log.Info($"Verification {verdict} (report: {report})");
    }

    private void Exit(bool success)
    {
        _phase = Phase.Exiting;
        Environment.ExitCode = success ? 0 : 1;
        Main.instance.Exit();
    }

    /// <summary>Unattended runs end even if the game hangs in a way the session cannot see (e.g. a deadlock).</summary>
    private static void StartWatchdog(TimeSpan limit)
    {
        new Thread(() =>
        {
            Thread.Sleep(limit);
            Log.Error($"Still running after {limit.TotalMinutes} minutes, killing the process.");
            Environment.Exit(3);
        }) { IsBackground = true, Name = "TAS watchdog" }.Start();
    }

    private void ShowStatus()
    {
        var state = _syncTest != null ? "VERIFYING" : _playback.State.ToString().ToUpperInvariant();
        var text = $"Terraria TAS {Version} | {state} | frame {_playback.Frame}/{_playback.Movie.TotalFrames} | slot {_slot} | {_notice}";
        _status.Show(text, urgent: _playback.State == PlaybackState.Paused);
    }

    private void Notify(string message)
    {
        _notice = message;
        Log.Info(message);
    }

    private static Point ScreenSize()
    {
        var parameters = Main.instance.GraphicsDevice.PresentationParameters;
        return new Point(parameters.BackBufferWidth, parameters.BackBufferHeight);
    }
}
