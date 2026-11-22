using System;
using System.Collections.Generic;
using System.IO;
using TerrariaTas.Core.Movies;

namespace TerrariaTas.Core.Studio;

/// <summary>Shared encodings for the Studio messages.</summary>
internal static class BinaryExtensions
{
    public static void WriteList<T>(this BinaryWriter writer, IReadOnlyCollection<T> items, Action<BinaryWriter, T> write)
    {
        writer.Write(items.Count);
        foreach (var item in items)
            write(writer, item);
    }

    public static List<T> ReadList<T>(this BinaryReader reader, Func<BinaryReader, T> read)
    {
        var count = reader.ReadCount();
        var items = new List<T>(count);
        for (var i = 0; i < count; i++)
            items.Add(read(reader));
        return items;
    }

    public static void WriteInts(this BinaryWriter writer, int[] values)
    {
        writer.Write(values.Length);
        foreach (var value in values)
            writer.Write(value);
    }

    public static int[] ReadInts(this BinaryReader reader)
    {
        var values = new int[reader.ReadCount()];
        for (var i = 0; i < values.Length; i++)
            values[i] = reader.ReadInt32();
        return values;
    }

    public static void WriteBytes(this BinaryWriter writer, byte[] values)
    {
        writer.Write(values.Length);
        writer.Write(values);
    }

    public static byte[] ReadBytes(this BinaryReader reader)
    {
        var count = reader.ReadCount();
        var values = reader.ReadBytes(count);
        if (values.Length != count)
            throw new EndOfStreamException("Corrupted Studio message: byte array cut short.");
        return values;
    }

    public static void WriteFloats(this BinaryWriter writer, float[] values)
    {
        writer.Write(values.Length);
        foreach (var value in values)
            writer.Write(value);
    }

    public static float[] ReadFloats(this BinaryReader reader)
    {
        var values = new float[reader.ReadCount()];
        for (var i = 0; i < values.Length; i++)
            values[i] = reader.ReadSingle();
        return values;
    }

    public static void WriteKeys(this BinaryWriter writer, IReadOnlyList<Key> keys)
    {
        writer.Write(keys.Count);
        foreach (var key in keys)
            writer.Write((byte)key);
    }

    public static Key[] ReadKeys(this BinaryReader reader)
    {
        var keys = new Key[reader.ReadCount()];
        for (var i = 0; i < keys.Length; i++)
            keys[i] = (Key)reader.ReadByte();
        return keys;
    }

    public static void WriteInputLine(this BinaryWriter writer, InputLine line)
    {
        writer.Write(line.FrameCount);
        writer.WriteKeys(line.Keys);
        writer.Write((byte)line.Buttons);
        writer.Write(line.Cursor.HasValue);
        if (line.Cursor is { } cursor)
        {
            writer.Write((byte)cursor.Space);
            writer.Write(cursor.X);
            writer.Write(cursor.Y);
        }

        writer.Write(line.Wheel);
        writer.Write(line.SourceLine);
    }

    public static InputLine ReadInputLine(this BinaryReader reader)
    {
        var frameCount = reader.ReadInt32();
        var keys = reader.ReadKeys();
        var buttons = (MouseButtons)reader.ReadByte();
        CursorTarget? cursor = reader.ReadBoolean()
            ? new CursorTarget((CursorSpace)reader.ReadByte(), reader.ReadInt32(), reader.ReadInt32())
            : null;
        var wheel = reader.ReadInt32();
        var sourceLine = reader.ReadInt32();
        return new InputLine(frameCount, keys, buttons, cursor, wheel, sourceLine);
    }

    private static int ReadCount(this BinaryReader reader)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > StudioProtocol.MaxFrameBytes)
            throw new InvalidDataException($"Corrupted Studio message: list of {count} elements.");
        return count;
    }
}
