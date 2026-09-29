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

    const string Slots = "head, neck, shoulder, shirt, chest, waist, legs, feet, wrist, hands, back, mainhand, offhand, tabard";

    Altrobe.Core.Builds.BuildSession Session => state.Current?.Session
        ?? throw new McpException("No WoW install is selected yet. Open Altrobe in the browser and pick your World of Warcraft: Forever install.");

    async Task<string> Tab(string command, JsonObject args, CancellationToken ct)
    {
        JsonNode? result;
        try { result = await tab.CallAsync(command, args, ct); }
        catch (TabSession.TabException e) { throw new McpException(e.Message); }
        // The tab learns an item's name only once it has drawn it, so fill in names it lacks.
        if (result?["items"] is JsonObject items && state.Current?.Session is { } session)
            foreach (var (_, entry) in items)
                if (entry is JsonObject e && e["name"] is null && e["itemId"]?.GetValue<int>() is { } id && session.Items.Get(id) is { } item)
                    e["name"] = item.Name;
        return result?.ToJsonString(Json) ?? "{}";
    }

    [McpServerTool(Name = "search_items", ReadOnly = true)]
    [Description("Search the item catalog by name or item ID. Only items with a visual are listed. Use the returned itemId with equip_item; never guess IDs.")]
    public string SearchItems(
        [Description("Part of the item name, or an item ID. Leave empty to browse by slot or quality.")] string? query = null,
        [Description($"Look slot to filter by: {Slots}")] string? slot = null,
        [Description("Quality to filter by: 0 poor, 1 common, 2 uncommon, 3 rare, 4 epic, 5 legendary")] int? quality = null,
        [Description("Results to return, 1-50")] int limit = 20,
        [Description("Results to skip, for paging")] int offset = 0)
    {
        var page = Session.Items.Search(new ItemQuery(query, slot is null ? null : [slot], quality is null ? null : [quality.Value], Math.Clamp(limit, 1, 50), offset));
        return JsonSerializer.Serialize(new { total = page.Total, items = page.Items.Select(i => new { i.ItemId, i.Name, i.Slot, i.Quality }) }, Json);
    }

    [McpServerTool(Name = "list_characters", ReadOnly = true)]
    [Description("The playable races, which sexes each has, whether each has HD and SD models, and its classes.")]
    public string ListCharacters() => JsonSerializer.Serialize(Session.Characters.Races().Select(r => new
    {
        r.Race, r.Name,
        sexes = r.Sexes.Select(s => new { sex = s.Sex == 0 ? "0 (male)" : "1 (female)", s.Hd, s.Sd }),
        classes = r.Classes.Select(c => c.Name),
    }), Json);

    [McpServerTool(Name = "get_look", ReadOnly = true)]
    [Description("The look shown in the Altrobe tab: character, equipped items with names, customization choices, characters shown side by side, and any notices. Includes every customization option and its choices, for set_customization.")]
    public Task<string> GetLook(CancellationToken ct) => Tab("get_look", [], ct);

    [McpServerTool(Name = "equip_item")]
    [Description("Put an item in a slot. Find the itemId with search_items first.")]
    public Task<string> EquipItem([Description($"Look slot: {Slots}")] string slot, [Description("Item ID from search_items")] int itemId, CancellationToken ct)
    {
        var item = Session.Items.Get(itemId) ?? throw new McpException($"Item {itemId} is not in the catalog, or has no visual. Use search_items to find an item ID.");
        var slots = ItemCatalog.SlotsFor(item);
        if (!slots.Contains(slot)) throw new McpException($"{item.Name} ({itemId}) goes in {string.Join(" or ", slots)}, not {slot}.");
        return Tab("equip_item", new() { ["slot"] = slot, ["itemId"] = itemId }, ct);
    }

    [McpServerTool(Name = "unequip")]
    [Description("Clear a slot.")]
    public Task<string> Unequip([Description($"Look slot: {Slots}")] string slot, CancellationToken ct) => Tab("unequip", new() { ["slot"] = slot }, ct);

    [McpServerTool(Name = "set_character")]
    [Description("Change the main character's race, sex and models. Race IDs come from list_characters. This resets customization choices.")]
    public Task<string> SetCharacter([Description("Race ID")] int race, [Description("0 male, 1 female")] int sex,
        [Description("hd or sd; leave out to keep the current one")] string? models, CancellationToken ct) =>
        Tab("set_character", new() { ["race"] = race, ["sex"] = sex, ["models"] = models }, ct);

    [McpServerTool(Name = "set_customization")]
    [Description("Pick a customization choice (skin color, face, hair style and so on) for the main character. Option and choice IDs come from get_look.")]
    public Task<string> SetCustomization([Description("Option ID")] int optionId, [Description("Choice ID")] int choiceId, CancellationToken ct) =>
        Tab("set_customization", new() { ["optionId"] = optionId, ["choiceId"] = choiceId }, ct);

    [McpServerTool(Name = "randomize_customization")]
    [Description("Pick a random choice for every customization option of the main character.")]
    public Task<string> RandomizeCustomization(CancellationToken ct) => Tab("randomize_customization", [], ct);

    [McpServerTool(Name = "reset_customization")]
    [Description("Set every customization option back to its default.")]
    public Task<string> ResetCustomization(CancellationToken ct) => Tab("reset_customization", [], ct);

    [McpServerTool(Name = "compare")]
    [Description("Show the current outfit on up to 3 more characters side by side. Pass an empty list to show only the main character.")]
    public Task<string> Compare([Description("Characters: race ID, sex (0 male, 1 female), models (hd or sd, default hd)")] CompareCharacter[] characters, CancellationToken ct) =>
        Tab("compare", new() { ["characters"] = JsonSerializer.SerializeToNode(characters, Json) }, ct);

    [McpServerTool(Name = "set_visibility")]
    [Description("Hide or show an equipped item without removing it, such as the helm or cloak.")]
    public Task<string> SetVisibility([Description($"Look slot: {Slots}")] string slot, [Description("false hides the item")] bool visible, CancellationToken ct) =>
        Tab("set_visibility", new() { ["slot"] = slot, ["visible"] = visible }, ct);

    [McpServerTool(Name = "set_view")]
    [Description("Turn the camera: front, side, back or head.")]
    public Task<string> SetView([Description("front, side, back or head")] string view, CancellationToken ct) => Tab("set_view", new() { ["view"] = view }, ct);

    [McpServerTool(Name = "share_link", ReadOnly = true)]
    [Description("A link that opens the current look in Altrobe on any computer with Altrobe installed.")]
    public Task<string> ShareLink(CancellationToken ct) => Tab("share_link", [], ct);

    public sealed record CompareCharacter(int Race, int Sex, string? Models);
}
