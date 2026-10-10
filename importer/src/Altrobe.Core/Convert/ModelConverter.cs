// Portions ported from wow.export (https://github.com/Kruithne/wow.export), MIT License, Copyright (c) Kruithne and Marlamin.
// Bone math follows src/js/3D/renderers/M2RendererGL.js (_update_bone_matrices) and the skin/animation
// layout follows src/js/3D/writers/GLTFWriter.js.
//
// Converts an M2 + its first .skin into a skinned binary glTF (.glb) plus a metadata JSON.
// Coordinate conversion follows wow.export's M2Loader: WoW (x, y, z) Z-up -> glTF (x, z, -y) Y-up.
// That mapping is a proper rotation, so triangle winding stays as stored.
//
// Bones: an M2 bone's model-space matrix is parent * T(pivot) * T(anim) * R(anim) * S(anim) * T(-pivot),
// and vertices are stored in bind pose, so that matrix is the skinning matrix directly. Each bone
// becomes a glTF node "bone_<index>" whose global transform is that matrix times T(pivot). Its local
// TRS is then translation = pivot - parentPivot + T(anim), rotation = R(anim), scale = S(anim), and its
// inverse bind matrix is T(-pivot).

using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Altrobe.Core.Convert;

static class ModelConverter
{
    public static Vector3 ToGltf(Vector3 v) => new(v.X, v.Z, -v.Y);

    public record Result(JsonObject Meta, byte[] Glb, uint[] HardcodedTextures);

    public static Result Convert(uint fdid, M2Model m2, uint skinFdid, M2Skin skin, Func<uint, byte[]> open)
    {
        var bin = new MemoryStream();
        var bufferViews = new JsonArray();
        var accessors = new JsonArray();

        int AddView(byte[] bytes, int? target)
        {
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            var view = new JsonObject { ["buffer"] = 0, ["byteOffset"] = bin.Length, ["byteLength"] = bytes.Length };
            if (target != null) view["target"] = target;
            bin.Write(bytes);
            bufferViews.Add(view);
            return bufferViews.Count - 1;
        }

        int AddAccessor(int view, int componentType, int count, string type, JsonArray? min = null, JsonArray? max = null)
        {
            var a = new JsonObject { ["bufferView"] = view, ["componentType"] = componentType, ["count"] = count, ["type"] = type };
            if (min != null) { a["min"] = min; a["max"] = max; }
            accessors.Add(a);
            return accessors.Count - 1;
        }

        // Shared vertex attributes for every primitive. Only the vertices this skin references are
        // written, in skin order, so triangle values (indices into skin.Indices) index them directly.
        // Body models carry far more M2 vertices than the first skin uses (917116: 288020 vs 46799).
        var n = skin.Indices.Length;
        var pos = new float[n * 3];
        var nor = new float[n * 3];
        var uv = new float[n * 2];
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        for (var i = 0; i < n; i++)
        {
            var v = m2.Vertices[skin.Indices[i]];
            var p = ToGltf(v.Position);
            var nn = ToGltf(v.Normal);
            // glTF requires unit-length normals. Zero normals get an arbitrary up vector.
            nn = nn.LengthSquared() > 1e-12f ? Vector3.Normalize(nn) : Vector3.UnitY;
            lo = Vector3.Min(lo, p);
            hi = Vector3.Max(hi, p);
            (pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]) = (p.X, p.Y, p.Z);
            (nor[i * 3], nor[i * 3 + 1], nor[i * 3 + 2]) = (nn.X, nn.Y, nn.Z);
            (uv[i * 2], uv[i * 2 + 1]) = (v.Uv0.X, v.Uv0.Y);
        }
        // Skinning: each M2 vertex has four bone indices and four uint8 weights. wow.export passes the
        // indices through as global bone indices (no remap through the skin's bone lookup), so this does too.
        var boneCount = m2.Bones.Length;
        if (boneCount == 0 || boneCount > 256) throw new InvalidDataException($"Unsupported bone count {boneCount}");
        var joints = new byte[n * 4];
        var weights = new byte[n * 4];
        var reweighted = 0;
        for (var i = 0; i < n; i++)
        {
            var v = m2.Vertices[skin.Indices[i]];
            var sum = 0;
            for (var k = 0; k < 4; k++)
            {
                var w = (byte)(v.BoneWeights >> (8 * k));
                var j = (byte)(v.BoneIndices >> (8 * k));
                if (w > 0 && j >= boneCount) throw new InvalidDataException($"Vertex {skin.Indices[i]} references bone {j} of {boneCount}");
                weights[i * 4 + k] = w;
                joints[i * 4 + k] = w > 0 ? j : (byte)0;
                sum += w;
            }
            if (sum == 255) continue;
            // glTF requires weights that sum to 1. Rescale, then give the rounding remainder to the largest.
            reweighted++;
            if (sum == 0) { weights[i * 4] = 255; joints[i * 4] = 0; continue; }
            var total = 0;
            for (var k = 0; k < 4; k++) total += weights[i * 4 + k] = (byte)(weights[i * 4 + k] * 255 / sum);
            var big = Enumerable.Range(0, 4).MaxBy(k => weights[i * 4 + k]);
            weights[i * 4 + big] += (byte)(255 - total);
        }

