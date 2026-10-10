// M2 particle emitters, exported into the model metadata for the viewer to simulate. Record layout
// from https://wowdev.wiki/M2 (Particle emitters: M2ParticleOld, M2Particle, The Fake-AnimationBlock,
// Compressed Particle Gravity). wow.export has no particle support.
//
// Animated values (speed, rate, lifespan, ...) are M2Tracks, sampled like bone tracks: the Stand
// sequence's keys, or a global sequence's. Lifetime curves (color, alpha, scale, texture cell) are
// fake animation blocks: int16 times from 0 to 32767 across a particle's life, values in the M2.
// Positions and vectors are converted to glTF axes; generator angles stay in WoW's emitter frame
// and the viewer converts the vectors it generates.

using System.Numerics;
using System.Text.Json.Nodes;

namespace Altrobe.Core.Convert;

static class M2Particles
{
    const uint MultiTexture = 0x10000000, CompressedGravity = 0x800000;

    public static JsonArray Export(M2Model m2, Func<uint, byte[]> open)
    {
        var list = new JsonArray();
        if (m2.ParticleCount == 0) return list;
        var (data, md20) = m2.InFileData;
        ReadOnlySpan<byte> d = data.AsSpan(md20);

        var stand = Array.FindIndex(m2.Sequences, q => q.Id == 0 && q.Variation == 0);
        (byte[] data, int baseOfs, string source)? standSource = stand >= 0 ? M2AnimSource.Resolve(m2, stand, open) : null;

        for (var i = 0; i < m2.ParticleCount; i++)
        {
            var p = m2.ParticleOfs + i * m2.ParticleStride;
            if (p + m2.ParticleStride > d.Length) break;
            var flags = M2Bin.U32(d, p + 0x04);
            var bone = M2Bin.U16(d, p + 0x14);
            var texId = M2Bin.U16(d, p + 0x16);
            // Multi-textured emitters pack three 5-bit texture indices; the first is the base texture.
            var texIndex = (flags & MultiTexture) != 0 ? texId & 0x1F : texId;
            var tex = texIndex < m2.Textures.Length ? m2.Textures[texIndex] : null;
            var g = ModelConverter.ToGltf(M2Bin.V3(d, p + 0x08));
            var hasBone = bone < m2.Bones.Length;
            var offset = hasBone ? g - ModelConverter.ToGltf(m2.Bones[bone].Pivot) : g;

            JsonObject? Track(int at, Func<ReadOnlySpan<byte>, int, float[]> read, int size)
            {
                var t = M2Track.Read(data, md20 + p + at, md20);
                (uint[] times, float[][] values)? keys;
                uint duration;
                if (t.GlobalSeq >= 0)
                {
                    if (t.GlobalSeq >= m2.GlobalSequences.Length) return null;
                    keys = t.Keys(0, data, md20, size, (s, o) => read(s, o));
                    duration = m2.GlobalSequences[t.GlobalSeq];
                }
                else
                {
                    if (stand < 0 || standSource == null) return null;
                    keys = t.Keys(stand, standSource.Value.data, standSource.Value.baseOfs, size, (s, o) => read(s, o));
                    duration = m2.Sequences[stand].Duration;
                }
                if (keys == null) return null;
                var (times, values) = keys.Value;
                return new JsonObject
                {
                    ["durationMs"] = duration,
                    ["interpolation"] = t.Interpolation,
                    ["globalSequence"] = t.GlobalSeq >= 0 ? t.GlobalSeq : null,
                    ["keys"] = new JsonArray(times.Select((time, j) => (JsonNode)new JsonArray([(JsonNode)time, .. values[j].Select(v => (JsonNode)v)])).ToArray()),
                };
            }
            JsonObject? Float(int at) => Track(at, (s, o) => [M2Bin.F32(s, o)], 4);

            var tracks = new JsonObject();
            void Add(string name, JsonObject? track) { if (track != null) tracks[name] = track; }
            Add("emissionSpeed", Float(0x34));
            Add("speedVariation", Float(0x48));
            Add("verticalRange", Float(0x5C));
            Add("horizontalRange", Float(0x70));
            Add("gravity", Track(0x84, (s, o) => Gravity(s, o, flags), 4));
            Add("lifespan", Float(0x98));
            Add("emissionRate", Float(0xB0));
            // wowdev calls these emissionAreaWidth and emissionAreaLength: the plane's extent along the
            // emitter's x and y, or a sphere's radius bounds.
            Add("areaX", Float(0xC8));
            Add("areaY", Float(0xDC));
            Add("zSource", Float(0xF0));

            list.Add(new JsonObject
            {
                ["index"] = i,
                ["flags"] = flags,
                ["bone"] = bone,
                ["boneNode"] = hasBone ? ModelConverter.BoneName(bone) : null,
                ["offsetGltf"] = new JsonArray(offset.X, offset.Y, offset.Z),
                ["textureIndex"] = texIndex,
                ["textureType"] = tex?.Type,
                ["textureFileDataId"] = tex is { Type: 0, FileDataId: > 0 } ? tex.FileDataId : null,
                ["blendMode"] = d[p + 0x28],
                ["emitterType"] = d[p + 0x29],
                ["particleColorIndex"] = M2Bin.U16(d, p + 0x2A),
                ["rows"] = Math.Max((int)M2Bin.U16(d, p + 0x30), 1),
                ["columns"] = Math.Max((int)M2Bin.U16(d, p + 0x32), 1),
                ["tracks"] = tracks,
                ["lifespanVariation"] = M2Bin.F32(d, p + 0xAC),
                ["emissionRateVariation"] = M2Bin.F32(d, p + 0xC4),
                ["color"] = Curve(d, p + 0x104, 12, (s, o) => [M2Bin.F32(s, o) / 255f, M2Bin.F32(s, o + 4) / 255f, M2Bin.F32(s, o + 8) / 255f]),
                ["alpha"] = Curve(d, p + 0x114, 2, (s, o) => [M2Bin.I16(s, o) / 32767f], scalar: true),
                ["scale"] = Curve(d, p + 0x124, 8, (s, o) => [M2Bin.F32(s, o), M2Bin.F32(s, o + 4)]),
                ["scaleVariation"] = new JsonArray(M2Bin.F32(d, p + 0x134), M2Bin.F32(d, p + 0x138)),
                ["headCell"] = Curve(d, p + 0x13C, 2, (s, o) => [M2Bin.U16(s, o)], scalar: true),
                ["tailCell"] = Curve(d, p + 0x14C, 2, (s, o) => [M2Bin.U16(s, o)], scalar: true),
                ["tailLength"] = M2Bin.F32(d, p + 0x15C),
                ["twinkleSpeed"] = M2Bin.F32(d, p + 0x160),
                ["twinklePercent"] = M2Bin.F32(d, p + 0x164),
                ["drag"] = M2Bin.F32(d, p + 0x174),
                ["baseSpin"] = M2Bin.F32(d, p + 0x178),
                ["baseSpinVariation"] = M2Bin.F32(d, p + 0x17C),
                ["spin"] = M2Bin.F32(d, p + 0x180),
                ["spinVariation"] = M2Bin.F32(d, p + 0x184),
            });
        }
        return list;
    }

