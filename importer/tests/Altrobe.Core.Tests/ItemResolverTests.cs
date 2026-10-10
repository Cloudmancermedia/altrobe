using Altrobe.Core.Resolve;
using Altrobe.Core.Tables;
using static Altrobe.Core.Tests.Fake;

namespace Altrobe.Core.Tests;

public class ItemResolverTests
{
    const int Orc = 2, Undead = 5, Male = 0, Female = 1;

    // One item per scenario, each with its own display. IDs are arbitrary.
    static InMemoryTables Tables()
    {
        var t = new InMemoryTables()
            .Add(GameTableNames.ChrRaces,
                R(("ID", Orc), ("MaleModelFallbackRaceID", 0), ("FemaleModelFallbackRaceID", 0), ("MaleTextureFallbackRaceID", 0), ("FemaleTextureFallbackRaceID", 0)),
                // Undead fall back to Human (1) for textures, as some real races do.
                R(("ID", Undead), ("MaleModelFallbackRaceID", 1), ("FemaleModelFallbackRaceID", 1), ("MaleTextureFallbackRaceID", 1), ("FemaleTextureFallbackRaceID", 1)));

        Item(t, 100, "Sword", inventoryType: 13, display: 1000, modelRes: [10, 0], matRes: [20, 0]);
        t.Add(GameTableNames.ModelFileData, R(("FileDataID", 5001), ("ModelResourcesID", 10)));
        t.Add(GameTableNames.TextureFileData, R(("FileDataID", 6001), ("MaterialResourcesID", 20), ("UsageType", 0)));

        // Shoulders: one resource with a left (PositionIndex 0) and right (1) model per race.
        Item(t, 200, "Pauldrons", inventoryType: 3, display: 2000, modelRes: [11, 11], matRes: [21, 21]);
        t.Add(GameTableNames.ModelFileData, R(("FileDataID", 5101), ("ModelResourcesID", 11)), R(("FileDataID", 5102), ("ModelResourcesID", 11)),
            R(("FileDataID", 5103), ("ModelResourcesID", 11)));
        t.Add(GameTableNames.ComponentModelFileData,
            R(("ID", 5101), ("RaceID", 0), ("GenderIndex", 2), ("ClassID", 0), ("PositionIndex", 0)),
            R(("ID", 5102), ("RaceID", 0), ("GenderIndex", 2), ("ClassID", 0), ("PositionIndex", 1)),
            R(("ID", 5103), ("RaceID", Orc), ("GenderIndex", Female), ("ClassID", 0), ("PositionIndex", 0)));
        t.Add(GameTableNames.TextureFileData, R(("FileDataID", 6101), ("MaterialResourcesID", 21), ("UsageType", 0)));

        // Chest with body textures chosen by race and sex, with a race fallback.
        Item(t, 300, "Robe", inventoryType: 20, display: 3000, modelRes: [0, 0], matRes: [0, 0], geosetGroup: [1, 0, 1, 0, 0, 0]);
        t.Add(GameTableNames.ItemDisplayInfoMaterialRes,
            R(("ID", 1), ("ItemDisplayInfoID", 3000), ("ComponentSection", 0), ("MaterialResourcesID", 30)),
            R(("ID", 2), ("ItemDisplayInfoID", 3000), ("ComponentSection", 3), ("MaterialResourcesID", 31)));
        t.Add(GameTableNames.TextureFileData,
            R(("FileDataID", 6301), ("MaterialResourcesID", 30), ("UsageType", 0)), R(("FileDataID", 6302), ("MaterialResourcesID", 30), ("UsageType", 0)),
            R(("FileDataID", 6303), ("MaterialResourcesID", 30), ("UsageType", 0)), R(("FileDataID", 6304), ("MaterialResourcesID", 31), ("UsageType", 0)),
            R(("FileDataID", 6305), ("MaterialResourcesID", 31), ("UsageType", 0)));
        t.Add(GameTableNames.ComponentTextureFileData,
            R(("ID", 6301), ("RaceID", Orc), ("GenderIndex", Male), ("ClassID", 0)),
            R(("ID", 6302), ("RaceID", 1), ("GenderIndex", Male), ("ClassID", 0)),
            R(("ID", 6303), ("RaceID", 0), ("GenderIndex", 3), ("ClassID", 0)),
            R(("ID", 6304), ("RaceID", 0), ("GenderIndex", Female), ("ClassID", 0)),
            R(("ID", 6305), ("RaceID", 0), ("GenderIndex", Male), ("ClassID", 0)));

        // Like the Phalanx Breastplate: the lower arm lists one texture, tagged female only.
        Item(t, 310, "One-file sleeves", inventoryType: 5, display: 3100, modelRes: [0, 0], matRes: [0, 0]);
        t.Add(GameTableNames.ItemDisplayInfoMaterialRes, R(("ID", 3), ("ItemDisplayInfoID", 3100), ("ComponentSection", 1), ("MaterialResourcesID", 32)));
        t.Add(GameTableNames.TextureFileData, R(("FileDataID", 6306), ("MaterialResourcesID", 32), ("UsageType", 0)));
        t.Add(GameTableNames.ComponentTextureFileData, R(("ID", 6306), ("RaceID", 0), ("GenderIndex", Female), ("ClassID", 0)));

        // Helm with hide lists chosen by sex, then filtered by race.
        Item(t, 400, "Helm", inventoryType: 1, display: 4000, modelRes: [12, 0], matRes: [22, 0], helmVis: [40, 41]);
        t.Add(GameTableNames.ModelFileData, R(("FileDataID", 5401), ("ModelResourcesID", 12)));
        t.Add(GameTableNames.HelmetGeosetData,
            R(("ID", 1), ("HelmetGeosetVisDataID", 40), ("RaceID", Orc), ("HideGeosetGroup", 7)),
            R(("ID", 2), ("HelmetGeosetVisDataID", 40), ("RaceID", 0), ("HideGeosetGroup", 1)),
            R(("ID", 3), ("HelmetGeosetVisDataID", 40), ("RaceID", Undead), ("HideGeosetGroup", 16)),
            R(("ID", 4), ("HelmetGeosetVisDataID", 40), ("RaceID", Orc), ("HideGeosetGroup", 1)),
            R(("ID", 5), ("HelmetGeosetVisDataID", 41), ("RaceID", 0), ("HideGeosetGroup", 3)));

        // Cloak with no model: its texture goes on the body's cape geoset.
        Item(t, 500, "Cloak", inventoryType: 16, display: 5000, modelRes: [0, 0], matRes: [25, 0], geosetGroup: [1, 0, 0, 0, 0, 0]);
        t.Add(GameTableNames.TextureFileData, R(("FileDataID", 6501), ("MaterialResourcesID", 25), ("UsageType", 0)));

        // Broken chains.
        t.Add(GameTableNames.ItemSparse, R(("ID", 600), ("Display_lang", "No appearance"), ("InventoryType", 5)));
        Item(t, 700, "No display", inventoryType: 5, display: 7000, modelRes: [0, 0], matRes: [0, 0], addDisplay: false);
        // A piece with a model but no ItemSparse row, like tier 2 in the Forever beta.
        Item(t, 800, "unused", inventoryType: 5, display: 8000, modelRes: [0, 0], matRes: [0, 0], sparse: false);
        return t;
    }