        const int ArrayBuffer = 34962, ElementArrayBuffer = 34963, Float = 5126, UShort = 5123, UByte = 5121;
        var posAcc = AddAccessor(AddView(Bytes(pos), ArrayBuffer), Float, n, "VEC3", [lo.X, lo.Y, lo.Z], [hi.X, hi.Y, hi.Z]);
        var norAcc = AddAccessor(AddView(Bytes(nor), ArrayBuffer), Float, n, "VEC3");
        var uvAcc = AddAccessor(AddView(Bytes(uv), ArrayBuffer), Float, n, "VEC2");
        var jointAcc = AddAccessor(AddView(joints, ArrayBuffer), UByte, n, "VEC4");
        var weightAcc = AddAccessor(AddView(weights, ArrayBuffer), UByte, n, "VEC4");
        accessors[weightAcc]!["normalized"] = true;

        var meshes = new JsonArray();
        var nodes = new JsonArray();
        var children = new JsonArray();
        var geosets = new JsonArray();

        for (var si = 0; si < skin.Submeshes.Length; si++)
        {
            var sm = skin.Submeshes[si];
            var idx = new ushort[sm.TriangleCount];
            Array.Copy(skin.Triangles, sm.TriangleStart, idx, 0, sm.TriangleCount);

            var units = TextureUnits(m2, skin, si);
            var name = $"geoset_{sm.SkinSectionId}";
            var extras = new JsonObject { ["geosetId"] = (int)sm.SkinSectionId, ["submeshIndex"] = si };

            if (idx.Length > 0)
            {
                var idxAcc = AddAccessor(AddView(Bytes(idx), ElementArrayBuffer), UShort, idx.Length, "SCALAR");
                meshes.Add(new JsonObject
                {
                    ["name"] = name,
                    ["primitives"] = new JsonArray(new JsonObject
                    {
                        ["attributes"] = new JsonObject { ["POSITION"] = posAcc, ["NORMAL"] = norAcc, ["TEXCOORD_0"] = uvAcc, ["JOINTS_0"] = jointAcc, ["WEIGHTS_0"] = weightAcc },
                        ["indices"] = idxAcc,
                        ["mode"] = 4,
                    }),
                });
                // Skinned mesh nodes sit at the scene root: glTF ignores their transform anyway.
                nodes.Add(new JsonObject { ["name"] = name, ["mesh"] = meshes.Count - 1, ["skin"] = 0, ["extras"] = extras });
                children.Add(nodes.Count - 1);
            }

            geosets.Add(new JsonObject
            {
                ["submeshIndex"] = si,
                ["geosetId"] = (int)sm.SkinSectionId,
                ["group"] = sm.SkinSectionId / 100,
                ["variant"] = sm.SkinSectionId % 100,
                ["triangles"] = sm.TriangleCount / 3,
                ["vertices"] = sm.VertexCount,
                ["textureUnits"] = units,
            });
        }

