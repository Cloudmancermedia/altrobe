// Port of tools/resolve/resolve.ts, plus the per-item parts of tools/viewer-test/dress.js
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

// An effect model (a weapon glow) attached to the item's own model at its attachment `AttachmentId`,
// whichever hand the item is in.
public sealed record ItemEffect(int AttachmentId, int ModelFileDataId);

// A geoset group a helmet hides. `KeepWithOptionFlags` is HelmetGeosetData's unnamed last column when
// every row for the group sets it (seen: 32), else 0: the group stays when the customization option
// filling it has one of those flags. The Undead jaw (Jaw Features, flags 40) is in groups 1 and 2,
// which Helm of Might hides with 32. The column is undocumented.
public sealed record HelmHide(int Group, int KeepWithOptionFlags);

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
    public IReadOnlyList<HelmHide> HelmHides { get; init; } = [];
    public IReadOnlyList<ModelSlot> Models { get; init; } = [];
    public IReadOnlyList<BodyTexture> BodyTextures { get; init; } = [];
    public IReadOnlyList<GeosetChange> Geosets { get; init; } = [];
    public IReadOnlyDictionary<int, int> BodyReplaceableTextures { get; init; } = new Dictionary<int, int>();
    public IReadOnlyList<Attachment> Attachments { get; init; } = [];
    public IReadOnlyList<ItemEffect> Effects { get; init; } = [];
    // Item.SheatheType (where a sheathed weapon goes: 1 back, 2 large weapon on the back, 3 hip,
    // 4 shield on the back, else it stays in hand) and Item.SubclassID (bow, gun, ...).
    public int? SheatheType { get; init; }
    public int? ItemSubclass { get; init; }
}

public sealed class ItemResolver
{
    public const int ShoulderLeft = 3, ShoulderRight = 30, BackSlot = 15, RangedSlot = 18;
    // The name of an item the game files don't name (no ItemSparse row).
    public const string NoName = "(no ItemSparse row)";
    const string HelmKeepColumn = "Field_10_0_0_46047_003";
    const long BareFeetFlag = 0x2;
    const int FootSection = 7;

    // Inventory type -> equipment slot (EquipmentSlots.js INVENTORY_TYPE_TO_SLOT_ID).
    public static readonly IReadOnlyDictionary<int, int> InventoryTypeToSlot = new Dictionary<int, int>
    {
        [1] = 1, [2] = 2, [3] = ShoulderLeft, [4] = 4, [5] = 5, [6] = 6, [7] = 7, [8] = 8, [9] = 9, [10] = 10,
        [13] = 16, [14] = 17, [15] = RangedSlot, [16] = BackSlot, [17] = 16, [19] = 19, [20] = 5, [21] = 16, [22] = 17, [23] = 17,
        [25] = RangedSlot, [26] = RangedSlot,
    };

    // M2 attachment IDs per slot, in ItemDisplayInfo model order (EquipmentSlots.js ATTACHMENT_ID).
    static readonly Dictionary<int, int[]> SlotToAttachment = new()
    {
        // A ranged weapon in the right hand; the viewer puts a bow in the left (it knows the subclass).
        [1] = [11], [ShoulderLeft] = [6], [ShoulderRight] = [5], [BackSlot] = [12], [16] = [1], [17] = [2, 0], [RangedSlot] = [1],
    };

    // Texture layer priority per slot: lower draws first (EquipmentSlots.js SLOT_LAYER).
    static readonly Dictionary<int, int> SlotLayer = new()
    {
        [4] = 10, [7] = 10, [1] = 11, [8] = 11, [3] = 13, [30] = 13, [5] = 13, [19] = 17, [6] = 18, [9] = 19, [10] = 20, [16] = 21, [17] = 22, [15] = 23,
    };

