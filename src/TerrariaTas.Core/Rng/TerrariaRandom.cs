using System;

namespace TerrariaTas.Core.Rng;

/// <summary>
/// Exact re-implementation of Terraria's <c>UnifiedRandom</c> (Knuth's subtractive generator,
/// the algorithm behind seeded <see cref="Random"/>). Prediction tools copy the game's RNG state
/// into this class and roll it forward without touching the game.
/// </summary>
[MirrorsGameMethod("Terraria.Utilities.UnifiedRandom", "SetSeed")]
[MirrorsGameMethod("Terraria.Utilities.UnifiedRandom", "InternalSample")]
[MirrorsGameMethod("Terraria.Utilities.UnifiedRandom", "Sample")]
[MirrorsGameMethod("Terraria.Utilities.UnifiedRandom", "GetSampleForLargeRange")]
[MirrorsGameMethod("Terraria.Utilities.UnifiedRandom", "Next")]
[MirrorsGameMethod("Terraria.Utilities.UnifiedRandom", "NextDouble")]
[MirrorsGameMethod("Terraria.Utilities.UnifiedRandom", "Peek")]
[MirrorsGameMethod("Terraria.Utils", "NextFloat")]
public sealed class TerrariaRandom
{
    public const int StateLength = 56;

    private const int MaxValue = int.MaxValue;
    private const int SeedConstant = 161803398;

    private readonly int[] _seedArray = new int[StateLength];
    private uint _index;

    public TerrariaRandom(int seed)
    {
        SetSeed(seed);
    }

    private TerrariaRandom()
    {
    }

    /// <summary>Position inside the state array (the game's private <c>inext</c> field).</summary>
    public uint Index => _index;

    /// <summary>Rebuilds a generator from the game's private <c>SeedArray</c> and <c>inext</c> fields.</summary>
    public static TerrariaRandom FromState(int[] seedArray, uint index)
    {
        if (seedArray.Length != StateLength)
            throw new ArgumentException($"Expected {StateLength} state values, got {seedArray.Length}.", nameof(seedArray));

        var random = new TerrariaRandom { _index = index };
        Array.Copy(seedArray, random._seedArray, StateLength);
        return random;
    }

    public int[] CopyState() => (int[])_seedArray.Clone();

    public TerrariaRandom Clone() => FromState(_seedArray, _index);

    public void SetSeed(int seed)
    {
        Array.Clear(_seedArray, 0, StateLength);
        var subtraction = seed == int.MinValue ? int.MaxValue : Math.Abs(seed);
        var mj = SeedConstant - subtraction;
        _seedArray[55] = mj;
        var mk = 1;
        for (var i = 1; i < 55; i++)
        {
            var ii = 21 * i % 55;
            _seedArray[ii] = mk;
            mk = mj - mk;
            if (mk < 0)
                mk += MaxValue;
            mj = _seedArray[ii];
        }

        for (var k = 1; k < 5; k++)
        {
            for (var i = 1; i < 56; i++)
            {
                _seedArray[i] -= _seedArray[1 + (i + 30) % 55];
                if (_seedArray[i] < 0)
                    _seedArray[i] += MaxValue;
            }
        }

        _index = 0;
    }

    public int Next() => InternalSample();

    public int Next(int maxValue)
    {
        if (maxValue < 0)
            throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be positive.");
        return (int)(Sample() * maxValue);
    }

    public int Next(int minValue, int maxValue)
    {
        if (minValue > maxValue)
            throw new ArgumentOutOfRangeException(nameof(minValue), "minValue must be less than maxValue.");

        var range = (long)maxValue - minValue;
        return range <= int.MaxValue
            ? (int)(Sample() * range) + minValue
            : (int)((long)(SampleForLargeRange() * range) + minValue);
    }

    public double NextDouble() => Sample();

    /// <summary>Terraria's <c>Utils.NextFloat(UnifiedRandom)</c> extension.</summary>
    public float NextFloat() => (float)Sample();

    /// <summary>The next raw sample without advancing, like the game's <c>Peek()</c>.</summary>
    public int Peek()
    {
        var (current, other) = NextIndices();
        return _seedArray[current] - _seedArray[other];
    }

    /// <summary>
    /// Counts the draws that take this generator to the given state (one per raw sample, so a
    /// <c>Next(min, max)</c> over more than <see cref="int.MaxValue"/> values counts twice).
    /// Returns -1 when it takes more than <paramref name="maxDraws"/>. This generator is not modified.
    /// </summary>
    public long DrawsUntil(int[] state, uint index, long maxDraws)
    {
        if (state.Length != StateLength)
            throw new ArgumentException($"Expected {StateLength} state values, got {state.Length}.", nameof(state));

        var probe = Clone();
        for (long draws = 0; ; draws++)
        {
            if (probe.HasState(state, index))
                return draws;
            if (draws == maxDraws)
                return -1;
            probe.InternalSample();
        }
    }

    public long DrawsUntil(TerrariaRandom target, long maxDraws) => DrawsUntil(target._seedArray, target._index, maxDraws);

    private bool HasState(int[] state, uint index)
    {
        // A draw only rewrites the slot at _index, so comparing it first rejects almost every candidate cheaply.
        if (_index != index || _seedArray[index] != state[index])
            return false;

        for (var i = 0; i < StateLength; i++)
        {
            if (_seedArray[i] != state[i])
                return false;
        }

        return true;
    }

    private double Sample() => InternalSample() * (1.0 / MaxValue);

    private double SampleForLargeRange()
    {
        var result = InternalSample();
        if (InternalSample() % 2 == 0)
            result = -result;
        return (result + 2147483646.0) / 4294967293.0;
    }

    private int InternalSample()
    {
        var (current, other) = NextIndices();
        var value = _seedArray[current] - _seedArray[other];
        if (value == MaxValue)
            value--;
        if (value < 0)
            value += MaxValue;

        _seedArray[current] = value;
        _index = current;
        return value;
    }

    private (uint Current, uint Other) NextIndices()
    {
        var current = _index + 1;
        if (current > 55)
            current = 1;
        var other = current + 21;
        if (other > 55)
            other -= 55;
        return (current, other);
    }
}
