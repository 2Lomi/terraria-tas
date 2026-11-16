using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TerrariaTas.Core.Verification;

/// <summary>
/// Per-frame fingerprints of a whole movie, saved by one launch and compared by the next:
/// a movie must replay identically every time the tool starts, not only within one session.
/// </summary>
public sealed class FingerprintLog
{
    public FingerprintLog(string key, IReadOnlyList<Fingerprint> frames)
    {
        Key = key;
        Frames = frames;
    }

    /// <summary>Identifies the TAS build and the movie: logs with another key are not comparable.</summary>
    public string Key { get; }

    public IReadOnlyList<Fingerprint> Frames { get; }

    public static FingerprintLog Parse(string text)
    {
        var lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var frames = lines.Skip(1).Select(line => new Fingerprint(line.Trim().Split(' ')
            .Select(part => part.Split('='))
            .ToDictionary(pair => pair[0], pair => ulong.Parse(pair[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture))));
        return new FingerprintLog(lines[0].Trim(), frames.ToList());
    }

    public string Write()
    {
        var text = new StringBuilder(Key).Append('\n');
        foreach (var frame in Frames)
        {
            text.Append(string.Join(" ", frame.Parts.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value:x16}")));
            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Describes the first frame where two runs of the same movie differ, or null if they match.</summary>
    public string? FirstDifferenceFrom(FingerprintLog previous)
    {
        if (previous.Key != Key)
            return null;

        var frames = Math.Min(Frames.Count, previous.Frames.Count);
        for (var frame = 0; frame < frames; frame++)
        {
            var differences = Frames[frame].DifferencesFrom(previous.Frames[frame]);
            if (differences.Count > 0)
                return $"frame {frame} differs in {string.Join(", ", differences)}";
        }

        return Frames.Count == previous.Frames.Count ? null : "the runs have different lengths";
    }
}
