using System.Collections.Generic;
using System.Linq;
using TerrariaTas.Core.Verification;
using TerrariaTas.Runtime.SaveStates;
using TerrariaTas.Runtime.Verification;

namespace TerrariaTas.Runtime.Session;

/// <summary>
/// Connects the self-test to the running game: fingerprints, RNG trace and savestates. Every load is also
/// audited: game fields that do not read the same right after loading as when the state was saved are
/// state the savestates miss.
/// </summary>
internal sealed class GameSyncTarget : ISyncTarget
{
    private readonly SaveStateStore _saveStates;
    private readonly RngTracer _tracer;
    private readonly Dictionary<SaveState, (int Frame, SortedDictionary<string, string> Census)> _censusAtSave = new();
    private readonly List<string> _restoreGaps = new();

    public GameSyncTarget(SaveStateStore saveStates, RngTracer tracer)
    {
        _saveStates = saveStates;
        _tracer = tracer;
    }

    /// <summary>Report lines: for each load, the fields that did not come back.</summary>
    public IReadOnlyList<string> RestoreGaps => _restoreGaps;

    public Fingerprint Fingerprint() => StateHasher.Capture();

    public ulong FullHash() => StateHasher.HashTiles();

    public IReadOnlyList<string> TakeRngCalls() => _tracer.TakeCalls();

    public object SaveState(int frame)
    {
        var state = _saveStates.Capture(frame);
        Remember(state, frame, StaticCensus.Take());
        return state;
    }

    /// <summary>Audits loads of a state captured elsewhere (the movie start), with the census taken right after capturing it.</summary>
    public void Remember(SaveState state, int frame, SortedDictionary<string, string> census) => _censusAtSave[state] = (frame, census);

    public void LoadState(object state)
    {
        var saveState = (SaveState)state;
        _saveStates.Restore(saveState);
        if (!_censusAtSave.TryGetValue(saveState, out var saved))
            return;

        var gaps = StaticCensus.Differences(saved.Census, StaticCensus.Take());
        _restoreGaps.Add($"Loading the state of frame {saved.Frame}: {(gaps.Count == 0 ? "every audited field came back" : $"{gaps.Count} fields did not come back")}");
        _restoreGaps.AddRange(gaps.Select(gap => "  " + gap));
    }
}
