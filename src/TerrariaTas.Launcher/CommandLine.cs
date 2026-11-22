using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TerrariaTas.Core.Launch;
using TerrariaTas.Runtime;

namespace TerrariaTas.Launcher;

internal static class CommandLine
{
    public const string Usage = """
        Usage:
          TerrariaTas <movie>             Play a movie from its start, to watch: nothing can change it.
          TerrariaTas <movie> --edit      Work on a movie: paused at its start, F1 records, savestates, and the movie
                                          reloads when you save it in a text editor.
          TerrariaTas --new [name]        Create a character, a world and a movie starting with them, and record from
                                          its first frame. By default a softcore character in a small corruption
                                          world, normal difficulty; the movie is named after the time if no name.
            --crimson                     Crimson world (--corruption is the default; or --evil <corruption|crimson>).
            --expert, --master, --journey Difficulty (--normal is the default; or --difficulty <...>). Journey worlds
                                          get a journey character.
            --medium, --large             World size (--small is the default; or --size <...>).
            --seed <number>               World seed (default: random).
            --player <file>               Start with this character of the data folder's Players\ instead of a new one.
            --world <file>                Start in this world of the data folder's Worlds\ instead of a new one.
          TerrariaTas --list              List the movies with their character and world, and check those files.
          TerrariaTas <movie> --verify    Determinism self-test: play, replay from savestates, compare, exit with code 0
                                          when in sync. Writes verify-result.txt to the data folder.
          TerrariaTas                     Free play: the game as usual, with the TAS hotkeys once in a world.

        <movie> is a path, or the name of a movie in the movies folder ("my-run" for movies\my-run.tas). A movie
        starts with the character and world files it names, and only with the very files it was made with.

        Options:
          --movies <dir>      Movies folder (default: movies, in the current folder).
          --data <dir>        TAS data folder, with Players\ and Worlds\ (default: Documents\My Games\Terraria TAS).
          --terraria <dir>    Terraria install folder (default: TERRARIA_DIR, then the Steam default).
          --ignore-version    Run even if the installed Terraria is not the targeted version.
        """;

    /// <summary>The movies and data folders the command line names, or the default ones.</summary>
    public static MovieLibrary Library(LaunchArguments arguments) =>
        new(arguments.MoviesDir ?? "movies",
            arguments.DataDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Terraria TAS"));

    /// <exception cref="LaunchException">The movie cannot be found, or the name --new was given is taken.</exception>
    public static TasOptions Options(LaunchArguments arguments, MovieLibrary library, DateTime now)
    {
        var options = new TasOptions
        {
            TerrariaDir = arguments.TerrariaDir
                          ?? Environment.GetEnvironmentVariable("TERRARIA_DIR")
                          ?? @"C:\Program Files (x86)\Steam\steamapps\common\Terraria",
            DataDir = library.DataDir,
            Mode = arguments.Mode,
            NewMovie = arguments.NewMovie,
            IgnoreVersionMismatch = arguments.IgnoreVersion,
            MoviePath = arguments.Mode switch
            {
                LaunchMode.New => library.NewMoviePath(arguments.Movie, now),
                LaunchMode.Create => Path.GetFullPath(arguments.Movie!),
                _ => arguments.Movie == null ? null : library.Find(arguments.Movie),
            },
        };

        Directory.CreateDirectory(library.PlayersDir);
        Directory.CreateDirectory(library.WorldsDir);
        return options;
    }

    /// <summary>The command line of the creation launch behind --new: every choice spelled out.</summary>
    public static string CreationArguments(TasOptions options)
    {
        var created = options.NewMovie;
        var arguments = new List<string> { "--create", options.MoviePath!, "--data", options.DataDir, "--terraria", options.TerrariaDir };
        if (created.World == null)
        {
            arguments.AddRange(new[] { "--size", Name(created.Size), "--evil", Name(created.Evil), "--difficulty", Name(created.Difficulty) });
            if (created.Seed is { } seed)
                arguments.AddRange(new[] { "--seed", seed.ToString(CultureInfo.InvariantCulture) });
        }
        else
        {
            arguments.AddRange(new[] { "--world", created.World });
        }

        if (created.Player != null)
            arguments.AddRange(new[] { "--player", created.Player });
        if (options.IgnoreVersionMismatch)
            arguments.Add("--ignore-version");
        return CommandLineText.Join(arguments);

        static string Name(Enum value) => value.ToString().ToLowerInvariant();
    }
}
