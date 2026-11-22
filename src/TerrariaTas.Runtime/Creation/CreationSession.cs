using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.IO;
using Terraria.Localization;
using Terraria.WorldBuilding;
using TerrariaTas.Core;
using TerrariaTas.Core.Launch;
using TerrariaTas.Core.Movies;
using TerrariaTas.Runtime.Determinism;
using TerrariaTas.Runtime.Frames;
using TerrariaTas.Runtime.GameAccess;
using TerrariaTas.Runtime.Status;

namespace TerrariaTas.Runtime.Creation;

/// <summary>
/// The game launch behind --new: boots to the title screen, creates the character and the world the way the game's
/// own menus do (or takes the existing files it was given), writes the movie that starts with them, and exits. The
/// recording happens in a fresh launch, so nothing world generation leaves in the game's memory reaches the movie, and
/// the movie start is the same as in every later launch.
/// </summary>
internal sealed class CreationSession : IFrameDriver
{
    // The longest names the game's naming screens take.
    private const int MaxPlayerName = 20;
    private const int MaxWorldName = 27;

    // Hard to reach even for a large world on a slow machine; the generation thread can hang in ways nothing reports.
    private static readonly TimeSpan TimeLimit = TimeSpan.FromMinutes(15);

    private readonly TasOptions _options;
    private readonly NewMovieOptions _new;
    private readonly string _name;
    private readonly FrameLoop _frames;
    private readonly EnvironmentPins _pins = new() { KeepConfigurationFiles = true, SavesCreatedFiles = true };
    private readonly WindowPins _window = new();
    private readonly AchievementPins _achievements = new();
    private readonly SettingPins _settings = new();
    private readonly KeyBindingPins _bindings = new();
    private readonly StatusDisplay _status = new();

    // Deleted if the creation fails: a half-made movie must not leave files behind.
    private readonly List<string> _created = new();
    private Step _step = Step.Booting;
    private string _playerFile = "";
    private string? _characterKind; // of the character created, null when an existing one is used
    private string _worldFile = "";
    private int _seed;
    private Task? _generation;
    private volatile int _generated; // 1 when the world was generated and saved, 2 when the generation gave up
    private bool _watchingExit;

    private CreationSession(TasOptions options)
    {
        _options = options;
        _new = options.NewMovie;
        _name = Path.GetFileNameWithoutExtension(options.MoviePath!);
        _frames = new FrameLoop(this);
    }

    private enum Step
    {
        Booting,
        GeneratingWorld,
        Done,
    }

    private string PlayersDir => Path.Combine(_options.DataDir, "Players");

    private string WorldsDir => Path.Combine(_options.DataDir, "Worlds");

    public static void Install(TasOptions options, Harmony harmony)
    {
        if (File.Exists(options.MoviePath))
            throw new TasSetupException($"{options.MoviePath} already exists, and --new never replaces a movie.");

        var session = new CreationSession(options);
        session._frames.Install(harmony);
        session._pins.Install(harmony);
        session._achievements.Install(harmony); // saving a player also saves achievements.dat, which TAS sessions never write
        var size = new MovieHeader();
        session._window.Configure(size.ScreenWidth, size.ScreenHeight);
        StartWatchdog();
        Log.Info($"Creating the movie {options.MoviePath}: the game window closes once its character and world are made.");
    }

    public TickPlan PlanTick()
    {
        try
        {
            if (!_watchingExit)
            {
                Main.instance.Exiting += (_, _) => ClosedEarly();
                _watchingExit = true;
            }

            switch (_step)
            {
                case Step.Booting when Main.showSplash:
                    return TickPlan.Run(present: true, realTime: true);
                case Step.Booting:
                    Start();
                    return _step == Step.Done ? TickPlan.Wait : TickPlan.Run(present: true, realTime: true);
                case Step.GeneratingWorld:
                    return WhileGenerating();
                default:
                    return TickPlan.Wait;
            }
        }
        catch (TasSetupException e)
        {
            Fail(e.Message, exitCode: 2);
        }
        catch (Exception e)
        {
            Fail("Creating the movie failed: " + e);
        }

        return TickPlan.Wait;
    }

    /// <summary>The game window was closed before the movie was written: nothing half made stays behind.</summary>
    private void ClosedEarly()
    {
        if (_step == Step.Done)
            return;

        _step = Step.Done;
        Environment.ExitCode = 1;
        Log.Error("The game was closed before the movie was made.");
        try
        {
            DeleteCreated();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not delete what was made so far: " + e.Message);
        }
    }

    public void GameCrashed(Exception exception)
    {
        try
        {
            Log.Error("Terraria crashed while creating the movie: " + exception);
            DeleteCreated();
        }
        catch (Exception e)
        {
            Log.Error("Could not clean up after the crash: " + e);
        }
    }

