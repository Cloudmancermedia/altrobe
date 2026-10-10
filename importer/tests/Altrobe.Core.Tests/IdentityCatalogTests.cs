using Altrobe.Core.Catalog;
using Altrobe.Core.Tables;
using static Altrobe.Core.Tests.Fake;

namespace Altrobe.Core.Tests;

public class IdentityCatalogTests
{
    static InMemoryTables Tables() => new InMemoryTables()
        .Add(GameTableNames.CharTitles,
            R(("ID", 1225), ("Name_lang", "%s the Blacksmith"), ("Name1_lang", "%s the Blacksmith")),
            R(("ID", 1017), ("Name_lang", "%s of the Magram"), ("Name1_lang", "")),
            R(("ID", 9999), ("Name_lang", "[DNT] Test %s"), ("Name1_lang", "DNT Test %s")))
        .Add(GameTableNames.NameGen,
            // NameType 0 is first names by sex; 1 is last names, listed for both sexes.
            R(("ID", 3), ("Name", "Nielas"), ("RaceID", 1), ("Sex", 0), ("NameType", 0)),
            R(("ID", 1), ("Name", "Arugal"), ("RaceID", 1), ("Sex", 0), ("NameType", 0)),
            R(("ID", 2), ("Name", "Nielas"), ("RaceID", 1), ("Sex", 0), ("NameType", 0)),
            R(("ID", 4), ("Name", "Calia"), ("RaceID", 1), ("Sex", 1), ("NameType", 0)),
            R(("ID", 5), ("Name", "Dawnstone"), ("RaceID", 1), ("Sex", 0), ("NameType", 1)),
            R(("ID", 6), ("Name", "Dawnstone"), ("RaceID", 1), ("Sex", 1), ("NameType", 1)),
            R(("ID", 7), ("Name", "Belgarden"), ("RaceID", 1), ("Sex", 1), ("NameType", 1)),
            R(("ID", 8), ("Name", "Cairne"), ("RaceID", 6), ("Sex", 0), ("NameType", 0)));

    [Fact]
    public void TitlesKeepBothWordingsInIdOrderAndLeaveOutTestEntries()
    {
        Assert.Equal(
            [new TitleInfo(1017, "%s of the Magram", "%s of the Magram"), new TitleInfo(1225, "%s the Blacksmith", "%s the Blacksmith")],
            new IdentityCatalog(Tables()).Titles);
    }

    [Fact]
    public void PvpRankTitlesBelongToTheirFaction()
    {
        var t = new InMemoryTables().Add(GameTableNames.CharTitles,
            R(("ID", 1270), ("Name_lang", "Grand Marshal %s"), ("Name1_lang", "Grand Marshal %s"), ("Flags", 4)),
            R(("ID", 1309), ("Name_lang", "Knight-Captain %s"), ("Name1_lang", "Knight-Captain %s"), ("Flags", 4)),
            R(("ID", 1284), ("Name_lang", "High Warlord %s"), ("Name1_lang", "High Warlord %s"), ("Flags", 4)),
            R(("ID", 1313), ("Name_lang", "Master Angler %s"), ("Name1_lang", "Master Angler %s"), ("Flags", 0)));
        var faction = new IdentityCatalog(t).Titles.ToDictionary(x => x.TitleId, x => x.Faction);
        Assert.Equal(("alliance", "alliance", "horde", (string?)null), (faction[1270], faction[1309], faction[1284], faction[1313]));
    }

    [Fact]
    public void NamesAreFirstNamesBySexAndSharedLastNamesPerRaceInIdOrderWithoutRepeats()
    {
        var names = new IdentityCatalog(Tables()).Names;
        Assert.Equal(["Arugal", "Nielas"], names[1].Male);
        Assert.Equal(["Calia"], names[1].Female);
        Assert.Equal(["Dawnstone", "Belgarden"], names[1].Last);
        Assert.Equal(["Cairne"], names[6].Male);
        Assert.Empty(names[6].Last);
    }

    [Fact]
    public void ABuildWithoutTheTablesHasNoTitlesOrNames()
    {
        var c = new IdentityCatalog(new InMemoryTables());
        Assert.Empty(c.Titles);
        Assert.Empty(c.Names);
    }
}