        // Bones: node "bone_<index>" for M2 bone <index>, so later steps can find them by name.
        var boneBase = nodes.Count;
        var bonesMeta = new JsonArray();
        var ibm = new float[boneCount * 16];
        for (var i = 0; i < boneCount; i++)
        {
            var b = m2.Bones[i];
            var p = ToGltf(b.Pivot);
            var pp = b.ParentBone >= 0 ? ToGltf(m2.Bones[b.ParentBone].Pivot) : Vector3.Zero;
            var t = p - pp;
            nodes.Add(new JsonObject
            {
                ["name"] = BoneName(i),
                ["translation"] = new JsonArray(t.X, t.Y, t.Z),
                ["extras"] = new JsonObject { ["bone"] = i, ["keyBoneId"] = b.KeyBoneId },
            });
            // Column-major T(-pivot).
            ibm[i * 16] = ibm[i * 16 + 5] = ibm[i * 16 + 10] = ibm[i * 16 + 15] = 1;
            (ibm[i * 16 + 12], ibm[i * 16 + 13], ibm[i * 16 + 14]) = (-p.X, -p.Y, -p.Z);
            bonesMeta.Add(new JsonObject
            {
                ["index"] = i,
                ["node"] = BoneName(i),
                ["keyBoneId"] = b.KeyBoneId,
                ["parent"] = b.ParentBone,
                ["flags"] = b.Flags,
                ["pivotGltf"] = new JsonArray(p.X, p.Y, p.Z),
            });
        }
        var skeletonRoots = new JsonArray();
        void AddChild(int parentNode, int child) => ((nodes[parentNode]!["children"] ??= new JsonArray()) as JsonArray)!.Add(child);
        for (var i = 0; i < boneCount; i++)
        {
            var parent = m2.Bones[i].ParentBone;
            if (parent >= 0 && parent < boneCount) AddChild(boneBase + parent, boneBase + i);
            else skeletonRoots.Add(boneBase + i);
        }

        // Attachment points are bone-relative. wow.export places one at boneMatrix * T(position), so as
        // a child of the bone node its offset is position - pivot (glTF axes). It follows the animation.
        var attachments = new JsonArray();
        foreach (var a in m2.Attachments)
        {
            var g = ToGltf(a.Position);
            var hasBone = a.Bone < boneCount;
            var offset = hasBone ? g - ToGltf(m2.Bones[a.Bone].Pivot) : g;
            nodes.Add(new JsonObject
            {
                ["name"] = $"attachment_{a.Id}",
                ["translation"] = new JsonArray(offset.X, offset.Y, offset.Z),
                ["extras"] = new JsonObject { ["attachmentId"] = a.Id, ["bone"] = a.Bone },
            });
            if (hasBone) AddChild(boneBase + a.Bone, nodes.Count - 1);
            else skeletonRoots.Add(nodes.Count - 1);
            attachments.Add(new JsonObject
            {
                ["id"] = a.Id,
                ["bone"] = a.Bone,
                ["boneNode"] = hasBone ? BoneName(a.Bone) : null,
                ["offsetGltf"] = new JsonArray(offset.X, offset.Y, offset.Z),
                ["position"] = new JsonArray(a.Position.X, a.Position.Y, a.Position.Z),
                ["positionGltf"] = new JsonArray(g.X, g.Y, g.Z),
            });
        }

        nodes.Add(new JsonObject { ["name"] = $"skeleton_{fdid}", ["children"] = skeletonRoots });
        var skeletonNode = nodes.Count - 1;
        children.Add(skeletonNode);
        var ibmAcc = AddAccessor(AddView(Bytes(ibm), null), Float, boneCount, "MAT4");
        var skins = new JsonArray(new JsonObject
        {
            ["name"] = $"m2_{fdid}_skin",
            ["joints"] = new JsonArray(Enumerable.Range(boneBase, boneCount).Select(x => (JsonNode)x).ToArray()),
            ["inverseBindMatrices"] = ibmAcc,
            ["skeleton"] = skeletonNode,
        });

