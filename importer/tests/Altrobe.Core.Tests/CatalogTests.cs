using Altrobe.Core.Catalog;
using Altrobe.Core.Tables;
using static Altrobe.Core.Tests.Fake;

namespace Altrobe.Core.Tests;

public class ItemCatalogTests
{
    // Items 1-4 have a visual; 5 has an appearance with no display, 6 has no appearance at all,
    // 7 has a visual but no name row.
    static ItemCatalog Catalog()
    {
        var t = new InMemoryTables()
            .Add(GameTableNames.ItemSparse,
                R(("ID", 1), ("Display_lang", "Robe of the Archmage"), ("InventoryType", (byte)20), ("OverallQualityID", (byte)4)),
                R(("ID", 2), ("Display_lang", "Thunderfury, Blessed Blade of the Windseeker"), ("InventoryType", (byte)13), ("OverallQualityID", (byte)5)),
                R(("ID", 3), ("Display_lang", "Robe of Winter Night"), ("InventoryType", (byte)20), ("OverallQualityID", (byte)3)),
                R(("ID", 4), ("Display_lang", "Frostweave Robe"), ("InventoryType", (byte)5), ("OverallQualityID", (byte)2)),
                R(("ID", 5), ("Display_lang", "Robe with no display"), ("InventoryType", (byte)20), ("OverallQualityID", (byte)1)),
                R(("ID", 6), ("Display_lang", "Robe with no appearance"), ("InventoryType", (byte)20), ("OverallQualityID", (byte)1)))
            .Add(GameTableNames.Item,
                R(("ID", 1), ("IconFileDataID", 1001)), R(("ID", 2), ("IconFileDataID", 1002)), R(("ID", 3), ("IconFileDataID", 1003)),
                R(("ID", 4), ("IconFileDataID", 1004)), R(("ID", 7), ("IconFileDataID", 1007)))
            .Add(GameTableNames.ItemModifiedAppearance,
                R(("ID", 10), ("ItemID", 1), ("ItemAppearanceModifierID", 0), ("OrderIndex", 0), ("ItemAppearanceID", 100)),
                R(("ID", 11), ("ItemID", 2), ("ItemAppearanceModifierID", 0), ("OrderIndex", 0), ("ItemAppearanceID", 200)),
                R(("ID", 12), ("ItemID", 3), ("ItemAppearanceModifierID", 0), ("OrderIndex", 0), ("ItemAppearanceID", 300)),
                R(("ID", 13), ("ItemID", 4), ("ItemAppearanceModifierID", 0), ("OrderIndex", 0), ("ItemAppearanceID", 400)),
                R(("ID", 14), ("ItemID", 5), ("ItemAppearanceModifierID", 0), ("OrderIndex", 0), ("ItemAppearanceID", 500)),
                R(("ID", 15), ("ItemID", 7), ("ItemAppearanceModifierID", 0), ("OrderIndex", 0), ("ItemAppearanceID", 100)))
            .Add(GameTableNames.ItemAppearance,
                R(("ID", 100), ("ItemDisplayInfoID", 1000)), R(("ID", 200), ("ItemDisplayInfoID", 2000)),
                R(("ID", 300), ("ItemDisplayInfoID", 3000)), R(("ID", 400), ("ItemDisplayInfoID", 4000)),
                R(("ID", 500), ("ItemDisplayInfoID", 0)))
            .Add(GameTableNames.ItemDisplayInfo, R(("ID", 1000)), R(("ID", 2000)), R(("ID", 3000)), R(("ID", 4000)));
        return new ItemCatalog(t);
    }

    [Fact]
    public void ListsOnlyItemsWithAVisualAndAName()
    {
        var all = Catalog().Search(new ItemQuery(Limit: 100));
        Assert.Equal(4, all.Total);
        Assert.Equal([4, 1, 3, 2], all.Items.Select(i => i.ItemId));
    }

