using Altrobe.Core.Tables;
using T = Altrobe.Core.Tables.GameTableNames;

namespace Altrobe.Core.Catalog;

public sealed record SetPiece(string Slot, ItemSummary Item);

public sealed record SkippedPiece(int ItemId, string Reason);

// Internal: a developer or placeholder set by its name (ItemCatalog.IsInternal). Listed last.
// Unnamed: every piece is unnamed in this build (ItemSummary.Unnamed), as tier 2 is in the Forever beta.
public sealed record ItemSetInfo(int SetId, string Name, IReadOnlyList<SetPiece> Pieces, IReadOnlyList<SkippedPiece> Skipped, bool Internal, bool Unnamed = false);

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

    public ItemSetInfo? Get(int setId) => _byId.GetValueOrDefault(setId);

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
        return new ItemSetInfo(set.Int("ID"), name, pieces, skipped, ItemCatalog.IsInternal(name), pieces.Count > 0 && pieces.All(p => p.Item.Unnamed));
    }
}
