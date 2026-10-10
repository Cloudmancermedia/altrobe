using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Altrobe.Server.Tests;

public class AssistantSettingsTests
{
    static async Task<JsonElement> Body(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    const string Key = "sk-ant-api03-abcdefghijklmnop1234";

    [Fact]
    public async Task NothingIsConfiguredAtFirst()
    {
        using var app = new TestApp();
        var s = await Body(await app.Local().GetAsync("/api/v1/assistant/settings"));
        Assert.Equal(JsonValueKind.Null, s.GetProperty("provider").ValueKind);
        Assert.False(s.GetProperty("hasKey").GetBoolean());
    }

    [Fact]
    public async Task SavedSettingsComeBackWithTheKeyMaskedAndTheFileReadableOnlyByTheUser()
    {
        using var app = new TestApp();
        var client = app.Local();
        var put = await client.PutAsJsonAsync("/api/v1/assistant/settings", new { provider = "anthropic", model = "claude-haiku-4-5", apiKey = Key });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.DoesNotContain(Key, await put.Content.ReadAsStringAsync());

        var raw = await (await client.GetAsync("/api/v1/assistant/settings")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(Key, raw);
        var s = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("anthropic", s.GetProperty("provider").GetString());
        Assert.Equal("claude-haiku-4-5", s.GetProperty("model").GetString());
        Assert.True(s.GetProperty("hasKey").GetBoolean());
        Assert.Equal("sk-…1234", s.GetProperty("maskedKey").GetString());

        var file = Path.Combine(app.ConfigRoot, "settings.json");
        Assert.Contains(Key, await File.ReadAllTextAsync(file));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
    }

    [Fact]
    public async Task AnEmptyKeyKeepsTheStoredOne()
    {
        using var app = new TestApp();
        var client = app.Local();
        await client.PutAsJsonAsync("/api/v1/assistant/settings", new { provider = "anthropic", model = "claude-haiku-4-5", apiKey = Key });
        await client.PutAsJsonAsync("/api/v1/assistant/settings", new { provider = "anthropic", model = "claude-sonnet-5-5", apiKey = "" });

        var s = await Body(await client.GetAsync("/api/v1/assistant/settings"));
        Assert.Equal("claude-sonnet-5-5", s.GetProperty("model").GetString());
        Assert.Equal("sk-…1234", s.GetProperty("maskedKey").GetString());
    }

    [Fact]
    public async Task LocalServersNeedABaseUrlButNoKey()
    {
        using var app = new TestApp();
        var client = app.Local();
        var put = await client.PutAsJsonAsync("/api/v1/assistant/settings", new { provider = "openai-compatible", baseUrl = "http://127.0.0.1:11434/v1", model = "qwen3" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var s = await Body(await client.GetAsync("/api/v1/assistant/settings"));
        Assert.Equal("http://127.0.0.1:11434/v1", s.GetProperty("baseUrl").GetString());
        Assert.False(s.GetProperty("hasKey").GetBoolean());
    }

    [Fact]
    public async Task ClaudeCodeNeedsNoKeyOrAddress()
    {
        using var app = new TestApp();
        var client = app.Local();
        var put = await client.PutAsJsonAsync("/api/v1/assistant/settings", new { provider = "claude-code", baseUrl = "http://ignored.example", model = "haiku" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var s = await Body(await client.GetAsync("/api/v1/assistant/settings"));
        Assert.Equal("claude-code", s.GetProperty("provider").GetString());
        Assert.Equal(JsonValueKind.Null, s.GetProperty("baseUrl").ValueKind);
        Assert.False(s.GetProperty("hasKey").GetBoolean());
    }

    [Theory]
    [InlineData("{\"provider\":\"gemini\",\"model\":\"x\"}")]
    [InlineData("{\"provider\":\"openai-compatible\",\"model\":\"x\"}")]
    [InlineData("{\"provider\":\"openai-compatible\",\"baseUrl\":\"file:///etc/passwd\",\"model\":\"x\"}")]
    [InlineData("{\"provider\":\"anthropic\",\"model\":\"\"}")]
    public async Task BadSettingsAreRefusedAndNothingIsSaved(string body)
    {
        using var app = new TestApp();
        var put = await app.Local().PutAsync("/api/v1/assistant/settings", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.False(File.Exists(Path.Combine(app.ConfigRoot, "settings.json")));
    }
}
