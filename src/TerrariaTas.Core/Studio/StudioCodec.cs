using System;
using System.IO;
using System.Text;

namespace TerrariaTas.Core.Studio;

/// <summary>Turns Studio messages into length-prefixed frames and back.</summary>
public static class StudioCodec
{
    public static byte[] Encode(StudioMessage message)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)message.Type);
            message.Write(writer);
        }

        return buffer.ToArray();
    }

    public static StudioMessage Decode(byte[] payload)
    {
        using var reader = new BinaryReader(new MemoryStream(payload), Encoding.UTF8);
        var type = (MessageType)reader.ReadByte();
        return type switch
        {
            MessageType.Hello => Hello.Read(reader),
            MessageType.MovieData => MovieData.Read(reader),
            MessageType.FrameStatus => FrameStatus.Read(reader),
            MessageType.WorldSnapshot => WorldSnapshot.Read(reader),
            MessageType.DropForecast => DropForecast.Read(reader),
            MessageType.KillRecord => KillRecord.Read(reader),
            MessageType.ItemIcons => ItemIcons.Read(reader),
            MessageType.HotkeyBindings => HotkeyBindings.Read(reader),
            MessageType.Command => Command.Read(reader),
            MessageType.ForecastRequest => ForecastRequest.Read(reader),
            MessageType.WatchList => WatchList.Read(reader),
            _ => throw new InvalidDataException($"Unknown Studio message type {(byte)type}."),
        };
    }

    public static void WriteFrame(Stream stream, byte[] payload)
    {
        // Both ends run on the same machine, so the native (little-endian) byte order is shared.
        stream.Write(BitConverter.GetBytes(payload.Length), 0, sizeof(int));
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
    }

    /// <returns>The frame's payload, or null when the stream ended cleanly between two frames.</returns>
    public static byte[]? ReadFrame(Stream stream)
    {
        var header = new byte[sizeof(int)];
        if (!Fill(stream, header, endAllowed: true))
            return null;

        var length = BitConverter.ToInt32(header, 0);
        if (length < 1 || length > StudioProtocol.MaxFrameBytes)
            throw new InvalidDataException($"Corrupted Studio stream: frame of {length} bytes.");

        var payload = new byte[length];
        Fill(stream, payload, endAllowed: false);
        return payload;
    }

    private static bool Fill(Stream stream, byte[] buffer, bool endAllowed)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = stream.Read(buffer, read, buffer.Length - read);
            if (count == 0)
            {
                if (read == 0 && endAllowed)
                    return false;
                throw new EndOfStreamException("The Studio stream ended in the middle of a frame.");
            }

            read += count;
        }

        return true;
    }
}
