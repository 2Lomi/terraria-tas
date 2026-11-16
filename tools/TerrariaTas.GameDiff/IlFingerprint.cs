using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TerrariaTas.GameDiff;

/// <summary>
/// Hash of a method body that survives recompilation: operands are written as names instead of
/// metadata tokens, branch targets as instruction indices, and compiler-generated names lose their
/// numbering. Equal fingerprints mean the method behaves the same.
/// </summary>
internal static class IlFingerprint
{
    private static readonly Regex CompilerNumbering = new(@"(<[^>]*>[a-zA-Z]+__)[0-9_]+|(<>c__DisplayClass)[0-9_]+|(<>9__)[0-9_]+", RegexOptions.Compiled);

    public static string Of(MethodDefinition method)
    {
        if (!method.HasBody)
            return "no-body";

        var body = method.Body;
        var instructions = body.Instructions;
        var text = new StringBuilder();
        foreach (var variable in body.Variables)
            text.Append("local ").Append(Name(variable.VariableType)).Append('\n');
        foreach (var handler in body.ExceptionHandlers)
        {
            text.Append("try ").Append(handler.HandlerType).Append(' ').Append(Name(handler.CatchType))
                .Append(' ').Append(instructions.IndexOf(handler.TryStart)).Append(' ').Append(instructions.IndexOf(handler.HandlerStart)).Append('\n');
        }

        foreach (var instruction in instructions)
            text.Append(instruction.OpCode.Name).Append(' ').Append(Operand(instruction.Operand, instructions)).Append('\n');

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    /// <summary>Full name without compiler-generated numbering, usable to match members across builds.</summary>
    public static string Name(MemberReference? member) =>
        member == null ? "" : CompilerNumbering.Replace(member.FullName, m => m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value);

    private static string Operand(object? operand, Mono.Collections.Generic.Collection<Instruction> instructions) => operand switch
    {
        null => "",
        Instruction target => "@" + instructions.IndexOf(target),
        Instruction[] targets => string.Join(",", targets.Select(t => "@" + instructions.IndexOf(t))),
        MemberReference member => Name(member),
        VariableDefinition variable => "V" + variable.Index,
        ParameterDefinition parameter => "A" + parameter.Index,
        string literal => "\"" + literal + "\"",
        CallSite site => site.FullName,
        _ => Convert.ToString(operand, CultureInfo.InvariantCulture) ?? "",
    };
}
