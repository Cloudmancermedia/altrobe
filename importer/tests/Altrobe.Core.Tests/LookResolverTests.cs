using Altrobe.Core.Catalog;
using Altrobe.Core.Resolve;
using Altrobe.Core.Tables;
using static Altrobe.Core.Tests.Fake;

namespace Altrobe.Core.Tests;

public class LookResolverTests
{
    const int Orc = 2, Male = 0, Warrior = 1, Mage = 8;
    const int HdModel = 3, SdModel = 259, HdLayout = 105, SdLayout = 203;
    const int HdBody = 917116, SdBody = 121287;

    static readonly int[] HdMesh = [0, 101, 102, 103, 401, 501, 701, 702, 1301, 1302, 1701, 2001, 3201, 3501];
    static readonly int[] SdMesh = [0, 401, 702];

    static InMemoryTables Tables()
    {
        var t = new InMemoryTables()
            .Add(GameTableNames.ChrRaceXChrModel, R(("ID", 3), ("ChrRacesID", Orc), ("Sex", Male), ("ChrModelID", HdModel)))
            .Add(GameTableNames.ChrModelAltVariant, R(("ID", 3), ("SourceChrModelID", HdModel), ("VariantChrModelID", SdModel)))
            .Add(GameTableNames.ChrModel,
                R(("ID", HdModel), ("DisplayID", 51), ("CharComponentTextureLayoutID", HdLayout)),
                R(("ID", SdModel), ("DisplayID", 52), ("CharComponentTextureLayoutID", SdLayout)))
            .Add(GameTableNames.CreatureDisplayInfo, R(("ID", 51), ("ModelID", 61)), R(("ID", 52), ("ModelID", 62)))
            .Add(GameTableNames.CreatureModelData, R(("ID", 61), ("FileDataID", HdBody)), R(("ID", 62), ("FileDataID", SdBody)))
            .Add(GameTableNames.CharComponentTextureLayouts, R(("ID", HdLayout), ("Width", 2048), ("Height", 1024)), R(("ID", SdLayout), ("Width", 1024), ("Height", 1024)))
            .Add(GameTableNames.ChrModelMaterial,
                R(("ID", 1), ("CharComponentTextureLayoutsID", HdLayout), ("TextureType", 1), ("Width", 2048), ("Height", 1024)),
                R(("ID", 2), ("CharComponentTextureLayoutsID", HdLayout), ("TextureType", 6), ("Width", 512), ("Height", 256)),
                R(("ID", 3), ("CharComponentTextureLayoutsID", SdLayout), ("TextureType", 1), ("Width", 1024), ("Height", 1024)))
            .Add(GameTableNames.CharComponentTextureSections,
                R(("ID", 1), ("CharComponentTextureLayoutID", HdLayout), ("SectionType", 0), ("X", 0), ("Y", 0), ("Width", 512), ("Height", 256)),
                R(("ID", 2), ("CharComponentTextureLayoutID", HdLayout), ("SectionType", 9), ("X", 512), ("Y", 0), ("Width", 512), ("Height", 512)),
                R(("ID", 3), ("CharComponentTextureLayoutID", SdLayout), ("SectionType", 0), ("X", 0), ("Y", 0), ("Width", 256), ("Height", 128)))
            // Layer 41 is the full-size skin (mask -1); layer 42 paints section 9 (face) of type 1;
            // layer 43 is texture type 6 (hair), full size.
            .Add(GameTableNames.ChrModelTextureLayer,
                R(("ID", 41), ("CharComponentTextureLayoutsID", HdLayout), ("ChrModelTextureTargetID", new[] { 1, 0 }), ("TextureType", 1), ("Layer", 0), ("BlendMode", 1), ("TextureSectionTypeBitMask", -1)),
                R(("ID", 42), ("CharComponentTextureLayoutsID", HdLayout), ("ChrModelTextureTargetID", new[] { 2, 0 }), ("TextureType", 1), ("Layer", 1), ("BlendMode", 4), ("TextureSectionTypeBitMask", 1 << 9)),
                R(("ID", 43), ("CharComponentTextureLayoutsID", HdLayout), ("ChrModelTextureTargetID", new[] { 3, 0 }), ("TextureType", 6), ("Layer", 0), ("BlendMode", 0), ("TextureSectionTypeBitMask", -1)),
                R(("ID", 44), ("CharComponentTextureLayoutsID", SdLayout), ("ChrModelTextureTargetID", new[] { 1, 0 }), ("TextureType", 1), ("Layer", 0), ("BlendMode", 1), ("TextureSectionTypeBitMask", -1)))
            .Add(GameTableNames.ChrCustomizationReq,
                R(("ID", 1), ("ReqType", 4), ("ClassMask", 0), ("OverrideArchive", (sbyte)-1)),              // unlockable: locked
                R(("ID", 2), ("ReqType", 1), ("ClassMask", 1 << (Mage - 1)), ("OverrideArchive", (sbyte)-1)), // mage only
                R(("ID", 3), ("ReqType", 3), ("ClassMask", -1), ("OverrideArchive", (sbyte)-1)),             // everyone
                R(("ID", 4), ("ReqType", 0), ("ClassMask", 0), ("OverrideArchive", (sbyte)1)),               // "Fresh" archive
                R(("ID", 5), ("ReqType", 0), ("ClassMask", 0), ("OverrideArchive", (sbyte)-1), ("ReqAchievementID", 99)));

        // Option 19 "Skin" (flag 0x20, hidden in the UI): 351 locked, 352 needs the other archive,
        // 353 is the first eligible one.
        Option(t, 19, "Skin Color", HdModel, order: 0, flags: 0x20);
        Choice(t, 351, 19, order: 0, req: 1);
        Choice(t, 352, 19, order: 1, req: 4);
        Choice(t, 353, 19, order: 2, req: 3);
        Choice(t, 354, 19, order: 3);
        Material(t, element: 1, choice: 353, material: 900, target: 1, res: 800);
        Material(t, element: 2, choice: 354, material: 901, target: 1, res: 801);
        t.Add(GameTableNames.TextureFileData, R(("FileDataID", 7000), ("MaterialResourcesID", 800), ("UsageType", 0)), R(("FileDataID", 7001), ("MaterialResourcesID", 801), ("UsageType", 0)));

        // Option 20 "Face": 384 is mage-only, so a warrior gets 385. 385 paints the face section and,
        // only together with skin 354, a second layer.
        Option(t, 20, "Face", HdModel, order: 1);
        Choice(t, 384, 20, order: 0, req: 2);
        Choice(t, 385, 20, order: 1);
        Material(t, element: 3, choice: 385, material: 902, target: 2, res: 802);
        Material(t, element: 4, choice: 385, material: 903, target: 3, res: 803, related: 354);
        // Face textures by sex and class: the warrior-specific male one wins.
        t.Add(GameTableNames.TextureFileData,
            R(("FileDataID", 7020), ("MaterialResourcesID", 802), ("UsageType", 0)), R(("FileDataID", 7021), ("MaterialResourcesID", 802), ("UsageType", 0)),
            R(("FileDataID", 7022), ("MaterialResourcesID", 802), ("UsageType", 0)), R(("FileDataID", 7023), ("MaterialResourcesID", 802), ("UsageType", 1)),
            R(("FileDataID", 7030), ("MaterialResourcesID", 803), ("UsageType", 0)));
        t.Add(GameTableNames.ComponentTextureFileData,
            R(("ID", 7020), ("RaceID", Orc), ("GenderIndex", 1), ("ClassID", 0)),
            R(("ID", 7021), ("RaceID", Orc), ("GenderIndex", 3), ("ClassID", 0)),
            R(("ID", 7022), ("RaceID", 0), ("GenderIndex", Male), ("ClassID", Warrior)));

        // Option 30 "Legs": every element of the chosen choice counts (1302 and 2001), and a geoset
        // another choice of the same option names (1301) is turned off.
        Option(t, 30, "Legs", HdModel, order: 2);
        Choice(t, 390, 30, order: 0);
        Choice(t, 391, 30, order: 1);
        Geoset(t, element: 10, choice: 390, geoset: 50, type: 13, id: 2);
        Geoset(t, element: 11, choice: 390, geoset: 51, type: 20, id: 1);
        Geoset(t, element: 12, choice: 391, geoset: 52, type: 13, id: 1);

        // Option 40 is locked as a whole; 41 has only unlockable choices.
        Option(t, 40, "Locked option", HdModel, order: 3, req: 5);
        Choice(t, 400, 40, order: 0);
        Option(t, 41, "Unlockables", HdModel, order: 4);
        Choice(t, 410, 41, order: 0, req: 5);

        // Option 50 has a choice that names a geoset the mesh lacks, and a skinned-model element.
        Option(t, 50, "Jaw", HdModel, order: 5);
        Choice(t, 500, 50, order: 0);
        Geoset(t, element: 20, choice: 500, geoset: 53, type: 2, id: 0);
        t.Add(GameTableNames.ChrCustomizationElement, R(("ID", 21), ("ChrCustomizationChoiceID", 500), ("ChrCustomizationSkinnedModelID", 77)));

        // Option 90 "Jaw features", like the Undead male's: the reset rule shows 101, which no choice
        // names. 950 is the default (102), 951 swaps it for 103.
        Option(t, 90, "Jaw features", HdModel, order: 9);
        Choice(t, 950, 90, order: 0);
        Choice(t, 951, 90, order: 1);
        Geoset(t, element: 190, choice: 950, geoset: 190, type: 1, id: 2);
        Geoset(t, element: 191, choice: 951, geoset: 191, type: 1, id: 3);

        // SD model: its own option and layout.
        Option(t, 9417, "Skin Color", SdModel, order: 0);
        Choice(t, 77751, 9417, order: 0);
        Material(t, element: 30, choice: 77751, material: 910, target: 1, res: 810);
        t.Add(GameTableNames.TextureFileData, R(("FileDataID", 7100), ("MaterialResourcesID", 810), ("UsageType", 0)));
        return t;
    }

