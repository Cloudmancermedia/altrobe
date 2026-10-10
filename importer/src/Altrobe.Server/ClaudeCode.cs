using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Altrobe.Server;

// Runs the player's own Claude Code. Tests swap in a fake that prints a set stream.
public interface IClaudeCodeCli
{
    // Runs claude with these arguments and the prompt on stdin, yielding each line it prints.
    IAsyncEnumerable<string> RunAsync(IReadOnlyList<string> args, string stdin, CancellationToken ct);
}

// Thrown when the claude program can't be started at all.
public sealed class ClaudeCodeMissingException(Exception inner) : Exception("Claude Code isn't installed.", inner);

public sealed class ProcessClaudeCodeCli(string program = "claude") : IClaudeCodeCli
{
    public async IAsyncEnumerable<string> RunAsync(IReadOnlyList<string> args, string stdin, [EnumeratorCancellation] CancellationToken ct)
    {
        var info = new ProcessStartInfo(program)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (Win32Exception e) { throw new ClaudeCodeMissingException(e); }

        // Stopping a prompt, or a limit, ends Claude Code and anything it started.
        using var kill = ct.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close();

        while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
            if (line.Length > 0) yield return line;
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            var err = (await stderr).Trim();
            throw new InvalidOperationException(err.Length > 0 ? err : $"Claude Code exited with code {process.ExitCode}.");
        }
    }
}

// One prompt through `claude -p`. Claude Code runs the tool loop itself against Altrobe's MCP
// endpoint; this reads its stream-json output and reports the same events as AssistantRunner.
public static class ClaudeCodeRunner
{
    const string ToolPrefix = "mcp__altrobe__";
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    // Built-in tools are off (--tools ""), only Altrobe's MCP tools are allowed, and anything else is
    // denied rather than asked about (dontAsk). The player's own settings and MCP servers are skipped.
    public static List<string> Args(AssistantSettings settings, string? mcpUrl, string systemPrompt)
    {
        List<string> args = ["-p", "--output-format", "stream-json", "--verbose", "--tools", "", "--permission-mode", "dontAsk",
            "--no-session-persistence", "--setting-sources", "", "--strict-mcp-config", "--system-prompt", systemPrompt];
        if (mcpUrl is not null)
        {
            var config = JsonSerializer.Serialize(new { mcpServers = new { altrobe = new { type = "http", url = mcpUrl } } });
            args.AddRange(["--mcp-config", config, "--allowedTools", "mcp__altrobe"]);
        }
        if (!string.IsNullOrEmpty(settings.Model)) args.AddRange(["--model", settings.Model]);
        return args;
    }

    // Claude Code keeps no conversation here, so earlier turns are written into the prompt.
    public static string Prompt(string text, IEnumerable<AssistantTurn> history)
    {
        var turns = history.TakeLast(AssistantRunner.MaxHistory).ToList();
        if (turns.Count == 0) return Clip(text);
        var sb = new StringBuilder("Earlier in this conversation:\n");
        foreach (var t in turns) sb.Append(t.Role == "assistant" ? "You: " : "User: ").Append(Clip(t.Text)).Append('\n');
        return sb.Append("\nThe user now says: ").Append(Clip(text)).ToString();
    }

    static string Clip(string s) => s.Length > AssistantRunner.MaxTextLength ? s[..AssistantRunner.MaxTextLength] : s;

    public static async Task RunAsync(IClaudeCodeCli cli, AssistantSettings settings, string mcpUrl, string text, IReadOnlyList<AssistantTurn> history, Func<object, Task> emit, CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(Timeout);
        var steps = 0;
        try
        {
            await foreach (var line in cli.RunAsync(Args(settings, mcpUrl, AssistantRunner.SystemPrompt), Prompt(text, history), stop.Token))
            {
                using var doc = Parse(line);
                if (doc is null) continue;
                var e = doc.RootElement;
                switch (Str(e, "type"))
                {
                    case "assistant":
                        foreach (var block in ToolUses(e))
                        {
                            if (++steps > AssistantRunner.MaxRounds)
                            {
                                await stop.CancelAsync();
                                await emit(new { type = "error", message = $"The model was still calling tools after {AssistantRunner.MaxRounds} rounds, so Altrobe stopped it." });
                                return;
                            }
                            var name = Str(block, "name") ?? "";
                            await emit(new { type = "step", tool = name.StartsWith(ToolPrefix) ? name[ToolPrefix.Length..] : name, arguments = block.TryGetProperty("input", out var input) ? input.Clone() : default(JsonElement?) });
                        }
                        break;
                    case "result":
                        if (e.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True || Str(e, "subtype") is { } sub && sub != "success")
                        {
                            await emit(new { type = "error", message = Explain(Str(e, "result") ?? Str(e, "subtype") ?? "unknown error") });
                            return;
                        }
                        if (Str(e, "result") is { Length: > 0 } reply) await emit(new { type = "reply", text = reply.Trim() });
                        var (inTokens, outTokens) = Usage(e);
                        await emit(new { type = "usage", inputTokens = inTokens, outputTokens = outTokens, plan = true });
                        await emit(new { type = "done" });
                        return;
                }
            }
            await emit(new { type = "error", message = "Claude Code stopped without a reply." });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await emit(new { type = "error", message = $"The model took longer than {Timeout.TotalSeconds:0} seconds, so Altrobe stopped it." });
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await emit(new { type = "error", message = Explain(e) });
        }
    }

    // The settings dialog's check: one short prompt with no tools.
    public static async Task<string> TestAsync(IClaudeCodeCli cli, AssistantSettings settings, CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(Timeout);
        await foreach (var line in cli.RunAsync(Args(settings, null, "Reply briefly."), "Reply with the word OK.", stop.Token))
        {
            using var doc = Parse(line);
            if (doc is null || Str(doc.RootElement, "type") != "result") continue;
            var e = doc.RootElement;
            var result = Str(e, "result") ?? "";
            if (e.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True) throw new InvalidOperationException(result);
            return result.Trim();
        }
        throw new InvalidOperationException("Claude Code stopped without a reply.");
    }

    public static string Explain(Exception e) => e switch
    {
        ClaudeCodeMissingException => "Claude Code isn't installed, or isn't on this computer's PATH. Install it from https://claude.com/claude-code, then try again.",
        _ => Explain(e.Message),
    };

    static string Explain(string message) =>
        message.Contains("login", StringComparison.OrdinalIgnoreCase) || message.Contains("logged in", StringComparison.OrdinalIgnoreCase)
            ? "Claude Code isn't signed in. Run claude in a terminal and sign in, then try again."
            : $"Claude Code answered with an error: {message}";

    static IEnumerable<JsonElement> ToolUses(JsonElement e) =>
        e.TryGetProperty("message", out var m) && m.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Array
            ? c.EnumerateArray().Where(b => Str(b, "type") == "tool_use").ToList()
            : [];

    // Cached input counts toward the plan the same as fresh input.
    static (long Input, long Output) Usage(JsonElement e)
    {
        if (!e.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) return (0, 0);
        long N(string name) => u.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
        return (N("input_tokens") + N("cache_creation_input_tokens") + N("cache_read_input_tokens"), N("output_tokens"));
    }

    static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static JsonDocument? Parse(string line)
    {
        try { return JsonDocument.Parse(line); }
        catch (JsonException) { return null; }
    }
}