    [Fact]
    public void DevAndNpcItemsAreOptInExceptByExactId()
    {
        string[] names = ["(DNT) Moonglaive", "Ahn'Qiraj Mace [PH]", "10% Test Speed Boots", "JEFF TEST SWORD", "Unused Feathered Gauntlets",
            "Monster - Axe, 2H Special NPC (Herod)", "Arcanite Reaper", "Testament of Hope", "Zealot's Robe"];
        var t = new InMemoryTables();
        for (var i = 0; i < names.Length; i++)
        {
            var id = i + 1;
            t.Add(GameTableNames.ItemSparse, R(("ID", id), ("Display_lang", names[i]), ("InventoryType", (byte)13), ("OverallQualityID", (byte)2)))
                .Add(GameTableNames.ItemModifiedAppearance, R(("ID", id), ("ItemID", id), ("ItemAppearanceModifierID", 0), ("OrderIndex", 0), ("ItemAppearanceID", id)))
                .Add(GameTableNames.ItemAppearance, R(("ID", id), ("ItemDisplayInfoID", id)))
                .Add(GameTableNames.ItemDisplayInfo, R(("ID", id)));
        }
        var catalog = new ItemCatalog(t);
        // By default only real items ("Testament" is not the word "Test").
        Assert.Equal(["Arcanite Reaper", "Testament of Hope", "Zealot's Robe"], catalog.Search(new ItemQuery(Limit: 100)).Items.Select(i => i.Name));
        Assert.Empty(catalog.Search(new ItemQuery("moonglaive")).Items);
        // Opted in: everything, dev and NPC items last.
        var all = catalog.Search(new ItemQuery(Limit: 100, IncludeInternal: true)).Items;
        Assert.Equal(9, all.Count);
        Assert.All(all.Take(3), i => Assert.False(i.Internal));
        Assert.All(all.Skip(3), i => Assert.True(i.Internal));
        // An exact item ID always finds the item, and Get always returns it.
        Assert.Equal("(DNT) Moonglaive", catalog.Search(new ItemQuery("1")).Items.Single().Name);
        Assert.True(catalog.Get(1)!.Internal);
    }

    [Fact]
    public void ItemsWithAModelButNoNameAreListedByTheirSetAndSlot()
    {
        // 20 and 21 have Item and appearance rows but no ItemSparse row; 20 is in a set, 21 is not.
        var t = new InMemoryTables()
            .Add(GameTableNames.ItemSparse, R(("ID", 1), ("Display_lang", "Robe of the Archmage"), ("InventoryType", (byte)20), ("OverallQualityID", (byte)4)))
            .Add(GameTableNames.Item, R(("ID", 1), ("InventoryType", (byte)20), ("IconFileDataID", 1001)),
                R(("ID", 20), ("InventoryType", (byte)5), ("IconFileDataID", 1020)), R(("ID", 21), ("InventoryType", (byte)7), ("IconFileDataID", 1021)),
                R(("ID", 22), ("InventoryType", (byte)11)))
            .Add(GameTableNames.ItemSet, R(("ID", 218), ("Name_lang", "Battlegear of Wrath"), ("ItemID", new[] { 20, 0 })));
        foreach (var id in new[] { 1, 20, 21, 22 })
            t.Add(GameTableNames.ItemModifiedAppearance, R(("ID", id), ("ItemID", id), ("ItemAppearanceModifierID", 0), ("OrderIndex", 0), ("ItemAppearanceID", id)))
                .Add(GameTableNames.ItemAppearance, R(("ID", id), ("ItemDisplayInfoID", id)))
                .Add(GameTableNames.ItemDisplayInfo, R(("ID", id)));
        var catalog = new ItemCatalog(t);

        var all = catalog.Search(new ItemQuery(Limit: 100)).Items;
        // Named items first; a ring (22) is not worn, so it is left out.
        Assert.Equal([1, 20, 21], all.Select(i => i.ItemId));
        var wrath = catalog.Get(20)!;
        Assert.Equal(("Battlegear of Wrath: chest", "chest", ItemCatalog.UnknownQuality, 1020, true), (wrath.Name, wrath.Slot, wrath.Quality, wrath.IconFileDataId, wrath.Unnamed));
        Assert.Equal("Unnamed legs (item 21)", catalog.Get(21)!.Name);
        Assert.Equal([20], catalog.Search(new ItemQuery("wrath")).Items.Select(i => i.ItemId));
        Assert.False(catalog.Get(1)!.Unnamed);
    }

