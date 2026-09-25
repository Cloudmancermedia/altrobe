// Portions ported from wow.export (https://github.com/Kruithne/wow.export), MIT License, Copyright (c) Kruithne and Marlamin.
// Source: src/js/3D/loaders/M2Generics.js (M2Track, patch_track_animation), SKELLoader.js, ANIMLoader.js,
// and the bone/sequence parts of M2Loader.js. Layouts checked against https://wowdev.wiki/M2,
// https://wowdev.wiki/M2/.skel and https://wowdev.wiki/M2/.anim.
//
// Shared skeleton and animation types for .m2 and .skel files. Track data stays as raw per-sequence
// (count, offset) pairs until a caller asks for one sequence, because most sequences live in external
// .anim files that are only loaded on demand.

using System.Buffers.Binary;
using System.Numerics;

namespace Altrobe.Convert;

// One M2Sequence (64 bytes). Flag 0x20: track data is inside this file. Flag 0x40: alias of `AliasNext`.
record M2Sequence(ushort Id, ushort Variation, uint Duration, uint Flags, short VariationNext, ushort AliasNext)
{
    public bool DataInFile => (Flags & 0x20) != 0;
    public bool IsAlias => (Flags & 0x40) != 0;

    public static M2Sequence[] ReadArray(ReadOnlySpan<byte> d, int headerAt, int baseOfs)
    {
        var (n, ofs) = (M2Bin.U32(d, headerAt), (int)M2Bin.U32(d, headerAt + 4));
        var r = new M2Sequence[n];
        for (var i = 0; i < n; i++)
        {
            var o = baseOfs + ofs + i * 64;
            r[i] = new M2Sequence(M2Bin.U16(d, o), M2Bin.U16(d, o + 2), M2Bin.U32(d, o + 4), M2Bin.U32(d, o + 12),
                M2Bin.I16(d, o + 60), M2Bin.U16(d, o + 62));
        }
        return r;
    }
}

// An M2Track header: interpolation, global sequence, and one (count, offset) pair per sequence for
// timestamps and for values. Offsets point into the owning file, or into the sequence's .anim file.
sealed class M2Track
{
    public ushort Interpolation;  // 0 none (step), 1 linear, 2 bezier, 3 hermite
    public short GlobalSeq;       // -1 when the track follows the playing sequence
    public (uint count, uint ofs)[] Times = [];
    public (uint count, uint ofs)[] Values = [];

    public static M2Track Read(ReadOnlySpan<byte> d, int at, int baseOfs)
    {
        var t = new M2Track { Interpolation = M2Bin.U16(d, at), GlobalSeq = M2Bin.I16(d, at + 2) };
        t.Times = Pairs(d, at + 4, baseOfs);
        t.Values = Pairs(d, at + 12, baseOfs);
        return t;
    }

    static (uint, uint)[] Pairs(ReadOnlySpan<byte> d, int at, int baseOfs)
    {
        var (n, ofs) = (M2Bin.U32(d, at), (int)M2Bin.U32(d, at + 4));
        var r = new (uint, uint)[n];
        for (var i = 0; i < n; i++) r[i] = (M2Bin.U32(d, baseOfs + ofs + i * 8), M2Bin.U32(d, baseOfs + ofs + i * 8 + 4));
        return r;
    }

    // Keyframes for sequence `seq`, read from `src` (offsets relative to `srcBase`). Returns null when the
    // track has no keys for that sequence or the data does not fit the buffer (same bounds rule as wow.export).
    public (uint[] times, T[] values)? Keys<T>(int seq, ReadOnlySpan<byte> src, int srcBase, int valueSize, Func<ReadOnlySpan<byte>, int, T> read)
    {
        if (seq >= Times.Length || seq >= Values.Length) return null;
        var (tn, to) = Times[seq];
        var (vn, vo) = Values[seq];
        if (tn == 0 || vn != tn) return null;
        if (srcBase + to + tn * 4 > src.Length || srcBase + vo + vn * valueSize > src.Length) return null;
        var times = new uint[tn];
        var values = new T[vn];
        for (var j = 0; j < tn; j++) times[j] = M2Bin.U32(src, (int)(srcBase + to + j * 4));
        for (var j = 0; j < vn; j++) values[j] = read(src, (int)(srcBase + vo + j * valueSize));
        return (times, values);
    }
}

