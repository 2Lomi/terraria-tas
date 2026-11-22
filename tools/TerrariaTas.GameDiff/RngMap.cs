using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TerrariaTas.GameDiff;

/// <summary>
/// Lists every game method that draws random numbers, with the number of call sites and a fingerprint of its code.
/// Comparing the list recorded for one game version with another build (<see cref="Check"/>) shows where RNG
/// behaviour may have changed, which is what RNG predictors, forecasts and manipulation routes depend on.
/// </summary>
internal static class RngMap
{
    private const string SwapMarker = "  swap ";

    private static readonly HashSet<string> RandomMethods = new()
    {
        "Next", "NextDouble", "NextFloat", "NextBool", "NextFloatDirection", "NextVector2Circular",
        "NextVector2CircularEdge", "NextVector2Square", "NextVector2Unit", "NextFromList", "NextFromCollection", "Peek",
    };

    public static void Write(ModuleDefinition game, string version, TextWriter output)
    {
        output.WriteLine($"# Methods of Terraria {version} that draw random numbers: call sites, code fingerprint, method.");
        output.WriteLine("# 'swap' marks the Main.SwapRandom streams a method switches to. Regenerate after porting (docs/porting.md).");
        foreach (var entry in Collect(game))
            output.WriteLine($"{entry.Calls,4} {entry.Fingerprint} {entry.Method}{(entry.Streams.Length > 0 ? SwapMarker + entry.Streams : "")}");
    }

    public static List<RngEntry> Read(string path) =>
        File.ReadAllLines(path)
            .Where(line => line.Length > 0 && line[0] != '#')
            .Select(line => line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries))
            .Select(parts =>
            {
                var swap = parts[2].IndexOf(SwapMarker, StringComparison.Ordinal);
                return swap < 0
                    ? new RngEntry(int.Parse(parts[0]), parts[1], parts[2], "")
                    : new RngEntry(int.Parse(parts[0]), parts[1], parts[2][..swap], parts[2][(swap + SwapMarker.Length)..]);
            })
            .ToList();

    /// <summary>
    /// Compares a recorded map with another game build. Methods are matched by name; compiler-generated ones (lambdas)
    /// can share a name, and are then compared as a group. Returns the number of names that need review.
    /// </summary>
    public static int Check(List<RngEntry> recorded, ModuleDefinition game, TextWriter report)
    {
        var current = Collect(game).GroupBy(entry => entry.Method).ToDictionary(group => group.Key, group => group.ToList());
        var differences = 0;
        var unchanged = 0;
        foreach (var group in recorded.GroupBy(entry => entry.Method))
        {
            var before = group.ToList();
            if (!current.Remove(group.Key, out var now))
            {
                report.WriteLine($"GONE     {group.Key} (drew random numbers at {before.Sum(entry => entry.Calls)} call sites)");
                differences++;
                continue;
            }

            var left = before.Select(entry => (entry.Fingerprint, entry.Streams)).ToList();
            var same = now.Count(entry => left.Remove((entry.Fingerprint, entry.Streams)));
            if (same == before.Count && same == now.Count)
            {
                unchanged += before.Count;
                continue;
            }

            int callsBefore = before.Sum(entry => entry.Calls), callsNow = now.Sum(entry => entry.Calls);
            var calls = callsBefore == callsNow ? $"{callsNow} call sites" : $"call sites {callsBefore} -> {callsNow}";
            var shared = before.Count > 1 || now.Count > 1 ? $", {before.Count} -> {now.Count} compiler-generated methods of this name" : "";
            report.WriteLine($"CHANGED  {group.Key} ({calls}{shared})");
            differences++;
        }

        foreach (var group in current)
            report.WriteLine($"NEW      {group.Key} ({group.Value.Sum(entry => entry.Calls)} call sites)");
        differences += current.Count;

        report.WriteLine($"{unchanged} of {recorded.Count} methods that draw random numbers unchanged, {differences} to review.");
        return differences;
    }

    private static IEnumerable<RngEntry> Collect(ModuleDefinition game)
    {
        var entries = new List<RngEntry>();
        foreach (var method in game.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody))
        {
            var calls = method.Body.Instructions.Count(IsRandomCall);
            if (calls == 0)
                continue;

            var streams = method.Body.Instructions
                .Where(i => i.Operand is MethodReference { Name: "SwapRandom" } && i.Previous?.Operand is string)
                .Select(i => (string)i.Previous.Operand);
            entries.Add(new RngEntry(calls, IlFingerprint.Of(method), IlFingerprint.Name(method), string.Join(",", streams)));
        }

        // By type and method name, as a reader looks them up; fingerprints keep same-named lambdas in a stable order.
        return entries.OrderBy(entry => entry.Method[(entry.Method.IndexOf(' ') + 1)..], StringComparer.Ordinal).ThenBy(entry => entry.Fingerprint, StringComparer.Ordinal);
    }

    private static bool IsRandomCall(Instruction instruction) =>
        instruction.OpCode.FlowControl == FlowControl.Call
        && instruction.Operand is MethodReference method
        && RandomMethods.Contains(method.Name)
        && (method.DeclaringType.FullName == "Terraria.Utilities.UnifiedRandom"
            || method.Parameters.FirstOrDefault()?.ParameterType.FullName == "Terraria.Utilities.UnifiedRandom");
}

internal sealed record RngEntry(int Calls, string Fingerprint, string Method, string Streams);
