using Homestead.Assets.Gfx;

namespace Homestead.AssetTool;

internal static class ContactSheet
{
    private const int Cell = 128;
    private const int Columns = 16;

    public static IEnumerable<(string Name, GfxBitmap Bitmap)> Frames(GfxFile file, string kind, int index)
    {
        switch (kind)
        {
            case "settler":
                for (int f = 0; f < file.GetSettlerFrameCount(index); f++)
                    yield return (f.ToString(), file.ReadSettler(index, f));
                break;
            case "torso":
                for (int f = 0; f < file.GetTorsoFrameCount(index); f++)
                    yield return (f.ToString(), file.ReadTorso(index, f));
                break;
            case "shadow":
                for (int f = 0; f < file.GetShadowFrameCount(index); f++)
                    yield return (f.ToString(), file.ReadShadow(index, f));
                break;
            case "composite":
                for (int f = 0; f < file.GetSettlerFrameCount(index); f++)
                    yield return (f.ToString(), Compose(file, index, f));
                break;
            case "landscape":
                yield return ("0", file.ReadLandscape(index));
                break;
            case "gui":
                yield return ("0", file.ReadGui(index));
                break;
            default:
                throw new ArgumentException($"Unknown kind '{kind}'.");
        }
    }

    /// <summary>Shadow, settler and torso (tinted red) layered at their offsets, like the viewer draws them.</summary>
    private static GfxBitmap Compose(GfxFile file, int sequence, int frame)
    {
        var layers = new List<(GfxBitmap Bitmap, bool Torso)>();
        if (file.ReadSettlerShadow(sequence, frame) is { } shadow)
            layers.Add((shadow, false));
        layers.Add((file.ReadSettler(sequence, frame), false));
        if (file.ReadSettlerTorso(sequence, frame) is { } torso)
            layers.Add((torso, true));

        int left = layers.Min(l => l.Bitmap.OffsetX), top = layers.Min(l => l.Bitmap.OffsetY);
        int right = layers.Max(l => l.Bitmap.OffsetX + l.Bitmap.Width);
        int bottom = layers.Max(l => l.Bitmap.OffsetY + l.Bitmap.Height);
        int width = right - left, height = bottom - top;
        var pixels = new uint[width * height];

        foreach (var (bmp, isTorso) in layers)
        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
        {
            uint p = bmp.Pixels[y * bmp.Width + x];
            uint alpha = p >> 24;
            if (alpha == 0)
                continue;
            if (isTorso)
                p = 0xff000000 | (p & 0xff); // keep intensity in the red channel only
            ref uint dst = ref pixels[(bmp.OffsetY - top + y) * width + bmp.OffsetX - left + x];
            dst = alpha == 0xff ? p : Blend(dst, p, alpha);
        }
        return new GfxBitmap(width, height, left, top, pixels);
    }

    private static uint Blend(uint dst, uint src, uint alpha)
    {
        uint result = 0;
        for (int shift = 0; shift < 24; shift += 8)
        {
            uint d = (dst >> shift) & 0xff, s = (src >> shift) & 0xff;
            result |= ((s * alpha + d * (255 - alpha)) / 255) << shift;
        }
        uint outAlpha = Math.Max(dst >> 24, alpha);
        return result | (outAlpha << 24);
    }

    /// <summary>First frame of every entry, scaled down to fit a grid cell.</summary>
    public static void Write(GfxFile file, string kind, string outPath)
    {
        int count = kind switch
        {
            "settler" or "composite" => file.SettlerCount,
            "torso" => file.TorsoCount,
            "shadow" => file.ShadowCount,
            "landscape" => file.LandscapeCount,
            "gui" => file.GuiCount,
            _ => throw new ArgumentException($"Unknown kind '{kind}'."),
        };
        int rows = Math.Max(1, (count + Columns - 1) / Columns);
        int sheetWidth = Columns * Cell;
        var sheet = new uint[sheetWidth * rows * Cell];
        Array.Fill(sheet, 0xff404040u);

        for (int i = 0; i < count; i++)
        {
            GfxBitmap bmp;
            try { bmp = Frames(file, kind, i).FirstOrDefault().Bitmap ?? GfxBitmap.Empty; }
            catch { continue; }
            if (bmp.Width == 0 || bmp.Height == 0)
                continue;

            int scale = Math.Max(1, (Math.Max(bmp.Width, bmp.Height) + Cell - 1) / Cell);
            int ox = i % Columns * Cell, oy = i / Columns * Cell;
            for (int y = 0; y < Math.Min(Cell, bmp.Height / scale); y++)
            for (int x = 0; x < Math.Min(Cell, bmp.Width / scale); x++)
            {
                uint p = bmp.Pixels[y * scale * bmp.Width + x * scale];
                if (p >> 24 != 0)
                    sheet[(oy + y) * sheetWidth + ox + x] = p | 0xff000000;
            }
        }
        Png.Write(outPath, new GfxBitmap(sheetWidth, rows * Cell, 0, 0, sheet));
    }
}
