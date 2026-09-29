// Port of the spike's tools/resolve/resolve.ts, plus the per-item parts of tools/viewer-test/dress.js
// (equipment slot, geoset groups and attachment points) so the browser gets them precomputed.
//
// Portions ported from wow.export (https://github.com/Kruithne/wow.export), MIT License,
// Copyright (c) Kruithne and Marlamin. Ported logic: slot tables, texture layer order and attachment
// IDs (src/js/wow/EquipmentSlots.js), item geoset groups (src/js/db/caches/DBItemGeosets.js), helmet
// hide geosets indexed by sex (DBItemGeosets.get_helmet_hide_geosets), and shoulder left/right by
// PositionIndex (src/js/db/caches/DBItemModels.js).

using System.Text.Json.Serialization;
using Altrobe.Core.Catalog;
using Altrobe.Core.Tables;
using T = Altrobe.Core.Tables.GameTableNames;

namespace Altrobe.Core.Resolve;

public sealed record FilePick(
    int FileDataId,
    string Match,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Position = null);

public sealed record ModelSlot(int Slot, int ModelResourcesId, IReadOnlyList<FilePick> Models, IReadOnlyList<FilePick> Textures);

public sealed record BodyTexture(int Section, IReadOnlyList<FilePick> Textures);

public sealed record GeosetChange(int Group, int Geoset);

public sealed record Attachment(int Slot, int AttachmentId, int ModelFileDataId, int? TextureFileDataId);

public sealed record ResolvedItem
{
    public string Build { get; init; } = "";
    public int Race { get; init; }
    public int Sex { get; init; }
    // "hd" or "sd". Item files are chosen by race and sex only: no table the resolver reads
    // distinguishes HD from SD bodies, and item section numbers map onto either body's layout.
    public string ModelSet { get; init; } = "hd";
    public int ItemId { get; init; }
    public string Name { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Error { get; init; }
    public int? InventoryType { get; init; }
    public int? EquipSlot { get; init; }
    public int? LayerPriority { get; init; }
    public int? ItemAppearanceId { get; init; }
    public int? ItemDisplayInfoId { get; init; }
    public int[] GeosetGroup { get; init; } = [];
    public int[] AttachmentGeosetGroup { get; init; } = [];
    public IReadOnlyList<int> HelmHideGeosetGroups { get; init; } = [];
    public IReadOnlyList<ModelSlot> Models { get; init; } = [];
    public IReadOnlyList<BodyTexture> BodyTextures { get; init; } = [];
    public IReadOnlyList<GeosetChange> Geosets { get; init; } = [];
    public IReadOnlyDictionary<int, int> BodyReplaceableTextures { get; init; } = new Dictionary<int, int>();
    public IReadOnlyList<Attachment> Attachments { get; init; } = [];
}

public sealed class ItemResolver
{
    public const int ShoulderLeft = 3, ShoulderRight = 30, BackSlot = 15;

    // Inventory type -> equipment slot (EquipmentSlots.js INVENTORY_TYPE_TO_SLOT_ID).
    public static readonly IReadOnlyDictionary<int, int> InventoryTypeToSlot = new Dictionary<int, int>
    {
        [1] = 1, [2] = 2, [3] = ShoulderLeft, [4] = 4, [5] = 5, [6] = 6, [7] = 7, [8] = 8, [9] = 9, [10] = 10,
        [13] = 16, [14] = 17, [15] = 16, [16] = BackSlot, [17] = 16, [19] = 19, [20] = 5, [21] = 16, [22] = 17, [23] = 17, [26] = 16,
    };

    // M2 attachment IDs per slot, in ItemDisplayInfo model order (EquipmentSlots.js ATTACHMENT_ID).
    static readonly Dictionary<int, int[]> SlotToAttachment = new()
    {
        [1] = [11], [ShoulderLeft] = [6], [ShoulderRight] = [5], [BackSlot] = [12], [16] = [1], [17] = [2, 0],
    };

    // Texture layer priority per slot: lower draws first (EquipmentSlots.js SLOT_LAYER).
    static readonly Dictionary<int, int> SlotLayer = new()
    {
        [4] = 10, [7] = 10, [1] = 11, [8] = 11, [3] = 13, [30] = 13, [5] = 13, [19] = 17, [6] = 18, [9] = 19, [10] = 20, [16] = 21, [17] = 22, [15] = 23,
    };

    // Slot -> (ItemDisplayInfo.GeosetGroup index, character geoset group, is-feet) (DBItemGeosets.js).
    static readonly Dictionary<int, (int index, int group, bool feet)[]> SlotGeosets = new()
    {
        [1] = [(0, 27, false), (1, 21, false)],
        [ShoulderLeft] = [(0, 26, false)],
        [4] = [(0, 8, false), (1, 10, false)],
        [5] = [(0, 8, false), (1, 10, false), (2, 13, false), (3, 22, false), (4, 28, false)],
        [6] = [(0, 18, false)],
        [7] = [(0, 11, false), (1, 9, false), (2, 13, false)],
        [8] = [(0, 5, false), (1, 20, true)],
        [10] = [(0, 4, false), (1, 23, false)],
        [BackSlot] = [(0, 15, false)],
        [19] = [(0, 12, false)],
    };

