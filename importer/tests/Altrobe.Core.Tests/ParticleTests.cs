using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Altrobe.Core.Convert;

namespace Altrobe.Core.Tests;

// M2 particle emitters over a hand-built MD21 file: one Stand sequence, two bones, one texture, one
// emitter whose tracks and lifetime curves are set to known values.
public class ParticleTests
{
    const int Header = 0x130, Version = 274, Record = 492;

    sealed class Md20(int size)
    {
        public readonly byte[] B = new byte[size];
        int _end = Header;

        public int Alloc(int n) { var at = _end; _end += (n + 3) & ~3; return at; }
        public void U32(int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(B.AsSpan(at), v);
        public void I32(int at, int v) => BinaryPrimitives.WriteInt32LittleEndian(B.AsSpan(at), v);
        public void U16(int at, int v) => BinaryPrimitives.WriteUInt16LittleEndian(B.AsSpan(at), (ushort)v);
        public void I16(int at, int v) => BinaryPrimitives.WriteInt16LittleEndian(B.AsSpan(at), (short)v);
        public void F32(int at, float v) => BinaryPrimitives.WriteSingleLittleEndian(B.AsSpan(at), v);
        public void Arr(int at, int count, int ofs) { U32(at, (uint)count); U32(at + 4, (uint)ofs); }

        // An M2Track with one key list for sequence 0 (or none), values written by `write`.
        public void Track(int at, (uint t, Action<int> write)[] keys, int valueSize, short globalSeq = -1)
        {
            U16(at, 1);
            I16(at + 2, globalSeq);
            if (keys.Length == 0) return;
            var times = Alloc(keys.Length * 4);
            var values = Alloc(keys.Length * valueSize);
            for (var i = 0; i < keys.Length; i++) { U32(times + i * 4, keys[i].t); keys[i].write(values + i * valueSize); }
            var tPair = Alloc(8); Arr(tPair, keys.Length, times);
            var vPair = Alloc(8); Arr(vPair, keys.Length, values);
            Arr(at + 4, 1, tPair);
            Arr(at + 12, 1, vPair);
        }

        public void FBlock(int at, short[] times, Action<int, int> write, int valueSize)
        {
            var t = Alloc(times.Length * 2);
            var v = Alloc(times.Length * valueSize);
            for (var i = 0; i < times.Length; i++) { I16(t + i * 2, times[i]); write(i, v + i * valueSize); }
            Arr(at, times.Length, t);
            Arr(at + 8, times.Length, v);
        }
    }

