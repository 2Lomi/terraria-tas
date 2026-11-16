using System.Collections.Generic;

namespace TerrariaTas.Core.Verification;

/// <summary>What the determinism self-test needs from the game.</summary>
public interface ISyncTarget
{
    /// <summary>Per-part hashes of the state at the current frame boundary. Called every frame.</summary>
    Fingerprint Fingerprint();

    /// <summary>A hash of everything that is too slow to fingerprint every frame (e.g. all tiles).</summary>
    ulong FullHash();

    /// <summary>Who drew from the traced RNG since the previous call, in order.</summary>
    IReadOnlyList<string> TakeRngCalls();

    object SaveState(int frame);

    void LoadState(object state);
}
