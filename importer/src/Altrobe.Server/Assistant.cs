using System.ClientModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using OpenAI;

namespace Altrobe.Server;

// The prompt box's model settings: which provider, which model, and the player's own API key.
public sealed record AssistantSettings(string? Provider, string? BaseUrl, string? Model, string? ApiKey)
{
    public static readonly AssistantSettings Empty = new(null, null, null, null);
}

// settings.json in the config folder, under "assistant". The file holds an API key, so it is
// created readable only by its owner (on Windows the user profile folder already limits access).
public sealed class AssistantSettingsStore(ServerSettings server)
{
    public const string KeyVariable = "ALTROBE_LLM_KEY";
    public const string Anthropic = "anthropic";
    public const string OpenAiCompatible = "openai-compatible";
    public const string ClaudeCode = "claude-code";

    readonly Lock _lock = new();

    string FilePath => Path.Combine(server.ConfigRoot, "settings.json");

    public AssistantSettings Load()
    {
        lock (_lock)
        {
            if (!File.Exists(FilePath)) return AssistantSettings.Empty;
            var node = JsonNode.Parse(File.ReadAllText(FilePath))?["assistant"];
            return node?.Deserialize<AssistantSettings>(Json) ?? AssistantSettings.Empty;
        }
    }

    // ALTROBE_LLM_KEY wins over the stored key, for people who keep keys in their environment.
    public static string? EffectiveKey(AssistantSettings s) =>
        Environment.GetEnvironmentVariable(KeyVariable) is { Length: > 0 } env ? env : s.ApiKey;

    public void Save(AssistantSettings settings)
    {
        lock (_lock)
        {
            JsonObject root = File.Exists(FilePath) ? JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? [] : [];
            root["assistant"] = JsonSerializer.SerializeToNode(settings, Json);
            Directory.CreateDirectory(server.ConfigRoot);
            // Write owner-only from the first byte, then swap it in, so the key is never readable by others.
            var temp = $"{FilePath}.{Guid.NewGuid():N}.tmp";
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            try
            {
                using (var f = new FileStream(temp, options))
                {
                    JsonSerializer.Serialize(f, root, Json);
                    f.Flush(flushToDisk: true);
                }
                File.Move(temp, FilePath, overwrite: true);
            }
            catch
            {
                File.Delete(temp);
                throw;
            }
        }
    }

    public static string Mask(string key) => key.Length > 8 ? $"{key[..3]}…{key[^4..]}" : "…";

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}

public static class AssistantApi
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/assistant/settings", (AssistantSettingsStore store) => Results.Json(View(store.Load())));

        api.MapPut("/assistant/settings", async (HttpRequest req, AssistantSettingsStore store) =>
        {
            // Requiring JSON also means a cross-site form post cannot change the settings.
            if (!req.HasJsonContentType()) return ApiErrors.Result(StatusCodes.Status415UnsupportedMediaType, "unsupported_media_type", "Send the body as application/json.");
            string? provider, baseUrl, model, apiKey;
            try
            {
                using var doc = await JsonDocument.ParseAsync(req.Body);
                string? Str(string name) => doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;
                (provider, baseUrl, model, apiKey) = (Str("provider"), Str("baseUrl"), Str("model"), Str("apiKey"));
            }
            catch (JsonException e)
            {
                return ApiErrors.BadRequest($"Body is not valid JSON: {e.Message}");
            }
            if (provider is not (AssistantSettingsStore.Anthropic or AssistantSettingsStore.OpenAiCompatible or AssistantSettingsStore.ClaudeCode))
                return ApiErrors.BadRequest($"\"provider\" must be \"{AssistantSettingsStore.Anthropic}\", \"{AssistantSettingsStore.OpenAiCompatible}\" or \"{AssistantSettingsStore.ClaudeCode}\".");
            if (string.IsNullOrEmpty(model)) return ApiErrors.BadRequest("\"model\" is required.");
            if (provider == AssistantSettingsStore.OpenAiCompatible && !IsHttpUrl(baseUrl))
                return ApiErrors.BadRequest("\"baseUrl\" must be an http or https address, for example http://127.0.0.1:11434/v1 for Ollama.");
            if (provider != AssistantSettingsStore.OpenAiCompatible) baseUrl = null;

            // An empty key keeps the stored one, so the settings form never has to show it.
            var saved = store.Load();
            var next = new AssistantSettings(provider, baseUrl, model, string.IsNullOrEmpty(apiKey) ? saved.ApiKey : apiKey);
            store.Save(next);
            return Results.Json(View(next));
        });
    }

    static bool IsHttpUrl(string? s) =>
        Uri.TryCreate(s, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    // Never returns the key itself.
    static object View(AssistantSettings s)
    {
        var key = AssistantSettingsStore.EffectiveKey(s);
        return new
        {
            provider = s.Provider,
            baseUrl = s.BaseUrl,
            model = s.Model,
            hasKey = !string.IsNullOrEmpty(key),
            maskedKey = string.IsNullOrEmpty(key) ? null : AssistantSettingsStore.Mask(key),
            keyFromEnvironment = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(AssistantSettingsStore.KeyVariable)),
        };
    }
}

