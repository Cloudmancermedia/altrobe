// Port of the spike's tools/looks/looks.ts: the default customization for a bare character, resolved
// to the geosets to show and the texture layers to composite, for the HD body or its SD variant.
//
// Portions ported from wow.export (https://github.com/Kruithne/wow.export), MIT License,
// Copyright (c) Kruithne and Marlamin. Ported logic: default geoset reset rule and per-option
// geoset toggling (src/js/ui/character-appearance.js), related-choice gating and texture layer
// section lookup (same file), race/gender texture selection
// (src/js/db/caches/DBComponentTextureFileData.js), and item section layers
// (src/js/modules/tab_characters.js update_textures).
//
// Where this deliberately differs from wow.export (carried over from the spike):
// - Options with flag 0x20 still get a default choice. wow.export leaves them unset, which drops
//   the Undead jaw geoset (202) and leaves a hole under the face.
// - Choices are filtered by ChrCustomizationReq for a fresh non-Death-Knight character.
// - Every element of a choice counts, not only the last one read.
// - Ears default to 702, not 701 (701 renders the Orc without ears).

using System.Text.Json.Serialization;
using Altrobe.Core.Catalog;
using Altrobe.Core.Tables;
using T = Altrobe.Core.Tables.GameTableNames;

namespace Altrobe.Core.Resolve;

// Spike casing (the raw CharComponentTextureLayouts row), which the web app reads.
public sealed record LayoutInfo(
    [property: JsonPropertyName("ID")] int Id,
    [property: JsonPropertyName("Width")] int Width,
    [property: JsonPropertyName("Height")] int Height);

public sealed record TextureSize(int TextureType, int Width, int Height);

public sealed record SectionRect(int SectionType, int X, int Y, int Width, int Height);

public sealed record SectionLayer(int SectionType, int TextureType, int BlendMode, int FromLayer);

public sealed record GeosetsFromChoice(int OptionId, int ChoiceId, IReadOnlyList<int> Geosets);

public sealed record TextureLayer
{
    public int TextureType { get; init; }
    public int Layer { get; init; }
    public int BlendMode { get; init; }
    public int Target { get; init; }
    public SectionRect Section { get; init; } = null!;
    public int FileDataId { get; init; }
    public int ChoiceId { get; init; }
    public int OptionId { get; init; }
    public int MaterialResourcesId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int[]? Candidates { get; init; }
    // Set on per-choice layers that apply only while this other choice is also active.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? RelatedChoiceId { get; init; }
}

// The default choice of one option, in the spike's looks.ts shape.
public sealed record LookChoice(
    int OptionId,
    string Option,
    long OptionFlags,
    int? ChoiceId,
    string Choice,
    int? OrderIndex,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Skipped,
    int EligibleChoices,
    int TotalChoices,
    int? WowExportChoiceId);

// One selectable choice and what it changes: the geosets it shows (after the option's own
// geosets are turned off) and the texture layers it paints.
public sealed record ChoiceInfo(
    int ChoiceId,
    string Name,
    int OrderIndex,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Swatch,
    bool IsDefault,
    IReadOnlyList<int> Geosets,
    IReadOnlyList<TextureLayer> Layers);

// Every choice a fresh character of the look's class can pick, by OrderIndex then ID. Geosets
// lists every geoset any choice of the option names, which a choice change turns off first.
public sealed record OptionInfo(
    int OptionId,
    string Name,
    long Flags,
    int OrderIndex,
    int? DefaultChoiceId,
    IReadOnlyList<int> Geosets,
    IReadOnlyList<ChoiceInfo> Choices);

