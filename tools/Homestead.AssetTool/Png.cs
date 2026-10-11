using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using Homestead.Assets.Gfx;

namespace Homestead.AssetTool;

/// <summary>Minimal RGBA PNG writer, enough for debug exports.</summary>
internal static class Png
{
    public static void Write(string path, GfxBitmap bitmap)
    {
        using var file = File.Create(path);
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0d, 0x0a, 0x1a, 0x0a]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, Math.Max(1, bitmap.Width));
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), Math.Max(1, bitmap.Height));
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA
        WriteChunk(file, "IHDR", header);

        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + bitmap.Width * 4];
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                    BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(1 + x * 4), bitmap.Pixels[y * bitmap.Width + x]);
                z.Write(row);
            }
            if (bitmap.Width == 0 || bitmap.Height == 0)
                z.Write(new byte[5]);
        }
        WriteChunk(file, "IDAT", raw.ToArray());
        WriteChunk(file, "IEND", []);
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buf, data.Length);
        s.Write(buf);
        byte[] typeAndData = [.. type.Select(c => (byte)c), .. data];
        s.Write(typeAndData);
        BinaryPrimitives.WriteUInt32BigEndian(buf, Crc32.HashToUInt32(typeAndData));
        s.Write(buf);
    }
}