    readonly Dictionary<int, Row> _itemSparse, _itemAppearance, _itemDisplayInfo, _componentModel, _componentTexture, _chrRaces;
    readonly Dictionary<int, List<Row>> _imaByItem, _matResByDisplay, _modelFilesByRes, _textureFilesByRes, _helmetGeosetsByVis;

    public ItemResolver(ITables t)
    {
        _itemSparse = t.Get(T.ItemSparse).ById();
        _imaByItem = t.Get(T.ItemModifiedAppearance).GroupByColumn("ItemID");
        _itemAppearance = t.Get(T.ItemAppearance).ById();
        _itemDisplayInfo = t.Get(T.ItemDisplayInfo).ById();
        _matResByDisplay = t.Get(T.ItemDisplayInfoMaterialRes).GroupByColumn("ItemDisplayInfoID");
        _modelFilesByRes = t.Get(T.ModelFileData).GroupByColumn("ModelResourcesID");
        _textureFilesByRes = t.Get(T.TextureFileData).GroupByColumn("MaterialResourcesID");
        _componentModel = t.Get(T.ComponentModelFileData).ById();
        _componentTexture = t.Get(T.ComponentTextureFileData).ById();
        _helmetGeosetsByVis = t.Get(T.HelmetGeosetData).GroupByColumn("HelmetGeosetVisDataID");
        _chrRaces = t.Get(T.ChrRaces).ById();
    }

    public ResolvedItem Resolve(int itemId, int raceId, int sex)
    {
        _itemSparse.TryGetValue(itemId, out var sparse);
        var name = sparse != null ? sparse.Str("Display_lang") : "(no ItemSparse row)";
        var imas = _imaByItem.Of(itemId);
        if (imas.Count == 0) return Fail("no ItemModifiedAppearance");
        var ima = ItemCatalog.PrimaryAppearance(imas);
        if (!_itemAppearance.TryGetValue(ima.Int("ItemAppearanceID"), out var appearance)) return Fail($"no ItemAppearance {ima.Int("ItemAppearanceID")}");
        if (!_itemDisplayInfo.TryGetValue(appearance.Int("ItemDisplayInfoID"), out var display)) return Fail($"no ItemDisplayInfo {appearance.Int("ItemDisplayInfoID")}");

        // A slot can have a texture with no model: cloaks paint onto the body's own cape geoset.
        var models = new List<ModelSlot>();
        for (var i = 0; i < 2; i++)
        {
            var resId = display.Int("ModelResourcesID", i);
            var matId = display.Int("ModelMaterialResourcesID", i);
            if (resId == 0 && matId == 0) continue;
            var files = resId != 0 ? _modelFilesByRes.Of(resId).Select(r => r.Int("FileDataID")).ToList() : [];
            var textures = matId != 0 ? Pick(_textureFilesByRes.Of(matId).Select(r => r.Int("FileDataID")).ToList(), _componentTexture, raceId, sex, "Texture") : [];
            models.Add(new ModelSlot(i, resId, resId != 0 ? Pick(files, _componentModel, raceId, sex, "Model") : [], textures));
        }

        var bodyTextures = _matResByDisplay.Of(display.Int("ID")).Select(m => new BodyTexture(
            m.Int("ComponentSection"),
            Pick(_textureFilesByRes.Of(m.Int("MaterialResourcesID")).Select(r => r.Int("FileDataID")).ToList(), _componentTexture, raceId, sex, "Texture"))).ToList();

        // HelmetGeosetVis is indexed by sex (0 male, 1 female).
        var helmVis = display.Int("HelmetGeosetVis", sex);
        var helmHides = helmVis != 0
            ? _helmetGeosetsByVis.Of(helmVis).Where(h => h.Int("RaceID") == raceId || h.Int("RaceID") == 0).Select(h => h.Int("HideGeosetGroup")).Distinct().ToList()
            : [];

        int? inventoryType = sparse?.Int("InventoryType");
        int? equipSlot = inventoryType is { } it && InventoryTypeToSlot.TryGetValue(it, out var s) ? s : null;
        var geosetGroup = display.Ints("GeosetGroup");

        return new ResolvedItem
        {
            ItemId = itemId,
            Name = name,
            InventoryType = inventoryType,
            EquipSlot = equipSlot,
            LayerPriority = equipSlot is { } es && SlotLayer.TryGetValue(es, out var layer) ? layer : null,
            ItemAppearanceId = ima.Int("ItemAppearanceID"),
            ItemDisplayInfoId = display.Int("ID"),
            GeosetGroup = geosetGroup,
            AttachmentGeosetGroup = display.Ints("AttachmentGeosetGroup"),
            HelmHideGeosetGroups = helmHides,
            Models = models,
            BodyTextures = bodyTextures,
            Geosets = equipSlot is { } g ? Geosets(g, geosetGroup) : [],
            BodyReplaceableTextures = equipSlot == BackSlot && models.All(m => m.Models.Count == 0)
                && models.FirstOrDefault(m => m.Textures.Count > 0)?.Textures[0].FileDataId is { } cape
                ? new Dictionary<int, int> { [2] = cape }
                : new Dictionary<int, int>(),
            Attachments = equipSlot is { } a ? Attachments(a, models) : [],
        };

        ResolvedItem Fail(string error) => new() { ItemId = itemId, Name = name, Error = error };
    }