// One M2CompBone (88 bytes): key bone id, flags, parent, submesh, name CRC, three tracks, pivot.
record M2Bone(int KeyBoneId, uint Flags, short ParentBone, uint NameCrc, M2Track Translation, M2Track Rotation, M2Track Scale, Vector3 Pivot)
{
    public static M2Bone[] ReadArray(ReadOnlySpan<byte> d, int headerAt, int baseOfs)
    {
        var (n, ofs) = (M2Bin.U32(d, headerAt), (int)M2Bin.U32(d, headerAt + 4));
        var r = new M2Bone[n];
        for (var i = 0; i < n; i++)
        {
            var o = baseOfs + ofs + i * 88;
            r[i] = new M2Bone(M2Bin.I32(d, o), M2Bin.U32(d, o + 4), M2Bin.I16(d, o + 8), M2Bin.U32(d, o + 12),
                M2Track.Read(d, o + 16, baseOfs), M2Track.Read(d, o + 36, baseOfs), M2Track.Read(d, o + 56, baseOfs), M2Bin.V3(d, o + 76));
        }
        return r;
    }

    // Compressed quaternion: four uint16, each mapped to [-1, 1] as (v - 32767) / 32768 (wow.export M2Generics).
    public static Quaternion ReadCompQuat(ReadOnlySpan<byte> d, int at) => new(
        (M2Bin.U16(d, at) - 32767) / 32768f, (M2Bin.U16(d, at + 2) - 32767) / 32768f,
        (M2Bin.U16(d, at + 4) - 32767) / 32768f, (M2Bin.U16(d, at + 6) - 32767) / 32768f);
}

record M2AnimFileId(ushort AnimId, ushort SubAnimId, uint FileDataId);

// A file that owns bones and sequences: either the M2 itself or a .skel. Exposes where each
// sequence's track data lives.
interface IM2Skeleton
{
    M2Bone[] Bones { get; }
    M2Sequence[] Sequences { get; }
    M2AnimFileId[] AnimFileIds { get; }
    bool ChunkedAnims { get; }
    // The buffer and base offset that in-file track offsets are relative to.
    (byte[] data, int baseOfs) InFileData { get; }
}

// .skel file: SKL1 header, SKB1 bones, SKS1 sequences, SKA1 attachments, SKPD parent skeleton, AFID, BFID.
sealed class M2Skel : IM2Skeleton
{
    public M2Bone[] Bones { get; private set; } = [];
    public M2Sequence[] Sequences { get; private set; } = [];
    public M2AnimFileId[] AnimFileIds { get; private set; } = [];
    public bool ChunkedAnims => true;
    public (byte[] data, int baseOfs) InFileData => (_data, _trackBase);
    public uint ParentSkelFileDataId;
    public M2Attachment[] Attachments = [];
    public short[] AttachmentLookup = [];
    public uint[] BoneFileDataIds = [];
    public List<string> Chunks = [];

    byte[] _data = [];
    int _trackBase;

    const uint SKB1 = 0x31424B53, SKPD = 0x44504B53, SKS1 = 0x31534B53, SKA1 = 0x31414B53, AFID = 0x44494641, BFID = 0x44494642;

    public static M2Skel Parse(byte[] data)
    {
        var s = new M2Skel { _data = data };
        int skb1 = -1, ska1 = -1, sks1 = -1;
        foreach (var (id, body, size) in M2Bin.Chunks(data))
        {
            s.Chunks.Add($"{M2Bin.FourCC(id)}({size})");
            switch (id)
            {
                case SKB1: skb1 = body; break;
                case SKA1: ska1 = body; break;
                case SKS1: sks1 = body; break;
                case SKPD: s.ParentSkelFileDataId = M2Bin.U32(data, body + 8); break;
                case AFID: s.AnimFileIds = M2Bin.ReadAfid(data, body, size); break;
                case BFID: s.BoneFileDataIds = Enumerable.Range(0, size / 4).Select(i => M2Bin.U32(data, body + i * 4)).ToArray(); break;
            }
        }
        // Offsets inside each chunk are relative to the start of that chunk's body.
        if (sks1 >= 0) s.Sequences = M2Sequence.ReadArray(data, sks1 + 8, sks1);
        if (skb1 >= 0) s.Bones = M2Bone.ReadArray(data, skb1, skb1);
        if (ska1 >= 0)
        {
            s.Attachments = M2Attachment.ReadArray(data, ska1, ska1);
            var (n, ofs) = (M2Bin.U32(data, ska1 + 8), (int)M2Bin.U32(data, ska1 + 12));
            s.AttachmentLookup = Enumerable.Range(0, (int)n).Select(i => M2Bin.I16(data, ska1 + ofs + i * 2)).ToArray();
        }
        // In-file (flag 0x20) bone track offsets are relative to SKB1, where the bones live.
        s._trackBase = skb1;
        return s;
    }
}