    static void Option(InMemoryTables t, int id, string name, int model, int order, int flags = 0, int req = 0) =>
        t.Add(GameTableNames.ChrCustomizationOption, R(("ID", id), ("Name_lang", name), ("ChrModelID", model), ("OrderIndex", order), ("Flags", flags), ("Requirement", req)));

    static void Choice(InMemoryTables t, int id, int option, int order, int req = 0, string name = "") =>
        t.Add(GameTableNames.ChrCustomizationChoice, R(("ID", id), ("Name_lang", name), ("ChrCustomizationOptionID", option), ("OrderIndex", order), ("ChrCustomizationReqID", req)));

    static void Material(InMemoryTables t, int element, int choice, int material, int target, int res, int related = 0)
    {
        t.Add(GameTableNames.ChrCustomizationElement, R(("ID", element), ("ChrCustomizationChoiceID", choice), ("ChrCustomizationMaterialID", material), ("RelatedChrCustomizationChoiceID", related)));
        t.Add(GameTableNames.ChrCustomizationMaterial, R(("ID", material), ("ChrModelTextureTargetID", target), ("MaterialResourcesID", res)));
    }

    static void Geoset(InMemoryTables t, int element, int choice, int geoset, int type, int id)
    {
        t.Add(GameTableNames.ChrCustomizationElement, R(("ID", element), ("ChrCustomizationChoiceID", choice), ("ChrCustomizationGeosetID", geoset)));
        t.Add(GameTableNames.ChrCustomizationGeoset, R(("ID", geoset), ("GeosetType", type), ("GeosetID", id)));
    }

