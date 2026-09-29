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
        foreach (var n in new[] { "search_items", "list_characters", "get_look", "equip_item", "unequip", "set_character", "set_customization",
                     "randomize_customization", "reset_customization", "compare", "set_visibility", "set_view", "share_link" })
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
