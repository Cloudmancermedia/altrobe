using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Altrobe.Server.Tests;

public class AssistantPromptTests
{
    // Plays one scripted turn per model call; the last turn repeats.
    sealed class ScriptedModel(params Func<IList<ChatMessage>, ChatResponseUpdate[]>[] turns) : IChatClient, IAssistantModels
    {
        public List<List<ChatMessage>> Seen { get; } = [];
        int _turn;

        public IChatClient Create(AssistantSettings settings, string? key) => this;

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        {
            var updates = new List<ChatResponseUpdate>();
            await foreach (var u in GetStreamingResponseAsync(messages, options, ct)) updates.Add(u);
            return updates.ToChatResponse();
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var list = messages.ToList();
            Seen.Add(list);
            var turn = turns[Math.Min(_turn++, turns.Length - 1)];
            foreach (var u in turn(list)) yield return u;
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    static int _calls;
    static ChatResponseUpdate[] Call(string tool, Dictionary<string, object?> args) =>
        [new(ChatRole.Assistant, [new FunctionCallContent($"call{Interlocked.Increment(ref _calls)}", tool, args)])];
    static ChatResponseUpdate[] Reply(string text) =>
        [new(ChatRole.Assistant, text), new(ChatRole.Assistant, [new UsageContent(new UsageDetails { InputTokenCount = 120, OutputTokenCount = 15 })])];

    static async Task<HttpClient> Ready(TestApp app)
    {
        var client = await app.Installed();
        await client.PutAsJsonAsync("/api/v1/assistant/settings", new { provider = "anthropic", model = "claude-haiku-4-5", apiKey = "sk-ant-test-key-1234" });
        return client;
    }

    static async Task<List<JsonElement>> Events(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/x-ndjson", r.Content.Headers.ContentType?.MediaType);
        var text = await r.Content.ReadAsStringAsync();
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement).ToList();
    }

    static string Type(JsonElement e) => e.GetProperty("type").GetString()!;

    [Fact]
    public async Task ToolCallsStreamAsStepsThenTheReplyAndTokenUsage()
    {
        var model = new ScriptedModel(_ => Call("search_items", new() { ["query"] = "Thunderfury" }), _ => Reply("Found Thunderfury."));
        using var app = new TestApp { AssistantModels = model };
        var client = await Ready(app);

        var events = await Events(await client.PostAsJsonAsync("/api/v1/assistant/prompt", new { text = "find thunderfury", history = Array.Empty<object>() }));

        Assert.Equal(["step", "reply", "usage", "done"], events.Select(Type));
        Assert.Equal("search_items", events[0].GetProperty("tool").GetString());
        Assert.Equal("Found Thunderfury.", events[1].GetProperty("text").GetString());
        Assert.Equal(120, events[2].GetProperty("inputTokens").GetInt64());
        // The real tool ran against the catalog, and the model saw its result.
        var result = model.Seen[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        Assert.Contains("Thunderfury", result.Result?.ToString());
        // The server instructions lead the conversation.
        Assert.Equal(ChatRole.System, model.Seen[0][0].Role);
        Assert.Contains("Altrobe", model.Seen[0][0].Text);
    }

    [Fact]
    public async Task EarlierExchangesFromTheTabComeBeforeTheNewPrompt()
    {
        var model = new ScriptedModel(_ => Reply("Done."));
        using var app = new TestApp { AssistantModels = model };
        var client = await Ready(app);

        await Events(await client.PostAsJsonAsync("/api/v1/assistant/prompt", new
        {
            text = "now make it red",
            history = new[] { new { role = "user", text = "show the orc" }, new { role = "assistant", text = "Showing the Orc." } },
        }));

        Assert.Equal([ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.User], model.Seen[0].Select(m => m.Role));
        Assert.Equal(["show the orc", "Showing the Orc.", "now make it red"], model.Seen[0].Skip(1).Select(m => m.Text));
    }

    [Fact]
    public async Task AProviderErrorBecomesAnErrorLine()
    {
        var model = new ScriptedModel(_ => throw new InvalidOperationException("invalid x-api-key"));
        using var app = new TestApp { AssistantModels = model };
        var client = await Ready(app);

        var events = await Events(await client.PostAsJsonAsync("/api/v1/assistant/prompt", new { text = "hi" }));

        var error = events.Single(e => Type(e) == "error");
        Assert.Contains("invalid x-api-key", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AModelThatKeepsCallingToolsIsStopped()
    {
        var model = new ScriptedModel(_ => Call("list_characters", []));
        using var app = new TestApp { AssistantModels = model };
        var client = await Ready(app);

        var events = await Events(await client.PostAsJsonAsync("/api/v1/assistant/prompt", new { text = "loop forever" }));

        Assert.InRange(model.Seen.Count, 1, AssistantRunner.MaxRounds + 1);
        Assert.Equal("error", Type(events[^1]));
        Assert.Contains($"{AssistantRunner.MaxRounds}", events[^1].GetProperty("message").GetString());
    }

    [Fact]
    public async Task WithoutSettingsThePromptIsRefusedBeforeStreaming()
    {
        using var app = new TestApp { AssistantModels = new ScriptedModel(_ => Reply("unused")) };
        var client = await app.Installed();

        var r = await client.PostAsJsonAsync("/api/v1/assistant/prompt", new { text = "hi" });

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("assistant_not_ready", JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task TheTestButtonSendsOneShortRequest()
    {
        var model = new ScriptedModel(_ => Reply("OK"));
        using var app = new TestApp { AssistantModels = model };
        var client = await Ready(app);

        var r = await client.PostAsync("/api/v1/assistant/test", null);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.True(JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("ok").GetBoolean());
        Assert.Single(model.Seen);
    }
}
