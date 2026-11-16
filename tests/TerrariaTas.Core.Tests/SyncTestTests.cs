using System.Collections.Generic;
using System.Linq;
using TerrariaTas.Core.Movies;
using TerrariaTas.Core.Playback;
using TerrariaTas.Core.Verification;
using Xunit;

namespace TerrariaTas.Core.Tests;

public class SyncTestTests
{
    [Fact]
    public void DeterministicGameWithCompleteSavestatesPasses()
    {
        var test = Run(new FakeGame());

        Assert.True(test.Passed);
        Assert.Equal(3, test.Results.Count(line => !line.StartsWith("  RNG consumer")));
        Assert.All(test.Results, line => Assert.DoesNotContain("DESYNC", line));
    }

    [Fact]
    public void SavestateMissingSomeStateIsReportedWithTheDivergingRngCall()
    {
        var test = Run(new FakeGame { SavesHiddenState = false });

        Assert.False(test.Passed);
        Assert.Contains(test.Results, line => line.StartsWith("Replay from a savestate at frame 40: DESYNC, frame") && line.EndsWith("differs in value"));
        Assert.Contains(test.Results, line => line.Contains("RNG diverged during frame"));
    }

    [Fact]
    public void RestoredStateIsComparedBeforeAnyFrameRuns()
    {
        var test = Run(new FakeGame { CorruptsOnLoad = true });

        Assert.Contains("Replay from a savestate at frame 40: DESYNC, frame 40 differs in value", test.Results);
        Assert.Contains("Replay from the movie start state: DESYNC, frame 0 differs in value", test.Results);
    }

    [Fact]
    public void SingleFrameMovieWorks()
    {
        Assert.True(Run(new FakeGame(), "1 D").Passed);
    }

    private static SyncTest Run(FakeGame game, string movie = "40 D\n40 A")
    {
        var playback = new PlaybackController(MovieParser.Parse(movie));
        var test = new SyncTest(playback, game, game.SaveState(0));
        for (var tick = 0; !test.Finished; tick++)
        {
            Assert.True(tick < 10_000, "the self-test never finished");
            test.Update();
            var decision = playback.Next();
            if (decision.Run)
                game.RunFrame(decision.Input!);
        }

        return test;
    }

    /// <summary>A tiny deterministic game whose savestates can be made to miss or corrupt state.</summary>
    private sealed class FakeGame : ISyncTarget
    {
        private int _value;
        private int _hidden;
        private List<string> _calls = new();

        public bool SavesHiddenState { get; init; } = true;

        public bool CorruptsOnLoad { get; init; }

        public void RunFrame(InputLine input)
        {
            _hidden++;
            var bonus = _hidden % 7 == 0;
            _calls.Add(bonus ? "Bonus" : "Step");
            _value = unchecked(_value * 31 + input.Keys.Count + (bonus ? 1 : 0));
        }

        public Fingerprint Fingerprint() => new(new Dictionary<string, ulong> { ["value"] = (ulong)_value });

        public ulong FullHash() => (ulong)_value;

        public IReadOnlyList<string> TakeRngCalls()
        {
            var calls = _calls;
            _calls = new List<string>();
            return calls;
        }

        public object SaveState(int frame) => new[] { _value, _hidden };

        public void LoadState(object state)
        {
            var saved = (int[])state;
            _value = saved[0] + (CorruptsOnLoad ? 1 : 0);
            if (SavesHiddenState)
                _hidden = saved[1];
        }
    }
}
