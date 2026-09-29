namespace Altrobe.Core.Catalog;

// Armor: cloth, leather, mail or plate for armor slots (cloaks are cloth for everyone, so the back slot
// ignores it). Slots defaults to the armor slots plus the back.
public sealed record OutfitRequest(int Level, string? Armor = null, int? ClassId = null, int MinQuality = 2, int MaxQuality = 5, IReadOnlyList<string>? Slots = null);

public sealed record Outfit(IReadOnlyDictionary<string, ItemSummary> Items, IReadOnlyList<string> Missing);

// Gear for a level: in each slot, an item whose required level is within Window below the target,
// highest required level first, then highest item level, then lowest ID, so the same request always
// gives the same outfit. Developer and unnamed items (no known level) are never picked.
public static class OutfitBuilder
{
    public const int Window = 7;
    public static readonly IReadOnlyList<string> DefaultSlots = ["head", "shoulder", "chest", "waist", "legs", "feet", "wrist", "hands", "back"];

    public static Outfit Build(ItemCatalog catalog, OutfitRequest r)
    {
        var items = new Dictionary<string, ItemSummary>();
        var missing = new List<string>();
        foreach (var slot in r.Slots ?? DefaultSlots)
        {
            var pick = catalog.All
                .Where(i => i.Slot == slot && !i.Internal && !i.Unnamed
                    && i.RequiredLevel is { } req && req <= r.Level && req > r.Level - Window
                    && i.Quality >= r.MinQuality && i.Quality <= r.MaxQuality
                    && (slot == "back" || r.Armor == null || i.Armor == r.Armor)
                    && (r.ClassId is not { } cls || i.AllowableClass == -1 || (i.AllowableClass & (1 << (cls - 1))) != 0))
                .OrderByDescending(i => i.RequiredLevel)
                .ThenByDescending(i => i.ItemLevel)
                .ThenBy(i => i.ItemId)
                .FirstOrDefault();
            if (pick == null) missing.Add(slot);
            else items[slot] = pick;
        }
        return new Outfit(items, missing);
    }
}