    static CharacterLook Look(ModelSet set = ModelSet.Hd, int classId = Warrior) =>
        new LookResolver(Tables()).Resolve(Orc, Male, classId, set, fdid => fdid == HdBody ? HdMesh : SdMesh)!;

    [Fact]
    public void PicksTheFirstEligibleChoicePerOptionIncludingHiddenOptions()
    {
        var look = Look();
        var defaults = look.Options.ToDictionary(o => o.OptionId, o => o.DefaultChoiceId);

        // 0x20 options still get a default (wow.export leaves them unset).
        Assert.Equal(353, defaults[19]);
        Assert.Equal(385, defaults[20]);
        Assert.Equal(390, defaults[30]);
        Assert.Null(defaults[40]);
        Assert.Null(defaults[41]);
        Assert.Equal([19, 20, 30, 40, 41, 50, 90], look.Options.Select(o => o.OptionId));

        // Options list only the choices a fresh warrior can pick.
        var skin = look.Options[0];
        Assert.Equal([353, 354], skin.Choices.Select(c => c.ChoiceId));
        Assert.Equal([true, false], skin.Choices.Select(c => c.IsDefault));
        Assert.Empty(look.Options.Single(o => o.OptionId == 40).Choices);
        Assert.Empty(look.Options.Single(o => o.OptionId == 41).Choices);

        // The defaults in looks.ts's shape, one per option.
        Assert.Equal([19, 20, 30, 40, 41, 50, 90], look.Choices.Select(c => c.OptionId));
        var skinDefault = look.Choices[0];
        Assert.Equal((353, 2, 2, 4), (skinDefault.ChoiceId!.Value, skinDefault.OrderIndex!.Value, skinDefault.EligibleChoices, skinDefault.TotalChoices));
        // wow.export leaves 0x20 options unset and otherwise takes the lowest choice ID.
        Assert.Null(skinDefault.WowExportChoiceId);
        Assert.Equal(384, look.Choices[1].WowExportChoiceId);
        Assert.NotNull(look.Choices.Single(c => c.OptionId == 40).Skipped);
        Assert.Equal("no eligible choice", look.Choices.Single(c => c.OptionId == 41).Skipped);
    }