// Thrown when the prompt box can't run yet; the message tells the player what to set.
public sealed class AssistantNotReadyException(string message) : Exception(message);

// Builds the model client for the saved settings. The tool loop (UseFunctionInvocation) is added by
// the caller, so tests can swap the model for a fake.
public static class AssistantClients
{
    // Throws when the settings can't run a prompt yet, whichever client is used.
    public static void EnsureReady(AssistantSettings s, string? key)
    {
        if (s.Provider is null || string.IsNullOrEmpty(s.Model))
            throw new AssistantNotReadyException("Choose a model provider and model in the prompt box settings.");
        if (s.Provider == AssistantSettingsStore.Anthropic && string.IsNullOrEmpty(key))
            throw new AssistantNotReadyException("Add your Anthropic API key in the prompt box settings.");
        if (s.Provider == AssistantSettingsStore.OpenAiCompatible && !Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out _))
            throw new AssistantNotReadyException("Set the server address in the prompt box settings.");
    }

    public static IChatClient Create(AssistantSettings s, string? key)
    {
        EnsureReady(s, key);
        switch (s.Provider)
        {
            case AssistantSettingsStore.Anthropic:
                return new AnthropicClient { ApiKey = key }.AsIChatClient(s.Model!);
            case AssistantSettingsStore.OpenAiCompatible:
                var endpoint = new Uri(s.BaseUrl!);
                // Local servers such as Ollama and LM Studio take any key; the client library needs a non-empty one.
                var credential = new ApiKeyCredential(string.IsNullOrEmpty(key) ? "none" : key);
                return new OpenAI.Chat.ChatClient(s.Model!, credential, new OpenAIClientOptions { Endpoint = endpoint }).AsIChatClient();
            default:
                throw new AssistantNotReadyException($"Unknown model provider \"{s.Provider}\".");
        }
    }
}

// Where model clients come from; tests swap in a scripted model.
public interface IAssistantModels
{
    IChatClient Create(AssistantSettings settings, string? key);
}

public sealed class SdkAssistantModels : IAssistantModels
{
    public IChatClient Create(AssistantSettings settings, string? key) => AssistantClients.Create(settings, key);
}

public sealed record AssistantTurn(string Role, string Text);

// One prompt: the model calls the same tools the MCP server offers, in a loop, and each step is
// reported as it happens. Events are written as they occur, one JSON object per line.
public static class AssistantRunner
{
    public const int MaxRounds = 12;
    public const int MaxHistory = 10;
    public const int MaxTextLength = 2000;
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    public static readonly string SystemPrompt = McpTools.Instructions + "\n\nYou are the prompt box inside Altrobe. Act with the tools, then reply in one or two plain sentences saying what changed.";