public sealed record CharacterLook
{
    public string Build { get; init; } = "";
    public int Race { get; init; }
    public int Sex { get; init; }
    public int ClassId { get; init; }
    public string Models { get; init; } = "hd";
    public int OverrideArchive { get; init; }
    public int ChrModelId { get; init; }
    public int HdChrModelId { get; init; }
    public int ModelFileDataId { get; init; }
    public int TextureLayoutId { get; init; }
    public LayoutInfo Layout { get; init; } = null!;
    public IReadOnlyList<TextureSize> Textures { get; init; } = [];
    public IReadOnlyList<SectionRect> Sections { get; init; } = [];
    public IReadOnlyList<SectionLayer> SectionLayers { get; init; } = [];
    public IReadOnlyList<LookChoice> Choices { get; init; } = [];
    public IReadOnlyList<int> Geosets { get; init; } = [];
    public IReadOnlyList<TextureLayer> Layers { get; init; } = [];
    public IReadOnlyList<OptionInfo> Options { get; init; } = [];
    public IReadOnlyList<GeosetsFromChoice> GeosetsFromChoices { get; init; } = [];
    public IReadOnlyList<Dictionary<string, int>> UnsupportedElements { get; init; } = [];
    public IReadOnlyList<string> Notes { get; init; } = [];
}

public sealed class LookResolver
{
    // Stands in for "any class that is not a Death Knight": several choices are split by class mask.
    public const int DefaultClassId = 1;
    // overrideArchive client setting. -1 on a requirement means "any"; the Undead "Fresh" skin type
    // and its skin colors need 1, the "Bony" ones need 0. We assume the default client value 0.
    const int OverrideArchive = 0;
    const int GenderAny = 3;
    static readonly string[] UnsupportedKeys = ["ChrCustomizationSkinnedModelID", "ChrCustomizationCondModelID", "ChrCustomizationDisplayInfoID", "ChrCustItemGeoModifyID"];

    readonly IReadOnlyList<Row> _raceXModel;
    readonly Dictionary<int, int> _altVariant;
    readonly Dictionary<int, Row> _chrModel, _custGeoset, _custMaterial, _req, _componentTexture, _layouts, _displayInfo, _modelData;
    readonly Dictionary<int, List<Row>> _optionsByModel, _choicesByOption, _elementsByChoice, _texturesByRes, _layersByLayout, _materialsByLayout, _sectionsByLayout;

    public LookResolver(ITables t)
    {
        _raceXModel = t.Get(T.ChrRaceXChrModel);
        _altVariant = t.Get(T.ChrModelAltVariant).ById("SourceChrModelID").ToDictionary(kv => kv.Key, kv => kv.Value.Int("VariantChrModelID"));
        _chrModel = t.Get(T.ChrModel).ById();
        _optionsByModel = t.Get(T.ChrCustomizationOption).GroupByColumn("ChrModelID");
        _choicesByOption = t.Get(T.ChrCustomizationChoice).GroupByColumn("ChrCustomizationOptionID");
        _elementsByChoice = t.Get(T.ChrCustomizationElement).GroupByColumn("ChrCustomizationChoiceID");
        _custGeoset = t.Get(T.ChrCustomizationGeoset).ById();
        _custMaterial = t.Get(T.ChrCustomizationMaterial).ById();
        _req = t.Get(T.ChrCustomizationReq).ById();
        _texturesByRes = t.Get(T.TextureFileData).GroupByColumn("MaterialResourcesID");
        _componentTexture = t.Get(T.ComponentTextureFileData).ById();
        _layersByLayout = t.Get(T.ChrModelTextureLayer).GroupByColumn("CharComponentTextureLayoutsID");
        _materialsByLayout = t.Get(T.ChrModelMaterial).GroupByColumn("CharComponentTextureLayoutsID");
        _sectionsByLayout = t.Get(T.CharComponentTextureSections).GroupByColumn("CharComponentTextureLayoutID");
        _layouts = t.Get(T.CharComponentTextureLayouts).ById();
        _displayInfo = t.Get(T.CreatureDisplayInfo).ById();
        _modelData = t.Get(T.CreatureModelData).ById();
    }