    static void Item(InMemoryTables t, int id, string name, int inventoryType, int display, int[] modelRes, int[] matRes,
        int[]? geosetGroup = null, int[]? helmVis = null, bool addDisplay = true, bool sparse = true, int itemVisual = 0, int sheatheType = 0, int subclass = 0)
    {
        if (sparse) t.Add(GameTableNames.ItemSparse, R(("ID", id), ("Display_lang", name), ("InventoryType", (byte)inventoryType)));
        t.Add(GameTableNames.Item, R(("ID", id), ("InventoryType", (byte)inventoryType), ("SheatheType", sheatheType), ("SubclassID", subclass)));
        t.Add(GameTableNames.ItemModifiedAppearance,
            R(("ID", id * 10 + 1), ("ItemID", id), ("ItemAppearanceModifierID", 1), ("OrderIndex", 0), ("ItemAppearanceID", 999999)),
            R(("ID", id * 10), ("ItemID", id), ("ItemAppearanceModifierID", 0), ("OrderIndex", 0), ("ItemAppearanceID", id)));
        t.Add(GameTableNames.ItemAppearance, R(("ID", id), ("ItemDisplayInfoID", display)));
        if (addDisplay)
            t.Add(GameTableNames.ItemDisplayInfo, R(("ID", display), ("ModelResourcesID", modelRes), ("ModelMaterialResourcesID", matRes),
                ("GeosetGroup", geosetGroup ?? new int[6]), ("AttachmentGeosetGroup", new int[6]), ("HelmetGeosetVis", helmVis ?? new int[2]), ("ItemVisual", itemVisual)));
    }

