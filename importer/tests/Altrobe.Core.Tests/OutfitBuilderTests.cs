using Altrobe.Core.Catalog;
using Altrobe.Core.Tables;
using static Altrobe.Core.Tests.Fake;

namespace Altrobe.Core.Tests;

public class OutfitBuilderTests
{
    // (id, name, inventory type, item class, armor subclass, quality, required level, item level, class mask)
    static readonly (int, string, int, int, int, int, int, int, int)[] Rows =
    [
        (1, "Mail Vest 22", 5, 4, 3, 2, 22, 27, -1),
        (2, "Mail Vest 29", 5, 4, 3, 2, 29, 34, -1),
        (3, "Mail Vest 29 Better", 5, 4, 3, 2, 29, 36, -1),
        (4, "Mail Vest 31", 5, 4, 3, 2, 31, 36, -1),   // above the target level
        (5, "Plate Vest 30", 5, 4, 4, 2, 30, 35, -1),  // wrong armor type
        (6, "Epic Mail Vest 30", 5, 4, 3, 4, 30, 40, -1), // above the highest quality asked for
        (7, "Mail Legs 25", 7, 4, 3, 3, 25, 30, -1),
        (8, "Cloth Cloak 28", 16, 4, 1, 2, 28, 33, -1), // cloaks are cloth for every class
        (9, "Paladin Mail Boots 28", 8, 4, 3, 2, 28, 33, 2), // paladin only
        (10, "TEST Mail Helm", 1, 4, 3, 2, 30, 35, -1),  // dev item
    ];

    static ItemCatalog Catalog()
    {
        var t = new InMemoryTables();
        foreach (var (id, name, inv, cls, sub, q, req, ilvl, mask) in Rows)
            t.Add(GameTableNames.ItemSparse, R(("ID", id), ("Display_lang", name), ("InventoryType", (byte)inv), ("OverallQualityID", (byte)q),
                    ("RequiredLevel", req), ("ItemLevel", ilvl), ("AllowableClass", mask)))
                .Add(GameTableNames.Item, R(("ID", id), ("InventoryType", (byte)inv), ("ClassID", cls), ("SubclassID", sub)))
                .Add(GameTableNames.ItemModifiedAppearance, R(("ID", id), ("ItemID", id), ("ItemAppearanceModifierID", 0), ("OrderIndex", 0), ("ItemAppearanceID", id)))
                .Add(GameTableNames.ItemAppearance, R(("ID", id), ("ItemDisplayInfoID", id)))
                .Add(GameTableNames.ItemDisplayInfo, R(("ID", id)));
        return new ItemCatalog(t);
    }

    [Fact]
    public void PicksTheHighestLevelMatchInEachSlotWithinTheWindow()
    {
        var o = OutfitBuilder.Build(Catalog(), new OutfitRequest(Level: 30, Armor: "mail", ClassId: 1, MinQuality: 2, MaxQuality: 3));
        // Chest: 29 beats 22, and the better item level breaks the tie; 31, plate and epic are out.
        Assert.Equal(3, o.Items["chest"].ItemId);
        Assert.Equal(7, o.Items["legs"].ItemId);
        Assert.Equal(8, o.Items["back"].ItemId); // the armor type does not apply to cloaks
        Assert.False(o.Items.ContainsKey("feet")); // paladin only
        Assert.False(o.Items.ContainsKey("head")); // only a dev item
        Assert.Contains("feet", o.Missing);
        Assert.Contains("head", o.Missing);
    }

    [Fact]
    public void TheWindowIsSevenLevelsAndTheSameRequestGivesTheSameOutfit()
    {
        var c = Catalog();
        var r = new OutfitRequest(Level: 28, Armor: "mail", ClassId: 1, MinQuality: 2, MaxQuality: 3);
        Assert.Equal(1, OutfitBuilder.Build(c, r).Items["chest"].ItemId); // 29 is too high; 22 is inside 21-28
        Assert.Equal(OutfitBuilder.Build(c, r).Items.Select(kv => (kv.Key, kv.Value.ItemId)), OutfitBuilder.Build(c, r).Items.Select(kv => (kv.Key, kv.Value.ItemId)));
        Assert.False(OutfitBuilder.Build(c, r with { Level = 20 }).Items.ContainsKey("chest"));
    }
}
