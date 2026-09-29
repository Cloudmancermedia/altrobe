using Altrobe.Core.Catalog;
using Altrobe.Core.Tables;
using static Altrobe.Core.Tests.Fake;

namespace Altrobe.Core.Tests;

public class SetCatalogTests
{
    // Items with a visual: 1 helm, 2 chest, 3 one-handed sword, 4 one-handed mace, 5 second helm.
    // 9 is a ring (not worn), 8 has no visual.
    static (ItemCatalog items, SetCatalog sets) Catalogs()
    {
        var t = new InMemoryTables();
        foreach (var (id, name, inv) in new[] { (1, "Lionheart Helm", 1), (2, "Breastplate of Wrath", 5), (3, "Sword of Might", 13), (4, "Mace of Might", 13), (5, "Spare Helm", 1), (9, "Band of Might", 11), (8, "Invisible Belt", 6) })
        {
            t.Add(GameTableNames.ItemSparse, R(("ID", id), ("Display_lang", name), ("InventoryType", (byte)inv), ("OverallQualityID", (byte)4)));
            t.Add(GameTableNames.Item, R(("ID", id), ("InventoryType", (byte)inv)));
            if (id == 8) continue;
            t.Add(GameTableNames.ItemModifiedAppearance, R(("ID", id), ("ItemID", id), ("ItemAppearanceModifierID", 0), ("OrderIndex", 0), ("ItemAppearanceID", id)))
                .Add(GameTableNames.ItemAppearance, R(("ID", id), ("ItemDisplayInfoID", id)))
                .Add(GameTableNames.ItemDisplayInfo, R(("ID", id)));
        }
        // 30 and 31 have models but no ItemSparse row, like tier 2 in the Forever beta.
        foreach (var (id, inv) in new[] { (30, 5), (31, 7) })
            t.Add(GameTableNames.Item, R(("ID", id), ("InventoryType", (byte)inv)))
                .Add(GameTableNames.ItemModifiedAppearance, R(("ID", id), ("ItemID", id), ("ItemAppearanceModifierID", 0), ("OrderIndex", 0), ("ItemAppearanceID", id)))
                .Add(GameTableNames.ItemAppearance, R(("ID", id), ("ItemDisplayInfoID", id)))
                .Add(GameTableNames.ItemDisplayInfo, R(("ID", id)));
        t.Add(GameTableNames.ItemSet, R(("ID", 218), ("Name_lang", "Battlegear of Wrath"), ("ItemID", new[] { 30, 31 })));
        t.Add(GameTableNames.ItemSet,
            R(("ID", 100), ("Name_lang", "Battlegear of Might"), ("ItemID", new[] { 1, 2, 3, 4, 5, 9, 8, 0 })),
            R(("ID", 101), ("Name_lang", "Rings Only"), ("ItemID", new[] { 9, 0 })),
            R(("ID", 102), ("Name_lang", "Test Set [PH]"), ("ItemID", new[] { 2 })));
        var items = new ItemCatalog(t);
        return (items, new SetCatalog(t, items));
    }

    [Fact]
    public void ASetHasItsPiecesWithVisualsInSlots()
    {
        var (_, sets) = Catalogs();
        var might = sets.Get(100)!;
        Assert.Equal("Battlegear of Might", might.Name);
        // The second one-hander goes in the off hand; the second helm has no free slot.
        Assert.Equal([("head", 1), ("chest", 2), ("mainhand", 3), ("offhand", 4)], might.Pieces.Select(p => (p.Slot, p.Item.ItemId)));
        Assert.Equal([5, 9, 8], might.Skipped.Select(s => s.ItemId));
        Assert.Contains("head", might.Skipped[0].Reason);
    }

    [Fact]
    public void ASetOfUnnamedPiecesIsShownUnderItsOwnName()
    {
        var (_, sets) = Catalogs();
        var wrath = sets.Get(218)!;
        Assert.True(wrath.Unnamed);
        Assert.Equal([("chest", "Battlegear of Wrath: chest"), ("legs", "Battlegear of Wrath: legs")], wrath.Pieces.Select(p => (p.Slot, p.Item.Name)));
        Assert.Contains(sets.Search(new SetQuery("battlegear of wrath")).Sets, s => s.SetId == 218);
    }

    [Fact]
    public void SetsWithNothingToShowAreLeftOutAndDevSetsSortLast()
    {
        var (_, sets) = Catalogs();
        Assert.Null(sets.Get(101));
        Assert.Equal([100, 218], sets.Search(new SetQuery()).Sets.Select(s => s.SetId));
        var all = sets.Search(new SetQuery(IncludeInternal: true)).Sets;
        Assert.Equal([100, 218, 102], all.Select(s => s.SetId));
        Assert.True(all[2].Internal);
        Assert.Equal([102], sets.Search(new SetQuery("102")).Sets.Select(s => s.SetId)); // exact ID always finds it
    }

    [Fact]
    public void SearchMatchesSetNamesAndPieceNames()
    {
        var (_, sets) = Catalogs();
        Assert.Equal([100, 218], sets.Search(new SetQuery("battlegear")).Sets.Select(s => s.SetId));
        Assert.Equal([100, 218], sets.Search(new SetQuery("wrath")).Sets.Select(s => s.SetId)); // by a piece's name, or the set's
        Assert.Equal([100, 218, 102], sets.Search(new SetQuery("wrath", IncludeInternal: true)).Sets.Select(s => s.SetId));
        Assert.Equal([100], sets.Search(new SetQuery("100")).Sets.Select(s => s.SetId)); // by set ID
        Assert.Equal(3, sets.Search(new SetQuery(Limit: 1, IncludeInternal: true)).Total);
    }
}