    static byte[] Model(Action<Md20, int>? editRecord = null)
    {
        var m = new Md20(8192);
        m.U32(0, 0x3032444D);
        m.U32(4, Version);

        var seq = m.Alloc(64);
        m.U32(seq + 4, 1000);
        m.U32(seq + 12, 0x20);
        m.Arr(0x1C, 1, seq);

        // Bone 1's pivot is (0, 0, 1) in WoW axes, (0, 1, 0) in glTF.
        var bones = m.Alloc(2 * 88);
        for (var i = 0; i < 2; i++) { m.I32(bones + i * 88, -1); m.I16(bones + i * 88 + 8, (short)(i - 1)); }
        m.F32(bones + 88 + 76 + 8, 1);
        m.Arr(0x2C, 2, bones);

        var tex = m.Alloc(16);
        m.Arr(0x50, 1, tex);

        var p = m.Alloc(Record);
        m.Arr(0x128, 1, p);
        m.I32(p, -1);
        m.F32(p + 0x08, 0.1f); m.F32(p + 0x0C, 0.2f); m.F32(p + 0x10, 1.3f);
        m.U16(p + 0x14, 1);
        m.U16(p + 0x16, 0);
        m.B[p + 0x28] = 4;
        m.B[p + 0x29] = 1;
        m.U16(p + 0x30, 2);
        m.U16(p + 0x32, 2);
        m.Track(p + 0x34, [(0, a => m.F32(a, 0.5f))], 4);
        m.Track(p + 0x84, [(0, a => m.F32(a, 2f))], 4);
        m.Track(p + 0x98, [(0, a => m.F32(a, 1.5f))], 4);
        m.F32(p + 0xAC, 0.25f);
        m.Track(p + 0xB0, [(0, a => m.F32(a, 10f)), (500, a => m.F32(a, 20f))], 4);
        float[][] rgb = [[255, 0, 0], [0, 255, 0], [0, 0, 255]];
        m.FBlock(p + 0x104, [0, 16383, 32767], (i, a) => { m.F32(a, rgb[i][0]); m.F32(a + 4, rgb[i][1]); m.F32(a + 8, rgb[i][2]); }, 12);
        m.FBlock(p + 0x114, [0, 32767], (i, a) => m.I16(a, i == 0 ? 32767 : 0), 2);
        m.FBlock(p + 0x124, [0], (_, a) => { m.F32(a, 0.3f); m.F32(a + 4, 0.3f); }, 8);
        m.F32(p + 0x134, 0.1f); m.F32(p + 0x138, 0.2f);
        m.F32(p + 0x174, 0.5f);
        editRecord?.Invoke(m, p);

        // MD21 chunk around the MD20 body, then TXID with the texture's FileDataID.
        var md21 = new byte[8 + m.B.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(md21, 0x3132444D);
        BinaryPrimitives.WriteUInt32LittleEndian(md21.AsSpan(4), (uint)m.B.Length);
        m.B.CopyTo(md21, 8);
        var txid = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(txid, 0x44495854);
        BinaryPrimitives.WriteUInt32LittleEndian(txid.AsSpan(4), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(txid.AsSpan(8), 203839);
        return [.. md21, .. txid];
    }

    // Through JSON text, as the viewer reads it.
    static JsonObject Emitter(byte[] model) =>
        JsonNode.Parse(M2Particles.Export(M2Model.Parse(model), _ => throw new FileNotFoundException()).ToJsonString())![0]!.AsObject();

    static float[] Floats(JsonNode? n) => n!.AsArray().Select(x => x!.GetValue<float>()).ToArray();

    [Fact]
    public void ReadsTheEmitterRecord()
    {
        var e = Emitter(Model());
        Assert.Equal(1, e["bone"]!.GetValue<int>());
        Assert.Equal("bone_1", e["boneNode"]!.GetValue<string>());
        Assert.Equal(203839u, e["textureFileDataId"]!.GetValue<uint>());
        Assert.Equal(4, e["blendMode"]!.GetValue<int>());
        Assert.Equal(1, e["emitterType"]!.GetValue<int>());
        Assert.Equal([2, 2], new[] { e["rows"]!.GetValue<int>(), e["columns"]!.GetValue<int>() });
        // Bone-relative, in glTF axes: (0.1, 0.2, 1.3) -> (0.1, 1.3, -0.2), minus the pivot (0, 1, 0).
        var off = Floats(e["offsetGltf"]);
        Assert.Equal(0.1f, off[0], 4); Assert.Equal(0.3f, off[1], 4); Assert.Equal(-0.2f, off[2], 4);
        Assert.Equal(0.25f, e["lifespanVariation"]!.GetValue<float>());
        Assert.Equal(0.5f, e["drag"]!.GetValue<float>());
        Assert.Equal([0.1f, 0.2f], Floats(e["scaleVariation"]));
    }

    [Fact]
    public void SamplesStandTracksAsKeysInMilliseconds()
    {
        var tracks = Emitter(Model())["tracks"]!;
        Assert.Equal(1000, tracks["emissionRate"]!["durationMs"]!.GetValue<int>());
        var keys = tracks["emissionRate"]!["keys"]!.AsArray().Select(Floats).ToArray();
        Assert.Equal([0f, 10f], keys[0]);
        Assert.Equal([500f, 20f], keys[1]);
        Assert.Equal([0f, 0.5f], Floats(tracks["emissionSpeed"]!["keys"]![0]));
        Assert.Equal([0f, 1.5f], Floats(tracks["lifespan"]!["keys"]![0]));
        // A track with no keys is left out, so the viewer uses its default.
        Assert.Null(tracks["zSource"]);
    }

    [Fact]
    public void GravityPointsDownInGltfAxes()
    {
        // A plain float pulls along WoW -Z, which is glTF -Y.
        var g = Floats(Emitter(Model())["tracks"]!["gravity"]!["keys"]![0]);
        Assert.Equal([0f, 0f, -2f, 0f], g.Select(x => x == 0 ? 0f : x).ToArray());
    }

    [Fact]
    public void CompressedGravityDecodesToAVector()
    {
        // Flag 0x800000: int8 x, int8 y, int16 z. x = 0, y = 0, z = 100 -> straight up, length 100 * 0.04238648.
        var e = Emitter(Model((m, p) =>
        {
            m.U32(p + 0x04, 0x800000);
            m.Track(p + 0x84, [(0, a => { m.B[a] = 0; m.B[a + 1] = 0; m.I16(a + 2, 100); })], 4);
        }));
        var g = Floats(e["tracks"]!["gravity"]!["keys"]![0]);
        Assert.Equal(0, g[1], 4);
        Assert.Equal(100 * 0.04238648f, g[2], 4);
        Assert.Equal(0, g[3], 4);
    }

    [Fact]
    public void LifetimeCurvesUseFractionsOfTheParticlesLife()
    {
        var e = Emitter(Model());
        Assert.Equal([0f, 0.5f, 1f], Floats(e["color"]!["times"]).Select(t => MathF.Round(t, 3)).ToArray());
        Assert.Equal([1f, 0f, 0f], Floats(e["color"]!["values"]![0]));
        Assert.Equal([0f, 0f, 1f], Floats(e["color"]!["values"]![2]));
        Assert.Equal([1f, 0f], e["alpha"]!["values"]!.AsArray().Select(v => MathF.Round(v!.GetValue<float>(), 3)).ToArray());
        Assert.Equal([0.3f, 0.3f], Floats(e["scale"]!["values"]![0]));
    }

    [Fact]
    public void AModelWithoutEmittersExportsNone()
    {
        var bytes = Model();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8 + 0x128), 0);
        Assert.Empty(M2Particles.Export(M2Model.Parse(bytes), _ => throw new FileNotFoundException()));
    }
}

// Opt-in (ALTROBE_INTEGRATION=1): the Horde PvP shoulders' glows come through the real converter.
public sealed class ParticleIntegrationTests : IDisposable
{
    readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), "altrobe-integration", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, true);
    }

    async Task<JsonArray> Particles(uint fdid)
    {
        var (install, product) = RealInstall.Find()!.Value;
        var session = Altrobe.Core.Builds.BuildSession.Open(install, product, _cacheRoot);
        var meta = JsonNode.Parse(await File.ReadAllBytesAsync((await session.ModelAsync(fdid))[0]))!;
        return meta["particles"]!.AsArray();
    }

    [IntegrationFact]
    public async Task WarlordsShoulderGlowsAreExported()
    {
        // Warlord's Chain Shoulders / Mail Spaulders: one additive emitter on bone 1, texture 203839.
        var one = Assert.Single(await Particles(143212));
        Assert.Equal(203839u, one!["textureFileDataId"]!.GetValue<uint>());
        Assert.Equal(1, one["bone"]!.GetValue<int>());
        Assert.Equal(4, one["blendMode"]!.GetValue<int>());
        Assert.True(one["tracks"]!["lifespan"]!["keys"]![0]![1]!.GetValue<float>() > 0);
        Assert.True(one["tracks"]!["emissionRate"]!["keys"]![0]![1]!.GetValue<float>() > 0);

        // Dreadweave Mantle / Silk Amice: three emitters.
        var three = await Particles(143324);
        Assert.Equal(3, three.Count);
        Assert.Subset(new HashSet<uint> { 125011, 123938, 125010 }, three.Select(e => e!["textureFileDataId"]!.GetValue<uint>()).ToHashSet());
    }
}
