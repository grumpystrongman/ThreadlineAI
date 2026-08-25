using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Threadline.Core;

namespace Threadline.Windows.Services;

public sealed class WindowsDeviceAgentPipeHost : IAsyncDisposable
{
    public const string PipeName = "Threadline.DeviceAgent.v1";

    private readonly WindowsDeviceAgent _agent;
    private readonly WindowsDeviceScreenObserver _screenObserver;
    private readonly WindowsUiAutomationInspector _uiaInspector;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _acceptLoop;

    public WindowsDeviceAgentPipeHost(
        WindowsDeviceAgent? agent = null,
        WindowsDeviceScreenObserver? screenObserver = null,
        WindowsUiAutomationInspector? uiaInspector = null)
    {
        _agent = agent ?? new WindowsDeviceAgent();
        _screenObserver = screenObserver ?? new WindowsDeviceScreenObserver();
        _uiaInspector = uiaInspector ?? new WindowsUiAutomationInspector();
    }

    public void Start()
    {
        if (_acceptLoop is not null) return;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_shutdown.Token));
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; }
            catch (OperationCanceledException) { }
        }
        _shutdown.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            await pipe.WaitForConnectionAsync(cancellationToken);
            await HandleClientAsync(pipe, cancellationToken);
        }
    }

    private async Task HandleClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 8192, leaveOpen: true) { AutoFlush = true };

        while (!cancellationToken.IsCancellationRequested && stream.CanRead)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) return;

            DevicePipeResponse response;
            try
            {
                var request = JsonSerializer.Deserialize<DevicePipeRequest>(line, JsonOptions)
                    ?? throw new InvalidOperationException("Device request was empty.");
                response = await DispatchAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
            {
                response = new DevicePipeResponse(false, null, ex.Message);
            }

            await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
        }
    }

    private async Task<DevicePipeResponse> DispatchAsync(DevicePipeRequest request, CancellationToken cancellationToken)
    {
        switch (request.Method?.Trim().ToLowerInvariant())
        {
            case "ping":
                return new DevicePipeResponse(true, new { ready = true, pipe = PipeName, interactive = Environment.UserInteractive }, null);

            case "capabilities":
                return new DevicePipeResponse(true, _agent.GetCapabilities(), null);

            case "observe":
                return new DevicePipeResponse(true, _agent.ObserveDesktop(), null);

            case "capture":
            {
                var result = await _screenObserver.CaptureAsync(request.Target, cancellationToken);
                return new DevicePipeResponse(result.Success, result, result.Error);
            }

            case "inspect":
            {
                var result = _uiaInspector.Inspect(request.Target);
                return new DevicePipeResponse(result.Success, result, result.Error);
            }

            case "authority/get":
                return new DevicePipeResponse(true, _agent.AuthorityGrant, null);

            case "authority/set":
            {
                var grant = request.Authority ?? throw new ArgumentException("authority is required.");
                _agent.SetAuthorityGrant(grant);
                return new DevicePipeResponse(true, _agent.AuthorityGrant, null);
            }

            case "execute":
            {
                var command = request.Command ?? throw new ArgumentException("command is required.");
                var result = await _agent.ExecuteAsync(command, request.Confirmed, cancellationToken);
                return new DevicePipeResponse(true, result, null);
            }

            default:
                throw new ArgumentException($"Unsupported device method '{request.Method}'.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private sealed record DevicePipeRequest(
        string? Method,
        DeviceCommand? Command = null,
        DeviceTarget? Target = null,
        bool Confirmed = false,
        DeviceAuthorityGrant? Authority = null);

    private sealed record DevicePipeResponse(bool Ok, object? Result, string? Error);
}
