using System.Text.Json.Nodes;

namespace Altrobe.Core.Convert;

// Public entry points over the ported converter. `open` reads a FileDataID from local storage and
// is also used for the skin, skeleton and .anim files a model references.
public static class AssetConverter
{
    // Bump when a change alters files the converter already wrote (a model, texture or animation
    // converts differently). Converted files are cached per version and served at URLs that carry
    // it, so a bump reconverts on next view and gets past the browser's year-long cache. New kinds
    // of output don't need a bump.
    // 2: model metadata gains particle emitters (shoulder and weapon glows).
    public const int OutputVersion = 2;

    public sealed record ConvertedModel(byte[] Glb, byte[] MetaJson, IReadOnlyList<uint> HardcodedTextures);

    public static ConvertedModel ConvertModel(uint fdid, Func<uint, byte[]> open)
    {
        var m2 = M2Model.Parse(open(fdid));
        if (m2.SkinFileDataIds.Length == 0) throw new InvalidDataException($"Model {fdid} has no skin FileDataIDs in its SFID chunk");
        var skinFdid = m2.SkinFileDataIds[0];
        var skin = M2Skin.Parse(open(skinFdid));
        var r = ModelConverter.Convert(fdid, m2, skinFdid, skin, open);
        return new ConvertedModel(r.Glb, System.Text.Encoding.UTF8.GetBytes(r.Meta.ToJsonString()), r.HardcodedTextures);
    }

    // Throws FileNotFoundException when the model has no such animation.
    public static byte[] ConvertAnimation(uint fdid, int animId, Func<uint, byte[]> open) =>
        ModelConverter.ConvertAnimation(M2Model.Parse(open(fdid)), animId, open, out var meta)
            ?? throw new FileNotFoundException($"Model {fdid} has no animation {animId} ({AnimationNames.Name(animId)}): {meta["error"]}");

    public static byte[] ConvertTexture(byte[] blp)
    {
        var img = Blp.Decode(blp);
        return Png.Encode(img.Width, img.Height, img.Rgba);
    }

    // Distinct geoset IDs of the mesh, from a model's metadata JSON.
    // Animation IDs the model has sequences for; empty for metadata written before they were recorded.
    public static IReadOnlyList<int> SequenceIds(byte[] metaJson) =>
        JsonNode.Parse(metaJson)!["sequenceIds"]?.AsArray().Select(x => (int)x!).ToList() ?? [];

    public static IReadOnlyList<int> GeosetIds(byte[] metaJson)
    {
        var meta = JsonNode.Parse(metaJson)!;
        return meta["geosets"]!.AsArray().Select(g => (int)g!["geosetId"]!).Distinct().Order().ToList();
    }

    // Distinct FileDataIDs of the model's hard-coded (type 0) textures, from its metadata JSON. The
    // hosted bake converts these alongside the model.
    public static IReadOnlyList<int> TextureIds(byte[] metaJson)
    {
        var meta = JsonNode.Parse(metaJson)!;
        return (meta["textures"]?.AsArray() ?? []).Select(t => t?["fileDataId"]?.GetValue<int>()).OfType<int>().Distinct().Order().ToList();
    }
}
