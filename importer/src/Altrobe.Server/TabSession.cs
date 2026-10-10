using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Altrobe.Server;

// The link to the open viewer tab. The tab owns the look, so look commands from MCP are sent to it
// over this WebSocket and it answers with the result. The newest tab to connect wins.
//
// Server -> tab: { "type": "command", "id": "7", "command": "equip_item", "args": { ... } }
// Tab -> server: { "type": "result", "id": "7", "ok": true, "result": { ... } }
//                { "type": "result", "id": "7", "ok": false, "error": "unknown slot \"ring\"" }
public sealed class TabSession(ServerSettings settings, ILogger<TabSession> log)
{
    // Longer than the tab waits for drawing (15 s in session.ts), so a slow first view still answers.
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    // Close code for a tab replaced by a newer one. The web app does not reconnect after it.
    public const WebSocketCloseStatus Replaced = (WebSocketCloseStatus)4001;

    readonly ConcurrentDictionary<string, TaskCompletionSource<JsonObject>> _pending = new();
    readonly SemaphoreSlim _send = new(1, 1);
    WebSocket? _tab;
    long _nextId;

    public bool Connected => _tab is { State: WebSocketState.Open };

    public sealed class TabException(string message) : Exception(message);

    // Serves one tab until it disconnects.
    public async Task RunAsync(WebSocket ws, CancellationToken ct)
    {
        var previous = Interlocked.Exchange(ref _tab, ws);
        if (previous != null)
        {
            // Commands sent to the old tab will not be answered; fail them now rather than at the timeout.
            FailPending("Another Altrobe tab took over before this one answered. Try again.");
            if (previous.State == WebSocketState.Open)
                _ = previous.CloseAsync(Replaced, "another Altrobe tab took over", CancellationToken.None);
        }
        log.LogInformation("Viewer tab connected");
        var buffer = new byte[64 * 1024];
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(buffer, ct);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    message.Write(buffer, 0, r.Count);
                } while (!r.EndOfMessage);
                Receive(Encoding.UTF8.GetString(message.ToArray()));
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException)
        {
            // The tab closed or reloaded.
        }
        finally
        {
            if (Interlocked.CompareExchange(ref _tab, null, ws) == ws)
            {
                log.LogInformation("Viewer tab disconnected");
                FailPending("The Altrobe tab closed before it answered.");
            }
        }
    }

    void FailPending(string message)
    {
        foreach (var p in _pending.Values) p.TrySetException(new TabException(message));
    }

    void Receive(string text)
    {
        JsonObject? msg;
        try { msg = JsonNode.Parse(text) as JsonObject; }
        catch (System.Text.Json.JsonException) { return; }
        if (msg?["type"]?.GetValue<string>() != "result" || msg["id"]?.GetValue<string>() is not { } id) return;
        if (_pending.TryRemove(id, out var p)) p.TrySetResult(msg);
    }

    // Sends a command to the tab and returns its result, or throws TabException with a message for
    // the model: no tab, a timeout, or the command's own error.
    public async Task<JsonNode?> CallAsync(string command, JsonObject args, CancellationToken ct)
    {
        var ws = _tab;
        if (ws is not { State: WebSocketState.Open })
            throw new TabException($"No Altrobe tab is open. Open Altrobe in the browser (http://127.0.0.1:{settings.Port}/) and try again.");
        var id = Interlocked.Increment(ref _nextId).ToString();
        var pending = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = pending;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(new JsonObject { ["type"] = "command", ["id"] = id, ["command"] = command, ["args"] = args }.ToJsonString());
            await _send.WaitAsync(ct);
            try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct); }
            catch (Exception e) when (e is WebSocketException or ObjectDisposedException)
            {
                // The tab closed between the check above and the send.
                throw new TabException($"No Altrobe tab is open. Open Altrobe in the browser (http://127.0.0.1:{settings.Port}/) and try again.");
            }
            finally { _send.Release(); }

            JsonObject reply;
            try { reply = await pending.Task.WaitAsync(CommandTimeout, ct); }
            catch (TimeoutException) { throw new TabException($"The Altrobe tab did not answer {command} within {CommandTimeout.TotalSeconds:0} seconds."); }
            if (reply["ok"]?.GetValue<bool>() != true) throw new TabException(reply["error"]?.GetValue<string>() ?? $"{command} failed.");
            return reply["result"]?.DeepClone();
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }
}
