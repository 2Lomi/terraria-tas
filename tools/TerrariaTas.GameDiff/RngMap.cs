using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TerrariaTas.GameDiff;

/// <summary>
/// Lists every game method that draws random numbers, with the number of call sites. Diffing the output
/// of two game versions shows where RNG behaviour changed, which is what RNG predictors depend on.
/// </summary>
internal static class RngMap
{
    private static readonly HashSet<string> RandomMethods = new()
    {
        "Next", "NextDouble", "NextFloat", "NextBool", "NextFloatDirection", "NextVector2Circular",
        "NextVector2CircularEdge", "NextVector2Square", "NextVector2Unit", "NextFromList", "NextFromCollection", "Peek",
    };

    public static void Write(ModuleDefinition game, TextWriter output)
    {
        output.WriteLine("# Methods that draw random numbers: call sites per method. 'swap' marks Main.SwapRandom streams.");
        foreach (var method in game.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody).OrderBy(m => m.DeclaringType.FullName).ThenBy(m => m.Name))
        {
            var calls = method.Body.Instructions.Count(IsRandomCall);
            if (calls == 0)
                continue;

            var streams = method.Body.Instructions
                .Where(i => i.Operand is MethodReference { Name: "SwapRandom" } && i.Previous?.Operand is string)
                .Select(i => (string)i.Previous.Operand);
            var swap = string.Join(",", streams);
            output.WriteLine($"{calls,4}  {method.DeclaringType.FullName}::{method.Name}{(swap.Length > 0 ? "  swap " + swap : "")}");
        }
    }

    private static bool IsRandomCall(Instruction instruction) =>
        instruction.OpCode.FlowControl == FlowControl.Call
        && instruction.Operand is MethodReference method
        && RandomMethods.Contains(method.Name)
        && (method.DeclaringType.FullName == "Terraria.Utilities.UnifiedRandom"
            || method.Parameters.FirstOrDefault()?.ParameterType.FullName == "Terraria.Utilities.UnifiedRandom");
}
