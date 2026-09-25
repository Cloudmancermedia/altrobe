// Probe modes that convert local game files into web-friendly formats under output/.
// All reads go through the `open` delegate, which Program.cs binds to LocalFiles.Open (local disk only).

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Altrobe.Convert;

static class ConvertCommands
{
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // Returns the hardcoded (TXID) texture IDs the model references, or null on failure.
    public static uint[]? Model(Func<uint, byte[]> open, string outputDir, uint fdid)
    {
        try
        {
            var m2 = M2Model.Parse(open(fdid));
            if (m2.SkinFileDataIds.Length == 0) throw new InvalidDataException("No skin FileDataIDs in SFID chunk");
            var skinFdid = m2.SkinFileDataIds[0];
            var skin = M2Skin.Parse(open(skinFdid));
            var r = ModelConverter.Convert(fdid, m2, skinFdid, skin, open);

            var dir = Path.Combine(outputDir, "models");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"{fdid}.glb"), r.Glb);
            File.WriteAllText(Path.Combine(dir, $"{fdid}.json"), r.Meta.ToJsonString(Indented));
            var ids = r.Meta["geosets"]!.AsArray().Select(g => (int)g!["geosetId"]!).ToList();
            Console.WriteLine($"model {fdid}: '{m2.Name}' skin {skinFdid}, {m2.Vertices.Length} verts, {skin.Submeshes.Length} submeshes [{string.Join(",", ids)}], {m2.Attachments.Length} attachments, {m2.Textures.Length} textures, {m2.Bones.Length} bones");
            foreach (var a in r.Meta["animations"]!.AsArray()) Console.WriteLine($"  animation: {a!.ToJsonString()}");
            return r.HardcodedTextures;
        }
        catch (Exception e)
        {
            Console.WriteLine($"model {fdid}: FAILED ({e.GetType().Name}: {e.Message})");
            return null;
        }
    }

    // Diagnostic: where do bones and sequences live for an M2 and an optional .skel FileDataID.
    public static void AnimInfo(Func<uint, byte[]> open, uint fdid, uint skelFdid)
    {
        var m2 = M2Model.Parse(open(fdid));
        Console.WriteLine($"m2 {fdid}: chunks {string.Join(" ", m2.Chunks)} flags 0x{m2.Flags:X}");
        Describe("m2", m2);
        var next = skelFdid != 0 ? skelFdid : m2.SkeletonFileDataId;
        while (next != 0)
        {
            var skel = M2Skel.Parse(open(next));
            Console.WriteLine($"skel {next}: chunks {string.Join(" ", skel.Chunks)} parent {skel.ParentSkelFileDataId} attachments {skel.Attachments.Length}");
            Describe($"skel {next}", skel);
            next = skel.ParentSkelFileDataId;
        }

        void Describe(string label, IM2Skeleton s)
        {
            Console.WriteLine($"  {label}: {s.Bones.Length} bones, {s.Sequences.Length} sequences, {s.AnimFileIds.Length} AFID entries, chunkedAnims={s.ChunkedAnims}");
            for (var i = 0; i < s.Sequences.Length; i++)
            {
                var q = s.Sequences[i];
                if (q.Id != 0) continue;
                var afid = s.AnimFileIds.Where(a => a.AnimId == q.Id && a.SubAnimId == q.Variation).Select(a => a.FileDataId).ToList();
                var withKeys = s.Bones.Count(b => b.Rotation.Times.Length > i && b.Rotation.Times[i].count > 0);
                Console.WriteLine($"    seq[{i}] id {q.Id} var {q.Variation} dur {q.Duration}ms flags 0x{q.Flags:X} inFile={q.DataInFile} alias={q.IsAlias} next={q.VariationNext} afid=[{string.Join(",", afid)}] bonesWithRotKeys={withKeys}");
            }
            if (s.Bones.Length > 0)
            {
                var b0 = s.Bones[0];
                Console.WriteLine($"    bone0 keyBone {b0.KeyBoneId} parent {b0.ParentBone} rotTracks {b0.Rotation.Times.Length} pivot {b0.Pivot}");
            }
        }
    }

    public static bool Texture(Func<uint, byte[]> open, string outputDir, uint fdid)
    {
        try
        {
            var img = Blp.Decode(open(fdid));
            var dir = Path.Combine(outputDir, "textures");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"{fdid}.png"), Png.Encode(img.Width, img.Height, img.Rgba));
            Console.WriteLine($"texture {fdid}: {img.Width}x{img.Height} {img.Format}");
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"texture {fdid}: FAILED ({e.GetType().Name}: {e.Message})");
            return false;
        }
    }

    // Converts every model and texture named in output/resolved/*/*.json, plus the textures
    // hardcoded in those models. Returns the number of failures.
    public static int All(Func<uint, byte[]> open, string outputDir, IEnumerable<uint> extraModels)
    {
        var models = new SortedSet<uint>(extraModels);
        var textures = new SortedSet<uint>();
        foreach (var file in Directory.GetFiles(Path.Combine(outputDir, "resolved"), "*.json", SearchOption.AllDirectories))
        {
            var doc = JsonNode.Parse(File.ReadAllText(file))!;
            if (doc["modelFileDataId"] is { } body) models.Add((uint)body);
            foreach (var slot in doc["models"]?.AsArray() ?? [])
            {
                foreach (var m in slot!["models"]!.AsArray()) models.Add((uint)m!["fileDataId"]!);
                foreach (var t in slot["textures"]!.AsArray()) textures.Add((uint)t!["fileDataId"]!);
            }
            foreach (var section in doc["bodyTextures"]?.AsArray() ?? [])
                foreach (var t in section!["textures"]!.AsArray()) textures.Add((uint)t!["fileDataId"]!);
        }

        var failures = 0;
        foreach (var m in models)
        {
            var hardcoded = Model(open, outputDir, m);
            if (hardcoded == null) failures++;
            else textures.UnionWith(hardcoded);
        }
        foreach (var t in textures)
            if (!Texture(open, outputDir, t)) failures++;
        Console.WriteLine($"convert-all: {models.Count} models, {textures.Count} textures, {failures} failures");
        return failures;
    }
}
