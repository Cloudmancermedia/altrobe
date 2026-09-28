using System.Buffers.Binary;
using System.IO.Compression;
using Altrobe.Core.Convert;

namespace Altrobe.Core.Tests;

public class AssetConverterTests
{
    // A hand-built BLP2 header (148 bytes) plus a 256-entry palette slot, then mip 0.
    static byte[] Blp(byte encoding, byte alphaDepth, int width, int height, byte[] mip0)
    {
        var header = new byte[148 + 256 * 4];
        "BLP2"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1);
        header[8] = encoding;
        header[9] = alphaDepth;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), (uint)height);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), (uint)header.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(84), (uint)mip0.Length);
        return [.. header, .. mip0];
    }

    static (int width, int height, byte[] rgba) ReadPng(byte[] png)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
        var idat = new MemoryStream();
        for (var at = 8; at < png.Length;)
        {
            var len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            if (type == "IDAT") idat.Write(png, at + 8, len);
            at += 12 + len;
        }
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        var raw = new MemoryStream();
        z.CopyTo(raw);
        // Each scanline starts with a filter byte; the encoder writes filter 0 (none).
        var rows = raw.ToArray();
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            Assert.Equal(0, rows[y * (width * 4 + 1)]);
            Array.Copy(rows, y * (width * 4 + 1) + 1, rgba, y * width * 4, width * 4);
        }
        return (width, height, rgba);
    }

    [Fact]
    public void ConvertsUncompressedBgraBlpToPng()
    {
        byte[] bgra = [0x10, 0x20, 0x30, 0xFF, 0x01, 0x02, 0x03, 0x80];
        var (w, h, rgba) = ReadPng(AssetConverter.ConvertTexture(Blp(3, 8, 2, 1, bgra)));

        Assert.Equal((2, 1), (w, h));
        Assert.Equal(new byte[] { 0x30, 0x20, 0x10, 0xFF, 0x03, 0x02, 0x01, 0x80 }, rgba);
    }

    [Fact]
    public void ConvertsPalettizedBlpWithEightBitAlpha()
    {
        var blp = Blp(1, 8, 2, 1, [1, 0, 0x40, 0xC0]);
        // Palette entry 1 is BGRA (0x0A, 0x0B, 0x0C); entry 0 is black.
        blp[148 + 4] = 0x0A;
        blp[148 + 5] = 0x0B;
        blp[148 + 6] = 0x0C;

        var (_, _, rgba) = ReadPng(AssetConverter.ConvertTexture(blp));

        Assert.Equal(new byte[] { 0x0C, 0x0B, 0x0A, 0x40, 0, 0, 0, 0xC0 }, rgba);
    }

    [Fact]
    public void RejectsFilesThatAreNotBlp()
    {
        Assert.Throws<InvalidDataException>(() => AssetConverter.ConvertTexture(new byte[200]));
    }

    [Fact]
    public void RejectsFilesThatAreNotM2()
    {
        Assert.ThrowsAny<Exception>(() => AssetConverter.ConvertModel(1, _ => new byte[64]));
    }

    [Fact]
    public void ReadsDistinctGeosetIdsFromModelMetadata()
    {
        var meta = """{"geosets":[{"geosetId":702},{"geosetId":0},{"geosetId":702},{"geosetId":401}]}"""u8.ToArray();
        Assert.Equal([0, 401, 702], AssetConverter.GeosetIds(meta));
    }
}
