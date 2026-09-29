using System.Text.Json.Nodes;

namespace Altrobe.Core.Convert;

// Public entry points over the ported converter. `open` reads a FileDataID from local storage and
// is also used for the skin, skeleton and .anim files a model references.
public static class AssetConverter
{
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

    public static byte[] ConvertTexture(byte[] blp)
    {
        var img = Blp.Decode(blp);
        return Png.Encode(img.Width, img.Height, img.Rgba);
    }

    // Distinct geoset IDs of the mesh, from a model's metadata JSON.
    public static IReadOnlyList<int> GeosetIds(byte[] metaJson)
    {
        var meta = JsonNode.Parse(metaJson)!;
        return meta["geosets"]!.AsArray().Select(g => (int)g!["geosetId"]!).Distinct().Order().ToList();
    }
}