    // Body model: ChrModel.DisplayID -> CreatureDisplayInfo.ModelID -> CreatureModelData.FileDataID.
    // Null when the race/sex has no body of that kind.
    public (int chrModelId, int hdChrModelId, int bodyFileDataId)? Body(int raceId, int sex, ModelSet set)
    {
        var link = _raceXModel.FirstOrDefault(r => r.Int("ChrRacesID") == raceId && r.Int("Sex") == sex);
        if (link == null) return null;
        var hd = link.Int("ChrModelID");
        var id = set == ModelSet.Sd ? _altVariant.TryGetValue(hd, out var v) ? v : (int?)null : hd;
        if (id == null || !_chrModel.TryGetValue(id.Value, out var model)) return null;
        if (!_displayInfo.TryGetValue(model.Int("DisplayID"), out var display) || !_modelData.TryGetValue(display.Int("ModelID"), out var data)) return null;
        return (id.Value, hd, data.Int("FileDataID"));
    }

    // `meshGeosets` returns the geoset IDs present in a body model's mesh (from its converted metadata).
    public CharacterLook? Resolve(int raceId, int sex, int classId, ModelSet set, Func<int, IReadOnlyList<int>> meshGeosets)
    {
        if (Body(raceId, sex, set) is not var (chrModelId, hdChrModelId, bodyFdid)) return null;
        var model = _chrModel[chrModelId];
        var layoutId = model.Int("CharComponentTextureLayoutID");
        var mesh = meshGeosets(bodyFdid).Distinct().Order().ToList();
        var meshSet = mesh.ToHashSet();
        var notes = new List<string>();

        // 1. Default choice per option: first eligible choice by OrderIndex, then ID.
        var options = _optionsByModel.Of(chrModelId).OrderBy(o => o.Int("OrderIndex")).ToList();
        var picks = new List<(Row option, Row? choice, string? optFail, List<(Row choice, string? fail)> all)>();
        foreach (var opt in options)
        {
            var optFail = ReqFailure(opt.Int("Requirement"), classId);
            var all = _choicesByOption.Of(opt.Int("ID")).OrderBy(c => c.Int("OrderIndex")).ThenBy(c => c.Int("ID"))
                .Select(c => (choice: c, fail: ReqFailure(c.Int("ChrCustomizationReqID"), classId))).ToList();
            var pick = optFail != null ? null : all.FirstOrDefault(c => c.fail == null).choice;
            picks.Add((opt, pick, optFail, all));
        }
        var active = picks.Where(p => p.choice != null).Select(p => (optionId: p.option.Int("ID"), choice: p.choice!)).ToList();
        var activeIds = active.Select(a => a.choice.Int("ID")).ToHashSet();

        // 2. Geosets. Reset rule from wow.export: 0, every xx01, every 32xx, minus groups 17 and 35.
        // With nothing equipped, the xx01 rule is also the bare state for equipment groups.
        // Insertion-ordered like the spike's JS Set, so notes list geosets in the same order.
        var show = new List<int>();
        foreach (var id in mesh)
        {
            var s = id.ToString();
            // Ears (7xx) are the exception: 701 is the "no ears" state helmets switch to, and the
            // Orc renders earless with it.
            var isDefault = id == 0 || (s.EndsWith("01") && !s.StartsWith('7')) || s.StartsWith("32") || id == 702;
            var hidden = s.StartsWith("17") || s.StartsWith("35");
            if (isDefault && !hidden && !show.Contains(id)) show.Add(id);
        }
        // Per active option: every geoset any choice of the option names is turned off, then the
        // chosen choice's geosets are turned on.
        var fromChoices = new List<GeosetsFromChoice>();
        foreach (var (optionId, choice) in active)
        {
            var mine = ChoiceGeosets(choice.Int("ID"), notes);
            foreach (var g in OptionGeosets(optionId, notes)) if (g != 0) show.Remove(g);
            foreach (var g in mine) if (!show.Contains(g)) show.Add(g);
            if (mine.Count > 0) fromChoices.Add(new GeosetsFromChoice(optionId, choice.Int("ID"), mine));
        }
        var missing = show.Where(g => !meshSet.Contains(g)).ToList();
        if (missing.Count > 0) notes.Add($"Choices name geosets the body mesh does not have (ignored): {string.Join(",", missing)}");
        var geosets = show.Where(meshSet.Contains).Order().ToList();

        // 3. Texture layers: choice -> element -> ChrCustomizationMaterial -> target -> layer + section.
        var unsupported = new List<Dictionary<string, int>>();
        var layers = new List<TextureLayer>();
        foreach (var (optionId, choice) in active)
        {
            foreach (var e in _elementsByChoice.Of(choice.Int("ID")))
                foreach (var k in UnsupportedKeys)
                    if (e.Int(k) != 0) unsupported.Add(new() { ["choiceId"] = choice.Int("ID"), [k] = e.Int(k) });
            layers.AddRange(ChoiceLayers(choice.Int("ID"), optionId, layoutId, raceId, sex, classId, notes)
                .Where(l => l.RelatedChoiceId == null || activeIds.Contains(l.RelatedChoiceId.Value))
                .Select(l => l with { RelatedChoiceId = null }));
        }
        layers = layers.OrderBy(l => l.TextureType).ThenBy(l => l.Layer).ToList();

        // 4. Where item textures go: every section rectangle of the layout, and for item component
        // sections 0-8 the texture layer that paints them: the first layer (in table order) whose
        // mask includes the section, else the full-size skin layer. Items blend over the skin, so
        // blend modes 0/1 become 15 (alpha), as in wow.export.
        var layoutLayers = _layersByLayout.Of(layoutId);
        var baseLayer = layoutLayers.FirstOrDefault(l => l.Int("TextureSectionTypeBitMask") == -1 && l.Int("TextureType") == 1);
        var sectionLayers = new List<SectionLayer>();
        for (var sectionType = 0; sectionType < 9; sectionType++)
        {
            var l = layoutLayers.FirstOrDefault(l => l.Int("TextureSectionTypeBitMask") != -1 && ((1 << sectionType) & l.Int("TextureSectionTypeBitMask")) != 0) ?? baseLayer;
            if (l == null) continue;
            var blend = l.Int("BlendMode");
            sectionLayers.Add(new SectionLayer(sectionType, l.Int("TextureType"), blend is 0 or 1 ? 15 : blend, l.Int("ID")));
        }

        var choices = picks.Select(p =>
        {
            var flags = p.option.Long("Flags");
            var byId = p.all.Select(c => c.choice).OrderBy(c => c.Int("ID")).FirstOrDefault();
            return new LookChoice(
                p.option.Int("ID"), p.option.Str("Name_lang"), flags,
                p.choice?.Int("ID"), p.choice?.Str("Name_lang") ?? "", p.choice?.Int("OrderIndex"),
                p.optFail ?? (p.choice == null ? "no eligible choice" : null),
                p.all.Count(c => c.fail == null), p.all.Count,
                (flags & 0x20) != 0 ? null : byId?.Int("ID"));
        }).ToList();

        var optionInfos = picks.Select(p =>
        {
            var optionId = p.option.Int("ID");
            var selectable = p.optFail != null ? [] : p.all.Where(c => c.fail == null).Select(c => c.choice).ToList();
            return new OptionInfo(
                optionId, p.option.Str("Name_lang"), p.option.Long("Flags"), p.option.Int("OrderIndex"),
                p.choice?.Int("ID"),
                OptionGeosets(optionId, null).Order().ToList(),
                selectable.Select(c => new ChoiceInfo(
                    c.Int("ID"), c.Str("Name_lang"), c.Int("OrderIndex"), Swatch(c),
                    c == p.choice,
                    ChoiceGeosets(c.Int("ID"), null).Order().ToList(),
                    ChoiceLayers(c.Int("ID"), optionId, layoutId, raceId, sex, classId, null))).ToList());
        }).ToList();

        var layout = _layouts.TryGetValue(layoutId, out var lr) ? new LayoutInfo(layoutId, lr.Int("Width"), lr.Int("Height")) : new LayoutInfo(layoutId, 0, 0);
        return new CharacterLook
        {
            Race = raceId,
            Sex = sex,
            ClassId = classId,
            Models = set == ModelSet.Sd ? "sd" : "hd",
            OverrideArchive = OverrideArchive,
            ChrModelId = chrModelId,
            HdChrModelId = hdChrModelId,
            ModelFileDataId = bodyFdid,
            TextureLayoutId = layoutId,
            Layout = layout,
            Textures = _materialsByLayout.Of(layoutId).Select(m => new TextureSize(m.Int("TextureType"), m.Int("Width"), m.Int("Height"))).ToList(),
            Sections = _sectionsByLayout.Of(layoutId).Select(ToRect).ToList(),
            SectionLayers = sectionLayers,
            Choices = choices,
            Geosets = geosets,
            Layers = layers,
            Options = optionInfos,
            GeosetsFromChoices = fromChoices,
            UnsupportedElements = unsupported,
            Notes = notes,
        };
    }

