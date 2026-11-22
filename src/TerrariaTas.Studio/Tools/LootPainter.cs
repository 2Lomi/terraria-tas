using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using TerrariaTas.Core.Studio;
using TerrariaTas.Studio.Connection;

namespace TerrariaTas.Studio.Tools;

/// <summary>
/// Draws loot inside a grid cell: each item's picture followed by its prefix and stack, then the coins.
/// An item whose picture has not arrived, or does not exist, shows its name instead.
/// </summary>
internal static class LootPainter
{
    private const TextFormatFlags TextFlags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter
                                              | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping;

    private const int ItemGap = 10;
    private static readonly Color PrefixColor = Color.FromArgb(109, 40, 217);
    private static readonly ImageAttributes Faded = FadedAttributes();

    public static void Paint(Graphics graphics, Rectangle cell, DropOutcome outcome, ItemIconCache icons, Font font, Color textColor, bool faded)
    {
        var saved = graphics.Save();
        try
        {
            graphics.IntersectClip(cell);
            var x = cell.Left + 4;
            if (outcome.NoLootReason.Length > 0)
            {
                Text(graphics, $"no loot ({outcome.NoLootReason})", font, SystemColors.GrayText, cell, ref x);
                return;
            }

            if (outcome.Items.Count == 0 && outcome.Copper == 0)
            {
                Text(graphics, "nothing", font, SystemColors.GrayText, cell, ref x);
                return;
            }

            foreach (var item in outcome.Items)
            {
                if (icons.Get(item.ItemId) is { } icon)
                {
                    Icon(graphics, icon, cell, faded, ref x);
                    if (item.PrefixName.Length > 0)
                        Text(graphics, " " + item.PrefixName, font, faded ? textColor : PrefixColor, cell, ref x);
                }
                else
                {
                    Text(graphics, item.Name, font, textColor, cell, ref x);
                }

                if (item.Stack > 1)
                    Text(graphics, $" ×{item.Stack}", font, textColor, cell, ref x);
                x += ItemGap;
            }

            if (outcome.Copper > 0)
                Text(graphics, Coins.Format(outcome.Copper), font, textColor, cell, ref x);
        }
        finally
        {
            graphics.Restore(saved);
        }
    }

    private static void Text(Graphics graphics, string text, Font font, Color color, Rectangle cell, ref int x)
    {
        var width = TextRenderer.MeasureText(graphics, text, font, new Size(int.MaxValue, cell.Height), TextFlags).Width;
        TextRenderer.DrawText(graphics, text, font, new Rectangle(x, cell.Top, width + 1, cell.Height), color, TextFlags);
        x += width;
    }

    /// <summary>Small pictures are enlarged by whole factors to stay crisp; large ones are reduced to the row height.</summary>
    private static void Icon(Graphics graphics, Bitmap icon, Rectangle cell, bool faded, ref int x)
    {
        var box = Math.Max(8, cell.Height - 4);
        var scale = Math.Min((float)box / icon.Height, 2f * box / icon.Width);
        if (scale >= 1)
            scale = Math.Min(2, MathF.Floor(scale));
        var width = Math.Max(1, (int)Math.Round(icon.Width * scale));
        var height = Math.Max(1, (int)Math.Round(icon.Height * scale));
        var target = new Rectangle(x, cell.Top + (cell.Height - height) / 2, width, height);

        graphics.InterpolationMode = scale < 1 ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(icon, target, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, faded ? Faded : null);
        x += width;
    }

    private static ImageAttributes FadedAttributes()
    {
        var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = 0.35f });
        return attributes;
    }
}