        // Animations: Stand (ID 0), first variation only.
        var animations = new JsonArray();
        var animMeta = new JsonArray();
        foreach (var (animId, variation) in new[] { (0, 0) })
        {
            var seq = Array.FindIndex(m2.Sequences, q => q.Id == animId && q.Variation == variation);
            if (seq < 0) { animMeta.Add(new JsonObject { ["id"] = animId, ["variation"] = variation, ["error"] = "no such sequence" }); continue; }
            var r = ExportAnimation(m2, seq, open, AddView, AddAccessor, boneBase);
            if (r.anim != null) animations.Add(r.anim);
            animMeta.Add(r.meta);
        }
        // Global sequences loop on their own clock, whatever sequence is playing. Character models use
        // them for constant per-race tweaks, such as the scale and tilt of the shoulder attachment bones
        // (Orc male 1.7, Undead female 0.65), so each one becomes its own clip that plays alongside Stand.
        for (var gs = 0; gs < m2.GlobalSequences.Length; gs++)
        {
            var r = ExportAnimation(m2, -1, open, AddView, AddAccessor, boneBase, gs);
            if (r.anim != null) animations.Add(r.anim);
            if (r.anim != null || r.meta["skippedSplineTracks"]!.GetValue<int>() > 0) animMeta.Add(r.meta);
        }