    // SwatchColor[0] is 0xAARRGGBB; 0 means the choice has no swatch.
    static string? Swatch(Row choice)
    {
        var c = choice.Int("SwatchColor", 0);
        return c == 0 ? null : $"#{c & 0xFFFFFF:x6}";
    }

    static SectionRect ToRect(Row s) => new(s.Int("SectionType"), s.Int("X"), s.Int("Y"), s.Int("Width"), s.Int("Height"));

    List<int> ChoiceGeosets(int choiceId, List<string>? notes)
    {
        var result = new List<int>();
        foreach (var e in _elementsByChoice.Of(choiceId))
        {
            var id = e.Int("ChrCustomizationGeosetID");
            if (id == 0) continue;
            if (_custGeoset.TryGetValue(id, out var g)) result.Add(g.Int("GeosetType") * 100 + g.Int("GeosetID"));
            else notes?.Add($"Choice {choiceId} names missing ChrCustomizationGeoset {id} (skipped)");
        }
        return result;
    }

    HashSet<int> OptionGeosets(int optionId, List<string>? notes)
    {
        var set = new HashSet<int>();
        foreach (var other in _choicesByOption.Of(optionId)) set.UnionWith(ChoiceGeosets(other.Int("ID"), notes));
        return set;
    }

    // Every texture layer a choice paints, including ones gated on a related choice (marked with
    // RelatedChoiceId). Notes are collected only when the caller passes a list.
    List<TextureLayer> ChoiceLayers(int choiceId, int optionId, int layoutId, int raceId, int sex, int classId, List<string>? notes)
    {
        var layers = new List<TextureLayer>();
        foreach (var e in _elementsByChoice.Of(choiceId))
        {
            var matId = e.Int("ChrCustomizationMaterialID");
            if (matId == 0) continue;
            var related = e.Int("RelatedChrCustomizationChoiceID");
            if (!_custMaterial.TryGetValue(matId, out var mat))
            {
                notes?.Add($"Choice {choiceId} names missing ChrCustomizationMaterial {matId} (skipped)");
                continue;
            }
            var target = mat.Int("ChrModelTextureTargetID");
            var layer = _layersByLayout.Of(layoutId).FirstOrDefault(l => l.Int("ChrModelTextureTargetID", 0) == target);
            if (layer == null)
            {
                notes?.Add($"Choice {choiceId} material {mat.Int("ID")} targets {target}, which layout {layoutId} has no layer for (skipped)");
                continue;
            }
            SectionRect section;
            var mask = layer.Int("TextureSectionTypeBitMask");
            if (mask == -1)
            {
                var texMat = _materialsByLayout.Of(layoutId).FirstOrDefault(m => m.Int("TextureType") == layer.Int("TextureType"));
                section = new SectionRect(-1, 0, 0, texMat?.Int("Width") ?? 0, texMat?.Int("Height") ?? 0);
            }
            else
            {
                var s = _sectionsByLayout.Of(layoutId).FirstOrDefault(s => ((1 << s.Int("SectionType")) & mask) != 0);
                if (s == null)
                {
                    notes?.Add($"No section for layer {layer.Int("ID")} mask {mask}");
                    continue;
                }
                section = ToRect(s);
            }
            var candidates = _texturesByRes.Of(mat.Int("MaterialResourcesID")).Where(t => t.Int("UsageType") == 0).Select(t => t.Int("FileDataID")).ToArray();
            var fileDataId = TextureForRaceGender(candidates, raceId, sex, classId);
            if (fileDataId == null)
            {
                notes?.Add($"Material {mat.Int("ID")} (resources {mat.Int("MaterialResourcesID")}) has no texture file");
                continue;
            }
            layers.Add(new TextureLayer
            {
                TextureType = layer.Int("TextureType"),
                Layer = layer.Int("Layer"),
                BlendMode = layer.Int("BlendMode"),
                Target = target,
                Section = section,
                FileDataId = fileDataId.Value,
                ChoiceId = choiceId,
                OptionId = optionId,
                MaterialResourcesId = mat.Int("MaterialResourcesID"),
                Candidates = candidates.Length > 1 ? candidates : null,
                RelatedChoiceId = related != 0 ? related : null,
            });
        }
        return layers;
    }

