using HarmonyLib;
using TerrariaTas.Runtime.GameAccess;
using TerrariaTas.Runtime.Session;

namespace TerrariaTas.Runtime;

/// <summary>Entry point called by the launcher once Terraria's assemblies can be resolved.</summary>
public static class TasRuntime
{
    public static void Run(TasOptions options)
    {
        GameVersion.EnsureSupported(options.IgnoreVersionMismatch);

        // Main's static fields derive paths from Program.SavePath, and patching can run Main's
        // static initializer early, so the save directory must be known before any patch.
        Terraria.Program.SavePath = options.DataDir;

        TasSession.Install(options, new Harmony("terraria-tas"));
        Terraria.Program.LaunchGame(new[] { "-savedirectory", options.DataDir });
    }
}
