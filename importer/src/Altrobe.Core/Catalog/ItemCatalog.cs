using Altrobe.Core.Tables;
using T = Altrobe.Core.Tables.GameTableNames;

namespace Altrobe.Core.Catalog;

// Internal: a developer or NPC item by its name (see ItemCatalog.IsInternal). Kept, but listed last.
public sealed record ItemSummary(int ItemId, string Name, string Slot, int InventoryType, int Quality, int IconFileDataId, bool Internal = false);

public sealed record ItemQuery(string? Text = null, IReadOnlyCollection<string>? Slots = null, IReadOnlyCollection<int>? Qualities = null, int Limit = 50, int Offset = 0);

public sealed record ItemPage(int Total, IReadOnlyList<ItemSummary> Items);

// Every wearable item with a name and a visual: ItemModifiedAppearance -> ItemAppearance ->
// ItemDisplayInfo. Slot is the web app's look slot name for the item's InventoryType.
public sealed class ItemCatalog
{
    public const int MaxLimit = 200;

    // Same mapping as the web app's dress.ts slotNameForInventoryType.
    public static readonly IReadOnlyDictionary<int, string> SlotNames = new Dictionary<int, string>
    {
        [1] = "head", [2] = "neck", [3] = "shoulder", [4] = "shirt", [5] = "chest", [6] = "waist", [7] = "legs", [8] = "feet",
        [9] = "wrist", [10] = "hands", [13] = "mainhand", [14] = "offhand", [15] = "mainhand", [16] = "back", [17] = "mainhand",
        [19] = "tabard", [20] = "chest", [21] = "mainhand", [22] = "offhand", [23] = "offhand", [26] = "mainhand",
    };
    // The client marks none of these in its data (ItemSparse.Flags is 0 on all of them in the Forever
    // beta), so their names are the only signal: "(DNT)", "[PH]", "TEST", "Unused", and NPC weapons
    // ("Monster - ...").
    static readonly System.Text.RegularExpressions.Regex InternalName = new(
        @"\((DNT|PH)\)|\[(DNT|PH)\]|\bTEST\b|\bTest\b|\bUNUSED\b|\bUnused\b|^Monster - ",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static bool IsInternal(string name) => InternalName.IsMatch(name);

    readonly IReadOnlyList<ItemSummary> _items;
    readonly Dictionary<int, ItemSummary> _byId;

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
                var inventoryType = s.Int("InventoryType");
                var name = s.Str("Display_lang");
                return new ItemSummary(x.itemId, name, SlotNames.GetValueOrDefault(inventoryType, ""), inventoryType,
                    s.Int("OverallQualityID"), item.TryGetValue(x.itemId, out var i) ? i.Int("IconFileDataID") : 0, IsInternal(name));
            })
            .Where(i => i.Name.Length > 0 && i.Slot.Length > 0)
            .OrderBy(i => i.Internal)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.ItemId)
            .ToList();
        _byId = _items.ToDictionary(i => i.ItemId);
    }

    public int Count => _items.Count;

    // Null when the item has no visual or no name.
    public ItemSummary? Get(int itemId) => _byId.GetValueOrDefault(itemId);

    // Look slot names an item may go in: its own, and the off hand for one-handers (dress.ts slotsForInventoryType).
    public static IReadOnlyList<string> SlotsFor(ItemSummary item) => item.InventoryType == 13 ? [item.Slot, "offhand"] : [item.Slot];

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
        var id = int.TryParse(text, out var n) ? n : (int?)null;
        var matches = _items.Where(i =>
            (string.IsNullOrEmpty(text) || i.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || i.ItemId == id)
            && (q.Slots is not { Count: > 0 } || q.Slots.Contains(i.Slot))
            && (q.Qualities is not { Count: > 0 } || q.Qualities.Contains(i.Quality))).ToList();
        return new ItemPage(matches.Count, matches.Skip(offset).Take(limit).ToList());
    }
}