record M2Attachment(uint Id, ushort Bone, Vector3 Position)
{
    public static M2Attachment[] ReadArray(ReadOnlySpan<byte> d, int headerAt, int baseOfs)
    {
        var (n, ofs) = (M2Bin.U32(d, headerAt), (int)M2Bin.U32(d, headerAt + 4));
        var r = new M2Attachment[n];
        for (var i = 0; i < n; i++)
        {
            var o = baseOfs + ofs + i * 40;
            r[i] = new M2Attachment(M2Bin.U32(d, o), M2Bin.U16(d, o + 4), M2Bin.V3(d, o + 8));
        }
        return r;
    }
}

// .anim file. Chunked form (skeleton-based models): AFM2, AFSA (attachment data), AFSB (bone data).
// Unchunked form: the whole file is the track data (wow.export ANIMLoader).
static class M2AnimFile
{
    const uint AFM2 = 0x324D4641, AFSA = 0x41534641, AFSB = 0x42534641;

    // Returns the buffer and base offset that bone track offsets are relative to.
    public static (byte[] data, int baseOfs, string layout) BoneData(byte[] file, bool chunked)
    {
        if (!chunked) return (file, 0, "raw");
        int afm2 = -1, afsb = -1;
        foreach (var (id, body, _) in M2Bin.Chunks(file))
        {
            if (id == AFM2) afm2 = body;
            if (id == AFSB) afsb = body;
        }
        // wow.export prefers AFSB (skeleton bone data) over AFM2 when both are present.
        if (afsb >= 0) return (file, afsb, "AFSB");
        if (afm2 >= 0) return (file, afm2, "AFM2");
        throw new InvalidDataException("Chunked .anim has neither AFSB nor AFM2");
    }
}

static class M2Bin
{
    public static uint U32(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadUInt32LittleEndian(d[at..]);
    public static int I32(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadInt32LittleEndian(d[at..]);
    public static ushort U16(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadUInt16LittleEndian(d[at..]);
    public static short I16(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadInt16LittleEndian(d[at..]);
    public static float F32(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadSingleLittleEndian(d[at..]);
    public static Vector3 V3(ReadOnlySpan<byte> d, int at) => new(F32(d, at), F32(d, at + 4), F32(d, at + 8));
    public static string FourCC(uint id) => System.Text.Encoding.ASCII.GetString(BitConverter.GetBytes(id));

    public static IEnumerable<(uint id, int body, int size)> Chunks(byte[] data)
    {
        var pos = 0;
        while (pos + 8 <= data.Length)
        {
            var id = U32(data, pos);
            var size = (int)U32(data, pos + 4);
            if (pos + 8 + size > data.Length) throw new InvalidDataException($"Chunk {FourCC(id)} at {pos} runs past end of file");
            yield return (id, pos + 8, size);
            pos += 8 + size;
        }
    }

    public static M2AnimFileId[] ReadAfid(byte[] d, int body, int size) =>
        Enumerable.Range(0, size / 8).Select(i => new M2AnimFileId(U16(d, body + i * 8), U16(d, body + i * 8 + 2), U32(d, body + i * 8 + 4))).ToArray();
}

static class M2AnimSource
{
    // Finds the buffer holding bone track data for sequence `seq`: the owning file when the sequence
    // has flag 0x20, otherwise the .anim file named by AFID for its (id, variation). Aliases (flag 0x40)
    // follow AliasNext to pick the file, as wow.export's loadAnimsForIndex does.
    public static (byte[] data, int baseOfs, string source)? Resolve(IM2Skeleton s, int seq, Func<uint, byte[]> open)
    {
        var q = s.Sequences[seq];
        for (var hops = 0; q.IsAlias && hops < s.Sequences.Length; hops++) q = s.Sequences[q.AliasNext];
        if (q.DataInFile)
        {
            var (d, b) = s.InFileData;
            return (d, b, "in-file");
        }
        var entry = s.AnimFileIds.FirstOrDefault(a => a.AnimId == q.Id && a.SubAnimId == q.Variation);
        if (entry == null || entry.FileDataId == 0) return null;
        var (data, baseOfs, layout) = M2AnimFile.BoneData(open(entry.FileDataId), s.ChunkedAnims);
        return (data, baseOfs, $"anim {entry.FileDataId} ({layout})");
    }
}