    [Fact]
    public void ClassRequirementsChangeTheDefault()
    {
        Assert.Equal(384, Look(classId: Mage).Options.Single(o => o.OptionId == 20).DefaultChoiceId);
    }

    [Fact]
    public void DefaultGeosetsFollowTheResetRuleWithEar702AndEveryChoiceElement()
    {
        var look = Look();
        // 701 is "no ears", so 702 is the default; 17xx and 35xx stay hidden; 1301 is turned off by
        // option 30, whose chosen choice turns on both 1302 and 2001. 200 (jaw) is not in the mesh.
        // Option 90's default 102 replaces 101, the reset rule's pick for group 1.
        Assert.Equal([0, 102, 401, 501, 702, 1302, 2001, 3201], look.Geosets);
        Assert.Contains(look.Notes, n => n.Contains("200"));
    }

    [Fact]
    public void OnlyCharacterCreationChoicesAreOfferedWhereAnOptionHasThem()
    {
        // Like the Orc's skin colors: 353-354 use a regular requirement (ReqType 3), and 362 is one of the
        // extras with ReqType 2. Option 80 is like the Skyborne's skin: an extra is listed first, so it would be
        // the default, but character creation offers only 801-802 (9 colors in game, not 39). Option 70 is
        // like Eye Style, where every choice is ReqType 2, so it keeps them all.
        var t = Tables();
        t.Add(GameTableNames.ChrCustomizationReq, R(("ID", 12), ("ReqType", 2), ("ClassMask", 0), ("OverrideArchive", (sbyte)-1)));
        Choice(t, 362, 19, order: 9, req: 12);
        Option(t, 80, "Skin Color B", HdModel, order: 8);
        Choice(t, 800, 80, order: 0, req: 12);
        Choice(t, 801, 80, order: 1, req: 3);
        Choice(t, 802, 80, order: 2, req: 3);
        Option(t, 70, "Eye Style", HdModel, order: 7);
        Choice(t, 700, 70, order: 0, req: 12);
        Choice(t, 701, 70, order: 1, req: 12);
        var look = new LookResolver(t).Resolve(Orc, Male, Warrior, ModelSet.Hd, _ => HdMesh)!;

        Assert.Equal([353, 354], look.Options.Single(o => o.OptionId == 19).Choices.Select(c => c.ChoiceId));
        var b = look.Options.Single(o => o.OptionId == 80);
        Assert.Equal([801, 802], b.Choices.Select(c => c.ChoiceId));
        Assert.Equal(801, b.DefaultChoiceId);
        Assert.Equal([700, 701], look.Options.Single(o => o.OptionId == 70).Choices.Select(c => c.ChoiceId));
        Assert.Equal(700, look.Options.Single(o => o.OptionId == 70).DefaultChoiceId);
    }