    /// <summary>Title screen: the character first (a new one is made for the world's difficulty), then the world.</summary>
    private void Start()
    {
        LanguageManager.Instance.SetLanguage(GameCulture.FromCultureName(GameCulture.CultureName.English));
        Directory.CreateDirectory(PlayersDir);
        Directory.CreateDirectory(WorldsDir);

        bool journeyWorld;
        if (_new.World != null)
        {
            _worldFile = _new.World;
            journeyWorld = ExistingWorld(Path.Combine(WorldsDir, _worldFile)).GameMode == GameModeJourney;
        }
        else
        {
            journeyWorld = _new.Difficulty == WorldDifficulty.Journey;
        }

        PlayerFileData player;
        if (_new.Player != null)
        {
            _playerFile = _new.Player;
            player = ExistingPlayer(Path.Combine(PlayersDir, _playerFile));
        }
        else
        {
            _playerFile = MovieLibrary.FreeFileName(PlayersDir, _name, ".plr");
            var path = Path.Combine(PlayersDir, _playerFile);
            _created.Add(path);
            player = CreatePlayer(path, Shorten(_name, MaxPlayerName), journeyWorld);
            _characterKind = journeyWorld ? "journey" : "softcore";
            Log.Info($"Created a {_characterKind} character: {path}");
        }

        var journeyPlayer = player.Player.difficulty == PlayerJourney;
        if (journeyPlayer != journeyWorld)
        {
            throw new TasSetupException(journeyPlayer
                ? $"{_playerFile} is a journey character, and journey characters only enter journey worlds: add --journey."
                : $"Journey worlds only take journey characters, and {_playerFile} is not one.");
        }

        if (_new.World != null)
        {
            WriteMovie();
            return;
        }

        _worldFile = MovieLibrary.FreeFileName(WorldsDir, _name, ".wld");
        StartWorld(Path.Combine(WorldsDir, _worldFile), Shorten(_name, MaxWorldName));
        _step = Step.GeneratingWorld;
    }

    private const byte PlayerJourney = 3; // Player.difficulty: 0 softcore, 1 mediumcore, 2 hardcore, 3 journey
    private const int GameModeJourney = 3; // Main.GameMode: 0 normal, 1 expert, 2 master, 3 journey

    /// <summary>
    /// A new character, made by the game's character creation screen as its Create button does, with the defaults the
    /// screen starts with (FinishCreatingCharacter): starting items, journey items for a journey character.
    /// </summary>
    [MirrorsGameMethod("Terraria.GameContent.UI.States.UICharacterCreation", "FinishCreatingCharacter")]
    [MirrorsGameMethod("Terraria.GameContent.UI.States.UICharacterSelect", "NewCharacterClick")]
    [MirrorsGameMethod("Terraria.IO.PlayerFileData", "CreateAndSave")]
    private static PlayerFileData CreatePlayer(string path, string name, bool journey)
    {
        var player = new Player();
        var screen = new UICharacterCreation(player); // what the menu shows for a new character, built on this player
        player.name = name;
        player.difficulty = journey ? PlayerJourney : (byte)0;
        AccessTools.Method(typeof(UICharacterCreation), "TryAutoAssigningHair").Invoke(screen, null);
        AccessTools.Method(typeof(UICharacterCreation), "SetupPlayerStatsAndInventoryBasedOnDifficulty").Invoke(screen, null);

        // As PlayerFileData.CreateAndSave, at a path of our choosing rather than one made from the character's name.
        var file = new PlayerFileData(path, cloudSave: false)
        {
            Metadata = FileMetadata.FromCurrentSettings(FileType.Player),
            Player = player,
        };
        Player.SavePlayer(file, skipMapSave: true);
        if (!File.Exists(path))
            throw new InvalidOperationException($"Terraria did not save the character to {path}.");
        return file;
    }

