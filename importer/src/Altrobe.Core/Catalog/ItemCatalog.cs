using Altrobe.Core.Tables;
using T = Altrobe.Core.Tables.GameTableNames;

namespace Altrobe.Core.Catalog;

public sealed record ItemSummary(int ItemId, string Name, int Slot, int Quality, int IconFileDataId);

public sealed record ItemQuery(string? Text = null, IReadOnlyCollection<int>? Slots = null, IReadOnlyCollection<int>? Qualities = null, int Limit = 50, int Offset = 0);

public sealed record ItemPage(int Total, IReadOnlyList<ItemSummary> Items);

// Every item with a name and a visual: ItemModifiedAppearance -> ItemAppearance -> ItemDisplayInfo.
// Slot is the item's InventoryType, as the game stores it (20 robe and 5 chest are both chest items).
public sealed class ItemCatalog
{
    public const int MaxLimit = 200;
    readonly IReadOnlyList<ItemSummary> _items;

    public ItemCatalog(ITables tables)
    {
        var sparse = tables.Get(T.ItemSparse).ById();
        var item = tables.Get(T.Item).ById();
        var appearance = tables.Get(T.ItemAppearance).ById();
        var display = tables.Get(T.ItemDisplayInfo).ById();

        _items = tables.Get(T.ItemModifiedAppearance).GroupByColumn("ItemID")
            .Select(g => (itemId: g.Key, ima: PrimaryAppearance(g.Value)))
            .Where(x => sparse.ContainsKey(x.itemId)
                && appearance.TryGetValue(x.ima.Int("ItemAppearanceID"), out var a)
                && display.ContainsKey(a.Int("ItemDisplayInfoID")))
            .Select(x =>
            {
                var s = sparse[x.itemId];
                return new ItemSummary(x.itemId, s.Str("Display_lang"), s.Int("InventoryType"), s.Int("OverallQualityID"),
                    item.TryGetValue(x.itemId, out var i) ? i.Int("IconFileDataID") : 0);
            })
            .Where(i => i.Name.Length > 0)
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.ItemId)
            .ToList();
    }

    public int Count => _items.Count;

    // The appearance an item shows by default: lowest modifier, then lowest OrderIndex (resolve.ts).
    public static Row PrimaryAppearance(IEnumerable<Row> modifiedAppearances) => modifiedAppearances
        .OrderBy(r => r.Int("ItemAppearanceModifierID"))
        .ThenBy(r => r.Int("OrderIndex"))
        .First();

    public ItemPage Search(ItemQuery q)
    {
        var limit = Math.Clamp(q.Limit, 1, MaxLimit);
        var offset = Math.Max(0, q.Offset);
        var text = q.Text?.Trim();
        var matches = _items.Where(i =>
            (string.IsNullOrEmpty(text) || i.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
            && (q.Slots is not { Count: > 0 } || q.Slots.Contains(i.Slot))
            && (q.Qualities is not { Count: > 0 } || q.Qualities.Contains(i.Quality))).ToList();
        return new ItemPage(matches.Count, matches.Skip(offset).Take(limit).ToList());
    }
}
