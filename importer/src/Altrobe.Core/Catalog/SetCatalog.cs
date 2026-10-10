using Altrobe.Core.Tables;
using T = Altrobe.Core.Tables.GameTableNames;

namespace Altrobe.Core.Catalog;

public sealed record SetPiece(string Slot, ItemSummary Item);

public sealed record SkippedPiece(int ItemId, string Reason);

// Internal: a developer or placeholder set by its name (ItemCatalog.IsInternal). Listed last.
// Unnamed: every piece is unnamed in this build (ItemSummary.Unnamed), as tier 2 is in the Forever beta.
// ClassMask: the classes every class-restricted piece allows (bit classId - 1), -1 when no piece is restricted.
// Quality: the best piece quality; for an unnamed tier set, its tier's (NotableSets.TierQuality); else null.
public sealed record ItemSetInfo(int SetId, string Name, IReadOnlyList<SetPiece> Pieces, IReadOnlyList<SkippedPiece> Skipped, bool Internal, bool Unnamed = false)
{
    public int ClassMask { get; init; } = -1;
    public int? Quality { get; init; }
}

// IncludeInternal: also list developer sets and match developer pieces. An exact set ID finds one either way.
public sealed record SetQuery(string? Text = null, int Limit = 20, int Offset = 0, bool IncludeInternal = false);

public sealed record SetPage(int Total, IReadOnlyList<ItemSetInfo> Sets);

// Item sets from ItemSet, each with the pieces that can be shown and the look slot each goes in.
// Slots are assigned in the set's order: an item's own slot if free, else another slot it fits (a
// second one-hander goes in the off hand), else it is skipped. Sets with no piece to show are left out.
public sealed class SetCatalog
{
    public const int MaxLimit = 50;
    readonly IReadOnlyList<ItemSetInfo> _sets;
    readonly Dictionary<int, ItemSetInfo> _byId;

    public SetCatalog(ITables tables, ItemCatalog items)
    {
        _sets = tables.Get(T.ItemSet)
            .Select(r => Build(r, items))
            .Where(s => s.Pieces.Count > 0)
            .OrderBy(s => s.Internal)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.SetId)
            .ToList();
        _byId = _sets.ToDictionary(s => s.SetId);
    }

    public int Count => _sets.Count;

    // Every set, in search order (internal sets last, then by name).
    public IReadOnlyList<ItemSetInfo> All => _sets;

    public ItemSetInfo? Get(int setId) => _byId.GetValueOrDefault(setId);

    // Notable sets (NotableSets) for a faction ("alliance" or "horde") and the given classes, grouped
    // PvP (rare), PvP (epic), then tiers 0 to 3; empty groups are left out. A named set wins over an
    // unnamed copy of the same name, and a set repeating another's name and classes is dropped.
    public IReadOnlyList<SetGroup> Notable(string? faction, IReadOnlyCollection<int> classIds)
    {
        var wanted = classIds.Aggregate(0, (m, c) => m | (1 << (c - 1)));
        var picked = _sets
            .Where(s => !s.Internal)
            .Select(s => (set: s, cls: NotableSets.Classify(s)))
            .Where(x => x.cls is { } c && (c.Faction == null || c.Faction == faction) && (c.ClassMask == -1 || (c.ClassMask & wanted) != 0))
            .Select(x => (x.set, group: x.cls!.Value.Group, mask: x.cls.Value.ClassMask))
            .GroupBy(x => (x.group, name: x.set.Name.ToLowerInvariant(), x.mask))
            .Select(g => g.OrderBy(x => x.set.Unnamed).ThenBy(x => x.set.SetId).First())
            .ToList();
        return NotableSets.GroupOrder
            .Select(group => new SetGroup(group, picked.Where(x => x.group == group)
                .OrderBy(x => TierClassOrder(x.mask)).ThenBy(x => x.set.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.set.SetId)
                .Select(x => x.set).ToList()))
            .Where(g => g.Sets.Count > 0)
            .ToList();
    }

    // Tier sets list in class order (warrior first); PvP sets, whose masks vary, by name.
    static int TierClassOrder(int mask) => NotableSets.Tiers.ToList().FindIndex(t => mask == 1 << (t.ClassId - 1)) is var i and >= 0 ? i : int.MaxValue;

    // Item IDs of every piece of every set, ascending.
    public IReadOnlyList<int> PieceItemIds() => _pieceItemIds ??= _sets.SelectMany(s => s.Pieces.Select(p => p.Item.ItemId)).Distinct().Order().ToList();
    IReadOnlyList<int>? _pieceItemIds;

    public SetPage Search(SetQuery q)
    {
        var text = q.Text?.Trim();
        var id = int.TryParse(text, out var n) ? n : (int?)null;
        var matches = _sets.Where(s => s.SetId == id || ((q.IncludeInternal || !s.Internal) && (string.IsNullOrEmpty(text)
            || s.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || s.Pieces.Any(p => (q.IncludeInternal || !p.Item.Internal) && p.Item.Name.Contains(text, StringComparison.OrdinalIgnoreCase))))).ToList();
        return new SetPage(matches.Count, matches.Skip(Math.Max(0, q.Offset)).Take(Math.Clamp(q.Limit, 1, MaxLimit)).ToList());
    }

    static ItemSetInfo Build(Row set, ItemCatalog items)
    {
        var pieces = new List<SetPiece>();
        var skipped = new List<SkippedPiece>();
        foreach (var itemId in set.Ints("ItemID").Where(i => i != 0))
        {
            if (items.Get(itemId) is not { } item)
            {
                skipped.Add(new SkippedPiece(itemId, "not worn, or has no visual"));
                continue;
            }
            var slots = ItemCatalog.SlotsFor(item);
            var free = slots.FirstOrDefault(s => pieces.All(p => p.Slot != s));
            if (free == null) skipped.Add(new SkippedPiece(itemId, $"{string.Join(" and ", slots)} already taken by this set"));
            else pieces.Add(new SetPiece(free, item));
        }
        var name = set.Str("Name_lang");
        var classMask = pieces.Select(p => p.Item.AllowableClass).Where(m => m is not (-1 or 0)).Aggregate(-1, (all, m) => all & m);
        var known = pieces.Where(p => !p.Item.Unnamed).Select(p => p.Item.Quality).ToList();
        var quality = known.Count > 0 ? known.Max() : NotableSets.TierQuality(name);
        return new ItemSetInfo(set.Int("ID"), name, pieces, skipped, ItemCatalog.IsInternal(name), pieces.Count > 0 && pieces.All(p => p.Item.Unnamed)) { ClassMask = classMask, Quality = quality };
    }
}