    [Fact]
    public void CarriesSlotQualityAndIcon()
    {
        var thunderfury = Catalog().Search(new ItemQuery("THUNDERfury")).Items.Single();
        Assert.Equal(new ItemSummary(2, "Thunderfury, Blessed Blade of the Windseeker", "mainhand", 13, 5, 1002), thunderfury);
    }

    [Fact]
    public void SearchIsACaseInsensitiveSubstringMatch()
    {
        var robes = Catalog().Search(new ItemQuery("robe"));
        Assert.Equal(3, robes.Total);
        Assert.Equal(["Frostweave Robe", "Robe of the Archmage", "Robe of Winter Night"], robes.Items.Select(i => i.Name));
    }

    [Fact]
    public void FiltersBySlotAndQuality()
    {
        var c = Catalog();
        // Robes (InventoryType 20) and chest pieces (5) share the "chest" slot.
        Assert.Equal([4, 1, 3], c.Search(new ItemQuery("robe", Slots: ["chest"])).Items.Select(i => i.ItemId));
        Assert.Equal([2], c.Search(new ItemQuery(Slots: ["mainhand"])).Items.Select(i => i.ItemId));
        Assert.Equal([2], c.Search(new ItemQuery(Slots: ["mainhand", "head"])).Items.Select(i => i.ItemId));
        Assert.Equal([1], c.Search(new ItemQuery(Slots: ["chest"], Qualities: [4])).Items.Select(i => i.ItemId));
    }

    [Fact]
    public void AnItemIdAlsoMatches()
    {
        Assert.Equal([2], Catalog().Search(new ItemQuery("2")).Items.Select(i => i.ItemId));
    }

    [Fact]
    public void LeavesOutItemsThatAreNotWorn()
    {
        var t = new InMemoryTables()
            .Add(GameTableNames.ItemSparse, R(("ID", 1), ("Display_lang", "Band of Robes"), ("InventoryType", (byte)11)))
            .Add(GameTableNames.ItemModifiedAppearance, R(("ID", 1), ("ItemID", 1), ("ItemAppearanceID", 1)))
            .Add(GameTableNames.ItemAppearance, R(("ID", 1), ("ItemDisplayInfoID", 1)))
            .Add(GameTableNames.ItemDisplayInfo, R(("ID", 1)));
        Assert.Equal(0, new ItemCatalog(t).Count);
    }

    [Fact]
    public void PagesWithOffsetAndLimit()
    {
        var c = Catalog();
        var page1 = c.Search(new ItemQuery("robe", Limit: 2));
        var page2 = c.Search(new ItemQuery("robe", Limit: 2, Offset: 2));
        var past = c.Search(new ItemQuery("robe", Limit: 2, Offset: 10));

        Assert.Equal([4, 1], page1.Items.Select(i => i.ItemId));
        Assert.Equal([3], page2.Items.Select(i => i.ItemId));
        Assert.Empty(past.Items);
        Assert.All([page1, page2, past], p => Assert.Equal(3, p.Total));
    }

    [Fact]
    public void ClampsBadPagingValues()
    {
        var c = Catalog();
        Assert.Single(c.Search(new ItemQuery(Limit: 0)).Items);
        Assert.Equal(4, c.Search(new ItemQuery(Limit: 100000, Offset: -5)).Items.Count);
    }
}

