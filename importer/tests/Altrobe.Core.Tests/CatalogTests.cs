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
    public void CarriesSlotQualityAndIcon()
    {
        var thunderfury = Catalog().Search(new ItemQuery("THUNDERfury")).Items.Single();
        Assert.Equal(new ItemSummary(2, "Thunderfury, Blessed Blade of the Windseeker", 13, 5, 1002), thunderfury);
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
        Assert.Equal([1, 3], c.Search(new ItemQuery("robe", Slots: [20])).Items.Select(i => i.ItemId));
        Assert.Equal([4, 1, 3], c.Search(new ItemQuery("robe", Slots: [5, 20])).Items.Select(i => i.ItemId));
        Assert.Equal([1], c.Search(new ItemQuery(Slots: [20], Qualities: [4])).Items.Select(i => i.ItemId));
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
    public void ARaceIsPlayableIffItHasACharBaseInfoRow()
    {
        var races = new CharacterCatalog(Tables()).Races();
        Assert.Equal([1, 2, 95], races.Select(r => r.RaceId));
        Assert.Equal(["Human", "Orc", "High Order Skyborne"], races.Select(r => r.Name));
    }

    [Fact]
    public void ListsClassesPerRaceFromCharBaseInfo()
    {
        var orc = new CharacterCatalog(Tables()).Races().Single(r => r.RaceId == 2);
        Assert.Equal([(1, "Warrior"), (3, "Hunter")], orc.Classes.Select(c => (c.ClassId, c.Name)));
    }

    [Fact]
    public void ReportsHdAndSdPerSex()
    {
        var races = new CharacterCatalog(Tables()).Races();
        var orc = races.Single(r => r.RaceId == 2);
        Assert.Equal([new SexAvailability(0, true, true, 3, 259), new SexAvailability(1, true, false, 4, null)], orc.Sexes);

        var skyborne = races.Single(r => r.RaceId == 95);
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
