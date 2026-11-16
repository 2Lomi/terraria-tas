using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TerrariaTas.GameDiff;

/// <summary>One Terraria member the TAS depends on.</summary>
/// <param name="Kind">patched, mirrored, calls, field or named (accessed by a string such as a private field name).</param>
internal sealed record SurfaceEntry(string Kind, string Fingerprint, string Member);

/// <summary>
/// Everything in Terraria the TAS assemblies depend on, with fingerprints, so that a new game version
/// can be checked member by member. "patched" and "mirrored" changes need a human to review the TAS code.
/// </summary>
internal static class Surface
{
    private static readonly string[] Priority = { "named", "field", "patched", "mirrored", "calls" };

    public static List<SurfaceEntry> Collect(IEnumerable<string> tasAssemblies, ModuleDefinition game)
    {
        var entries = new HashSet<SurfaceEntry>();
        foreach (var path in tasAssemblies)
        {
            using var module = Modules.Read(path);
            foreach (var method in module.GetTypes().SelectMany(t => t.Methods))
            {
                foreach (var target in HarmonyTargets(method, game))
                    entries.Add(new SurfaceEntry("patched", IlFingerprint.Of(target), IlFingerprint.Name(target)));
                if (method.HasBody)
                    CollectNamedMembers(method.Body, game, entries);
            }

            foreach (var type in module.GetTypes())
            {
                foreach (var mirrored in MirroredMethods(type, game))
                    entries.Add(new SurfaceEntry("mirrored", IlFingerprint.Of(mirrored), IlFingerprint.Name(mirrored)));
            }

            foreach (var reference in module.GetMemberReferences().Where(IsGameMember))
                entries.Add(Describe(reference, game));
        }

        return entries.OrderBy(e => Array.IndexOf(Priority, e.Kind)).ThenBy(e => e.Member, StringComparer.Ordinal).ToList();
    }

    public static void Write(string path, string gameVersion, IEnumerable<SurfaceEntry> entries)
    {
        var lines = new List<string>
        {
            $"# Terraria members used by Terraria TAS, fingerprinted on Terraria {gameVersion}.",
            "# Regenerate after porting: dotnet run --project tools/TerrariaTas.GameDiff -- surface (see docs/porting.md)",
        };
        lines.AddRange(entries.Select(e => $"{e.Kind,-8} {e.Fingerprint,-16} {e.Member}"));
        File.WriteAllLines(path, lines);
    }

