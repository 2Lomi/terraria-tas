using System.Collections.Generic;
using TerrariaTas.Core.Verification;
using TerrariaTas.Runtime.SaveStates;
using TerrariaTas.Runtime.Verification;

namespace TerrariaTas.Runtime.Session;

/// <summary>Connects the self-test to the running game: fingerprints, RNG trace and savestates.</summary>
internal sealed class GameSyncTarget : ISyncTarget
{
    private readonly SaveStateStore _saveStates;
    private readonly RngTracer _tracer;

    public GameSyncTarget(SaveStateStore saveStates, RngTracer tracer)
    {
        _saveStates = saveStates;
        _tracer = tracer;
    }

    public Fingerprint Fingerprint() => StateHasher.Capture();

    public ulong FullHash() => StateHasher.HashTiles();

    public IReadOnlyList<string> TakeRngCalls() => _tracer.TakeCalls();

    public object SaveState(int frame) => _saveStates.Capture(frame);

    public void LoadState(object state) => _saveStates.Restore((SaveState)state);
}
