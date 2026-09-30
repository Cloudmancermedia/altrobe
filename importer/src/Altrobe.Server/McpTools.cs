using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Altrobe.Core.Catalog;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Altrobe.Server;

// The command API (Phase 3 spec) as MCP tools. Catalog reads run here; every look change is sent to
// the open viewer tab, which runs the same commands its buttons use. Items are checked against the
// catalog first, so a model can only equip items that exist.
[McpServerToolType]
public sealed class McpTools(AppState state, TabSession tab)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Sent once when a client connects; Claude Code puts it in the system prompt.
    public const string Instructions = """
        Altrobe is a 3D dressing room for World of Warcraft: Forever, running on this computer and open in
        the user's browser. Use these tools whenever the user wants to dress, gear, transmog or customize a
        WoW character, try items or outfits, or compare how a look shows on different races.

        - Always find items with search_items, then equip them by the returned itemId. Never guess item IDs.
        - For a whole set (a tier set, the High Warlord's gear), use search_sets and equip_set.
        - search_items and search_sets show only real items and sets. Developer, test and NPC items are left
          out; an exact item ID still finds one.
        - Items and sets marked unnamed have a model in this build but no item name, quality or level
          (all of tier 1 and tier 2 in the beta). Show them when asked, and say the build has no name for the
          pieces rather than inventing one; the set name is real.
        - Race IDs come from list_characters. Customization option and choice IDs come from get_look.
        - Weapons have a type (sword, axe, ...) and hands (one-hand, two-hand, ...). A two-hander in the main
          hand clears the off hand. build_outfit fills the main hand only when given weapon types, since
          the data does not say which weapons a class can use.
        - For a leveling journey ("an Orc warrior from 10 to 60"), call build_outfit once per level, then put
          the main character at one level and the others in compare with their own items and a label.
        - compare shows up to 5 more characters side by side. They wear the main outfit unless you give one
          its own items, which is how to show the same character at several levels.
        - The catalog holds each item's name, slot, quality, required level, item level and armor type, and
          search_items filters on them: for a level 30 warrior, try min_level 25, max_level 32, armor mail,
          class_id 1. It does not know where an item drops or which quest gives it. If you pick items by
          source from your own knowledge of the game, say so.
        - Look changes need an open Altrobe tab. If a tool says none is open, ask the user to open Altrobe.
        """;

    const string Slots = "head, neck, shoulder, shirt, chest, waist, legs, feet, wrist, hands, back, mainhand, offhand, tabard";
    const string WeaponList = "axe, bow, crossbow, dagger, fishing pole, fist weapon, gun, mace, misc, polearm, staff, sword, thrown, wand";

    // A comma-separated list checked against the allowed values, lowercased; null when empty.
    static List<string>? List(string? text, IEnumerable<string> allowed, string what)
    {
        var values = text?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(v => v.ToLowerInvariant()).ToList();
        if (values is not { Count: > 0 }) return null;
        var known = allowed.ToHashSet();
        if (values.FirstOrDefault(v => !known.Contains(v)) is { } bad) throw new McpException($"Unknown {what} \"{bad}\". Use one of: {string.Join(", ", known.Order())}.");
        return values;
    }

    Altrobe.Core.Builds.BuildSession Session => state.Current?.Session
        ?? throw new McpException("No WoW install is selected yet. Open Altrobe in the browser and pick your World of Warcraft: Forever install.");

    async Task<string> Tab(string command, JsonObject args, CancellationToken ct)
    {
        JsonNode? result;
        try { result = await tab.CallAsync(command, args, ct); }
        catch (TabSession.TabException e) { throw new McpException(e.Message); }
        // The tab learns an item's name only once it has drawn it, so fill in names it lacks, wherever
        // an item appears (the main outfit and side-by-side outfits).
        if (state.Current?.Session is { } session) FillNames(result, session.Items);
        return result?.ToJsonString(Json) ?? "{}";
    }

    [McpServerTool(Name = "search_items", ReadOnly = true)]
    [Description("World of Warcraft: Forever dressing room (Altrobe). Search the item catalog by name or item ID, and filter by slot, quality, required level, armor type and class. Only items with a visual are listed. Use the returned itemId with equip_item; never guess IDs.")]
    public string SearchItems(
        [Description("Part of the item name, or an item ID. Leave empty to browse by the filters.")] string? query = null,
        [Description($"Look slot to filter by: {Slots}")] string? slot = null,
        [Description("Quality to filter by: 0 poor, 1 common, 2 uncommon, 3 rare, 4 epic, 5 legendary")] int? quality = null,
        [Description("Lowest required level")] int? min_level = null,
        [Description("Highest required level")] int? max_level = null,
        [Description("Armor type: cloth, leather, mail or plate. Comma-separate several.")] string? armor = null,
        [Description("Class ID (from list_characters): leave out items only other classes can use. It does not check armor proficiency; pick the armor type for that.")] int? class_id = null,
        [Description($"Weapon type: {WeaponList}. Comma-separate several.")] string? weapon = null,
        [Description("Hands: one-hand, two-hand, main hand, off hand, shield, held in off hand, ranged or thrown. Comma-separate several.")] string? hands = null,
        [Description("Results to return, 1-50")] int limit = 20,
        [Description("Results to skip, for paging")] int offset = 0)
    {
        var armorTypes = armor?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(a => a.ToLowerInvariant()).ToList();
        if (armorTypes?.FirstOrDefault(a => !ItemCatalog.ArmorTypes.Values.Contains(a)) is { } badArmor)
            throw new McpException($"Unknown armor type \"{badArmor}\". Use cloth, leather, mail or plate.");
        var weapons = List(weapon, ItemCatalog.WeaponTypes.Values, "weapon type");
        var handsList = List(hands, ItemCatalog.HandsByInventoryType.Values, "hands");
        var page = Session.Items.Search(new ItemQuery(query, slot is null ? null : [slot], quality is null ? null : [quality.Value], Math.Clamp(limit, 1, 50), offset,
            MinLevel: min_level, MaxLevel: max_level, Armor: armorTypes, ClassId: class_id, Weapons: weapons, Hands: handsList));
        return JsonSerializer.Serialize(new
        {
            total = page.Total,
            items = page.Items.Select(i => new { i.ItemId, i.Name, i.Slot, quality = i.Unnamed ? (int?)null : i.Quality, i.RequiredLevel, i.ItemLevel, i.Armor, i.Weapon, i.Hands, i.Internal, i.Unnamed }),
        }, Json);
    }

    [McpServerTool(Name = "build_outfit", ReadOnly = true)]
    [Description("World of Warcraft: Forever dressing room (Altrobe). Gear for one level from the catalog: in each slot, the item with the highest required level within 7 levels below the target, then the highest item level. The same request always gives the same outfit. It returns items per slot (pass them to compare or equip_item) and the slots it could not fill. For a leveling journey, call it once per level.")]
    public string BuildOutfit(
        [Description("Character level, 1-60")] int level,
        [Description("Armor type for armor slots: cloth, leather, mail or plate. Pick what the class wears at that level.")] string? armor = null,
        [Description("Class ID (from list_characters): leave out items only other classes can use")] int? class_id = null,
        [Description("Lowest quality: 0 poor, 1 common, 2 uncommon (green), 3 rare (blue), 4 epic")] int min_quality = 2,
        [Description("Highest quality")] int max_quality = 5,
        [Description("Armor slots to fill; default head, shoulder, chest, waist, legs, feet, wrist, hands, back")] string[]? slots = null,
        [Description($"Weapon types for the main hand ({WeaponList}); without them the main hand stays empty, because the data does not say which weapons a class can use")] string[]? weapons = null,
        [Description("Main hand: one-hand or two-hand; default either")] string? hands = null,
        [Description("Off hand: shield or held (an off-hand item); skipped with a two-hander")] string? off_hand = null)
    {
        var a = armor?.Trim().ToLowerInvariant();
        if (a != null && !ItemCatalog.ArmorTypes.Values.Contains(a)) throw new McpException($"Unknown armor type \"{armor}\". Use cloth, leather, mail or plate.");
        var w = weapons is { Length: > 0 } ? List(string.Join(",", weapons), ItemCatalog.WeaponTypes.Values, "weapon type") : null;
        if (hands is not (null or "one-hand" or "two-hand")) throw new McpException("hands must be one-hand or two-hand.");
        if (off_hand is not (null or "shield" or "held")) throw new McpException("off_hand must be shield or held.");
        var outfit = OutfitBuilder.Build(Session.Items, new OutfitRequest(level, a, class_id, min_quality, max_quality, slots, w, hands, off_hand));
        return JsonSerializer.Serialize(new
        {
            level,
            items = outfit.Items.ToDictionary(kv => kv.Key, kv => new { kv.Value.ItemId, kv.Value.Name, kv.Value.RequiredLevel, kv.Value.Quality, kv.Value.Armor, kv.Value.Weapon, kv.Value.Hands }),
            missing = outfit.Missing,
        }, Json);
    }

    [McpServerTool(Name = "search_sets", ReadOnly = true)]
    [Description("World of Warcraft: Forever dressing room (Altrobe). Search item sets (tier sets, PvP sets such as the High Warlord's, dungeon sets) by set name, a piece's name, or set ID. Each set lists the pieces that can be shown and the slot each goes in. Equip one with equip_set.")]
    public string SearchSets(
        [Description("Part of the set name or of a piece's name, or a set ID. Leave empty to browse.")] string? query = null,
        [Description("Results to return, 1-20")] int limit = 10,
        [Description("Results to skip, for paging")] int offset = 0)
    {
        var page = Session.Sets.Search(new SetQuery(query, Math.Clamp(limit, 1, 20), offset));
        return JsonSerializer.Serialize(new { total = page.Total, sets = page.Sets.Select(s => new
        {
            s.SetId, s.Name, s.Internal, s.Unnamed,
            pieces = s.Pieces.Select(p => new { p.Slot, p.Item.ItemId, p.Item.Name }),
        }) }, Json);
    }

    [McpServerTool(Name = "equip_set")]
    [Description("World of Warcraft: Forever dressing room (Altrobe). Put every piece of an item set on the main character in one step. Other slots keep what they have. Find the setId with search_sets.")]
    public async Task<string> EquipSet([Description("Set ID from search_sets")] int setId, CancellationToken ct)
    {
        var set = Session.Sets.Get(setId) ?? throw new McpException($"Set {setId} is not in the catalog, or has no piece that can be shown. Use search_sets to find a set ID.");
        var items = new JsonArray(set.Pieces.Select(p => (JsonNode)new JsonObject { ["slot"] = p.Slot, ["itemId"] = p.Item.ItemId }).ToArray());
        var look = JsonNode.Parse(await Tab("equip_items", new() { ["items"] = items }, ct));
        var skipped = set.Skipped.Select(s => new { s.ItemId, name = Session.Items.Get(s.ItemId)?.Name, s.Reason });
        return JsonSerializer.Serialize(new { set = set.Name, skipped, look }, Json);
    }

    [McpServerTool(Name = "list_characters", ReadOnly = true)]
    [Description("World of Warcraft: Forever dressing room (Altrobe). The playable races, which sexes each has, whether each has HD and SD models, and its classes.")]
    public string ListCharacters() => JsonSerializer.Serialize(Session.Characters.Races().Select(r => new
    {
        r.Race, r.Name,
        sexes = r.Sexes.Select(s => new { sex = s.Sex == 0 ? "0 (male)" : "1 (female)", s.Hd, s.Sd }),
        classes = r.Classes.Select(c => c.Name),
    }), Json);

    [McpServerTool(Name = "get_look", ReadOnly = true)]
    [Description("World of Warcraft: Forever dressing room (Altrobe). The look shown in the Altrobe tab: character, equipped items with names, customization choices, characters shown side by side, and any notices. Includes every customization option and its choices, for set_customization.")]
    public Task<string> GetLook(CancellationToken ct) => Tab("get_look", [], ct);

    [McpServerTool(Name = "equip_item")]
    [Description("World of Warcraft: Forever dressing room (Altrobe). Put an item in a slot. Find the itemId with search_items first.")]
    public async Task<string> EquipItem([Description($"Look slot: {Slots}")] string slot, [Description("Item ID from search_items")] int itemId, CancellationToken ct)
    {
        CheckItem(slot, itemId);
        var item = Session.Items.Get(itemId)!;
        if (slot == "offhand")
        {
            // The off hand is empty while the main hand holds a two-hander, as in the game.
            var look = JsonNode.Parse(await Tab("get_look", [], ct));
            if (look?["items"]?["mainhand"]?["itemId"]?.GetValue<int>() is { } mainId && Session.Items.Get(mainId) is { Hands: "two-hand" } main)
                throw new McpException($"The main hand holds {main.Name}, a two-handed weapon, so the off hand stays empty. Equip a one-handed weapon first.");
        }
        var result = await Tab("equip_item", new() { ["slot"] = slot, ["itemId"] = itemId }, ct);
        if (slot == "mainhand" && item.Hands == "two-hand") result = await Tab("unequip", new() { ["slot"] = "offhand" }, ct);
        return result;
    }

    static void FillNames(JsonNode? node, ItemCatalog items)
    {
        if (node is JsonArray list) foreach (var n in list) FillNames(n, items);
        if (node is not JsonObject o) return;
        if (o.ContainsKey("itemId") && o["name"] is null && o["itemId"]?.GetValue<int>() is { } id && items.Get(id) is { } item) o["name"] = item.Name;
        foreach (var (_, child) in o.ToList()) FillNames(child, items);
    }

    // Throws a message for the model unless the item exists in the catalog and fits the slot.
    void CheckItem(string slot, int itemId)
    {
        var item = Session.Items.Get(itemId) ?? throw new McpException($"Item {itemId} is not in the catalog, or has no visual. Use search_items to find an item ID.");
        var slots = ItemCatalog.SlotsFor(item);
        if (!slots.Contains(slot)) throw new McpException($"{item.Name} ({itemId}) goes in {string.Join(" or ", slots)}, not {slot}.");
    }

    [McpServerTool(Name = "unequip")]
    [Description("World of Warcraft: Forever dressing room (Altrobe). Clear a slot.")]
    public Task<string> Unequip([Description($"Look slot: {Slots}")] string slot, CancellationToken ct) => Tab("unequip", new() { ["slot"] = slot }, ct);

    [McpServerTool(Name = "set_character")]
    [Description("World of Warcraft: Forever dressing room (Altrobe). Change the main character's race, sex and models. Race IDs come from list_characters. This resets customization choices.")]
    public Task<string> SetCharacter([Description("Race ID")] int race, [Description("0 male, 1 female")] int sex,
        [Description("hd or sd; leave out to keep the current one")] string? models, CancellationToken ct) =>
        Tab("set_character", new() { ["race"] = race, ["sex"] = sex, ["models"] = models }, ct);

    [McpServerTool(Name = "set_customization")]
    [Description("World of Warcraft: Forever dressing room (Altrobe). Pick a customization choice (skin color, face, hair style and so on) for the main character. Option and choice IDs come from get_look.")]
    public Task<string> SetCustomization([Description("Option ID")] int optionId, [Description("Choice ID")] int choiceId, CancellationToken ct) =>
        Tab("set_customization", new() { ["optionId"] = optionId, ["choiceId"] = choiceId }, ct);

    [McpServerTool(Name = "randomize_customization")]
    [Description("World of Warcraft: Forever dressing room (Altrobe). Pick a random choice for every customization option of the main character.")]
    public Task<string> RandomizeCustomization(CancellationToken ct) => Tab("randomize_customization", [], ct);

    [McpServerTool(Name = "reset_customization")]
    [Description("World of Warcraft: Forever dressing room (Altrobe). Set every customization option back to its default.")]
    public Task<string> ResetCustomization(CancellationToken ct) => Tab("reset_customization", [], ct);

    [McpServerTool(Name = "compare")]
    [Description("World of Warcraft: Forever dressing room (Altrobe). Show up to 5 more characters side by side. Each wears the main character's outfit, unless you give it its own items (for example the same character at levels 10, 30 and 60). A label such as \"Level 30\" shows above it. Pass an empty list to show only the main character.")]
    public Task<string> Compare([Description("Characters: race ID, sex (0 male, 1 female), models (hd or sd, default hd), and optionally label, items (slot to item ID, from search_items), hide (slots) and custom (option ID to choice ID)")] CompareCharacter[] characters, CancellationToken ct)
    {
        foreach (var c in characters)
        {
            foreach (var (slot, itemId) in c.Items ?? [])
                CheckItem(slot, itemId);
            if (c.Items is { } own && own.TryGetValue("mainhand", out var mainId) && own.ContainsKey("offhand") && Session.Items.Get(mainId) is { Hands: "two-hand" } main)
                throw new McpException($"{main.Name} is two-handed, so that character's off hand must stay empty.");
        }
        return Tab("compare", new() { ["characters"] = JsonSerializer.SerializeToNode(characters, Json) }, ct);
    }

    [McpServerTool(Name = "wear_main_outfit")]
    [Description("World of Warcraft: Forever dressing room (Altrobe). A side-by-side character drops its own outfit and wears the main character's again.")]
    public Task<string> WearMainOutfit([Description("Which side-by-side character: 1 is the first one after the main character")] int index, CancellationToken ct) =>
        Tab("wear_main_outfit", new() { ["index"] = index - 1 }, ct);

    [McpServerTool(Name = "set_visibility")]
    [Description("World of Warcraft: Forever dressing room (Altrobe). Hide or show an equipped item without removing it, such as the helm or cloak.")]
    public Task<string> SetVisibility([Description($"Look slot: {Slots}")] string slot, [Description("false hides the item")] bool visible, CancellationToken ct) =>
        Tab("set_visibility", new() { ["slot"] = slot, ["visible"] = visible }, ct);

    [McpServerTool(Name = "set_view")]
    [Description("World of Warcraft: Forever dressing room (Altrobe). Turn the camera: front, side, back or head.")]
    public Task<string> SetView([Description("front, side, back or head")] string view, CancellationToken ct) => Tab("set_view", new() { ["view"] = view }, ct);

    [McpServerTool(Name = "share_link", ReadOnly = true)]
    [Description("World of Warcraft: Forever dressing room (Altrobe). A link that opens the current look in Altrobe on any computer with Altrobe installed.")]
    public Task<string> ShareLink(CancellationToken ct) => Tab("share_link", [], ct);

    public sealed record CompareCharacter(int Race, int Sex, string? Models, string? Label = null,
        Dictionary<string, int>? Items = null, string[]? Hide = null, Dictionary<string, int>? Custom = null);
}