    // Returns null if the requirement passes, else the reason it fails.
    string? ReqFailure(int reqId, int classId)
    {
        if (reqId == 0) return null;
        if (!_req.TryGetValue(reqId, out var r)) return $"req {reqId} missing";
        var reqType = r.Int("ReqType");
        // WoWDBDefs documents only ReqType &1 = class required. Bit 2 is on the plain "everyone"
        // requirement (ReqType 3), so it is not a lock. Bit 4 without bit 2 is only on extra skin
        // and hair colors, which look like unlockables, so a fresh character is assumed not to have them.
        if ((reqType & 4) != 0 && (reqType & 2) == 0) return $"req {reqId} ReqType {reqType} (assumed locked)";
        if ((reqType & 1) != 0 && (r.Int("ClassMask") & (1 << (classId - 1))) == 0) return $"req {reqId} class mask {r.Int("ClassMask")}";
        var archive = r.Int("OverrideArchive");
        if (archive != -1 && archive != OverrideArchive) return $"req {reqId} overrideArchive {archive}";
        if (r.Int("ReqAchievementID") != 0 || r.Int("ReqQuestID") != 0 || r.Int("ReqItemModifiedAppearanceID") != 0) return $"req {reqId} needs unlock";
        return null;
    }

    // Ported from wow.export DBComponentTextureFileData.getTextureForRaceGender.
    int? TextureForRaceGender(int[] fdids, int raceId, int gender, int classId)
    {
        if (fdids.Length == 0) return null;
        if (fdids.Length == 1) return fdids[0];
        var candidates = fdids.Select(f => (fdid: f, info: _componentTexture.GetValueOrDefault(f))).ToList();
        if (!candidates.Any(c => c.info != null)) return fdids[0];
        var tagged = candidates.Where(c => c.info != null
            && (c.info.Int("GenderIndex") == gender || c.info.Int("GenderIndex") == GenderAny)
            && (c.info.Int("ClassID") == 0 || c.info.Int("ClassID") == classId)).ToList();
        if (tagged.Count == 0) return candidates.Where(c => c.info == null).Select(c => (int?)c.fdid).FirstOrDefault();
        return tagged
            .OrderBy(c => c.info!.Int("GenderIndex") == gender ? 0 : 1)
            .ThenBy(c => c.info!.Int("ClassID") == classId ? 0 : 1)
            .ThenBy(c => c.info!.Int("RaceID") == raceId ? 0 : 1)
            .First().fdid;
    }
}
