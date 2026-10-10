using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Altrobe.Server.Tests;

// The prompt box's Claude Code provider: Altrobe runs the player's own `claude -p` and reads its stream.
public class ClaudeCodeTests
{
    // Prints a set stream, one JSON object per line, and records how it was called.
    sealed class FakeCli(params string[] lines) : IClaudeCodeCli
    {
        public List<(IReadOnlyList<string> Args, string Stdin)> Calls { get; } = [];
        public Exception? Fails { get; init; }

        public async IAsyncEnumerable<string> RunAsync(IReadOnlyList<string> args, string stdin, [EnumeratorCancellation] CancellationToken ct)
        {
            Calls.Add((args, stdin));
            if (Fails is { } e) throw e;
            foreach (var line in lines)
            {
                ct.ThrowIfCancellationRequested();
                yield return line;
            }
            await Task.CompletedTask;
        }
    }

    static int _ids;
    static string ToolUse(string tool, object input) => JsonSerializer.Serialize(new
    {
        type = "assistant",
        message = new { content = new object[] { new { type = "tool_use", id = $"toolu_{Interlocked.Increment(ref _ids)}", name = $"mcp__altrobe__{tool}", input } } },
    });
    static string Text(string text) => JsonSerializer.Serialize(new { type = "assistant", message = new { content = new object[] { new { type = "text", text } } } });
    static string Result(string text, bool error = false) => JsonSerializer.Serialize(new
    {
        type = "result",
        subtype = error ? "error_during_execution" : "success",
        is_error = error,
        result = text,
        usage = new { input_tokens = 100, cache_creation_input_tokens = 20, cache_read_input_tokens = 30, output_tokens = 15 },
    });
    const string Init = """{"type":"system","subtype":"init","tools":[],"mcp_servers":[{"name":"altrobe","status":"connected"}]}""";

