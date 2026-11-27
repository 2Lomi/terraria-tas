using System.Collections.Generic;
using System.Linq;
using Terraria.Utilities;
using TerrariaTas.Core.Rng;
using TerrariaTas.Core.Studio;
using TerrariaTas.Runtime.GameAccess;

namespace TerrariaTas.Runtime.RngTools;

/// <summary>
/// Follows every random number generator from frame to frame: how many draws it made since it was
/// seeded (its position) and during the last frame. Nothing is hooked: at each frame boundary a model of
/// the previous state is stepped until it matches the current one, so tracking cannot change the game.
/// </summary>
internal sealed class RngTracker
{
    // A frame draws thousands of numbers per stream, rarely more than a few hundred thousand.
    // Past this many, the tracker gives up on that stream rather than stall the game.
    private const long MaxDrawsPerFrame = 20_000_000;

    private Dictionary<string, Track> _tracks = new();

    /// <summary>The movie just started: the shared generator was reseeded and the streams were discarded.</summary>
    public void Reset()
    {
        _tracks = new Dictionary<string, Track>
        {
            [GameRng.SharedName] = new(0, -1, GameRng.ToModel(Terraria.Main.rand)),
        };
    }

    /// <summary>Accounts for the frame that just ended. Call exactly once per frame, at the boundary after it.</summary>
    public void CompleteFrame()
    {
        var tracks = new Dictionary<string, Track>();
        foreach (var generator in GameRng.AtFrameBoundary())
        {
            var current = GameRng.ToModel(generator.Value);
            tracks[generator.Key] = _tracks.TryGetValue(generator.Key, out var track)
                ? track.Advance(current)
                : Track.Discover(generator.Key, current);
        }

        _tracks = tracks;
    }

    /// <summary>Position of a stream at the current frame boundary, or -1 when unknown.</summary>
    public long PositionOf(string name) => _tracks.TryGetValue(name, out var track) ? track.Position : -1;

    /// <summary>
    /// Position of a stream where the next frame starts drawing from it (<see cref="GameRng.CloneForNextFrame"/>): 0 for
    /// a stream the game has not created yet. Call at a frame boundary.
    /// </summary>
    public long StartPositionOf(string name) => GameRng.Streams.ContainsKey(name) ? PositionOf(name) : 0;

    /// <summary>Draws a generator made since the current frame started, or -1 when unknown. Usable in the middle of a frame.</summary>
    public long DrawsThisFrame(string name, UnifiedRandom generator)
    {
        var current = GameRng.ToModel(generator);
        if (_tracks.TryGetValue(name, out var track))
            return track.Start.DrawsUntil(current, MaxDrawsPerFrame);

        // Not there at the frame start: the stream was created during this frame.
        return name == GameRng.SharedName ? -1 : new TerrariaRandom(GameRng.StreamSeed(name)).DrawsUntil(current, MaxDrawsPerFrame);
    }

    /// <summary>Every generator with its tracking and its full state. Call at a frame boundary.</summary>
    public List<RngStreamView> Views() => GameRng.AtFrameBoundary().Select(generator =>
    {
        var model = GameRng.ToModel(generator.Value);
        _tracks.TryGetValue(generator.Key, out var track);
        return new RngStreamView
        {
            Name = generator.Key,
            Position = track?.Position ?? -1,
            LastFrameDraws = track?.LastFrameDraws ?? -1,
            State = model.CopyState(),
            Index = model.Index,
        };
    }).ToList();

    /// <summary>Savestate support: tracks are immutable, so a copy of the table is enough.</summary>
    public object CaptureState() => new Dictionary<string, Track>(_tracks);

    public void RestoreState(object? state) => _tracks = new Dictionary<string, Track>((Dictionary<string, Track>)state!);

    /// <summary>What is known about one generator at a frame boundary. Start is never mutated.</summary>
    private sealed class Track
    {
        public Track(long position, long lastFrameDraws, TerrariaRandom start)
        {
            Position = position;
            LastFrameDraws = lastFrameDraws;
            Start = start;
        }

        public long Position { get; }

        public long LastFrameDraws { get; }

        /// <summary>State at the boundary, which is where the next frame starts.</summary>
        public TerrariaRandom Start { get; }

        public static Track Discover(string name, TerrariaRandom current)
        {
            // Boundaries are visited every frame, so a stream missing at the previous one was created during the
            // last frame, from its seed. The shared generator is only known after a Reset.
            var position = name == GameRng.SharedName
                ? -1
                : new TerrariaRandom(GameRng.StreamSeed(name)).DrawsUntil(current, MaxDrawsPerFrame);
            return new Track(position, position, current);
        }

        public Track Advance(TerrariaRandom current)
        {
            var draws = Start.DrawsUntil(current, MaxDrawsPerFrame);
            var position = draws < 0 || Position < 0 ? -1 : Position + draws;
            return new Track(position, draws, current);
        }
    }
}
