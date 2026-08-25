using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Threadline.Service;

public sealed record BrowserAutomationRequest(string Action, Dictionary<string, JsonElement>? Arguments = null, int TimeoutSeconds = 45);
public sealed record BrowserAutomationResult(string CommandId, bool Success, JsonElement? Result, string? Error, DateTimeOffset CompletedAt);
public sealed record BrowserAutomationStatus(bool Connected, DateTimeOffset? ConnectedAt, string? ExtensionVersion, string? CurrentUrl, string? CurrentTitle);

/// <summary>
/// Local, authenticated command bridge to the existing Chrome/Edge extension.
/// A normal HTTP request authenticated with the Threadline local token creates a short-lived
/// one-time WebSocket ticket; the long-lived browser extension never puts the service token in a URL.
/// </summary>
public sealed class BrowserAutomationHub
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _tickets = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BrowserAutomationResult>> _pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private WebSocket? _socket;
    private DateTimeOffset? _connectedAt;
    private string? _extensionVersion;
    private string? _currentUrl;
    private string? _currentTitle;

    public string CreateTicket()
    {
        CleanupTickets();
        var ticket = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _tickets[ticket] = DateTimeOffset.UtcNow.AddSeconds(30);
        return ticket;
    }

    public BrowserAutomationStatus GetStatus() => new(
        _socket?.State == WebSocketState.Open,
        _connectedAt,
        _extensionVersion,
        _currentUrl,
        _currentTitle);

    public async Task AcceptAsync(WebSocket socket, string ticket, CancellationToken cancellationToken)
    {
        if (!_tickets.TryRemove(ticket, out var expires) || expires < DateTimeOffset.UtcNow)
        {
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Invalid or expired ticket", cancellationToken);
            return;
        }

        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            var previous = _socket;
            _socket = socket;
            _connectedAt = DateTimeOffset.UtcNow;
            if (previous is not null && previous.State == WebSocketState.Open)
            {
                try { await previous.CloseAsync(WebSocketCloseStatus.NormalClosure, "Replaced by a newer browser agent connection", cancellationToken); }
                catch (WebSocketException) { }
            }
        }
        finally { _connectionGate.Release(); }

        try
        {
            await ReceiveLoopAsync(socket, cancellationToken);
        }
        finally
        {
            await _connectionGate.WaitAsync(CancellationToken.None);
            try
            {
                if (ReferenceEquals(_socket, socket))
                {
                    _socket = null;
                    _connectedAt = null;
                    FailPending("Browser extension disconnected before completing the command.");
                }
            }
            finally { _connectionGate.Release(); }
        }
    }

    public async Task<BrowserAutomationResult> ExecuteAsync(BrowserAutomationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Action)) throw new ArgumentException("Browser action is required.");
        var socket = _socket;
        if (socket is null || socket.State != WebSocketState.Open)
        {
            return new BrowserAutomationResult(string.Empty, false, null, "Browser agent is not connected. Open Chrome/Edge with the Threadline extension enabled.", DateTimeOffset.UtcNow);
        }

        var commandId = $"browser-{Guid.NewGuid():N}";
        var completion = new TaskCompletionSource<BrowserAutomationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(commandId, completion)) throw new InvalidOperationException("Could not register browser command.");

        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                type = "command",
                commandId,
                action = request.Action.Trim().ToLowerInvariant(),
                arguments = request.Arguments ?? new Dictionary<string, JsonElement>()
            });

            await _sendGate.WaitAsync(cancellationToken);
            try
            {
                await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
            }
            finally { _sendGate.Release(); }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 1, 120)));
            try { return await completion.Task.WaitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new BrowserAutomationResult(commandId, false, null, "Browser command timed out.", DateTimeOffset.UtcNow);
            }
        }
        finally { _pending.TryRemove(commandId, out _); }
    }

    private async Task ReceiveLoopAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            using var stream = new MemoryStream();
            WebSocketReceiveResult receive;
            do
            {
                receive = await socket.ReceiveAsync(buffer, cancellationToken);
                if (receive.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Browser agent closed", CancellationToken.None);
                    return;
                }
                stream.Write(buffer, 0, receive.Count);
                if (stream.Length > 512 * 1024) throw new InvalidOperationException("Browser agent message exceeded 512 KB.");
            } while (!receive.EndOfMessage);

            if (receive.MessageType != WebSocketMessageType.Text) continue;
            using var document = JsonDocument.Parse(stream.ToArray());
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            if (string.Equals(type, "hello", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "state", StringComparison.OrdinalIgnoreCase))
            {
                _extensionVersion = ReadString(root, "extensionVersion") ?? _extensionVersion;
                _currentUrl = ReadString(root, "url") ?? _currentUrl;
                _currentTitle = ReadString(root, "title") ?? _currentTitle;
                continue;
            }
            if (!string.Equals(type, "result", StringComparison.OrdinalIgnoreCase)) continue;

            var commandId = ReadString(root, "commandId");
            if (string.IsNullOrWhiteSpace(commandId) || !_pending.TryGetValue(commandId, out var pending)) continue;
            var success = root.TryGetProperty("success", out var successElement) && successElement.ValueKind == JsonValueKind.True;
            JsonElement? result = root.TryGetProperty("result", out var resultElement) ? resultElement.Clone() : null;
            var error = ReadString(root, "error");
            pending.TrySetResult(new BrowserAutomationResult(commandId, success, result, error, DateTimeOffset.UtcNow));
        }
    }

    private void FailPending(string error)
    {
        foreach (var pair in _pending)
        {
            pair.Value.TrySetResult(new BrowserAutomationResult(pair.Key, false, null, error, DateTimeOffset.UtcNow));
        }
    }

    private void CleanupTickets()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _tickets)
            if (pair.Value < now) _tickets.TryRemove(pair.Key, out _);
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

public static class BrowserAutomationEndpointMappings
{
    public static void MapThreadlineBrowserAutomation(this WebApplication app)
    {
        var secured = app.MapGroup("/v1/browser-agent")
            .RequireThreadlineLocalAccess();

        secured.MapPost("/socket-ticket", (BrowserAutomationHub hub, HttpContext context) =>
        {
            var ticket = hub.CreateTicket();
            var scheme = context.Request.IsHttps ? "wss" : "ws";
            return Results.Ok(new { ticket, websocketUrl = $"{scheme}://{context.Request.Host}/v1/browser-agent/ws?ticket={Uri.EscapeDataString(ticket)}", expiresInSeconds = 30 });
        });

        secured.MapGet("/status", (BrowserAutomationHub hub) => Results.Ok(hub.GetStatus()));

        secured.MapPost("/execute", async (BrowserAutomationRequest request, BrowserAutomationHub hub, CancellationToken ct) =>
        {
            var result = await hub.ExecuteAsync(request, ct);
            return result.Success ? Results.Ok(result) : Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        // The WebSocket itself is authorized by the one-time, 30-second ticket minted only
        // through the secured local-token endpoint above. Keeping the service token out of the
        // WebSocket URL avoids leaking it through browser/network diagnostics.
        app.MapGet("/v1/browser-agent/ws", async (HttpContext context, BrowserAutomationHub hub, CancellationToken ct) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            var ticket = context.Request.Query["ticket"].ToString();
            if (string.IsNullOrWhiteSpace(ticket))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await hub.AcceptAsync(socket, ticket, ct);
        });
    }
}
