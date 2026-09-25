// Converts an M2 + its first .skin into a static binary glTF (.glb) plus a metadata JSON.
// Coordinate conversion follows wow.export's M2Loader: WoW (x, y, z) Z-up -> glTF (x, z, -y) Y-up.
// That mapping is a proper rotation, so triangle winding stays as stored.

using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Altrobe.Convert;

static class ModelConverter
{
    public static Vector3 ToGltf(Vector3 v) => new(v.X, v.Z, -v.Y);

    public record Result(JsonObject Meta, byte[] Glb, uint[] HardcodedTextures);

    public static Result Convert(uint fdid, M2Model m2, uint skinFdid, M2Skin skin)
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
        const int ArrayBuffer = 34962, ElementArrayBuffer = 34963, Float = 5126, UShort = 5123;
        var posAcc = AddAccessor(AddView(Bytes(pos), ArrayBuffer), Float, n, "VEC3", [lo.X, lo.Y, lo.Z], [hi.X, hi.Y, hi.Z]);
        var norAcc = AddAccessor(AddView(Bytes(nor), ArrayBuffer), Float, n, "VEC3");
        var uvAcc = AddAccessor(AddView(Bytes(uv), ArrayBuffer), Float, n, "VEC2");

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
                        ["attributes"] = new JsonObject { ["POSITION"] = posAcc, ["NORMAL"] = norAcc, ["TEXCOORD_0"] = uvAcc },
                        ["indices"] = idxAcc,
                        ["mode"] = 4,
                    }),
                });
                nodes.Add(new JsonObject { ["name"] = name, ["mesh"] = meshes.Count - 1, ["extras"] = extras });
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

        // Attachment points become empty nodes so a viewer can parent items to them later.
        var attachments = new JsonArray();
        foreach (var a in m2.Attachments)
        {
            var g = ToGltf(a.Position);
            var pivot = a.Bone < m2.Bones.Length ? m2.Bones[a.Bone].Pivot : (Vector3?)null;
            nodes.Add(new JsonObject
            {
                ["name"] = $"attachment_{a.Id}",
                ["translation"] = new JsonArray(g.X, g.Y, g.Z),
                ["extras"] = new JsonObject { ["attachmentId"] = a.Id, ["bone"] = a.Bone },
            });
            children.Add(nodes.Count - 1);
            attachments.Add(new JsonObject
            {
                ["id"] = a.Id,
                ["bone"] = a.Bone,
                ["position"] = new JsonArray(a.Position.X, a.Position.Y, a.Position.Z),
                ["positionGltf"] = new JsonArray(g.X, g.Y, g.Z),
                ["bonePivot"] = pivot is { } pv ? new JsonArray(pv.X, pv.Y, pv.Z) : null,
            });
        }

        nodes.Add(new JsonObject { ["name"] = $"m2_{fdid}", ["children"] = children });
        var root = nodes.Count - 1;

        while (bin.Length % 4 != 0) bin.WriteByte(0);
        var gltf = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "altrobe probe convert-model" },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(root) }),
            ["nodes"] = nodes,
            ["meshes"] = meshes,
            ["accessors"] = accessors,
            ["bufferViews"] = bufferViews,
            ["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = bin.Length }),
        };

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
            ["coordinateSystem"] = "glTF Y-up: (x, y, z)_wow -> (x, z, -y)",
            ["textures"] = textures,
            ["geosets"] = geosets,
            ["attachments"] = attachments,
        };

        var hardcoded = m2.Textures.Where(t => t.Type == 0 && t.FileDataId != 0).Select(t => t.FileDataId).Distinct().ToArray();
        return new Result(meta, Glb(gltf, bin.ToArray()), hardcoded);
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
            list.Add(new JsonObject
            {
                ["priority"] = b.Priority,
                ["shaderId"] = b.ShaderId,
                ["materialIndex"] = b.MaterialIndex,
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