public class CharacterCatalogTests
{
    static InMemoryTables Tables() => new InMemoryTables()
        .Add(GameTableNames.ChrRaces,
            R(("ID", 1), ("Name_lang", "Human"), ("Name_female_lang", "Human"), ("ClientFileString", "Human")),
            R(("ID", 2), ("Name_lang", "Orc"), ("Name_female_lang", "Orc"), ("ClientFileString", "Orc")),
            R(("ID", 10), ("Name_lang", "Blood Elf"), ("Name_female_lang", "Blood Elf"), ("ClientFileString", "BloodElf")),
            R(("ID", 95), ("Name_lang", "High Order Skyborne"), ("Name_female_lang", "High Order Skyborne"), ("ClientFileString", "Skyborne")))
        .Add(GameTableNames.ChrClasses, R(("ID", 1), ("Name_lang", "Warrior")), R(("ID", 3), ("Name_lang", "Hunter")), R(("ID", 8), ("Name_lang", "Mage")))
        // Blood Elf has models but no CharBaseInfo row, so it is not playable.
        .Add(GameTableNames.CharBaseInfo,
            R(("ID", 1), ("RaceID", 2), ("ClassID", 3)), R(("ID", 2), ("RaceID", 2), ("ClassID", 1)),
            R(("ID", 3), ("RaceID", 1), ("ClassID", 8)), R(("ID", 4), ("RaceID", 95), ("ClassID", 8)))
        .Add(GameTableNames.ChrRaceXChrModel,
            R(("ID", 1), ("ChrRacesID", 1), ("Sex", 0), ("ChrModelID", 1)), R(("ID", 2), ("ChrRacesID", 1), ("Sex", 1), ("ChrModelID", 2)),
            R(("ID", 3), ("ChrRacesID", 2), ("Sex", 0), ("ChrModelID", 3)), R(("ID", 4), ("ChrRacesID", 2), ("Sex", 1), ("ChrModelID", 4)),
            R(("ID", 5), ("ChrRacesID", 10), ("Sex", 0), ("ChrModelID", 19)),
            R(("ID", 6), ("ChrRacesID", 95), ("Sex", 0), ("ChrModelID", 200)), R(("ID", 7), ("ChrRacesID", 95), ("Sex", 1), ("ChrModelID", 201)))
        // Skyborne has no SD variant; Orc female has none either.
        .Add(GameTableNames.ChrModelAltVariant,
            R(("ID", 1), ("SourceChrModelID", 1), ("VariantChrModelID", 257)), R(("ID", 2), ("SourceChrModelID", 2), ("VariantChrModelID", 258)),
            R(("ID", 3), ("SourceChrModelID", 3), ("VariantChrModelID", 259)));

    [Fact]
    public void ABuildWithoutTheSdVariantTableOffersOnlyHd()
    {
        // ChrModelAltVariant exists only in Forever builds; other products must still load.
        string[] present = [GameTableNames.ChrRaces, GameTableNames.ChrClasses, GameTableNames.CharBaseInfo, GameTableNames.ChrRaceXChrModel];
        var races = new CharacterCatalog(new StrictTables(Tables(), present)).Races();
        Assert.Equal([1, 2, 95], races.Select(r => r.Race));
        Assert.All(races.SelectMany(r => r.Sexes), s => Assert.False(s.Sd));
    }

    [Fact]
    public void ARaceIsPlayableIffItHasACharBaseInfoRow()
    {
        var races = new CharacterCatalog(Tables()).Races();
        Assert.Equal([1, 2, 95], races.Select(r => r.Race));
        Assert.Equal(["Human", "Orc", "High Order Skyborne"], races.Select(r => r.Name));
    }

    [Fact]
    public void ListsClassesPerRaceFromCharBaseInfo()
    {
        var orc = new CharacterCatalog(Tables()).Races().Single(r => r.Race == 2);
        Assert.Equal([(1, "Warrior"), (3, "Hunter")], orc.Classes.Select(c => (c.ClassId, c.Name)));
    }

    [Fact]
    public void ReportsHdAndSdPerSex()
    {
        var races = new CharacterCatalog(Tables()).Races();
        var orc = races.Single(r => r.Race == 2);
        Assert.Equal([new SexAvailability(0, true, true, 3, 259), new SexAvailability(1, true, false, 4, null)], orc.Sexes);

        var skyborne = races.Single(r => r.Race == 95);
        Assert.All(skyborne.Sexes, s => Assert.False(s.Sd));
    }

    [Fact]
    public void ResolvesTheChrModelForHdAndSd()
    {
        var c = new CharacterCatalog(Tables());
        Assert.Equal(3, c.ChrModelFor(2, 0, ModelSet.Hd));
        Assert.Equal(259, c.ChrModelFor(2, 0, ModelSet.Sd));
        Assert.Null(c.ChrModelFor(2, 1, ModelSet.Sd));
        Assert.Null(c.ChrModelFor(10, 0, ModelSet.Hd));
        Assert.Null(c.ChrModelFor(3, 0, ModelSet.Hd));
    }
}
