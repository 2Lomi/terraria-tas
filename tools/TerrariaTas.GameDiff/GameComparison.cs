using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace TerrariaTas.GameDiff;

/// <summary>Every method added, removed or changed between two game builds (optionally under a namespace).</summary>
internal static class GameComparison
{
    public static void Write(ModuleDefinition before, ModuleDefinition after, string? namespacePrefix, TextWriter output)
    {
        var old = Fingerprints(before, namespacePrefix);
        var current = Fingerprints(after, namespacePrefix);

        var removed = old.Keys.Except(current.Keys).OrderBy(k => k).ToList();
        var added = current.Keys.Except(old.Keys).OrderBy(k => k).ToList();
        var changed = old.Keys.Intersect(current.Keys).Where(k => old[k] != current[k]).OrderBy(k => k).ToList();

        foreach (var name in removed)
            output.WriteLine("removed  " + name);
        foreach (var name in added)
            output.WriteLine("added    " + name);
        foreach (var name in changed)
            output.WriteLine("changed  " + name);
        output.WriteLine($"{changed.Count} changed, {added.Count} added, {removed.Count} removed, {old.Count - removed.Count - changed.Count} unchanged.");
    }

    private static Dictionary<string, string> Fingerprints(ModuleDefinition module, string? namespacePrefix) =>
        module.GetTypes()
            .Where(t => namespacePrefix == null || t.FullName.StartsWith(namespacePrefix))
            .SelectMany(t => t.Methods)
            .GroupBy(IlFingerprint.Name)
            .ToDictionary(g => g.Key, g => IlFingerprint.Of(g.First()));
}