    static ResolvedItem Resolve(int itemId, int race, int sex) => new ItemResolver(Tables()).Resolve(itemId, race, sex);

    [Fact]
    public void AShieldHangsOnTheShieldPointAndAHeldItemInTheLeftHand()
    {
        // M2 attachment 0 is the shield point on the forearm, 2 the left hand (wowdev.wiki/M2#Attachments).
        var t = Tables();
        Item(t, 900, "Shield", inventoryType: 14, display: 9000, modelRes: [10, 0], matRes: [20, 0]);
        Item(t, 901, "Orb", inventoryType: 23, display: 9010, modelRes: [10, 0], matRes: [20, 0]);
        var resolver = new ItemResolver(t);
        Assert.Equal([new Attachment(17, 0, 5001, 6001)], resolver.Resolve(900, Orc, Male).Attachments);
        Assert.Equal([new Attachment(17, 2, 5001, 6001)], resolver.Resolve(901, Orc, Male).Attachments);
    }

    [Fact]
    public void AWeaponsItemVisualPutsEachEffectModelOnTheWeaponsMatchingAttachment()
    {
        // ItemVisuals column i goes on the weapon model's attachment i, 0 to 4 (wowdev.wiki/M2#Attachments),
        // like The Unstoppable Force's glow. Empty columns add nothing.
        var t = Tables();
        Item(t, 950, "Glowing mace", inventoryType: 17, display: 9500, modelRes: [10, 0], matRes: [20, 0], itemVisual: 124);
        Item(t, 951, "Glowing dagger", inventoryType: 22, display: 9510, modelRes: [10, 0], matRes: [20, 0], itemVisual: 125);
        t.Add(GameTableNames.ItemVisuals,
            R(("ID", 124), ("ModelFileID", new[] { 165992, 165992, 0, 165993, 0 })),
            R(("ID", 125), ("ModelFileID", new[] { 0, 0, 0, 0, 165994 })));
        var resolver = new ItemResolver(t);

        Assert.Equal([new ItemEffect(0, 165992), new ItemEffect(1, 165992), new ItemEffect(3, 165993)],
            resolver.Resolve(950, Orc, Male).Effects);
        Assert.Equal([new ItemEffect(4, 165994)], resolver.Resolve(951, Orc, Male).Effects);
    }

    [Fact]
    public void AnItemWithoutAnItemVisualOrABuildWithoutTheTableHasNoEffects()
    {
        Assert.Empty(Resolve(100, Orc, Male).Effects);
        var t = Tables();
        Item(t, 952, "Mace", inventoryType: 17, display: 9520, modelRes: [10, 0], matRes: [20, 0], itemVisual: 124);
        Assert.Empty(new ItemResolver(t).Resolve(952, Orc, Male).Effects);
    }

    [Fact]
    public void AnItemWithoutAnItemSparseRowTakesItsSlotFromItem()
    {
        var r = Resolve(800, Orc, Male);
        Assert.Equal(5, r.InventoryType);
        Assert.Equal("(no ItemSparse row)", r.Name);
    }

