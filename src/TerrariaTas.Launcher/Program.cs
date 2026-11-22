using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using TerrariaTas.Core.Launch;
using TerrariaTas.Runtime;

namespace TerrariaTas.Launcher;

internal static class Program
{
    [STAThread] // XNA and WinForms need a single-threaded apartment, like Terraria's own Main
    private static int Main(string[] args)
    {
        LaunchArguments arguments;
        try
        {
            arguments = LaunchArguments.Parse(args);
        }
        catch (ArgumentException e)
        {
            WriteError(e.Message);
            Console.Error.WriteLine(CommandLine.Usage);
            return 2;
        }

        if (arguments.Help)
        {
            Console.WriteLine(CommandLine.Usage);
            return 0;
        }

        TasOptions options;
        try
        {
            var library = CommandLine.Library(arguments);
            if (arguments.Mode == LaunchMode.List)
            {
                Console.Write(library.Describe(library.List()));
                return 0;
            }

            options = CommandLine.Options(arguments, library, DateTime.Now);
            GameHost.Prepare(options.TerrariaDir);
            if (options.Mode == LaunchMode.New)
            {
                var created = CreateMovie(options);
                if (created != 0)
                    return created;
            }
        }
        catch (Exception e) when (e is LaunchException or IOException or UnauthorizedAccessException)
        {
            WriteError(e.Message);
            return 2;
        }

        return Run(options);
    }

    /// <summary>
    /// --new: a game launch of its own creates the character, the world and the movie, and exits; this launch then records.
    /// World generation leaves plenty behind in the game's memory, and none of it may reach the movie.
    /// </summary>
    /// <returns>The exit code of the creation launch: 0 when the movie was created.</returns>
    private static int CreateMovie(TasOptions options)
    {
        var self = Process.GetCurrentProcess().MainModule!.FileName;
        var start = new ProcessStartInfo(self, CommandLine.CreationArguments(options)) { UseShellExecute = false };
        using var creation = Process.Start(start)!;
        creation.WaitForExit();
        var exitCode = creation.ExitCode == 0 && !File.Exists(options.MoviePath) ? 1 : creation.ExitCode;
        if (exitCode != 0)
            WriteError($"The movie was not created (exit code {exitCode}, the reason is above).");
        else
            Console.WriteLine($"Created {options.MoviePath}: starting the game to record it.");
        return exitCode;
    }

    /// <summary>In red in a console, so the reason a run stops stands out from the build and game logs around it.</summary>
    private static void WriteError(string message)
    {
        var console = !Console.IsErrorRedirected;
        if (console)
            Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(message);
        if (console)
            Console.ResetColor();
    }

    // Kept separate so the JIT only loads Terraria-dependent code after GameHost.Prepare installed the resolver.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(TasOptions options)
    {
        try
        {
            TasRuntime.Run(options);
            return Environment.ExitCode;
        }
        catch (TasSetupException e)
        {
            WriteError(e.Message);
            return 2;
        }
        catch (Exception e)
        {
            WriteError(e.ToString());
            return 1;
        }
    }
}