    [Fact]
    public void SdDefaultsFollowTheHdDefaultByOptionAndChoiceName()
    {
        // The SD Undead "Eye Glow" option lists None first, but HD's default is Glow. An SD option
        // takes the HD default when both option and choice names match exactly once.
        var t = Tables();
        Option(t, 60, "Eye Glow", HdModel, order: 6);
        Choice(t, 600, 60, order: 0, name: "Glow");
        Choice(t, 601, 60, order: 8, name: "None");
        Option(t, 9481, "Eye Glow", SdModel, order: 1);
        Choice(t, 78855, 9481, order: 0, name: "None");
        Choice(t, 78856, 9481, order: 1, name: "Glow");
        var sd = new LookResolver(t).Resolve(Orc, Male, Warrior, ModelSet.Sd, _ => SdMesh)!;

        Assert.Equal(78856, sd.Options.Single(o => o.OptionId == 9481).DefaultChoiceId);
        Assert.Equal(78856, sd.Choices.Single(c => c.OptionId == 9481).ChoiceId);
        // Unnamed choices have nothing to match, so the SD skin keeps its own first choice.
        Assert.Equal(77751, sd.Options.Single(o => o.OptionId == 9417).DefaultChoiceId);
    }

    [Fact]
    public void MeshGeosetsListEveryGeosetInTheBodyModel()
    {
        // The web app filters non-default choices' geosets by this list, the way the server filters the defaults.
        var look = Look();
        Assert.DoesNotContain(200, look.MeshGeosets);
        Assert.Contains(1301, look.MeshGeosets);
        Assert.Equal(look.MeshGeosets.Order(), look.MeshGeosets);
    }

    [Fact]
    public void TextureLayersResolveSectionsAndRaceGenderClassTextures()
    {
        var look = Look();

        Assert.Equal(2, look.Layers.Count);
        var skin = look.Layers[0];
        Assert.Equal((1, 0, 1, 1, 7000, 353, 19), (skin.TextureType, skin.Layer, skin.BlendMode, skin.Target, skin.FileDataId, skin.ChoiceId, skin.OptionId));
        Assert.Equal(new SectionRect(-1, 0, 0, 2048, 1024), skin.Section);

        var face = look.Layers[1];
        Assert.Equal((1, 1, 7022), (face.TextureType, face.Layer, face.FileDataId));
        Assert.Equal(new SectionRect(9, 512, 0, 512, 512), face.Section);
        Assert.Equal([7020, 7021, 7022], face.Candidates!);
    }

    [Fact]
    public void RelatedChoiceLayersOnlyApplyWithTheirChoiceButAreListedPerChoice()
    {
        var look = Look();
        Assert.DoesNotContain(look.Layers, l => l.Target == 3);

        var face = look.Options.Single(o => o.OptionId == 20).Choices.Single(c => c.ChoiceId == 385);
        Assert.Equal([2, 3], face.Layers.Select(l => l.Target));
        Assert.Equal([null, 354], face.Layers.Select(l => l.RelatedChoiceId));
    }

    [Fact]
    public void ChoicesCarryTheirGeosetsAndOptionsTheGeosetsTheyControl()
    {
        var legs = Look().Options.Single(o => o.OptionId == 30);
        Assert.Equal([1301, 1302, 2001], legs.Geosets);
        Assert.Equal([1302, 2001], legs.Choices[0].Geosets);
        Assert.Equal([1301], legs.Choices[1].Geosets);
    }

    [Fact]
    public void ChoicesCarryTheirSwatchColor()
    {
        var t = Tables().Add(GameTableNames.ChrCustomizationOption, R(("ID", 60), ("Name_lang", "Hair Color"), ("ChrModelID", HdModel), ("OrderIndex", 6)))
            .Add(GameTableNames.ChrCustomizationChoice,
                R(("ID", 600), ("Name_lang", "Red"), ("ChrCustomizationOptionID", 60), ("OrderIndex", 0), ("SwatchColor", new[] { unchecked((int)0xFFC03020), 0 })),
                R(("ID", 601), ("Name_lang", "Plain"), ("ChrCustomizationOptionID", 60), ("OrderIndex", 1), ("SwatchColor", new[] { 0, 0 })));
        var look = new LookResolver(t).Resolve(Orc, Male, Warrior, ModelSet.Hd, _ => HdMesh)!;
        var hair = look.Options.Single(o => o.OptionId == 60);
        Assert.Equal(["#c03020", null], hair.Choices.Select(c => c.Swatch));
    }

