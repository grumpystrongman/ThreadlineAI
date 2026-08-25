using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Threadline.Core;

const string pipeName = "Threadline.DeviceAgent.v1";
var server = new DeviceMcpServer(pipeName);
await server.RunAsync();

internal sealed class DeviceMcpServer
{
    private readonly string _pipeName;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public DeviceMcpServer(string pipeName)
    {
        _pipeName = pipeName;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        string? line;
        while ((line = await Console.In.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonNode? request;
            try
            {
                request = JsonNode.Parse(line);
            }
            catch (JsonException ex)
            {
                await WriteErrorAsync(null, -32700, $"Parse error: {ex.Message}");
                continue;
            }

            if (request is not JsonObject obj) continue;
            var id = obj["id"]?.DeepClone();
            var method = obj["method"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(method))
            {
                if (id is not null) await WriteErrorAsync(id, -32600, "Invalid Request");
                continue;
            }

            try
            {
                switch (method)
                {
                    case "initialize":
                        await HandleInitializeAsync(id, obj["params"] as JsonObject);
                        break;
                    case "notifications/initialized":
                    case "notifications/cancelled":
                        break;
                    case "ping":
                        if (id is not null) await WriteResultAsync(id, new JsonObject());
                        break;
                    case "tools/list":
                        if (id is not null) await WriteResultAsync(id, BuildToolList());
                        break;
                    case "tools/call":
                        if (id is not null) await HandleToolCallAsync(id, obj["params"] as JsonObject, cancellationToken);
                        break;
                    default:
                        if (id is not null) await WriteErrorAsync(id, -32601, $"Method not found: {method}");
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or ArgumentException or JsonException)
            {
                if (id is not null)
                {
                    await WriteResultAsync(id, ToolResult($"Device tool failed: {ex.Message}", isError: true));
                }
            }
        }
    }

    private async Task HandleInitializeAsync(JsonNode? id, JsonObject? parameters)
    {
        if (id is null) return;
        var requestedVersion = parameters?["protocolVersion"]?.GetValue<string>();
        var protocolVersion = string.IsNullOrWhiteSpace(requestedVersion) ? "2025-06-18" : requestedVersion;
        var result = new JsonObject
        {
            ["protocolVersion"] = protocolVersion,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "threadline-windows-device",
                ["version"] = "0.1.0"
            },
            ["instructions"] = "Use native Windows/app capabilities first. Treat coordinate mouse input as a fallback. Each mutating tool returns verification state; do not claim completion unless status is completed and verification is satisfied."
        };
        await WriteResultAsync(id, result);
    }

    private async Task HandleToolCallAsync(JsonNode id, JsonObject? parameters, CancellationToken cancellationToken)
    {
        var name = parameters?["name"]?.GetValue<string>() ?? throw new ArgumentException("Tool name is required.");
        var arguments = parameters?["arguments"] as JsonObject ?? new JsonObject();
        var response = name switch
        {
            "observe_desktop" => await CallDeviceAsync(new { method = "observe" }, cancellationToken),
            "list_device_capabilities" => await CallDeviceAsync(new { method = "capabilities" }, cancellationToken),
            "get_owner_authority" => await CallDeviceAsync(new { method = "authority/get" }, cancellationToken),
            "open_application" => await ExecuteAsync(BuildLaunchCommand(arguments), cancellationToken),
            "focus_window" => await ExecuteAsync(BuildFocusCommand(arguments), cancellationToken),
            "invoke_control" => await ExecuteAsync(BuildInvokeCommand(arguments), cancellationToken),
            "set_control_text" => await ExecuteAsync(BuildSetTextCommand(arguments), cancellationToken),
            "send_keys" => await ExecuteAsync(BuildKeyboardCommand(arguments), cancellationToken),
            "click_at" => await ExecuteAsync(BuildMouseCommand(arguments), cancellationToken),
            _ => throw new ArgumentException($"Unknown device tool '{name}'.")
        };

        var isError = response["ok"]?.GetValue<bool>() == false;
        await WriteResultAsync(id, ToolResult(response.ToJsonString(_jsonOptions), isError));
    }

    private async Task<JsonObject> ExecuteAsync(DeviceCommand command, CancellationToken cancellationToken)
    {
        return await CallDeviceAsync(new { method = "execute", command, confirmed = false }, cancellationToken);
    }

    private DeviceCommand BuildLaunchCommand(JsonObject args)
    {
        var application = RequiredString(args, "application");
        var expectedWindow = OptionalString(args, "expected_window_title");
        return new DeviceCommand(
            NewId("launch"),
            DeviceOperationKind.LaunchApplication,
            new DeviceTarget(Application: application),
            ExpectedState: new DeviceExpectedState(
                string.IsNullOrWhiteSpace(expectedWindow) ? DeviceVerificationKind.ProcessRunning : DeviceVerificationKind.WindowExists,
                expectedWindow,
                12000),
            Rationale: "Launch the requested application for the owner's task.");
    }

    private DeviceCommand BuildFocusCommand(JsonObject args)
    {
        var target = BuildTarget(args);
        return new DeviceCommand(
            NewId("focus"),
            DeviceOperationKind.FocusWindow,
            target,
            ExpectedState: new DeviceExpectedState(DeviceVerificationKind.ForegroundWindowMatches, TimeoutMilliseconds: 5000),
            Rationale: "Bring the requested application window to the foreground before interacting with it.");
    }

    private DeviceCommand BuildInvokeCommand(JsonObject args)
    {
        var target = BuildTarget(args, requireControl: true);
        var expectedText = OptionalString(args, "expected_text");
        return new DeviceCommand(
            NewId("invoke"),
            DeviceOperationKind.InvokeControl,
            target,
            ExpectedState: string.IsNullOrWhiteSpace(expectedText)
                ? DeviceExpectedState.None
                : new DeviceExpectedState(DeviceVerificationKind.AccessibleTextContains, expectedText, 8000),
            Rationale: "Invoke a native UI Automation control instead of clicking coordinates.");
    }

    private DeviceCommand BuildSetTextCommand(JsonObject args)
    {
        var target = BuildTarget(args, requireControl: true);
        var text = RequiredString(args, "text");
        var expectedText = OptionalString(args, "expected_text") ?? text;
        return new DeviceCommand(
            NewId("set-text"),
            DeviceOperationKind.SetText,
            target,
            new Dictionary<string, string> { ["text"] = text },
            new DeviceExpectedState(DeviceVerificationKind.AccessibleTextContains, expectedText, 8000),
            Rationale: "Set a native UI value and verify the resulting accessible state.");
    }

    private DeviceCommand BuildKeyboardCommand(JsonObject args)
    {
        var target = BuildTarget(args, allowEmpty: true);
        var hotkey = OptionalString(args, "hotkey");
        var text = OptionalString(args, "text");
        if (string.IsNullOrWhiteSpace(hotkey) == string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Provide exactly one of 'hotkey' or 'text'.");
        }
        var arguments = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(hotkey)) arguments["hotkey"] = hotkey!;
        if (!string.IsNullOrWhiteSpace(text)) arguments["text"] = text!;
        var expectedText = OptionalString(args, "expected_text");
        return new DeviceCommand(
            NewId("keys"),
            DeviceOperationKind.KeyboardInput,
            target,
            arguments,
            string.IsNullOrWhiteSpace(expectedText)
                ? DeviceExpectedState.None
                : new DeviceExpectedState(DeviceVerificationKind.AccessibleTextContains, expectedText, 8000),
            Rationale: "Use keyboard input only when a stronger app-specific or UI Automation capability is unavailable.");
    }