    public static IList<AITool> Tools(McpTools tools) =>
        [.. typeof(McpTools).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => (method: m, tool: m.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(x => x.tool?.Name is not null)
            .Select(x => AIFunctionFactory.Create(x.method, tools, new AIFunctionFactoryOptions { Name = x.tool!.Name }))];

    public static List<ChatMessage> Messages(string text, IEnumerable<AssistantTurn> history)
    {
        List<ChatMessage> messages = [new(ChatRole.System, SystemPrompt)];
        foreach (var turn in history.TakeLast(MaxHistory))
            messages.Add(new(turn.Role == "assistant" ? ChatRole.Assistant : ChatRole.User, Clip(turn.Text)));
        messages.Add(new(ChatRole.User, Clip(text)));
        return messages;
    }

    static string Clip(string s) => s.Length > MaxTextLength ? s[..MaxTextLength] : s;

    public static async Task RunAsync(IChatClient model, McpTools tools, AssistantSettings settings, string text, IReadOnlyList<AssistantTurn> history, Func<object, Task> emit, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        using var client = new FunctionInvokingChatClient(model) { MaximumIterationsPerRequest = MaxRounds, IncludeDetailedErrors = true };
        var reply = new StringBuilder();
        long input = 0, output = 0;
        var unanswered = new HashSet<string>();
        try
        {
            await foreach (var update in client.GetStreamingResponseAsync(Messages(text, history), new ChatOptions { Tools = Tools(tools) }, timeout.Token))
            {
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case FunctionCallContent call:
                            unanswered.Add(call.CallId);
                            await emit(new { type = "step", tool = call.Name, arguments = call.Arguments });
                            break;
                        case FunctionResultContent result:
                            unanswered.Remove(result.CallId);
                            break;
                        case UsageContent usage:
                            input += usage.Details.InputTokenCount ?? 0;
                            output += usage.Details.OutputTokenCount ?? 0;
                            break;
                        case TextContent t:
                            reply.Append(t.Text);
                            break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await emit(new { type = "error", message = $"The model took longer than {Timeout.TotalSeconds:0} seconds, so Altrobe stopped it." });
            return;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await emit(new { type = "error", message = AssistantErrors.Explain(e, settings) });
            return;
        }
        if (unanswered.Count > 0)
        {
            await emit(new { type = "error", message = $"The model was still calling tools after {MaxRounds} rounds, so Altrobe stopped it." });
            return;
        }
        if (reply.Length > 0) await emit(new { type = "reply", text = reply.ToString().Trim() });
        await emit(new { type = "usage", inputTokens = input, outputTokens = output });
        await emit(new { type = "done" });
    }
}

public static class AssistantPromptApi
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/assistant/prompt", async (HttpContext ctx, AssistantSettingsStore store, IAssistantModels models, IClaudeCodeCli claudeCode) =>
        {
            if (!ctx.Request.HasJsonContentType()) return ApiErrors.Result(StatusCodes.Status415UnsupportedMediaType, "unsupported_media_type", "Send the body as application/json.");
            string? text;
            List<AssistantTurn> history = [];
            try
            {
                using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
                text = doc.RootElement.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()?.Trim() : null;
                if (doc.RootElement.TryGetProperty("history", out var h) && h.ValueKind == JsonValueKind.Array)
                    foreach (var turn in h.EnumerateArray())
                        if (turn.TryGetProperty("role", out var r) && turn.TryGetProperty("text", out var x) && x.ValueKind == JsonValueKind.String)
                            history.Add(new(r.GetString() ?? "user", x.GetString()!));
            }
            catch (JsonException e)
            {
                return ApiErrors.BadRequest($"Body is not valid JSON: {e.Message}");
            }
            if (string.IsNullOrEmpty(text)) return ApiErrors.BadRequest("\"text\" is required.");

            var settings = store.Load();
            if (settings.Provider == AssistantSettingsStore.ClaudeCode && !string.IsNullOrEmpty(settings.Model))
            {
                ctx.Response.ContentType = "application/x-ndjson";
                // Claude Code reaches the tools through this server's own MCP endpoint.
                var mcpUrl = $"http://127.0.0.1:{ctx.Connection.LocalPort}/mcp";
                await ClaudeCodeRunner.RunAsync(claudeCode, settings, mcpUrl, text, history, e => Write(ctx, e), ctx.RequestAborted);
                return Results.Empty;
            }
            IChatClient model;
            try
            {
                var key = AssistantSettingsStore.EffectiveKey(settings);
                AssistantClients.EnsureReady(settings, key);
                model = models.Create(settings, key);
            }
            catch (AssistantNotReadyException e)
            {
                return ApiErrors.Result(StatusCodes.Status409Conflict, "assistant_not_ready", e.Message);
            }
            using (model)
            {
                var tools = ActivatorUtilities.CreateInstance<McpTools>(ctx.RequestServices);
                ctx.Response.ContentType = "application/x-ndjson";
                await AssistantRunner.RunAsync(model, tools, settings, text, history, e => Write(ctx, e), ctx.RequestAborted);
            }
            return Results.Empty;
        });

