// Portions ported from wow.export (https://github.com/Kruithne/wow.export), MIT License, Copyright (c) Kruithne and Marlamin.
// Source: src/js/3D/loaders/M2Loader.js and src/js/3D/Skin.js. Layouts checked against
// https://wowdev.wiki/M2 and https://wowdev.wiki/M2/.skin.
//
// Reads vertices (with bone weights and indices), textures, materials, texture combos, bones with their animation
// track headers, sequences, attachments, and the chunked-file FileDataID lists (SFID, TXID, SKID, AFID).

using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Altrobe.Convert;

sealed class M2Model : IM2Skeleton
{
    public record Vertex(Vector3 Position, Vector3 Normal, Vector2 Uv0, uint BoneWeights, uint BoneIndices);
    public record Texture(uint Type, uint Flags, uint FileDataId);
    public record Attachment(uint Id, ushort Bone, Vector3 Position);
    // M2Material: render flags (0x1 unlit, 0x2 unfogged, 0x4 two-sided, 0x8 no depth test, 0x10 no depth write)
    // and blending mode (0 opaque, 1 alpha key, 2 alpha, 3 no-alpha add, 4 add, 5 mod, 6 mod2x, 7 blend add).
    public record Material(ushort Flags, ushort BlendMode);

    public uint Version;
    public string Name = "";
    public uint Flags;
    public Vertex[] Vertices = [];
    public uint ViewCount;
    public Texture[] Textures = [];
    public ushort[] TextureCombos = [];
    public Material[] Materials = [];
    public M2Bone[] Bones { get; private set; } = [];
    public M2Sequence[] Sequences { get; private set; } = [];
    public M2AnimFileId[] AnimFileIds { get; private set; } = [];
    // wow.export M2Loader: .anim files are chunked when flag 0x200000 is set or the model has a .skel.
    public bool ChunkedAnims => (Flags & 0x200000) != 0 || SkeletonFileDataId > 0;
    public (byte[] data, int baseOfs) InFileData => (_data, _md20);
    public List<string> Chunks = [];
    byte[] _data = [];
    int _md20;
    public Attachment[] Attachments = [];
    public short[] AttachmentLookup = [];
    public uint[] SkinFileDataIds = [];
    public uint SkeletonFileDataId;

    const uint MD21 = 0x3132444D, MD20 = 0x3032444D, SFID = 0x44494653, TXID = 0x44495854, SKID = 0x44494B53, AFID = 0x44494641;

    public static M2Model Parse(byte[] data)
    {
        var m = new M2Model { _data = data };
        var txid = Array.Empty<uint>();
        uint[]? sfid = null;
        var pos = 0;
        var sawMd21 = false;
        while (pos + 8 <= data.Length)
        {
            var id = U32(data, pos);
            var size = (int)U32(data, pos + 4);
            var body = pos + 8;
            if (body + size > data.Length)
                throw new InvalidDataException($"Chunk {FourCC(id)} at offset {pos} runs past end of file ({body + size} > {data.Length})");
            m.Chunks.Add($"{FourCC(id)}({size})");
            switch (id)
            {
                case MD21: m.ParseMd20(data.AsSpan(body, size)); m._md20 = body; sawMd21 = true; break;
                case SFID: sfid = ReadU32s(data, body, size / 4); break;
                case TXID: txid = ReadU32s(data, body, size / 4); break;
                case SKID: m.SkeletonFileDataId = U32(data, body); break;
                case AFID: m.AnimFileIds = M2Bin.ReadAfid(data, body, size); break;
            }
            pos = body + size;
        }
        if (!sawMd21) throw new InvalidDataException("No MD21 chunk; legacy (non-chunked) M2 is not supported");

        // SFID holds viewCount skin IDs followed by LOD skin IDs. Only the first viewCount are real skins.
        m.SkinFileDataIds = sfid == null ? [] : sfid.Take((int)m.ViewCount).ToArray();
        // TXID gives one FileDataID per texture entry, in order. Only hardcoded (type 0) textures use it.
        m.Textures = m.Textures.Select((t, i) => t with { FileDataId = i < txid.Length ? txid[i] : 0 }).ToArray();
        return m;
    }

