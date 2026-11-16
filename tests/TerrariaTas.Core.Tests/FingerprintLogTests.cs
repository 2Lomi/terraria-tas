using System.Collections.Generic;
using TerrariaTas.Core.Verification;
using Xunit;

namespace TerrariaTas.Core.Tests;

public class FingerprintLogTests
{
    private static Fingerprint Frame(ulong players, ulong rng) =>
        new(new Dictionary<string, ulong> { ["players"] = players, ["rng:Main.rand"] = rng });

    [Fact]
    public void RoundTripsThroughText()
    {
        var log = new FingerprintLog("build-1", new[] { Frame(1, 2), Frame(ulong.MaxValue, 0) });

        var parsed = FingerprintLog.Parse(log.Write());

        Assert.Equal("build-1", parsed.Key);
        Assert.Null(parsed.FirstDifferenceFrom(log));
    }

    [Fact]
    public void ReportsTheFirstDifferingFrameAndParts()
    {
        var previous = new FingerprintLog("b", new[] { Frame(1, 1), Frame(2, 2), Frame(3, 3) });
        var current = new FingerprintLog("b", new[] { Frame(1, 1), Frame(2, 9), Frame(7, 9) });

        Assert.Equal("frame 1 differs in rng:Main.rand", current.FirstDifferenceFrom(previous));
    }

    [Fact]
    public void LogsWithAnotherKeyAreNotCompared()
    {
        var previous = new FingerprintLog("old", new[] { Frame(1, 1) });
        var current = new FingerprintLog("new", new[] { Frame(2, 2) });

        Assert.Null(current.FirstDifferenceFrom(previous));
    }
}
