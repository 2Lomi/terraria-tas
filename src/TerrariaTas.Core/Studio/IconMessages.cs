using System;
using System.Collections.Generic;
using System.IO;

namespace TerrariaTas.Core.Studio;

/// <summary>
/// Item pictures, sent before the first message that mentions each item on a connection. Studio reads no
/// game files: the game's content is compressed in a format only XNA decodes.
/// </summary>
public sealed class ItemIcons : StudioMessage
{
    public override MessageType Type => MessageType.ItemIcons;

    public List<ItemIcon> Icons { get; set; } = new();

    internal override void Write(BinaryWriter writer) => writer.WriteList(Icons, (w, icon) => icon.Write(w));

    internal static ItemIcons Read(BinaryReader reader) => new() { Icons = reader.ReadList(ItemIcon.Read) };
}

/// <summary>An item as the inventory shows it: the first animation frame of its texture.</summary>
public sealed class ItemIcon
{
    public int ItemId { get; set; }

    /// <summary>0 when the game has no picture for the item.</summary>
    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>Red, green, blue, alpha for each pixel, row by row, with the color premultiplied by alpha as the game stores it.</summary>
    public byte[] Rgba { get; set; } = Array.Empty<byte>();

    internal void Write(BinaryWriter writer)
    {
        writer.Write(ItemId);
        writer.Write(Width);
        writer.Write(Height);
        writer.WriteBytes(Rgba);
    }

    internal static ItemIcon Read(BinaryReader reader)
    {
        var icon = new ItemIcon { ItemId = reader.ReadInt32(), Width = reader.ReadInt32(), Height = reader.ReadInt32(), Rgba = reader.ReadBytes() };
        if (icon.Width < 0 || icon.Height < 0 || icon.Rgba.Length != icon.Width * icon.Height * 4)
            throw new InvalidDataException($"Corrupted item icon {icon.ItemId}: {icon.Width}x{icon.Height} with {icon.Rgba.Length} bytes.");
        return icon;
    }
}
