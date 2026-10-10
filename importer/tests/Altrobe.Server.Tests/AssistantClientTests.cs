using Microsoft.Extensions.AI;

namespace Altrobe.Server.Tests;

// Builds model clients from the saved settings. Checked through the client's own metadata, so no
// request leaves the machine.
public class AssistantClientTests
{
    static ChatClientMetadata Meta(IChatClient c) => c.GetService<ChatClientMetadata>()!;

    [Fact]
    public void AnthropicUsesTheSavedModel()
    {
        using var client = AssistantClients.Create(new AssistantSettings("anthropic", null, "claude-haiku-4-5", null), "sk-ant-test");
        Assert.Equal("claude-haiku-4-5", Meta(client).DefaultModelId);
    }

    [Fact]
    public void OpenAiCompatibleServersUseTheirBaseUrl()
    {
        using var client = AssistantClients.Create(new AssistantSettings("openai-compatible", "http://127.0.0.1:11434/v1", "qwen3", null), null);
        Assert.Equal("qwen3", Meta(client).DefaultModelId);
        Assert.Equal(new Uri("http://127.0.0.1:11434/v1"), Meta(client).ProviderUri);
    }

    [Theory]
    [InlineData(null, null, null, null, "Choose a model provider")]
    [InlineData("anthropic", null, "claude-haiku-4-5", null, "Add your Anthropic API key")]
    public void MissingSettingsSayWhatToDo(string? provider, string? baseUrl, string? model, string? key, string message)
    {
        var e = Assert.Throws<AssistantNotReadyException>(() => AssistantClients.Create(new AssistantSettings(provider, baseUrl, model, null), key));
        Assert.StartsWith(message, e.Message);
    }
}

public class AssistantErrorTests
{
    [Fact]
    public void AnUnreachableServerSaysWhereAltrobeLookedAndWhatToCheck()
    {
        var refused = new HttpRequestException("Connection refused (127.0.0.1:11434)", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused));
        var wrapped = new AggregateException("Retry failed after 4 tries.", refused);
        var settings = new AssistantSettings("openai-compatible", "http://127.0.0.1:11434/v1", "qwen3", null);

        Assert.Equal("Couldn't reach http://127.0.0.1:11434/v1. Is the model server (such as Ollama or LM Studio) running?", AssistantErrors.Explain(wrapped, settings));
    }

    [Fact]
    public void OtherErrorsKeepTheProvidersWords()
    {
        var settings = new AssistantSettings("anthropic", null, "claude-haiku-4-5", null);
        Assert.Equal("The model provider answered with an error: invalid x-api-key", AssistantErrors.Explain(new InvalidOperationException("invalid x-api-key"), settings));
    }
}