    // wow.export: a group's geoset is group * 100 + 1 + GeosetGroup[n]; feet use GeosetGroup[1] as
    // is, and 2 when it is 0.
    static List<GeosetChange> Geosets(int slot, int[] geosetGroup) =>
        (SlotGeosets.GetValueOrDefault(slot) ?? []).Select(m =>
        {
            var g = m.index < geosetGroup.Length ? geosetGroup[m.index] : 0;
            var value = m.feet ? (g == 0 ? 2 : g) : 1 + g;
            return new GeosetChange(m.group, m.group * 100 + value);
        }).ToList();

    static List<Attachment> Attachments(int equipSlot, List<ModelSlot> models)
    {
        var result = new List<Attachment>();
        // Shoulders fill both shoulder slots from one item.
        foreach (var slot in equipSlot == ShoulderLeft ? [ShoulderLeft, ShoulderRight] : new[] { equipSlot })
        {
            if (!SlotToAttachment.TryGetValue(slot, out var ids)) continue;
            List<(FilePick model, IReadOnlyList<FilePick> textures)> picks;
            if (slot is ShoulderLeft or ShoulderRight)
            {
                // Both display slots list the same pair; PositionIndex 0 is the left model, 1 the right.
                var displaySlot = slot == ShoulderLeft ? 0 : 1;
                var src = displaySlot < models.Count ? models[displaySlot] : models.FirstOrDefault();
                var model = src?.Models.FirstOrDefault(m => m.Position == displaySlot) ?? (src != null && displaySlot < src.Models.Count ? src.Models[displaySlot] : null);
                picks = model != null ? [(model, src!.Textures)] : [];
            }
            else
            {
                picks = models.Where(m => m.Models.Count > 0).Select(m => (m.Models[0], m.Textures)).ToList();
            }
            for (var i = 0; i < Math.Min(picks.Count, ids.Length); i++)
                result.Add(new Attachment(slot, ids[i], picks[i].model.FileDataId, picks[i].textures.Count > 0 ? picks[i].textures[0].FileDataId : null));
        }
        return result;
    }

    // Component files carry race/sex filters; 0 for race means "any". GenderIndex 0 is male, 1
    // female; other values (seen: 2, 3) count as "any" (inferred in the spike, not verified).
    List<FilePick> Pick(List<int> fileIds, Dictionary<int, Row> meta, int raceId, int sex, string kind)
    {
        var withMeta = fileIds.Select(id => (id, m: meta.GetValueOrDefault(id))).ToList();
        var neutral = withMeta.Where(f => f.m == null).Select(f => new FilePick(f.id, "neutral")).ToList();
        if (neutral.Count == withMeta.Count) return neutral;

        var fallback = FallbackRace(raceId, sex, kind);
        bool SexOk(Row m) => m.Int("GenderIndex") == sex || (m.Int("GenderIndex") != 0 && m.Int("GenderIndex") != 1);
        (string label, Func<Row, bool> test)[] tiers =
        [
            ("race+sex", m => m.Int("RaceID") == raceId && SexOk(m)),
            ("fallback-race", m => m.Int("RaceID") == fallback && fallback != 0 && SexOk(m)),
            ("any-race", m => m.Int("RaceID") == 0 && SexOk(m)),
        ];
        foreach (var (label, test) in tiers)
        {
            var hits = withMeta.Where(f => f.m != null && test(f.m)).ToList();
            // Texture component rows have no PositionIndex; only models carry one.
            if (hits.Count > 0) return hits.Select(f => new FilePick(f.id, label, kind == "Model" ? f.m!.Int("PositionIndex") : null)).ToList();
        }
        return neutral;
    }

    int FallbackRace(int raceId, int sex, string kind) =>
        _chrRaces.TryGetValue(raceId, out var r) ? r.Int(sex == 0 ? $"Male{kind}FallbackRaceID" : $"Female{kind}FallbackRaceID") : 0;
}
