using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Altrobe.Server.Tests;

// The MCP endpoint and the viewer-tab session, over the fake install. A "tab" here is a plain
// WebSocket client that answers commands the way the web app's session.ts does.
public class McpTests : IAsyncLifetime
{
    readonly TestApp _app = new();
    static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    public async Task InitializeAsync() => await _app.Installed();

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    async Task<McpClient> Client() => await McpClient.CreateAsync(
        new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri("http://127.0.0.1:5161/mcp"), TransportMode = HttpTransportMode.StreamableHttp }, _app.Local()),
        cancellationToken: Ct);

    static string Text(CallToolResult r) => string.Concat(r.Content.OfType<TextContentBlock>().Select(t => t.Text));

    async Task<WebSocket> Tab(Func<JsonObject, JsonObject> answer)
    {
        var ws = await _app.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://127.0.0.1:5161/api/v1/session"), Ct);
        _ = Task.Run(async () =>
        {
            var buf = new byte[64 * 1024];
            while (ws.State == WebSocketState.Open)
            {
                var r = await ws.ReceiveAsync(buf, CancellationToken.None);
                if (r.MessageType == WebSocketMessageType.Close) break;
                var msg = JsonNode.Parse(Encoding.UTF8.GetString(buf, 0, r.Count))!.AsObject();
                var reply = answer(msg);
                reply["type"] = "result";
                reply["id"] = msg["id"]!.GetValue<string>();
                await ws.SendAsync(Encoding.UTF8.GetBytes(reply.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        });
        return ws;
    }

    [Fact]
    public async Task ListsTheLookCommandsAsTools()
    {
        var tools = await (await Client()).ListToolsAsync(cancellationToken: Ct);
        var names = tools.Select(t => t.Name).ToHashSet();
        foreach (var n in new[] { "search_items", "search_sets", "equip_set", "build_outfit", "list_characters", "get_look", "equip_item", "unequip", "set_character", "set_customization",
                     "randomize_customization", "reset_customization", "compare", "wear_main_outfit", "set_visibility", "set_view", "share_link" })
            Assert.Contains(n, names);
    }

    [Fact]
    public async Task TellsTheModelWhatAltrobeIsAndWhenToUseIt()
    {
        var client = await Client();
        Assert.Contains("World of Warcraft: Forever", client.ServerInstructions);
        Assert.Contains("Never guess item IDs", client.ServerInstructions);
        // Tool lists can be loaded by name and first line only, so each description names the game.
        foreach (var t in await client.ListToolsAsync(cancellationToken: Ct))
            Assert.StartsWith("World of Warcraft: Forever", t.Description);
    }

    [Fact]
    public async Task SearchWorksWithoutAnOpenTab()
    {
        var r = await (await Client()).CallToolAsync("search_items", new Dictionary<string, object?> { ["query"] = "robe", ["slot"] = "chest" }, cancellationToken: Ct);
        Assert.NotEqual(true, r.IsError);
        var body = JsonNode.Parse(Text(r))!;
        Assert.Equal(3, body["total"]!.GetValue<int>());
        Assert.Contains(body["items"]!.AsArray(), i => i!["name"]!.GetValue<string>() == "Robe of the Archmage");
        Assert.All(body["items"]!.AsArray(), i => Assert.False(i!["internal"]!.GetValue<bool>()));
    }

    [Fact]
    public async Task ClaudeSearchLeavesOutDevItemsButAnExactIdFindsThem()
    {
        var client = await Client();
        var byName = JsonNode.Parse(Text(await client.CallToolAsync("search_items", new Dictionary<string, object?> { ["query"] = "glaive" }, cancellationToken: Ct)))!;
        Assert.Equal(0, byName["total"]!.GetValue<int>());
        var byId = JsonNode.Parse(Text(await client.CallToolAsync("search_items", new Dictionary<string, object?> { ["query"] = "5" }, cancellationToken: Ct)))!;
        Assert.Equal("(DNT) Test Glaive", byId["items"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task UnnamedSetsAreFoundAndMarked()
    {
        var client = await Client();
        var found = JsonNode.Parse(Text(await client.CallToolAsync("search_sets", new Dictionary<string, object?> { ["query"] = "wrath" }, cancellationToken: Ct)))!;
        var set = found["sets"]![0]!;
        Assert.Equal(501, set["setId"]!.GetValue<int>());
        Assert.True(set["unnamed"]!.GetValue<bool>());
        Assert.Equal("Battlegear of Wrath: chest", set["pieces"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task SearchFiltersByLevelArmorAndClass()
    {
        var client = await Client();
        var r = JsonNode.Parse(Text(await client.CallToolAsync("search_items", new Dictionary<string, object?> { ["min_level"] = 20, ["max_level"] = 40, ["armor"] = "leather" }, cancellationToken: Ct)))!;
        var item = r["items"]!.AsArray().Single()!;
        Assert.Equal((7, 30, "leather"), (item["itemId"]!.GetValue<int>(), item["requiredLevel"]!.GetValue<int>(), item["armor"]!.GetValue<string>()));
        var bad = await client.CallToolAsync("search_items", new Dictionary<string, object?> { ["armor"] = "wood" }, cancellationToken: Ct);
        Assert.True(bad.IsError);
    }

    [Fact]
    public async Task BuildOutfitFillsSlotsForALevelAndListsTheRest()
    {
        var client = await Client();
        var r = JsonNode.Parse(Text(await client.CallToolAsync("build_outfit", new Dictionary<string, object?> { ["level"] = 32, ["armor"] = "leather", ["min_quality"] = 2, ["max_quality"] = 3 }, cancellationToken: Ct)))!;
        Assert.Equal(7, r["items"]!["legs"]!["itemId"]!.GetValue<int>());
        Assert.Contains("chest", r["missing"]!.AsArray().Select(m => m!.GetValue<string>()));
        var tooLow = JsonNode.Parse(Text(await client.CallToolAsync("build_outfit", new Dictionary<string, object?> { ["level"] = 20, ["armor"] = "leather" }, cancellationToken: Ct)))!;
        Assert.Empty(tooLow["items"]!.AsObject());
    }

    [Fact]
    public async Task WeaponsHaveATypeAndHandsAndBuildOutfitFillsThemWhenAsked()
    {
        var client = await Client();
        var found = JsonNode.Parse(Text(await client.CallToolAsync("search_items", new Dictionary<string, object?> { ["weapon"] = "sword", ["hands"] = "two-hand" }, cancellationToken: Ct)))!;
        var claymore = found["items"]!.AsArray().Single()!;
        Assert.Equal((8, "sword", "two-hand"), (claymore["itemId"]!.GetValue<int>(), claymore["weapon"]!.GetValue<string>(), claymore["hands"]!.GetValue<string>()));
        var outfit = JsonNode.Parse(Text(await client.CallToolAsync("build_outfit", new Dictionary<string, object?> { ["level"] = 32, ["weapons"] = new[] { "sword" }, ["hands"] = "two-hand" }, cancellationToken: Ct)))!;
        Assert.Equal(8, outfit["items"]!["mainhand"]!["itemId"]!.GetValue<int>());
    }

    [Fact]
    public async Task ATwoHanderClearsTheOffHandAndAnOffHandItemIsRefusedWithOne()
    {
        var seen = new List<JsonObject>();
        var mainhand = 0;
        using var tab = await Tab(msg =>
        {
            seen.Add(msg);
            if (msg["command"]!.GetValue<string>() == "equip_item" && msg["args"]!["slot"]!.GetValue<string>() == "mainhand") mainhand = msg["args"]!["itemId"]!.GetValue<int>();
            var items = mainhand == 0 ? new JsonObject() : new JsonObject { ["mainhand"] = new JsonObject { ["itemId"] = mainhand, ["name"] = null } };
            return new JsonObject { ["ok"] = true, ["result"] = new JsonObject { ["items"] = items } };
        });
        var client = await Client();
        var r = await client.CallToolAsync("equip_item", new Dictionary<string, object?> { ["slot"] = "mainhand", ["itemId"] = 8 }, cancellationToken: Ct);
        Assert.NotEqual(true, r.IsError);
        Assert.Contains(seen, m => m["command"]!.GetValue<string>() == "unequip" && m["args"]!["slot"]!.GetValue<string>() == "offhand");

        var shield = await client.CallToolAsync("equip_item", new Dictionary<string, object?> { ["slot"] = "offhand", ["itemId"] = 9 }, cancellationToken: Ct);
        Assert.True(shield.IsError);
        Assert.Contains("two-handed", Text(shield));

        var bad = new[] { new Dictionary<string, object?> { ["race"] = 2, ["sex"] = 0, ["items"] = new Dictionary<string, int> { ["mainhand"] = 8, ["offhand"] = 9 } } };
        var cmp = await client.CallToolAsync("compare", new Dictionary<string, object?> { ["characters"] = bad }, cancellationToken: Ct);
        Assert.True(cmp.IsError);
        Assert.Contains("two-handed", Text(cmp));
    }

    [Fact]
    public async Task LookCommandsNeedAnOpenTab()
    {
        var r = await (await Client()).CallToolAsync("get_look", cancellationToken: Ct);
        Assert.True(r.IsError);
        Assert.Contains("Open Altrobe", Text(r));
    }

    [Fact]
    public async Task EquipIsCheckedAgainstTheCatalogThenSentToTheTab()
    {
        var seen = new List<JsonObject>();
        using var tab = await Tab(msg => { seen.Add(msg); return new JsonObject { ["ok"] = true, ["result"] = new JsonObject { ["items"] = new JsonObject { ["mainhand"] = new JsonObject { ["itemId"] = 2, ["name"] = null } } } }; });
        var client = await Client();
        var ct = Ct;

        var ok = await client.CallToolAsync("equip_item", new Dictionary<string, object?> { ["slot"] = "mainhand", ["itemId"] = 2 }, cancellationToken: ct);
        Assert.NotEqual(true, ok.IsError);
        Assert.Equal("Thunderfury", JsonNode.Parse(Text(ok))!["items"]!["mainhand"]!["name"]!.GetValue<string>()); // filled in by the server
        var sent = Assert.Single(seen);
        Assert.Equal("equip_item", sent["command"]!.GetValue<string>());
        Assert.Equal(2, sent["args"]!["itemId"]!.GetValue<int>());

        // An item the catalog does not have, or one in the wrong slot, never reaches the tab.
        var unknown = await client.CallToolAsync("equip_item", new Dictionary<string, object?> { ["slot"] = "head", ["itemId"] = 999 }, cancellationToken: ct);
        Assert.True(unknown.IsError);
        Assert.Contains("999", Text(unknown));
        var wrongSlot = await client.CallToolAsync("equip_item", new Dictionary<string, object?> { ["slot"] = "head", ["itemId"] = 1 }, cancellationToken: ct);
        Assert.True(wrongSlot.IsError);
        Assert.Contains("chest", Text(wrongSlot));
        Assert.Single(seen);
    }

    [Fact]
    public async Task EquipSetSendsEveryPieceInOneCommand()
    {
        var seen = new List<JsonObject>();
        using var tab = await Tab(msg => { seen.Add(msg); return new JsonObject { ["ok"] = true, ["result"] = new JsonObject() }; });
        var client = await Client();
        var found = JsonNode.Parse(Text(await client.CallToolAsync("search_sets", new Dictionary<string, object?> { ["query"] = "archmage" }, cancellationToken: Ct)))!;
        Assert.Equal(500, found["sets"]![0]!["setId"]!.GetValue<int>());

        var r = await client.CallToolAsync("equip_set", new Dictionary<string, object?> { ["setId"] = 500 }, cancellationToken: Ct);
        Assert.NotEqual(true, r.IsError);
        var sent = Assert.Single(seen);
        Assert.Equal("equip_items", sent["command"]!.GetValue<string>());
        Assert.Equal([("chest", 1), ("mainhand", 2)], sent["args"]!["items"]!.AsArray().Select(i => (i!["slot"]!.GetValue<string>(), i["itemId"]!.GetValue<int>())));
        // The answer says which pieces were left out, and why.
        Assert.Contains("Frostweave Robe", Text(r));

        var unknown = await client.CallToolAsync("equip_set", new Dictionary<string, object?> { ["setId"] = 999 }, cancellationToken: Ct);
        Assert.True(unknown.IsError);
        Assert.Single(seen);
    }

    [Fact]
    public async Task CompareCarriesOwnOutfitsCheckedAgainstTheCatalog()
    {
        var seen = new List<JsonObject>();
        using var tab = await Tab(msg => { seen.Add(msg); return new JsonObject { ["ok"] = true, ["result"] = new JsonObject() }; });
        var client = await Client();
        var chars = new[] { new Dictionary<string, object?> { ["race"] = 2, ["sex"] = 0, ["label"] = "Level 30", ["items"] = new Dictionary<string, int> { ["legs"] = 7 } } };
        var ok = await client.CallToolAsync("compare", new Dictionary<string, object?> { ["characters"] = chars }, cancellationToken: Ct);
        Assert.NotEqual(true, ok.IsError);
        var sent = Assert.Single(seen)["args"]!["characters"]![0]!;
        Assert.Equal(("Level 30", 7), (sent["label"]!.GetValue<string>(), sent["items"]!["legs"]!.GetValue<int>()));

        // A made-up item, or one in the wrong slot, never reaches the tab.
        var bad = new[] { new Dictionary<string, object?> { ["race"] = 2, ["sex"] = 0, ["items"] = new Dictionary<string, int> { ["head"] = 7 } } };
        var r = await client.CallToolAsync("compare", new Dictionary<string, object?> { ["characters"] = bad }, cancellationToken: Ct);
        Assert.True(r.IsError);
        Assert.Contains("legs", Text(r));
        Assert.Single(seen);

        await client.CallToolAsync("wear_main_outfit", new Dictionary<string, object?> { ["index"] = 1 }, cancellationToken: Ct);
        Assert.Equal(("wear_main_outfit", 0), (seen[1]["command"]!.GetValue<string>(), seen[1]["args"]!["index"]!.GetValue<int>()));
    }

    [Fact]
    public async Task ATabErrorComesBackAsAToolError()
    {
        using var tab = await Tab(_ => new JsonObject { ["ok"] = false, ["error"] = "unknown view \"top\"" });
        var r = await (await Client()).CallToolAsync("set_view", new Dictionary<string, object?> { ["view"] = "top" }, cancellationToken: Ct);
        Assert.True(r.IsError);
        Assert.Contains("unknown view", Text(r));
    }

    [Fact]
    public async Task TheSessionRefusesCrossSiteOrigins()
    {
        var ws = _app.Server.CreateWebSocketClient();
        ws.ConfigureRequest = req => req.Headers.Origin = "https://evil.com";
        await Assert.ThrowsAnyAsync<Exception>(() => ws.ConnectAsync(new Uri("ws://127.0.0.1:5161/api/v1/session"), Ct));
    }
}
