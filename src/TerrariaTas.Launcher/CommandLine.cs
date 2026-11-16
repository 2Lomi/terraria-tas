using System;
using System.IO;
using TerrariaTas.Runtime;

namespace TerrariaTas.Launcher;

internal static class CommandLine
{
    public const string Usage = """
        Usage: TerrariaTas [movie.tas] [options]

          movie.tas           Movie to load. Without one, the game runs in free play with TAS controls.
          --terraria <dir>    Terraria install folder (default: TERRARIA_DIR, then the Steam default).
          --data <dir>        TAS data folder holding Players/ and Worlds/
                              (default: Documents\My Games\Terraria TAS).
          --play              Start playing as soon as the movie start is reached.
          --verify            Determinism self-test: play, replay from savestates, compare, exit
                              with code 0 when in sync. Writes verify-result.txt to the data folder.
          --ignore-version    Run even if the installed Terraria is not the targeted version.
        """;

    /// <returns>The options, or null when help was requested.</returns>
    public static TasOptions? Parse(string[] args)
    {
        var options = new TasOptions
        {
            TerrariaDir = Environment.GetEnvironmentVariable("TERRARIA_DIR")
                          ?? @"C:\Program Files (x86)\Steam\steamapps\common\Terraria",
            DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Terraria TAS"),
        };

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    return null;
                case "--terraria":
                    options.TerrariaDir = Value(args, ref i);
                    break;
                case "--data":
                    options.DataDir = Value(args, ref i);
                    break;
                case "--play":
                    options.AutoPlay = true;
                    break;
                case "--verify":
                    options.Verify = true;
                    break;
                case "--ignore-version":
                    options.IgnoreVersionMismatch = true;
                    break;
                default:
                    if (args[i].StartsWith("-", StringComparison.Ordinal) || options.MoviePath != null)
                        throw new ArgumentException($"Unexpected argument '{args[i]}'.");
                    options.MoviePath = Path.GetFullPath(args[i]);
                    break;
            }
        }

        options.DataDir = Path.GetFullPath(options.DataDir);
        Directory.CreateDirectory(Path.Combine(options.DataDir, "Players"));
        Directory.CreateDirectory(Path.Combine(options.DataDir, "Worlds"));
        return options;
    }

    private static string Value(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"{args[i]} needs a value.");
        return args[++i];
    }
}