    static async Task<HttpClient> Ready(TestApp app)
    {
        var client = await app.Installed();
        var put = await client.PutAsJsonAsync("/api/v1/assistant/settings", new { provider = "claude-code", model = "haiku" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        return client;
    }

    static async Task<List<JsonElement>> Events(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var text = await r.Content.ReadAsStringAsync();
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement).ToList();
    }

    static string Type(JsonElement e) => e.GetProperty("type").GetString()!;

    [Fact]
    public async Task ToolCallsStreamAsStepsThenTheReplyAndPlanUsage()
    {
        var cli = new FakeCli(Init, Text("Let me look."), ToolUse("search_items", new { query = "Thunderfury" }), Result("Equipped Thunderfury."));
        using var app = new TestApp { ClaudeCode = cli };
        var client = await Ready(app);

        var events = await Events(await client.PostAsJsonAsync("/api/v1/assistant/prompt", new { text = "find thunderfury" }));

        Assert.Equal(["step", "reply", "usage", "done"], events.Select(Type));
        Assert.Equal("search_items", events[0].GetProperty("tool").GetString());
        Assert.Equal("Thunderfury", events[0].GetProperty("arguments").GetProperty("query").GetString());
        // The reply is the final result, not the text the model wrote between tool calls.
        Assert.Equal("Equipped Thunderfury.", events[1].GetProperty("text").GetString());
        Assert.Equal(150, events[2].GetProperty("inputTokens").GetInt64());
        Assert.Equal(15, events[2].GetProperty("outputTokens").GetInt64());
        Assert.True(events[2].GetProperty("plan").GetBoolean());
    }

    [Fact]
    public async Task TheModelGetsOnlyAltrobesToolsAndNoSavedSession()
    {
        var cli = new FakeCli(Result("Done."));
        using var app = new TestApp { ClaudeCode = cli };
        var client = await Ready(app);

        await Events(await client.PostAsJsonAsync("/api/v1/assistant/prompt", new { text = "show the orc" }));

        var args = cli.Calls.Single().Args;
        string After(string flag) => args[args.ToList().IndexOf(flag) + 1];
        Assert.Equal("-p", args[0]);
        Assert.Equal("", After("--tools"));
        Assert.Equal("mcp__altrobe", After("--allowedTools"));
        Assert.Equal("dontAsk", After("--permission-mode"));
        Assert.Equal("stream-json", After("--output-format"));
        Assert.Equal("haiku", After("--model"));
        Assert.Contains("--strict-mcp-config", args);
        Assert.Contains("--no-session-persistence", args);
        var server = JsonDocument.Parse(After("--mcp-config")).RootElement.GetProperty("mcpServers").GetProperty("altrobe");
        Assert.Equal("http", server.GetProperty("type").GetString());
        Assert.EndsWith("/mcp", server.GetProperty("url").GetString());
        Assert.Contains("Altrobe", After("--system-prompt"));
    }

    [Fact]
    public async Task EarlierExchangesGoIntoThePromptBeforeTheNewOne()
    {
        var cli = new FakeCli(Result("Done."));
        using var app = new TestApp { ClaudeCode = cli };
        var client = await Ready(app);

        await Events(await client.PostAsJsonAsync("/api/v1/assistant/prompt", new
        {
            text = "now make it red",
            history = new[] { new { role = "user", text = "show the orc" }, new { role = "assistant", text = "Showing the Orc." } },
        }));

        var stdin = cli.Calls.Single().Stdin;
        Assert.True(stdin.IndexOf("show the orc") < stdin.IndexOf("Showing the Orc.") && stdin.IndexOf("Showing the Orc.") < stdin.IndexOf("now make it red"));
        Assert.DoesNotContain("now make it red", cli.Calls.Single().Args);
    }

    [Fact]
    public async Task AModelThatKeepsCallingToolsIsStopped()
    {
        var lines = Enumerable.Range(0, AssistantRunner.MaxRounds + 5).Select(_ => ToolUse("list_characters", new { })).Append(Result("never"));
        var cli = new FakeCli([.. lines]);
        using var app = new TestApp { ClaudeCode = cli };
        var client = await Ready(app);

        var events = await Events(await client.PostAsJsonAsync("/api/v1/assistant/prompt", new { text = "loop forever" }));

        Assert.Equal(AssistantRunner.MaxRounds, events.Count(e => Type(e) == "step"));
        Assert.Equal("error", Type(events[^1]));
        Assert.Contains($"{AssistantRunner.MaxRounds}", events[^1].GetProperty("message").GetString());
    }

    [Fact]
    public async Task NotSignedInSaysHowToSignIn()
    {
        var cli = new FakeCli(Result("Not logged in · Please run /login", error: true));
        using var app = new TestApp { ClaudeCode = cli };
        var client = await Ready(app);

        var events = await Events(await client.PostAsJsonAsync("/api/v1/assistant/prompt", new { text = "hi" }));

        var error = events.Single(e => Type(e) == "error").GetProperty("message").GetString();
        Assert.Contains("sign in", error);
    }

    [Fact]
    public async Task MissingClaudeCodeSaysHowToInstallIt()
    {
        // The real launcher, pointed at a program that doesn't exist.
        using var app = new TestApp { ClaudeCode = new ProcessClaudeCodeCli("altrobe-no-such-claude") };
        var client = await Ready(app);

        var events = await Events(await client.PostAsJsonAsync("/api/v1/assistant/prompt", new { text = "hi" }));

        var error = events.Single(e => Type(e) == "error").GetProperty("message").GetString();
        Assert.Contains("isn't installed", error);
    }

    [Fact]
    public async Task TheTestButtonRunsOneShortPromptWithNoTools()
    {
        var cli = new FakeCli(Result("OK"));
        using var app = new TestApp { ClaudeCode = cli };
        var client = await Ready(app);

        var r = await client.PostAsync("/api/v1/assistant/test", null);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.Equal("OK", body.GetProperty("reply").GetString());
        Assert.DoesNotContain("--mcp-config", cli.Calls.Single().Args);
    }
}
