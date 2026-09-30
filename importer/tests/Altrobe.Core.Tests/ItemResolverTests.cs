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
        int[]? geosetGroup = null, int[]? helmVis = null, bool addDisplay = true, bool sparse = true)
    {
        if (sparse) t.Add(GameTableNames.ItemSparse, R(("ID", id), ("Display_lang", name), ("InventoryType", (byte)inventoryType)));
        t.Add(GameTableNames.Item, R(("ID", id), ("InventoryType", (byte)inventoryType)));
        t.Add(GameTableNames.ItemModifiedAppearance,
            R(("ID", id * 10 + 1), ("ItemID", id), ("ItemAppearanceModifierID", 1), ("OrderIndex", 0), ("ItemAppearanceID", 999999)),
            R(("ID", id * 10), ("ItemID", id), ("ItemAppearanceModifierID", 0), ("OrderIndex", 0), ("ItemAppearanceID", id)));
        t.Add(GameTableNames.ItemAppearance, R(("ID", id), ("ItemDisplayInfoID", display)));
        if (addDisplay)
            t.Add(GameTableNames.ItemDisplayInfo, R(("ID", display), ("ModelResourcesID", modelRes), ("ModelMaterialResourcesID", matRes),
                ("GeosetGroup", geosetGroup ?? new int[6]), ("AttachmentGeosetGroup", new int[6]), ("HelmetGeosetVis", helmVis ?? new int[2])));
    }

    static ResolvedItem Resolve(int itemId, int race, int sex) => new ItemResolver(Tables()).Resolve(itemId, race, sex);

    [Fact]
    public void AnItemWithoutAnItemSparseRowTakesItsSlotFromItem()
    {
        var r = Resolve(800, Orc, Male);
        Assert.Equal(5, r.InventoryType);
        Assert.Equal("(no ItemSparse row)", r.Name);
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