    /// <summary>
    /// Starts generating the world as the world creation screen's Create button does (FinishCreatingWorld): the game
    /// shows its progress screen, generates on a thread of its own and saves the world when done.
    /// </summary>
    [MirrorsGameMethod("Terraria.GameContent.UI.States.UIWorldCreation", "FinishCreatingWorld")]
    [MirrorsGameMethod("Terraria.GameContent.UI.States.UIWorldCreation", "ClearSeed")]
    private void StartWorld(string path, string name)
    {
        _seed = _new.Seed ?? new Random().Next();
        WorldGenerationOptions.Reset(); // no special seed
        WorldGen.SecretSeed.ClearAllSeeds();
        WorldGen.SetWorldSize((int)_new.Size);
        WorldGen.WorldGenParam_Evil = (int)_new.Evil;
        var gameMode = (int)_new.Difficulty;
        Main.worldName = name;
        Main.autoGenFileLocation = path; // CreateMetadata takes it instead of a path made from the world's name
        Main.ActiveWorldFileData = WorldFile.CreateMetadata(name, cloudSave: false, gameMode);
        Main.GameMode = gameMode;
        Main.ActiveWorldFileData.SetSeed(_seed.ToString(CultureInfo.InvariantCulture));
        if (!string.Equals(Path.GetFullPath(Main.worldPathName), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Terraria would save the world to {Main.worldPathName} instead of {path}.");

        _created.Add(path);
        Log.Info($"Generating a {_new.DescribeWorld()}, world seed {_seed}: {path}");
        _generation = WorldGen.CreateNewWorld(null, null, playable => _generated = playable ? 1 : 2);
    }

    private TickPlan WhileGenerating()
    {
        if (_generation!.IsFaulted)
        {
            Fail("Terraria failed to generate the world: " + _generation.Exception!.GetBaseException());
            return TickPlan.Wait;
        }

        switch (_generated)
        {
            case 0:
                _status.Show($"Terraria TAS | creating {_name}: generating the world (the window closes when done)");
                return TickPlan.Run(present: true, realTime: true);
            case 2:
                Fail("Terraria gave up generating the world (it said it is not playable). Try another seed (--seed).");
                return TickPlan.Wait;
            default:
                WriteMovie();
                return TickPlan.Wait;
        }
    }

    /// <summary>
    /// The movie: the files' names and checksums, and the game settings and key bindings of the TAS configuration that
    /// differ from a fresh install, so that recording uses them.
    /// </summary>
    private void WriteMovie()
    {
        var playerPath = Path.Combine(PlayersDir, _playerFile);
        var worldPath = Path.Combine(WorldsDir, _worldFile);
        if (!File.Exists(worldPath))
            throw new InvalidOperationException($"Terraria did not save the world to {worldPath}.");

        var header = new MovieHeader
        {
            TerrariaVersion = GameVersion.Running,
            Player = _playerFile,
            PlayerChecksum = FileChecksum.Of(playerPath),
            World = _worldFile,
            WorldChecksum = FileChecksum.Of(worldPath),
        };
        foreach (var setting in _settings.ReadChangedFromDefaults())
            header.Settings[setting.Key] = setting.Value;
        foreach (var binding in _bindings.ReadChangedFromDefaults())
            header.Bindings[binding.Key] = binding.Value;

        var text = Description() + MovieWriter.Write(new Movie(header, Array.Empty<InputLine>()));
        Directory.CreateDirectory(Path.GetDirectoryName(_options.MoviePath)!);
        using (var stream = new FileStream(_options.MoviePath!, FileMode.CreateNew, FileAccess.Write))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            writer.Write(text);
        _created.Clear(); // the movie needs them now

        if (header.Settings.Count > 0)
            Log.Info("Settings of the TAS configuration kept in the movie: " + string.Join(", ", header.Settings.Select(setting => $"{setting.Key} {setting.Value}")));
        if (header.Bindings.Count > 0)
            Log.Info("Key bindings kept in the movie: " + string.Join(", ", header.Bindings.Select(binding => $"{binding.Key} {string.Join(" ", binding.Value)}")));
        Log.Info($"Created {_options.MoviePath}");
        Exit(0);
    }

    private string Description()
    {
        var character = _characterKind == null ? $"the character {_playerFile}" : $"a new {_characterKind} character";
        var world = _new.World != null ? $"the world {_worldFile}" : $"a new {_new.DescribeWorld()} (world seed {_seed})";
        return $"# Made with Terraria TAS on {DateTime.Now:yyyy-MM-dd}: {character} in {world}.\n"
               + "# The checksums pin these exact files: the movie only starts from them.\n";
    }

    private static PlayerFileData ExistingPlayer(string path)
    {
        if (!File.Exists(path))
            throw new TasSetupException($"{Path.GetFileName(path)} is not in {Path.GetDirectoryName(path)}.");
        return Player.GetFileData(path, cloudSave: false) is { Player: not null } file
            ? file
            : throw new TasSetupException($"Terraria cannot load the character {path}.");
    }

    private static WorldFileData ExistingWorld(string path)
    {
        if (!File.Exists(path))
            throw new TasSetupException($"{Path.GetFileName(path)} is not in {Path.GetDirectoryName(path)}.");
        return WorldFile.GetAllMetadata(path, cloudSave: false)
               ?? throw new TasSetupException($"Terraria cannot read the world {path}.");
    }

    private static string Shorten(string name, int length) => name.Length <= length ? name : name.Substring(0, length);

    private void Fail(string message, int exitCode = 1)
    {
        Log.Error(message);
        DeleteCreated();
        Exit(exitCode);
    }

    private void DeleteCreated()
    {
        foreach (var path in _created.Where(File.Exists))
        {
            File.Delete(path);
            Log.Info("Deleted " + path);
        }

        _created.Clear();
    }

    private void Exit(int exitCode)
    {
        _step = Step.Done;
        Environment.ExitCode = exitCode;
        Main.instance.Exit();
    }

    private static void StartWatchdog()
    {
        new Thread(() =>
        {
            Thread.Sleep(TimeLimit);
            Log.Error($"Still creating the movie after {TimeLimit.TotalMinutes} minutes, stopping.");
            Environment.Exit(3);
        }) { IsBackground = true, Name = "TAS creation watchdog" }.Start();
    }
}