    [Fact]
    public void RangedWeaponsGoInTheRangedSlotAndWeaponsCarryTheirSheathTypeAndSubclass()
    {
        // Bows (15), thrown (25) and guns, crossbows and wands (26) take the ranged slot, 18. The viewer
        // needs SheatheType (Item) to sheathe a weapon and the subclass to tell a bow from a gun.
        var t = Tables();
        Item(t, 960, "Bow", inventoryType: 15, display: 9600, modelRes: [10, 0], matRes: [20, 0]);
        Item(t, 961, "Throwing axe", inventoryType: 25, display: 9610, modelRes: [10, 0], matRes: [20, 0]);
        Item(t, 962, "Dagger", inventoryType: 13, display: 9620, modelRes: [10, 0], matRes: [20, 0], sheatheType: 3, subclass: 15);
        var resolver = new ItemResolver(t);

        Assert.Equal(18, resolver.Resolve(960, Orc, Male).EquipSlot);
        Assert.Equal(18, resolver.Resolve(961, Orc, Male).EquipSlot);
        var dagger = resolver.Resolve(962, Orc, Male);
        Assert.Equal((16, 3, 15), (dagger.EquipSlot, dagger.SheatheType, dagger.ItemSubclass));
    }

    [Fact]
    public void AWeaponResolvesToANeutralModelAndItsTexture()
    {
        var r = Resolve(100, Orc, Male);

        Assert.Null(r.Error);
        Assert.Equal("Sword", r.Name);
        Assert.Equal(13, r.InventoryType);
        Assert.Equal(16, r.EquipSlot);
        Assert.Equal(100, r.ItemAppearanceId);
        Assert.Equal(1000, r.ItemDisplayInfoId);
        var slot = Assert.Single(r.Models);
        Assert.Equal((0, 10), (slot.Slot, slot.ModelResourcesId));
        Assert.Equal([new FilePick(5001, "neutral")], slot.Models);
        Assert.Equal([new FilePick(6001, "neutral")], slot.Textures);
        var a = Assert.Single(r.Attachments);
        Assert.Equal(new Attachment(16, 1, 5001, 6001), a);
    }

    [Fact]
    public void ShouldersPickLeftAndRightModelsByPositionIndex()
    {
        var r = Resolve(200, Orc, Male);

        var models = r.Models[0].Models;
        Assert.Equal([new FilePick(5101, "any-race", 0), new FilePick(5102, "any-race", 1)], models);
        Assert.Equal([new Attachment(3, 6, 5101, 6101), new Attachment(30, 5, 5102, 6101)], r.Attachments);
    }

    [Fact]
    public void RaceAndSexSpecificComponentFilesWinOverAnyRace()
    {
        var r = Resolve(200, Orc, Female);
        Assert.Equal([new FilePick(5103, "race+sex", 0)], r.Models[0].Models);
    }

    [Fact]
    public void ASectionsOnlyTextureIsUsedWhateverItsSexTag()
    {
        // wow.export and WoW Model Viewer use a lone file as-is: with one option there is nothing to choose.
        var orc = Resolve(310, Orc, Male);
        Assert.Equal([new FilePick(6306, "only-file")], orc.BodyTextures.Single().Textures);
    }

    [Fact]
    public void BodyTexturesAreChosenPerSectionByRaceSexAndFallback()
    {
        var orc = Resolve(300, Orc, Male);
        Assert.Empty(orc.Models);
        Assert.Equal([0, 3], orc.BodyTextures.Select(b => b.Section));
        Assert.Equal([new FilePick(6301, "race+sex")], orc.BodyTextures[0].Textures);
        Assert.Equal([new FilePick(6305, "any-race")], orc.BodyTextures[1].Textures);

        var undead = Resolve(300, Undead, Male);
        Assert.Equal([new FilePick(6302, "fallback-race")], undead.BodyTextures[0].Textures);

        // GenderIndex 3 counts as "any sex".
        var undeadFemale = Resolve(300, Undead, Female);
        Assert.Equal([new FilePick(6303, "any-race")], undeadFemale.BodyTextures[0].Textures);
        Assert.Equal([new FilePick(6304, "any-race")], undeadFemale.BodyTextures[1].Textures);
    }