    private DeviceCommand BuildMouseCommand(JsonObject args)
    {
        var target = BuildTarget(args, allowEmpty: true);
        var x = RequiredInt(args, "x");
        var y = RequiredInt(args, "y");
        var button = OptionalString(args, "button") ?? "left";
        var expectedText = OptionalString(args, "expected_text");
        return new DeviceCommand(
            NewId("mouse"),
            DeviceOperationKind.MouseInput,
            target,
            new Dictionary<string, string>
            {
                ["x"] = x.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["y"] = y.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["button"] = button
            },
            string.IsNullOrWhiteSpace(expectedText)
                ? DeviceExpectedState.None
                : new DeviceExpectedState(DeviceVerificationKind.AccessibleTextContains, expectedText, 8000),
            Rationale: "Coordinate mouse input is a last-resort fallback and should be followed by state verification.");
    }

    private static DeviceTarget BuildTarget(JsonObject args, bool requireControl = false, bool allowEmpty = false)
    {
        var application = OptionalString(args, "application");
        var windowTitle = OptionalString(args, "window_title");
        var automationId = OptionalString(args, "automation_id");
        var controlName = OptionalString(args, "control_name");
        var processId = OptionalInt(args, "process_id");

        if (!allowEmpty && string.IsNullOrWhiteSpace(application) && string.IsNullOrWhiteSpace(windowTitle) && processId is null)
        {
            throw new ArgumentException("Target requires application, window_title, or process_id.");
        }
        if (requireControl && string.IsNullOrWhiteSpace(automationId) && string.IsNullOrWhiteSpace(controlName))
        {
            throw new ArgumentException("Native control action requires automation_id or control_name.");
        }

        return new DeviceTarget(
            Application: application,
            WindowTitle: windowTitle,
            AutomationId: automationId,
            ControlName: controlName,
            ProcessId: processId);
    }