    public static List<SurfaceEntry> Read(string path) =>
        File.ReadAllLines(path)
            .Where(line => line.Length > 0 && line[0] != '#')
            .Select(line => line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries))
            .Select(parts => new SurfaceEntry(parts[0], parts[1], parts[2].Trim()))
            .ToList();

    /// <summary>Compares a recorded surface with another game build. Returns the number of problems.</summary>
    public static int Check(List<SurfaceEntry> recorded, ModuleDefinition game, TextWriter report)
    {
        var methods = game.GetTypes().SelectMany(t => t.Methods).GroupBy(IlFingerprint.Name).ToDictionary(g => g.Key, g => g.First());
        var problems = 0;
        var unchanged = 0;
        foreach (var entry in recorded)
        {
            string? status;
            if (entry.Kind is "named" or "field")
            {
                status = MemberExists(game, entry.Member) ? null : "MISSING";
            }
            else
            {
                status = !methods.TryGetValue(entry.Member, out var method) ? "MISSING"
                    : IlFingerprint.Of(method) != entry.Fingerprint ? "CHANGED"
                    : null;
            }

            if (status == null)
            {
                unchanged++;
                continue;
            }

            // A changed method we only call rarely breaks anything; everything else needs review.
            if (entry.Kind != "calls" || status == "MISSING")
                problems++;
            report.WriteLine($"{status,-8} {entry.Kind,-8} {entry.Member}");
        }

        report.WriteLine($"{unchanged} of {recorded.Count} members unchanged, {problems} need review.");
        return problems;
    }

    // Array accessors such as Tile[,]::Get are provided by the runtime, not by the game.
    private static bool IsGameMember(MemberReference reference) =>
        reference.DeclaringType is { IsArray: false, Scope.Name: "Terraria" } && reference is MethodReference or FieldReference;

    private static SurfaceEntry Describe(MemberReference reference, ModuleDefinition game)
    {
        if (reference is FieldReference field)
            return new SurfaceEntry("field", "-", $"{IlFingerprint.Name(field.DeclaringType)}::{field.Name}");

        var method = FindMethod(game, IlFingerprint.Name(reference));
        return new SurfaceEntry("calls", method == null ? "missing" : IlFingerprint.Of(method), IlFingerprint.Name(reference));
    }

    private static MethodDefinition? FindMethod(ModuleDefinition game, string fullName) =>
        game.GetTypes().SelectMany(t => t.Methods).FirstOrDefault(m => IlFingerprint.Name(m) == fullName);

    private static bool MemberExists(ModuleDefinition game, string member)
    {
        var separator = member.IndexOf("::", StringComparison.Ordinal);
        var type = game.GetType(member[..separator]);
        var name = member[(separator + 2)..];
        return type != null && (type.Fields.Any(f => f.Name == name) || type.Properties.Any(p => p.Name == name) || type.Methods.Any(m => m.Name == name));
    }

    /// <summary>[HarmonyPatch(typeof(X), "Method", ...)] attributes that point into Terraria.</summary>
    private static IEnumerable<MethodDefinition> HarmonyTargets(MethodDefinition patch, ModuleDefinition game)
    {
        foreach (var attribute in patch.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"))
        {
            var args = attribute.ConstructorArguments;
            if (args.Count == 0 || args[0].Value is not TypeReference { Scope.Name: "Terraria" } targetType)
                continue;

            var type = game.GetType(IlFingerprint.Name(targetType));
            if (type == null)
                continue;

            var name = args.Count > 1 && args[1].Value is string s ? s : null;
            var isConstructor = args.Count > 1 && args[1].Value is int methodType && methodType == 3;
            var parameterTypes = args.Skip(1).Select(a => a.Value).OfType<CustomAttributeArgument[]>().FirstOrDefault()
                                 ?? args.Skip(2).Where(a => a.Value is TypeReference).Select(a => new CustomAttributeArgument(a.Type, a.Value)).ToArray();

            foreach (var method in type.Methods.Where(m => isConstructor ? m.IsConstructor && !m.IsStatic : m.Name == name))
            {
                var wanted = parameterTypes.Select(p => IlFingerprint.Name((TypeReference)p.Value)).ToList();
                if (wanted.Count == 0 && !isConstructor || method.Parameters.Select(p => IlFingerprint.Name(p.ParameterType)).SequenceEqual(wanted))
                    yield return method;
            }
        }
    }

    private static IEnumerable<MethodDefinition> MirroredMethods(TypeDefinition type, ModuleDefinition game)
    {
        var attributes = type.CustomAttributes.Concat(type.Methods.SelectMany(m => m.CustomAttributes))
            .Where(a => a.AttributeType.Name == "MirrorsGameMethodAttribute");
        foreach (var attribute in attributes)
        {
            var gameType = game.GetType((string)attribute.ConstructorArguments[0].Value);
            var name = (string)attribute.ConstructorArguments[1].Value;
            foreach (var method in gameType?.Methods.Where(m => m.Name == name) ?? Enumerable.Empty<MethodDefinition>())
                yield return method;
        }
    }

    /// <summary>
    /// Members reached by name, e.g. AccessTools.Field(typeof(Main), "_rngs"): a type token followed by string
    /// literals that name members of that type. These are invisible to the compiler, so they are the most fragile.
    /// </summary>
    private static void CollectNamedMembers(MethodBody body, ModuleDefinition game, HashSet<SurfaceEntry> entries)
    {
        TypeDefinition? lastType = null;
        foreach (var instruction in body.Instructions)
        {
            if (instruction.OpCode == OpCodes.Ldtoken && instruction.Operand is TypeReference { Scope.Name: "Terraria" } token)
                lastType = game.GetType(IlFingerprint.Name(token));
            else if (instruction.OpCode == OpCodes.Ldstr && lastType != null && instruction.Operand is string name && Names(lastType).Contains(name))
                entries.Add(new SurfaceEntry("named", "-", $"{lastType.FullName}::{name}"));
        }
    }

    private static HashSet<string> Names(TypeDefinition type) =>
        type.Fields.Select(f => f.Name).Concat(type.Properties.Select(p => p.Name)).Concat(type.Methods.Select(m => m.Name)).ToHashSet();
}
