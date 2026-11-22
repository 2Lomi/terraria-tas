using System;
using System.Collections.Generic;
using System.Globalization;

namespace TerrariaTas.Core.Launch;

/// <summary>What TerrariaTas.exe was asked to do.</summary>
public enum LaunchMode
{
    /// <summary>No movie: the game runs normally, with the TAS hotkeys once in a world.</summary>
    FreePlay,

    /// <summary>Watch a movie: it plays from its start, and nothing records into it or reloads it.</summary>
    Play,

    /// <summary>Work on a movie: paused at its start, with recording, savestates and reloading of the edited file.</summary>
    Edit,

    /// <summary>Create a character, a world and a movie that starts with them, then record from the movie's first frame.</summary>
    New,

    /// <summary>Determinism self-test of a movie.</summary>
    Verify,

    /// <summary>List the movies with the player and world each starts with.</summary>
    List,

    /// <summary>Internal: the game launch behind <see cref="New"/> that creates the files and the movie, then exits.</summary>
    Create,
}

/// <summary>The command line of TerrariaTas.exe, checked but not resolved against the disk yet.</summary>
public sealed class LaunchArguments
{
    // The flags only --new takes, to say which one an existing movie cannot use.
    private readonly List<string> _creationFlags = new();
    private string? _modeFlag;
    private string? _sizeFlag;
    private string? _evilFlag;
    private string? _difficultyFlag;

    private LaunchArguments()
    {
    }

    public LaunchMode Mode { get; private set; } = LaunchMode.FreePlay;

    public bool Help { get; private set; }

    /// <summary>
    /// The movie as given: a path, or a name in the movies folder. With --new, the name of the movie to create (null for a
    /// generated name); with --create, its full path.
    /// </summary>
    public string? Movie { get; private set; }

    public string? TerrariaDir { get; private set; }

    public string? DataDir { get; private set; }

    public string? MoviesDir { get; private set; }

    public bool IgnoreVersion { get; private set; }

    public NewMovieOptions NewMovie { get; } = new();

    /// <exception cref="ArgumentException">The arguments do not make sense: the message says why.</exception>
    public static LaunchArguments Parse(IReadOnlyList<string> args)
    {
        var result = new LaunchArguments();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help" or "/?":
                    result.Help = true;
                    return result;
                case "--terraria":
                    result.TerrariaDir = Value(args, ref i);
                    break;
                case "--data":
                    result.DataDir = Value(args, ref i);
                    break;
                case "--movies":
                    result.MoviesDir = Value(args, ref i);
                    break;
                case "--ignore-version":
                    result.IgnoreVersion = true;
                    break;
                case "--edit":
                    result.SetMode(LaunchMode.Edit, arg);
                    break;
                case "--verify":
                    result.SetMode(LaunchMode.Verify, arg);
                    break;
                case "--list":
                    result.SetMode(LaunchMode.List, arg);
                    break;
                case "--new":
                    result.SetMode(LaunchMode.New, arg);
                    if (i + 1 < args.Count && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                        result.SetMovie(args[++i]);
                    break;
                case "--create":
                    result.SetMode(LaunchMode.Create, arg);
                    result.SetMovie(Value(args, ref i));
                    break;
                case "--small" or "--medium" or "--large":
                    result.SetSize(arg.Substring(2), arg);
                    break;
                case "--size":
                    result.SetSize(Value(args, ref i), arg);
                    break;
                case "--corruption" or "--crimson":
                    result.SetEvil(arg.Substring(2), arg);
                    break;
                case "--evil" or "--type":
                    result.SetEvil(Value(args, ref i), arg);
                    break;
                case "--normal" or "--classic" or "--expert" or "--master" or "--journey":
                    result.SetDifficulty(arg.Substring(2), arg);
                    break;
                case "--difficulty":
                    result.SetDifficulty(Value(args, ref i), arg);
                    break;
                case "--seed":
                    result.NewMovie.Seed = ParseSeed(Value(args, ref i));
                    result._creationFlags.Add(arg);
                    break;
                case "--player":
                    result.NewMovie.Player = Value(args, ref i);
                    result._creationFlags.Add(arg);
                    break;
                case "--world":
                    result.NewMovie.World = Value(args, ref i);
                    result._creationFlags.Add(arg);
                    break;
                case "--play":
                    throw new ArgumentException("--play is not needed anymore: a movie given alone plays. To work on it, add --edit instead.");
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                        throw new ArgumentException($"Unexpected argument '{arg}'.");
                    result.SetMovie(arg);
                    break;
            }
        }

