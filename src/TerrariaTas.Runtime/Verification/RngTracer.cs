using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using HarmonyLib;
using Terraria;
using Terraria.Utilities;
using TerrariaTas.Runtime.GameAccess;

namespace TerrariaTas.Runtime.Verification;

/// <summary>
/// Records which game method draws each sample from the base RNG (<c>Main.rand</c> outside the
/// per-phase streams). Comparing two runs' traces shows exactly which call diverged first.
/// A stack walk per sample is slow, so this is only installed for --verify.
/// </summary>
internal sealed class RngTracer
{
    private static RngTracer? _active;

    private readonly Thread _mainThread = Thread.CurrentThread;
    private List<string> _calls = new();

    /// <summary>Off until the movie starts: boot and loading draw far too many samples to trace.</summary>
    public bool Enabled { get; set; }

    public void Install(Harmony harmony)
    {
        _active = this;
        harmony.CreateClassProcessor(typeof(Hooks)).Patch();
    }

    /// <summary>Calls recorded since the previous call to this method.</summary>
    public List<string> TakeCalls()
    {
        var calls = _calls;
        _calls = new List<string>();
        return calls;
    }

    private void Record(UnifiedRandom random)
    {
        // Other threads (world loading, audio) have their own [ThreadStatic] generators and no streams.
        if (!Enabled || Thread.CurrentThread != _mainThread)
            return;

        if (GameRng.IsShared(random))
            _calls.Add(FindCaller());
    }

    private static string FindCaller()
    {
        foreach (var frame in new StackTrace(2, false).GetFrames()!)
        {
            var type = frame.GetMethod()?.DeclaringType;
            if (type == null || type == typeof(UnifiedRandom) || type == typeof(Utils) || type.Namespace is "HarmonyLib" or null
                || type.Namespace.StartsWith("TerrariaTas"))
            {
                continue;
            }

            return type.FullName + "." + frame.GetMethod()!.Name;
        }

        return "?";
    }

    [HarmonyPatch]
    private static class Hooks
    {
        [HarmonyPatch(typeof(UnifiedRandom), "InternalSample"), HarmonyPrefix]
        private static void Sample(UnifiedRandom __instance) => _active?.Record(__instance);
    }
}