    private async Task<JsonObject> CallDeviceAsync(object request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(18));
        await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(2500, timeout.Token);
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException("AIKA/JARVIS Windows Device Host is not running. Launch the Threadline Windows app first.", ex);
        }

        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 8192, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 8192, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, _jsonOptions));
        var line = await reader.ReadLineAsync(timeout.Token);
        if (line is null) throw new IOException("Windows Device Host closed the pipe without a response.");
        return JsonNode.Parse(line) as JsonObject ?? throw new JsonException("Windows Device Host returned invalid JSON.");
    }

    private JsonObject BuildToolList()
    {
        var tools = new JsonArray
        {
            Tool("observe_desktop", "Observe the current Windows desktop, foreground window, visible top-level windows, and accessible text. Use this before acting and after unexpected state changes.", Schema()),
            Tool("list_device_capabilities", "List device-control capabilities available from the interactive Windows host.", Schema()),
            Tool("get_owner_authority", "Read the active owner authority grant that controls which routine device operations are pre-authorized.", Schema()),
            Tool("open_application", "Open an installed Windows application and verify that its process/window appears.", Schema(
                Prop("application", "string", "Application name or executable path."),
                Prop("expected_window_title", "string", "Optional window-title text to verify after launch."),
                required: new[] { "application" })),
            Tool("focus_window", "Bring a matching application window to the foreground and verify focus.", TargetSchema()),
            Tool("invoke_control", "Invoke a Windows UI Automation control by AutomationId or accessible control name. Prefer this over coordinate clicks.", ControlSchema(includeText: false)),
            Tool("set_control_text", "Set text through the native Windows UI Automation Value pattern and verify accessible state.", ControlSchema(includeText: true)),
            Tool("send_keys", "Fallback keyboard input for the focused target. Prefer app-specific/native UI Automation tools first. Supply exactly one of hotkey or text.", KeyboardSchema()),
            Tool("click_at", "Last-resort coordinate click. Use only when stronger native/app-specific controls are unavailable and verify resulting state when possible.", MouseSchema())
        };
        return new JsonObject { ["tools"] = tools };
    }

    private static JsonObject Tool(string name, string description, JsonObject inputSchema) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = inputSchema
    };

    private static JsonObject Schema(params JsonObject[] properties) => Schema(properties, Array.Empty<string>());

    private static JsonObject Schema(JsonObject[] properties, string[] required)
    {
        var props = new JsonObject();
        foreach (var property in properties)
        {
            var name = property["_name"]!.GetValue<string>();
            property.Remove("_name");
            props[name] = property;
        }
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = new JsonArray(required.Select(name => JsonValue.Create(name)).ToArray()),
            ["additionalProperties"] = false
        };
    }

    private static JsonObject Schema(JsonObject p1, JsonObject p2, string[] required) => Schema(new[] { p1, p2 }, required);

    private static JsonObject TargetSchema()
    {
        return Schema(
            new[]
            {
                Prop("application", "string", "Application/process name."),
                Prop("window_title", "string", "Substring of the top-level window title."),
                Prop("process_id", "integer", "Windows process id.")
            },
            Array.Empty<string>());
    }

    private static JsonObject ControlSchema(bool includeText)
    {
        var props = new List<JsonObject>
        {
            Prop("application", "string", "Application/process name."),
            Prop("window_title", "string", "Substring of the top-level window title."),
            Prop("process_id", "integer", "Windows process id."),
            Prop("automation_id", "string", "Preferred Windows UI Automation AutomationId."),
            Prop("control_name", "string", "Accessible control name when AutomationId is unavailable."),
            Prop("expected_text", "string", "Accessible text expected after the action.")
        };
        var required = new List<string>();
        if (includeText)
        {
            props.Add(Prop("text", "string", "Text to set through the native Value pattern."));
            required.Add("text");
        }
        return Schema(props.ToArray(), required.ToArray());
    }

    private static JsonObject KeyboardSchema()
    {
        return Schema(
            new[]
            {
                Prop("application", "string", "Optional application/process target to focus first."),
                Prop("window_title", "string", "Optional window-title target to focus first."),
                Prop("process_id", "integer", "Optional process id target to focus first."),
                Prop("hotkey", "string", "Hotkey such as ctrl+s or alt+f4."),
                Prop("text", "string", "Unicode text to type."),
                Prop("expected_text", "string", "Accessible text expected after input.")
            },
            Array.Empty<string>());
    }

    private static JsonObject MouseSchema()
    {
        return Schema(
            new[]
            {
                Prop("application", "string", "Optional application/process target to focus first."),
                Prop("window_title", "string", "Optional window-title target to focus first."),
                Prop("process_id", "integer", "Optional process id target to focus first."),
                Prop("x", "integer", "Absolute screen X coordinate."),
                Prop("y", "integer", "Absolute screen Y coordinate."),
                Prop("button", "string", "Mouse button: left or right."),
                Prop("expected_text", "string", "Accessible text expected after the click.")
            },
            new[] { "x", "y" });
    }

    private static JsonObject Prop(string name, string type, string description) => new()
    {
        ["_name"] = name,
        ["type"] = type,
        ["description"] = description
    };

    private static JsonObject ToolResult(string text, bool isError) => new()
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = text
            }
        },
        ["isError"] = isError
    };

    private async Task WriteResultAsync(JsonNode id, JsonNode result)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["result"] = result
        };
        await Console.Out.WriteLineAsync(response.ToJsonString(_jsonOptions));
        await Console.Out.FlushAsync();
    }

    private async Task WriteErrorAsync(JsonNode? id, int code, string message)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
        await Console.Out.WriteLineAsync(response.ToJsonString(_jsonOptions));
        await Console.Out.FlushAsync();
    }

    private static string RequiredString(JsonObject args, string name) =>
        OptionalString(args, name) is { Length: > 0 } value
            ? value
            : throw new ArgumentException($"'{name}' is required.");

    private static string? OptionalString(JsonObject args, string name) =>
        args[name] is null ? null : args[name]!.GetValue<string>().Trim();

    private static int RequiredInt(JsonObject args, string name) =>
        OptionalInt(args, name) ?? throw new ArgumentException($"'{name}' is required.");

    private static int? OptionalInt(JsonObject args, string name) =>
        args[name] is null ? null : args[name]!.GetValue<int>();

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