        // One short request, so the settings dialog can say whether the key and model work.
        api.MapPost("/assistant/test", async (HttpContext ctx, AssistantSettingsStore store, IAssistantModels models, IClaudeCodeCli claudeCode) =>
        {
            var settings = store.Load();
            if (settings.Provider == AssistantSettingsStore.ClaudeCode)
            {
                try
                {
                    return Results.Json(new { ok = true, model = settings.Model, reply = await ClaudeCodeRunner.TestAsync(claudeCode, settings, ctx.RequestAborted) });
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    return Results.Json(new { ok = false, model = settings.Model, message = ClaudeCodeRunner.Explain(e) }, statusCode: StatusCodes.Status502BadGateway);
                }
            }
            try
            {
                var key = AssistantSettingsStore.EffectiveKey(settings);
                AssistantClients.EnsureReady(settings, key);
                using var model = models.Create(settings, key);
                var r = await model.GetResponseAsync("Reply with the word OK.", new ChatOptions { MaxOutputTokens = 16 }, ctx.RequestAborted);
                return Results.Json(new { ok = true, model = settings.Model, reply = r.Text });
            }
            catch (AssistantNotReadyException e)
            {
                return ApiErrors.Result(StatusCodes.Status409Conflict, "assistant_not_ready", e.Message);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return Results.Json(new { ok = false, model = settings.Model, message = AssistantErrors.Explain(e, settings) }, statusCode: StatusCodes.Status502BadGateway);
            }
        });
    }

    static async Task Write(HttpContext ctx, object e)
    {
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(e, Json) + "\n", ctx.RequestAborted);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
    }

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

// Turns a failed model request into a sentence the player can act on.
public static class AssistantErrors
{
    public static string Explain(Exception e, AssistantSettings settings)
    {
        if (Causes(e).Any(x => x is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused }))
            return settings.Provider == AssistantSettingsStore.OpenAiCompatible
                ? $"Couldn't reach {settings.BaseUrl}. Is the model server (such as Ollama or LM Studio) running?"
                : "Couldn't reach the model provider. Check your internet connection.";
        return $"The model provider answered with an error: {e.Message}";
    }

    static IEnumerable<Exception> Causes(Exception e)
    {
        var stack = new Stack<Exception>([e]);
        while (stack.TryPop(out var x))
        {
            yield return x;
            if (x is AggregateException agg) foreach (var inner in agg.InnerExceptions) stack.Push(inner);
            else if (x.InnerException is { } inner) stack.Push(inner);
        }
    }
}
