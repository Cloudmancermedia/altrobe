using Altrobe.Core.Catalog;
using Altrobe.Core.Tables;
using static Altrobe.Core.Tests.Fake;

namespace Altrobe.Core.Tests;

public class ClassicItemNamesTests
{
    // The shape of cmangos classic-db's item_template: a column list, then multi-row INSERTs.
    const string Sql = """
        CREATE TABLE `item_template` (
          `entry` mediumint(8) unsigned NOT NULL default '0',
          `class` tinyint(3) unsigned NOT NULL default '0',
          `name` varchar(255) NOT NULL default '',
          `Quality` tinyint(3) unsigned NOT NULL default '0',
          `AllowableClass` mediumint(9) NOT NULL default '-1',
          `ItemLevel` tinyint(3) unsigned NOT NULL default '0',
          `RequiredLevel` tinyint(3) unsigned NOT NULL default '0',
          PRIMARY KEY  (`entry`)
        ) ENGINE=MyISAM;
        INSERT INTO `item_template` VALUES (18816,2,'Perdition\'s Blade',4,-1,77,60),(19019,2,'Thunderfury, Blessed Blade of the Windseeker',5,-1,80,60);
        INSERT INTO `item_template` VALUES (16905,4,'Bloodfang Chestpiece',4,8,76,60);
        INSERT INTO `other_table` VALUES (1,'not an item');
        """;

    [Fact]
    public void ReadsNameQualityLevelsAndClassFromTheItemTable()
    {
        var items = ClassicItemNames.Parse(Sql);
        Assert.Equal(3, items.Count);
        Assert.Equal(new ClassicItem("Perdition's Blade", 4, 77, 60, -1), items[18816]);
        Assert.Equal("Thunderfury, Blessed Blade of the Windseeker", items[19019].Name);
        Assert.Equal(8, items[16905].AllowableClass);
    }

    [Fact]
    public void TheCatalogNamesOnlyItemsTheGameFilesLeaveUnnamed()
    {
        var t = new InMemoryTables();
        void Item(int id, int inventoryType, string? sparseName)
        {
            if (sparseName != null) t.Add(GameTableNames.ItemSparse, R(("ID", id), ("Display_lang", sparseName), ("InventoryType", inventoryType), ("OverallQualityID", 3)));
            t.Add(GameTableNames.Item, R(("ID", id), ("InventoryType", inventoryType), ("IconFileDataID", 0)));
            t.Add(GameTableNames.ItemModifiedAppearance, R(("ID", id), ("ItemID", id), ("ItemAppearanceModifierID", 0), ("OrderIndex", 0), ("ItemAppearanceID", id)));
            t.Add(GameTableNames.ItemAppearance, R(("ID", id), ("ItemDisplayInfoID", id)));
            t.Add(GameTableNames.ItemDisplayInfo, R(("ID", id)));
        }
        Item(18816, 13, null);           // no ItemSparse row: cmangos names it
        Item(19019, 13, "Thunderfury");  // the game files name it: they win
        Item(230000, 5, null);           // in neither: stays unnamed
        var classic = new Dictionary<int, ClassicItem>
        {
            [18816] = new("Perdition's Blade", 4, 77, 60, -1),
            [19019] = new("Something else", 5, 80, 60, -1),
        };
        var catalog = new ItemCatalog(t, classic);

        var blade = catalog.Get(18816)!;
        Assert.Equal(("Perdition's Blade", 4, false, 77, 60), (blade.Name, blade.Quality, blade.Unnamed, blade.ItemLevel, blade.RequiredLevel));
        Assert.Equal("cmangos", blade.NameSource);
        Assert.Equal(("Thunderfury", 3, (string?)null), (catalog.Get(19019)!.Name, catalog.Get(19019)!.Quality, catalog.Get(19019)!.NameSource));
        Assert.True(catalog.Get(230000)!.Unnamed);
    }
}