    [Fact]
    public void ItemGeosetGroupsBecomeGeosetsForTheSlot()
    {
        var r = Resolve(300, Orc, Male);
        Assert.Equal([1, 0, 1, 0, 0, 0], r.GeosetGroup);
        Assert.Equal(5, r.EquipSlot);
        Assert.Equal([new GeosetChange(8, 802), new GeosetChange(10, 1001), new GeosetChange(13, 1302), new GeosetChange(22, 2201), new GeosetChange(28, 2801)], r.Geosets);
    }

    [Fact]
    public void HelmHidesAreChosenBySexThenFilteredByRace()
    {
        Assert.Equal([7, 1], Resolve(400, Orc, Male).HelmHideGeosetGroups);
        Assert.Equal([1, 16], Resolve(400, Undead, Male).HelmHideGeosetGroups);
        Assert.Equal([3], Resolve(400, Orc, Female).HelmHideGeosetGroups);
        Assert.Equal([new Attachment(1, 11, 5401, null)], Resolve(400, Orc, Male).Attachments);
    }

    [Fact]
    public void AHelmHideCarriesTheOptionFlagThatKeepsTheGroup()
    {
        // Like Helm of Might (HelmetGeosetVis 247): its facial-group rows carry 32 in HelmetGeosetData's
        // unnamed last column, -1 elsewhere. The viewer keeps such a group when the customization option
        // filling it has that flag (the Undead jaw). A -1 row for the same group always hides it.
        var t = Tables();
        Item(t, 410, "Helm of Might", inventoryType: 1, display: 4100, modelRes: [12, 0], matRes: [22, 0], helmVis: [42, 42]);
        const string Keep = "Field_10_0_0_46047_003";
        t.Add(GameTableNames.HelmetGeosetData,
            R(("ID", 10), ("HelmetGeosetVisDataID", 42), ("RaceID", Undead), ("HideGeosetGroup", 1), (Keep, 32)),
            R(("ID", 11), ("HelmetGeosetVisDataID", 42), ("RaceID", Undead), ("HideGeosetGroup", 2), (Keep, 32)),
            R(("ID", 12), ("HelmetGeosetVisDataID", 42), ("RaceID", Orc), ("HideGeosetGroup", 3), (Keep, -1)),
            R(("ID", 13), ("HelmetGeosetVisDataID", 42), ("RaceID", Orc), ("HideGeosetGroup", 1), (Keep, 32)),
            R(("ID", 14), ("HelmetGeosetVisDataID", 42), ("RaceID", 0), ("HideGeosetGroup", 1), (Keep, -1)));
        var resolver = new ItemResolver(t);

        Assert.Equal([new HelmHide(1, 0), new HelmHide(2, 32)], resolver.Resolve(410, Undead, Male).HelmHides);
        Assert.Equal([new HelmHide(3, 0), new HelmHide(1, 0)], resolver.Resolve(410, Orc, Male).HelmHides);
        Assert.Equal([1, 2], resolver.Resolve(410, Undead, Male).HelmHideGeosetGroups);
    }

