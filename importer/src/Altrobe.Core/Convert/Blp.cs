// Portions ported from wow.export (https://github.com/Kruithne/wow.export), MIT License, Copyright (c) Kruithne and Marlamin.
// Source: src/js/casc/blp.js. Format reference: https://wowdev.wiki/BLP.
//
// Decodes the top mipmap of a BLP2 file to RGBA8. Supports palettized (encoding 1),
// DXT1/3/5 (encoding 2), and uncompressed BGRA (encoding 3).

using System.Buffers.Binary;

namespace Altrobe.Core.Convert;

static class Blp
{
    const uint Magic = 0x32504C42; // "BLP2"

    public record Image(int Width, int Height, byte[] Rgba, string Format);

    public static Image Decode(byte[] d)
    {
        if (U32(d, 0) != Magic) throw new InvalidDataException("Not a BLP2 file (bad magic)");
        var type = U32(d, 4);
        if (type != 1) throw new InvalidDataException($"Unsupported BLP type {type}");
        var encoding = d[8];
        var alphaDepth = d[9];
        var alphaEncoding = d[10];
        var width = (int)U32(d, 12);
        var height = (int)U32(d, 16);
        var ofs = (int)U32(d, 20);   // mip 0 offset
        var size = (int)U32(d, 84);  // mip 0 size
        if (ofs + size > d.Length) throw new InvalidDataException($"Mip 0 at {ofs}+{size} runs past end of file ({d.Length})");
        var raw = d.AsSpan(ofs, size);
        var rgba = new byte[width * height * 4];

        switch (encoding)
        {
            case 1: // palettized: 256 BGRA entries at offset 148, then indices, then alpha
                for (var i = 0; i < width * height; i++)
                {
                    var p = 148 + raw[i] * 4;
                    rgba[i * 4] = d[p + 2];
                    rgba[i * 4 + 1] = d[p + 1];
                    rgba[i * 4 + 2] = d[p];
                    rgba[i * 4 + 3] = PaletteAlpha(raw, width * height, i, alphaDepth);
                }
                return new Image(width, height, rgba, $"palette/a{alphaDepth}");
            case 2:
                var fmt = alphaDepth > 1 ? (alphaEncoding == 7 ? 5 : 3) : 1;
                DecodeDxt(raw, width, height, fmt, rgba);
                return new Image(width, height, rgba, $"DXT{fmt}");
            case 3: // BGRA
                for (var i = 0; i < width * height; i++)
                {
                    rgba[i * 4] = raw[i * 4 + 2];
                    rgba[i * 4 + 1] = raw[i * 4 + 1];
                    rgba[i * 4 + 2] = raw[i * 4];
                    rgba[i * 4 + 3] = raw[i * 4 + 3];
                }
                return new Image(width, height, rgba, "BGRA");
            default:
                throw new InvalidDataException($"Unsupported BLP encoding {encoding}");
        }
    }

    static byte PaletteAlpha(ReadOnlySpan<byte> raw, int n, int i, int depth) => depth switch
    {
        1 => (byte)((raw[n + i / 8] & (1 << (i % 8))) == 0 ? 0 : 255),
        4 => (byte)(i % 2 == 0 ? (raw[n + i / 2] & 0x0F) << 4 : raw[n + i / 2] & 0xF0),
        8 => raw[n + i],
        _ => 255,
    };

    static void DecodeDxt(ReadOnlySpan<byte> raw, int w, int h, int fmt, byte[] outp)
    {
        var blockBytes = fmt == 1 ? 8 : 16;
        Span<byte> colours = stackalloc byte[16];
        Span<byte> block = stackalloc byte[64];
        Span<byte> alphas = stackalloc byte[8];
        var pos = 0;
        for (var y = 0; y < h; y += 4)
        {
            for (var x = 0; x < w; x += 4)
            {
                if (pos + blockBytes > raw.Length) return;
                var ci = fmt == 1 ? pos : pos + 8;
                var a = Unpack565(raw, ci, colours, 0);
                var b = Unpack565(raw, ci + 2, colours, 4);
                var threeColour = fmt == 1 && a <= b;
                for (var i = 0; i < 3; i++)
                {
                    int c = colours[i], e = colours[i + 4];
                    if (threeColour) { colours[i + 8] = (byte)((c + e) / 2); colours[i + 12] = 0; }
                    else { colours[i + 8] = (byte)((2 * c + e) / 3); colours[i + 12] = (byte)((c + 2 * e) / 3); }
                }
                colours[11] = 255;
                colours[15] = (byte)(threeColour ? 0 : 255);

                for (var i = 0; i < 16; i++)
                {
                    var idx = (raw[ci + 4 + i / 4] >> (2 * (i % 4))) & 3;
                    for (var k = 0; k < 4; k++) block[i * 4 + k] = colours[idx * 4 + k];
                }

                if (fmt == 3)
                {
                    for (var i = 0; i < 8; i++)
                    {
                        var q = raw[pos + i];
                        int lo = q & 0x0F, hi = q & 0xF0;
                        block[8 * i + 3] = (byte)(lo | (lo << 4));
                        block[8 * i + 7] = (byte)(hi | (hi >> 4));
                    }
                }
                else if (fmt == 5)
                {
                    int a0 = raw[pos], a1 = raw[pos + 1];
                    alphas[0] = (byte)a0; alphas[1] = (byte)a1;
                    if (a0 <= a1)
                    {
                        for (var i = 1; i < 5; i++) alphas[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5);
                        alphas[6] = 0; alphas[7] = 255;
                    }
                    else
                    {
                        for (var i = 1; i < 7; i++) alphas[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);
                    }
                    for (var half = 0; half < 2; half++)
                    {
                        var v = raw[pos + 2 + half * 3] | (raw[pos + 3 + half * 3] << 8) | (raw[pos + 4 + half * 3] << 16);
                        for (var j = 0; j < 8; j++) block[(half * 8 + j) * 4 + 3] = alphas[(v >> (3 * j)) & 7];
                    }
                }

                for (var py = 0; py < 4; py++)
                    for (var px = 0; px < 4; px++)
                    {
                        int sx = x + px, sy = y + py;
                        if (sx >= w || sy >= h) continue;
                        var o = 4 * (w * sy + sx);
                        var bp = 4 * (py * 4 + px);
                        outp[o] = block[bp]; outp[o + 1] = block[bp + 1]; outp[o + 2] = block[bp + 2]; outp[o + 3] = block[bp + 3];
                    }
                pos += blockBytes;
            }
        }
    }

    static int Unpack565(ReadOnlySpan<byte> d, int at, Span<byte> c, int co)
    {
        var v = d[at] | (d[at + 1] << 8);
        int r = (v >> 11) & 0x1F, g = (v >> 5) & 0x3F, b = v & 0x1F;
        c[co] = (byte)((r << 3) | (r >> 2));
        c[co + 1] = (byte)((g << 2) | (g >> 4));
        c[co + 2] = (byte)((b << 3) | (b >> 2));
        c[co + 3] = 255;
        return v;
    }

    static uint U32(byte[] d, int at) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at));
}