    // Body texture sections each equipment slot paints (0 arm upper, 1 arm lower, 2 hand, 3 torso upper,
    // 4 torso lower, 5 leg upper, 6 leg lower, 7 foot). Some late displays list sections outside their
    // slot (tier 2.5 shoulders carry legs, the Frostfire Bindings a red torso) that the game doesn't
    // show. Taken from the data: each slot's own sections appear on 90-100% of its items, strays on 3-7%.
    static readonly Dictionary<int, int[]> SlotSections = new()
    {
        [4] = [0, 1, 3, 4], [5] = [0, 1, 3, 4, 5, 6], [6] = [4, 5], [7] = [5, 6], [8] = [6, 7],
        [9] = [1], [10] = [1, 2], [19] = [3, 4, 5],
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

    readonly Dictionary<int, Row> _item, _itemSparse, _itemAppearance, _itemDisplayInfo, _componentModel, _componentTexture, _chrRaces, _itemVisuals;
    readonly Dictionary<int, List<Row>> _imaByItem, _matResByDisplay, _modelFilesByRes, _textureFilesByRes, _helmetGeosetsByVis;

    public ItemResolver(ITables t)
    {
        _itemSparse = t.Get(T.ItemSparse).ById();
        _item = t.Get(T.Item).ById();
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
        _itemVisuals = t.Has(T.ItemVisuals) ? t.Get(T.ItemVisuals).ById() : [];
    }

    public ResolvedItem Resolve(int itemId, int raceId, int sex)
    {
        _itemSparse.TryGetValue(itemId, out var sparse);
        var name = sparse != null ? sparse.Str("Display_lang") : NoName;
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

        // Some items have a model but no ItemSparse row (tier 2 in the Forever beta); Item has the slot too.
        int? inventoryType = sparse?.Int("InventoryType") ?? (_item.TryGetValue(itemId, out var itemRow) ? itemRow.Int("InventoryType") : null);
        int? equipSlot = inventoryType is { } it && InventoryTypeToSlot.TryGetValue(it, out var s) ? s : null;

        // ChrRaces flag 0x2, "Bare Feet" (wowdev.wiki DB/ChrRaces): Tauren and Trolls keep their own feet in
        // boots, so the foot texture is skipped, as WoW Model Viewer does. The leg texture and geosets stay.
        var bareFeet = _chrRaces.TryGetValue(raceId, out var race) && (race.Long("Flags") & BareFeetFlag) != 0;
        var slotSections = equipSlot is { } bs ? SlotSections.GetValueOrDefault(bs, []) : [];
        var bodyTextures = _matResByDisplay.Of(display.Int("ID"))
            .Where(m => slotSections.Contains(m.Int("ComponentSection")) && !(bareFeet && m.Int("ComponentSection") == FootSection))
            .Select(m => new BodyTexture(
            m.Int("ComponentSection"),
            Pick(_textureFilesByRes.Of(m.Int("MaterialResourcesID")).Select(r => r.Int("FileDataID")).ToList(), _componentTexture, raceId, sex, "Texture"))).ToList();

        // HelmetGeosetVis is indexed by sex (0 male, 1 female).
        var helmVis = display.Int("HelmetGeosetVis", sex);
        var helmRows = helmVis != 0
            ? _helmetGeosetsByVis.Of(helmVis).Where(h => h.Int("RaceID") == raceId || h.Int("RaceID") == 0).ToList()
            : [];
        var helmHides = helmRows.Select(h => h.Int("HideGeosetGroup")).Distinct().ToList();
        var helmHideRules = helmHides.Select(g =>
        {
            var keep = helmRows.Where(h => h.Int("HideGeosetGroup") == g).Select(h => h.Int(HelmKeepColumn)).ToList();
            return new HelmHide(g, keep.All(k => k > 0) ? keep.Aggregate(0, (a, k) => a | k) : 0);
        }).ToList();

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
            HelmHides = helmHideRules,
            Models = models,
            BodyTextures = bodyTextures,
            Geosets = equipSlot is { } g ? Geosets(g, geosetGroup) : [],
            BodyReplaceableTextures = equipSlot == BackSlot && models.All(m => m.Models.Count == 0)
                && models.FirstOrDefault(m => m.Textures.Count > 0)?.Textures[0].FileDataId is { } cape
                ? new Dictionary<int, int> { [2] = cape }
                : new Dictionary<int, int>(),
            Attachments = equipSlot is { } a ? Attachments(a, models, inventoryType) : [],
            Effects = Effects(display.Int("ItemVisual")),
            SheatheType = _item.TryGetValue(itemId, out var itemInfo) ? itemInfo.Int("SheatheType") : null,
            ItemSubclass = _item.TryGetValue(itemId, out var itemSub) ? itemSub.Int("SubclassID") : null,
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

    // M2 attachment 0, the shield point on the forearm. wow.export's table lists it second for the off
    // hand, so following it puts a one-model shield in the left hand (2), through the arm.
    const int ShieldAttachment = 0;
    const int ShieldInventoryType = 14;

    static List<Attachment> Attachments(int equipSlot, List<ModelSlot> models, int? inventoryType)
    {
        var result = new List<Attachment>();
        // Shoulders fill both shoulder slots from one item.
        foreach (var slot in equipSlot == ShoulderLeft ? [ShoulderLeft, ShoulderRight] : new[] { equipSlot })
        {
            if (!SlotToAttachment.TryGetValue(slot, out var ids)) continue;
            if (inventoryType == ShieldInventoryType) ids = [ShieldAttachment];
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

    // ItemDisplayInfo.ItemVisual names an ItemVisuals row of five effect models; column i goes on the item
    // model's attachment i (wowdev.wiki/M2#Attachments, ItemVisual0-4). The glows that are not part of
    // the weapon's own model, such as The Unstoppable Force's, come from here.
    List<ItemEffect> Effects(int itemVisual)
    {
        if (itemVisual == 0 || !_itemVisuals.TryGetValue(itemVisual, out var row)) return [];
        var files = row.Ints("ModelFileID");
        return Enumerable.Range(0, files.Length).Where(i => files[i] != 0).Select(i => new ItemEffect(i, files[i])).ToList();
    }

    // Component files carry race/sex filters; 0 for race means "any". GenderIndex 0 is male, 1
    // female; other values (seen: 2, 3) count as "any" (inferred, not verified). A list
    // of one file skips the filters (see below).
    List<FilePick> Pick(List<int> fileIds, Dictionary<int, Row> meta, int raceId, int sex, string kind)
    {
        var withMeta = fileIds.Select(id => (id, m: meta.GetValueOrDefault(id))).ToList();
        var neutral = withMeta.Where(f => f.m == null).Select(f => new FilePick(f.id, "neutral")).ToList();
        if (neutral.Count == withMeta.Count) return neutral;
        // A lone file is used whatever its tags, as wow.export and WoW Model Viewer do: the Phalanx
        // Breastplate's lower arm lists only a female texture, and male characters wear it too.
        if (withMeta.Count == 1)
            return [new FilePick(withMeta[0].id, "only-file", kind == "Model" ? withMeta[0].m!.Int("PositionIndex") : null)];

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