    [Fact]
    public void AnItemPaintsOnlyTheBodySectionsItsSlotCovers()
    {
        // Some late displays list body textures outside their slot: tier 2.5 shoulders carry legs, the
        // Frostfire Bindings a red torso. The game shows neither, so each slot keeps only its own sections.
        var t = Tables();
        void WithSections(int id, int inventoryType, params int[] sections)
        {
            Item(t, id, $"Item {id}", inventoryType, display: id * 10, modelRes: [0, 0], matRes: [0, 0]);
            foreach (var sec in sections)
            {
                t.Add(GameTableNames.ItemDisplayInfoMaterialRes, R(("ID", id * 10 + sec), ("ItemDisplayInfoID", id * 10), ("ComponentSection", sec), ("MaterialResourcesID", id * 10 + sec)));
                t.Add(GameTableNames.TextureFileData, R(("FileDataID", id * 100 + sec), ("MaterialResourcesID", id * 10 + sec), ("UsageType", 0)));
            }
        }
        WithSections(430, 3, 3, 4, 5, 6);   // shoulders
        WithSections(431, 7, 3, 4, 5, 6);   // legs
        WithSections(432, 9, 0, 1, 3, 7);   // bracers
        WithSections(433, 20, 0, 1, 3, 4, 5, 6); // robe
        var resolver = new ItemResolver(t);
        int[] Sections(int id) => resolver.Resolve(id, Orc, Male).BodyTextures.Select(b => b.Section).ToArray();

        Assert.Empty(Sections(430));
        Assert.Equal([5, 6], Sections(431));
        Assert.Equal([1], Sections(432));
        Assert.Equal([0, 1, 3, 4, 5, 6], Sections(433));
    }

    [Fact]
    public void BootsLeaveTheFeetBareOnRacesWithTheBareFeetFlag()
    {
        // ChrRaces flag 0x2 is "Bare Feet" (wowdev.wiki DB/ChrRaces): Tauren and Trolls keep their feet
        // and hooves in boots, which cover only the lower leg. WoW Model Viewer skips the foot texture
        // (section 7) for them and keeps the leg texture and boot geosets.
        var t = Tables();
        const int Tauren = 6;
        t.Add(GameTableNames.ChrRaces, R(("ID", Tauren), ("Flags", 0x60000e), ("MaleModelFallbackRaceID", 0), ("FemaleModelFallbackRaceID", 0), ("MaleTextureFallbackRaceID", 0), ("FemaleTextureFallbackRaceID", 0)));
        Item(t, 420, "Boots", inventoryType: 8, display: 4200, modelRes: [0, 0], matRes: [0, 0], geosetGroup: [1, 0, 0, 0, 0, 0]);
        t.Add(GameTableNames.ItemDisplayInfoMaterialRes,
            R(("ID", 20), ("ItemDisplayInfoID", 4200), ("ComponentSection", 6), ("MaterialResourcesID", 60)),
            R(("ID", 21), ("ItemDisplayInfoID", 4200), ("ComponentSection", 7), ("MaterialResourcesID", 61)));
        t.Add(GameTableNames.TextureFileData,
            R(("FileDataID", 6601), ("MaterialResourcesID", 60), ("UsageType", 0)), R(("FileDataID", 6602), ("MaterialResourcesID", 61), ("UsageType", 0)));
        var resolver = new ItemResolver(t);

        Assert.Equal([6, 7], resolver.Resolve(420, Orc, Male).BodyTextures.Select(b => b.Section));
        var tauren = resolver.Resolve(420, Tauren, Male);
        Assert.Equal([6], tauren.BodyTextures.Select(b => b.Section));
        Assert.Equal([new GeosetChange(5, 502), new GeosetChange(20, 2002)], tauren.Geosets);
    }

    [Fact]
    public void ACloakWithoutAModelPaintsTheCapeGeoset()
    {
        var r = Resolve(500, Orc, Male);
        var slot = Assert.Single(r.Models);
        Assert.Empty(slot.Models);
        Assert.Equal(0, slot.ModelResourcesId);
        Assert.Equal(new Dictionary<int, int> { [2] = 6501 }, r.BodyReplaceableTextures);
        Assert.Empty(r.Attachments);
        Assert.Equal([new GeosetChange(15, 1502)], r.Geosets);
    }

    [Fact]
    public void BrokenChainsReportAnError()
    {
        Assert.Equal("no ItemModifiedAppearance", Resolve(600, Orc, Male).Error);
        Assert.Equal("no ItemDisplayInfo 7000", Resolve(700, Orc, Male).Error);
        var missing = Resolve(12345, Orc, Male);
        Assert.Equal("no ItemModifiedAppearance", missing.Error);
        Assert.Equal("(no ItemSparse row)", missing.Name);
    }
}
