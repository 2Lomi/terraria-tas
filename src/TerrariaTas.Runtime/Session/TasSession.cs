using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.Localization;
using TerrariaTas.Core.Hotkeys;
using TerrariaTas.Core.Launch;
using TerrariaTas.Core.Movies;
using TerrariaTas.Core.Playback;
using TerrariaTas.Core.Studio;
using TerrariaTas.Core.Verification;
using TerrariaTas.Runtime.Determinism;
using TerrariaTas.Runtime.Frames;
using TerrariaTas.Runtime.GameAccess;
using TerrariaTas.Runtime.Hotkeys;
using TerrariaTas.Runtime.Input;
using TerrariaTas.Runtime.Inspection;
using TerrariaTas.Runtime.Overlay;
using TerrariaTas.Runtime.RngTools;
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

    // The game clock at the movie start: one hour of frames, later than any boot, so the clock never runs backwards
    // and does not depend on how long booting took (some AI reads it).
    private const long MovieStartClock = 60L * 60 * 60;
    private static readonly TimeSpan HoldToRepeat = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan VerifyTimeLimit = TimeSpan.FromMinutes(3);

    // Recorded input reaches the file once recording rests for a second, and every ten seconds while it goes on.
    private static readonly TimeSpan SaveWhenIdle = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SaveWhileRecording = TimeSpan.FromSeconds(10);

    // The movie file is checked for edits four times a second, and an edit is loaded once its writer has been done with
    // the file for a moment (some editors write it in several steps).
    private static readonly TimeSpan FileCheckInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan EditSettleTime = TimeSpan.FromMilliseconds(200);
    private static readonly List<Command> NoCommands = new();

    private readonly TasOptions _options;
    private readonly PlaybackController _playback;
    private readonly MovieFile? _movieFile;
    private readonly FrameLoop _frames;
    private readonly VirtualInput _input = new();
    private readonly EnvironmentPins _pins = new();
    private readonly WindowPins _window = new();
    private readonly SettingPins _settings = new();
    private readonly KeyBindingPins _bindings = new();
    private readonly AchievementPins _achievements = new();
    private readonly SoundClock _sounds = new();
    private readonly MusicChoice _music = new();
    private readonly SaveStateStore _saveStates = new();
    private readonly WorldLoader _world = new();
    private readonly WorldLoadPins _worldLoad = new();
    private readonly HotkeyListener _hotkeys;
    private readonly StatusDisplay _status = new();
    private readonly RngTracker _rng = new();
    private readonly KillLog _kills;
    private readonly WorldInspector _inspector = new();
    private readonly InfoOverlay _overlay = new();
    private readonly SaveState?[] _slots = new SaveState?[UserSlots + 1];

    // For each slot, the input its game state was made with: tells whether it belongs to the movie as it is now.
    private readonly InputHistory?[] _slotInputs = new InputHistory?[UserSlots + 1];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Phase _phase = Phase.Booting;

    // The input of the last savestate loaded, up to its frame: with the movie's input after it, what made the game state.
    private InputHistory _stateInput = InputHistory.Empty;
    private int _stateFrame;
    private long _changeVersion = -1;
    private TimeSpan _lastChange;
    private TimeSpan _lastSaveAttempt;
    private TimeSpan _lastFileCheck;

    // Why the movie file as it is on disk could not be loaded, while it cannot: the TAS must not write over it.
    private string? _movieFileError;

    // The header the game started the movie with. Edits of the header only apply when the movie starts again.
    private MovieHeader? _headerInEffect;
    private bool _bootStateCaptured;
    private bool _minimized;
    private SyncTest? _syncTest;
    private GameSyncTarget? _syncTarget;
    private SortedDictionary<string, string>? _startCensus;
    private RngTracer? _rngTracer;
    private StudioLink? _studio;
    private int _slot = 1;
    private string _notice = "";
    private long _framesRun;
    private long _framesAccounted;
    private long _stateVersion;

    /// <param name="movieFile">Where recordings are saved and edits come from; null when the movie is only read (watched or verified).</param>
    private TasSession(TasOptions options, MovieRecorder movie, MovieFile? movieFile, HotkeyMap hotkeys)
    {
        _options = options;
        _playback = new PlaybackController(movie);
        _playback.LiveInput = () => _input.ReadRealDevices(Main.instance.Window.Handle);
        _playback.StopsAtBreakpoints = options.Mode != LaunchMode.Play; // watching plays through, to the end

        _movieFile = movieFile;
        _hotkeys = new HotkeyListener(hotkeys);
        _frames = new FrameLoop(this);
        _kills = new KillLog(_rng);
    }

    private enum Phase
    {
        Booting,
        EnteringWorld,
        Running,
        Exiting,
    }

    private MovieHeader Header => _playback.Recorder.Header;

    private static string Version =>
        typeof(TasSession).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";

    public static void Install(TasOptions options, Harmony harmony)
    {
        // A movie --new just created has nothing worth keeping a backup of.
        var file = options.MoviePath == null ? null : new MovieFile(options.MoviePath, backUp: !options.RecordFromStart);
        var movie = OpenMovie(options, file);
        if (options.Verify && (movie.TotalFrames == 0 || !movie.Header.StartsInWorld))
            throw new TasSetupException("--verify needs a movie with inputs and with Player and World set.");

        var hotkeys = HotkeyListener.Load(options.DataDir, out var hotkeysPath);
        var session = new TasSession(options, movie, options.ReadOnly ? null : file, hotkeys);
        session._frames.Install(harmony);
        session._input.Install(harmony);
        session._input.HideFromGame(hotkeys.BoundKeys);
        session._pins.Install(harmony);
        session._pins.KeepConfigurationFiles = options.MoviePath != null;
        session._bindings.Install(harmony);
        session._achievements.Install(harmony);
        session._sounds.Install(harmony);
        session._music.Install(harmony);
        session._world.Install(harmony);
        session._worldLoad.Install(harmony);
        session._kills.Install(harmony);
        session._overlay.Install(harmony);
        session._overlay.Lines = session.OverlayLines;
        if (options.Verify)
        {
            session._rngTracer = new RngTracer();
            session._rngTracer.Install(harmony);
            StartWatchdog(VerifyTimeLimit);
        }
        else
        {
            var hello = new Hello { TasVersion = Version, TerrariaVersion = GameVersion.Running, MovieName = Path.GetFileName(options.MoviePath ?? "") };
            var watchLog = Path.Combine(options.DataDir, Path.GetFileNameWithoutExtension(options.MoviePath ?? "free-play") + ".watch.csv");
            session._studio = StudioLink.TryStart(session._inspector, session._rng, hello, hotkeys, watchLog);
        }

        session._pins.SetDate(movie.Header.Date);
        session._window.Configure(movie.Header.ScreenWidth, movie.Header.ScreenHeight);
        session._saveStates.Register("TAS cursor", session._input.CaptureState, session._input.RestoreState);
        session._saveStates.Register("Frame clock", () => session._frames.ElapsedFrames, s => session._frames.ElapsedFrames = (long)s!);
        session._saveStates.Register("RNG positions", session._rng.CaptureState, session._rng.RestoreState);
        session._saveStates.Register("Seed counters", session._pins.CaptureSeedCounters, session._pins.RestoreSeedCounters);
        session._saveStates.Register("Sound clock", session._sounds.CaptureState, session._sounds.RestoreState);
        session._saveStates.Register("Key bindings", session._bindings.Capture, session._bindings.Restore);
        session._saveStates.Register("Achievements", session._achievements.Capture, session._achievements.Restore);

        var mode = options.Mode switch
        {
            LaunchMode.Play => ", watch only",
            LaunchMode.Edit => ", to work on",
            LaunchMode.New => ", recording from its start",
            LaunchMode.Verify => ", verifying",
            _ => "",
        };
        Log.Info($"Terraria TAS {Version}, movie: {options.MoviePath ?? "none (free play)"} ({movie.TotalFrames} frames{mode})");
        Log.Info($"{hotkeys.Help()} (keys in {hotkeysPath})");
        if (session._studio != null)
            Log.Info("TAS Studio (TerrariaTas.Studio.exe) can connect at any time.");
    }

    /// <summary>The movie file (created already by the creation launch for --new), with its player and world checked.</summary>
    private static MovieRecorder OpenMovie(TasOptions options, MovieFile? file)
    {
        if (file == null)
            return new MovieRecorder(Movie.Empty);

        string text;
        Movie movie;
        try
        {
            text = file.Read();
            movie = MovieParser.Parse(text);
        }
        catch (MovieFormatException e)
        {
            throw new TasSetupException($"{options.MoviePath}, line {e.LineNumber}: {e.Reason}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new TasSetupException($"Cannot read {options.MoviePath}: {e.Message}");
        }

        return new MovieRecorder(CheckStartFiles(options, file, text, movie.Header));
    }

    /// <summary>
    /// The movie only replays from the very player and world files it was made with: they must match the checksums of
    /// its header. A movie without checksums plays with the files as they are, and gets the checksums when it may be
    /// written (--edit), so that from then on it only starts from these files.
    /// </summary>
    /// <returns>The movie's text, with the checksums added when they were.</returns>
    private static string CheckStartFiles(TasOptions options, MovieFile file, string text, MovieHeader header)
    {
        if (!header.StartsInWorld)
            return text;

        var player = Checksum(Path.Combine(options.DataDir, "Players", header.Player!), header.PlayerChecksum, "PlayerChecksum");
        var world = Checksum(Path.Combine(options.DataDir, "Worlds", header.World!), header.WorldChecksum, "WorldChecksum");
        var unpinned = string.Join(" and ", new[] { header.PlayerChecksum == null ? header.Player : null, header.WorldChecksum == null ? header.World : null }
            .Where(name => name != null));
        if (unpinned.Length == 0)
            return text;

        if (options.ReadOnly)
        {
            Log.Warn($"The movie has no checksum of {unpinned}: it cannot tell whether they are the files it was made with. "
                     + "Opening it once with --edit adds the checksums.");
            return text;
        }

        var pinned = MovieHeaderText.AddChecksums(text, header.PlayerChecksum == null ? player : null, header.WorldChecksum == null ? world : null);
        try
        {
            file.Save(pinned);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new TasSetupException($"Cannot add the checksums of {unpinned} to {file.Path}: {e.Message}");
        }

        Log.Info($"Checksums of {unpinned} added to the movie header: from now on it only starts from these files"
                 + (file.HasBackup ? $" (the movie as it was is in {Path.GetFileName(file.BackupPath)})" : ""));
        return pinned;
    }

    /// <summary>The checksum of a player or world file, which must be <paramref name="expected"/> when the movie states it.</summary>
    private static string Checksum(string path, string? expected, string property)
    {
        if (!File.Exists(path))
            throw new TasSetupException($"The movie starts with {Path.GetFileName(path)}, which is not in {Path.GetDirectoryName(path)}: copy it there.");

        var actual = FileChecksum.Of(path);
        if (expected != null && actual != expected)
        {
            throw new TasSetupException($"{path} is not the file the movie was made with (its checksum differs from the movie's {property}), "
                                        + "so the movie would not replay: put the original file back. To make the movie start from this file instead, "
                                        + $"delete its {property} line and open it with --edit.");
        }

        return actual;
    }

    /// <summary>The movie's key bindings, then its settings (one of them lives in the control profile the bindings set up).</summary>
    private void ApplyMovieControlsAndSettings()
    {
        if (_options.MoviePath == null)
            return; // free play keeps the local configuration
        _bindings.Apply(Header);
        _settings.Apply(Header);
    }

    public TickPlan PlanTick()
    {
        try
        {
            _hotkeys.Poll(Main.instance.Window.Handle);
            var studioCommands = _studio?.TakeCommands() ?? NoCommands;
            if (_studio?.TakeNotice() is { } studioNotice)
                Notify(studioNotice);
            var plan = _phase switch
            {
                Phase.Booting => Boot(),
                Phase.EnteringWorld => EnterWorld(),
                Phase.Running => Play(studioCommands),
                _ => TickPlan.Wait,
            };
            // Recording frame by frame, the game draws nothing between frames: Windows' cursor shows where the next one aims.
            _input.ShowRealCursor(_phase == Phase.Running && _playback.Recording && _playback.State == PlaybackState.Paused);
            PublishToStudio(plan);
            return plan;
        }
        catch (Exception e)
        {
            // A broken TAS must not keep the game running in an unknown state: stop with the reason.
            Abort("Internal error, stopping: " + e);
            return TickPlan.Wait;
        }
    }

    public void GameCrashed(Exception exception)
    {
        try
        {
            Log.Error("Terraria crashed: " + exception);
            SaveMovie(closing: true);
            _studio?.Close();
        }
        catch (Exception e)
        {
            Log.Error("Could not save the movie while Terraria crashed: " + e);
        }
    }

    /// <summary>Splash screen: once content is loaded, set up the window and head for the movie's world.</summary>
    private TickPlan Boot()
    {
        if (!_bootStateCaptured)
        {
            _pins.CaptureBootState(); // the first tick comes before the first update
            _bootStateCaptured = true;
            Main.instance.Exiting += (_, _) =>
            {
                SaveMovie(closing: true);
                _studio?.Close();
            };
        }

        HoldInputWhileLoading();
        if (Main.showSplash)
            return TickPlan.Run(present: true, realTime: true);

        LanguageManager.Instance.SetLanguage(GameCulture.FromCultureName(GameCulture.CultureName.English));
        if (!SoundEngine.IsAudioSupported)
        {
            if (_options.MoviePath != null)
            {
                Abort("Terraria found no audio device. Without one it skips every sound, and with them the random numbers sounds "
                      + "roll, so movies would not replay as they were made. Connect or enable a sound output (speakers, headphones, "
                      + "a monitor with sound, or a virtual audio device) and start again.");
                return TickPlan.Wait;
            }

            Log.Warn("Terraria found no audio device: free play works, but movies need one (sounds roll random numbers).");
        }

        _pins.ApplyGameplaySettings();
        if (_options.MoviePath != null)
            _window.Lock();
        TexturePreload.LoadNpcsAndGores();
        ApplyMovieControlsAndSettings();

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
        return _phase == Phase.Running ? Play(NoCommands) : TickPlan.Wait;
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

        var size = WindowPins.ScreenSize;
        if (_options.MoviePath != null && (size.X != Header.ScreenWidth || size.Y != Header.ScreenHeight))
        {
            Abort($"The movie plays in a {Header.ScreenWidth}x{Header.ScreenHeight} window, but Terraria made it {size.X}x{size.Y} "
                  + "(a screen too small for it?). The screen size changes what the game simulates, so the movie would not replay: "
                  + "play it on a screen where the window fits.");
            return;
        }

        _headerInEffect = Header;
        GameRng.ResetForMovieStart(Header.Seed);
        _rng.Reset();
        _pins.ResetWorldSession(Header.Seed);
        _pins.ResetVisualSchedules(Header.Seed);
        _pins.ApplyGameplaySettings();
        ApplyMovieControlsAndSettings();
        if (_options.MoviePath != null)
            _achievements.ResetForMovie();
        _frames.ElapsedFrames = MovieStartClock;
        _input.PlaceCursor(new Point(Main.screenWidth / 2, Main.screenHeight / 2));
        _slots[StartSlot] = _saveStates.Capture(0);
        _slotInputs[StartSlot] = InputHistory.Empty;
        _phase = Phase.Running;

        if (_options.Verify)
        {
            _startCensus = StaticCensus.Take();
            _rngTracer!.Enabled = true;
            _syncTarget = new GameSyncTarget(_saveStates, _rngTracer);
            _syncTarget.Remember(_slots[StartSlot]!, 0, _startCensus);
            _syncTest = new SyncTest(_playback, _syncTarget, _slots[StartSlot]!);
            Notify("Movie start reached, start state kept in slot 0");
            return;
        }

        if (_options.RecordFromStart)
        {
            ToggleRecording();
            Notify($"Movie start reached: recording. Play ({KeyName(HotkeyAction.PlayPause)}) or frame advance ({KeyName(HotkeyAction.FrameAdvance)}) "
                   + "with the game window focused");
        }
        else if (_options.ReadOnly)
        {
            Notify("Playing the movie, watch only: nothing records into it. To work on it, start it with --edit");
        }
        else
        {
            Notify($"Movie start reached, start state kept in slot 0. {KeyName(HotkeyAction.ToggleRecording)} records from the current frame");
        }

        if (_options.ReadOnly || _options.MoviePath == null)
            _playback.Play();
    }

    /// <summary>The key of a hotkey as hotkeys.txt sets it, for notices.</summary>
    private string KeyName(HotkeyAction action) =>
        _hotkeys.Map.KeyOf(action) is { } key && key != Key.None ? key.ToString() : $"the {action} hotkey (unbound in {HotkeyMap.FileName})";

    private TickPlan Play(List<Command> studioCommands)
    {
        AccountForLastFrame();
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
            foreach (var command in studioCommands)
                Execute(command);
            WatchMovieFile();
        }

        if (WindowPins.IsMinimized)
            return WaitWhileMinimized();
        _minimized = false;

        var decision = _playback.Next();
        AutosaveMovie();
        ShowStatus();
        if (!decision.Run)
            return TickPlan.Wait;

        if (!decision.Recorded)
            _input.ForgetRealWheel();
        if (decision.Input != null)
            _input.Apply(decision.Input, ScreenSize());
        else
            _input.PassThrough();

        _pins.ApplyGameplaySettings();
        _framesRun++;
        return TickPlan.Run(decision.Present, decision.RealTime);
    }

    /// <summary>
    /// XNA would run the frame without drawing it (see <see cref="WindowPins"/>): frames wait, playing or not, and
    /// everything else goes on (hotkeys, Studio, saving the movie).
    /// </summary>
    private TickPlan WaitWhileMinimized()
    {
        if (!_minimized)
        {
            _minimized = true;
            Notify("Window minimized: frames wait until it is restored");
        }

        AutosaveMovie();
        ShowStatus();
        return TickPlan.Wait;
    }

    /// <summary>Runs once at the boundary after each frame, before anything can save or replace the state.</summary>
    private void AccountForLastFrame()
    {
        if (_framesAccounted == _framesRun)
            return;

        _framesAccounted = _framesRun;
        _stateVersion++;
        _rng.CompleteFrame();
        var kills = _kills.TakeRecords();
        foreach (var kill in kills)
            kill.Frame = _playback.Frame - 1;
        _studio?.AddKills(kills);
        _studio?.FrameCompleted(_playback.Frame);
    }

    private void HandleHotkeys()
    {
        if (_hotkeys.WasPressed(HotkeyAction.ToggleRecording))
            ToggleRecording();
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
        if (_hotkeys.WasPressed(HotkeyAction.ToggleOverlay))
            ToggleOverlay();
    }

    /// <summary>A command from TAS Studio: the same actions as the hotkeys, plus slot numbers and frame targets.</summary>
    private void Execute(Command command)
    {
        var argument = command.Argument;
        switch (command.Kind)
        {
            case CommandKind.TogglePause:
                _playback.TogglePause();
                break;
            case CommandKind.FrameAdvance:
                _playback.AdvanceOneFrame();
                break;
            case CommandKind.FastForwardOn:
                _playback.SetFastForward(true);
                break;
            case CommandKind.FastForwardOff:
                _playback.SetFastForward(false);
                break;
            case CommandKind.PreviousSlot:
                SelectSlot((_slot + UserSlots - 2) % UserSlots + 1);
                break;
            case CommandKind.NextSlot:
                SelectSlot(_slot % UserSlots + 1);
                break;
            case CommandKind.SelectSlot when argument is >= 1 and <= UserSlots:
                SelectSlot(argument);
                break;
            case CommandKind.SaveSlot when argument is >= 1 and <= UserSlots:
                SaveSlot(argument);
                break;
            case CommandKind.LoadSlot when argument is >= StartSlot and <= UserSlots:
                LoadSlot(argument);
                break;
            case CommandKind.Restart:
                LoadSlot(StartSlot);
                break;
            case CommandKind.ReloadMovie:
                ReloadMovie();
                break;
            case CommandKind.RunToFrame when argument >= 0:
                RunToFrame(argument);
                break;
            case CommandKind.ToggleRecording:
                ToggleRecording();
                break;
            case CommandKind.ToggleOverlay:
                ToggleOverlay();
                break;
            default:
                Notify($"Ignored Studio command {command.Kind} {argument}");
                break;
        }
    }

    /// <summary>
    /// Goes back through the closest earlier savestate of this movie if needed (also when the game left the movie,
    /// e.g. after loading a savestate of another version), then fast-forwards and pauses before <paramref name="frame"/>.
    /// The frames on the way replay the movie, even while recording.
    /// </summary>
    private void RunToFrame(int frame)
    {
        if (frame < _playback.Frame || !FollowsMovie(GameInput(), _playback.Frame))
        {
            var start = -1;
            for (var slot = StartSlot; slot <= UserSlots; slot++)
            {
                if (_slots[slot] is { } state && state.Frame <= frame && FollowsMovie(_slotInputs[slot]!, state.Frame)
                    && (start < 0 || state.Frame > _slots[start]!.Frame))
                    start = slot;
            }

            if (start < 0)
            {
                Notify($"No savestate of this movie at or before frame {frame}");
                return;
            }

            LoadSlot(start);
        }

        if (frame > _playback.Frame)
        {
            _playback.RunTo(frame);
            Notify($"Running to frame {Math.Min(frame, _playback.Recording ? _playback.TotalFrames : frame)}");
        }
    }

    private void ToggleOverlay()
    {
        _overlay.Enabled = !_overlay.Enabled;
        Notify(_overlay.Enabled ? "Overlay on: frame, position and velocity show over the game from the next frame drawn" : "Overlay off");
    }

    /// <summary>What the overlay shows, read while a frame draws.</summary>
    private string[] OverlayLines()
    {
        var player = Main.LocalPlayer;
        var state = (_playback.Recording ? "REC " : "") + _playback.State.ToString().ToUpperInvariant();
        return new[]
        {
            $"frame {_playback.Frame} {state}",
            $"pos {Exact(player.position.X)}, {Exact(player.position.Y)}",
            $"vel {Exact(player.velocity.X)}, {Exact(player.velocity.Y)}",
        };

        static string Exact(float value) => value.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Starts recording at the current frame, paused (a stray F1 during playback must not overwrite the rest of the
    /// movie at 60 frames per second), or stops and saves.
    /// </summary>
    private void ToggleRecording()
    {
        if (_playback.Recording)
        {
            _playback.StopRecording();
            SaveMovie();
            Notify($"Recording off: the movie has {_playback.TotalFrames} frames");
            return;
        }

        if (_movieFile == null)
        {
            Notify(_options.Verify ? "No recording while verifying"
                : _options.ReadOnly ? "Watch only: nothing records into this movie. To work on it, start it with --edit"
                : "No movie to record into: start TerrariaTas with --new, or with a movie and --edit");
            return;
        }

        if (_movieFileError != null)
        {
            Notify($"Cannot record: the movie file was edited and has an error ({_movieFileError}). Fix it and save: the TAS loads it then");
            return;
        }

        var played = GameInput();
        if (played.Frames < _playback.Frame)
        {
            Notify($"Cannot record here: frames {played.Frames}-{_playback.Frame - 1} were played past the end of the movie without recording. "
                   + $"Load a savestate at or before frame {played.Frames}");
            return;
        }

        // Recording continues from the game as it is: if it came from a savestate of another version of the
        // movie, the movie takes that version's input first.
        var changedFrom = _playback.Recorder.Adopt(played);
        _stateInput = played;
        _stateFrame = _playback.Frame;
        _input.ForgetRealWheel();
        _playback.Pause();
        _playback.StartRecording();
        var frame = _playback.Frame;
        Notify(changedFrom is { } changed
            ? $"Recording from frame {frame} (paused). The movie now follows the loaded savestate from frame {changed}"
            : frame < _playback.TotalFrames
                ? $"Recording from frame {frame} (paused): frames played now replace frames {frame}-{_playback.TotalFrames - 1}"
                : $"Recording from frame {frame} (paused)");
    }

    /// <summary>The input the game state was made with: the input of the last savestate loaded, then the movie's.</summary>
    private InputHistory GameInput() =>
        InputHistory.Splice(_stateInput, _stateFrame, _playback.Recorder.HistoryUpTo(_playback.Frame));

    /// <summary>True when a game state made with <paramref name="input"/> up to <paramref name="frame"/> is on the movie's timeline.</summary>
    private bool FollowsMovie(InputHistory input, int frame) =>
        input.Frames == frame && _playback.Recorder.FirstDifferenceFrom(input) == null;

    private void SelectSlot(int slot)
    {
        _slot = slot;
        Notify(_slots[slot] is { } state ? $"Slot {slot} (frame {state.Frame})" : $"Slot {slot} (empty)");
    }

    private void SaveSlot(int slot)
    {
        _slots[slot] = _saveStates.Capture(_playback.Frame);
        _slotInputs[slot] = GameInput();
        Notify($"Saved slot {slot} at frame {_playback.Frame}");
    }

    /// <summary>
    /// Loads a savestate. While recording, the movie follows it: if the state was made on another version of the
    /// movie, the movie takes that version's input up to the state.
    /// </summary>
    private void LoadSlot(int slot)
    {
        if (_slots[slot] is not { } state)
        {
            Notify($"Slot {slot} is empty");
            return;
        }

        var input = _slotInputs[slot]!;
        var movieNote = "";
        if (_playback.Recording && input.Frames < state.Frame)
        {
            _playback.StopRecording();
            SaveMovie();
            movieNote = $". Recording stopped: frames {input.Frames}-{state.Frame - 1} of this savestate were played past the end of the movie without recording";
        }
        else if (_playback.Recording)
        {
            if (_playback.Recorder.Adopt(input) is { } changed)
                movieNote = $". The movie follows this savestate from frame {changed} (what came after was replaced)";
        }
        else if (input.Frames < state.Frame)
        {
            movieNote = $" (played past the end of the movie from frame {input.Frames})";
        }
        else if (_playback.Recorder.FirstDifferenceFrom(input) is { } difference)
        {
            movieNote = $". It belongs to another version of the movie (input differs from frame {difference}): playing on will not match the movie";
        }

        _saveStates.Restore(state);
        _stateInput = input;
        _stateFrame = state.Frame;
        _playback.JumpTo(state.Frame);
        _stateVersion++;
        // Drawing now would run Terraria's draw-time logic outside a frame, so the picture refreshes on the next frame.
        Notify($"Loaded slot {slot}: frame {state.Frame}{movieNote} (picture updates on the next frame)");
    }

    /// <summary>
    /// Hot reload: a few times a second, looks whether a text editor saved the movie file, and loads it once the editor
    /// is done writing (<see cref="LoadEditedMovie"/>).
    /// </summary>
    private void WatchMovieFile()
    {
        if (_movieFile == null || _clock.Elapsed - _lastFileCheck < FileCheckInterval)
            return;

        _lastFileCheck = _clock.Elapsed;
        try
        {
            if (_movieFile.ChangedOnDisk() && _movieFile.SinceLastWrite >= EditSettleTime)
                LoadEditedMovie();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Being written right now: the next check reads it.
        }
    }

    /// <summary>F10: loads the movie file now, if it changed since the TAS loaded or saved it.</summary>
    private void ReloadMovie()
    {
        if (_movieFile == null)
        {
            Notify(_options.ReadOnly ? "Watch only: the movie plays as it was when it started. To work on it, start it with --edit" : "No movie file to reload");
            return;
        }

        try
        {
            if (_movieFile.ChangedOnDisk())
                LoadEditedMovie();
            else
                Notify("The movie file has not changed since the TAS loaded or saved it");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Notify("Reload failed: " + e.Message);
        }
    }

    /// <summary>
    /// Loads the movie file as an editor left it, and puts the game on the new version: when the game went through
    /// changed input, the latest savestate of the new version at or before the change is loaded and the frames up to
    /// where the game was replay (paused there, still recording if it was). Frames recorded but not saved yet go to a
    /// copy of the movie, never over the edit. Header changes only apply when the TAS starts again.
    /// </summary>
    /// <exception cref="IOException">The copy or the file could not be written or read: the next check tries again.</exception>
    private void LoadEditedMovie()
    {
        var aside = _playback.Recorder.HasUnsavedChanges ? KeepRecordingAside() : null;
        var asideNote = aside != null ? $"; the frames recorded since the last save are in {aside}, recording stopped" : "";
        var text = _movieFile!.Read(); // only now: should anything before fail, the edit still shows as one
        var played = GameInput();
        var before = _playback.Movie;
        var frame = _playback.Frame;
        try
        {
            _playback.Reload(text);
        }
        catch (MovieFormatException e)
        {
            _movieFileError = $"line {e.LineNumber}: {e.Reason}";
            var stopped = _playback.Recording ? "; recording stopped, so that it does not write over the file" : "";
            _playback.StopRecording();
            Notify($"The movie file changed but cannot be loaded, {_movieFileError}{asideNote}{stopped}");
            return;
        }

        _movieFileError = null;
        _stateInput = played;
        _stateFrame = frame;
        var edit = new MovieEdit(before, _playback.Movie, played);
        string what;
        if (edit.FirstChangedFrame is not { } changed)
        {
            what = "no input changed";
        }
        else if (edit.NeedsReplay)
        {
            RunToFrame(frame);
            var target = Math.Min(frame, _playback.TotalFrames);
            what = $"input changed from frame {changed}, " + (target > _playback.Frame ? $"replaying from frame {_playback.Frame} to frame {target}" : $"back at frame {_playback.Frame}");
        }
        else
        {
            what = $"input changed from frame {changed}"
                   + (changed >= edit.FollowedUpTo && edit.FollowedUpTo < frame ? $" (the game left the movie at frame {edit.FollowedUpTo} and stays as it is)" : "");
        }

        var headerChanges = _headerInEffect?.DifferencesFrom(Header) ?? Array.Empty<string>();
        var header = headerChanges.Count > 0 ? $"; the game still plays with the header it started with ({string.Join(", ", headerChanges)} changed): restart the TAS to apply it" : "";
        Notify($"Movie reloaded: {what}{header}{asideNote}");
    }

    /// <summary>
    /// The movie file was edited while recorded frames were not saved yet: keeps the recording in a copy next to the
    /// movie and stops recording, so that nothing writes over the edit.
    /// </summary>
    /// <returns>The file name of the copy.</returns>
    private string KeepRecordingAside()
    {
        var recorder = _playback.Recorder;
        var text = recorder.Text();
        var path = _movieFile!.SaveCopy(text, "recording", DateTime.Now);
        recorder.Load(text); // what it holds is saved now, in the copy
        _playback.StopRecording();
        Log.Info($"The movie file was edited while recording: the recording is kept in {path}");
        return Path.GetFileName(path);
    }

    /// <summary>Writes recorded input to the movie file once recording rests for a second, and every ten seconds while it goes on.</summary>
    private void AutosaveMovie()
    {
        var movie = _playback.Recorder;
        if (_movieFile == null || !movie.HasUnsavedChanges)
            return;

        var now = _clock.Elapsed;
        if (movie.Version != _changeVersion)
        {
            _changeVersion = movie.Version;
            _lastChange = now;
        }

        if (now - _lastChange >= SaveWhenIdle || now - _lastSaveAttempt >= SaveWhileRecording)
            SaveMovie();
    }

    /// <summary>
    /// Writes the movie if it changed. A failure is reported and retried by the autosave. Never writes over an edit of
    /// the file: the watch loads it in a moment, or when the game is <paramref name="closing"/>, the recording goes to a
    /// copy.
    /// </summary>
    private void SaveMovie(bool closing = false)
    {
        var movie = _playback.Recorder;
        if (_movieFile == null || !movie.HasUnsavedChanges)
            return;

        _lastSaveAttempt = _clock.Elapsed;
        try
        {
            if (_movieFileError != null || _movieFile.ChangedOnDisk())
            {
                if (closing)
                    KeepRecordingAside();
                return;
            }

            var hadBackup = _movieFile.HasBackup;
            var text = movie.Text();
            _movieFile.Save(text);
            movie.Load(text);
            if (!hadBackup && _movieFile.HasBackup)
                Notify($"Recording saved into {Path.GetFileName(_movieFile.Path)}; the version from before is in {Path.GetFileName(_movieFile.BackupPath)}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Notify("Could not save the movie, trying again shortly: " + e.Message);
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

        // Diagnostics: they explain desyncs (or predict ones this movie does not show) but decide nothing.
        results.AddRange(_syncTarget!.RestoreGaps);
        results.AddRange(CompareStartWithPreviousLaunch());
        foreach (var line in results)
            Log.Info(line);
        WriteVerifyReport(passed ? "PASSED" : "FAILED", results);
        Exit(passed);
    }

    /// <summary>
    /// Game fields that read differently at the movie start than in the previous launch: inputs from outside the
    /// movie (how long booting took, the machine) that the pins do not neutralize yet.
    /// </summary>
    private List<string> CompareStartWithPreviousLaunch()
    {
        var path = Path.Combine(_options.DataDir, Path.GetFileNameWithoutExtension(_options.MoviePath) + ".census");
        var key = BuildAndMovieKey();
        var lines = new List<string>();
        var previous = File.Exists(path) ? File.ReadAllText(path) : "";
        if (previous.StartsWith(key + "\n", StringComparison.Ordinal))
        {
            var differences = StaticCensus.Differences(StaticCensus.Parse(previous.Substring(key.Length + 1)), _startCensus!);
            lines.Add("Movie start against the previous launch: " + (differences.Count == 0 ? "every audited field is the same" : $"{differences.Count} fields differ"));
            lines.AddRange(differences.Select(difference => "  " + difference));
        }
        else
        {
            lines.Add("Movie start against the previous launch: nothing comparable yet, run --verify again");
        }

        File.WriteAllText(path, key + "\n" + StaticCensus.Write(_startCensus!));
        return lines;
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
        var state = _syncTest != null ? "VERIFYING" : (_playback.Recording ? "REC " : "") + _playback.State.ToString().ToUpperInvariant();
        var text = $"Terraria TAS {Version} | {state} | frame {_playback.Frame}/{_playback.TotalFrames} | slot {_slot} | {_notice}";
        _status.Show(text, urgent: _playback.State == PlaybackState.Paused);
    }

    private void Notify(string message)
    {
        _notice = message;
        Log.Info(message);
    }

    /// <summary>
    /// Studio only reads the game (forecasts restore what they touch, or throw GameStateChangedException),
    /// so an error there stops the link rather than the TAS session and its savestates.
    /// </summary>
    private void PublishToStudio(TickPlan plan)
    {
        if (_studio == null)
            return;

        try
        {
            _studio.Publish(StudioView(plan));
        }
        catch (Exception e) when (e is not GameStateChangedException)
        {
            _studio = null;
            Log.Error("TAS Studio link stopped: " + e);
            Notify("TAS Studio link stopped after an error (details in the console)");
        }
    }

    private SessionView StudioView(TickPlan plan)
    {
        var inWorld = _phase == Phase.Running && !Main.gameMenu;
        return new SessionView
        {
            Phase = _phase switch
            {
                Phase.Booting => SessionPhase.Booting,
                Phase.EnteringWorld => SessionPhase.LoadingWorld,
                _ => SessionPhase.Running,
            },
            Frame = _playback.Frame,
            State = _playback.State,
            Slot = _slot,
            SlotFrames = _slots.Select(state => state?.Frame ?? -1).ToArray(),
            Notice = _notice,
            Recording = _playback.Recording,
            NextInput = _playback.Recording && _phase == Phase.Running ? NextRecordedInput() : null,
            TotalFrames = _playback.TotalFrames,
            MovieVersion = _playback.Recorder.Version,
            ReadMovie = () => _playback.Movie,
            InWorld = inWorld,
            CanInspect = inWorld && !plan.RunFrame,
            Overlay = _overlay.Enabled,
            StateVersion = _stateVersion,
        };
    }

    /// <summary>While recording: what the next frame would record right now, with the tile under the cursor.</summary>
    private InputView NextRecordedInput()
    {
        var live = _input.PeekRealDevices(Main.instance.Window.Handle);
        var cursor = new Point(live.Cursor!.Value.X, live.Cursor.Value.Y);
        var tile = CursorMapper.ToTile(cursor, ScreenSize());
        return new InputView
        {
            Keys = live.Keys.ToArray(),
            Buttons = live.Buttons,
            CursorX = cursor.X,
            CursorY = cursor.Y,
            CursorTileX = tile.X,
            CursorTileY = tile.Y,
        };
    }

    private static Point ScreenSize()
    {
        var parameters = Main.instance.GraphicsDevice.PresentationParameters;
        return new Point(parameters.BackBufferWidth, parameters.BackBufferHeight);
    }
}
