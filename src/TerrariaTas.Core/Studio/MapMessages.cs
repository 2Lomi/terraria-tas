using System;
using System.Collections.Generic;
using System.IO;
using TerrariaTas.Core.World;

namespace TerrariaTas.Core.Studio;

/// <summary>
/// Asks for the world map: every tile in the color of the game's own map, fully revealed. The game answers at the next
/// paused frame inside a world with a <see cref="MapHeader"/>, then the rows in several <see cref="MapRows"/>.
/// </summary>
public sealed class MapRequest : StudioMessage
{
    public override MessageType Type => MessageType.MapRequest;

    /// <summary>Echoed in the answers, so Studio can ignore rows of an older request.</summary>
    public int RequestId { get; set; }

    internal override void Write(BinaryWriter writer) => writer.Write(RequestId);

    internal static MapRequest Read(BinaryReader reader) => new() { RequestId = reader.ReadInt32() };
}

/// <summary>The start of a world map: its size and layers. Its rows follow. Sent again with an <see cref="Error"/> if reading stops.</summary>
public sealed class MapHeader : StudioMessage
{
    public override MessageType Type => MessageType.MapHeader;

    public int RequestId { get; set; }

    /// <summary>The frame boundary the first rows were read at.</summary>
    public int Frame { get; set; }

    public string WorldName { get; set; } = "";

    public WorldGeometry World { get; set; } = new();

    /// <summary>Why the map stopped (no rows follow), empty while it is fine.</summary>
    public string Error { get; set; } = "";

    internal override void Write(BinaryWriter writer)
    {
        writer.Write(RequestId);
        writer.Write(Frame);
        writer.Write(WorldName);
        World.Write(writer);
        writer.Write(Error);
    }

    internal static MapHeader Read(BinaryReader reader) => new()
    {
        RequestId = reader.ReadInt32(),
        Frame = reader.ReadInt32(),
        WorldName = reader.ReadString(),
        World = WorldGeometry.Read(reader),
        Error = reader.ReadString(),
    };
}

/// <summary>
/// Whole rows of the world map, run-length encoded: colors are 0xRRGGBB values of <see cref="Palette"/>, and a run
/// may continue from the end of one row into the next. Build them with <see cref="MapRowsBuilder"/>.
/// </summary>
public sealed class MapRows : StudioMessage
{
    public override MessageType Type => MessageType.MapRows;

    public int RequestId { get; set; }

    /// <summary>The frame boundary these rows were read at: the game may have run frames since the first rows.</summary>
    public int Frame { get; set; }

    public int FirstRow { get; set; }

    public int RowCount { get; set; }

    /// <summary>The distinct colors of these rows, 0xRRGGBB.</summary>
    public int[] Palette { get; set; } = Array.Empty<int>();

    /// <summary>For each run, the index of its color in <see cref="Palette"/>.</summary>
    public int[] RunColors { get; set; } = Array.Empty<int>();

    /// <summary>For each run, how many tiles it covers, in row-major order.</summary>
    public int[] RunLengths { get; set; } = Array.Empty<int>();

    /// <summary>Writes the rows into a map of the whole world, row-major: the color of tile (x, y) goes to pixels[y * width + x].</summary>
    /// <exception cref="InvalidDataException">The runs do not cover exactly these rows, or the rows do not fit in the map.</exception>
    public void CopyTo(int[] pixels, int width)
    {
        if (RunColors.Length != RunLengths.Length)
            throw new InvalidDataException("Corrupted map rows: run colors and lengths differ in count.");

        long start = (long)FirstRow * width;
        long end = start + (long)RowCount * width;
        if (FirstRow < 0 || RowCount < 0 || end > pixels.Length)
            throw new InvalidDataException($"Map rows {FirstRow} to {FirstRow + RowCount - 1} do not fit in a map of {pixels.Length / Math.Max(1, width)} rows.");

        var index = (int)start;
        for (var run = 0; run < RunLengths.Length; run++)
        {
            var length = RunLengths[run];
            var color = RunColors[run];
            if (length < 0 || color < 0 || color >= Palette.Length || index + (long)length > end)
                throw new InvalidDataException("Corrupted map rows: a run is out of range.");
            for (var i = 0; i < length; i++)
                pixels[index + i] = Palette[color];
            index += length;
        }

        if (index != end)
            throw new InvalidDataException($"Corrupted map rows: the runs cover {index - start} tiles, the rows {end - start}.");
    }

    internal override void Write(BinaryWriter writer)
    {
        writer.Write(RequestId);
        writer.Write(Frame);
        writer.Write(FirstRow);
        writer.Write(RowCount);
        writer.WriteInts(Palette);
        writer.WriteInts(RunColors);
        writer.WriteInts(RunLengths);
    }

    internal static MapRows Read(BinaryReader reader) => new()
    {
        RequestId = reader.ReadInt32(),
        Frame = reader.ReadInt32(),
        FirstRow = reader.ReadInt32(),
        RowCount = reader.ReadInt32(),
        Palette = reader.ReadInts(),
        RunColors = reader.ReadInts(),
        RunLengths = reader.ReadInts(),
    };
}

/// <summary>Collects the colors of whole map rows, tile by tile in row-major order, into a <see cref="MapRows"/>.</summary>
public sealed class MapRowsBuilder
{
    private readonly int _width;
    private readonly Dictionary<int, int> _palette = new();
    private readonly List<int> _paletteColors = new();
    private readonly List<int> _runColors = new();
    private readonly List<int> _runLengths = new();
    private int _lastColor = -1;
    private long _tiles;

    public MapRowsBuilder(int width)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "A map row needs at least one tile.");
        _width = width;
    }

    /// <summary>Rows completed so far.</summary>
    public int Rows => (int)(_tiles / _width);

    /// <param name="rgb">The color of the next tile, 0xRRGGBB.</param>
    public void Add(int rgb)
    {
        rgb &= 0xFFFFFF;
        _tiles++;
        if (rgb == _lastColor)
        {
            _runLengths[_runLengths.Count - 1]++;
            return;
        }

        if (!_palette.TryGetValue(rgb, out var index))
        {
            index = _paletteColors.Count;
            _palette[rgb] = index;
            _paletteColors.Add(rgb);
        }

        _runColors.Add(index);
        _runLengths.Add(1);
        _lastColor = rgb;
    }

    /// <exception cref="InvalidOperationException">The tiles added do not make whole rows.</exception>
    public MapRows Build(int requestId, int frame, int firstRow)
    {
        if (_tiles % _width != 0)
            throw new InvalidOperationException($"{_tiles} tiles do not make whole rows of {_width}.");

        return new MapRows
        {
            RequestId = requestId,
            Frame = frame,
            FirstRow = firstRow,
            RowCount = Rows,
            Palette = _paletteColors.ToArray(),
            RunColors = _runColors.ToArray(),
            RunLengths = _runLengths.ToArray(),
        };
    }
}
