using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TerrariaTas.Core.Movies;

/// <summary>
/// SHA-256 of a player or world file, as the movie header writes it (<c>PlayerChecksum</c>, <c>WorldChecksum</c>): 64
/// lowercase hexadecimal digits. A movie only replays from the exact files it was made with.
/// </summary>
public static class FileChecksum
{
    public const int Length = 64;

    public static string Of(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Hex(sha.ComputeHash(stream));
    }

    /// <summary>The checksum in its written form, or null when <paramref name="text"/> is not one.</summary>
    public static string? Normalize(string text)
    {
        var value = text.Trim();
        return value.Length == Length && value.All(Uri.IsHexDigit) ? value.ToLowerInvariant() : null;
    }

    private static string Hex(byte[] bytes)
    {
        var text = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
            text.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return text.ToString();
    }
}
