using System;
using System.Linq;
using System.Reflection;
using Terraria;

namespace TerrariaTas.Runtime.GameAccess;

/// <summary>Compares the running Terraria with the build this release targets (see build/Version.props).</summary>
internal static class GameVersion
{
    public static string Target { get; } = typeof(GameVersion).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .First(a => a.Key == "TargetTerrariaVersion").Value!;

    /// <summary>Read from the loaded assembly; <c>Main.curRelease</c> is a constant and would be compiled in.</summary>
    public static string Running { get; } = typeof(Main).Assembly.GetName().Version.ToString();

    public static void EnsureSupported(bool ignoreMismatch)
    {
        if (Running == Target)
            return;

        var message = $"This TAS build targets Terraria {Target}, but the installed game is {Running}. "
                      + "Movies will desync and hooks may break. See docs/porting.md.";
        if (!ignoreMismatch)
            throw new InvalidOperationException(message + " Use --ignore-version to run anyway.");
        Log.Warn(message);
    }
}