    void ParseMd20(ReadOnlySpan<byte> d)
    {
        if (U32(d, 0) != MD20) throw new InvalidDataException("MD21 chunk does not start with MD20 magic");
        Version = U32(d, 4);
        var (nameCount, nameOfs) = Arr(d, 8);
        if (nameCount > 0) Name = Encoding.ASCII.GetString(d.Slice((int)nameOfs, (int)nameCount)).TrimEnd('\0');
        Flags = U32(d, 16);

        Sequences = M2Sequence.ReadArray(d, 28, 0);
        Bones = M2Bone.ReadArray(d, 44, 0);

        // Vertices: 48 bytes each (pos, bone weights[4], bone indices[4], normal, uv0, uv1).
        var (vertCount, vertOfs) = Arr(d, 60);
        Vertices = new Vertex[vertCount];
        for (var i = 0; i < vertCount; i++)
        {
            var o = (int)vertOfs + i * 48;
            Vertices[i] = new Vertex(V3(d, o), V3(d, o + 20), new Vector2(F32(d, o + 32), F32(d, o + 36)), U32(d, o + 12), U32(d, o + 16));
        }

        ViewCount = U32(d, 68);

        // Textures: 16 bytes each (type, flags, filename M2Array).
        var (texCount, texOfs) = Arr(d, 80);
        Textures = new Texture[texCount];
        for (var i = 0; i < texCount; i++)
        {
            var o = (int)texOfs + i * 16;
            Textures[i] = new Texture(U32(d, o), U32(d, o + 4), 0);
        }

        // Materials: 4 bytes each (flags u16, blending mode u16). https://wowdev.wiki/M2#Render_flags_and_blending_modes
        var (matCount, matOfs) = Arr(d, 112);
        Materials = new Material[matCount];
        for (var i = 0; i < matCount; i++)
        {
            var o = (int)matOfs + i * 4;
            Materials[i] = new Material(BinaryPrimitives.ReadUInt16LittleEndian(d[o..]), BinaryPrimitives.ReadUInt16LittleEndian(d[(o + 2)..]));
        }

        // Texture combos (a.k.a. texture lookup): maps a batch's textureComboIndex to a texture index.
        var (comboCount, comboOfs) = Arr(d, 128);
        TextureCombos = new ushort[comboCount];
        for (var i = 0; i < comboCount; i++) TextureCombos[i] = BinaryPrimitives.ReadUInt16LittleEndian(d[((int)comboOfs + i * 2)..]);

        // Attachments: 40 bytes each (id, bone, unknown, position, animate_attached track).
        var (attCount, attOfs) = Arr(d, 240);
        Attachments = new Attachment[attCount];
        for (var i = 0; i < attCount; i++)
        {
            var o = (int)attOfs + i * 40;
            Attachments[i] = new Attachment(U32(d, o), BinaryPrimitives.ReadUInt16LittleEndian(d[(o + 4)..]), V3(d, o + 8));
        }
        var (lookCount, lookOfs) = Arr(d, 248);
        AttachmentLookup = new short[lookCount];
        for (var i = 0; i < lookCount; i++) AttachmentLookup[i] = BinaryPrimitives.ReadInt16LittleEndian(d[((int)lookOfs + i * 2)..]);
    }

    static (uint count, uint ofs) Arr(ReadOnlySpan<byte> d, int at) => (U32(d, at), U32(d, at + 4));
    internal static uint U32(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadUInt32LittleEndian(d[at..]);
    static int I32(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadInt32LittleEndian(d[at..]);
    static float F32(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadSingleLittleEndian(d[at..]);
    static Vector3 V3(ReadOnlySpan<byte> d, int at) => new(F32(d, at), F32(d, at + 4), F32(d, at + 8));
    static uint[] ReadU32s(byte[] d, int at, int n) => Enumerable.Range(0, n).Select(i => U32(d, at + i * 4)).ToArray();
    static string FourCC(uint id) => Encoding.ASCII.GetString(BitConverter.GetBytes(id));
}

sealed class M2Skin
{
    public record Submesh(ushort SkinSectionId, ushort Level, ushort VertexStart, ushort VertexCount, int TriangleStart, int TriangleCount);
    public record Batch(byte Flags, sbyte Priority, ushort ShaderId, ushort SkinSectionIndex, ushort MaterialIndex, ushort MaterialLayer, ushort TextureCount, ushort TextureComboIndex);

    public ushort[] Indices = [];   // skin vertex list -> global M2 vertex index
    public ushort[] Triangles = []; // triangle corners, each an index into Indices
    public Submesh[] Submeshes = [];
    public Batch[] Batches = [];

    const uint SKIN = 0x4E494B53;

    public static M2Skin Parse(byte[] data)
    {
        ReadOnlySpan<byte> d = data;
        if (M2Model.U32(d, 0) != SKIN) throw new InvalidDataException("Not a .skin file (bad magic)");
        var s = new M2Skin();
        s.Indices = U16s(d, 4);
        s.Triangles = U16s(d, 12);

        var (smCount, smOfs) = (M2Model.U32(d, 28), M2Model.U32(d, 32));
        s.Submeshes = new Submesh[smCount];
        for (var i = 0; i < smCount; i++)
        {
            var o = (int)smOfs + i * 48;
            var level = U16(d, o + 2);
            // Level extends triangleStart past 65535 on large models.
            s.Submeshes[i] = new Submesh(U16(d, o), level, U16(d, o + 4), U16(d, o + 6), U16(d, o + 8) + (level << 16), U16(d, o + 10));
        }

        var (bCount, bOfs) = (M2Model.U32(d, 36), M2Model.U32(d, 40));
        s.Batches = new Batch[bCount];
        for (var i = 0; i < bCount; i++)
        {
            var o = (int)bOfs + i * 24;
            s.Batches[i] = new Batch(d[o], (sbyte)d[o + 1], U16(d, o + 2), U16(d, o + 4), U16(d, o + 10), U16(d, o + 12), U16(d, o + 14), U16(d, o + 16));
        }
        return s;
    }

    static ushort U16(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadUInt16LittleEndian(d[at..]);
    static ushort[] U16s(ReadOnlySpan<byte> d, int at)
    {
        var (n, ofs) = (M2Model.U32(d, at), M2Model.U32(d, at + 4));
        var r = new ushort[n];
        for (var i = 0; i < n; i++) r[i] = U16(d, (int)ofs + i * 2);
        return r;
    }
}