    [Fact]
    public void ReportsLayoutSectionsAndSectionLayers()
    {
        var look = Look();
        Assert.Equal(new LayoutInfo(HdLayout, 2048, 1024), look.Layout);
        Assert.Equal([new TextureSize(1, 2048, 1024), new TextureSize(6, 512, 256)], look.Textures);
        Assert.Equal([new SectionRect(0, 0, 0, 512, 256), new SectionRect(9, 512, 0, 512, 512)], look.Sections);
        // No layer masks sections 0-8 here, so every item section falls back to the skin layer 41,
        // whose blend mode 1 becomes 15 (alpha) for items.
        Assert.Equal(9, look.SectionLayers.Count);
        Assert.All(look.SectionLayers, s => Assert.Equal((1, 15, 41), (s.TextureType, s.BlendMode, s.FromLayer)));
        Assert.Single(look.UnsupportedElements);
    }

    [Fact]
    public void PlayersSeeOnlyOptionsCharacterCreationOffers()
    {
        // Like HD Eye Style: the option itself carries a ReqType 2 requirement, which character creation
        // does not offer (no race shows Eye Style in game). Option 50 (one choice), 40 and 41 (none) and,
        // for a warrior, 20 (one face) have nothing to pick between, so the game shows no dropdown.
        var t = Tables();
        t.Add(GameTableNames.ChrCustomizationReq, R(("ID", 12), ("ReqType", 2), ("ClassMask", 0), ("OverrideArchive", (sbyte)-1)));
        Option(t, 95, "Eye Style", HdModel, order: 9, req: 12);
        Choice(t, 960, 95, order: 0, req: 12);
        Choice(t, 961, 95, order: 1, req: 12);
        var look = new LookResolver(t).Resolve(Orc, Male, Warrior, ModelSet.Hd, _ => HdMesh)!;

        Assert.Equal([19, 30, 90], look.Options.Where(o => o.Offered).Select(o => o.OptionId));
        Assert.Equal([19, 30, 90], look.ForPlayer().Options.Select(o => o.OptionId));
        // Hidden options still apply their defaults: the look is the same, only the dropdowns go.
        Assert.Equal(look.Geosets, look.ForPlayer().Geosets);
        Assert.Contains(look.ForPlayer().Choices, c => c.OptionId == 50);
    }

    [Fact]
    public void BaseLookCarriesModelIds()
    {
        var look = Look();
        Assert.Equal((HdModel, HdModel, HdBody, HdLayout, "hd"), (look.ChrModelId, look.HdChrModelId, look.ModelFileDataId, look.TextureLayoutId, look.Models));
    }

    [Fact]
    public void SdLooksUseTheAltVariantModelAndItsLayout()
    {
        var look = Look(ModelSet.Sd);
        Assert.Equal((SdModel, HdModel, SdBody, SdLayout, "sd"), (look.ChrModelId, look.HdChrModelId, look.ModelFileDataId, look.TextureLayoutId, look.Models));
        Assert.Equal([0, 401, 702], look.Geosets);
        Assert.Equal([7100], look.Layers.Select(l => l.FileDataId));
        Assert.Equal(9417, Assert.Single(look.Options).OptionId);
    }

    [Fact]
    public void ReturnsNullWithoutABody()
    {
        var r = new LookResolver(Tables());
        Assert.Null(r.Resolve(99, Male, Warrior, ModelSet.Hd, _ => []));
        var noSd = new LookResolver(new InMemoryTables().Add(GameTableNames.ChrRaceXChrModel, R(("ID", 1), ("ChrRacesID", Orc), ("Sex", Male), ("ChrModelID", HdModel))));
        Assert.Null(noSd.Resolve(Orc, Male, Warrior, ModelSet.Sd, _ => []));
    }
}
