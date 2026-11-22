using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TerrariaTas.Core.Studio;

namespace TerrariaTas.Studio.Connection;

/// <summary>
/// Item pictures the game sent, as bitmaps. They come from the game's files, so they stay valid across
/// reconnections. Only used from the UI thread.
/// </summary>
internal sealed class ItemIconCache
{
    private readonly Dictionary<int, Bitmap?> _icons = new();

    /// <summary>The item's picture; null until the game sent it, or when the game has none.</summary>
    public Bitmap? Get(int itemId) => _icons.TryGetValue(itemId, out var icon) ? icon : null;

    public void Add(ItemIcon icon)
    {
        if (_icons.TryGetValue(icon.ItemId, out var previous))
            previous?.Dispose();
        _icons[icon.ItemId] = icon.Width > 0 && icon.Height > 0 ? ToBitmap(icon) : null;
    }

    private static Bitmap ToBitmap(ItemIcon icon)
    {
        var format = IsPremultiplied(icon.Rgba) ? PixelFormat.Format32bppPArgb : PixelFormat.Format32bppArgb;
        var bitmap = new Bitmap(icon.Width, icon.Height, format);
        var data = bitmap.LockBits(new Rectangle(0, 0, icon.Width, icon.Height), ImageLockMode.WriteOnly, format);
        try
        {
            // GDI+ wants blue, green, red, alpha.
            var row = new byte[icon.Width * 4];
            for (var y = 0; y < icon.Height; y++)
            {
                for (var x = 0; x < icon.Width; x++)
                {
                    var source = (y * icon.Width + x) * 4;
                    row[x * 4] = icon.Rgba[source + 2];
                    row[x * 4 + 1] = icon.Rgba[source + 1];
                    row[x * 4 + 2] = icon.Rgba[source];
                    row[x * 4 + 3] = icon.Rgba[source + 3];
                }

                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    // XNA's content pipeline premultiplies colors by alpha; a channel brighter than alpha means this picture was not.
    private static bool IsPremultiplied(byte[] rgba)
    {
        for (var i = 0; i < rgba.Length; i += 4)
        {
            var alpha = rgba[i + 3];
            if (rgba[i] > alpha || rgba[i + 1] > alpha || rgba[i + 2] > alpha)
                return false;
        }

        return true;
    }
}
