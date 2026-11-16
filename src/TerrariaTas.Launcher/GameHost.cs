using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TerrariaTas.Launcher;

/// <summary>
/// Makes the user's Terraria runnable inside this process without touching its install folder:
/// assembly resolution (including the DLLs Terraria embeds as resources), native DLL search path,
/// working directory and XNA's content root.
/// </summary>
internal static class GameHost
{
    private const string XnaAssembly = "Microsoft.Xna.Framework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553";

    // One instance per assembly name: loading an embedded DLL twice from bytes creates two distinct
    // copies of its types, and calls between them fail with MissingMethodException.
    private static readonly Dictionary<string, Assembly?> Resolved = new(StringComparer.OrdinalIgnoreCase);

    private static string _gameDir = "";
    private static Assembly? _terraria;

    public static void Prepare(string gameDir)
    {
        _gameDir = Path.GetFullPath(gameDir);
        if (!File.Exists(Path.Combine(_gameDir, "Terraria.exe")))
            throw new FileNotFoundException($"Terraria.exe not found in '{_gameDir}'. Pass the install folder with --terraria.");

        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        Environment.CurrentDirectory = _gameDir;
        SetDllDirectory(_gameDir);
        SetXnaContentRoot(_gameDir);
    }

    private static Assembly? Resolve(object sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        lock (Resolved)
        {
            if (!Resolved.TryGetValue(name, out var assembly))
                Resolved[name] = assembly = Load(name);
            return assembly;
        }
    }

    private static Assembly? Load(string name)
    {
        if (name == "Terraria")
            return Terraria();

        var file = Path.Combine(_gameDir, name + ".dll");
        return File.Exists(file) ? Assembly.LoadFrom(file) : LoadEmbedded(name);
    }

    private static Assembly Terraria() => _terraria ??= Assembly.LoadFrom(Path.Combine(_gameDir, "Terraria.exe"));

    /// <summary>Terraria ships ReLogic, Steamworks.NET, Newtonsoft.Json... as resources (see Terraria.WindowsLaunch.Main).</summary>
    private static Assembly? LoadEmbedded(string name)
    {
        var resource = Array.Find(Terraria().GetManifestResourceNames(), r => r.EndsWith(name + ".dll", StringComparison.Ordinal));
        if (resource == null)
            return null;

        using var stream = Terraria().GetManifestResourceStream(resource)!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return Assembly.Load(bytes.ToArray());
    }

    /// <summary>XNA looks for Content/ next to the entry assembly and caches that folder in TitleLocation._titleLocation.</summary>
    private static void SetXnaContentRoot(string gameDir)
    {
        var titleLocation = Assembly.Load(XnaAssembly).GetType("Microsoft.Xna.Framework.TitleLocation", throwOnError: true);
        titleLocation.GetField("_titleLocation", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, gameDir);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string path);
}
