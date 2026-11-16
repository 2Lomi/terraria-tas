using System;

namespace TerrariaTas.Core;

/// <summary>
/// Marks code that re-implements the behaviour of a Terraria method (for example an RNG model).
/// The GameDiff tool fingerprints every mirrored method, so a game update that changes the
/// original is reported and the mirror can be reviewed.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class MirrorsGameMethodAttribute : Attribute
{
    public MirrorsGameMethodAttribute(string typeName, string methodName)
    {
        TypeName = typeName;
        MethodName = methodName;
    }

    public string TypeName { get; }

    public string MethodName { get; }
}
