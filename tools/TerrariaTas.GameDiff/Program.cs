using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using TerrariaTas.GameDiff;

const string usage = """
    TerrariaTas.GameDiff: pinpoint what a Terraria update changed for the TAS.

      surface <Terraria.exe> <surface-file> <tas-assembly>...
          Record every Terraria member the TAS assemblies use, with IL fingerprints.
      check <Terraria.exe> <surface-file>
          Compare a recorded surface with another Terraria build. Exit code 1 when something needs review.
      rng <Terraria.exe> [output-file]
          List the methods that draw random numbers.
      diff <old Terraria.exe> <new Terraria.exe> [namespace-prefix]
          List every method added, removed or changed between two builds.
    """;

if (args.Length == 0)
{
    Console.WriteLine(usage);
    return 0;
}

try
{
    switch (args[0])
    {
        case "surface" when args.Length >= 4:
            using (var game = Modules.Read(args[1]))
            {
                var entries = Surface.Collect(args.Skip(3), game);
                Surface.Write(args[2], game.Assembly.Name.Version.ToString(), entries);
                Console.WriteLine($"{entries.Count} members recorded in {args[2]}.");
            }

            return 0;

        case "check" when args.Length == 3:
            using (var game = Modules.Read(args[1]))
            {
                Console.WriteLine($"Checking {args[2]} against Terraria {game.Assembly.Name.Version}:");
                return Surface.Check(Surface.Read(args[2]), game, Console.Out) == 0 ? 0 : 1;
            }

        case "rng" when args.Length is 2 or 3:
            using (var game = Modules.Read(args[1]))
            using (var output = args.Length == 3 ? new StreamWriter(args[2]) : Console.Out)
                RngMap.Write(game, output);
            return 0;

        case "diff" when args.Length is 3 or 4:
            using (var before = Modules.Read(args[1]))
            using (var after = Modules.Read(args[2]))
                GameComparison.Write(before, after, args.Length == 4 ? args[3] : null, Console.Out);
            return 0;

        default:
            Console.Error.WriteLine(usage);
            return 2;
    }
}
catch (IOException e)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}

