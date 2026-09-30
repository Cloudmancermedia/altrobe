namespace Altrobe.Core.Catalog;

// Armor: cloth, leather, mail or plate for armor slots (cloaks are cloth for everyone, so the back slot
// ignores it). Slots defaults to the armor slots plus the back. Weapons (weapon types, from
// ItemCatalog.WeaponTypes) fills the main hand: Hands "one-hand" or "two-hand" narrows it. OffHand
// "shield" or "held" fills the off hand, unless the main hand holds a two-hander.
public sealed record OutfitRequest(int Level, string? Armor = null, int? ClassId = null, int MinQuality = 2, int MaxQuality = 5, IReadOnlyList<string>? Slots = null,
    IReadOnlyList<string>? Weapons = null, string? Hands = null, string? OffHand = null);

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
            if (Pick(catalog, r, i => i.Slot == slot && (slot == "back" || r.Armor == null || i.Armor == r.Armor)) is { } pick) items[slot] = pick;
            else missing.Add(slot);
        }

        // Weapons only when asked for by type: the data does not say which weapons a class can use.
        string[] mainHands = r.Hands switch { "one-hand" => ["one-hand", "main hand"], "two-hand" => ["two-hand"], _ => ["one-hand", "main hand", "two-hand"] };
        var main = r.Weapons is { Count: > 0 } weapons
            ? Pick(catalog, r, i => i.Slot == "mainhand" && i.Weapon != null && weapons.Contains(i.Weapon) && i.Hands != null && mainHands.Contains(i.Hands))
            : null;
        if (main != null) items["mainhand"] = main;
        else missing.Add("mainhand");

        if (r.OffHand is { } off && main?.Hands != "two-hand")
        {
            var hands = off == "shield" ? "shield" : "held in off hand";
            if (Pick(catalog, r, i => i.Slot == "offhand" && i.Hands == hands) is { } pick) items["offhand"] = pick;
            else missing.Add("offhand");
        }
        return new Outfit(items, missing);
    }

    static ItemSummary? Pick(ItemCatalog catalog, OutfitRequest r, Func<ItemSummary, bool> fits) => catalog.All
        .Where(i => fits(i) && !i.Internal && !i.Unnamed
            && i.RequiredLevel is { } req && req <= r.Level && req > r.Level - Window
            && i.Quality >= r.MinQuality && i.Quality <= r.MaxQuality
            && (r.ClassId is not { } cls || i.AllowableClass == -1 || (i.AllowableClass & (1 << (cls - 1))) != 0))
        .OrderByDescending(i => i.RequiredLevel)
        .ThenByDescending(i => i.ItemLevel)
        .ThenBy(i => i.ItemId)
        .FirstOrDefault();
}
