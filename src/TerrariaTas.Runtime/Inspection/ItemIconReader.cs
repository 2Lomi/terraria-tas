using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using TerrariaTas.Core;
using TerrariaTas.Core.Studio;

namespace TerrariaTas.Runtime.Inspection;

/// <summary>
/// Item pictures for Studio, cropped and tinted the way the inventory shows them. A private content manager decodes them
/// straight from the game's files: the game's own textures are never requested, so whether and when the game
/// loads a texture stays exactly as it would without Studio. Main thread only: decoding creates a texture on
/// the game's graphics device.
/// </summary>
internal sealed class ItemIconReader
{
    private readonly string _imagesDir = Path.Combine(Path.GetDirectoryName(typeof(Main).Assembly.Location)!, "Content", "Images");
    private bool _warned;

    /// <summary>The item's picture, or an empty icon (Studio then shows the name) when it cannot be read.</summary>
    public ItemIcon Read(int itemId)
    {
        if (itemId <= 0 || itemId >= ItemID.Count)
            return new ItemIcon { ItemId = itemId };

        try
        {
            // Some items borrow another item's texture.
            var textureId = ItemID.Sets.TextureCopyLoad[itemId] >= 0 ? ItemID.Sets.TextureCopyLoad[itemId] : itemId;
            using var content = new FileContent(Main.instance.Services);
            using var texture = content.LoadTexture(Path.Combine(_imagesDir, $"Item_{textureId}.xnb"));
            var frame = FirstFrame(texture, Main.itemAnimations[itemId]);
            var pixels = new Color[frame.Width * frame.Height];
            texture.GetData(0, frame, pixels, 0, pixels.Length);
            return new ItemIcon { ItemId = itemId, Width = frame.Width, Height = frame.Height, Rgba = Tint(pixels, itemId) };
        }
        catch (Exception e)
        {
            // Pictures are a convenience: never let one stop the link.
            if (!_warned)
                Log.Warn($"TAS Studio: cannot read the picture of item {itemId}, Studio shows names instead ({e.Message})");
            _warned = true;
            return new ItemIcon { ItemId = itemId };
        }
    }

    /// <summary>
    /// The inventory draws the texture tinted by <c>GetAlpha</c>, then, for items with a color (gel...), again tinted
    /// by <c>GetColor</c>, both under white light. Both are pure functions of the item; the game's sample item is read.
    /// </summary>
    [MirrorsGameMethod("Terraria.UI.ItemSlot", "DrawItemIcon")]
    private static byte[] Tint(Color[] pixels, int itemId)
    {
        ContentSamples.ItemsByType.TryGetValue(itemId, out var sample);
        var first = sample?.GetAlpha(Color.White) ?? Color.White;
        Color? second = sample != null && sample.color != Color.Transparent ? sample.GetColor(Color.White) : null;

        var rgba = new byte[pixels.Length * 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            var pixel = Multiply(pixels[i], first);
            if (second is { } tint)
                pixel = Over(Multiply(pixels[i], tint), pixel);
            rgba[i * 4] = pixel.R;
            rgba[i * 4 + 1] = pixel.G;
            rgba[i * 4 + 2] = pixel.B;
            rgba[i * 4 + 3] = pixel.A;
        }

        return rgba;
    }

    // SpriteBatch multiplies every channel of the (premultiplied) texel by the tint.
    private static Color Multiply(Color texel, Color tint) =>
        new(texel.R * tint.R / 255, texel.G * tint.G / 255, texel.B * tint.B / 255, texel.A * tint.A / 255);

    // SpriteBatch's default blend state: premultiplied "over".
    private static Color Over(Color top, Color below)
    {
        var keep = 255 - top.A;
        return new Color(top.R + below.R * keep / 255, top.G + below.G * keep / 255, top.B + below.B * keep / 255, top.A + below.A * keep / 255);
    }

    /// <summary>Animated items stack equal frames vertically, 2 pixels apart; the inventory shows the first one.</summary>
    [MirrorsGameMethod("Terraria.DataStructures.DrawAnimationVertical", "GetFrame")]
    [MirrorsGameMethod("Terraria.DataStructures.DrawAnimationScryingOrb", "GetFrame")]
    private static Rectangle FirstFrame(Texture2D texture, DrawAnimation? animation)
    {
        if (animation is not (DrawAnimationVertical or DrawAnimationScryingOrb) || animation.FrameCount < 1)
            return texture.Bounds;
        return new Rectangle(0, 0, texture.Width, Math.Max(1, texture.Height / animation.FrameCount - 2));
    }

    /// <summary>Reads .xnb files by path, outside the game's asset repository.</summary>
    private sealed class FileContent : ContentManager
    {
        public FileContent(IServiceProvider services)
            : base(services)
        {
        }

        public Texture2D LoadTexture(string path) => ReadAsset<Texture2D>(path, null);

        protected override Stream OpenStream(string assetName) => File.OpenRead(assetName);
    }
}