        while (bin.Length % 4 != 0) bin.WriteByte(0);
        var gltf = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "altrobe probe convert-model" },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = children }),
            ["nodes"] = nodes,
            ["skins"] = skins,
            ["meshes"] = meshes,
            ["accessors"] = accessors,
            ["bufferViews"] = bufferViews,
            ["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = bin.Length }),
        };
        if (animations.Count > 0) gltf["animations"] = animations;

        var textures = new JsonArray();
        for (var t = 0; t < m2.Textures.Length; t++)
        {
            var tx = m2.Textures[t];
            textures.Add(new JsonObject { ["index"] = t, ["type"] = tx.Type, ["flags"] = tx.Flags, ["fileDataId"] = tx.Type == 0 ? tx.FileDataId : null });
        }

        var meta = new JsonObject
        {
            ["fileDataId"] = fdid,
            ["name"] = m2.Name,
            ["version"] = m2.Version,
            ["flags"] = m2.Flags,
            ["skinFileDataIds"] = new JsonArray(m2.SkinFileDataIds.Select(x => (JsonNode)x).ToArray()),
            ["skinUsed"] = skinFdid,
            ["skeletonFileDataId"] = m2.SkeletonFileDataId == 0 ? null : m2.SkeletonFileDataId,
            ["vertexCount"] = n,
            ["m2VertexCount"] = m2.Vertices.Length,
            ["boneCount"] = m2.Bones.Length,
            ["reweightedVertices"] = reweighted,
            ["sequenceCount"] = m2.Sequences.Length,
            ["sequenceIds"] = new JsonArray(m2.Sequences.Select(q => (int)q.Id).Distinct().Order().Select(x => (JsonNode)x).ToArray()),
            ["standVariations"] = new JsonArray(m2.Sequences.Select((q, i) => (q, i)).Where(x => x.q.Id == 0)
                .Select(x => (JsonNode)new JsonObject { ["variation"] = x.q.Variation, ["sequenceIndex"] = x.i, ["durationMs"] = x.q.Duration, ["flags"] = x.q.Flags }).ToArray()),
            ["animFiles"] = AnimFileSummary(m2, open),
            ["animations"] = animMeta,
            ["bones"] = bonesMeta,
            ["coordinateSystem"] = "glTF Y-up: (x, y, z)_wow -> (x, z, -y)",
            ["textures"] = textures,
            ["materials"] = new JsonArray(m2.Materials.Select((x, i) => (JsonNode)new JsonObject { ["index"] = i, ["flags"] = x.Flags, ["blendMode"] = x.BlendMode }).ToArray()),
            ["geosets"] = geosets,
            ["attachments"] = attachments,
            ["particles"] = M2Particles.Export(m2, open),
        };

        var hardcoded = m2.Textures.Where(t => t.Type == 0 && t.FileDataId != 0).Select(t => t.FileDataId).Distinct().ToArray();
        return new Result(meta, Glb(gltf, bin.ToArray()), hardcoded);
    }

    public static string BoneName(int i) => $"bone_{i}";

    // One animation (first variation of `animId`) for a model's bones, with no meshes: the viewer plays it
    // on the body it already loaded, whose bone nodes have the same names. Null when the model has no
    // such sequence or the sequence has no channels.
    public static byte[]? ConvertAnimation(M2Model m2, int animId, Func<uint, byte[]> open, out JsonObject meta)
    {
        var seq = Array.FindIndex(m2.Sequences, q => q.Id == animId && q.Variation == 0);
        if (seq < 0) seq = Array.FindIndex(m2.Sequences, q => q.Id == animId);
        if (seq < 0)
        {
            meta = new JsonObject { ["id"] = animId, ["error"] = "no such sequence" };
            return null;
        }
        var bin = new MemoryStream();
        var bufferViews = new JsonArray();
        var accessors = new JsonArray();
        int AddView(byte[] bytes, int? target)
        {
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            bufferViews.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = bin.Length, ["byteLength"] = bytes.Length });
            bin.Write(bytes);
            return bufferViews.Count - 1;
        }
        int AddAccessor(int view, int componentType, int count, string type, JsonArray? min = null, JsonArray? max = null)
        {
            var a = new JsonObject { ["bufferView"] = view, ["componentType"] = componentType, ["count"] = count, ["type"] = type };
            if (min != null) { a["min"] = min; a["max"] = max; }
            accessors.Add(a);
            return accessors.Count - 1;
        }

        var (anim, m) = ExportAnimation(m2, seq, open, AddView, AddAccessor, 0);
        meta = m;
        if (anim == null) return null;
        // Bone nodes carry only names and hierarchy: the clip's keys set every animated value.
        var nodes = new JsonArray();
        var roots = new JsonArray();
        for (var i = 0; i < m2.Bones.Length; i++) nodes.Add(new JsonObject { ["name"] = BoneName(i) });
        for (var i = 0; i < m2.Bones.Length; i++)
        {
            var parent = m2.Bones[i].ParentBone;
            if (parent >= 0 && parent < m2.Bones.Length) ((nodes[parent]!["children"] ??= new JsonArray()) as JsonArray)!.Add(i);
            else roots.Add(i);
        }
        while (bin.Length % 4 != 0) bin.WriteByte(0);
        var gltf = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "altrobe convert-animation" },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = roots }),
            ["nodes"] = nodes,
            ["animations"] = new JsonArray(anim),
            ["accessors"] = accessors,
            ["bufferViews"] = bufferViews,
            ["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = bin.Length }),
        };
        return Glb(gltf, bin.ToArray());
    }

    // One glTF animation for sequence `seq`, or, when `globalSeq` >= 0, for the tracks tied to that global
    // sequence. A sequence clip skips global-sequence tracks (they go in their own clips). Spline
    // (bezier/hermite) tracks are skipped, as in wow.export's glTF writer; the counts go in the metadata.
    // Global-sequence tracks keep their keys at index 0 of the track and their data in the M2 itself
    // (https://wowdev.wiki/M2#Global_sequences).
    static (JsonObject? anim, JsonObject meta) ExportAnimation(M2Model m2, int seq, Func<uint, byte[]> open,
        Func<byte[], int?, int> addView, Func<int, int, int, string, JsonArray?, JsonArray?, int> addAccessor, int boneBase,
        int globalSeq = -1)
    {
        const int Float = 5126;
        var isGlobal = globalSeq >= 0;
        var duration = isGlobal ? m2.GlobalSequences[globalSeq] : m2.Sequences[seq].Duration;
        var keyIndex = isGlobal ? 0 : seq;
        JsonObject meta;
        byte[] data;
        int baseOfs;
        if (isGlobal)
        {
            meta = new JsonObject { ["globalSequence"] = globalSeq, ["durationMs"] = duration, ["source"] = "in-file" };
            (data, baseOfs) = m2.InFileData;
        }
        else
        {
            var q0 = m2.Sequences[seq];
            meta = new JsonObject { ["id"] = q0.Id, ["variation"] = q0.Variation, ["sequenceIndex"] = seq, ["durationMs"] = duration, ["flags"] = q0.Flags };
            var src = M2AnimSource.Resolve(m2, seq, open);
            if (src == null) { meta["error"] = "no track data (not in file, no AFID entry)"; return (null, meta); }
            (data, baseOfs, var source) = src.Value;
            meta["source"] = source;
        }

        var samplers = new JsonArray();
        var channels = new JsonArray();
        int globalSkipped = 0, splineSkipped = 0, emptyOrBad = 0;
        var counts = new Dictionary<string, int> { ["translation"] = 0, ["rotation"] = 0, ["scale"] = 0 };

        void Channel(int bone, string path, uint[] times, float[][] values)
        {
            // Match wow.export: wrap timestamps into [0, duration], keep an end-of-loop key, sort, and drop
            // repeated times (glTF sampler input must be strictly increasing).
            var keys = times.Select((t, j) => (t: duration > 0 ? (t % duration == 0 && t > 0 ? duration : t % duration) : t, v: values[j]))
                .OrderBy(k => k.t).ToList();
            keys = keys.Where((k, j) => j == 0 || k.t != keys[j - 1].t).ToList();
            var input = keys.Select(k => k.t / 1000f).ToArray();
            var output = keys.SelectMany(k => k.v).ToArray();
            var inAcc = addAccessor(addView(Bytes(input), null), Float, input.Length, "SCALAR", [input[0]], [input[^1]]);
            var outAcc = addAccessor(addView(Bytes(output), null), Float, keys.Count, values[0].Length == 4 ? "VEC4" : "VEC3", null, null);
            var track = path switch { "rotation" => m2.Bones[bone].Rotation, "scale" => m2.Bones[bone].Scale, _ => m2.Bones[bone].Translation };
            var interp = track.Interpolation;
            samplers.Add(new JsonObject { ["input"] = inAcc, ["output"] = outAcc, ["interpolation"] = interp == 0 ? "STEP" : "LINEAR" });
            channels.Add(new JsonObject { ["sampler"] = samplers.Count - 1, ["target"] = new JsonObject { ["node"] = boneBase + bone, ["path"] = path } });
            counts[path]++;
        }

        bool Usable(M2Track t)
        {
            // Check the global sequence first: a global track has one timeline (index 0), so a bounds
            // check on `seq` first would drop these tracks from the count for any Stand not at index 0.
            if (isGlobal ? t.GlobalSeq != globalSeq : t.GlobalSeq >= 0) { if (!isGlobal) globalSkipped++; return false; }
            if (keyIndex >= t.Times.Length || t.Times[keyIndex].count == 0) return false;
            if (t.Interpolation >= 2) { splineSkipped++; return false; }
            return true;
        }

        for (var i = 0; i < m2.Bones.Length; i++)
        {
            var b = m2.Bones[i];
            var p = ToGltf(b.Pivot);
            var pp = b.ParentBone >= 0 ? ToGltf(m2.Bones[b.ParentBone].Pivot) : Vector3.Zero;
            if (Usable(b.Translation))
            {
                var k = b.Translation.Keys(keyIndex, data, baseOfs, 12, (d, at) => M2Bin.V3(d, at));
                if (k == null) emptyOrBad++;
                else Channel(i, "translation", k.Value.times, k.Value.values.Select(v => { var g = ToGltf(v) + p - pp; return new[] { g.X, g.Y, g.Z }; }).ToArray());
            }
            if (Usable(b.Rotation))
            {
                var k = b.Rotation.Keys(keyIndex, data, baseOfs, 8, M2Bone.ReadCompQuat);
                if (k == null) emptyOrBad++;
                else Channel(i, "rotation", k.Value.times, k.Value.values.Select(v =>
                {
                    // Same axis swap as positions (wow.export: x, z, -y, w), then normalise for glTF.
                    var g = new Quaternion(v.X, v.Z, -v.Y, v.W);
                    g = g.LengthSquared() > 1e-12f ? Quaternion.Normalize(g) : Quaternion.Identity;
                    return new[] { g.X, g.Y, g.Z, g.W };
                }).ToArray());
            }
            if (Usable(b.Scale))
            {
                var k = b.Scale.Keys(keyIndex, data, baseOfs, 12, (d, at) => M2Bin.V3(d, at));
                if (k == null) emptyOrBad++;
                else Channel(i, "scale", k.Value.times, k.Value.values.Select(v => new[] { v.X, v.Z, v.Y }).ToArray());
            }
        }

        meta["channels"] = new JsonObject { ["translation"] = counts["translation"], ["rotation"] = counts["rotation"], ["scale"] = counts["scale"] };
        if (!isGlobal) meta["skippedGlobalSequenceTracks"] = globalSkipped;
        meta["skippedSplineTracks"] = splineSkipped;
        meta["unreadableTracks"] = emptyOrBad;
        if (channels.Count == 0) { meta["error"] = "no channels"; return (null, meta); }
        if (isGlobal)
        {
            var gname = $"Global_{globalSeq}";
            meta["name"] = gname;
            return (new JsonObject { ["name"] = gname, ["samplers"] = samplers, ["channels"] = channels,
                ["extras"] = new JsonObject { ["globalSequence"] = globalSeq, ["durationMs"] = duration } }, meta);
        }
        var q = m2.Sequences[seq];
        var name = q.Id == 0 ? $"Stand_{q.Variation}" : $"anim_{q.Id}_{q.Variation}";
        meta["name"] = name;
        return (new JsonObject { ["name"] = name, ["samplers"] = samplers, ["channels"] = channels,
            ["extras"] = new JsonObject { ["animId"] = q.Id, ["variation"] = q.Variation, ["durationMs"] = q.Duration } }, meta);
    }

    // Which AFID-referenced .anim files exist in local storage. Missing ones are listed, not fetched.
    static JsonObject AnimFileSummary(M2Model m2, Func<uint, byte[]> open)
    {
        var missing = new JsonArray();
        var present = 0;
        foreach (var a in m2.AnimFileIds.Where(a => a.FileDataId != 0))
        {
            try { open(a.FileDataId); present++; }
            catch (FileNotFoundException) { missing.Add(new JsonObject { ["animId"] = a.AnimId, ["subAnimId"] = a.SubAnimId, ["fileDataId"] = a.FileDataId }); }
        }
        return new JsonObject { ["afidEntries"] = m2.AnimFileIds.Length, ["present"] = present, ["missing"] = missing };
    }

    // Texture units (skin batches) that draw submesh `si`, resolved through the texture combo table.
    static JsonArray TextureUnits(M2Model m2, M2Skin skin, int si)
    {
        var list = new JsonArray();
        foreach (var b in skin.Batches.Where(b => b.SkinSectionIndex == si))
        {
            var layers = new JsonArray();
            for (var k = 0; k < Math.Max((int)b.TextureCount, 1); k++)
            {
                var combo = b.TextureComboIndex + k;
                if (combo >= m2.TextureCombos.Length) break;
                var ti = m2.TextureCombos[combo];
                if (ti >= m2.Textures.Length) { layers.Add(new JsonObject { ["textureIndex"] = ti, ["error"] = "out of range" }); continue; }
                var tx = m2.Textures[ti];
                layers.Add(new JsonObject { ["textureIndex"] = ti, ["type"] = tx.Type, ["fileDataId"] = tx.Type == 0 ? tx.FileDataId : null });
            }
            var mat = b.MaterialIndex < m2.Materials.Length ? m2.Materials[b.MaterialIndex] : null;
            list.Add(new JsonObject
            {
                ["priority"] = b.Priority,
                ["shaderId"] = b.ShaderId,
                ["materialIndex"] = b.MaterialIndex,
                ["blendMode"] = mat?.BlendMode,
                ["materialFlags"] = mat?.Flags,
                ["materialLayer"] = b.MaterialLayer,
                ["textures"] = layers,
            });
        }
        return list;
    }

    static byte[] Bytes<T>(T[] a) where T : struct => System.Runtime.InteropServices.MemoryMarshal.AsBytes(a.AsSpan()).ToArray();

    // GLB container: header, JSON chunk (space-padded), BIN chunk (zero-padded). https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#binary-gltf-layout
    static byte[] Glb(JsonObject gltf, byte[] bin)
    {
        var json = Encoding.UTF8.GetBytes(gltf.ToJsonString());
        var jsonPad = (4 - json.Length % 4) % 4;
        var total = 12 + 8 + json.Length + jsonPad + 8 + bin.Length;
        using var ms = new MemoryStream(total);
        using var w = new BinaryWriter(ms);
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)total);
        w.Write((uint)(json.Length + jsonPad)); w.Write(0x4E4F534Au); w.Write(json); for (var i = 0; i < jsonPad; i++) w.Write((byte)0x20);
        w.Write((uint)bin.Length); w.Write(0x004E4942u); w.Write(bin);
        return ms.ToArray();
    }
}