    // Gravity in glTF axes. A plain float pulls along WoW -Z. With flag 0x800000 the 4 bytes are a
    // compressed vector: int8 x, int8 y (a unit direction's xy / 128), int16 z (signed magnitude).
    static float[] Gravity(ReadOnlySpan<byte> d, int at, uint flags)
    {
        Vector3 v;
        if ((flags & CompressedGravity) != 0)
        {
            var dir = new Vector3((sbyte)d[at], (sbyte)d[at + 1], 0) / 128f;
            var z = MathF.Sqrt(MathF.Max(0, 1 - Vector3.Dot(dir, dir)));
            var mag = M2Bin.I16(d, at + 2) * 0.04238648f;
            if (mag < 0) { z = -z; mag = -mag; }
            v = new Vector3(dir.X, dir.Y, z) * mag;
        }
        else v = new Vector3(0, 0, -M2Bin.F32(d, at));
        var g = ModelConverter.ToGltf(v);
        return [g.X, g.Y, g.Z];
    }

    // A fake animation block: int16 times (fractions of the particle's life) and values, both by offset
    // into the MD20 body. Null when empty or out of bounds.
    static JsonObject? Curve(ReadOnlySpan<byte> d, int at, int size, Func<ReadOnlySpan<byte>, int, float[]> read, bool scalar = false)
    {
        var (tn, to) = ((int)M2Bin.U32(d, at), (int)M2Bin.U32(d, at + 4));
        var (vn, vo) = ((int)M2Bin.U32(d, at + 8), (int)M2Bin.U32(d, at + 12));
        if (tn == 0 || vn != tn || to + tn * 2 > d.Length || vo + vn * size > d.Length) return null;
        var times = new JsonArray();
        var values = new JsonArray();
        for (var j = 0; j < tn; j++)
        {
            times.Add(M2Bin.I16(d, to + j * 2) / 32767f);
            var v = read(d, vo + j * size);
            values.Add(scalar ? v[0] : new JsonArray(v.Select(x => (JsonNode)x).ToArray()));
        }
        return new JsonObject { ["times"] = times, ["values"] = values };
    }
}
