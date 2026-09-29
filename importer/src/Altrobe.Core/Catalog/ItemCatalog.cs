using Altrobe.Core.Tables;
using T = Altrobe.Core.Tables.GameTableNames;

namespace Altrobe.Core.Catalog;

// Internal: a developer or NPC item by its name (see ItemCatalog.IsInternal). Left out of searches
// unless asked for, and listed last when included.
// Unnamed: the build has a model for the item but no ItemSparse row, so no name, quality or level.
// Its name is made up from its set and slot (or its slot and ID), and it is listed after named items.
// RequiredLevel and ItemLevel are null for unnamed items. Armor is cloth, leather, mail or plate, else
// null. AllowableClass is ItemSparse's class mask (bit classId - 1; -1 for any class).
public sealed record ItemSummary(int ItemId, string Name, string Slot, int InventoryType, int Quality, int IconFileDataId, bool Internal = false, bool Unnamed = false)
{
    public int? RequiredLevel { get; init; }
    public int? ItemLevel { get; init; }
    public string? Armor { get; init; }
    public int AllowableClass { get; init; } = -1;
}

// IncludeInternal: also list developer and NPC items. An exact item ID finds one either way.
// MinLevel and MaxLevel bound the required level; items with no known level (unnamed) never match them.
// Armor: armor types to keep. ClassId: leave out items restricted to other classes.
public sealed record ItemQuery(string? Text = null, IReadOnlyCollection<string>? Slots = null, IReadOnlyCollection<int>? Qualities = null, int Limit = 50, int Offset = 0, bool IncludeInternal = false,
    int? MinLevel = null, int? MaxLevel = null, IReadOnlyCollection<string>? Armor = null, int? ClassId = null);

public sealed record ItemPage(int Total, IReadOnlyList<ItemSummary> Items);

// Every wearable item with a name and a visual: ItemModifiedAppearance -> ItemAppearance ->
// ItemDisplayInfo. Slot is the web app's look slot name for the item's InventoryType.
public sealed class ItemCatalog
{
    public const int MaxLimit = 200;
    // Quality of an unnamed item, whose ItemSparse row (and so its quality) is missing.
    public const int UnknownQuality = -1;
    // Item class 4 (armor) subclasses.
    public static readonly IReadOnlyDictionary<int, string> ArmorTypes = new Dictionary<int, string> { [1] = "cloth", [2] = "leather", [3] = "mail", [4] = "plate" };

    static string? ArmorOf(Row? item) => item != null && item.Int("ClassID") == 4 ? ArmorTypes.GetValueOrDefault(item.Int("SubclassID")) : null;

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

        // The first set that lists an item names its unnamed pieces.
        var setOf = new Dictionary<int, string>();
        foreach (var set in tables.Get(T.ItemSet))
            foreach (var id in set.Ints("ItemID").Where(i => i != 0))
                setOf.TryAdd(id, set.Str("Name_lang"));

        _items = tables.Get(T.ItemModifiedAppearance).GroupByColumn("ItemID")
            .Select(g => (itemId: g.Key, ima: PrimaryAppearance(g.Value)))
            .Where(x => (sparse.ContainsKey(x.itemId) || item.ContainsKey(x.itemId))
                && appearance.TryGetValue(x.ima.Int("ItemAppearanceID"), out var a)
                && display.ContainsKey(a.Int("ItemDisplayInfoID")))
            .Select(x =>
            {
                var icon = item.TryGetValue(x.itemId, out var i) ? i.Int("IconFileDataID") : 0;
                if (sparse.TryGetValue(x.itemId, out var s))
                {
                    var inventoryType = s.Int("InventoryType");
                    var name = s.Str("Display_lang");
                    return new ItemSummary(x.itemId, name, SlotNames.GetValueOrDefault(inventoryType, ""), inventoryType,
                        s.Int("OverallQualityID"), icon, IsInternal(name))
                    {
                        RequiredLevel = s.Int("RequiredLevel"), ItemLevel = s.Int("ItemLevel"), Armor = ArmorOf(i), AllowableClass = s.Int("AllowableClass"),
                    };
                }
                var type = i!.Int("InventoryType");
                var slot = SlotNames.GetValueOrDefault(type, "");
                var made = setOf.TryGetValue(x.itemId, out var setName) ? $"{setName}: {slot}" : $"Unnamed {slot} (item {x.itemId})";
                return new ItemSummary(x.itemId, made, slot, type, UnknownQuality, icon, setName is not null && IsInternal(setName), Unnamed: true) { Armor = ArmorOf(i) };
            })
            .Where(i => i.Name.Length > 0 && i.Slot.Length > 0)
            .OrderBy(i => i.Internal)
            .ThenBy(i => i.Unnamed)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.ItemId)
            .ToList();
        _byId = _items.ToDictionary(i => i.ItemId);
    }

    public int Count => _items.Count;

    // Every item, in search order.
    public IReadOnlyList<ItemSummary> All => _items;

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
            (i.ItemId == id || ((q.IncludeInternal || !i.Internal) && (string.IsNullOrEmpty(text) || i.Name.Contains(text, StringComparison.OrdinalIgnoreCase))))
            && (q.Slots is not { Count: > 0 } || q.Slots.Contains(i.Slot))
            && (q.Qualities is not { Count: > 0 } || q.Qualities.Contains(i.Quality))
            && (q.MinLevel is not { } min || i.RequiredLevel >= min)
            && (q.MaxLevel is not { } max || i.RequiredLevel <= max)
            && (q.Armor is not { Count: > 0 } || (i.Armor != null && q.Armor.Contains(i.Armor)))
            && (q.ClassId is not { } cls || i.AllowableClass == -1 || (i.AllowableClass & (1 << (cls - 1))) != 0)).ToList();
        return new ItemPage(matches.Count, matches.Skip(offset).Take(limit).ToList());
    }
}
