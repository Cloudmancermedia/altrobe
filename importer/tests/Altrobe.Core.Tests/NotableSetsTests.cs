using Altrobe.Core.Catalog;
using Altrobe.Core.Tables;
using static Altrobe.Core.Tests.Fake;

namespace Altrobe.Core.Tests;

public class NotableSetsTests
{
    const int Warrior = 1, Paladin = 2, Mage = 8;

    // PvP pieces carry their class in ItemSparse.AllowableClass (bit classId - 1). Tier pieces 30 and
    // 31 have no ItemSparse row, like tier 1 and 2 in the Forever beta.
    static SetCatalog Sets()
    {
        var t = new InMemoryTables();
        foreach (var (id, name, inv, classMask) in new[]
        {
            (1, "Helm of Might", 1, 1), (40, "Warlord's Plate Headpiece", 1, 1), (41, "Warlord's Plate Helm", 1, 1),
            (42, "Field Marshal's Plate Helm", 1, 1), (43, "Champion's Silk Hood", 1, 128), (44, "Knight-Lieutenant's Plate Helm", 1, 3),
            (45, "Lorekeeper's Hood", 1, -1),
        })
        {
            t.Add(GameTableNames.ItemSparse, R(("ID", id), ("Display_lang", name), ("InventoryType", (byte)inv), ("OverallQualityID", (byte)4), ("AllowableClass", classMask)));
            Visual(t, id, inv);
        }
        foreach (var (id, inv) in new[] { (30, 5), (31, 1) }) Visual(t, id, inv);
        t.Add(GameTableNames.ItemSet,
            R(("ID", 100), ("Name_lang", "Battlegear of Might"), ("ItemID", new[] { 1 })),
            R(("ID", 1600), ("Name_lang", "Battlegear of Might"), ("ItemID", new[] { 31 })), // an unnamed copy
            R(("ID", 218), ("Name_lang", "Battlegear of Wrath"), ("ItemID", new[] { 30 })),
            R(("ID", 383), ("Name_lang", "Warlord's Battlegear"), ("ItemID", new[] { 40 })),
            R(("ID", 1721), ("Name_lang", "Warlord's Battlegear"), ("ItemID", new[] { 41 })), // same name and class
            R(("ID", 384), ("Name_lang", "Field Marshal's Battlegear"), ("ItemID", new[] { 42 })),
            R(("ID", 341), ("Name_lang", "Champion's Regalia"), ("ItemID", new[] { 43 })),
            R(("ID", 1619), ("Name_lang", "Knight-Lieutenant's Plate"), ("ItemID", new[] { 44 })),
            R(("ID", 500), ("Name_lang", "Lorekeeper's Garb"), ("ItemID", new[] { 45 })));
        return new SetCatalog(t, new ItemCatalog(t));
    }

    static void Visual(InMemoryTables t, int id, int inv) =>
        t.Add(GameTableNames.Item, R(("ID", id), ("InventoryType", (byte)inv)))
            .Add(GameTableNames.ItemModifiedAppearance, R(("ID", id), ("ItemID", id), ("ItemAppearanceModifierID", 0), ("OrderIndex", 0), ("ItemAppearanceID", id)))
            .Add(GameTableNames.ItemAppearance, R(("ID", id), ("ItemDisplayInfoID", id)))
            .Add(GameTableNames.ItemDisplayInfo, R(("ID", id)));

    static List<(string, string)> Groups(SetCatalog sets, string faction, params int[] classes) =>
        sets.Notable(faction, classes).Select(g => (g.Group, string.Join(",", g.Sets.Select(s => s.SetId)))).ToList();

    [Fact]
    public void AHordeWarriorGetsHordePvpSetsThenTiersInOrder()
    {
        // Field Marshal's is Alliance; the second Warlord's Battlegear repeats the first; the named
        // Battlegear of Might wins over its unnamed copy; Lorekeeper's Garb is not notable.
        Assert.Equal([("PvP (epic)", "383"), ("Tier 1", "100"), ("Tier 2", "218")], Groups(Sets(), "horde", Warrior));
    }

    [Fact]
    public void ClassesNarrowPvpSetsByThePiecesClassMask()
    {
        Assert.Equal([("PvP (epic)", "384"), ("Tier 1", "100"), ("Tier 2", "218")], Groups(Sets(), "alliance", Warrior, Paladin));
        Assert.Equal([("PvP (rare)", "341")], Groups(Sets(), "horde", Mage));
    }

    [Fact]
    public void APvpSetSharedBySeveralClassesIsNotNotable()
    {
        // Like Knight-Lieutenant's Satin (Priest and Mage): wearable, but not a class's own PvP set.
        var sets = Sets();
        Assert.Equal(3, sets.Get(1619)!.ClassMask);
        Assert.Empty(Groups(sets, "alliance", Paladin));
        Assert.Null(NotableSets.Classify(sets.Get(1619)!));
    }

    [Fact]
    public void UnnamedTierSetsTakeTheirTiersQuality()
    {
        // Tier 1 and 2 pieces have no ItemSparse row, so no quality of their own; tiers 1-3 are epic.
        var sets = Sets();
        Assert.Equal(4, sets.Get(218)!.Quality);
        Assert.Equal(4, sets.Get(1600)!.Quality);
        Assert.Equal(4, sets.Get(100)!.Quality); // named: its pieces' quality
    }

    [Fact]
    public void ASetsClassMaskIsWhatEveryRestrictedPieceAllows()
    {
        var sets = Sets();
        Assert.Equal(3, sets.Get(1619)!.ClassMask);
        Assert.Equal(-1, sets.Get(500)!.ClassMask);
    }

    [Fact]
    public void TierNamesListSixSetsPerClassInTierOrder()
    {
        var priest = NotableSets.Tiers.Single(t => t.Class == "Priest");
        Assert.Equal(5, priest.ClassId);
        Assert.Equal(["Vestments of the Devout", "Vestments of the Virtuous", "Vestments of Prophecy", "Vestments of Transcendence", "Garments of the Oracle", "Vestments of Faith"], priest.Sets);
        Assert.All(NotableSets.Tiers, t => Assert.Equal(NotableSets.TierLabels.Count, t.Sets.Count));
    }
}