        result.Check();
        return result;
    }

    private void Check()
    {
        if (_modeFlag == null)
            Mode = Movie != null ? LaunchMode.Play : LaunchMode.FreePlay;

        if (Mode is not (LaunchMode.New or LaunchMode.Create) && _creationFlags.Count > 0)
        {
            throw new ArgumentException($"{_creationFlags[0]} only goes with --new: an existing movie starts with the player and world it names. "
                                        + "To make a new movie, add --new.");
        }

        if (Mode == LaunchMode.List && Movie != null)
            throw new ArgumentException($"--list lists every movie: leave out '{Movie}'.");
        if ((Mode is LaunchMode.Edit or LaunchMode.Verify) && Movie == null)
            throw new ArgumentException($"{_modeFlag} needs the movie to open, e.g. 'TerrariaTas my-run {_modeFlag}'.");

        foreach (var (flag, file, folder) in new[] { ("--player", NewMovie.Player, "Players"), ("--world", NewMovie.World, "Worlds") })
        {
            if (file != null && file.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
                throw new ArgumentException($"{flag} takes the name of a file in the data folder's {folder} folder, not a path: copy the file there first.");
        }

        if (NewMovie.World != null)
        {
            var worldFlag = _sizeFlag ?? _evilFlag ?? _difficultyFlag ?? (NewMovie.Seed != null ? "--seed" : null);
            if (worldFlag != null)
                throw new ArgumentException($"--world starts in a world that exists: its size, evil, difficulty and seed are its own, so {worldFlag} does not apply.");
        }
    }

    private void SetMode(LaunchMode mode, string flag)
    {
        if (_modeFlag != null && Mode != mode)
            throw new ArgumentException($"{_modeFlag} and {flag} cannot go together.");
        Mode = mode;
        _modeFlag = flag;
    }

    private void SetMovie(string movie)
    {
        if (Movie != null)
            throw new ArgumentException($"Two movies given, '{Movie}' and '{movie}': give one.");
        Movie = movie;
    }

    private void SetSize(string value, string flag)
    {
        var size = ParseChoice<WorldSize>(value, flag, "small, medium or large");
        if (_sizeFlag != null && NewMovie.Size != size)
            throw new ArgumentException($"{_sizeFlag} and {flag} cannot go together: choose one world size.");
        NewMovie.Size = size;
        _sizeFlag = flag;
        _creationFlags.Add(flag);
    }

    private void SetEvil(string value, string flag)
    {
        var evil = ParseChoice<WorldEvil>(value, flag, "corruption or crimson");
        if (_evilFlag != null && NewMovie.Evil != evil)
            throw new ArgumentException($"{_evilFlag} and {flag} cannot go together: choose one world evil.");
        NewMovie.Evil = evil;
        _evilFlag = flag;
        _creationFlags.Add(flag);
    }

    private void SetDifficulty(string value, string flag)
    {
        // Terraria's menu calls the normal difficulty "Classic".
        var difficulty = value.Equals("classic", StringComparison.OrdinalIgnoreCase)
            ? WorldDifficulty.Normal
            : ParseChoice<WorldDifficulty>(value, flag, "journey, normal, expert or master");
        if (_difficultyFlag != null && NewMovie.Difficulty != difficulty)
            throw new ArgumentException($"{_difficultyFlag} and {flag} cannot go together: choose one difficulty.");
        NewMovie.Difficulty = difficulty;
        _difficultyFlag = flag;
        _creationFlags.Add(flag);
    }

    private static T ParseChoice<T>(string value, string flag, string choices)
        where T : struct
    {
        // Names only: Enum.TryParse also takes numbers.
        if (value.Length > 0 && char.IsLetter(value[0]) && Enum.TryParse<T>(value, ignoreCase: true, out var choice) && Enum.IsDefined(typeof(T), choice))
            return choice;
        throw new ArgumentException($"{flag} takes {choices}, got '{value}'.");
    }

    private static int ParseSeed(string value)
    {
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
            return seed;
        throw new ArgumentException($"--seed takes a whole number from 0 to {int.MaxValue}, got '{value}'.");
    }

    private static string Value(IReadOnlyList<string> args, ref int i)
    {
        if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{args[i]} needs a value.");
        return args[++i];
    }
}
