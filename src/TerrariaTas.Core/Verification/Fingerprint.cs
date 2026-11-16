using System;
using System.Collections.Generic;
using System.Linq;

namespace TerrariaTas.Core.Verification;

/// <summary>Per-part hashes of the game state at one frame boundary, e.g. "npcs" or "rng:SpawnNPCs".</summary>
public sealed class Fingerprint
{
    public Fingerprint(IReadOnlyDictionary<string, ulong> parts)
    {
        Parts = parts;
    }

    public IReadOnlyDictionary<string, ulong> Parts { get; }

    /// <summary>Names of the parts that differ, sorted.</summary>
    public IReadOnlyList<string> DifferencesFrom(Fingerprint other) =>
        Parts.Keys.Union(other.Parts.Keys)
            .Where(name => !Parts.TryGetValue(name, out var mine) || !other.Parts.TryGetValue(name, out var theirs) || mine != theirs)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
}
