using System;
using System.IO;
using System.Runtime.CompilerServices;
using TerrariaTas.Runtime;

namespace TerrariaTas.Launcher;

internal static class Program
{
    [STAThread] // XNA and WinForms need a single-threaded apartment, like Terraria's own Main
    private static int Main(string[] args)
    {
        TasOptions? options;
        try
        {
            options = CommandLine.Parse(args);
            if (options == null)
            {
                Console.WriteLine(CommandLine.Usage);
                return 0;
            }

            GameHost.Prepare(options.TerrariaDir);
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine(CommandLine.Usage);
            return 2;
        }

        return Run(options);
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
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }
}
